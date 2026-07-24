using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectKMP.UI
{
    /// <summary>
    /// タッチ操作用の発射ボタン。Android / iOS でのみ表示する。
    /// 押しっぱなしを IsHeld で公開し、連射間隔の管理は PlayerShooter 側に任せる。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class FireButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        // ---- 定数 ----------------------------------------
        private const int INVALID_POINTER_ID = -1000;

        // ---- 調整パラメータ ------------------------------
        [SerializeField] private bool _showInEditor = true; // エディタでの確認用

        // ---- 内部状態 ------------------------------------
        private CanvasGroup _canvasGroup;
        private bool _isSupportedPlatform;
        private int _activePointerId = INVALID_POINTER_ID;

        // ---- 公開API -------------------------------------

        /// <summary>押されている間 true。非対応環境では常に false</summary>
        public bool IsHeld => _activePointerId != INVALID_POINTER_ID;

        /// <summary>表示を切り替える。非対応環境では visible を渡しても表示されない</summary>
        public void SetVisible(bool visible)
        {
            bool show = visible && _isSupportedPlatform;

            _canvasGroup.alpha = show ? 1.0f : 0.0f;
            _canvasGroup.interactable = show;
            _canvasGroup.blocksRaycasts = show;

            if (!show) _activePointerId = INVALID_POINTER_ID;
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _isSupportedPlatform = IsTouchPlatform();

            ServiceLocator.Register(this);
            SetVisible(false);
        }

        private void OnDisable()
        {
            _activePointerId = INVALID_POINTER_ID;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_activePointerId != INVALID_POINTER_ID) return;
            _activePointerId = eventData.pointerId;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;
            _activePointerId = INVALID_POINTER_ID;
        }

        // ---- 内部処理 ------------------------------------

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
