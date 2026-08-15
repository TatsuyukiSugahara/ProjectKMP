using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Core
{
    /// <summary>
    /// 操作の読み取り口。
    ///
    /// これまではキーの種類ごとに if を並べ、画面のボタンも別に見ていた。
    /// 同じ操作の割り当てが何か所にも散り、変えるたびに全部を直す必要があった。
    ///
    /// 割り当ては ProjectKMP.inputactions に集める。
    /// 読む側はここを通すだけでよく、どの機器で押されたかを気にしなくてよい。
    ///
    /// 画面のボタンも同じ仕組みへ流し込むので、扱いは同じになる。
    /// </summary>
    public static class GameInput
    {
        // ---- 定数 ----------------------------------------

        /// <summary>割り当て表の置き場。Resources から読むので、どこからでも使える</summary>
        private const string ASSET_PATH = "ProjectKMP";

        /// <summary>トリガーを引いたと見なす深さ。軽く触れただけでは反応させない</summary>
        private const float TRIGGER_THRESHOLD = 0.5f;

        // ---- 内部状態 ------------------------------------

        private static InputActionAsset _asset;

        private static InputAction _move;
        private static InputAction _look;
        private static InputAction _attack;
        private static InputAction _beam;
        private static InputAction _energyBall;
        private static InputAction _dive;
        private static InputAction _target;
        private static InputAction _sprint;

        private static InputAction _navigate;
        private static InputAction _submit;
        private static InputAction _cancel;

        /// <summary>画面のボタンから受け取った状態。1フレームぶんをまとめて持つ</summary>
        private struct TouchState
        {
            public Vector2 Move;
            public Vector2 Look;
            public bool Attack;
            public bool Beam;
            public bool EnergyBall;
            public bool Dive;
            public bool Target;
        }

        private static TouchState _touchNow;
        private static TouchState _touchPrev;

        // ---- 公開API: 画面からの押し込み -----------------

        /// <summary>
        /// 画面のボタンの状態を渡す。UI 側から毎フレーム呼ぶ。
        ///
        /// 読む側が画面のボタンを直接見に行くと、遊びの処理が画面の作りに縛られる。
        /// 押し込む形にすれば、読む側はどこから来た入力かを知らなくてよい。
        /// </summary>
        public static void PushTouch(
            Vector2 move, Vector2 look, bool attack, bool beam, bool energyBall, bool dive, bool target)
        {
            // 押した瞬間を見分けるため、前のフレームの状態を残しておく
            _touchPrev = _touchNow;

            _touchNow.Move = move;
            _touchNow.Look = look;
            _touchNow.Attack = attack;
            _touchNow.Beam = beam;
            _touchNow.EnergyBall = energyBall;
            _touchNow.Dive = dive;
            _touchNow.Target = target;
        }

        /// <summary>画面のボタンが1つも無い場面へ移ったときに、押しっぱなしの誤検出を防ぐ</summary>
        public static void ClearTouch()
        {
            _touchPrev = default;
            _touchNow = default;
        }

        /// <summary>画面をなぞった量。カメラを回すのに使う</summary>
        public static Vector2 TouchLookDelta => _touchNow.Look;

        // ---- 公開API: ゲーム操作 -------------------------

        public static Vector2 Move
        {
            get
            {
                Vector2 value = Ensure() ? _move.ReadValue<Vector2>() : Vector2.zero;

                // 機器と画面のどちらからでも動かせるよう、大きいほうを採る
                return value.sqrMagnitude >= _touchNow.Move.sqrMagnitude ? value : _touchNow.Move;
            }
        }

        public static Vector2 Look => Ensure() ? _look.ReadValue<Vector2>() : Vector2.zero;

        public static bool AttackHeld => (Ensure() && _attack.IsPressed()) || _touchNow.Attack;
        public static bool AttackPressed =>
            (Ensure() && _attack.WasPressedThisFrame()) || (_touchNow.Attack && !_touchPrev.Attack);

        public static bool BeamHeld => (Ensure() && _beam.IsPressed()) || _touchNow.Beam;
        public static bool BeamPressed =>
            (Ensure() && _beam.WasPressedThisFrame()) || (_touchNow.Beam && !_touchPrev.Beam);

        public static bool DiveHeld => (Ensure() && _dive.IsPressed()) || _touchNow.Dive;
        public static bool DivePressed =>
            (Ensure() && _dive.WasPressedThisFrame()) || (_touchNow.Dive && !_touchPrev.Dive);

        public static bool TargetPressed =>
            (Ensure() && _target.WasPressedThisFrame()) || (_touchNow.Target && !_touchPrev.Target);

        public static bool SprintHeld => Ensure() && _sprint.IsPressed();

        /// <summary>
        /// 必殺技。パッドは LT と RT の同時引きで出す。
        ///
        /// 2つのトリガーを同時に引く操作は割り当て表で表しづらいので、ここで直に見る。
        /// 片方だけでは出さない。溜め技なので、偶然発動しないようにしている。
        /// </summary>
        public static bool EnergyBallHeld
        {
            get
            {
                if (_touchNow.EnergyBall) return true;
                if (Ensure() && _energyBall.IsPressed()) return true;

                Gamepad gamepad = Gamepad.current;
                if (gamepad == null) return false;

                return gamepad.leftTrigger.ReadValue() >= TRIGGER_THRESHOLD
                    && gamepad.rightTrigger.ReadValue() >= TRIGGER_THRESHOLD;
            }
        }

        // ---- 公開API: メニュー操作 -----------------------

        public static Vector2 Navigate => Ensure() ? _navigate.ReadValue<Vector2>() : Vector2.zero;

        public static bool SubmitPressed => Ensure() && _submit.WasPressedThisFrame();
        public static bool CancelPressed => Ensure() && _cancel.WasPressedThisFrame();

        // ---- 公開API: 出し入れ ---------------------------

        /// <summary>ゲーム操作の受け付けを切り替える。演出中に動かれると困る場面で使う</summary>
        public static void SetPlayerEnabled(bool enabled)
        {
            if (!Ensure()) return;

            InputActionMap map = _asset.FindActionMap("Player", false);
            if (map == null) return;

            if (enabled) map.Enable();
            else map.Disable();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>割り当て表を読み込む。読めなければ false を返し、呼ぶ側は初期値で動く</summary>
        private static bool Ensure()
        {
            if (_asset != null) return true;

            _asset = Resources.Load<InputActionAsset>(ASSET_PATH);

            if (_asset == null)
            {
                Debug.LogError("[入力] 割り当て表が見つかりません: Resources/" + ASSET_PATH);
                return false;
            }

            InputActionMap player = _asset.FindActionMap("Player", true);
            InputActionMap ui = _asset.FindActionMap("UI", true);

            _move = player.FindAction("Move", true);
            _look = player.FindAction("Look", true);
            _attack = player.FindAction("Attack", true);
            _beam = player.FindAction("Beam", true);
            _energyBall = player.FindAction("EnergyBall", true);
            _dive = player.FindAction("Dive", true);
            _target = player.FindAction("Target", true);
            _sprint = player.FindAction("Sprint", true);

            _navigate = ui.FindAction("Navigate", true);
            _submit = ui.FindAction("Submit", true);
            _cancel = ui.FindAction("Cancel", true);

            // 読み込んだ時点で受け付けを始める。
            // 使う側が有効化を忘れると『押しても何も起きない』になり、原因が分かりにくい
            player.Enable();
            ui.Enable();

            return true;
        }
    }
}
