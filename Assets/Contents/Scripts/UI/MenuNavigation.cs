using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// パッドでメニューを操作できるようにする。
    ///
    /// ボタンの行き来はもともと設定されているが、最初に選ばれるものが決まっていないため、
    /// パッドだけでは何も選べない状態だった。ここで最初の1つを選んでやる。
    ///
    /// マウスや指のときは選択を外す。選択枠が出たままだと、
    /// クリックしている場所と光っている場所が食い違って分かりにくいため。
    ///
    /// 候補は上から順に見て、いま画面に出ていて押せるものを選ぶ。
    /// 画面の切り替わり(名前入力が出るなど)にもこれで追随できる。
    /// </summary>
    public class MenuNavigation : MonoBehaviour
    {
        [SerializeField, Tooltip("選ぶ候補。上から順に、出ていて押せるものが選ばれる")]
        private List<Selectable> _candidates = new List<Selectable>();

        private void OnEnable()
        {
            InputModeTracker.Ensure();
        }

        private void Update()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return;

            // 遊び方が開いている間は口を出さない。後ろのボタンを選び直してしまう
            if (TitleOverlay.IsOpen) return;

            if (InputModeTracker.Current != InputMode.Gamepad)
            {
                // パッド以外では選択枠を出さない
                if (eventSystem.currentSelectedGameObject != null) eventSystem.SetSelectedGameObject(null);
                return;
            }

            if (IsSelectionValid(eventSystem)) return;

            Selectable next = FindFirstUsable();
            if (next == null) return;

            eventSystem.SetSelectedGameObject(next.gameObject);
        }

        /// <summary>いま選ばれているものが、まだ押せる状態で画面に出ているか</summary>
        private static bool IsSelectionValid(EventSystem eventSystem)
        {
            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null || !selected.activeInHierarchy) return false;

            var selectable = selected.GetComponent<Selectable>();

            return selectable != null && selectable.interactable;
        }

        private Selectable FindFirstUsable()
        {
            foreach (Selectable candidate in _candidates)
            {
                if (candidate == null) continue;
                if (!candidate.gameObject.activeInHierarchy) continue;
                if (!candidate.interactable) continue;

                return candidate;
            }

            return null;
        }
    }
}
