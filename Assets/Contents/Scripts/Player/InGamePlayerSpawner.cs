using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// インゲームに入ったときに、自分のキャラをネットワーク生成する。
    /// 生成した本人が所有者になるので、各クライアントが自分のぶんだけ生成する。
    /// </summary>
    public class InGamePlayerSpawner : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("Resources からの相対パス。PhotonNetwork.Instantiate の仕様")]
        private string _prefabPath = "NetworkPrefabs/PF_Player_Online";

        [SerializeField, Tooltip("ひとりあたりに確保する円周の長さ(メートル)。人数が増えるほど円が大きくなる")]
        private float _spacingPerPlayer = 3.0f;

        [SerializeField, Tooltip("出現円の最小半径(メートル)")]
        private float _minRadius = 6.0f;

        [SerializeField, Tooltip("出現させる高さ(メートル)")]
        private float _spawnHeight = 0.2f;

        [SerializeField, Tooltip("ルームに入っていないときも、ひとりで動作確認できるようにする")]
        private bool _allowOfflineTest = true;

        // ---- 内部状態 ------------------------------------

        private GameObject _localPlayer;

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            SpawnAsync(destroyCancellationToken).Forget();
        }

        // ---- 内部処理 ------------------------------------

        private async UniTaskVoid SpawnAsync(CancellationToken ct)
        {
            if (!PhotonNetwork.InRoom)
            {
                if (!_allowOfflineTest)
                {
                    Debug.LogError("[Player] ルームに入っていないため生成できません");
                    return;
                }

                // エディタでこのシーンを直接再生したとき用。ひとりだけのオフラインルームを作る
                if (!PhotonNetwork.IsConnected)
                {
                    Debug.Log("[Player] オフラインモードで動作確認します");
                    PhotonNetwork.OfflineMode = true;
                    PhotonNetwork.CreateRoom("Offline");
                }

                await UniTask.WaitUntil(() => PhotonNetwork.InRoom, cancellationToken: ct);
            }

            int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
            int slotCount = PhotonNetwork.CurrentRoom.MaxPlayers > 0 ? PhotonNetwork.CurrentRoom.MaxPlayers : 4;

            Vector3 position = CalcSpawnPosition(actorNumber, slotCount);
            Quaternion rotation = Quaternion.LookRotation(new Vector3(-position.x, 0.0f, -position.z).normalized, Vector3.up);

            _localPlayer = PhotonNetwork.Instantiate(_prefabPath, position, rotation);
            Debug.Log($"[Player] 生成 Actor={actorNumber} pos={position}");
        }

        /// <summary>人数ぶんの間隔が空くように、円周上へ均等に配置する</summary>
        private Vector3 CalcSpawnPosition(int actorNumber, int slotCount)
        {
            int slots = Mathf.Max(1, slotCount);

            // 円周 = 人数 × ひとりあたりの間隔 になる半径を求める
            float radius = Mathf.Max(_minRadius, slots * _spacingPerPlayer / (2.0f * Mathf.PI));
            float radian = 2.0f * Mathf.PI * ((actorNumber - 1) % slots) / slots;

            return new Vector3(Mathf.Cos(radian) * radius, _spawnHeight, Mathf.Sin(radian) * radius);
        }
    }
}
