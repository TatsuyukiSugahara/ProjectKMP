using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 試合の開始時刻を Room の CustomProperties に置き、各クライアントが残り時間を自分で計算する。
    /// PhotonNetwork.Time はサーバ同期された時刻なので、全員が同じ残り時間になる。
    /// </summary>
    public static class BattleClock
    {
        // ---- 定数 ----------------------------------------
        public const string KEY_START_TIME = "bst";
        public const string KEY_ROUND      = "rnd";
        public const double DEFAULT_DURATION_SEC = 120.0;

        // ---- 内部状態 ------------------------------------
        // ゲームモードごとに試合時間が違うため、シーン側から差し替えられるようにする
        private static double _durationSec = DEFAULT_DURATION_SEC;

        // ---- 公開API -------------------------------------

        /// <summary>現在の試合時間(秒)</summary>
        public static double DurationSec => _durationSec;

        /// <summary>試合時間を設定する。シーンの初期化時に呼ぶ</summary>
        public static void SetDurationSec(double seconds)
        {
            _durationSec = seconds > 0.0 ? seconds : DEFAULT_DURATION_SEC;
        }

        /// <summary>試合を開始する。MasterClient だけが呼ぶこと</summary>
        public static void StartNewRound()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                { KEY_START_TIME, PhotonNetwork.Time },
                { KEY_ROUND,      GetRound() + 1 },
            });
        }

        /// <summary>残り秒数。未開始なら試合時間そのものを返す</summary>
        public static double GetRemainingSeconds()
        {
            if (!TryGetStartTime(out double startTime)) return _durationSec;

            double remaining = _durationSec - (PhotonNetwork.Time - startTime);
            return remaining > 0.0 ? remaining : 0.0;
        }

        /// <summary>試合中かどうか</summary>
        public static bool IsRunning => TryGetStartTime(out _) && GetRemainingSeconds() > 0.0;

        /// <summary>何戦目か。未開始は0</summary>
        public static int GetRound()
        {
            if (!PhotonNetwork.InRoom) return 0;
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KEY_ROUND, out object value)
                && value is int round ? round : 0;
        }

        // ---- 内部処理 ------------------------------------

        private static bool TryGetStartTime(out double startTime)
        {
            startTime = 0.0;
            if (!PhotonNetwork.InRoom) return false;

            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KEY_START_TIME, out object value)
                && value is double time && (startTime = time) != 0.0;
        }
    }
}
