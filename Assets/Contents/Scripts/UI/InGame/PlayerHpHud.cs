using System.Collections.Generic;
using ProjectKMP.Battle;
using ProjectKMP.Core;
using R3;
using UnityEngine;

namespace ProjectKMP.UI.InGame
{
    /// <summary>
    /// 自分のHPと、倒れたときの復活待ちを表示する。
    ///
    /// プレイヤーの部品を探しに行かず、用意された状態だけを見る。
    /// ネットワークで遅れて生まれる相手を待つ必要がなく、
    /// 遊びの処理が変わっても、この表示は影響を受けない。
    ///
    /// カットシーン中は表示ごと隠す。
    /// </summary>
    public class PlayerHpHud : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("HUD全体の表示・非表示に使う CanvasGroup。カットシーン中に隠す")]
        private CanvasGroup _group;

        [SerializeField, Tooltip("HPバー表示")]
        private PlayerHpGauge _gauge;

        [SerializeField, Tooltip("リスポーンカウントダウン表示")]
        private RespawnCountdownView _countdown;

        // ---- 内部状態 ------------------------------------

        private readonly List<System.IDisposable> _subscriptions = new List<System.IDisposable>();

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            if (_countdown != null) _countdown.Hide();

            // カットシーン中はHUDごと隠す(購読時に現在値も流れるので初期状態もそろう)
            _subscriptions.Add(BattlePlayGate.OnChanged.Subscribe(SetVisible));

            Bind();
        }

        private void OnDestroy()
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
        }

        // ---- 内部処理 ------------------------------------

        private void SetVisible(bool visible)
        {
            if (_group == null) return;

            _group.alpha = visible ? 1.0f : 0.0f;
        }

        /// <summary>
        /// 状態の変化を受け取るようにする。
        /// 待つ必要が無いので、その場でつなげてしまえる。
        /// </summary>
        private void Bind()
        {
            PlayerStatus status = PlayerStatusHub.Local;

            // 体力は現在値と上限のどちらが変わっても描き直す
            _subscriptions.Add(status.CurrentHp.Subscribe(hp => ApplyHp(hp, status.MaxHp.CurrentValue)));
            _subscriptions.Add(status.MaxHp.Subscribe(max => ApplyHp(status.CurrentHp.CurrentValue, max)));

            // 倒れた瞬間に数え始め、起き上がったら片付ける
            _subscriptions.Add(status.IsDead.Subscribe(dead => ApplyDead(dead, status.RespawnDelaySec.CurrentValue)));

            _subscriptions.Add(status.RespawnRemainingSec.Subscribe(sec =>
            {
                if (_countdown == null || !status.IsDead.CurrentValue) return;

                _countdown.UpdateRemaining(sec);
            }));
        }

        private void ApplyHp(int current, int max)
        {
            // 上限が入る前は描かない。0/0 が一瞬映るのを避ける
            if (_gauge == null || max <= 0) return;

            _gauge.SetHealth(current, max);
        }

        private void ApplyDead(bool dead, float delaySec)
        {
            if (_countdown == null) return;

            if (dead) _countdown.Show(delaySec);
            else _countdown.Hide();
        }
    }
}
