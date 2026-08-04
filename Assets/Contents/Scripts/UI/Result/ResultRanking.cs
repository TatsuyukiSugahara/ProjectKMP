using System.Collections.Generic;
using Photon.Pun;
using ProjectKMP.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.Result
{
    /// <summary>
    /// リザルトの与ダメージランキング表示。
    /// ルームの各プレイヤーの与ダメージ(DamageScore、CustomProperties同期)を読み、多い順に行を並べる。
    /// 1〜3位には金・銀・銅の王冠を付けて目立たせる。行はコードで生成するので人数が変わっても崩れない。
    /// ルームに入っていないとき(エディタで直接再生)は、レイアウト確認用のサンプルを表示する。
    /// </summary>
    public class ResultRanking : MonoBehaviour
    {
        /// <summary>ランキング1行ぶんのデータ</summary>
        public struct RankingEntry
        {
            public string Name;
            public int Damage;

            public RankingEntry(string name, int damage)
            {
                Name = name;
                Damage = damage;
            }
        }

        // ---- インスペクタ設定 ------------------------------

        [Header("参照")]
        [SerializeField, Tooltip("行を並べる親。上端基準で下へ並ぶ")]
        private RectTransform _rowContainer;

        [SerializeField, Tooltip("文字に使うフォント")]
        private TMP_FontAsset _font;

        [SerializeField, Tooltip("行の背景に使うピル型スプライト")]
        private Sprite _rowSprite;

        [SerializeField, Tooltip("1〜3位に付ける王冠スプライト")]
        private Sprite _crownSprite;

        [Header("見た目")]
        [SerializeField] private float _rowWidth = 980.0f;
        [SerializeField] private float _rowHeight = 92.0f;
        [SerializeField] private float _rowSpacing = 16.0f;
        [SerializeField, Tooltip("4位以下の行の背景色")]
        private Color _rowColor = new Color(0.098f, 0.110f, 0.141f, 0.85f);
        [SerializeField, Tooltip("1位(金)")]
        private Color _goldColor = new Color(1.00f, 0.82f, 0.29f);
        [SerializeField, Tooltip("2位(銀)")]
        private Color _silverColor = new Color(0.80f, 0.84f, 0.90f);
        [SerializeField, Tooltip("3位(銅)")]
        private Color _bronzeColor = new Color(0.83f, 0.53f, 0.28f);

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            Build(CollectEntries());
        }

        // ---- 公開API -------------------------------------

        /// <summary>ランキングの行を作り直す(エディタでの見た目確認にも使える)</summary>
        public void Build(IReadOnlyList<RankingEntry> entries)
        {
            if (_rowContainer == null) return;

            // 作り直しに備えて既存の行を消す
            for (int i = _rowContainer.childCount - 1; i >= 0; i--)
            {
                var child = _rowContainer.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            for (int i = 0; i < entries.Count; i++)
            {
                CreateRow(i, entries[i]);
            }
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>ルームの全プレイヤーの与ダメージを集め、多い順に並べる</summary>
        private List<RankingEntry> CollectEntries()
        {
            var entries = new List<RankingEntry>();

            if (PhotonNetwork.InRoom)
            {
                foreach (var player in PhotonNetwork.PlayerList)
                {
                    string name = string.IsNullOrWhiteSpace(player.NickName) ? $"プレイヤー{player.ActorNumber}" : player.NickName;
                    entries.Add(new RankingEntry(name, DamageScore.GetDamage(player)));
                }
            }
            else
            {
                // ルームに入っていない(エディタで直接再生した)ときのレイアウト確認用サンプル
                entries.Add(new RankingEntry("プレイヤー1", 320));
                entries.Add(new RankingEntry("プレイヤー2", 250));
                entries.Add(new RankingEntry("プレイヤー3", 180));
                entries.Add(new RankingEntry("プレイヤー4", 90));
            }

            // ダメージの多い順。同点は名前順で安定させる
            entries.Sort((a, b) =>
            {
                int byDamage = b.Damage.CompareTo(a.Damage);
                return byDamage != 0 ? byDamage : string.CompareOrdinal(a.Name, b.Name);
            });
            return entries;
        }

        /// <summary>順位に応じた色。4位以下は白</summary>
        private Color GetRankColor(int rankIndex)
        {
            if (rankIndex == 0) return _goldColor;
            if (rankIndex == 1) return _silverColor;
            if (rankIndex == 2) return _bronzeColor;
            return Color.white;
        }

        private void CreateRow(int index, RankingEntry entry)
        {
            bool isTop3 = index < 3;
            Color rankColor = GetRankColor(index);

            var rowGo = new GameObject($"Row_{index + 1}", typeof(RectTransform));
            var row = rowGo.GetComponent<RectTransform>();
            row.SetParent(_rowContainer, false);
            rowGo.layer = LayerMask.NameToLayer("UI");
            row.anchorMin = new Vector2(0.5f, 1.0f);
            row.anchorMax = new Vector2(0.5f, 1.0f);
            row.pivot = new Vector2(0.5f, 1.0f);
            row.sizeDelta = new Vector2(_rowWidth, _rowHeight);
            row.anchoredPosition = new Vector2(0.0f, -index * (_rowHeight + _rowSpacing));

            // 1位はひとまわり大きくして主役感を出す
            if (index == 0) row.localScale = Vector3.one * 1.06f;

            // 背景: 上位3位は順位の色を暗くした帯にする(ボスゲージと同じ配色関係)
            var bg = rowGo.AddComponent<Image>();
            bg.sprite = _rowSprite;
            bg.type = Image.Type.Sliced;
            bg.color = isTop3 ? Color.Lerp(rankColor, Color.black, 0.55f) : _rowColor;
            bg.raycastTarget = false;

            // 王冠(1〜3位のみ)
            if (isTop3 && _crownSprite != null)
            {
                var crown = CreateImage(row, "Crown", _crownSprite, rankColor);
                crown.rectTransform.anchorMin = new Vector2(0.0f, 0.5f);
                crown.rectTransform.anchorMax = new Vector2(0.0f, 0.5f);
                crown.rectTransform.anchoredPosition = new Vector2(64.0f, 6.0f);
                crown.rectTransform.sizeDelta = new Vector2(64.0f, 64.0f);
                crown.rectTransform.localRotation = Quaternion.Euler(0.0f, 0.0f, 10.0f);
            }

            // 順位
            var rank = CreateText(row, "Rank", (index + 1).ToString(), isTop3 ? 52.0f : 40.0f, rankColor);
            rank.rectTransform.anchorMin = new Vector2(0.0f, 0.0f);
            rank.rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
            rank.rectTransform.offsetMin = new Vector2(110.0f, 0.0f);
            rank.rectTransform.offsetMax = new Vector2(190.0f, 0.0f);
            rank.alignment = TextAlignmentOptions.Center;

            // プレイヤー名
            var name = CreateText(row, "Name", entry.Name, 40.0f, Color.white);
            name.rectTransform.anchorMin = new Vector2(0.0f, 0.0f);
            name.rectTransform.anchorMax = new Vector2(1.0f, 1.0f);
            name.rectTransform.offsetMin = new Vector2(210.0f, 0.0f);
            name.rectTransform.offsetMax = new Vector2(-300.0f, 0.0f);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.overflowMode = TextOverflowModes.Ellipsis;

            // ダメージ量
            var damage = CreateText(row, "Damage", entry.Damage.ToString("N0"), isTop3 ? 46.0f : 38.0f, isTop3 ? rankColor : Color.white);
            damage.rectTransform.anchorMin = new Vector2(1.0f, 0.0f);
            damage.rectTransform.anchorMax = new Vector2(1.0f, 1.0f);
            damage.rectTransform.offsetMin = new Vector2(-280.0f, 0.0f);
            damage.rectTransform.offsetMax = new Vector2(-40.0f, 0.0f);
            damage.alignment = TextAlignmentOptions.MidlineRight;
            damage.fontStyle = FontStyles.Bold;
        }

        private Image CreateImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private TextMeshProUGUI CreateText(RectTransform parent, string name, string text, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = _font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
