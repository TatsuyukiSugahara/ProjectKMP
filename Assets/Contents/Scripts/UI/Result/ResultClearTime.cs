using ProjectKMP.Battle;
using ProjectKMP.Core;
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

        [SerializeField, Tooltip("その日の順位を出す文字。未設定なら順位を出さない")]
        private TMP_Text _rankLabel;

        [SerializeField, Tooltip("順位の書式。{0} に順位が入る")]
        private string _rankFormat = "きょうの {0}い！";

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

            SubmitToBoard(seconds);
        }

        /// <summary>
        /// その日の記録へ登録し、上位に入っていればそれも見せる。
        /// 順位が出ると『もう一回』の動機になる。
        /// </summary>
        private void SubmitToBoard(double seconds)
        {
            if (_rankLabel != null) _rankLabel.gameObject.SetActive(false);
            if (seconds <= 0.0) return;

            string name = Photon.Pun.PhotonNetwork.NickName;
            int rank = BestTimeBoard.Submit(name, seconds);

            if (rank <= 0 || _rankLabel == null) return;

            _rankLabel.gameObject.SetActive(true);
            _rankLabel.text = string.Format(_rankFormat, rank);
        }
    }
}
