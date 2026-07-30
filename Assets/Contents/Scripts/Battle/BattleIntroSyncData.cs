using ExitGames.Client.Photon;

/// <summary>
/// カットシーンの進行状況。スキップするかどうかは MasterClient だけが決めて、
/// この値に載せて全員へ配る。各クライアントは配られた値を見て演出を打ち切る。
/// </summary>
[System.Serializable]
public class BattleIntroSyncData : ISyncableData
{
    /// <summary>MasterClient がスキップを決めたか</summary>
    public bool IsSkipped;

    public Hashtable Serialize() => new()
    {
        { "skip", IsSkipped },
    };

    public void Deserialize(Hashtable data)
    {
        if (data.ContainsKey("skip")) IsSkipped = (bool)data["skip"];
    }
}
