using System.Collections.Generic;
using System.Text;
using ProjectKMP.Battle;
using TMPro;
using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>
    /// その日の記録をタイトルに並べる。
    ///
    /// 遊んだ人には次の目標ができ、待っている人には
    /// 『どれくらいで倒せるゲームなのか』が伝わる。
    ///
    /// 記録が1件も無い日は、掲示ごと隠す。
    /// 空の枠が出ていると、壊れているように見えるため。
    /// </summary>
    public class BestTimeDisplay : MonoBehaviour
    {
        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("記録を書き込む文字。未設定なら自分から探す")]
        private TMP_Text _label;

        [SerializeField, Tooltip("見出し。空にすると出さない")]
        private string _heading = "きょうの きろく";

        [SerializeField, Tooltip("1行の書式。{0}=順位 {1}=名前 {2}=タイム")]
        private string _lineFormat = "{0}. {1}  {2}";

        [SerializeField, Tooltip("記録が無い日は掲示ごと隠す")]
        private bool _hideWhenEmpty = true;

        // ---- Unityイベント -------------------------------

        private void OnEnable()
        {
            if (_label == null) _label = GetComponent<TMP_Text>();

            Refresh();
        }

        // ---- 内部処理 ------------------------------------

        private void Refresh()
        {
            List<BestTimeBoard.Entry> entries = BestTimeBoard.Load();

            if (entries.Count == 0 && _hideWhenEmpty)
            {
                gameObject.SetActive(false);
                return;
            }

            if (_label == null) return;

            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(_heading)) builder.AppendLine(_heading);

            for (int i = 0; i < entries.Count; i++)
            {
                builder.AppendLine(string.Format(
                    _lineFormat, i + 1, entries[i].Name, ClearTime.Format(entries[i].Seconds)));
            }

            _label.text = builder.ToString().TrimEnd();
        }
    }
}
