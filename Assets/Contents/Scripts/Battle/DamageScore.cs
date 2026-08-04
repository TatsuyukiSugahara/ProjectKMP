using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// ボスへ与えたダメージの合計を Player の CustomProperties で保持する。
    /// 各プレイヤーが自分の値だけを書き換えるため、同時更新でも競合しない(BattleScore と同じ方式)。
    /// CustomProperties は全クライアントへ自動同期され、シーンをまたいでも保持されるので、
    /// リザルトのランキングはこの値を読むだけでよい。
    /// </summary>
    public static class DamageScore
    {
        // ---- 定数 ----------------------------------------
        public const string KEY_DAMAGE = "dmg";

        // ---- 公開API -------------------------------------

        /// <summary>指定プレイヤーの与ダメージ合計</summary>
        public static int GetDamage(Photon.Realtime.Player player)
        {
            if (player == null) return 0;
            return player.CustomProperties.TryGetValue(KEY_DAMAGE, out object value) && value is int number ? number : 0;
        }

        /// <summary>自分がボスへ与えたダメージを加算する</summary>
        public static void AddLocalDamage(int damage)
        {
            if (damage <= 0) return;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                { KEY_DAMAGE, GetDamage(PhotonNetwork.LocalPlayer) + damage },
            });
        }

        /// <summary>自分の与ダメージを初期化する(バトル開始時に各自が呼ぶ)</summary>
        public static void ResetLocal()
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { KEY_DAMAGE, 0 } });
        }
    }
}
