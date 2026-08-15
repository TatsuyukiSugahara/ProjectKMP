using System.Collections.Generic;
using ProjectKMP.Core;
using ProjectKMP.UI.InGame;
using R3;
using UnityEngine;

namespace ProjectKMP.UI.Presenters
{
    /// <summary>
    /// 狙っている相手を、印とボタンの見た目へ渡す。
    ///
    /// 印は『誰を囲むか』、ボタンは『入っているかどうか』を必要としている。
    /// 元は同じ1つの状態なので、まとめて配る役をここに置く。
    /// </summary>
    public class LockOnPresenter : MonoBehaviour
    {
        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("相手を囲む印。未設定なら探す")]
        private LockOnMarker _marker;

        [SerializeField, Tooltip("ターゲットボタンの見た目。未設定なら探す")]
        private TargetButtonVisual _buttonVisual;

        // ---- 内部状態 ------------------------------------

        private readonly List<System.IDisposable> _subscriptions = new List<System.IDisposable>();

        // ---- 内部処理 ------------------------------------

        private void OnEnable()
        {
            _subscriptions.Add(PlayerStatusHub.Local.LockTarget.Subscribe(Apply));
        }

        private void OnDisable()
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
        }

        private void Apply(Transform target)
        {
            // 印は場面に途中から現れることがあるので、無ければ探し直す
            if (_marker == null) _marker = FindAnyObjectByType<LockOnMarker>(FindObjectsInactive.Include);
            if (_buttonVisual == null) _buttonVisual = FindAnyObjectByType<TargetButtonVisual>(FindObjectsInactive.Include);

            if (_marker != null) _marker.SetTarget(target);
            if (_buttonVisual != null) _buttonVisual.SetLockedOn(target != null);
        }
    }
}
