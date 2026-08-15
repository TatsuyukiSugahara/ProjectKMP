using UnityEngine;

namespace ProjectKMP.UI.InGame
{
    /// <summary>
    /// 戦いの画面で必要な表示を用意する。
    ///
    /// これまでは技の側が『合図を出す表示』を作らせていたが、
    /// 遊びの処理が画面の面倒を見るのは筋が違う。
    ///
    /// 画面のことは画面の側でまとめる。技を作り変えても表示は影響を受けず、
    /// 表示を増やしても技のファイルを触らずに済む。
    /// </summary>
    public class InGameHudBootstrap : MonoBehaviour
    {
        private void Start()
        {
            // 合体ビームの呼びかけ。相手が居ないときは自分から隠れる
            FriendBeamSignal.Ensure();

            // 体力が減ったときの赤い縁
            DangerVignette.Ensure();
        }
    }
}
