using UnityEngine;

namespace ProjectKMP.Dog
{
    /// <summary>
    /// 犬(Husky)の歩行/待機アニメーション再生。
    /// PlayerMover 等が CharacterController を動かした結果の速度を見て切り替えるだけなので、
    /// 移動ロジック側(PlayerMover)には手を加えない。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(CharacterController))]
    public class DogAnimationDriver : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------
        private const string ANIM_IDLE = "Idle_A";
        private const string ANIM_WALK = "Walk";
        private const float ANIM_CROSSFADE = 0.2f;
        private const int ANIM_BASE_LAYER = 0; // Idle_A/Walk は Base Layer にのみ存在するため明示指定する(Shapekeyレイヤーと分離)
        private const float MOVE_SPEED_THRESHOLD = 0.1f;

        // ---- 内部状態 ------------------------------------
        private Animator _animator;
        private CharacterController _controller;
        private bool _isWalking;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            bool isMoving = HorizontalSpeed() > MOVE_SPEED_THRESHOLD;
            if (isMoving == _isWalking) return;

            _isWalking = isMoving;
            _animator.CrossFade(isMoving ? ANIM_WALK : ANIM_IDLE, ANIM_CROSSFADE, ANIM_BASE_LAYER);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>CharacterController が実際に動いた速度から水平成分だけを取り出す</summary>
        private float HorizontalSpeed()
        {
            Vector3 velocity = _controller.velocity;
            velocity.y = 0.0f;
            return velocity.magnitude;
        }
    }
}
