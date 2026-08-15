using System.Collections.Generic;
using ProjectKMP.Core;
using R3;
using UnityEngine;

namespace ProjectKMP.UI.Presenters
{
    /// <summary>
    /// 状態と通常攻撃のボタンをつなぐ。
    ///
    /// 待ち時間に加えて『押しどきの受付中か』も渡す。
    /// ボタンはその2つで色と暗さを決めるだけでよくなる。
    /// </summary>
    public class AttackButtonPresenter : MonoBehaviour
    {
        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("値を渡す先のボタン。未設定なら同じ物から探す")]
        private AttackButton _button;

        // ---- 内部状態 ------------------------------------

        private readonly List<System.IDisposable> _subscriptions = new List<System.IDisposable>();

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_button == null) _button = GetComponent<AttackButton>();
        }

        private void OnEnable()
        {
            if (_button == null) return;

            PlayerStatus status = PlayerStatusHub.Local;

            _subscriptions.Add(status.AttackCooldown01.Subscribe(ratio => _button.SetCooldownRatio(ratio)));
            _subscriptions.Add(status.IsInJustWindow.Subscribe(inWindow => _button.SetJustWindow(inWindow)));
        }

        private void Update()
        {
            if (_button == null) return;

            // 押している状態は入力の読み取り口が持っている。
            // 指・キー・パッドのどれで押しても同じ動きになる
            _button.SetPressed(Core.GameInput.AttackHeld);
        }

        private void OnDisable()
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
        }
    }
}
