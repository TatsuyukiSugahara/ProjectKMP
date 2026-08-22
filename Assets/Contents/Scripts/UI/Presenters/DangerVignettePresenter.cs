using System.Collections.Generic;
using ProjectKMP.Battle;
using ProjectKMP.Core;
using ProjectKMP.UI.InGame;
using R3;
using UnityEngine;

namespace ProjectKMP.UI.Presenters
{
    /// <summary>
    /// 体力からピンチの度合いを求めて、赤い縁へ渡す。
    ///
    /// 『どれくらい減ったら危ないか』は見せ方の判断なので、ここで決める。
    /// 縁の側は渡された値の濃さで塗るだけにしてある。
    /// </summary>
    public class DangerVignettePresenter : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>この割合を下回ったら出しはじめる</summary>
        private const float THRESHOLD = 0.5f;

        // ---- 内部状態 ------------------------------------

        private readonly List<System.IDisposable> _subscriptions = new List<System.IDisposable>();

        // ---- 内部処理 ------------------------------------

        private void OnEnable()
        {
            PlayerStatus status = PlayerStatusHub.Local;

            // 体力と生死のどちらが変わっても見直す
            _subscriptions.Add(status.CurrentHp.Subscribe(_ => Apply()));
            _subscriptions.Add(status.MaxHp.Subscribe(_ => Apply()));
            _subscriptions.Add(status.IsDead.Subscribe(_ => Apply()));

            // 操作できない間は出さない。カットシーンや決着の絵に赤が乗っていると締まらない
            _subscriptions.Add(BattlePlayGate.OnChanged.Subscribe(_ => Apply()));
        }

        private void OnDisable()
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();

            // 縁はシーンをまたいで生き残るので、渡す側が居なくなるときに消しておく。
            // これをしないと最後に渡した濃さのまま、次のシーンでも出たままになる
            DangerVignette.Clear();
        }

        private void Apply()
        {
            DangerVignette vignette = DangerVignette.Instance;
            if (vignette == null) return;

            vignette.SetDanger(ResolveDanger());
        }

        /// <summary>いまの危なさ。0で安全、1で瀕死</summary>
        private static float ResolveDanger()
        {
            PlayerStatus status = PlayerStatusHub.Local;

            // 死んでいる間は出さない。倒れた画面を赤く塗っても意味がない
            if (status.IsDead.CurrentValue) return 0.0f;

            // 操作を止めている間も出さない。戦っていないのに急かす必要はない
            if (!BattlePlayGate.IsPlayable) return 0.0f;
            if (status.MaxHp.CurrentValue <= 0) return 0.0f;

            float ratio = status.HpRatio01;
            if (ratio >= THRESHOLD) return 0.0f;

            return 1.0f - ratio / THRESHOLD;
        }
    }
}
