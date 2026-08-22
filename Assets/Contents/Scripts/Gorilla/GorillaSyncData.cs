using ExitGames.Client.Photon;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// ゴリラのステート種別。ステートクラスそのものはネットワークに載せられないため、
    /// 「どのステートか」をこの列挙体に置き換えて配り、受け取った側で同じステートを作り直す。
    /// </summary>
    public enum GorillaStateKind
    {
        None = 0,
        Idle,
        Patrol,
        Chase,
        NormalAttack,
        SweepAttack,
        StampAttack,
        BeamAttack,
        ChargeAttack,
        RockThrow,
        RushPunch,
        Pounce,
        Fissure,
        Grab,
        Roar,
        Stagger,
        Death,
    }

    /// <summary>
    /// ゴリラの位置・向き・ステートの同期データ。
    /// 決めるのは MasterClient だけで、ゲストは配られたこの値をそのまま再生する。
    /// </summary>
    [System.Serializable]
    public class GorillaSyncData : ISyncableData
    {
        /// <summary>ワールド座標</summary>
        public Vector3 Position;

        /// <summary>Y軸まわりの向き(度)。ゴリラは地面に立っているのでYawだけ配れば足りる</summary>
        public float YawDeg;

        /// <summary>いま実行中のステート</summary>
        public GorillaStateKind State;

        /// <summary>
        /// ステートを切り替えるたびに増える通し番号。
        /// 「攻撃 → 硬直 → また同じ攻撃」のように同じ種別が続いても、
        /// 番号が変わることでゲスト側が確実に再生し直せる。
        /// </summary>
        public int StateSequence;

        /// <summary>
        /// 掴んでいるプレイヤーの ActorNumber。誰も掴んでいなければ GorillaAI.NO_GRAB。
        /// 誰を掴んだかはゲームの状態そのものなので、MasterClient が決めて全員へ配る。
        /// </summary>
        public int GrabbedActorNumber = int.MinValue;

        public Hashtable Serialize() => new()
        {
            { "px",  Position.x },
            { "py",  Position.y },
            { "pz",  Position.z },
            { "yaw", YawDeg },
            { "st",  (int)State },
            { "seq", StateSequence },
            { "grab", GrabbedActorNumber },
        };

        public void Deserialize(Hashtable data)
        {
            if (data.ContainsKey("px"))  Position.x    = (float)data["px"];
            if (data.ContainsKey("py"))  Position.y    = (float)data["py"];
            if (data.ContainsKey("pz"))  Position.z    = (float)data["pz"];
            if (data.ContainsKey("yaw")) YawDeg        = (float)data["yaw"];
            if (data.ContainsKey("st"))  State         = (GorillaStateKind)(int)data["st"];
            if (data.ContainsKey("seq"))  StateSequence      = (int)data["seq"];
            if (data.ContainsKey("grab")) GrabbedActorNumber = (int)data["grab"];
        }
    }
}
