using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using ProjectKMP.Battle;
using ProjectKMP.Player;
using ProjectKMP.Tag;
using ProjectKMP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Sandbox
{
    /// <summary>
    /// 鬼ごっこテスト用のフロー制御。
    /// 接続 → ランダム入室 → 待機 → ホストがSTART → 鬼ごっこ(90秒) → リザルト → 再戦 or ロビーへ。
    /// 残り時間と現在の鬼は Room の CustomProperties で共有し、各クライアントが自分で判定する。
    /// </summary>
    public class TagGameFlow : MonoBehaviourPunCallbacks
    {
        private enum FlowState { Connecting, Lobby, Playing, Result }

        // ---- 定数 ----------------------------------------
        private const double ROUND_DURATION_SEC = 90.0;

        // ---- 参照(ロビー) --------------------------------
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private PlayerSpawner  _playerSpawner;
        [SerializeField] private GameObject     _lobbyPanel;
        [SerializeField] private TMP_Text       _statusText;
        [SerializeField] private TMP_Text       _memberText;
        [SerializeField] private Button         _startButton;

        // ---- 参照(ゲーム中) ------------------------------
        [SerializeField] private GameObject _gameHud;
        [SerializeField] private TMP_Text   _timerText;
        [SerializeField] private TMP_Text   _oniText;

        // ---- 参照(リザルト) ------------------------------
        [SerializeField] private GameObject _resultPanel;
        [SerializeField] private TMP_Text   _resultText;
        [SerializeField] private Button     _rematchButton;
        [SerializeField] private Button     _leaveButton;

        // ---- 内部状態 ------------------------------------
        private FlowState _state = FlowState.Connecting;
        private int _lastOniActorNumber = -1;

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            // 鬼ごっこは90秒。共通のタイマーにこのシーンの試合時間を教える
            BattleClock.SetDurationSec(ROUND_DURATION_SEC);

            _startButton.onClick.AddListener(OnClickStart);
            _rematchButton.onClick.AddListener(OnClickRematch);
            _leaveButton.onClick.AddListener(OnClickLeave);

            ShowOnly(FlowState.Connecting);
            RunFlowAsync(destroyCancellationToken).Forget();
        }

        private void Update()
        {
            if (_state != FlowState.Playing) return;

            double remaining = BattleClock.GetRemainingSeconds();
            _timerText.text = FormatTime(remaining);
            _oniText.text = BuildOniText();

            // 誰が最後に鬼だったかはリザルトで使うので覚えておく
            int oniActorNumber = TagState.GetOniActorNumber();
            if (oniActorNumber >= 0) _lastOniActorNumber = oniActorNumber;

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

            // 進行中の試合に後から入った場合はそのまま参加させる
            if (BattleClock.IsRunning) EnterPlaying();
        }

        private void EnterLobby()
        {
            ShowOnly(FlowState.Lobby);
            SetStatus("Waiting for players");
            RefreshLobbyView();
        }

        private void EnterPlaying()
        {
            if (_state == FlowState.Playing) return;

            ShowOnly(FlowState.Playing);
            SetStatus("Tag!  Move: WASD / Stick");

            _lastOniActorNumber = -1;
            _playerSpawner.SpawnLocalPlayer(); // 2回目以降は既存のキャラをそのまま使う

            // タッチ端末でのみ表示される
            ServiceLocator.TryGet<VirtualStick>()?.SetVisible(true);
        }

        private void EnterResult()
        {
            if (_state == FlowState.Result) return;

            ShowOnly(FlowState.Result);
            SetStatus("Result");
            RefreshResultView();

            ServiceLocator.TryGet<VirtualStick>()?.SetVisible(false);
        }

        // ---- ボタン --------------------------------------

        private void OnClickStart()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            _startButton.interactable = false;
            _networkManager.CloseRoom();
            StartRound();
        }

        private void OnClickRematch()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            _rematchButton.interactable = false;
            StartRound();
        }

        private void OnClickLeave()
        {
            _rematchButton.interactable = false;
            _leaveButton.interactable = false;
            SetStatus("Leaving room...");
            PhotonNetwork.LeaveRoom();
        }

        /// <summary>記録を消してから時間と鬼を決める。この順でないと初回の鬼の記録が消える</summary>
        private void StartRound()
        {
            TagScore.ResetAll();
            BattleClock.StartNewRound();
            TagState.ChooseRandomOni();
        }

        // ---- 表示更新 ------------------------------------

        /// <summary>状態に応じてパネルの出し分けをまとめて行う</summary>
        private void ShowOnly(FlowState state)
        {
            _state = state;

            _lobbyPanel.SetActive(state == FlowState.Lobby);
            _gameHud.SetActive(state == FlowState.Playing);
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

        /// <summary>自分が鬼かどうかを一番大きく見せる</summary>
        private string BuildOniText()
        {
            int oniActorNumber = TagState.GetOniActorNumber();
            if (oniActorNumber < 0) return "Choosing ONI...";

            if (TagState.IsLocalOni)
            {
                return TagState.IsTagReady()
                    ? "YOU ARE ONI !   Chase them!"
                    : "YOU ARE ONI !   (wait...)";
            }

            return $"Run away!   ONI : Player {oniActorNumber}";
        }

        private void RefreshResultView()
        {
            if (_state != FlowState.Result || !PhotonNetwork.InRoom) return;

            // 鬼にされた回数の少ない順。同数なら ActorNumber 順で安定させる
            List<Photon.Realtime.Player> ranking = PhotonNetwork.PlayerList
                .OrderBy(TagScore.GetOniCount)
                .ThenBy(player => player.ActorNumber)
                .ToList();

            var text = new StringBuilder();
            text.AppendLine($"=== RESULT  (Round {BattleClock.GetRound()}) ===");
            text.AppendLine();

            for (int i = 0; i < ranking.Count; i++)
            {
                Photon.Realtime.Player player = ranking[i];
                string youMark = player.IsLocal ? "  <- YOU" : "";
                string oniMark = player.ActorNumber == _lastOniActorNumber ? "   [ONI at the end]" : "";
                text.AppendLine($"{i + 1}.  Player {player.ActorNumber}" +
                                $"    Tagged {TagScore.GetOniCount(player)}{oniMark}{youMark}");
            }

            text.AppendLine();
            text.AppendLine(_lastOniActorNumber >= 0
                ? $"LOSER : Player {_lastOniActorNumber}  (ONI when time ran out)"
                : "LOSER : -");
            text.AppendLine($"Never Tagged : {DescribeCleanPlayers(ranking)}");

            _resultText.text = text.ToString();

            _rematchButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
            _rematchButton.interactable = PhotonNetwork.IsMasterClient;
            _leaveButton.interactable = true;
        }

        /// <summary>一度も鬼にされなかった人を並べる</summary>
        private static string DescribeCleanPlayers(List<Photon.Realtime.Player> players)
        {
            IEnumerable<string> names = players
                .Where(player => TagScore.GetOniCount(player) == 0)
                .Select(player => $"Player {player.ActorNumber}");

            string joined = string.Join(", ", names);
            return string.IsNullOrEmpty(joined) ? "-" : joined;
        }

        private void SetStatus(string message)
        {
            _statusText.text = message;
            Debug.Log($"[TagFlow] {message}");
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
                EnterPlaying();
            }
        }

        public override void OnLeftRoom()
        {
            ShowOnly(FlowState.Connecting);
            JoinRoomAsync(destroyCancellationToken).Forget();
        }
    }
}
