using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>グリフの下地の形</summary>
    public enum GlyphShape
    {
        /// <summary>出さない(押せる場所があるので入力表示が要らないとき)</summary>
        None,

        /// <summary>丸。ABXY 用</summary>
        Round,

        /// <summary>角の丸い四角。キーキャップ用</summary>
        Key,

        /// <summary>横長。LB / RB / Space 用</summary>
        Wide,
    }

    /// <summary>
    /// 『どの動作を、どの機器で、どう見せるか』をまとめた表。
    ///
    /// 動作の名前(ビーム・必殺技など)は表示側が持ち、ここは入力の見せ方だけを持つ。
    /// 分けておくことで、名前をアイコンに差し替えても入力表示に手を入れずに済む。
    ///
    /// Sprite を入れればそちらが優先される。公式のボタン素材を使えるようになったら、
    /// ここへ差し込むだけでコードを触らずに切り替えられる。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_InputGlyphs", menuName = "ProjectKMP/Input Glyph Table")]
    public class InputGlyphTable : ScriptableObject
    {
        /// <summary>1つの動作を1つの機器で見せるときの中身</summary>
        [Serializable]
        public class Entry
        {
            public GameAction Action;
            public InputMode Mode;

            [Tooltip("グリフに載せる文字。A / B / LB / Space など")]
            public string Label;

            [Tooltip("下地の形。None なら入力表示そのものを出さない")]
            public GlyphShape Shape = GlyphShape.Round;

            [Tooltip("下地の色")]
            public Color Tint = Color.white;

            [Tooltip("文字の色")]
            public Color LabelColor = Color.white;

            [Tooltip("差し替え用の絵。入っていれば下地と文字の代わりにこれを出す")]
            public Sprite Sprite;

            [Tooltip("長押しで使う動作かどうか。表示に『長押し』を添える")]
            public bool Hold;
        }

        [SerializeField, Tooltip("機器ごとの見せ方の一覧")]
        private List<Entry> _entries = new List<Entry>();

        [Header("下地の絵")]
        [SerializeField] private Sprite _roundSprite;
        [SerializeField] private Sprite _keySprite;
        [SerializeField] private Sprite _wideSprite;

        // ---- 公開API -------------------------------------

        /// <summary>指定した動作・機器の見せ方を返す。無ければ null</summary>
        public Entry Find(GameAction action, InputMode mode)
        {
            foreach (Entry entry in _entries)
            {
                if (entry.Action == action && entry.Mode == mode) return entry;
            }

            return null;
        }

        /// <summary>形に対応する下地の絵を返す</summary>
        public Sprite ResolveShapeSprite(GlyphShape shape)
        {
            switch (shape)
            {
                case GlyphShape.Round: return _roundSprite;
                case GlyphShape.Key: return _keySprite;
                case GlyphShape.Wide: return _wideSprite;
                default: return null;
            }
        }
    }
}
