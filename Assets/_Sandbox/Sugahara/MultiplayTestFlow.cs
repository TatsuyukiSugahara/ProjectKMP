using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using ProjectKMP.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Sandbox
{
    /// <summary>
    /// 通信テスト用のフロー制御。
    /// 接続 → ランダム入室 → 待機(参加人数を表示) → ホストがSTART → インゲーム(移動)。
    /// 開始通知は Room の CustomProperties で全員に配る(独自RPCは使わない)。
    /// </summary>
    public class MultiplayTestFlow : MonoBehaviourPunCallbacks
    {
        // ---- 定数 ----------------------------------------
        private const string KEY_STARTED = "st";

        // ---- 参照 ----------------------------------------
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private PlayerSpawner  _playerSpawner;
        [SerializeField] private GameObject     _lobbyPanel;
        [SerializeField] private TMP_Text       _statusText;
        [SerializeField] private TMP_Text       _memberText;
        [SerializeField] private Button         _startButton;

        // ---- 内部状態 ------------------------------------
        private bool _isInGame;

        private void Start()
        {
            _startButton.onClick.AddListener(OnClickStart);
            _startButton.gameObject.SetActive(false);
            _lobbyPanel.SetActive(true);

            RunFlowAsync(destroyCancellationToken).Forget();
        }

        // ---- フロー --------------------------------------

        /// <summary>接続からルーム待機までを順に実行する</summary>
        private async UniTaskVoid RunFlowAsync(CancellationToken ct)
        {
            SetStatus("Connecting to Photon...");
            ConnectResult result = await _networkManager.ConnectAndJoinLobbyAsync(ct);
            if (result != ConnectResult.Success)
            {
                SetStatus($"Connect failed : {result}");
                return;
            }

            SetStatus("Matching...");
            bool joined = await _networkManager.JoinOrCreateRoomAsync(ct);
            if (!joined)
            {
                SetStatus("Failed to join room");
                return;
            }

            SetStatus("Waiting for players");
            RefreshRoomView();

            // 開始済みのルームに後から入った場合は待たずにインゲームへ
            if (IsRoomStarted()) EnterInGame();
        }

        /// <summary>ホストが押す。新規参加を締め切って全員に開始を通知する</summary>
        private void OnClickStart()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            _startButton.interactable = false;
            _networkManager.CloseRoom();
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KEY_STARTED, true } });
        }

        private void EnterInGame()
        {
            if (_isInGame) return;
            _isInGame = true;

            _lobbyPanel.SetActive(false);
            SetStatus("In Game :  WASD / Left Stick to move");
            _playerSpawner.SpawnLocalPlayer();
        }

        // ---- 表示更新 ------------------------------------

        private void RefreshRoomView()
        {
            if (!PhotonNetwork.InRoom) return;

            var room = PhotonNetwork.CurrentRoom;
            string hostMark = PhotonNetwork.IsMasterClient ? "  (Host)" : "";
            _memberText.text =
                $"Room : {room.Name}\n" +
                $"Players : {room.PlayerCount} / {(int)room.MaxPlayers}\n" +
                $"You : Actor {PhotonNetwork.LocalPlayer.ActorNumber}{hostMark}";

            bool canStart = PhotonNetwork.IsMasterClient && !_isInGame;
            _startButton.gameObject.SetActive(canStart);
            _startButton.interactable = canStart;
        }

        private void SetStatus(string message)
        {
            _statusText.text = message;
            Debug.Log($"[TestFlow] {message}");
        }

        private bool IsRoomStarted()
        {
            if (!PhotonNetwork.InRoom) return false;
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KEY_STARTED, out object value)
                && value is bool started && started;
        }

        // ---- Photon コールバック --------------------------

        public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer) => RefreshRoomView();

        public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer) => RefreshRoomView();

        public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient) => RefreshRoomView();

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            if (propertiesThatChanged.ContainsKey(KEY_STARTED)) EnterInGame();
        }
    }
}
