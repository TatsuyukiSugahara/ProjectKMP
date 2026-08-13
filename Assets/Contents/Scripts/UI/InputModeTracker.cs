using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ProjectKMP.UI
{
    /// <summary>操作に使っている機器の種類</summary>
    public enum InputMode
    {
        /// <summary>画面を指で触る(スマホ・タブレット)</summary>
        Touch,

        /// <summary>キーボードとマウス。押せるUIはマウスで押す</summary>
        KeyboardMouse,

        /// <summary>ゲームパッド</summary>
        Gamepad,
    }

    /// <summary>
    /// いまどの機器で遊んでいるかを見張り、変わったら知らせる。
    ///
    /// 展示ではパッドを挿したPCを置くので、キーボードを触る人とパッドを握る人が混ざる。
    /// 端末の種類で決め打ちにすると、パッドを持っているのにキー表示が出続けてしまうため、
    /// 『最後に触った機器』で切り替える。
    ///
    /// 判定は入力があったときだけ動かす。何も触っていない間は前のままにして、
    /// 表示がちらつかないようにしている。
    /// </summary>
    public class InputModeTracker : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>スティックやマウスの微動で切り替わらないための遊び</summary>
        private const float STICK_DEAD_ZONE = 0.3f;
        private const float MOUSE_MOVE_THRESHOLD = 2.0f;

        // ---- 内部状態 ------------------------------------

        private static InputModeTracker _instance;
        private static InputMode _current = InputMode.KeyboardMouse;

        /// <summary>確認用に固定している機器。していなければ null</summary>
        private static InputMode? _forcedMode;

        // ---- 公開API -------------------------------------

        /// <summary>いまの操作機器</summary>
        public static InputMode Current => _current;

        /// <summary>操作機器が変わったときに呼ばれる</summary>
        public static event Action<InputMode> Changed;

        /// <summary>
        /// 機器を手で固定する。実機が無いときに、指で触ったときの見た目を
        /// エディタで確かめるために使う。null を渡すと自動判定へ戻る。
        /// </summary>
        public static void Force(InputMode? mode)
        {
            _forcedMode = mode;
            if (mode == null) return;

            if (_current == mode.Value) return;

            _current = mode.Value;
            Changed?.Invoke(_current);
        }

        /// <summary>見張りを始める。すでにあれば何もしない</summary>
        public static void Ensure()
        {
            if (_instance != null) return;

            var go = new GameObject(nameof(InputModeTracker));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<InputModeTracker>();
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }

            _instance = this;

            // 触られる前の初期値。触れる画面しか無い端末はタッチから始める
            _current = HasTouchScreenOnly() ? InputMode.Touch : InputMode.KeyboardMouse;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            // 固定中は触った機器を見ない。見た目を確かめている最中に切り替わると確認にならない
            if (_forcedMode != null) return;

            InputMode detected = Detect();
            if (detected == _current) return;

            _current = detected;
            Changed?.Invoke(_current);
        }

        /// <summary>
        /// いま触られている機器を返す。
        /// パッドを先に見るのは、パッドを握った人がキーボードにも手を置いていることがあるため。
        /// </summary>
        private static InputMode Detect()
        {
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && IsGamepadActive(gamepad)) return InputMode.Gamepad;

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) return InputMode.Touch;

            if (Keyboard.current != null && Keyboard.current.anyKey.isPressed) return InputMode.KeyboardMouse;

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                bool clicked = mouse.leftButton.isPressed || mouse.rightButton.isPressed;
                bool moved = mouse.delta.ReadValue().sqrMagnitude > MOUSE_MOVE_THRESHOLD * MOUSE_MOVE_THRESHOLD;

                if (clicked || moved) return InputMode.KeyboardMouse;
            }

            return _current;
        }

        private static bool IsGamepadActive(Gamepad gamepad)
        {
            if (gamepad.leftStick.ReadValue().sqrMagnitude > STICK_DEAD_ZONE * STICK_DEAD_ZONE) return true;
            if (gamepad.rightStick.ReadValue().sqrMagnitude > STICK_DEAD_ZONE * STICK_DEAD_ZONE) return true;

            // どのボタンでもよいので、押されているものがあるか
            foreach (var control in gamepad.allControls)
            {
                if (control is ButtonControl button && button.isPressed) return true;
            }

            return false;
        }

        /// <summary>触れる画面しか持たない端末か</summary>
        private static bool HasTouchScreenOnly()
        {
            return Touchscreen.current != null && Keyboard.current == null && Gamepad.current == null;
        }
    }
}
