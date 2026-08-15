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

        [SerializeField, Range(0f, 0.4f), Tooltip("押したときにキバが閉じる量。枠の高さに対する割合")]
        private float _fangCloseRatio = 0.12f;

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

        /// <summary>牙の高さ(割合)。動かしても厚みが変わらないように控えておく</summary>
        /// <summary>指以外の操作で押されているか</summary>
        private bool _externalPressed;

        private float _fangTopHeight;
        private float _fangBottomHeight;
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

        /// <summary>
        /// 押されている状態を外から渡す。
        /// キーやパッドで攻撃したときも、画面のボタンを噛ませるために使う。
        /// </summary>
        public void SetPressed(bool pressed)
        {
            _externalPressed = pressed;
        }

        /// <summary>押しどきの受付中かを外から設定する。ゲージの色に使う</summary>
        public void SetJustWindow(bool inWindow)
        {
            _isJustWindow = inWindow;
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
            // 牙は割合で置いてあるので、位置ではなく縁の割合を控える。
            // 位置を足す方式では、割合で置かれた物は動かない
            if (_fangTop != null)
            {
                _fangTopHome = _fangTop.anchorMin;
                _fangTopHeight = _fangTop.anchorMax.y - _fangTop.anchorMin.y;
            }

            if (_fangBottom != null)
            {
                _fangBottomHome = _fangBottom.anchorMax;
                _fangBottomHeight = _fangBottom.anchorMax.y - _fangBottom.anchorMin.y;
            }
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
            // 指で押していなくても、キーやパッドで攻撃したときは噛ませる。
            // ボタンを押した人にしか動きが出ないと、機器によって手応えが変わってしまう
            float target = IsHeld || _externalPressed ? 1f : 0f;
            _pressAmount = Mathf.MoveTowards(_pressAmount, target, Time.unscaledDeltaTime * _animateSpeed);

            _visual.anchoredPosition = _visualHomePos + new Vector2(0f, _pressedSinkY * _pressAmount);
            _visual.localScale = _visualHomeScale * Mathf.Lerp(1f, _pressedScale, _pressAmount);

            // キバは押し込みに合わせて内側へ寄る。
            // 割合で置いてあるので、位置ではなく割合のほうを動かす
            float close = _fangCloseRatio * _pressAmount;

            if (_fangTop != null)
            {
                _fangTop.anchorMin = new Vector2(_fangTop.anchorMin.x, _fangTopHome.y - close);
                _fangTop.anchorMax = new Vector2(_fangTop.anchorMax.x, _fangTopHome.y - close + _fangTopHeight);
            }

            if (_fangBottom != null)
            {
                _fangBottom.anchorMin = new Vector2(_fangBottom.anchorMin.x, _fangBottomHome.y + close - _fangBottomHeight);
                _fangBottom.anchorMax = new Vector2(_fangBottom.anchorMax.x, _fangBottomHome.y + close);
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
