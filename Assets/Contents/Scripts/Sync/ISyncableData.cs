/// <summary>
/// Photon で同期するデータクラスに実装するインターフェース。
/// SerializeでHashtableに変換、DeserializeでHashtableから復元する。
/// </summary>
public interface ISyncableData
{
    ExitGames.Client.Photon.Hashtable Serialize();
    void Deserialize(ExitGames.Client.Photon.Hashtable data);
}