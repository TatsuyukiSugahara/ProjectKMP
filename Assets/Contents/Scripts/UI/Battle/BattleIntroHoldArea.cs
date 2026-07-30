using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectKMP.UI.Battle
{
    /// <summary>
    /// 画面のどこを押しても長押しとして受け取るための領域。
    /// スマホにはAボタンが無いので、画面押し込みでもスキップできるようにしている。
    /// </summary>
    public class BattleIntroHoldArea : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        // ---- 公開API -------------------------------------

        /// <summary>いま押されているか</summary>
        public bool IsHeld { get; private set; }

        // ---- Unityイベント -------------------------------

        public void OnPointerDown(PointerEventData eventData)
        {
            IsHeld = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsHeld = false;
        }

        private void OnDisable()
        {
            // 押したまま消えた場合に押しっぱなし扱いが残らないようにする
            IsHeld = false;
        }
    }
}
