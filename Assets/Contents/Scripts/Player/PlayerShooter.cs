using Photon.Pun;
using ProjectKMP.Battle;
using ProjectKMP.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 弾の発射。所有者だけが入力を受け取り、発射位置と向きを RPC で全員に配る。
    /// 弾そのものは各クライアントがローカル生成するため、弾ごとの通信は発生しない。
    /// </summary>
    public class PlayerShooter : MonoBehaviourPun
    {
        // ---- 参照 ----------------------------------------
        [SerializeField] private Bullet _bulletPrefab;

        // ---- 調整パラメータ ------------------------------
        [SerializeField] private float _fireIntervalSec = 0.8f;
        [SerializeField] private Vector3 _muzzleOffset  = new Vector3(0.0f, 1.2f, 0.7f);

        // ---- 内部状態 ------------------------------------
        private PlayerHealth _health;
        private float _cooldown;

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _health = GetComponent<PlayerHealth>();
        }

        private void Start()
        {
            // 他人のキャラで発射入力を見る必要は無い
            if (!photonView.IsMine) enabled = false;
        }

        private void Update()
        {
            _cooldown -= Time.deltaTime;

            if (!BattleClock.IsRunning) return;
            if (_health != null && _health.IsDead) return;
            if (_cooldown > 0.0f) return;
            if (!IsFirePressed()) return;

            _cooldown = _fireIntervalSec;

            Vector3 position = transform.TransformPoint(_muzzleOffset);
            Vector3 direction = transform.forward;
            photonView.RPC(nameof(RpcSpawnBullet), RpcTarget.All, position, direction);
        }

        // ---- RPC -----------------------------------------

        [PunRPC]
        private void RpcSpawnBullet(Vector3 position, Vector3 direction, PhotonMessageInfo info)
        {
            if (_bulletPrefab == null) return;

            Bullet bullet = Instantiate(_bulletPrefab, position, Quaternion.LookRotation(direction));

            int shooterActorNumber = info.Sender != null ? info.Sender.ActorNumber : -1;
            bool isOwnerSide = info.Sender != null && info.Sender.IsLocal;
            bullet.Initialize(shooterActorNumber, isOwnerSide);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>キーボードSpace / ゲームパッドR1 / タッチ用発射ボタンのいずれか</summary>
        private bool IsFirePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.isPressed) return true;

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.rightShoulder.isPressed) return true;

            FireButton fireButton = ServiceLocator.TryGet<FireButton>();
            if (fireButton != null && fireButton.IsHeld) return true;

            return false;
        }
    }
}
