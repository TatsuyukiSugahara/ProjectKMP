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

        // ---- 公開API -------------------------------------

        /// <summary>シーン内で唯一のタッチ操作。無い環境では null になる</summary>
        public static TouchControls Instance { get; private set; }

        /// <summary>移動入力(-1〜1)。タッチ非対応の環境では常にゼロ</summary>
        public Vector2 MoveValue => _moveStick != null ? _moveStick.Value : Vector2.zero;

        /// <summary>このフレームのスワイプ量(ピクセル)。なぞっていなければゼロ</summary>
        public Vector2 LookDelta => _lookArea != null ? _lookArea.Delta : Vector2.zero;

        /// <summary>攻撃ボタンが押されているか</summary>
        public bool AttackHeld => _attackButton != null && _attackButton.IsHeld;

        /// <summary>スティックと攻撃ボタンの表示を切り替える。カットシーン中に隠すのに使う</summary>
        public void SetControlsVisible(bool visible)
        {
            if (_moveStick != null) _moveStick.SetVisible(visible);
            if (_attackButton != null) _attackButton.SetVisible(visible);
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
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
