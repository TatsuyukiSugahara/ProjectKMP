using System;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using ProjectKMP.Battle;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 被弾による死亡とリスポーンを扱う。弾1発で死亡する。
    /// 撃った側のクライアントが ApplyKill を呼び、そこから全員に RPC が飛ぶ。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerHealth : MonoBehaviourPun
    {
        // ---- 定数 ----------------------------------------
        private const float RESPAWN_DELAY_SEC = 3.0f;
        private const float RESPAWN_RADIUS    = 8.0f;
        private const float RESPAWN_HEIGHT    = 1.0f;

        // ---- 内部状態 ------------------------------------
        private CharacterController _controller;
        private PlayerMover _mover;
        private Renderer[] _renderers;
        private bool _isDead;

        // ---- 公開API -------------------------------------

        public bool IsDead => _isDead;

        /// <summary>このプレイヤーを操作しているクライアントの ActorNumber</summary>
        public int OwnerActorNumber => photonView.Owner != null ? photonView.Owner.ActorNumber : -1;

        /// <summary>撃った側のクライアントから呼ぶ。全員に死亡を伝える</summary>
        public void ApplyKill(int killerActorNumber)
        {
            if (_isDead) return;
            photonView.RPC(nameof(RpcOnKilled), RpcTarget.All, killerActorNumber);
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _mover = GetComponent<PlayerMover>();
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        // ---- RPC -----------------------------------------

        [PunRPC]
        private void RpcOnKilled(int killerActorNumber)
        {
            if (_isDead) return;
            _isDead = true;
            SetAlive(false);

            // 死んだ本人が自分の死亡数を加算し、復帰も自分で行う
            if (photonView.IsMine)
            {
                BattleScore.AddLocalDeath();
                RespawnAfterDelayAsync().Forget();
            }

            // 倒した本人が自分の撃破数を加算する
            if (killerActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
            {
                BattleScore.AddLocalKill();
            }
        }

        [PunRPC]
        private void RpcRevive(Vector3 position)
        {
            _isDead = false;
            Teleport(position);
            SetAlive(true);
        }

        // ---- 内部処理 ------------------------------------

        private async UniTaskVoid RespawnAfterDelayAsync()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(RESPAWN_DELAY_SEC),
                cancellationToken: destroyCancellationToken);

            Vector2 circle = UnityEngine.Random.insideUnitCircle * RESPAWN_RADIUS;
            Vector3 position = new Vector3(circle.x, RESPAWN_HEIGHT, circle.y);

            photonView.RPC(nameof(RpcRevive), RpcTarget.All, position);
        }

        /// <summary>CharacterController が有効なままだと位置を代入しても戻されるので、一度切る</summary>
        private void Teleport(Vector3 position)
        {
            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = wasEnabled;
        }

        private void SetAlive(bool alive)
        {
            foreach (Renderer renderer in _renderers)
            {
                if (renderer != null) renderer.enabled = alive;
            }

            // 死亡中は弾が当たらないよう当たり判定も切る
            _controller.enabled = alive;

            if (_mover != null && photonView.IsMine) _mover.enabled = alive;
        }
    }
}
