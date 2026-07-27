using Cysharp.Threading.Tasks;
using Photon.Pun;
using Photon.Realtime;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Photon への接続・ロビー入室・ルーム操作を担う。
/// UniTask でコールバックをラップし、await で使えるようにしている。
/// シーンをまたいで生き残るので、最初のシーン(Title)にひとつ置いておけばよい。
/// </summary>
public class NetworkManager : MonoBehaviourPunCallbacks
{
    // ---- 定数 ----------------------------------------

    private const string GAME_VERSION = "1.0";

    // ---- 調整パラメータ ------------------------------

    [SerializeField, Tooltip("みんなが集まる部屋の名前。この名前の部屋がひとつだけ作られる")]
    private string _roomName = "GabuttoBuster";

    [SerializeField, Tooltip("1ルームの最大人数。ここを変えるとマッチング画面の表示も追従する")]
    private int _maxPlayers = 20;

    [SerializeField, Tooltip("ゲームを開始できる最少人数。1ならひとりでも始められる")]
    private int _minPlayersToStart = 1;

    [SerializeField, Tooltip("同時接続の上限。Photon無料プランは20。ここを超える接続は弾く")]
    private int _maxCcu = 20;

    // ---- 内部状態 ------------------------------------

    private TaskCompletionSource<bool> _connectTcs;
    private TaskCompletionSource<bool> _joinRoomTcs;

    /// <summary>入室に失敗した理由。成功時は使わない</summary>
    private MatchResult _lastJoinFailure = MatchResult.Failed;

    // ---- 公開API -------------------------------------

    /// <summary>シーンをまたいで共有される唯一の NetworkManager</summary>
    public static NetworkManager Instance { get; private set; }

    /// <summary>1ルームの最大人数</summary>
    public int MaxPlayers => Mathf.Clamp(_maxPlayers, 1, 255);

    /// <summary>ゲームを開始できる最少人数</summary>
    public int MinPlayersToStart => Mathf.Max(1, _minPlayersToStart);

    /// <summary>表示名を設定する。空なら既定の名前を使う</summary>
    public void SetNickName(string nickName, string fallbackName)
    {
        PhotonNetwork.NickName = string.IsNullOrWhiteSpace(nickName) ? fallbackName : nickName.Trim();
        Debug.Log($"[Network] 表示名: {PhotonNetwork.NickName}");
    }

    /// <summary>
    /// Photon に接続し、ロビーまで入室する。
    /// 同時接続が上限に達している場合は Full を返す。
    /// </summary>
    public async UniTask<ConnectResult> ConnectAndJoinLobbyAsync(CancellationToken ct)
    {
        if (PhotonNetwork.IsConnected && PhotonNetwork.InLobby)
        {
            Debug.Log("[Network] すでに接続済み");
            return ConnectResult.Success;
        }

        // ひとり用で遊んだあとにマルチへ進めるよう、オフラインモードを解除する
        await StopOfflineModeAsync(ct);

        _connectTcs = new TaskCompletionSource<bool>();

        PhotonNetwork.GameVersion = GAME_VERSION;
        PhotonNetwork.AutomaticallySyncScene = true; // MasterClientのシーン遷移に追従
        PhotonNetwork.ConnectUsingSettings();

        // コールバック（OnJoinedLobby か OnDisconnected）を待つ
        bool connected = await _connectTcs.Task
            .AsUniTask()
            .AttachExternalCancellation(ct);

        if (!connected) return ConnectResult.Failed;

        // 同時接続の上限チェック。無料プランでは20人を超えるとそもそも繋がらない
        if (PhotonNetwork.CountOfPlayers > _maxCcu)
        {
            Debug.LogWarning($"[Network] 同時接続が上限: {PhotonNetwork.CountOfPlayers}/{_maxCcu}");
            PhotonNetwork.Disconnect();
            return ConnectResult.Full;
        }

        return ConnectResult.Success;
    }

    /// <summary>
    /// 決まった名前の部屋に入る。無ければ作る。
    /// 「入る」と「作る」をサーバー側で一度に判定させるため、
    /// 同時に何人が押しても部屋がふたつできることはなく、
    /// 先にサーバーへ届いた人が作成者(＝ホスト)になり、あとの人はそこへ参加する。
    /// </summary>
    public async UniTask<MatchResult> FindMatchAsync(CancellationToken ct)
    {
        _joinRoomTcs = new TaskCompletionSource<bool>();
        _lastJoinFailure = MatchResult.Failed;

        var options = new RoomOptions
        {
            MaxPlayers = (byte)MaxPlayers,
            IsVisible  = true,
            IsOpen     = true,
        };

        PhotonNetwork.JoinOrCreateRoom(_roomName, options, TypedLobby.Default);

        bool joined = await _joinRoomTcs.Task
            .AsUniTask()
            .AttachExternalCancellation(ct);

        if (!joined) return _lastJoinFailure;

        return PhotonNetwork.IsMasterClient ? MatchResult.Created : MatchResult.Joined;
    }

    /// <summary>
    /// 空き部屋に入り、無ければ作る。入れたら true。
    /// _Sandbox のテスト用シーンなど従来の呼び出し向け。
    /// マッチング画面からは FindMatchAsync を使うこと。
    /// </summary>
    public async UniTask<bool> JoinOrCreateRoomAsync(CancellationToken ct)
    {
        MatchResult match = await FindMatchAsync(ct);
        return match == MatchResult.Joined || match == MatchResult.Created;
    }

