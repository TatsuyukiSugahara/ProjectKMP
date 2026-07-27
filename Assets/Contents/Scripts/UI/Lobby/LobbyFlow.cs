using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PhotonPlayer = Photon.Realtime.Player;

namespace ProjectKMP.UI.Lobby
{
    /// <summary>人数に応じたカードの大きさと列数の設定</summary>
    [Serializable]
    public class CardSizeTier
    {
        [Tooltip("この人数までのときに使う設定")]
        public int maxPlayerCount = 4;

        [Tooltip("カード1枚ぶんの大きさ")]
        public Vector2 cellSize = new Vector2(168.0f, 188.0f);

        [Tooltip("横に並べる枚数")]
        public int columns = 4;
    }

    /// <summary>
    /// マッチング画面の進行役。
    /// 参加者の一覧を並べ、ホストにはゲーム開始ボタン、参加者には待機表示を出す。
    /// ホストが抜けた場合は Photon が次のホストを選ぶので、その結果を受けて表示を切り替える。
    /// </summary>
    public class LobbyFlow : MonoBehaviourPunCallbacks
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("一覧")]
        [SerializeField, Tooltip("カードを並べる親。GridLayoutGroup が付いていること")]
        private RectTransform _cardParent;

        [SerializeField, Tooltip("カードの並びを制御する GridLayoutGroup")]
        private GridLayoutGroup _grid;

        [SerializeField, Tooltip("1人ぶんのカードのプレハブ")]
        private LobbyPlayerCard _cardPrefab;

        [SerializeField, Tooltip("人数に応じたカードの大きさ。小さい人数の設定から順に並べる")]
        private CardSizeTier[] _cardTiers = new CardSizeTier[0];

        [SerializeField, Tooltip("顔の色。入室順に割り当てる")]
        private Color[] _avatarColors = new Color[0];

        [Header("表示")]
        [SerializeField, Tooltip("「3 / 20 にん」の表示")]
        private TMP_Text _countText;

        [SerializeField, Tooltip("人数表示の書式。{0}が現在人数、{1}が最大人数")]
        private string _countFormat = "{0} / {1} にん";

        [SerializeField, Tooltip("満員のときに後ろに足す文言")]
        private string _fullSuffix = "　まんいん!";

        [Header("ボタン")]
        [SerializeField, Tooltip("ホストにだけ出すゲーム開始ボタン")]
        private Button _startButton;

        [SerializeField, Tooltip("全員に出す退出ボタン")]
        private Button _leaveButton;

        [SerializeField, Tooltip("参加者に出す待機表示")]
        private CanvasGroup _waitingGroup;

        [Header("遷移")]
        [SerializeField, Tooltip("ゲーム本編のシーン名")]
        private string _inGameSceneName = "InGame";

        [SerializeField, Tooltip("退出したときに戻るシーン名")]
        private string _titleSceneName = "Title";

        [Header("参照")]
        [SerializeField, Tooltip("未設定なら NetworkManager.Instance を使う")]
        private NetworkManager _networkManager;

        [SerializeField, Tooltip("未設定なら SceneLoader.Instance を使う")]
        private SceneLoader _sceneLoader;

        // ---- 内部状態 ------------------------------------

        private readonly System.Collections.Generic.List<LobbyPlayerCard> _cards = new System.Collections.Generic.List<LobbyPlayerCard>();
        private bool _isLeaving;

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            if (_startButton != null) _startButton.onClick.AddListener(OnStartClicked);
            if (_leaveButton != null) _leaveButton.onClick.AddListener(OnLeaveClicked);

            if (!PhotonNetwork.InRoom)
            {
                // エディタでこのシーンを直接再生したときはルームに居ないので、表示だけ空にしておく
                Debug.LogWarning("[Lobby] ルームに入っていません。タイトルから始めてください");
            }

            Refresh();
            RefreshRepeatedlyAsync(destroyCancellationToken).Forget();
        }

        /// <summary>
        /// 入室直後はプレイヤー情報が届ききっていないことがあるため、
        /// 最初の数秒だけ定期的に貼り直して取りこぼしを防ぐ。
        /// </summary>
        private async UniTaskVoid RefreshRepeatedlyAsync(CancellationToken ct)
        {
            for (int i = 0; i < 6; i++)
            {
                await UniTask.Delay(System.TimeSpan.FromSeconds(0.5), cancellationToken: ct);
                Refresh();
            }
        }

        // ---- Photon コールバック --------------------------

        public override void OnJoinedRoom() => Refresh();

        public override void OnPlayerEnteredRoom(PhotonPlayer newPlayer) => Refresh();

        public override void OnPlayerLeftRoom(PhotonPlayer otherPlayer) => Refresh();

        /// <summary>
        /// 名前などのプレイヤー情報は入室より少し遅れて届くことがある。
        /// 届いたタイミングで貼り直さないと、名前が空のままになる。
        /// </summary>
        public override void OnPlayerPropertiesUpdate(PhotonPlayer targetPlayer, ExitGames.Client.Photon.Hashtable changedProps) => Refresh();

        /// <summary>ホストが抜けたときは Photon が次のホストを決めるので、その結果で表示を切り替える</summary>
        public override void OnMasterClientSwitched(PhotonPlayer newMasterClient)
        {
            Debug.Log($"[Lobby] あたらしいホスト: {newMasterClient.NickName}");
            Refresh();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>いまのルームの状態を画面に反映する</summary>
        private void Refresh()
        {
            NetworkManager network = _networkManager != null ? _networkManager : NetworkManager.Instance;
            int maxPlayers = network != null ? network.MaxPlayers : 20;
            int minToStart = network != null ? network.MinPlayersToStart : 1;

            PhotonPlayer[] players = PhotonNetwork.InRoom ? PhotonNetwork.PlayerList : Array.Empty<PhotonPlayer>();
            int count = players.Length;

            ApplyTier(count);
            BuildCards(players);

            if (_countText != null)
            {
                string text = string.Format(_countFormat, count, maxPlayers);
                if (count >= maxPlayers) text += _fullSuffix;
                _countText.text = text;
            }

            if (count > 0)
            {
                Debug.Log($"[Lobby] 参加者 {count}人: " + string.Join(", ",
                    System.Array.ConvertAll(players, p => $"{p.ActorNumber}:'{p.NickName}'{(p.IsMasterClient ? "(ホスト)" : string.Empty)}")));
            }

            bool isHost = PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;

            if (_startButton != null)
            {
                _startButton.gameObject.SetActive(isHost);
                _startButton.interactable = isHost && count >= minToStart && !_isLeaving;
            }

            if (_waitingGroup != null)
            {
                bool showWaiting = !isHost;
                _waitingGroup.alpha = showWaiting ? 1.0f : 0.0f;
                _waitingGroup.blocksRaycasts = false;
            }
        }

        /// <summary>人数に合ったカードの大きさと列数をグリッドに反映する</summary>
        private void ApplyTier(int count)
        {
            if (_grid == null || _cardTiers == null || _cardTiers.Length == 0) return;

            CardSizeTier tier = _cardTiers[_cardTiers.Length - 1];
            foreach (var candidate in _cardTiers)
            {
                if (count <= candidate.maxPlayerCount)
                {
                    tier = candidate;
                    break;
                }
            }

            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = Mathf.Max(1, tier.columns);
            _grid.cellSize = tier.cellSize;
        }

        /// <summary>参加人数ぶんのカードを用意して中身を設定する</summary>
        private void BuildCards(PhotonPlayer[] players)
        {
            if (_cardPrefab == null || _cardParent == null)
            {
                Debug.LogError("[Lobby] カードのプレハブか並べる場所が未設定です。参加者が表示できません");
                return;
            }

            // 足りなければ作り、余っていれば消す
            while (_cards.Count < players.Length)
            {
                var card = Instantiate(_cardPrefab, _cardParent);
                _cards.Add(card);
            }
            while (_cards.Count > players.Length)
            {
                int last = _cards.Count - 1;
                if (_cards[last] != null) Destroy(_cards[last].gameObject);
                _cards.RemoveAt(last);
            }

            float cellWidth = _grid != null ? _grid.cellSize.x : 168.0f;

            for (int i = 0; i < players.Length; i++)
            {
                PhotonPlayer player = players[i];
                string playerName = string.IsNullOrWhiteSpace(player.NickName)
                    ? $"プレイヤー{player.ActorNumber}"
                    : player.NickName;

                _cards[i].Setup(playerName, player.IsMasterClient, player.IsLocal, PickColor(player.ActorNumber));
                _cards[i].gameObject.name = $"Card_{player.ActorNumber}_{playerName}";
                _cards[i].ApplyCellWidth(cellWidth);
            }
        }

        /// <summary>入室順で顔の色を選ぶ</summary>
        private Color PickColor(int actorNumber)
        {
            if (_avatarColors == null || _avatarColors.Length == 0) return Color.white;

            int index = Mathf.Abs(actorNumber - 1) % _avatarColors.Length;
            return _avatarColors[index];
        }

        /// <summary>ホストがゲームを始める。参加を締め切ってから全員をゲーム本編へ連れて行く</summary>
        private void OnStartClicked()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            NetworkManager network = _networkManager != null ? _networkManager : NetworkManager.Instance;
            network?.CloseRoom();

            if (_startButton != null) _startButton.interactable = false;
            if (_leaveButton != null) _leaveButton.interactable = false;

            Debug.Log($"[Lobby] ゲーム開始 → {_inGameSceneName}");

            // AutomaticallySyncScene が有効なので、参加者は自動で同じシーンへ移る
            PhotonNetwork.LoadLevel(_inGameSceneName);
        }

        private void OnLeaveClicked()
        {
            if (_isLeaving) return;
            _isLeaving = true;

            if (_startButton != null) _startButton.interactable = false;
            if (_leaveButton != null) _leaveButton.interactable = false;

            LeaveAsync(destroyCancellationToken).Forget();
        }

        /// <summary>ルームから抜けてタイトルへ戻る</summary>
        private async UniTaskVoid LeaveAsync(CancellationToken ct)
        {
            NetworkManager network = _networkManager != null ? _networkManager : NetworkManager.Instance;
            SceneLoader loader = _sceneLoader != null ? _sceneLoader : SceneLoader.Instance;

            if (network != null) await network.LeaveRoomAsync(ct);

            Debug.Log("[Lobby] 退出してタイトルへ戻ります");

            if (loader != null)
            {
                await loader.LoadSceneAsync(_titleSceneName, ct);
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(_titleSceneName);
            }
        }
    }
}
