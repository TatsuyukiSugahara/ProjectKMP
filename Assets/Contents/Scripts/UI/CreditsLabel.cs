using TMPro;
using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 使わせてもらった素材のクレジットを表示する。
    /// 外部の素材はライセンス上、出どころの表記が必要なものがあるため、タイトル画面に出しておく。
    /// 素材が増えたら _entries に足すだけでよい(あわせてリポジトリの README にも追記すること)。
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class CreditsLabel : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, TextArea, Tooltip("表示するクレジット。1件につき1行")]
        private string[] _entries = new string[]
        {
            "Produced & Developed by KBCGames",
            "Emoji: Noto Emoji (C) Google LLC / SIL OFL 1.1",
        };

        [SerializeField, Tooltip("横に並べるときの区切り。空にすると改行で縦に並べる")]
        private string _separator = "   /   ";

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            var label = GetComponent<TMP_Text>();
            if (label == null || _entries == null || _entries.Length == 0) return;

            string separator = string.IsNullOrEmpty(_separator) ? "\n" : _separator;
            label.text = string.Join(separator, _entries);
        }
    }
}
