using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 徘徊ステート。
    /// 「歩く→少し待機→歩く→少し待機…」のペースで巡回し、
    /// 索敵範囲内(かつ視野角内)にPlayerを発見したら追跡ステートへ遷移する。
    /// </summary>
    public class GorillaStatePatrol : IGorillaState
    {
        private const float ARRIVE_DISTANCE = 0.2f;

        /// <summary>移動中かどうか(falseの間は待機中)</summary>
        private bool _isMoving;

        private Vector3 _wanderTarget;
        private float _waitTimer;
        private float _waitDuration;

        public void Enter(GorillaAI owner)
        {
            StartMoving(owner);
        }

        public void Update(GorillaAI owner)
        {
            // 索敵範囲内(視野角内)にPlayerを発見したら追跡ステートへ遷移
            if (owner.IsPlayerFound())
            {
                owner.ChangeState(new GorillaStateChase());
                return;
            }

            if (_isMoving)
            {
                owner.MoveTowards(_wanderTarget, owner.PatrolSpeed);

                if (Vector3.Distance(owner.transform.position, _wanderTarget) <= ARRIVE_DISTANCE)
                {
                    StartWaiting(owner);
                }
            }
            else
            {
                _waitTimer += Time.deltaTime;
                if (_waitTimer >= _waitDuration)
                {
                    StartMoving(owner);
                }
            }
        }

        public void Exit(GorillaAI owner)
        {
        }

        /// <summary>次の巡回地点を決めて歩き出す</summary>
        private void StartMoving(GorillaAI owner)
        {
            _isMoving = true;
            _wanderTarget = PickWanderTarget(owner);
            owner.PlayAnimation(GorillaAI.ANIM_WALK);
        }

        /// <summary>その場で少しの間待機する</summary>
        private void StartWaiting(GorillaAI owner)
        {
            _isMoving = false;
            _waitTimer = 0f;
            _waitDuration = Random.Range(owner.PatrolWaitTimeMin, owner.PatrolWaitTimeMax);
            owner.PlayAnimation(GorillaAI.ANIM_IDLE);
        }

        private Vector3 PickWanderTarget(GorillaAI owner)
        {
            Vector2 offset = Random.insideUnitCircle * owner.WanderRadius;
            return owner.HomePosition + new Vector3(offset.x, 0f, offset.y);
        }
    }
}
