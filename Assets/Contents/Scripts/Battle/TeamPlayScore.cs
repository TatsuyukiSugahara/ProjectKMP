using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Battle
{
    /// <summary>合体必殺への参加回数をリザルトまで持ち運ぶ、各プレイヤーの協力記録。</summary>
    public static class TeamPlayScore
    {
        public const string KEY_BURST_JOINS = "tbj";

        public static int GetBurstJoins(Photon.Realtime.Player player)
        {
            if (player == null) return 0;
            return player.CustomProperties.TryGetValue(KEY_BURST_JOINS, out object value) && value is int number
                ? number
                : 0;
        }

        public static void AddLocalBurstJoin()
        {
            if (PhotonNetwork.LocalPlayer == null) return;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                { KEY_BURST_JOINS, GetBurstJoins(PhotonNetwork.LocalPlayer) + 1 },
            });
        }

        public static void ResetLocal()
        {
            if (PhotonNetwork.LocalPlayer == null) return;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { KEY_BURST_JOINS, 0 } });
        }
    }
}
