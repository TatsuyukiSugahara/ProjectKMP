using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 画面上のスキル(ビーム)ボタン。長押しで狙い、離すと発射する。
    /// 押している間は沈み、クールタイム中はフチのゲージが一周しながら全体が暗くなる
    /// (AttackButton と同じ見せ方)。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class SkillButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        // ---- 定数 ----------------------------------------

        private const int INVALID_POINTER_ID = -1000;

        /// <summary>クールタイム表示の参照元にするスキル</summary>
        public enum CooldownSource
        {
            /// <summary>ビーム</summary>
            Beam,

            /// <summary>必殺技(元気玉)</summary>
            EnergyBall,

            /// <summary>とびこみ</summary>
            Dive,
        }

        // ---- インスペクタ設定 ------------------------------

        [Header("見た目の参照")]
        [SerializeField, Tooltip("押したときに沈ませるまとまり")]
        private RectTransform _visual;

        [SerializeField, Tooltip("押すと薄くなる影")]
        private Graphic _shadow;

        [Header("押したときの動き")]
        [SerializeField, Tooltip("押したときに沈む量(ピクセル)")]
        private float _pressedSinkY = -12f;

        [SerializeField, Tooltip("押したときの大きさ倍率")]
        private float _pressedScale = 0.96f;

        [SerializeField, Tooltip("動きの速さ。大きいほどキビキビする")]
        private float _animateSpeed = 18f;

        [Header("クールタイム表示")]
        [SerializeField, Tooltip("フチを一周するゲージ")]
        private Image _cooldownFill;

        [SerializeField, Tooltip("ゲージの下地")]
        private Graphic _cooldownTrack;

        [SerializeField, Tooltip("クールタイム中にかぶせる暗幕")]
        private Graphic _dimOverlay;

        [SerializeField, Tooltip("クールタイム中の暗さ(0〜1)"), Range(0f, 1f)]
        private float _dimAlpha = 0.45f;

        [Header("その他")]
        [SerializeField, Tooltip("タッチ非対応の環境(PCなど)では隠す")]
        private bool _hideOnNonTouchPlatform;

        // ---- 内部状態 ------------------------------------

        private CanvasGroup _canvasGroup;
        private Vector2 _visualHomePos;
        private Vector3 _visualHomeScale = Vector3.one;
        private int _activePointerId = INVALID_POINTER_ID;
        private float _pressAmount;
        private float _cooldownRatio;
        private bool _pressQueued;

        // ---- 公開API -------------------------------------

        /// <summary>押した瞬間を1回だけ取り出す。長押しではない技(とびこみなど)で使う</summary>
        public bool ConsumePress()
        {
            if (!_pressQueued) return false;

            _pressQueued = false;
            return true;
        }

        /// <summary>押されている間 true。ビームスキルの長押し判定に使う</summary>
        public bool IsHeld => _activePointerId != INVALID_POINTER_ID;

        /// <summary>クールタイムの残り具合を外から設定する(1=撃った直後、0=撃てる)</summary>
        public void SetCooldownRatio(float ratio01)
        {
            _cooldownRatio = Mathf.Clamp01(ratio01);
        }

        /// <summary>表示を切り替える</summary>
        public void SetVisible(bool visible)
        {
            bool show = visible && (!_hideOnNonTouchPlatform || IsTouchPlatform());

            _canvasGroup.alpha = show ? 1f : 0f;
            _canvasGroup.interactable = show;
            _canvasGroup.blocksRaycasts = show;

            if (!show) ReleasePress();
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_visual == null) _visual = (RectTransform)transform;

            _visualHomePos = _visual.anchoredPosition;
            _visualHomeScale = _visual.localScale;
        }

        private void OnDisable()
        {
            ReleasePress();
        }

        private void Update()
        {
            UpdateCooldown();
            UpdatePressVisual();
        }

        /// <summary>押した瞬間。押している間の状態は IsHeld から読み取られる</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (_activePointerId != INVALID_POINTER_ID) return;

            _activePointerId = eventData.pointerId;
            _pressQueued = true;
        }

        /// <summary>離した瞬間(=発射のタイミング)</summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;
            ReleasePress();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>
        /// クールタイム中はゲージを一周させ、ボタンを暗くする。
        /// 値は外から渡されたものだけを使う。自分で状態を見に行かない。
        /// </summary>
        private void UpdateCooldown()
        {
            bool isCooling = _cooldownRatio > 0f;

            if (_cooldownFill != null)
            {
                _cooldownFill.enabled = isCooling;
                _cooldownFill.fillAmount = 1f - _cooldownRatio;
            }

            if (_cooldownTrack != null) _cooldownTrack.enabled = isCooling;

            if (_dimOverlay != null)
            {
                Color color = _dimOverlay.color;
                color.a = isCooling ? _dimAlpha : 0f;
                _dimOverlay.color = color;
                _dimOverlay.enabled = isCooling;
            }
        }

        /// <summary>押し込みの見た目をなめらかに近づける</summary>
        private void UpdatePressVisual()
        {
            float target = IsHeld ? 1f : 0f;
            _pressAmount = Mathf.MoveTowards(_pressAmount, target, Time.unscaledDeltaTime * _animateSpeed);

            _visual.anchoredPosition = _visualHomePos + new Vector2(0f, _pressedSinkY * _pressAmount);
            _visual.localScale = _visualHomeScale * Mathf.Lerp(1f, _pressedScale, _pressAmount);

            if (_shadow != null)
            {
                Color color = _shadow.color;
                color.a = Mathf.Lerp(0.35f, 0.05f, _pressAmount);
                _shadow.color = color;
            }
        }

        private void ReleasePress()
        {
            _activePointerId = INVALID_POINTER_ID;
        }

        private static bool IsTouchPlatform()
        {
#if UNITY_EDITOR
            return true;
#elif UNITY_ANDROID || UNITY_IOS
            return true;
#else
            return false;
#endif
        }
    }
}
