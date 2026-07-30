using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>通常攻撃ステート（攻撃タイプ判定でスタンプ攻撃以外が選ばれた場合の単発攻撃）</summary>
    public class GorillaStateNormalAttack : IGorillaState
    {
        /// <summary>予備動作（振りかぶり）の時間。攻撃モーションをスローで見せて溜める</summary>
        private const float WINDUP_TIME = 0.5f;

        /// <summary>予備動作中のアニメーション再生速度倍率(通常速度に対する割合)。小さいほどはっきり止まって見える</summary>
        private const float WINDUP_SPEED_MULTIPLIER = 0.1f;

        /// <summary>振りかぶり中の体の震え幅の最大値(メートル)。溜まるほど大きく震える</summary>
        private const float MAX_SHAKE_AMOUNT = 0.08f;

        /// <summary>攻撃モーション自体(振り切り部分)の再生時間</summary>
        private const float ATTACK_MOTION_TIME = 0.6f;

        private float _elapsedTime;
        private bool _hasSwungYet;
        private bool _hasApplyDamage;
        private float _baseAnimatorSpeed;
        private Vector3 _originalPosition;
        private GameObject _chargeEffectInstance;

        public void Enter(GorillaAI owner)
        {
            _elapsedTime = 0f;
            _hasSwungYet = false;
            _hasApplyDamage = false;
            _originalPosition = owner.transform.position;

            // 現在のAnimator再生速度を基準として保持しておき、予備動作の間だけ大きく落とす
            _baseAnimatorSpeed = owner.Animator.speed;

            // 攻撃モーション自体をスローで再生することで、振りかぶりの予備動作として見せる
            owner.PlayAnimation(GorillaAI.ANIM_NORMAL_ATTACK);
            owner.Animator.speed = _baseAnimatorSpeed * WINDUP_SPEED_MULTIPLIER;

            // チャージ中のエフェクトを体に出す
            if (owner.NormalAttackChargeEffectPrefab != null)
            {
                Vector3 pos = owner.transform.position + Vector3.up * owner.NormalAttackChargeEffectHeight;
                _chargeEffectInstance = Object.Instantiate(owner.NormalAttackChargeEffectPrefab, pos, Quaternion.identity, owner.transform);
            }
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            if (!_hasSwungYet)
            {
                // 溜まるほど震えが大きくなる(チャージ感の演出)
                float chargeRatio = Mathf.Clamp01(_elapsedTime / WINDUP_TIME);
                Vector2 jitter = Random.insideUnitCircle * (MAX_SHAKE_AMOUNT * chargeRatio);
                owner.transform.position = _originalPosition + new Vector3(jitter.x, 0f, jitter.y);

                if (_elapsedTime < WINDUP_TIME) return;

                // 予備動作が終わったので位置と速度を戻し、実際の振り切りに入る
                owner.transform.position = _originalPosition;
                _hasSwungYet = true;
                owner.Animator.speed = _baseAnimatorSpeed;

                if (_chargeEffectInstance != null)
                {
                    Object.Destroy(_chargeEffectInstance);
                    _chargeEffectInstance = null;
                }

                // 溜めた力を頭突きとして解放する瞬間のエフェクト
                if (owner.NormalAttackSwingEffectPrefab != null)
                {
                    Vector3 pos = owner.transform.position + Vector3.up * owner.NormalAttackChargeEffectHeight;
                    Object.Instantiate(owner.NormalAttackSwingEffectPrefab, pos, owner.transform.rotation);
                }
                return;
            }

            float swingElapsed = _elapsedTime - WINDUP_TIME;

            // @todo アニメーションの特定タイミングで一度だけ単発ダメージを発生させる
            if (!_hasApplyDamage && swingElapsed >= ATTACK_MOTION_TIME * 0.5f)
            {
                _hasApplyDamage = true;

                // 命中の瞬間のヒットエフェクト
                if (owner.NormalAttackHitEffectPrefab != null)
                {
                    Vector3 pos = owner.transform.position + owner.transform.forward * owner.NormalAttackHitEffectForwardOffset + Vector3.up * owner.NormalAttackChargeEffectHeight;
                    var hitInstance = Object.Instantiate(owner.NormalAttackHitEffectPrefab, pos, owner.transform.rotation);

                    // ScalingMode が Shape のパーティクルは Transform.localScale を変えても大きさが反映されないため、
                    // Hierarchy に切り替えてから scale を適用する
                    var hitParticleSystems = hitInstance.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (var hitPs in hitParticleSystems)
                    {
                        var hitMain = hitPs.main;
                        hitMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    }

                    hitInstance.transform.localScale = Vector3.one * owner.NormalAttackHitEffectScale;
                }
            }

            if (swingElapsed >= ATTACK_MOTION_TIME)
            {
                owner.ChangeState(new GorillaStateStagger(owner.NormalAttackStaggerTime));
            }
        }

        public void Exit(GorillaAI owner)
        {
            // 硬直等で早期に抜けた場合でも、スロー・震え・エフェクトが残らないよう必ず後始末する
            owner.Animator.speed = _baseAnimatorSpeed;
            owner.transform.position = _originalPosition;

            if (_chargeEffectInstance != null)
            {
                Object.Destroy(_chargeEffectInstance);
                _chargeEffectInstance = null;
            }
        }
    }
}
