using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 硬直（隙）ステート。スタンプ攻撃・通常攻撃の両方から遷移してくる共通ステート。
    /// 硬直時間はコンストラクタで受け取り、攻撃の種類によって長さを変える。
    /// </summary>
    public class GorillaStateStagger : IGorillaState
    {
        private readonly float _staggerTime;
        private float _elapsedTime;

        public GorillaStateStagger(float staggerTime)
        {
            _staggerTime = staggerTime;
        }

        public void Enter(GorillaAI owner)
        {
            _elapsedTime = 0f;

            // @note Hitアニメーション（のけぞりモーション）は見た目が不自然なため使用しない。
            //       攻撃後の隙をIdle_Aで表現する。専用の硬直モーションがあれば差し替える。
            owner.PlayAnimation(GorillaAI.ANIM_IDLE);
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;
            if (_elapsedTime < _staggerTime)
            {
                return;
            }

            // 硬直後、再追跡へ（見失っていた場合は徘徊へ）
            if (owner.IsPlayerLost())
            {
                owner.ChangeState(new GorillaStatePatrol());
            }
            else
            {
                owner.ChangeState(new GorillaStateChase());
            }
        }

        public void Exit(GorillaAI owner)
        {
        }
    }
}
