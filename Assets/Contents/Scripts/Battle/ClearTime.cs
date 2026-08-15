using Photon.Pun;
using ProjectKMP.Core;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// ボスを倒すまでにかかった時間を測って持ち回る。
    /// 開始時刻とクリアタイムは Room の CustomProperties に置くので、全員が同じ値を見られて、
    /// リザルトシーンへ移っても残る(BattleClock と同じ方式)。書き込むのは MasterClient だけ。
    /// ルームに入っていない動作確認時は、このクライアント限りで測る。
    /// </summary>
    public static class ClearTime
    {
        // ---- 定数 ----------------------------------------

        /// <summary>戦闘が始まった時刻(PhotonNetwork.Time)</summary>
        public const string KEY_START = "cts";

        /// <summary>確定したクリアタイム(秒)</summary>
        public const string KEY_CLEAR = "ctc";

        // ---- 内部状態 ------------------------------------

        private static double _offlineStartTime = -1.0;
        private static double _offlineClearSec = -1.0;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 戦闘の開始時刻を控える。カットシーンが明けて操作できるようになるたびに呼ばれ、
        /// 最後の呼び出しが有効になる(導入中に一度 true になっても正しい開始時刻で上書きされる)。
        /// </summary>
        public static void Begin()
        {
            if (IsInRoom)
            {
                if (!PhotonNetwork.IsMasterClient) return;

                PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
                {
                    { KEY_START, PhotonNetwork.Time },
                    { KEY_CLEAR, -1.0 },
                });
                return;
            }

            _offlineStartTime = Time.realtimeSinceStartupAsDouble;
            _offlineClearSec = -1.0;
        }

        /// <summary>ボスを倒した時点でタイムを確定する</summary>
        public static void Finish()
        {
            if (IsInRoom)
            {
                if (!PhotonNetwork.IsMasterClient) return;
                if (!TryGetStartTime(out double start)) return;

                double elapsed = PhotonNetwork.Time - start;
                if (elapsed < 0.0) return;

                PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KEY_CLEAR, elapsed } });
                return;
            }

            if (_offlineStartTime < 0.0) return;
            _offlineClearSec = Time.realtimeSinceStartupAsDouble - _offlineStartTime;
        }

        /// <summary>確定したクリアタイム(秒)。まだ記録が無ければ負の値</summary>
        public static double GetClearSeconds()
        {
            if (!IsInRoom) return _offlineClearSec;

            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KEY_CLEAR, out object value)
                && value is double seconds ? seconds : -1.0;
        }

        /// <summary>1:23.45 の形にする。記録が無ければダッシュ表示</summary>
        public static string Format(double seconds)
        {
            // 見せ方の計算は通信と関係がないので、切り出した側へ任せる。
            // そちらは部屋へ入らなくても正しさを確かめられる
            return ClearTimeText.Format(seconds);
        }

        // ---- 内部処理 ------------------------------------

        private static bool IsInRoom => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null;

        private static bool TryGetStartTime(out double startTime)
        {
            startTime = 0.0;
            if (!IsInRoom) return false;

            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KEY_START, out object value)
                && value is double time && (startTime = time) != 0.0;
        }
    }
}
