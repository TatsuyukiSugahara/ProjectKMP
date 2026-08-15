using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectKMP.Presentation
{
    /// <summary>
    /// 選ばれた瞬間・マウスが乗った瞬間にカーソル移動音を鳴らす。
    /// UiSoundPlayer が対象のボタンへ実行時に付けるので、手で付ける必要はない。
    /// </summary>
    [DisallowMultipleComponent]
    public class UiCursorSoundTrigger : MonoBehaviour, ISelectHandler, IPointerEnterHandler
    {
        // ---- 公開API -------------------------------------

        /// <summary>マウスを乗せたときにも鳴らすか</summary>
        public bool PlayOnHover { get; set; } = true;

        // ---- 内部状態 ------------------------------------

        private Selectable _selectable;

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
        }

        public void OnSelect(BaseEventData eventData)
        {
            Play();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!PlayOnHover) return;
            Play();
        }

        // ---- 内部処理 ------------------------------------

        private void Play()
        {
            // 押せない状態のボタンで鳴ると、操作できたように聞こえてしまう
            if (_selectable != null && !_selectable.IsInteractable()) return;
            if (UiSoundPlayer.Instance == null) return;

            UiSoundPlayer.Instance.PlayCursor();
        }
    }
}
