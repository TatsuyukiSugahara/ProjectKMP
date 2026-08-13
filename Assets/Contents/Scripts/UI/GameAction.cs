namespace ProjectKMP.UI
{
    /// <summary>
    /// 画面に『何を押せばよいか』を出す対象の動作。
    /// 表示は InputGlyphTable が持ち、ここは呼び名だけを決める。
    /// </summary>
    public enum GameAction
    {
        /// <summary>かみつき攻撃</summary>
        Attack,

        /// <summary>ビーム</summary>
        Beam,

        /// <summary>必殺技(元気玉)</summary>
        EnergyBall,

        /// <summary>とびこみ</summary>
        Dive,

        /// <summary>ターゲットカメラ</summary>
        TargetCamera,

        /// <summary>決定・進む</summary>
        Confirm,

        /// <summary>戻る・タイトルへ</summary>
        Back,

        /// <summary>演出の飛ばし</summary>
        Skip,
    }
}
