using ProjectKMP.Player;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 薙ぎ払い攻撃ステート（正面の広い扇形を腕で薙ぎ払い、犬を弾き飛ばす攻撃）。
    /// 通常攻撃(頭突き)より判定の角度を広く取ってあり、背後以外のほぼ全方位に当たる。
    /// 予備動作(振りかぶり)→振り切り、という流れは通常攻撃と同じだが、
    /// 振り切り中は開いた手のひらモデル(SimpleHandsのエフェクト代用)を2本同時に、
    /// 右側・左側からそれぞれ正面へ向かって斜めに突き出し、正面で挟み込むように動かす。
    /// 単に横へ弧を描くのではなく、「体の外側・少し高い位置に構える→素早く正面・通常の高さへ
    /// 突き出しながら振り出す→突き出したまま一瞬止める(命中)→ゆっくり構えの位置へ引き戻す」
    /// という4段階のタイミングで動かし、パンチらしいメリハリを出している。
    /// </summary>
    public class GorillaStateSweepAttack : IGorillaState
    {
        /// <summary>予備動作（振りかぶり）の時間。攻撃モーションをスローで見せて溜める</summary>
        private const float WINDUP_TIME = 0.45f;

        /// <summary>予備動作中のアニメーション再生速度倍率(通常速度に対する割合)。小さいほどはっきり止まって見える</summary>
        private const float WINDUP_SPEED_MULTIPLIER = 0.15f;

        /// <summary>振りかぶり中の体の震え幅の最大値(メートル)。溜まるほど大きく震える</summary>
        private const float MAX_SHAKE_AMOUNT = 0.08f;

        /// <summary>振りかぶり中、上体を後ろへ反らす最大角度(度)。溜まるほど深く反り、前足が浮くほど大きく仰け反る</summary>
        private const float MAX_LEAN_BACK_ANGLE_DEG = 35.0f;

        /// <summary>薙ぎ払い(振り切り部分)の再生時間</summary>
        private const float ATTACK_MOTION_TIME = 0.5f;

        /// <summary>ダメージを発生させるタイミング(振り切りの進行度)。両拳が伸びきって止まる直後に合わせる</summary>
        private const float HIT_TIMING_RATIO = 0.4f;

        /// <summary>振り出し(角度・突き出し・高さとも)が完了する進行度。ここまでで一気に振り切る</summary>
        private const float SNAP_END_RATIO = 0.35f;

        /// <summary>伸ばしたまま止めておく(命中させる)進行度の終わり</summary>
        private const float HOLD_END_RATIO = 0.5f;

        /// <summary>畳んだ状態(構え)での前方オフセット比率(SweepFistEffectForwardOffsetに対する割合)。数値が大きいほど構え時に体の外側へ大きく離れる</summary>
        private const float FOLDED_OFFSET_RATIO = 0.55f;

        /// <summary>引き戻し終わりの伸び具合。完全には畳みきらず少し残す</summary>
        private const float RETRACT_END_RATIO = 0.15f;

        /// <summary>構え(振り始め・振り戻り後)で拳を配置する角度(度)。0が正面、90でちょうど体の真横。ゴリラの真横に構えさせたいのでほぼ90度にする</summary>
        private const float STANCE_SIDE_ANGLE_DEG = 90.0f;

        /// <summary>構え位置での高さの上乗せ(メートル)。正面へ突き出すにつれて通常の高さへ収束する</summary>
        private const float STANCE_HEIGHT_OFFSET = 0.35f;

        /// <summary>両手が正面で完全に重ならず、隙間を残して挟むようにするための収束後の角度(度)。右は+、左は-側に残す</summary>
        private const float MEET_HALF_ANGLE_DEG = 6.0f;

        /// <summary>
        /// 拳モデル自体が指先をローカルZ-方向・手のひらをローカルY-方向(床側)に向けているため、
        /// 指先を正面(Z+)へ向けたうえで、手のひらは中央(内側)を向くように補正する回転。
        /// 右の拳は手のひらが左(内側)、左の拳は手のひらが右(内側)を向くよう、左右で符号を変える。
        /// </summary>
        private static readonly Quaternion RIGHT_HAND_FORWARD_CORRECTION = Quaternion.Euler(0f, 180f, -90f);
        private static readonly Quaternion LEFT_HAND_FORWARD_CORRECTION = Quaternion.Euler(0f, 180f, 90f);

        private float _elapsedTime;
        private bool _hasSwungYet;
        private bool _hasApplyDamage;
        private float _baseAnimatorSpeed;
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;
        private GameObject _chargeEffectInstance;
        private GameObject _chargeAuraEffectInstance;
        private GameObject _rightHandAuraEffectInstance;
        private GameObject _leftHandAuraEffectInstance;
        private GameObject _rightFistEffectInstance;
        private GameObject _leftFistEffectInstance;

        public void Enter(GorillaAI owner)
        {
            _elapsedTime = 0f;
            _hasSwungYet = false;
            _hasApplyDamage = false;
            _originalPosition = owner.transform.position;
            _originalRotation = owner.transform.rotation;

            // 現在のAnimator再生速度を基準として保持しておき、予備動作の間だけ大きく落とす
            _baseAnimatorSpeed = owner.Animator.speed;

            // 専用の薙ぎ払いモーションが無いため、頭突きモーションを流用してスロー再生し、
            // 振りかぶりの予備動作として見せる(通常攻撃と同じ手法)
            owner.PlayAnimation(GorillaAI.ANIM_SWEEP_ATTACK);
            owner.Animator.speed = _baseAnimatorSpeed * WINDUP_SPEED_MULTIPLIER;

            // チャージ中のエフェクトを体に出す
            if (owner.SweepAttackChargeEffectPrefab != null)
            {
                Vector3 pos = owner.transform.position + Vector3.up * owner.SweepAttackChargeEffectHeight;
                _chargeEffectInstance = Object.Instantiate(owner.SweepAttackChargeEffectPrefab, pos, Quaternion.identity, owner.transform);
            }

            // 足元に「力を溜めている感」を出す魔法陣風のオーラエフェクトを重ねて出す
            if (owner.SweepAttackChargeAuraEffectPrefab != null)
            {
                Vector3 auraPos = owner.transform.position + Vector3.up * owner.SweepAttackChargeAuraHeight;
                _chargeAuraEffectInstance = Object.Instantiate(owner.SweepAttackChargeAuraEffectPrefab, auraPos, Quaternion.identity, owner.transform);

                var particleSystems = _chargeAuraEffectInstance.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in particleSystems)
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }

                _chargeAuraEffectInstance.transform.localScale = Vector3.one * owner.SweepAttackChargeAuraEffectScale;

                // 魔法陣本体(MagicCircle/Light)の位置は変えず、上昇する光の線(RiseLine/SmallRiseLine)だけを上へずらす
                foreach (Transform child in _chargeAuraEffectInstance.transform)
                {
                    if (child.name == "RiseLine" || child.name == "SmallRiseLine")
                    {
                        Vector3 localPos = child.localPosition;
                        localPos.y += owner.SweepAttackChargeAuraRiseLineHeightOffset;
                        child.localPosition = localPos;
                    }
                    else if (child.name == "MagicCircle")
                    {
                        // 元は寿命1秒に対して毎秒4個生成しており、円が4枚重なって見えるため、
                        // 寿命と同じ数(1秒に1個)まで発生数を落として円が1枚だけ見えるようにする
                        var magicCirclePs = child.GetComponent<ParticleSystem>();
                        if (magicCirclePs != null)
                        {
                            var magicCircleEmission = magicCirclePs.emission;
                            magicCircleEmission.rateOverTime = 1f;
                        }
                    }
                }
            }

            // 溜めている最中から拳モデル(手のひら)を構えの位置で出しておく。振り切り開始時に同じ関数で
            // 姿勢を更新し続けるので、ここでは初期の構えポーズ(ratio=0)を出すだけでよい
            SpawnFistEffects(owner);

            // 体だけでなく手のひらにもオーラを重ねて出し、力が拳に集まっている感を出す。
            // 拳モデルの子として出すことで、構え中の位置・向きに自動で追従する
            if (owner.SweepAttackChargeAuraEffectPrefab != null)
            {
                _rightHandAuraEffectInstance = SpawnHandAuraEffect(owner, _rightFistEffectInstance);
                _leftHandAuraEffectInstance = SpawnHandAuraEffect(owner, _leftFistEffectInstance);
            }
        }

        /// <summary>拳モデル1本の子として、手のひらを包む小さめのオーラエフェクトを出す</summary>
        private static GameObject SpawnHandAuraEffect(GorillaAI owner, GameObject fist)
        {
            if (fist == null) return null;

            var instance = Object.Instantiate(owner.SweepAttackChargeAuraEffectPrefab, fist.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            var particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in particleSystems)
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            instance.transform.localScale = Vector3.one * owner.SweepAttackHandAuraEffectScale;
            return instance;
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

                // 溜まるほど上体を後ろへ反らし、前足が浮くくらい大きく仰け反らせる
                float leanAngle = Mathf.Lerp(0f, MAX_LEAN_BACK_ANGLE_DEG, chargeRatio);
                owner.transform.rotation = _originalRotation * Quaternion.Euler(-leanAngle, 0f, 0f);

                if (_elapsedTime < WINDUP_TIME) return;

                // 予備動作が終わったので位置・回転・速度を戻し、実際の振り切り(両拳パンチ)に入る
                owner.transform.position = _originalPosition;
                owner.transform.rotation = _originalRotation;
                _hasSwungYet = true;
                owner.Animator.speed = _baseAnimatorSpeed;

                if (_chargeEffectInstance != null)
                {
                    Object.Destroy(_chargeEffectInstance);
                    _chargeEffectInstance = null;
                }

                if (_chargeAuraEffectInstance != null)
                {
                    Object.Destroy(_chargeAuraEffectInstance);
                    _chargeAuraEffectInstance = null;
                }

                // 溜め演出用の手のひらオーラも、振り切り(パンチ)に入るタイミングで消す
                if (_rightHandAuraEffectInstance != null)
                {
                    Object.Destroy(_rightHandAuraEffectInstance);
                    _rightHandAuraEffectInstance = null;
                }
                if (_leftHandAuraEffectInstance != null)
                {
                    Object.Destroy(_leftHandAuraEffectInstance);
                    _leftHandAuraEffectInstance = null;
                }

                // 拳モデル(手のひら)はEnter()で既に構えの位置に出ているので、ここでは何もしない。
                // 以降のUpdateでパンチの動きをつける
                return;
            }

            float swingElapsed = _elapsedTime - WINDUP_TIME;
            float swingRatio = Mathf.Clamp01(swingElapsed / ATTACK_MOTION_TIME);
            UpdateFistPunchVisual(owner, swingRatio);

            // 両拳が伸びきって止まる直後の瞬間に一度だけダメージとインパクトエフェクトを発生させる
            if (!_hasApplyDamage && swingElapsed >= ATTACK_MOTION_TIME * HIT_TIMING_RATIO)
            {
                _hasApplyDamage = true;
                SpawnImpactEffect(owner);
                TryApplyDamageToLocalPlayer(owner);
            }

            if (swingElapsed >= ATTACK_MOTION_TIME)
            {
                DestroyFistEffects();
                owner.ChangeState(new GorillaStateStagger(owner.SweepAttackStaggerTime));
            }
        }

        public void Exit(GorillaAI owner)
        {
            // 硬直等で早期に抜けた場合でも、スロー・震え・反り・エフェクトが残らないよう必ず後始末する
            owner.Animator.speed = _baseAnimatorSpeed;
            owner.transform.position = _originalPosition;
            owner.transform.rotation = _originalRotation;

            if (_chargeEffectInstance != null)
            {
                Object.Destroy(_chargeEffectInstance);
                _chargeEffectInstance = null;
            }

            if (_chargeAuraEffectInstance != null)
            {
                Object.Destroy(_chargeAuraEffectInstance);
                _chargeAuraEffectInstance = null;
            }

            if (_rightHandAuraEffectInstance != null)
            {
                Object.Destroy(_rightHandAuraEffectInstance);
                _rightHandAuraEffectInstance = null;
            }
            if (_leftHandAuraEffectInstance != null)
            {
                Object.Destroy(_leftHandAuraEffectInstance);
                _leftHandAuraEffectInstance = null;
            }

            DestroyFistEffects();
        }

        /// <summary>拳モデル(SimpleHandsのエフェクト代用)を左右2本、構えの位置で体の正面に出す</summary>
        private void SpawnFistEffects(GorillaAI owner)
        {
            if (owner.SweepFistEffectPrefab == null) return;

            Vector3 fistScale = new Vector3(
                owner.SweepFistEffectScale,
                owner.SweepFistEffectScale * owner.SweepFistEffectThicknessScale,
                owner.SweepFistEffectScale);

            _rightFistEffectInstance = Object.Instantiate(owner.SweepFistEffectPrefab, owner.transform);
            // モデルは左右で同じメッシュ(鏡像化されていない)ため、そのまま向かい合わせると
            // 親指と小指が向き合うなど指の対応が逆になってしまう。右手側だけモデルのローカルX軸を
            // 反転(鏡像化)することで、左手と親指同士・小指同士が正しく向かい合うようにする。
            _rightFistEffectInstance.transform.localScale = new Vector3(-fistScale.x, fistScale.y, fistScale.z);

            _leftFistEffectInstance = Object.Instantiate(owner.SweepFistEffectPrefab, owner.transform);
            _leftFistEffectInstance.transform.localScale = fistScale;

            UpdateFistPunchVisual(owner, 0f);
        }

        /// <summary>
        /// 拳モデル2本(右・左)を、それぞれの側からパンチのように動かす。ratioは振り切りの進行度(0=振り始め、1=振り終わり)。
        /// 右の拳は体の右側、左の拳は体の左側に構え、どちらも正面(角度0)へ向かって斜めに突き出す。
        /// 角度・高さ・突き出しをそれぞれ別のタイミングカーブで動かすことで、
        /// 「素早く正面へ振り出しながら突き出し、命中の瞬間だけ止め、その後は構えの位置へゆっくり引き戻す」
        /// というパンチらしい緩急を作る(引き戻し中に正面より外側へ逆走させないのがポイント)。
        /// </summary>
        private void UpdateFistPunchVisual(GorillaAI owner, float ratio)
        {
            float halfAngle = owner.SweepAttackHitAngle * 0.5f;
            float snapT = ComputeSnapRatio(ratio);
            float extendT = ComputeExtendRatio(ratio);

            // 構え(振り始め・振り戻り後)では正面より少し外側・上に位置し、
            // 振り出しにつれて正面(角度0)・通常の高さへ収束する
            float stanceAngle = STANCE_SIDE_ANGLE_DEG;
            float heightOffset = Mathf.Lerp(STANCE_HEIGHT_OFFSET, 0f, snapT);

            UpdateOneFist(_rightFistEffectInstance, owner, stanceAngle, MEET_HALF_ANGLE_DEG, snapT, extendT, heightOffset, isRightHand: false);
            UpdateOneFist(_leftFistEffectInstance, owner, -stanceAngle, -MEET_HALF_ANGLE_DEG, snapT, extendT, heightOffset, isRightHand: true);
        }

        /// <summary>拳1本ぶんの位置・向きを更新する。startAngleDegが構え位置の角度(度)で、そこから正面(角度0)へsnapTで収束する</summary>
        private static void UpdateOneFist(GameObject fist, GorillaAI owner, float startAngleDeg, float endAngleDeg, float snapT, float extendT, float heightOffset, bool isRightHand)
        {
            if (fist == null) return;

            float angleDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, snapT);
            Quaternion swingRotation = Quaternion.AngleAxis(angleDeg, Vector3.up);

            float forwardOffset = Mathf.Lerp(
                owner.SweepFistEffectForwardOffset * FOLDED_OFFSET_RATIO,
                owner.SweepFistEffectForwardOffset,
                extendT);

            Vector3 localOffset = Vector3.forward * forwardOffset + Vector3.up * (owner.SweepFistEffectHeight + heightOffset);
            fist.transform.localPosition = swingRotation * localOffset;
            // モデル自体は指先がローカルZ-側・手のひらがローカルY-側(床側)を向いているため、
            // 指先が前後逆にならずかつ手のひらが床を向かないよう、左右別の補正を掛ける
            Quaternion forwardCorrection = isRightHand ? RIGHT_HAND_FORWARD_CORRECTION : LEFT_HAND_FORWARD_CORRECTION;
            fist.transform.localRotation = swingRotation * forwardCorrection;
        }

        /// <summary>
        /// 振り出し(角度・高さ)の進行度。SNAP_END_RATIOまでに一気に振り切り(ease-in加速)、
        /// それ以降は引き戻し中も構えの外側へ戻さず、正面へ振り切った位置のまま保持する。
        /// </summary>
        private static float ComputeSnapRatio(float ratio)
        {
            if (ratio >= SNAP_END_RATIO) return 1f;

            float t = ratio / SNAP_END_RATIO;
            return t * t; // 加速しながら振り出す(ease-in)
        }

        /// <summary>
        /// 前後方向(突き出し)の進行度。加速しながら突き出し(ease-in)、命中の瞬間まで伸ばしたまま保持し、
        /// そのあとはゆっくり構えへ引き戻す(ease-out)。完全には畳みきらず少し伸びた状態で止める。
        /// </summary>
        private static float ComputeExtendRatio(float ratio)
        {
            if (ratio <= SNAP_END_RATIO)
            {
                float t = ratio / SNAP_END_RATIO;
                return t * t; // 加速しながら突き出す(ease-in)
            }

            if (ratio <= HOLD_END_RATIO)
            {
                return 1f; // 命中の瞬間、伸ばしたまま保持する
            }

            float tail = (ratio - HOLD_END_RATIO) / (1f - HOLD_END_RATIO);
            float eased = 1f - (1f - tail) * (1f - tail); // ゆっくり引き戻す(ease-out)
            return Mathf.Lerp(1f, RETRACT_END_RATIO, eased);
        }

        /// <summary>両拳が正面で合わさる瞬間に、両拳の間へインパクトエフェクトを出す。
        /// より衝撃感を出すため、通常のヒットエフェクトに、迫力のある2つ目のエフェクトを重ねて出す。</summary>
        private void SpawnImpactEffect(GorillaAI owner)
        {
            // 両拳の中間点(未生成の場合は正面の突き出し位置)にエフェクトを出す
            Vector3 pos;
            if (_rightFistEffectInstance != null && _leftFistEffectInstance != null)
            {
                pos = (_rightFistEffectInstance.transform.position + _leftFistEffectInstance.transform.position) * 0.5f;
            }
            else
            {
                pos = owner.transform.position
                    + owner.transform.forward * owner.SweepFistEffectForwardOffset
                    + Vector3.up * owner.SweepFistEffectHeight;
            }

            SpawnImpactEffectInstance(owner.SweepImpactEffectPrefab, owner.SweepImpactEffectScale, pos, owner.transform.rotation);
            SpawnImpactEffectInstance(owner.SweepImpactEffectPrefab2, owner.SweepImpactEffectScale2, pos, owner.transform.rotation);
        }

        /// <summary>インパクトエフェクトを1つ生成する共通処理</summary>
        private static void SpawnImpactEffectInstance(GameObject prefab, float scale, Vector3 pos, Quaternion rotation)
        {
            if (prefab == null) return;

            GameObject instance = Object.Instantiate(prefab, pos, rotation);

            // ScalingModeがShapeのパーティクルはTransform.localScaleを変えても大きさが反映されないため、
            // Hierarchyに切り替えてからscaleを適用する(スタンプ攻撃の衝撃波エフェクトと同じ対応)
            var particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in particleSystems)
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            instance.transform.localScale = Vector3.one * scale;
        }

        private void DestroyFistEffects()
        {
            if (_rightFistEffectInstance != null)
            {
                Object.Destroy(_rightFistEffectInstance);
                _rightFistEffectInstance = null;
            }

            if (_leftFistEffectInstance != null)
            {
                Object.Destroy(_leftFistEffectInstance);
                _leftFistEffectInstance = null;
            }
        }

        /// <summary>
        /// 自分が操作しているローカルプレイヤーだけを対象に、正面を中心とした広い扇形の当たり判定を取ってダメージを与える。
        /// (破壊光線と同じ方式。全クライアントで同じ処理が走るため、各自が自分のぶんだけ判定することで
        ///  多重ダメージを避ける。ダメージ自体は PlayerHealth の RPC で全員に同期される)
        /// </summary>
        private void TryApplyDamageToLocalPlayer(GorillaAI owner)
        {
            if (owner.SweepAttackDamage <= 0) return;

            PlayerAttack localAttack = PlayerAttack.Local;
            if (localAttack == null) return;

            PlayerHealth localHealth = localAttack.GetComponent<PlayerHealth>();
            if (localHealth == null || localHealth.IsDead) return;

            // 距離判定(水平)
            Vector3 toPlayer = localHealth.transform.position - owner.transform.position;
            toPlayer.y = 0f;
            float horizontalDistance = toPlayer.magnitude;
            if (horizontalDistance > owner.SweepAttackHitRange) return;

            // 正面を中心とした広い扇形の角度判定。ほぼ同一地点にいる場合は角度に関わらず命中扱い
            float angle = 0f;
            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                angle = Vector3.Angle(owner.transform.forward, toPlayer.normalized);
                if (angle > owner.SweepAttackHitAngle * 0.5f) return;
            }

            // 両手のひらが閉じるのは正面付近だけなので、挟み潰し(即死)は
            // 「ゴリラ本体からPalmCrushRadius以内」かつ「正面からPalmCrushAngleDeg以内」の両方を満たした場合だけにする。
            // 距離だけが近くても、正面から大きく横にズレている(=片手側でしか触れていない)場合は挟みきれないので、
            // 通常通り強めに吹き飛ばすだけにする
            if (horizontalDistance <= owner.SweepAttackPalmCrushRadius && angle <= owner.SweepAttackPalmCrushAngleDeg)
            {
                localHealth.ApplyCrushKill(-1, owner.transform.position);
                return;
            }

            // ゴリラの位置を発生源として渡し、反対方向へ薙ぎ払うように吹き飛ばす。
            // 薙ぎ払いは両手で叩き飛ばす一撃なので、通常の被弾よりずっと大きく、かつ上空も巻き込むように吹き飛ばす
            localHealth.ApplyDamage(owner.SweepAttackDamage, -1, owner.transform.position,
                owner.SweepAttackKnockbackDistance, owner.SweepAttackKnockbackDurationSec, owner.SweepAttackKnockbackArcHeight);
        }
    }
}
