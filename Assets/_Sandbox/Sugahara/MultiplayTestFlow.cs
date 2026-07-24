using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using ProjectKMP.Battle;
using ProjectKMP.Player;
using ProjectKMP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Sandbox
{
    /// <summary>
    /// 通信テスト用のフロー制御。
    /// 接続 → ランダム入室 → 待機 → ホストがSTART → バトル(2分) → リザルト → 再戦 or ロビーへ。
    /// 開始と残り時間は Room の CustomProperties で共有し、各クライアントが自分で判定する。
    /// </summary>
    public class MultiplayTestFlow : MonoBehaviourPunCallbacks
    {
        private enum FlowState { Connecting, Lobby, Battle, Result }

        // ---- 参照(ロビー) --------------------------------
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private PlayerSpawner  _playerSpawner;
        [SerializeField] private GameObject     _lobbyPanel;
        [SerializeField] private TMP_Text       _statusText;
        [SerializeField] private TMP_Text       _memberText;
        [SerializeField] private Button         _startButton;

        // ---- 参照(バトル) --------------------------------
        [SerializeField] private GameObject _battleHud;
        [SerializeField] private TMP_Text   _timerText;
        [SerializeField] private TMP_Text   _scoreText;

        // ---- 参照(リザルト) ------------------------------
        [SerializeField] private GameObject _resultPanel;
        [SerializeField] private TMP_Text   _resultText;
        [SerializeField] private Button     _rematchButton;
        [SerializeField] private Button     _leaveButton;

        // ---- 内部状態 ------------------------------------
        private FlowState _state = FlowState.Connecting;

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            _startButton.onClick.AddListener(OnClickStart);
            _rematchButton.onClick.AddListener(OnClickRematch);
            _leaveButton.onClick.AddListener(OnClickLeave);

            ShowOnly(FlowState.Connecting);
            RunFlowAsync(destroyCancellationToken).Forget();
        }

        private void Update()
        {
            if (_state != FlowState.Battle) return;

            double remaining = BattleClock.GetRemainingSeconds();
            _timerText.text = FormatTime(remaining);
            _scoreText.text = BuildLocalScoreText();

            if (remaining <= 0.0) EnterResult();
        }

        // ---- フロー --------------------------------------

        private async UniTaskVoid RunFlowAsync(CancellationToken ct)
        {
            SetStatus("Connecting to Photon...");
            ConnectResult connectResult = await _networkManager.ConnectAndJoinLobbyAsync(ct);
            if (connectResult != ConnectResult.Success)
            {
                SetStatus($"Connect failed : {connectResult}");
                return;
            }

            await JoinRoomAsync(ct);
        }

        private async UniTask JoinRoomAsync(CancellationToken ct)
        {
            SetStatus("Matching...");
            bool joined = await _networkManager.JoinOrCreateRoomAsync(ct);
            if (!joined)
            {
                SetStatus("Failed to join room");
                return;
            }

            EnterLobby();

            // 進行中の試合に後から入った場合はそのままバトルへ
            if (BattleClock.IsRunning) EnterBattle();
        }

        private void EnterLobby()
        {
            ShowOnly(FlowState.Lobby);
            SetStatus("Waiting for players");
            RefreshLobbyView();
        }

        private void EnterBattle()
        {
            if (_state == FlowState.Battle) return;

            ShowOnly(FlowState.Battle);
            SetStatus("Battle!  Move: WASD / Stick    Fire: Space / R1");

            BattleScore.ResetLocal();          // 前の試合や前の部屋のスコアを引き継がない
            _playerSpawner.SpawnLocalPlayer(); // 2回目以降は既存のキャラをそのまま使う

            // タッチ端末でのみ表示される
            ServiceLocator.TryGet<VirtualStick>()?.SetVisible(true);
            ServiceLocator.TryGet<FireButton>()?.SetVisible(true);
        }

        private void EnterResult()
        {
            if (_state == FlowState.Result) return;

            ShowOnly(FlowState.Result);
            SetStatus("Result");
            RefreshResultView();

            ServiceLocator.TryGet<VirtualStick>()?.SetVisible(false);
            ServiceLocator.TryGet<FireButton>()?.SetVisible(false);
        }

        // ---- ボタン --------------------------------------

        private void OnClickStart()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            _startButton.interactable = false;
            _networkManager.CloseRoom();
            BattleClock.StartNewRound();
        }

        private void OnClickRematch()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            _rematchButton.interactable = false;
            BattleClock.StartNewRound();
        }

        private void OnClickLeave()
        {
            _rematchButton.interactable = false;
            _leaveButton.interactable = false;
            SetStatus("Leaving room...");
            PhotonNetwork.LeaveRoom();
        }

        // ---- 表示更新 ------------------------------------

        /// <summary>状態に応じてパネルの出し分けをまとめて行う</summary>
        private void ShowOnly(FlowState state)
        {
            _state = state;

            _lobbyPanel.SetActive(state == FlowState.Lobby);
            _battleHud.SetActive(state == FlowState.Battle);
            _resultPanel.SetActive(state == FlowState.Result);
        }

        private void RefreshLobbyView()
        {
            if (!PhotonNetwork.InRoom || _state != FlowState.Lobby) return;

            var room = PhotonNetwork.CurrentRoom;
            string hostMark = PhotonNetwork.IsMasterClient ? "  (Host)" : "";
            _memberText.text =
                $"Room : {room.Name}\n" +
                $"Players : {room.PlayerCount} / {(int)room.MaxPlayers}\n" +
                $"You : Player {PhotonNetwork.LocalPlayer.ActorNumber}{hostMark}";

            bool canStart = PhotonNetwork.IsMasterClient;
            _startButton.gameObject.SetActive(canStart);
            _startButton.interactable = canStart;
        }

        private string BuildLocalScoreText()
        {
            var me = PhotonNetwork.LocalPlayer;
            return $"Kills {BattleScore.GetKills(me)}    Deaths {BattleScore.GetDeaths(me)}";
        }

        private void RefreshResultView()
        {
            if (_state != FlowState.Result || !PhotonNetwork.InRoom) return;

            // 撃破数の多い順、同数なら死亡数の少ない順
            List<Photon.Realtime.Player> ranking = PhotonNetwork.PlayerList
                .OrderByDescending(BattleScore.GetKills)
                .ThenBy(BattleScore.GetDeaths)
                .ToList();

            var text = new System.Text.StringBuilder();
            text.AppendLine($"=== RESULT  (Round {BattleClock.GetRound()}) ===");
            text.AppendLine();

            for (int i = 0; i < ranking.Count; i++)
            {
                Photon.Realtime.Player player = ranking[i];
                string youMark = player.IsLocal ? "  <- YOU" : "";
                text.AppendLine($"{i + 1}.  Player {player.ActorNumber}" +
                                $"    Kills {BattleScore.GetKills(player)}" +
                                $"    Deaths {BattleScore.GetDeaths(player)}{youMark}");
            }

            text.AppendLine();
            text.AppendLine($"MVP (Most Kills)  : {DescribeWinners(ranking, BattleScore.GetKills, true)}");
            text.AppendLine($"Fewest Deaths     : {DescribeWinners(ranking, BattleScore.GetDeaths, false)}");

            _resultText.text = text.ToString();

            _rematchButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
            _rematchButton.interactable = PhotonNetwork.IsMasterClient;
            _leaveButton.interactable = true;
        }

        /// <summary>最大(または最小)の該当者を列挙する。同点なら全員並べる</summary>
        private string DescribeWinners(
            List<Photon.Realtime.Player> players,
            Func<Photon.Realtime.Player, int> selector,
            bool wantsMax)
        {
            if (players.Count == 0) return "-";

            int best = wantsMax ? players.Max(selector) : players.Min(selector);
            IEnumerable<string> names = players
                .Where(p => selector(p) == best)
                .Select(p => $"Player {p.ActorNumber}");

            return string.Join(", ", names) + $"  ({best})";
        }

        private void SetStatus(string message)
        {
            _statusText.text = message;
            Debug.Log($"[TestFlow] {message}");
        }

        private static string FormatTime(double seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return $"{total / 60:0}:{total % 60:00}";
        }

        // ---- Photon コールバック --------------------------

        public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer) => RefreshLobbyView();

        public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
        {
            RefreshLobbyView();
            RefreshResultView();
        }

        public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
        {
            RefreshLobbyView();
            RefreshResultView();
        }

        public override void OnPlayerPropertiesUpdate(
            Photon.Realtime.Player targetPlayer, Hashtable changedProps)
        {
            RefreshResultView();
        }

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            // 開始時刻が更新された = 新しいラウンドの開始(初戦・再戦とも)
            if (propertiesThatChanged.ContainsKey(BattleClock.KEY_START_TIME))
            {
                EnterBattle();
            }
        }

        public override void OnLeftRoom()
        {
            ShowOnly(FlowState.Connecting);
            JoinRoomAsync(destroyCancellationToken).Forget();
        }
    }
}
