using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 画面をなぞってカメラを回すための領域。指の移動量(ピクセル)をそのまま渡す。
    /// 移動スティックなど手前にあるUIを触っているときは、そちらが優先されるのでここには届かない。
    /// </summary>
    public class TouchLookArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        // ---- 定数 ----------------------------------------

        private const int INVALID_POINTER_ID = -1000;

        // ---- 内部状態 ------------------------------------

        private int _activePointerId = INVALID_POINTER_ID;
        private Vector2 _delta;
        private int _deltaFrame = -1;

        // ---- 公開API -------------------------------------

        /// <summary>このフレームの指の移動量(ピクセル)。触っていなければゼロ</summary>
        public Vector2 Delta => _deltaFrame == Time.frameCount ? _delta : Vector2.zero;

        /// <summary>いま画面をなぞっている最中か</summary>
        public bool IsDragging => _activePointerId != INVALID_POINTER_ID;

        // ---- 入力処理 ------------------------------------

        public void OnPointerDown(PointerEventData eventData)
        {
            // 2本目以降の指はカメラ操作に使わない(移動スティックとの取り合いを避ける)
            if (_activePointerId != INVALID_POINTER_ID) return;

            _activePointerId = eventData.pointerId;
            _delta = Vector2.zero;
            _deltaFrame = Time.frameCount;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;

            // 同じフレームに複数回届くことがあるので足し込む
            if (_deltaFrame != Time.frameCount)
            {
                _delta = Vector2.zero;
                _deltaFrame = Time.frameCount;
            }

            _delta += eventData.delta;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;

            _activePointerId = INVALID_POINTER_ID;
            _delta = Vector2.zero;
        }

        private void OnDisable()
        {
            _activePointerId = INVALID_POINTER_ID;
            _delta = Vector2.zero;
        }
    }
}
