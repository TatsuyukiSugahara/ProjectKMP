using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// スタンプ攻撃ステート（近距離 or 確率で選ばれる範囲攻撃）。
    /// その場で真上にジャンプし、頂点で少し溜めてから地面に向かって落下、
    /// 着地の瞬間に範囲ダメージを発生させる。着地後、硬直ステートへ遷移する。
    /// </summary>
    public class GorillaStateStampAttack : IGorillaState
    {
        /// <summary>ジャンプの高さ（m）</summary>
        private const float RISE_HEIGHT = 3.0f;

        /// <summary>上昇にかける時間（秒）</summary>
        private const float RISE_TIME = 0.35f;

        /// <summary>頂点での溜め時間（秒）</summary>
        private const float HOLD_TIME = 0.15f;

        /// <summary>落下にかける時間（秒）。上昇より短くして「落ちる」勢いを出す</summary>
        private const float FALL_TIME = 0.25f;

        /// <summary>着地後、硬直ステートへ遷移するまでの余韻（秒）</summary>
        private const float LANDING_RECOVERY_TIME = 0.2f;

        private float _elapsedTime;
        private Vector3 _groundPosition;
        private bool _hasApplyDamage;
        private bool _hasPlayedFallAnim;

        public void Enter(GorillaAI owner)
        {
            _elapsedTime = 0f;
            _hasApplyDamage = false;
            _hasPlayedFallAnim = false;
            _groundPosition = owner.transform.position;

            // 上昇開始（ジャンプモーション）
            owner.PlayAnimation(GorillaAI.ANIM_JUMP);
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            float riseEnd = RISE_TIME;
            float holdEnd = riseEnd + HOLD_TIME;
            float fallEnd = holdEnd + FALL_TIME;
            float recoverEnd = fallEnd + LANDING_RECOVERY_TIME;

            if (_elapsedTime <= riseEnd)
            {
                // ---- 上昇フェーズ：減速しながら上に伸びる ----
                float t = _elapsedTime / RISE_TIME;
                float eased = 1f - (1f - t) * (1f - t); // ease-out
                SetHeight(owner, Mathf.Lerp(0f, RISE_HEIGHT, eased));
            }
            else if (_elapsedTime <= holdEnd)
            {
                // ---- 頂点での溜め ----
                SetHeight(owner, RISE_HEIGHT);

                if (!_hasPlayedFallAnim)
                {
                    _hasPlayedFallAnim = true;
                    // 落下開始のタイミングで踏みつけモーションに切り替える
                    owner.PlayAnimation(GorillaAI.ANIM_STAMP_ATTACK);
                }
            }
            else if (_elapsedTime <= fallEnd)
            {
                // ---- 落下フェーズ：重力のように加速しながら落ちる ----
                float t = (_elapsedTime - holdEnd) / FALL_TIME;
                float eased = t * t; // ease-in（加速）
                SetHeight(owner, Mathf.Lerp(RISE_HEIGHT, 0f, eased));
            }
            else
            {
                // ---- 着地 ----
                SetHeight(owner, 0f);

                if (!_hasApplyDamage)
                {
                    _hasApplyDamage = true;
                    // @todo 着地の瞬間に範囲ダメージ・地面エフェクト・カメラシェイクなどを発生させる
                }

                if (_elapsedTime >= recoverEnd)
                {
                    owner.ChangeState(new GorillaStateStagger(owner.StampAttackStaggerTime));
                }
            }
        }

        public void Exit(GorillaAI owner)
        {
            // 位置ズレが残らないよう、地面の高さに戻しておく
            SetHeight(owner, 0f);
        }

        /// <summary>地面の位置を基準に高さだけを変更する</summary>
        private void SetHeight(GorillaAI owner, float height)
        {
            Vector3 pos = _groundPosition;
            pos.y = _groundPosition.y + height;
            owner.transform.position = pos;
        }
    }
}
