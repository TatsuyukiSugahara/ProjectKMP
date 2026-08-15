using TMPro;
using UnityEngine;

namespace ProjectKMP.Presentation
{
    /// <summary>
    /// 実行時に作る文字へ割り当てるフォントを探す。
    ///
    /// 実行時に組み立てる表示には、手でフォントを割り当てる先が無い。
    /// そこで画面に出ている文字から借りることになるが、
    /// 最初に見つかったものを取ると英字だけのフォントを掴み、日本語が全部化ける。
    ///
    /// この間違いは擬音・最終フェーズの合図・締めの題字で三度起きた。
    /// 同じ直し方を書き写すのをやめ、ここに集める。
    /// </summary>
    public static class RuntimeFont
    {
        // ---- 定数 ----------------------------------------

        /// <summary>
        /// 出せるかどうかを確かめる文字。
        /// ひらがな・カタカナ・長音を混ぜて、かな全般を扱えるかを見る。
        /// </summary>
        private static readonly char[] SAMPLES = { 'が', 'ぶ', 'タ', 'ー' };

        // ---- 内部状態 ------------------------------------

        private static TMP_FontAsset _cached;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 日本語が出せるフォントを返す。
        /// 一度見つけたら覚えておくので、作るたびに探し直さない。
        /// </summary>
        public static TMP_FontAsset Japanese()
        {
            if (_cached != null) return _cached;

            TMP_FontAsset firstFound = null;

            foreach (TMP_Text sample in Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (sample == null || sample.font == null) continue;

                if (firstFound == null) firstFound = sample.font;
                if (!CanShowJapanese(sample.font)) continue;

                _cached = sample.font;
                return _cached;
            }

            // どれも確かめられなければ、最初に見つけたものを使う。
            // 既定のフォントは英字だけのことが多く、そちらへ落とすと必ず化ける
            _cached = firstFound != null ? firstFound : TMP_Settings.defaultFontAsset;

            return _cached;
        }

        /// <summary>場面をまたいだときに覚えを捨てる。前の場面のフォントは消えている</summary>
        public static void Forget()
        {
            _cached = null;
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>
        /// 日本語を出せるフォントか。
        ///
        /// 引数を付けずに調べると『いま焼き込まれている文字』しか見ないため、
        /// 出せるはずのフォントでも、まだ使っていない文字は無いと判定されてしまう。
        /// フォント全体と、後ろに控えているフォントまで含めて探す。
        /// </summary>
        private static bool CanShowJapanese(TMP_FontAsset font)
        {
            foreach (char sample in SAMPLES)
            {
                if (!font.HasCharacter(sample, true, true)) return false;
            }

            return true;
        }
    }
}