    /// <summary>
    /// 通信せずにひとりで遊ぶ準備をする。
    /// Photon のオフラインモードを使うので、ネットワーク生成や同期処理をそのまま流用できる。
    /// </summary>
    public async UniTask StartOfflineModeAsync(CancellationToken ct)
    {
        // 前回のマルチプレイの接続が残っていたら、先に片付ける
        if (PhotonNetwork.InRoom) await LeaveRoomAsync(ct);

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.Disconnect();
            await UniTask.WaitUntil(() => !PhotonNetwork.IsConnected, cancellationToken: ct);
        }

        PhotonNetwork.OfflineMode = true;

        if (!PhotonNetwork.InRoom)
        {
            PhotonNetwork.CreateRoom("Offline");
            await UniTask.WaitUntil(() => PhotonNetwork.InRoom, cancellationToken: ct);
        }

        Debug.Log("[Network] ひとり用(オフライン)で開始します");
    }

    /// <summary>マルチプレイに戻る前に、オフラインモードを解除する</summary>
    public async UniTask StopOfflineModeAsync(CancellationToken ct)
    {
        if (!PhotonNetwork.OfflineMode) return;

        if (PhotonNetwork.InRoom) await LeaveRoomAsync(ct);
        PhotonNetwork.OfflineMode = false;
    }

    /// <summary>
    /// ゲーム開始時に新規参加を締め切る。
    /// 一覧には残す(IsVisible=true)ので、あとから来た人は「あそび中」だと判定できる。
    /// </summary>
    public void CloseRoom()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!PhotonNetwork.InRoom) return;

        PhotonNetwork.CurrentRoom.IsOpen    = false;
        PhotonNetwork.CurrentRoom.IsVisible = true;
        Debug.Log("[Network] ルームを締め切りました");
    }

    /// <summary>ふたたび参加を受け付ける(ゲーム終了時など)</summary>
    public void ReopenRoom()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!PhotonNetwork.InRoom) return;

        PhotonNetwork.CurrentRoom.IsOpen    = true;
        PhotonNetwork.CurrentRoom.IsVisible = true;
    }

    /// <summary>ルームから退出してロビーに戻る</summary>
    public async UniTask LeaveRoomAsync(CancellationToken ct)
    {
        if (!PhotonNetwork.InRoom) return;

        PhotonNetwork.LeaveRoom();
        await UniTask.WaitUntil(() => !PhotonNetwork.InRoom, cancellationToken: ct);
    }

    // ---- Unityイベント -------------------------------

    private void Awake()
    {
        // シーンをまたいでも接続を維持するため、ひとつだけ残して使い回す
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ---- Photon コールバック --------------------------

    public override void OnConnectedToMaster()
    {
        // オフラインモードにはロビーが無いので何もしない
        if (PhotonNetwork.OfflineMode) return;

        // ロビーに入室してからSuccessを返す
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        _connectTcs?.TrySetResult(true);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogError($"[Network] 切断: {cause}");
        _connectTcs?.TrySetResult(false);
        _joinRoomTcs?.TrySetResult(false);
    }

    public override void OnJoinedRoom()
    {
        string role = PhotonNetwork.IsMasterClient ? "ホスト" : "参加者";
        Debug.Log($"[Network] ルーム入室: {PhotonNetwork.CurrentRoom.Name} ({role}) " +
                  $"{PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers}");
        _joinRoomTcs?.TrySetResult(true);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        // 締め切られている = すでにゲームが始まっている
        if (returnCode == ErrorCode.GameClosed) _lastJoinFailure = MatchResult.GameInProgress;
        else if (returnCode == ErrorCode.GameFull) _lastJoinFailure = MatchResult.RoomFull;
        else _lastJoinFailure = MatchResult.Failed;

        Debug.LogWarning($"[Network] 入室できません({returnCode}): {message}");
        _joinRoomTcs?.TrySetResult(false);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[Network] ルーム作成失敗({returnCode}): {message}");
        _lastJoinFailure = MatchResult.Failed;
        _joinRoomTcs?.TrySetResult(false);
    }

    public override void OnMasterClientSwitched(Player newMaster)
    {
        Debug.Log($"[Network] MasterClient → {newMaster.NickName}");
        // SyncObject が全員に同期されているため、引き継ぎは自動で完了する
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"[Network] 入室: {newPlayer.NickName} " +
                  $"({PhotonNetwork.CurrentRoom.PlayerCount}/{MaxPlayers})");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"[Network] 退室: {otherPlayer.NickName}");
    }
}

public enum ConnectResult { Success, Full, Failed }

/// <summary>マッチングの結果</summary>
public enum MatchResult
{
    /// <summary>既存の部屋に参加した</summary>
    Joined,
    /// <summary>部屋が無かったので自分が作った(ホスト)</summary>
    Created,
    /// <summary>すでにゲームが始まっていて入れない</summary>
    GameInProgress,
    /// <summary>満員で入れない</summary>
    RoomFull,
    /// <summary>通信の失敗</summary>
    Failed,
}
