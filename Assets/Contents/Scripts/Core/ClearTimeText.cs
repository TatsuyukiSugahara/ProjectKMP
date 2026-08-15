namespace ProjectKMP.Battle
{
    /// <summary>
    /// クリアタイムを人が読む形に直す。
    ///
    /// 通信の状態に関係なく答えが決まる計算なので、ここへ切り出してある。
    /// 通信を含んだままだと、テストのたびに部屋へ入る必要が出てしまう。
    /// </summary>
    public static class ClearTimeText
    {
        /// <summary>記録が無いときの表示</summary>
        public const string EMPTY = "-:--.--";

        /// <summary>1:23.45 の形にする。負の値は記録なしとして扱う</summary>
        public static string Format(double seconds)
        {
            if (seconds < 0.0) return EMPTY;

            int minutes = (int)(seconds / 60.0);
            double rest = seconds - minutes * 60.0;

            return $"{minutes}:{rest:00.00}";
        }
    }
}
