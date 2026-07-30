using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 死亡ステート。ジタバタ→ひっくり返る→徐々に縮小、の順に演出した後、
    /// ゴリラのGameObjectを破棄する。
    /// </summary>
    public class GorillaStateDeath : IGorillaState
    {
        private enum Phase
        {
            Jitter,
            Flip,
            Shrink,
        }

        private const float JITTER_DURATION = 0.6f;
        private const float JITTER_AMOUNT = 0.08f;
        private const float JITTER_SPEED = 40.0f;

        private const float FLIP_DURATION = 0.5f;
        private const float FLIP_ANGLE = 90.0f; // 仰向けに倒れる角度(度)

        private const float SHRINK_DURATION = 0.8f;

        private Phase _phase;
        private float _elapsedTime;

        private Vector3 _basePosition;
        private Quaternion _baseRotation;
        private Vector3 _baseScale;

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
                case Phase.Flip:
                    UpdateFlip(owner);
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
                owner.transform.position = _basePosition;
                _phase = Phase.Flip;
                _elapsedTime = 0f;
            }
        }

        /// <summary>仰向けにひっくり返る</summary>
        private void UpdateFlip(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            float rate = Mathf.Clamp01(_elapsedTime / FLIP_DURATION);
            // X軸プラス方向だと上体が持ち上がって見えるため、マイナス方向に回転させて
            // まっすぐ後ろに倒れ込むようにする
            owner.transform.rotation = _baseRotation * Quaternion.Euler(-FLIP_ANGLE * rate, 0f, 0f);

            if (_elapsedTime >= FLIP_DURATION)
            {
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
