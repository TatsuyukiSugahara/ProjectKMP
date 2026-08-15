namespace ProjectKMP.Core
{
    /// <summary>
    /// 操作している人の状態の置き場。
    ///
    /// 画面はここから状態を受け取る。プレイヤーの物を探し回る必要がなくなり、
    /// まだ生まれていない場面でも安全に読める。
    ///
    /// 状態は場面をまたいで残らない。戦いが終わったら消す。
    /// 前の戦いの体力が次の画面へ持ち越されると、見た目が食い違う。
    /// </summary>
    public static class PlayerStatusHub
    {
        /// <summary>操作している人の状態。まだ生まれていなければ空のまま返る</summary>
        public static PlayerStatus Local { get; } = new PlayerStatus();

        /// <summary>状態を初期に戻す。場面を抜けるときに呼ぶ</summary>
        public static void Reset()
        {
            Local.SetHp(0, 0);
            Local.SetDead(false);
            Local.SetAttack(0.0f, false);
            Local.SetBeamCooldown(0.0f);
            Local.SetDiveCooldown(0.0f);
            Local.SetEnergyBallCooldown(0.0f);
            Local.SetAimingBeam(false);
            Local.SetLockTarget(null);
            Local.SetFriendBeamCallTarget(null);
        }
    }
}
