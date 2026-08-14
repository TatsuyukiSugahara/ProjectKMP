using System.Collections.Generic;
using Photon.Pun;
using ProjectKMP.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.Result
{
    /// <summary>
    /// リザルトの「みんなのかつやく」表示。
    /// 順位で競わせず、合体必殺への参加や攻撃など、その子ができたことを必ずひとつ褒める。
    /// 行はコードで生成するので人数が変わっても崩れない。
    /// ルームに入っていないとき(エディタで直接再生)は、レイアウト確認用のサンプルを表示する。
    /// </summary>
    public class ResultRanking : MonoBehaviour
    {
        /// <summary>ランキング1行ぶんのデータ</summary>
        public struct RankingEntry
        {
            public string Name;
            public int Damage;
            public int BurstJoins;
            public string Praise;

            public RankingEntry(string name, int damage, int burstJoins = 0, string praise = null)
            {
                Name = name;
                Damage = damage;
                BurstJoins = burstJoins;
                Praise = praise;
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
                    int damage = DamageScore.GetDamage(player);
                    int joins = TeamPlayScore.GetBurstJoins(player);
                    entries.Add(new RankingEntry(name, damage, joins, ResolvePraise(damage, joins)));
                }
            }
            else
            {
                // ルームに入っていない(エディタで直接再生した)ときのレイアウト確認用サンプル
                entries.Add(new RankingEntry("プレイヤー1", 320, 2, "ひっさつ名人！"));
                entries.Add(new RankingEntry("プレイヤー2", 250, 1, "れんけい名人！"));
                entries.Add(new RankingEntry("プレイヤー3", 180, 0, "ガブガブ名人！"));
                entries.Add(new RankingEntry("プレイヤー4", 90, 0, "げんきいっぱい！"));
            }

            // 順位にしない。ロビーから同じ入室順で並べ、全員を同じ大きさで見せる。
            return entries;
        }

        private static string ResolvePraise(int damage, int burstJoins)
        {
            if (burstJoins >= 2) return "ひっさつ名人！";
            if (burstJoins == 1) return "れんけい名人！";
            if (damage >= 200) return "ガブガブ名人！";
            if (damage > 0) return "ナイスアタック！";
            return "げんきいっぱい！";
        }

        private void CreateRow(int index, RankingEntry entry)
        {
            Color accent = entry.BurstJoins > 0 ? _goldColor : new Color(0.45f, 0.85f, 1.0f, 1.0f);

            var rowGo = new GameObject($"Row_{index + 1}", typeof(RectTransform));
            var row = rowGo.GetComponent<RectTransform>();
            row.SetParent(_rowContainer, false);
            rowGo.layer = LayerMask.NameToLayer("UI");
            row.anchorMin = new Vector2(0.5f, 1.0f);
            row.anchorMax = new Vector2(0.5f, 1.0f);
            row.pivot = new Vector2(0.5f, 1.0f);
            row.sizeDelta = new Vector2(_rowWidth, _rowHeight);
            row.anchoredPosition = new Vector2(0.0f, -index * (_rowHeight + _rowSpacing));

            // 全員を同じ大きさで表示する。色は協力必殺へ参加したかだけで変える。
            var bg = rowGo.AddComponent<Image>();
            bg.sprite = _rowSprite;
            bg.type = Image.Type.Sliced;
            bg.color = entry.BurstJoins > 0 ? Color.Lerp(accent, Color.black, 0.62f) : _rowColor;
            bg.raycastTarget = false;

            // プレイヤー名
            var name = CreateText(row, "Name", entry.Name, 40.0f, Color.white);
            name.rectTransform.anchorMin = new Vector2(0.0f, 0.0f);
            name.rectTransform.anchorMax = new Vector2(1.0f, 1.0f);
            name.rectTransform.offsetMin = new Vector2(50.0f, 0.0f);
            name.rectTransform.offsetMax = new Vector2(-520.0f, 0.0f);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.overflowMode = TextOverflowModes.Ellipsis;

            // 順位や王冠の代わりに、その子だけの肯定的な称号を大きく出す。
            var praise = CreateText(row, "Praise", string.IsNullOrEmpty(entry.Praise) ? ResolvePraise(entry.Damage, entry.BurstJoins) : entry.Praise, 38.0f, accent);
            praise.rectTransform.anchorMin = new Vector2(1.0f, 0.0f);
            praise.rectTransform.anchorMax = new Vector2(1.0f, 1.0f);
            praise.rectTransform.offsetMin = new Vector2(-500.0f, 0.0f);
            praise.rectTransform.offsetMax = new Vector2(-35.0f, 0.0f);
            praise.alignment = TextAlignmentOptions.MidlineRight;
            praise.fontStyle = FontStyles.Bold;
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
