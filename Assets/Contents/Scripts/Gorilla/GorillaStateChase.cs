using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 追跡ステート（Player発見中）。追跡ループのハブとなる。
    ///
    /// ゴリラの足はプレイヤーより遅く、走って逃げる相手には追いつけない。
    /// そこで「追いかけて殴る」だけにせず、距離帯ごとに違う手札を切ることで、
    /// どの距離にいても攻撃が飛んでくる状態を作る。
    ///
    ///   0〜攻撃範囲   … 頭突き(連撃) / 薙ぎ払い / スタンプ  (既存の近接)
    ///   〜突進の射程  … 突進。一気に間合いを詰める
    ///   〜連打の射程  … 連続パンチ。打ちながらじりじり詰めてくる
    ///   〜光線の射程  … 破壊光線
    ///   〜地割れの射程… 地割れ。線で場所を奪う
    ///   〜跳躍の射程  … 跳びかかり。縦に跳び越えて逃げた先へ落ちてくる
    ///   〜岩投げの射程… 岩投げ。遠くへ逃げても安全ではない
    /// 近距離ではさらに、クールタイム明けなら掴み(1人を拘束する技)が優先される。
    ///   それ以外      … 走って距離を詰める
    ///
    /// どれも溜めの間に狙いが固定されるので、見てから避けられる。
    /// 硬直ステートから戻ってくることで「攻撃→硬直→再追跡」のループを構成する。
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

            // ---- 近距離: 既存の近接技 ----------------------
            if (owner.IsPlayerInAttackRange())
            {
                // 掴みは近接の中でも別格の技。クールタイムが長いぶん優先して出す
                if (owner.CanUseGrab && owner.IsPlayerInGrabRange() && owner.ShouldUseGrab())
                {
                    owner.ChangeState(new GorillaStateGrab());
                    return;
                }

                ChooseMeleeAttack(owner);
                return;
            }

            // ---- 中距離: 突進 ------------------------------
            // 追いつけない相手を捕まえるための主力。近接圏より外にいるときだけ使う
            if (owner.IsPlayerInChargeRange() && owner.CanUseChargeAttack && owner.ShouldUseChargeAttack())
            {
                owner.ChangeState(new GorillaStateChargeAttack());
                return;
            }

            // ---- 中距離: 連続パンチ ------------------------
            // 突進が「一撃で間合いを消す」のに対して、こちらは下がる相手を押し続ける
            if (owner.IsPlayerInRushPunchRange() && owner.CanUseRushPunch && owner.ShouldUseRushPunch())
            {
                owner.ChangeState(new GorillaStateRushPunch());
                return;
            }

            // ---- 中距離: 破壊光線 --------------------------
            if (owner.IsPlayerInBeamRange() && owner.CanUseBeamAttack && owner.ShouldUseBeamAttack())
            {
                owner.ChangeState(new GorillaStateBeamAttack());
                return;
            }

            // ---- 中遠距離: 地割れ --------------------------
            // 線で場所を奪う技。円や扇形とは逃げ方が変わるので、立ち位置を考えさせられる
            if (owner.IsPlayerInFissureRange() && owner.CanUseFissure && owner.ShouldUseFissure())
            {
                owner.ChangeState(new GorillaStateFissure());
                return;
            }

            // ---- 遠中距離: 跳びかかり ----------------------
            // 横ではなく縦に間合いを詰める。逃げた先に落ちてくる
            if (owner.IsPlayerInPounceRange() && owner.CanUsePounce && owner.ShouldUsePounce())
            {
                owner.ChangeState(new GorillaStatePounce());
                return;
            }

            // ---- 遠距離: 岩投げ ----------------------------
            // 遠くまで走って逃げれば安全、という状態をなくす
            if (owner.IsPlayerInRockThrowRange() && owner.CanUseRockThrow && owner.ShouldUseRockThrow())
            {
                owner.ChangeState(new GorillaStateRockThrow());
                return;
            }

            // どの手札も選ばれなかった → そのまま追跡を継続
        }

        public void Exit(GorillaAI owner)
        {
        }

        /// <summary>近距離での攻撃の選択。向きが合わないときは確実に捉えられる技を選ぶ</summary>
        private void ChooseMeleeAttack(GorillaAI owner)
        {
            if (owner.ShouldUseStampAttack())
            {
                owner.ChangeState(new GorillaStateStampAttack());
                return;
            }

            if (owner.IsTargetOutsideNormalAttackCone() || owner.ShouldUseSweepAttack())
            {
                // 通常攻撃(頭突き)の正面扇形からは外れているが、薙ぎ払いの広い扇形には
                // まだ届く側面・斜め後方にいる場合は、確実に薙ぎ払い攻撃で捉える。
                // 正面にいる場合は確率で薙ぎ払いを混ぜる
                owner.ChangeState(new GorillaStateSweepAttack());
                return;
            }

            // 頭突きはフェーズが進むほど連撃数が増える
            owner.ChangeState(new GorillaStateNormalAttack(owner.RollNormalAttackComboCount(), false));
        }
    }
}
