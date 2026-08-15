using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>
    /// インゲームのタッチ操作をまとめて持つ入れ物。
    /// 移動は左下のスティック、カメラは画面をなぞるスワイプ、攻撃は右下のボタンで操作する。
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("移動に使う左スティック")]
        private VirtualStick _moveStick;

        [SerializeField, Tooltip("カメラを回すスワイプ領域")]
        private TouchLookArea _lookArea;

        [SerializeField, Tooltip("攻撃(かみつき)ボタン")]
        private AttackButton _attackButton;

        [SerializeField, Tooltip("スキル(ビーム)ボタン")]
        private SkillButton _skillButton;

        [SerializeField, Tooltip("スキル(元気玉)ボタン")]
        private SkillButton _energyBallButton;

        [SerializeField, Tooltip("とびこみボタン")]
        private SkillButton _diveButton;

        [SerializeField, Tooltip("ターゲットカメラボタン。押すたびにボスへの固定を入り切りする")]
        private SkillButton _targetButton;

        // ---- 公開API -------------------------------------

        /// <summary>シーン内で唯一のタッチ操作。無い環境では null になる</summary>
        public static TouchControls Instance { get; private set; }

        /// <summary>移動入力(-1〜1)。タッチ非対応の環境では常にゼロ</summary>
        public Vector2 MoveValue => _moveStick != null ? _moveStick.Value : Vector2.zero;

        /// <summary>このフレームのスワイプ量(ピクセル)。なぞっていなければゼロ</summary>
        public Vector2 LookDelta => _lookArea != null ? _lookArea.Delta : Vector2.zero;

        /// <summary>攻撃ボタンが押されているか</summary>
        public bool AttackHeld => _attackButton != null && _attackButton.IsHeld;

        /// <summary>スキル(ビーム)ボタンが押されているか。長押し判定に使う</summary>
        public bool SkillHeld => _skillButton != null && _skillButton.IsHeld;

        /// <summary>スキル(元気玉)ボタンが押されているか。長押し判定に使う</summary>
        public bool EnergyBallHeld => _energyBallButton != null && _energyBallButton.IsHeld;

        /// <summary>
        /// ターゲットカメラボタンが押されているか。
        /// 押した瞬間の判定はカメラ側で持つので、ここでは押下の有無だけを返す。
        /// </summary>
        public bool TargetHeld => _targetButton != null && _targetButton.IsHeld;

        /// <summary>とびこみボタンが押されているか。押している間だけ予測を出す</summary>
        public bool DiveHeld => _diveButton != null && _diveButton.IsHeld;

        /// <summary>スティックと攻撃ボタンの表示を切り替える。カットシーン中に隠すのに使う</summary>
        public void SetControlsVisible(bool visible)
        {
            if (_moveStick != null) _moveStick.SetVisible(visible);
            if (_attackButton != null) _attackButton.SetVisible(visible);
            if (_skillButton != null) _skillButton.SetVisible(visible);
            if (_energyBallButton != null) _energyBallButton.SetVisible(visible);
            if (_diveButton != null) _diveButton.SetVisible(visible);
            if (_targetButton != null) _targetButton.SetVisible(visible);
        }

        /// <summary>攻撃ボタンを押した瞬間を1回だけ取り出す</summary>
        public bool ConsumeAttackPress()
        {
            return _attackButton != null && _attackButton.ConsumePress();
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            Instance = this;
            ServiceLocator.Register(this);
        }

        private void Start()
        {
            // VirtualStick は生成直後に自分を隠すので、インゲームでは出しておく
            if (_moveStick != null) _moveStick.SetVisible(true);
            if (_attackButton != null) _attackButton.SetVisible(true);
            if (_skillButton != null) _skillButton.SetVisible(true);
            if (_energyBallButton != null) _energyBallButton.SetVisible(true);
            if (_diveButton != null) _diveButton.SetVisible(true);
        }

        /// <summary>
        /// 画面のボタンの状態を読み取り口へ渡す。
        ///
        /// 遊びの処理が画面のボタンを直接見に行くと、画面の作りに縛られてしまう。
        /// こちらから渡す形にすれば、読む側はどこから来た入力かを知らなくてよい。
        /// </summary>
        private void Update()
        {
            Core.GameInput.PushTouch(
                MoveValue,
                LookDelta,
                AttackHeld,
                SkillHeld,
                EnergyBallHeld,
                DiveHeld,
                TargetHeld);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // 場面を抜けたあとに押しっぱなしと見なされないよう、状態を消しておく
            Core.GameInput.ClearTouch();
        }
    }
}
