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

        [Header("とどめの演出")]
        [SerializeField, Tooltip("倒した瞬間をゆっくり見せる")]
        private bool _enableFinishBlow = true;

        [SerializeField, Range(0.05f, 1.0f), Tooltip("どれだけ遅くするか。0.2で5分の1の速さ")]
        private float _finishTimeScale = 0.18f;

        [SerializeField, Min(0.0f), Tooltip("遅くしている時間(秒)。実時間で数える")]
        private float _finishSlowSec = 1.1f;

        [SerializeField, Min(0.0f), Tooltip("元の速さへ戻すのにかける時間(秒)")]
        private float _finishRecoverSec = 0.5f;

        [SerializeField, Min(0.0f), Tooltip("倒した瞬間にカメラを寄せる量(メートル)。0で寄せない")]
        private float _finishCameraPull = 3.0f;

        [SerializeField, Min(0.0f), Tooltip("撃破からゲームクリア表示までの間(秒)。ボスが倒れるのを見せる時間")]
        private float _delayBeforeShowSec = 1.2f;

        [SerializeField, Min(0.0f), Tooltip("ゲームクリアを見せる時間(秒)。この後リザルトへ遷移する")]
        private float _showSeconds = 4.0f;

        [SerializeField, Tooltip("遷移先のリザルトシーン名")]
        private string _resultSceneName = "Result";

        // ---- 内部状態 ------------------------------------

        private System.IDisposable _subscription;
        private System.IDisposable _playableSubscription;
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

            // 戦闘が始まった時刻を控える。カットシーンが明けて操作できるようになった瞬間が開始。
            // 導入前に一度 true になっても、明けたときの呼び出しで正しい時刻に上書きされる
            _playableSubscription = BattlePlayGate.OnChanged.Subscribe(playable =>
            {
                if (playable) ClearTime.Begin();
            });
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
            _playableSubscription?.Dispose();
        }

        // ---- 内部処理 ------------------------------------

        private void OnBossDefeated()
        {
            if (_isFinished) return;
            _isFinished = true;
            FinishAsync(destroyCancellationToken).Forget();
        }

        /// <summary>
        /// 倒した瞬間をゆっくり見せる。
        ///
        /// 決着はギャラリーがいちばん沸く場面なので、そこに一番よい絵を置く。
        /// 速度を落としてカメラを寄せるだけで、同じ動きが見せ場に変わる。
        /// </summary>
        private void PlayFinishBlow(Player.ThirdPersonCamera cameraController)
        {
            if (!_enableFinishBlow) return;

            // ヒットストップと同じ係へ預ける。別々に速度を書くと取り合いになる
            HitStop.Play(_finishSlowSec, _finishTimeScale, _finishRecoverSec);

            // 決着の瞬間はいちばん深く引かせる。静けさが見せ場を作る
            UI.BgmPlayer.Duck(0.75f, _finishSlowSec, 0.8f);

            if (cameraController == null || _finishCameraPull <= 0.0f) return;

            // 寄せたままにする。この後は倒れる演出とクリア表示が続くので、戻す必要がない
            cameraController.SetDistanceOffset(-_finishCameraPull);
        }

        private async UniTaskVoid FinishAsync(CancellationToken ct)
        {
            // 倒した時点でタイムを確定する。操作を止めるより先に測る
            ClearTime.Finish();

            // 操作を止めて戦闘UIを隠す(プレイヤーのHPバーは BattlePlayGate の購読で自動的に消える)
            BattlePlayGate.SetPlayable(false);

            var touch = FindAnyObjectByType<TouchControls>();
            if (touch != null) touch.SetControlsVisible(false);
            UI.InGame.TeamPowerHud.SetVisible(false);

            // 最終フェーズの赤みを消す。決着の絵に赤が乗ったままだと締まらない
            FinalPhaseDirector.End();

            // ターゲットカメラを解く。解かないと、倒したあとも照準がボスに残り続ける
            var cameraController = FindAnyObjectByType<Player.ThirdPersonCamera>();
            if (cameraController != null) cameraController.ReleaseLockOn();

            // 照準は消えるまで少しかかる。この直後に画面を撮るので、待たずにその場で消す
            var lockOnMarker = FindAnyObjectByType<UI.LockOnMarker>();
            if (lockOnMarker != null) lockOnMarker.HideNow();

            var bossGauge = FindAnyObjectByType<BossHealthGauge>();
            if (bossGauge != null) bossGauge.SetVisible(false);

            // ボスを倒れさせる(各クライアントが自分の画面で再生する)
            var gorillaAI = FindAnyObjectByType<GorillaAI>();
            if (gorillaAI != null && !gorillaAI.IsDead) gorillaAI.ChangeState(new GorillaStateDeath());

            PlayFinishBlow(cameraController);

            // 全画面を塗る演出が残っていると、撮った絵が一色に潰れる。
            // 協力技のように決着と同時に光る技だと、これが起きやすい
            UI.ImpactFrame.Clear();

            // UIが消えた状態を1フレーム反映させてから、倒した瞬間の画面を撮っておく(リザルトの背景に使う)
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await UniTask.WaitForEndOfFrame(this, ct);

            // 待っている間に新しく光ることがあるので、撮る直前にもう一度消す
            UI.ImpactFrame.Clear();
            GameClearSnapshot.Set(ScreenCapture.CaptureScreenshotAsTexture());

            await UniTask.Delay(TimeSpan.FromSeconds(_delayBeforeShowSec), cancellationToken: ct);

            if (_ui != null) await _ui.ShowAsync(ct);

            await UniTask.Delay(TimeSpan.FromSeconds(_showSeconds), cancellationToken: ct);

            // 全クライアントが自分の画面を黒にしてから遷移する。ゲストは暗転のままマスターの遷移を待つので、
            // 切り替わりの瞬間が見えず、リザルト側のフェードインへ自然につながる
            if (_ui != null) await _ui.FadeOutAsync(ct);

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
