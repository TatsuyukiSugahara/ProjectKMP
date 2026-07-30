using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectKMP.Battle;
using ProjectKMP.Player;
using R3;
using UnityEngine;

namespace ProjectKMP.UI.InGame
{
    /// <summary>
    /// 自分のプレイヤーが生成されるのを待って、HPゲージとリスポーンカウントダウンをつなぐ。
    /// 他プレイヤーのHPは表示しない(自分が所有する PlayerHealth だけを購読する)。
    /// リスポーンしてもプレイヤーのオブジェクトは作り直されないため、購読は一度だけでよい。
    /// カットシーン中(BattlePlayGate が false の間)はHUD全体を隠す。
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

            BindAsync(destroyCancellationToken).Forget();
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

        /// <summary>自分のキャラはネットワーク生成で遅れて現れるので、見つかるまで待ってからつなぐ</summary>
        private async UniTaskVoid BindAsync(CancellationToken ct)
        {
            PlayerHealth local = null;
            while (local == null)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                local = FindLocalPlayerHealth();
            }

            _subscriptions.Add(local.HpChanged.Subscribe(hp =>
            {
                if (_gauge != null) _gauge.SetHealth(hp, local.MaxHp);
            }));

            _subscriptions.Add(local.Died.Subscribe(_ =>
            {
                if (_countdown != null) _countdown.Show(local.RespawnDelaySec);
            }));

            _subscriptions.Add(local.RespawnRemainingSec.Subscribe(sec =>
            {
                if (_countdown != null && local.IsDead) _countdown.UpdateRemaining(sec);
            }));

            _subscriptions.Add(local.Revived.Subscribe(_ =>
            {
                if (_countdown != null) _countdown.Hide();
            }));
        }

        private PlayerHealth FindLocalPlayerHealth()
        {
            foreach (var health in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (health.photonView != null && health.photonView.IsMine) return health;
            }
            return null;
        }
    }
}
