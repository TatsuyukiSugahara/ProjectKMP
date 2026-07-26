using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Tag
{
    /// <summary>
    /// 現在の鬼を Room の CustomProperties で共有する。
    /// 全員が同じ値を読んで判定するため、鬼の交代に RPC を使わない。
    /// </summary>
    public static class TagState
    {
        // ---- 定数 ----------------------------------------
        public const string KEY_ONI_ACTOR = "oni";
        public const string KEY_TAG_TIME  = "ont";

        // 交代直後に押し合って延々とタッチが往復するのを防ぐための猶予
        public const double TAG_COOLDOWN_SEC = 2.0;

        // ---- 公開API -------------------------------------

        /// <summary>現在の鬼の ActorNumber。未設定なら -1</summary>
        public static int GetOniActorNumber()
        {
            if (!PhotonNetwork.InRoom) return -1;

            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KEY_ONI_ACTOR, out object value)
                && value is int actorNumber ? actorNumber : -1;
        }

        /// <summary>指定したプレイヤーが鬼かどうか</summary>
        public static bool IsOni(Photon.Realtime.Player player)
        {
            return player != null && player.ActorNumber == GetOniActorNumber();
        }

        /// <summary>自分が鬼かどうか</summary>
        public static bool IsLocalOni => IsOni(PhotonNetwork.LocalPlayer);

        /// <summary>交代直後の猶予が明けていて、次のタッチを受け付けられるか</summary>
        public static bool IsTagReady()
        {
            if (!PhotonNetwork.InRoom) return false;

            if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KEY_TAG_TIME, out object value))
            {
                return true;
            }

            return value is double changedTime
                && PhotonNetwork.Time - changedTime >= TAG_COOLDOWN_SEC;
        }

        /// <summary>鬼を交代する。MasterClient だけが呼ぶこと</summary>
        public static void SetOni(int actorNumber)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                { KEY_ONI_ACTOR, actorNumber },
                { KEY_TAG_TIME,  PhotonNetwork.Time },
            });
        }

        /// <summary>参加者からランダムに鬼を決める。MasterClient だけが呼ぶこと</summary>
        public static void ChooseRandomOni()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            Photon.Realtime.Player[] players = PhotonNetwork.PlayerList;
            if (players.Length == 0) return;

            SetOni(players[UnityEngine.Random.Range(0, players.Length)].ActorNumber);
        }
    }
}
