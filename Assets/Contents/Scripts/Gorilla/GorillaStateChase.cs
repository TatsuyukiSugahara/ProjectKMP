using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 追跡ステート（Player発見中）。追跡ループのハブとなる。
    /// ・見失う/索敵範囲外 → 徘徊へ遷移
    /// ・攻撃範囲内 → 攻撃タイプ判定（距離 or 確率）でスタンプ攻撃/通常攻撃へ遷移
    /// ・攻撃範囲外 → 何もせず追跡を継続
    /// 硬直ステートから戻ってくることで「硬直→再追跡」のループを構成する。
    /// </summary>
    public class GorillaStateChase : IGorillaState
    {
        public void Enter(GorillaAI owner)
        {
            owner.PlayAnimation(GorillaAI.ANIM_RUN);
        }

        public void Update(GorillaAI owner)
        {
            // 見失う / 索敵範囲外 → 徘徊へ戻る
            if (owner.IsPlayerLost())
            {
                owner.ChangeState(new GorillaStatePatrol());
                return;
            }

            if (owner.Target != null)
            {
                owner.MoveTowards(owner.Target.position, owner.ChaseSpeed);
            }

            // 攻撃範囲内？（距離判定）
            if (owner.IsPlayerInAttackRange())
            {
                // 攻撃タイプ判定（タイマー / 確率）
                if (owner.ShouldUseStampAttack())
                {
                    owner.ChangeState(new GorillaStateStampAttack());
                }
                else
                {
                    owner.ChangeState(new GorillaStateNormalAttack());
                }
            }

            // No（範囲外）→ 何もせず追跡を継続
        }

        public void Exit(GorillaAI owner)
        {
        }
    }
}
