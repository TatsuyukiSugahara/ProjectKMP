using UnityEngine;

namespace ProjectKMP.Core
{
    /// <summary>
    /// ボスのHPを何本かに分けて見せるための計算。
    ///
    /// 見た目の部品から切り出してある。画面が無くても答えが決まる計算なので、
    /// ここだけを取り出せば、絵を出さずに正しさを確かめられる。
    /// </summary>
    public static class BossSegments
    {
        /// <summary>
        /// 残っている本数。1が最後の1本、0は削り切った状態。
        /// </summary>
        public static int Remaining(float total01, int segmentCount)
        {
            if (segmentCount <= 1) return total01 <= 0.0f ? 0 : 1;
            if (total01 <= 0.0f) return 0;

            return Mathf.Clamp(Mathf.CeilToInt(total01 * segmentCount), 1, segmentCount);
        }

        /// <summary>
        /// いま削っている1本ぶんの残り(0〜1)。
        /// 全体が減って本の切れ目をまたぐと0から1へ戻る。
        /// </summary>
        public static float Ratio(float total01, int segmentCount)
        {
            if (segmentCount <= 1) return Mathf.Clamp01(total01);
            if (total01 <= 0.0f) return 0.0f;
            if (total01 >= 1.0f) return 1.0f;

            float scaled = total01 * segmentCount;

            // 切れ目のちょうど上は、削り切った側ではなく満タン側として見せる
            return Mathf.Clamp01(scaled - (Mathf.CeilToInt(scaled) - 1));
        }
    }
}
