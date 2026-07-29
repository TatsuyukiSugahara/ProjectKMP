/// <summary>
/// モンスターのHP同期。SyncObject&lt;T&gt; は総称型のままだと Unity のコンポーネントとして
/// アタッチできないため、型を決めたこのクラスを噛ませている。中身は空でよい。
/// </summary>
public class MonsterSyncObject : SyncObject<MonsterSyncData>
{
}
