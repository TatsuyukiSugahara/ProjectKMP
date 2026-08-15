using System.Collections.Generic;
using ProjectKMP.Core;
using ProjectKMP.UI.InGame;
using R3;
using UnityEngine;

namespace ProjectKMP.UI.Presenters
{
    /// <summary>
    /// 合体ビームの呼びかけの相手を、合図の表示へ渡す。
    ///
    /// 誰に呼びかけているかは技の側が決めている。
    /// 合図の側は受け取って出すだけでよい。
    /// </summary>
    public class FriendBeamSignalPresenter : MonoBehaviour
    {
        // ---- 内部状態 ------------------------------------

        private readonly List<System.IDisposable> _subscriptions = new List<System.IDisposable>();

        // ---- 内部処理 ------------------------------------

        private void OnEnable()
        {
            _subscriptions.Add(PlayerStatusHub.Local.FriendBeamCallTarget.Subscribe(Apply));
        }

        private void OnDisable()
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
        }

        private static void Apply(Transform target)
        {
            // 合図は必要になったときに自分で現れるので、無ければ何もしない
            FriendBeamSignal signal = FriendBeamSignal.Instance;
            if (signal == null) return;

            signal.SetTarget(target);
        }
    }
}
