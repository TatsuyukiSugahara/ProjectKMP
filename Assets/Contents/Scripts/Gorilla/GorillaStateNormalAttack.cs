using ProjectKMP.Player;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 通常攻撃ステート（攻撃タイプ判定でスタンプ攻撃以外が選ばれた場合の頭突き）。
    ///
    /// 連撃数を渡すと、振り切ったあと硬直へ行かずにもう一度自分へ遷移して連続で殴る。
    /// 2撃目以降は振りかぶりを短くして畳みかけ、最後の1撃のあとだけ通常どおり硬直するので、
    /// 「連撃を耐えきれば大きな反撃チャンスが来る」という読み合いになる。
    /// </summary>
    public class GorillaStateNormalAttack : IGorillaState
    {
        /// <summary>予備動作（振りかぶり）の時間。攻撃モーションをスローで見せて溜める</summary>
        private const float WINDUP_TIME = 0.5f;

        /// <summary>2撃目以降の振りかぶり時間(秒)。1撃目より短くして連続で来ている感を出す</summary>
        private const float FOLLOW_UP_WINDUP_TIME = 0.22f;

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
        private GorillaAttackTelegraph _telegraph;

        /// <summary>この一撃のあと、あと何回続けて殴るか</summary>
        private readonly int _comboRemaining;

        /// <summary>連撃の2撃目以降か。振りかぶりの長さが変わる</summary>
        private readonly bool _isFollowUp;

        /// <summary>この一撃の振りかぶり時間</summary>
        private float WindupTime => _isFollowUp ? FOLLOW_UP_WINDUP_TIME : WINDUP_TIME;

        /// <summary>単発の頭突き</summary>
        public GorillaStateNormalAttack() : this(0, false)
        {
        }

        /// <summary>
        /// 連撃つきの頭突き。
        /// </summary>
        /// <param name="comboRemaining">この一撃のあと、続けて殴る回数</param>
        /// <param name="isFollowUp">2撃目以降なら true。振りかぶりが短くなる</param>
        public GorillaStateNormalAttack(int comboRemaining, bool isFollowUp)
        {
            _comboRemaining = Mathf.Max(0, comboRemaining);
            _isFollowUp = isFollowUp;
        }

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

            // 当たる範囲(正面の扇形)を地面に出す。振りかぶりに入った時点で向きは固定されているので、
            // 最初から「もう曲がらない」色で出して、逃げる方向をすぐ判断できるようにする
            _telegraph = GorillaAttackTelegraph.SpawnSector(
                owner.MeleeTelegraphPrefab, owner.transform.position, owner.transform.eulerAngles.y,
                owner.NormalAttackHitRange, owner.NormalAttackHitAngle);
            if (_telegraph != null) _telegraph.SetLocked(true);

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
                float chargeRatio = Mathf.Clamp01(_elapsedTime / WindupTime);
                Vector2 jitter = Random.insideUnitCircle * (MAX_SHAKE_AMOUNT * chargeRatio);
                owner.transform.position = _originalPosition + new Vector3(jitter.x, 0f, jitter.y);

                if (_elapsedTime < WindupTime) return;

                // 予備動作が終わったので位置と速度を戻し、実際の振り切りに入る
                owner.transform.position = _originalPosition;
                _hasSwungYet = true;
                owner.Animator.speed = _baseAnimatorSpeed;

                // 振り切りに入ったら予測は役目を終える
                GorillaAttackTelegraph.Dismiss(_telegraph);
                _telegraph = null;

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

            float swingElapsed = _elapsedTime - WindupTime;

            // 振り切りの中間(ヒットエフェクトと同じタイミング)で一度だけ単発ダメージを発生させる
            if (!_hasApplyDamage && swingElapsed >= ATTACK_MOTION_TIME * 0.5f)
            {
                _hasApplyDamage = true;
                TryApplyDamageToLocalPlayer(owner);

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
                // まだ連撃が残っていれば硬直を挟まずにもう一度殴る。
                // 硬直に入るのは最後の1撃のあとだけなので、そこが反撃のタイミングになる
                if (_comboRemaining > 0)
                {
                    owner.ChangeState(new GorillaStateNormalAttack(_comboRemaining - 1, true));
                    return;
                }

                owner.ChangeState(new GorillaStateStagger(owner.NormalAttackStaggerTime));
            }
        }

        /// <summary>
        /// 自分が操作しているローカルプレイヤーだけを対象に、正面扇形の当たり判定を取ってダメージを与える。
        /// (破壊光線と同じ方式。全クライアントで同じ処理が走るため、各自が自分のぶんだけ判定することで
        ///  多重ダメージを避ける。ダメージ自体は PlayerHealth の RPC で全員に同期される)
        /// </summary>
        private void TryApplyDamageToLocalPlayer(GorillaAI owner)
        {
            if (owner.NormalAttackDamage <= 0) return;

            PlayerAttack localAttack = PlayerAttack.Local;
            if (localAttack == null) return;

            PlayerHealth localHealth = localAttack.GetComponent<PlayerHealth>();
            if (localHealth == null || localHealth.IsDead) return;

            // 距離判定(水平)
            Vector3 toPlayer = localHealth.transform.position - owner.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.magnitude > owner.NormalAttackHitRange) return;

            // 正面を中心とした扇形の角度判定。ほぼ同一地点にいる場合は角度に関わらず命中扱い
            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                float angle = Vector3.Angle(owner.transform.forward, toPlayer.normalized);
                if (angle > owner.NormalAttackHitAngle * 0.5f) return;
            }

            // ゴリラの位置を発生源として渡し、反対方向へ吹き飛ばす
            localHealth.ApplyDamage(owner.NormalAttackDamage, -1, owner.transform.position);
        }

        public void Exit(GorillaAI owner)
        {
            // 硬直等で早期に抜けた場合でも、スロー・震え・エフェクトが残らないよう必ず後始末する
            owner.Animator.speed = _baseAnimatorSpeed;
            owner.transform.position = _originalPosition;

            GorillaAttackTelegraph.Dismiss(_telegraph);
            _telegraph = null;

            if (_chargeEffectInstance != null)
            {
                Object.Destroy(_chargeEffectInstance);
                _chargeEffectInstance = null;
            }
        }
    }
}
