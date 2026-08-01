using ProjectKMP.Attack;
using ProjectKMP.Player;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// スタンプ攻撃ステート（近距離 or 確率で選ばれる範囲攻撃）。
    /// その場で真上にジャンプし、頂点で溜めながらチャージ演出を見せてから地面に向かって落下、
    /// 着地の瞬間に範囲ダメージを発生させる。着地後、硬直ステートへ遷移する。
    /// </summary>
    public class GorillaStateStampAttack : IGorillaState
    {
        /// <summary>ジャンプの高さ（m）</summary>
        private const float RISE_HEIGHT = 3.0f;

        /// <summary>上昇にかける時間（秒）</summary>
        private const float RISE_TIME = 0.35f;

        /// <summary>頂点での溜め時間（秒）。チャージ演出を見せるため少し長めに取る</summary>
        private const float HOLD_TIME = 0.4f;

        /// <summary>落下にかける時間（秒）。上昇より短くして「落ちる」勢いを出す</summary>
        private const float FALL_TIME = 0.25f;

        /// <summary>着地後、硬直ステートへ遷移するまでの余韻（秒）</summary>
        private const float LANDING_RECOVERY_TIME = 0.2f;

        /// <summary>頂点で溜めている間の体の震え幅の最大値(メートル)</summary>
        private const float MAX_SHAKE_AMOUNT = 0.1f;

        private float _elapsedTime;
        private Vector3 _groundPosition;
        private bool _hasApplyDamage;
        private bool _hasPlayedFallAnim;
        private bool _hasSpawnedCharge;
        private GameObject _chargeEffectInstance;

        public void Enter(GorillaAI owner)
        {
            _elapsedTime = 0f;
            _hasApplyDamage = false;
            _hasPlayedFallAnim = false;
            _hasSpawnedCharge = false;
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
                // ---- 頂点での溜め：チャージ演出(震え + エフェクト) ----
                if (!_hasSpawnedCharge)
                {
                    _hasSpawnedCharge = true;
                    if (owner.StampAttackChargeEffectPrefab != null)
                    {
                        Vector3 pos = owner.transform.position + Vector3.up * owner.StampAttackChargeEffectHeight;
                        _chargeEffectInstance = Object.Instantiate(owner.StampAttackChargeEffectPrefab, pos, Quaternion.identity, owner.transform);
                    }
                }

                float holdElapsed = _elapsedTime - riseEnd;
                float chargeRatio = Mathf.Clamp01(holdElapsed / HOLD_TIME);
                Vector2 jitter = Random.insideUnitCircle * (MAX_SHAKE_AMOUNT * chargeRatio);
                SetHeight(owner, RISE_HEIGHT, new Vector3(jitter.x, 0f, jitter.y));

                if (!_hasPlayedFallAnim)
                {
                    _hasPlayedFallAnim = true;
                    // 落下開始のタイミングで踏みつけモーションに切り替える
                    owner.PlayAnimation(GorillaAI.ANIM_STAMP_ATTACK);
                }
            }
            else if (_elapsedTime <= fallEnd)
            {
                // 落下に入ったのでチャージエフェクトは消す
                if (_chargeEffectInstance != null)
                {
                    Object.Destroy(_chargeEffectInstance);
                    _chargeEffectInstance = null;
                }

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
                    SpawnImpactEffect(owner);

                    // 着地点に地面を抉った痕を残す。エフェクトと同じく全クライアントで
                    // 同じタイミングに呼ばれるため、追加の通信なしで全員の画面に痕が出る
                    AttackDecal.Spawn(owner.StampDecalPrefab, _groundPosition, owner.StampDecalDiameter);
                    TryApplyDamageToLocalPlayer(owner);
                    // @todo カメラシェイクは別途対応
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

            if (_chargeEffectInstance != null)
            {
                Object.Destroy(_chargeEffectInstance);
                _chargeEffectInstance = null;
            }
        }

        /// <summary>地面の位置を基準に高さと水平方向のズレ(震え用)を反映する</summary>
        private void SetHeight(GorillaAI owner, float height, Vector3 horizontalOffset = default)
        {
            Vector3 pos = _groundPosition + horizontalOffset;
            pos.y = _groundPosition.y + height;
            owner.transform.position = pos;
        }

        /// <summary>
        /// 自分が操作しているローカルプレイヤーだけを対象に、着地点を中心とした円形範囲の当たり判定を取って
        /// ダメージを与える。(破壊光線と同じ方式。全クライアントで同じ処理が走るため、各自が自分のぶんだけ
        /// 判定することで多重ダメージを避ける。ダメージ自体は PlayerHealth の RPC で全員に同期される)
        /// </summary>
        private void TryApplyDamageToLocalPlayer(GorillaAI owner)
        {
            if (owner.StampAttackDamage <= 0) return;

            PlayerAttack localAttack = PlayerAttack.Local;
            if (localAttack == null) return;

            PlayerHealth localHealth = localAttack.GetComponent<PlayerHealth>();
            if (localHealth == null || localHealth.IsDead) return;

            // 着地点からの水平距離で判定する
            Vector3 toPlayer = localHealth.transform.position - _groundPosition;
            toPlayer.y = 0f;
            if (toPlayer.magnitude > owner.StampAttackRadius) return;

            // 着地点を発生源として渡し、衝撃波の外側へ吹き飛ばす
            localHealth.ApplyDamage(owner.StampAttackDamage, -1, _groundPosition);
        }

        /// <summary>着地位置に衝撃波エフェクトを出す</summary>
        private void SpawnImpactEffect(GorillaAI owner)
        {
            if (owner.StampImpactEffectPrefab == null) return;
            var instance = Object.Instantiate(owner.StampImpactEffectPrefab, _groundPosition, Quaternion.identity);

            // ScalingMode が Shape のパーティクルは Transform.localScale を変えても大きさが反映されないため、
            // Hierarchy に切り替えてから scale を適用する
            var particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in particleSystems)
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            instance.transform.localScale = Vector3.one * owner.StampImpactEffectScale;
        }
    }
}
