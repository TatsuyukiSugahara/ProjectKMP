using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Tag
{
    /// <summary>
    /// 鬼にタッチされた回数を Player の CustomProperties で保持する。
    /// 書き込むのは判定役の MasterClient だけなので、値の取り合いが起きない。
    /// </summary>
    public static class TagScore
    {
        // ---- 定数 ----------------------------------------
        public const string KEY_ONI_COUNT = "oc";

        // ---- 公開API -------------------------------------

        /// <summary>そのプレイヤーが鬼にされた回数</summary>
        public static int GetOniCount(Photon.Realtime.Player player)
        {
            if (player == null) return 0;

            return player.CustomProperties.TryGetValue(KEY_ONI_COUNT, out object value)
                && value is int count ? count : 0;
        }

        /// <summary>タッチされたプレイヤーの回数を1増やす。MasterClient だけが呼ぶこと</summary>
        public static void AddOniCount(Photon.Realtime.Player player)
        {
            if (!PhotonNetwork.IsMasterClient || player == null) return;

            player.SetCustomProperties(new Hashtable { { KEY_ONI_COUNT, GetOniCount(player) + 1 } });
        }

        /// <summary>全員の記録を初期化する。MasterClient だけが呼ぶこと</summary>
        public static void ResetAll()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerList)
            {
                player.SetCustomProperties(new Hashtable { { KEY_ONI_COUNT, 0 } });
            }
        }
    }
}
