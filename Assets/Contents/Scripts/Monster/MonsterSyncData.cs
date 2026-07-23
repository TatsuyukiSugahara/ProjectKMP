using ExitGames.Client.Photon;
using UnityEngine;

public enum MonsterState { Idle, Chasing, Attacking, Dead }

[System.Serializable]
public class MonsterSyncData : ISyncableData
{
    public int HP;
    public int MaxHP;
    public MonsterState State;

    public Hashtable Serialize() => new()
    {
        { "hp",    HP },
        { "mhp",   MaxHP },
        { "state", (int)State },
    };

    public void Deserialize(Hashtable data)
    {
        if (data.ContainsKey("hp"))    HP    = (int)data["hp"];
        if (data.ContainsKey("mhp"))   MaxHP = (int)data["mhp"];
        if (data.ContainsKey("state")) State = (MonsterState)(int)data["state"];
    }
}