using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>通常攻撃ステート（攻撃タイプ判定でスタンプ攻撃以外が選ばれた場合の単発攻撃）</summary>
    public class GorillaStateNormalAttack : IGorillaState
    {
        private const float ATTACK_MOTION_TIME = 0.6f;

        private float _elapsedTime;
        private bool _hasApplyDamage;

        public void Enter(GorillaAI owner)
        {
            _elapsedTime = 0f;
            _hasApplyDamage = false;
            owner.PlayAnimation(GorillaAI.ANIM_NORMAL_ATTACK);
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            // @todo アニメーションの特定タイミングで一度だけ単発ダメージを発生させる
            if (!_hasApplyDamage && _elapsedTime >= ATTACK_MOTION_TIME * 0.5f)
            {
                _hasApplyDamage = true;
            }

            if (_elapsedTime >= ATTACK_MOTION_TIME)
            {
                owner.ChangeState(new GorillaStateStagger(owner.NormalAttackStaggerTime));
            }
        }

        public void Exit(GorillaAI owner)
        {
        }
    }
}
