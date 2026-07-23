using Cysharp.Threading.Tasks;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Photon への接続・ロビー入室・ルーム操作を担う。
/// UniTask でコールバックをラップし、await で使えるようにしている。
/// </summary>
public class NetworkManager : MonoBehaviourPunCallbacks
{
    // ---- 定数 ----------------------------------------
    private const int MAX_CCU      = 90;   // 無料枠の上限に余裕を持たせた値
    private const int MAX_PLAYERS  = 4;    // 1ルームの最大人数
    private const string GAME_VERSION = "1.0";

    // ---- 内部状態 ------------------------------------
    private TaskCompletionSource<bool> _connectTcs;
    private TaskCompletionSource<bool> _joinRoomTcs;

    // ---- 公開API -------------------------------------

    /// <summary>
    /// Photon に接続し、ロビーまで入室する。
    /// CCU が90以上なら ConnectResult.Full を返す。
    /// </summary>
    public async UniTask<ConnectResult> ConnectAndJoinLobbyAsync(CancellationToken ct)
    {
        if (PhotonNetwork.IsConnected)
        {
            Debug.Log("[Network] すでに接続済み");
            return ConnectResult.Success;
        }

        _connectTcs = new TaskCompletionSource<bool>();

        PhotonNetwork.GameVersion = GAME_VERSION;
        PhotonNetwork.AutomaticallySyncScene = true; // MasterClientのシーン遷移に追従
        PhotonNetwork.ConnectUsingSettings();

        // コールバック（OnConnectedToMaster か OnDisconnected）を待つ
        bool connected = await _connectTcs.Task
            .AsUniTask()
            .AttachExternalCancellation(ct);

        if (!connected) return ConnectResult.Failed;

        // CCU チェック
        if (PhotonNetwork.CountOfPlayers >= MAX_CCU)
        {
            PhotonNetwork.Disconnect();
            return ConnectResult.Full;
        }

        return ConnectResult.Success;
    }

    /// <summary>
    /// ランダムマッチング。空きルームがなければ新規作成する。
    /// </summary>
    public async UniTask<bool> JoinOrCreateRoomAsync(CancellationToken ct)
    {
        _joinRoomTcs = new TaskCompletionSource<bool>();

        var options = new RoomOptions
        {
            MaxPlayers  = MAX_PLAYERS,
            IsVisible   = true,
            IsOpen      = true,
        };

        // まずランダム入室を試みる
        PhotonNetwork.JoinRandomRoom();

        return await _joinRoomTcs.Task
            .AsUniTask()
            .AttachExternalCancellation(ct);
    }

    /// <summary>ゲーム開始時に新規参加を締め切る</summary>
    public void CloseRoom()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom.IsOpen    = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;
    }

    /// <summary>ルームから退出してロビーに戻る</summary>
    public async UniTask LeaveRoomAsync(CancellationToken ct)
    {
        PhotonNetwork.LeaveRoom();
        await UniTask.WaitUntil(
            () => !PhotonNetwork.InRoom, cancellationToken: ct);
    }

    // ---- Photon コールバック --------------------------

    public override void OnConnectedToMaster()
    {
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
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"[Network] ルーム入室: {PhotonNetwork.CurrentRoom.Name}");
        _joinRoomTcs?.TrySetResult(true);
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        // 空きルームがなければ新規作成
        Debug.Log("[Network] 空きルームなし → 新規作成");
        var options = new RoomOptions { MaxPlayers = MAX_PLAYERS };
        PhotonNetwork.CreateRoom(null, options); // null = 自動命名
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[Network] ルーム作成失敗: {message}");
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
                  $"({PhotonNetwork.CurrentRoom.PlayerCount}/{MAX_PLAYERS})");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"[Network] 退室: {otherPlayer.NickName}");
    }
}

public enum ConnectResult { Success, Full, Failed }