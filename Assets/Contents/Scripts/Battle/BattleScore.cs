using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 撃破数・死亡数を Player の CustomProperties で保持する。
    /// 各プレイヤーが自分の値だけを書き換えるため、同時更新でも競合しない。
    /// </summary>
    public static class BattleScore
    {
        // ---- 定数 ----------------------------------------
        public const string KEY_KILLS  = "k";
        public const string KEY_DEATHS = "d";

        // ---- 公開API -------------------------------------

        public static int GetKills(Photon.Realtime.Player player) => GetValue(player, KEY_KILLS);

        public static int GetDeaths(Photon.Realtime.Player player) => GetValue(player, KEY_DEATHS);

        /// <summary>自分の撃破数を1増やす</summary>
        public static void AddLocalKill()
        {
            SetLocal(KEY_KILLS, GetKills(PhotonNetwork.LocalPlayer) + 1);
        }

        /// <summary>自分の死亡数を1増やす</summary>
        public static void AddLocalDeath()
        {
            SetLocal(KEY_DEATHS, GetDeaths(PhotonNetwork.LocalPlayer) + 1);
        }

        /// <summary>自分のスコアを初期化する(再戦時に各自が呼ぶ)</summary>
        public static void ResetLocal()
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                { KEY_KILLS,  0 },
                { KEY_DEATHS, 0 },
            });
        }

        // ---- 内部処理 ------------------------------------

        private static int GetValue(Photon.Realtime.Player player, string key)
        {
            if (player == null) return 0;
            return player.CustomProperties.TryGetValue(key, out object value) && value is int number ? number : 0;
        }

        private static void SetLocal(string key, int value)
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { key, value } });
        }
    }
}
