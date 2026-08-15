using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 画面上の攻撃(かみつき)ボタン。押すと沈んでキバが閉じ、
    /// クールタイム中はフチのゲージが一周しながらボタン全体が暗くなる。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class AttackButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        // ---- 定数 ----------------------------------------

        private const int INVALID_POINTER_ID = -1000;

        // ---- インスペクタ設定 ------------------------------

        [Header("見た目の参照")]
        [SerializeField, Tooltip("押したときに沈ませるまとまり")]
        private RectTransform _visual;

        [SerializeField, Tooltip("押すと消える影")]
        private Graphic _shadow;

        [SerializeField, Tooltip("上のキバ")]
        private RectTransform _fangTop;

        [SerializeField, Tooltip("下のキバ")]
        private RectTransform _fangBottom;

        [Header("押したときの動き")]
        [SerializeField, Tooltip("押したときに沈む量(ピクセル)")]
        private float _pressedSinkY = -12f;

        [SerializeField, Tooltip("押したときの大きさ倍率")]
        private float _pressedScale = 0.96f;

        [SerializeField, Tooltip("押したときにキバが閉じる量(ピクセル)")]
        private float _fangCloseDistance = 26f;

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

        [SerializeField, Tooltip("操作中のプレイヤーのクールタイムを自動で拾う")]
        private bool _followLocalPlayer = true;

        [SerializeField, Tooltip("ジャスト入力の受付中にゲージを塗る色。押しどきを見せる")]
        private Color _justWindowColor = new Color(1f, 0.9f, 0.35f);

        [Header("その他")]
        [SerializeField, Tooltip("タッチ非対応の環境(PCなど)では隠す")]
        private bool _hideOnNonTouchPlatform;

        // ---- 内部状態 ------------------------------------

        private CanvasGroup _canvasGroup;
        private Vector2 _visualHomePos;
        private Vector3 _visualHomeScale = Vector3.one;
        private Vector2 _fangTopHome;
        private Vector2 _fangBottomHome;
        private int _activePointerId = INVALID_POINTER_ID;
        private bool _pressPending;
        private float _pressAmount;
        private float _cooldownRatio;
        private bool _isJustWindow;
        private Color _cooldownFillHomeColor = Color.white;

        // ---- 公開API -------------------------------------

        /// <summary>押されている間 true</summary>
        public bool IsHeld => _activePointerId != INVALID_POINTER_ID;

        /// <summary>押した瞬間を1回だけ取り出す。取り出したらフラグは消える</summary>
        public bool ConsumePress()
        {
            if (!_pressPending) return false;
            _pressPending = false;
            return true;
        }

        /// <summary>クールタイムの残り具合を外から設定する(1=打ったばかり、0=撃てる)</summary>
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
            if (_fangTop != null) _fangTopHome = _fangTop.anchoredPosition;
            if (_fangBottom != null) _fangBottomHome = _fangBottom.anchoredPosition;
            if (_cooldownFill != null) _cooldownFillHomeColor = _cooldownFill.color;
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

        /// <summary>押した瞬間。ここで立てたフラグを入力の読み取り口が拾う</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (_activePointerId != INVALID_POINTER_ID) return;

            _activePointerId = eventData.pointerId;
            _pressPending = true;
        }

        /// <summary>離した瞬間</summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;
            ReleasePress();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>クールタイム中はゲージを一周させ、ボタンを暗くする</summary>
        private void UpdateCooldown()
        {
            if (_followLocalPlayer)
            {
                // 用意された状態から読む。技を探しに行く必要がない
                Core.PlayerStatus status = Core.PlayerStatusHub.Local;

                _cooldownRatio = status.AttackCooldown01.CurrentValue;
                _isJustWindow = status.IsInJustWindow.CurrentValue;
            }

            bool isCooling = _cooldownRatio > 0f;

            if (_cooldownFill != null)
            {
                _cooldownFill.enabled = isCooling;
                // 撃った直後は空、撃てるようになる瞬間に一周し終わる
                _cooldownFill.fillAmount = 1f - _cooldownRatio;

                // 一周し終わる直前だけ色を変えて、押しどきを見せる
                _cooldownFill.color = _isJustWindow ? _justWindowColor : _cooldownFillHomeColor;
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

            // キバは押し込みに合わせて内側へ寄る
            if (_fangTop != null)
            {
                _fangTop.anchoredPosition = _fangTopHome + new Vector2(0f, -_fangCloseDistance * _pressAmount);
            }
            if (_fangBottom != null)
            {
                _fangBottom.anchoredPosition = _fangBottomHome + new Vector2(0f, _fangCloseDistance * _pressAmount);
            }

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
