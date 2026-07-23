using Photon.Pun;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// ローカルプレイヤーのネットワーク生成を担当する。
    /// 各クライアントが自分の分だけ生成する(生成したクライアントが所有権を持つため)。
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------
        // PhotonNetwork.Instantiate は Resources 直下からの相対パスで指定する
        private const string PLAYER_PREFAB_PATH = "NetworkPrefabs/PF_Player_Test";

        // ---- 調整パラメータ ------------------------------
        [SerializeField] private float _spawnRadius = 3.0f;
        [SerializeField] private float _spawnHeight = 1.0f;

        // ---- 内部状態 ------------------------------------
        private GameObject _localPlayer;

        // ---- 公開API -------------------------------------

        /// <summary>ローカルプレイヤーを生成する。二重生成はしない</summary>
        public GameObject SpawnLocalPlayer()
        {
            if (_localPlayer != null) return _localPlayer;

            if (!PhotonNetwork.InRoom)
            {
                Debug.LogError("[Player] ルームに入室していないため生成できません");
                return null;
            }

            int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
            int slotCount   = (int)PhotonNetwork.CurrentRoom.MaxPlayers;
            Vector3 position = CalcSpawnPosition(actorNumber, slotCount);

            _localPlayer = PhotonNetwork.Instantiate(PLAYER_PREFAB_PATH, position, Quaternion.identity);
            Debug.Log($"[Player] 生成 Actor={actorNumber} pos={position}");
            return _localPlayer;
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>ActorNumber を使って重ならない位置を円周上に割り振る</summary>
        private Vector3 CalcSpawnPosition(int actorNumber, int slotCount)
        {
            int count = Mathf.Max(slotCount, 1);
            float rad = 2.0f * Mathf.PI * ((actorNumber - 1) % count) / count;
            return new Vector3(Mathf.Cos(rad) * _spawnRadius, _spawnHeight, Mathf.Sin(rad) * _spawnRadius);
        }
    }
}
