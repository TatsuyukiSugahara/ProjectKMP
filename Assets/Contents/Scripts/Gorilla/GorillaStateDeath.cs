using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 死亡ステート。ジタバタ→Deathアニメーションを再生しながら床(原点の高さ)まで
    /// 徐々に沈める→アニメーションを途中の姿勢で停止→徐々に縮小、の順に演出する。
    /// </summary>
    public class GorillaStateDeath : IGorillaState
    {
        private enum Phase
        {
            Jitter,
            Settle,
            Shrink,
        }

        private const float JITTER_DURATION = 0.6f;
        private const float JITTER_AMOUNT = 0.08f;
        private const float JITTER_SPEED = 40.0f;

        /// <summary>床(原点の高さ)まで沈める時間(秒)。この間はDeathアニメーションを再生し続ける</summary>
        private const float SETTLE_DURATION = 0.6f;

        private const float SHRINK_DURATION = 0.8f;

        private Phase _phase;
        private float _elapsedTime;

        private Vector3 _basePosition;
        private Quaternion _baseRotation;
        private Vector3 _baseScale;

        private Vector3 _settleStartPosition;
        private Vector3 _settleTargetPosition;

        public void Enter(GorillaAI owner)
        {
            _phase = Phase.Jitter;
            _elapsedTime = 0f;

            _basePosition = owner.transform.position;
            _baseRotation = owner.transform.rotation;
            _baseScale = owner.transform.localScale;

            // 復活(デバッグ用のIキートグル)に備えて、死亡前の状態をGorillaAI側にも記録させる
            owner.NotifyDeathStarted();

            owner.PlayAnimation(GorillaAI.ANIM_DEATH);
        }

        public void Update(GorillaAI owner)
        {
            switch (_phase)
            {
                case Phase.Jitter:
                    UpdateJitter(owner);
                    break;
                case Phase.Settle:
                    UpdateSettle(owner);
                    break;
                case Phase.Shrink:
                    UpdateShrink(owner);
                    break;
            }
        }

        public void Exit(GorillaAI owner)
        {
        }

        /// <summary>小刻みに左右へ暴れる</summary>
        private void UpdateJitter(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            float shakeX = Mathf.Sin(_elapsedTime * JITTER_SPEED) * JITTER_AMOUNT;
            float shakeZ = Mathf.Cos(_elapsedTime * JITTER_SPEED * 1.3f) * JITTER_AMOUNT;
            owner.transform.position = _basePosition + new Vector3(shakeX, 0f, shakeZ);

            if (_elapsedTime >= JITTER_DURATION)
            {
                _settleStartPosition = _basePosition;
                _settleTargetPosition = new Vector3(_basePosition.x, 0f, _basePosition.z);

                _phase = Phase.Settle;
                _elapsedTime = 0f;
            }
        }

        /// <summary>Deathアニメーションを再生させたまま、床(原点の高さ)まで徐々に沈める</summary>
        private void UpdateSettle(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            float rate = Mathf.Clamp01(_elapsedTime / SETTLE_DURATION);
            float eased = rate * rate * (3f - 2f * rate); // smoothstep
            owner.transform.position = Vector3.Lerp(_settleStartPosition, _settleTargetPosition, eased);

            if (_elapsedTime >= SETTLE_DURATION)
            {
                owner.transform.position = _settleTargetPosition;
                _basePosition = _settleTargetPosition;

                // 強制的に別の姿勢へジャンプさせると、沈みながら自然に回転していたポーズと
                // ズレて見えてしまうため、ここでは正規化時間を指定し直さず、Settleで自然に
                // 再生が進んだそのままの姿勢で速度だけ0にして止める
                if (owner.Animator != null)
                {
                    owner.Animator.speed = 0f;
                }

                _phase = Phase.Shrink;
                _elapsedTime = 0f;
            }
        }

        /// <summary>
        /// 徐々に小さくなりながら消滅する。
        /// 復活(デバッグ用のIキートグル)でGameObjectを再利用するため、
        /// ここではDestroyせず縮小しきった状態で待機する。
        /// </summary>
        private void UpdateShrink(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            float rate = Mathf.Clamp01(_elapsedTime / SHRINK_DURATION);
            owner.transform.localScale = _baseScale * (1f - rate);
        }
    }
}
