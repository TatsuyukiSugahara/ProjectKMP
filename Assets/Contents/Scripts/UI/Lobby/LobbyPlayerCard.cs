using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.Lobby
{
    /// <summary>
    /// マッチング画面に並ぶプレイヤーカード1枚ぶんの表示。
    /// 中身は基準サイズで組んでおき、並べるときにセル幅に合わせて縮尺する。
    /// </summary>
    public class LobbyPlayerCard : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("縮尺の対象になるカード本体")]
        private RectTransform _cardRect;

        [SerializeField, Tooltip("名前の表示")]
        private TMP_Text _nameText;

        [SerializeField, Tooltip("顔の丸")]
        private Image _avatarImage;

        [SerializeField, Tooltip("ホストのときだけ出す王冠バッジ")]
        private GameObject _hostBadge;

        [SerializeField, Tooltip("自分のときだけ出すバッジ")]
        private GameObject _youBadge;

        [SerializeField, Tooltip("カードを組んだときの基準の幅。この幅を等倍として縮尺する")]
        private float _referenceWidth = 168.0f;

        // ---- 公開API -------------------------------------

        /// <summary>1人ぶんの表示を設定する</summary>
        public void Setup(string playerName, bool isHost, bool isYou, Color avatarColor)
        {
            if (_nameText != null) _nameText.text = playerName;
            if (_avatarImage != null) _avatarImage.color = avatarColor;
            if (_hostBadge != null) _hostBadge.SetActive(isHost);
            if (_youBadge != null) _youBadge.SetActive(isYou);
        }

        /// <summary>並べるセルの幅に合わせてカードを拡大縮小する</summary>
        public void ApplyCellWidth(float cellWidth)
        {
            if (_cardRect == null || _referenceWidth <= 0.0f) return;

            float scale = cellWidth / _referenceWidth;
            _cardRect.localScale = new Vector3(scale, scale, 1.0f);
        }
    }
}
