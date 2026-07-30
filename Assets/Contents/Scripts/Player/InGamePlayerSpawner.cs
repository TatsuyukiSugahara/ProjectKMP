using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// インゲームに入ったときに、自分のキャラをネットワーク生成する。
    /// 生成した本人が所有者になるので、各クライアントが自分のぶんだけ生成する。
    /// 出現位置はドーナツ状の範囲からランダムに選ぶ(フィールド中央のボス付近は避ける)。
    /// </summary>
    public class InGamePlayerSpawner : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("Resources からの相対パス。PhotonNetwork.Instantiate の仕様")]
        private string _prefabPath = "NetworkPrefabs/PF_Player_Online";

        [SerializeField, Min(0.0f), Tooltip("出現地点を選ぶ円の最小半径(メートル)。フィールド中央(ボス付近)を避ける")]
        private float _minSpawnRadius = 8.0f;

        [SerializeField, Min(0.0f), Tooltip("出現地点を選ぶ円の最大半径(メートル)。壁の内側に収める")]
        private float _maxSpawnRadius = 20.0f;

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

            Vector3 position = CalcRandomSpawnPosition();

            // フィールド中央を向いて出現させる
            Vector3 toCenter = new Vector3(-position.x, 0.0f, -position.z);
            Quaternion rotation = toCenter.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(toCenter.normalized, Vector3.up)
                : Quaternion.identity;

            _localPlayer = PhotonNetwork.Instantiate(_prefabPath, position, rotation);
            Debug.Log($"[Player] 生成 Actor={PhotonNetwork.LocalPlayer.ActorNumber} pos={position}");
        }

        /// <summary>ドーナツ状の範囲から、面積が偏らないようにランダムな出現位置を選ぶ</summary>
        private Vector3 CalcRandomSpawnPosition()
        {
            float min = Mathf.Min(_minSpawnRadius, _maxSpawnRadius);
            float max = Mathf.Max(_minSpawnRadius, _maxSpawnRadius);

            // 半径を単純な乱数にすると中心寄りに偏るため、面積が均等になるように選ぶ
            float radius = Mathf.Sqrt(Mathf.Lerp(min * min, max * max, Random.value));
            float radian = Random.value * 2.0f * Mathf.PI;

            return new Vector3(Mathf.Cos(radian) * radius, _spawnHeight, Mathf.Sin(radian) * radius);
        }
    }
}
