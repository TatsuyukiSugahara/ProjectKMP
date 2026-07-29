using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>待機ステート。一定時間経過で自動的に徘徊ステートへ遷移する</summary>
    public class GorillaStateIdle : IGorillaState
    {
        private float _idleTime;
        private float _elapsedTime;

        public void Enter(GorillaAI owner)
        {
            _elapsedTime = 0f;
            _idleTime = Random.Range(owner.IdleTimeMin, owner.IdleTimeMax);
            owner.PlayAnimation(GorillaAI.ANIM_IDLE);
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;
            if (_elapsedTime >= _idleTime)
            {
                owner.ChangeState(new GorillaStatePatrol());
            }
        }

        public void Exit(GorillaAI owner)
        {
        }
    }
}
