using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using ProjectKMP.Gorilla;
using ProjectKMP.Monster;
using ProjectKMP.UI;
using ProjectKMP.UI.InGame;
using R3;
using UnityEngine;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// ボスのHPが0になったときの流れを進める。
    /// 操作を止めてボスを倒れさせ、「ゲームクリア」をインゲームに表示し、数秒見せてからリザルトへ遷移する。
    /// 撃破はボスのHP同期(SyncObject)経由で全クライアントへ届くため、表示は各クライアントが自分で行う。
    /// シーン遷移は MasterClient が PhotonNetwork.LoadLevel で行えば、AutomaticallySyncScene で全員が追従する
    /// (ロビー→インゲームと同じ方式)。
    /// </summary>
    public class GameClearDirector : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("「ゲームクリア」表示。未設定ならシーンから探す")]
        private GameClearUI _ui;

        [SerializeField, Min(0.0f), Tooltip("撃破からゲームクリア表示までの間(秒)。ボスが倒れるのを見せる時間")]
        private float _delayBeforeShowSec = 1.2f;

        [SerializeField, Min(0.0f), Tooltip("ゲームクリアを見せる時間(秒)。この後リザルトへ遷移する")]
        private float _showSeconds = 4.0f;

        [SerializeField, Tooltip("遷移先のリザルトシーン名")]
        private string _resultSceneName = "Result";

        // ---- 内部状態 ------------------------------------

        private System.IDisposable _subscription;
        private bool _isFinished;

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            if (_ui == null) _ui = FindAnyObjectByType<GameClearUI>(FindObjectsInactive.Include);
            if (_ui != null) _ui.Hide();

            var boss = FindAnyObjectByType<BossHealth>();
            if (boss == null)
            {
                Debug.LogWarning("[Battle] BossHealth が見つからないため、ゲームクリアは動きません", this);
                return;
            }

            _subscription = boss.Defeated.Subscribe(_ => OnBossDefeated());
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
        }

        // ---- 内部処理 ------------------------------------

        private void OnBossDefeated()
        {
            if (_isFinished) return;
            _isFinished = true;
            FinishAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid FinishAsync(CancellationToken ct)
        {
            // 操作を止めて戦闘UIを隠す(プレイヤーのHPバーは BattlePlayGate の購読で自動的に消える)
            BattlePlayGate.SetPlayable(false);

            var touch = FindAnyObjectByType<TouchControls>();
            if (touch != null) touch.SetControlsVisible(false);

            var bossGauge = FindAnyObjectByType<BossHealthGauge>();
            if (bossGauge != null) bossGauge.SetVisible(false);

            // ボスを倒れさせる(各クライアントが自分の画面で再生する)
            var gorillaAI = FindAnyObjectByType<GorillaAI>();
            if (gorillaAI != null && !gorillaAI.IsDead) gorillaAI.ChangeState(new GorillaStateDeath());

            await UniTask.Delay(TimeSpan.FromSeconds(_delayBeforeShowSec), cancellationToken: ct);

            if (_ui != null) await _ui.ShowAsync(ct);

            await UniTask.Delay(TimeSpan.FromSeconds(_showSeconds), cancellationToken: ct);

            // ルームに入っていればマスターの遷移に全員が追従する。オフライン確認時は自分で遷移する
            if (PhotonNetwork.InRoom)
            {
                if (PhotonNetwork.IsMasterClient) PhotonNetwork.LoadLevel(_resultSceneName);
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(_resultSceneName);
            }
        }
    }
}
