using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Dog
{
    /// <summary>
    /// 犬(Husky)の歩行/待機アニメーション再生。
    /// PlayerMover 等が CharacterController を動かした結果の速度を見て切り替えるだけなので、
    /// 移動ロジック側(PlayerMover)には手を加えない。
    /// Jキーで頭突き(Attack)アニメーションをワンショット再生する(とりあえず実装)。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(CharacterController))]
    public class DogAnimationDriver : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------
        private const string ANIM_IDLE   = "Idle_A";
        private const string ANIM_WALK   = "Walk";
        private const string ANIM_ATTACK = "Attack"; // 頭突き
        private const float ANIM_CROSSFADE = 0.2f;
        private const int ANIM_BASE_LAYER = 0; // Idle_A/Walk/Attack は Base Layer にのみ存在するため明示指定する(Shapekeyレイヤーと分離)
        private const float MOVE_SPEED_THRESHOLD = 0.1f;
        private const float ATTACK_DURATION = 0.4166667f; // Attackクリップの長さ(秒)

        // ---- 内部状態 ------------------------------------
        private Animator _animator;
        private CharacterController _controller;
        private PhotonView _photonView;
        private bool _isWalking;
        private bool _isAttacking;
        private float _attackTimer;

        /// <summary>頭突き(Attack)アニメーション再生中かどうか。PlayerMover等が移動制限の判定に使う。</summary>
        public bool IsAttacking => _isAttacking;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _controller = GetComponent<CharacterController>();
            _photonView = GetComponent<PhotonView>();
        }

        private void Update()
        {
            if (_isAttacking)
            {
                _attackTimer -= Time.deltaTime;
                if (_attackTimer <= 0.0f)
                {
                    _isAttacking = false;
                    // 攻撃終了後、現在の移動状態に合わせてIdle/Walkへ戻す
                    ApplyMoveAnimation(HorizontalSpeed() > MOVE_SPEED_THRESHOLD);
                }
                return;
            }

            if (IsLocalInputAllowed() && TryReadAttackInput())
            {
                StartAttack();
                return;
            }

            bool isMoving = HorizontalSpeed() > MOVE_SPEED_THRESHOLD;
            if (isMoving == _isWalking) return;

            ApplyMoveAnimation(isMoving);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>PhotonViewが無い(オフライン単体テスト等)場合は許可、あれば自分の所有物のときだけ許可する</summary>
        private bool IsLocalInputAllowed()
        {
            return _photonView == null || _photonView.IsMine;
        }

        private bool TryReadAttackInput()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.jKey.wasPressedThisFrame;
        }

        private void StartAttack()
        {
            _isAttacking = true;
            _attackTimer = ATTACK_DURATION;
            _animator.CrossFade(ANIM_ATTACK, ANIM_CROSSFADE, ANIM_BASE_LAYER);
        }

        private void ApplyMoveAnimation(bool isMoving)
        {
            _isWalking = isMoving;
            _animator.CrossFade(isMoving ? ANIM_WALK : ANIM_IDLE, ANIM_CROSSFADE, ANIM_BASE_LAYER);
        }

        /// <summary>CharacterController が実際に動いた速度から水平成分だけを取り出す</summary>
        private float HorizontalSpeed()
        {
            Vector3 velocity = _controller.velocity;
            velocity.y = 0.0f;
            return velocity.magnitude;
        }
    }
}
