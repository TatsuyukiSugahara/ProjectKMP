using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// タッチ操作用の仮想スティック。Android / iOS でのみ表示し、それ以外の環境では常に無入力を返す。
    /// 入力値は ServiceLocator 経由で PlayerMover が取得する。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    public class VirtualStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        // ---- 定数 ----------------------------------------
        private const int INVALID_POINTER_ID = -1000;

        // ---- 参照 ----------------------------------------
        [SerializeField] private RectTransform _handle;

        // ---- 調整パラメータ ------------------------------
        [SerializeField] private float _handleRange = 100.0f; // ハンドルが動ける半径(px)
        [SerializeField] private bool  _showInEditor = true;  // エディタで見た目を確認するための強制表示

        // ---- 内部状態 ------------------------------------
        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Canvas _canvas;
        private Vector2 _value;
        private bool _isSupportedPlatform;
        private int _activePointerId = INVALID_POINTER_ID;

        // ---- 公開API -------------------------------------

        /// <summary>-1〜1 に正規化された入力値。非対応環境では常にゼロ</summary>
        public Vector2 Value => _value;

        /// <summary>表示を切り替える。非対応環境では visible を渡しても表示されない</summary>
        public void SetVisible(bool visible)
        {
            bool show = visible && _isSupportedPlatform;

            _canvasGroup.alpha = show ? 1.0f : 0.0f;
            _canvasGroup.interactable = show;
            _canvasGroup.blocksRaycasts = show;

            if (!show) ResetStick();
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvas = GetComponentInParent<Canvas>();
            _isSupportedPlatform = IsTouchPlatform();

            ServiceLocator.Register(this);

            // 生成直後は隠しておき、インゲームに入ったタイミングで表示する
            SetVisible(false);
        }

        private void OnDisable()
        {
            ResetStick();
        }

        // ---- 入力処理 ------------------------------------

        public void OnPointerDown(PointerEventData eventData)
        {
            // 既に別の指で操作中なら無視する(マルチタッチでの誤動作防止)
            if (_activePointerId != INVALID_POINTER_ID) return;

            _activePointerId = eventData.pointerId;
            UpdateHandle(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;
            UpdateHandle(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;

            _activePointerId = INVALID_POINTER_ID;
            ResetStick();
        }

        // ---- 内部処理 ------------------------------------

        private void UpdateHandle(PointerEventData eventData)
        {
            // Overlay の場合はカメラを渡してはいけないので null にする
            Camera eventCamera = null;
            if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                eventCamera = _canvas.worldCamera;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rectTransform, eventData.position, eventCamera, out Vector2 localPoint))
            {
                return;
            }

            Vector2 clamped = Vector2.ClampMagnitude(localPoint, _handleRange);
            if (_handle != null) _handle.anchoredPosition = clamped;
            _value = clamped / _handleRange;
        }

        private void ResetStick()
        {
            _value = Vector2.zero;
            _activePointerId = INVALID_POINTER_ID;
            if (_handle != null) _handle.anchoredPosition = Vector2.zero;
        }

        /// <summary>タッチ操作を前提とするプラットフォームかどうか</summary>
        private bool IsTouchPlatform()
        {
#if UNITY_EDITOR
            return _showInEditor;
#elif UNITY_ANDROID || UNITY_IOS
            return true;
#else
            return false;
#endif
        }
    }
}
