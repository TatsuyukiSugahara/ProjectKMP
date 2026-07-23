using Photon.Pun;
using R3;
using UnityEngine;

/// <summary>
/// 汎用データ同期コンポーネント。
/// MasterClient が SetValue() で値を変更すると全クライアントに自動同期される。
/// 受信側は Value.Subscribe() で変化を受け取れる。
///
/// 使い方:
///   1. ISyncableData を実装したデータクラスを作る
///   2. SyncObject[T] を MonoBehaviour にアタッチし PhotonView と同じ GameObject に置く
///   3. PhotonView の Observed Components にこのコンポーネントを追加する
/// </summary>
public class SyncObject<T> : MonoBehaviourPun, IPunObservable
    where T : ISyncableData, new()
{
    // 外部はこれをSubscribeする
    public ReadOnlyReactiveProperty<T> Value => _value;

    private readonly ReactiveProperty<T> _value = new(new T());
    private bool _isDirty = false;

    /// <summary>
    /// MasterClient だけが呼び出す。値を変更して全員に同期する。
    /// 例: _syncObject.SetValue(d => d.HP = 100);
    /// </summary>
    public void SetValue(System.Action<T> mutate)
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[SyncObject] SetValue は MasterClient のみ呼び出せます");
            return;
        }
        mutate(_value.Value);
        _value.ForceNotify();
        _isDirty = true;
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // 変化があった時だけ送信（帯域節約）
            stream.SendNext(_isDirty ? _value.Value.Serialize() : null);
            _isDirty = false;
        }
        else
        {
            var data = stream.ReceiveNext()
                as ExitGames.Client.Photon.Hashtable;
            if (data == null) return;

            _value.Value.Deserialize(data);
            _value.ForceNotify();
        }
    }

    private void OnDestroy()
    {
        _value.Dispose();
    }
}