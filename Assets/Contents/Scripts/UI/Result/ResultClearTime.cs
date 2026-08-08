using ProjectKMP.Battle;
using TMPro;
using UnityEngine;

namespace ProjectKMP.UI.Result
{
    /// <summary>
    /// リザルトにクリアタイムを出す。値は Room の CustomProperties に残っているので、
    /// シーンをまたいでも読むだけでよい(集計や通信は不要)。
    /// </summary>
    public class ResultClearTime : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("タイムを出す文字。未設定なら同じ GameObject から探す")]
        private TMP_Text _label;

        [SerializeField, Tooltip("表示の書式。{0} にタイムが入る")]
        private string _format = "クリアタイム  {0}";

        [SerializeField, Tooltip("記録が無いとき(時間切れなど)は表示ごと隠す")]
        private bool _hideWhenMissing = true;

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            if (_label == null) _label = GetComponent<TMP_Text>();

            double seconds = ClearTime.GetClearSeconds();

            if (seconds < 0.0 && _hideWhenMissing)
            {
                gameObject.SetActive(false);
                return;
            }

            if (_label != null) _label.text = string.Format(_format, ClearTime.Format(seconds));
        }
    }
}
