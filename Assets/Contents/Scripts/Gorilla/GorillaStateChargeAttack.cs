using ProjectKMP.Player;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 突進攻撃ステート（中距離）。
    ///
    /// ゴリラの移動速度はプレイヤーより遅く、走って逃げる相手には永遠に追いつけない。
    /// そこを埋めるための技で、一気に間合いを詰めて殴る。
    ///
    /// 流れは「溜め → 突進 → 急旋回 → 溜め → 突進 …」の繰り返し。
    /// 走り抜けて終わりではなく振り向いてもう一度来るので、避けた後も気が抜けない。
    /// 繰り返す回数はフェーズが進むほど増え、最後の1回のあとにだけ長い硬直が残る。
    ///
    /// 溜めの間は進路を地面に表示し、後半で狙いを固定する(表示が赤く速く点滅する)。
    /// 固定されてからは横へ逃げれば必ずかわせるので、「見てから避けて殴り返す」が成立する。
    /// </summary>
    public class GorillaStateChargeAttack : IGorillaState
    {
        /// <summary>溜め中のアニメーション再生速度倍率。ほぼ止めて「タメている」ことを見せる</summary>
        private const float WINDUP_SPEED_MULTIPLIER = 0.1f;

        /// <summary>溜め中の体の震え幅の最大値(メートル)</summary>
        private const float MAX_SHAKE_AMOUNT = 0.1f;

        /// <summary>突進中のアニメーション再生速度倍率。走りを速回しして勢いを出す</summary>
        private const float DASH_SPEED_MULTIPLIER = 2.5f;

        /// <summary>溜めのうち、後ろへ反りきるまでの割合。ここまでは沈み込みながら狙いを合わせる</summary>
        private const float LEAN_BACK_END_RATIO = 0.35f;

        /// <summary>急旋回中、滑りながら進む速さの割合(突進速度に対する)。急停止に見えないための余韻</summary>
        private const float TURN_SKID_SPEED_RATIO = 0.35f;

        /// <summary>急旋回で振り向く速さ(度/秒)</summary>
        private const float TURN_SPEED_DEG = 360.0f;

        /// <summary>突進が終わってから硬直へ移るまでの余韻(秒)。急停止の一拍</summary>
        private const float BRAKE_TIME = 0.15f;

        private enum Phase
        {
            /// <summary>溜め。後ろへ反りながら狙いを定める</summary>
            Windup,
            /// <summary>突進。前傾のまま走る</summary>
            Dash,
            /// <summary>急旋回。次の突進に向けて振り向く</summary>
            Turn,
            /// <summary>急停止の余韻</summary>
            Brake,
        }

        private Phase _phase;
        private float _elapsedTime;
        private float _baseAnimatorSpeed;

        /// <summary>水平の向き(度)。体はX軸に傾けるので、進行方向はこの角度から作る</summary>
        private float _yawDeg;

        /// <summary>いまのX軸の傾き(度)。マイナスで後傾、プラスで前傾</summary>
        private float _leanAngleDeg;

        /// <summary>この突進の開始位置。溜め中の震えの基準にする</summary>
        private Vector3 _roundStartPosition;

        /// <summary>何回目の突進か(0始まり)</summary>
        private int _roundIndex;

        /// <summary>この技で何回突進するか</summary>
        private int _totalRounds;

        private float _dashedDistance;
        private float _dashDistanceThisRound;
        private bool _hasHit;
        private bool _hasLockedAim;

        private GameObject _chargeEffectInstance;
        private GorillaAttackTelegraph _telegraph;

        public void Enter(GorillaAI owner)
        {
            _baseAnimatorSpeed = owner.Animator != null ? owner.Animator.speed : 1.0f;
            _yawDeg = owner.transform.eulerAngles.y;
            _hasHit = false;
            _roundIndex = 0;
            _totalRounds = Mathf.Max(1, owner.RollChargeCount());

            owner.NotifyChargeAttackUsed();

            BeginWindup(owner);
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Windup: UpdateWindup(owner); break;
                case Phase.Dash:   UpdateDash(owner);   break;
                case Phase.Turn:   UpdateTurn(owner);   break;
                case Phase.Brake:  UpdateBrake(owner);  break;
            }
        }

        public void Exit(GorillaAI owner)
        {
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;

            // 傾けたまま抜けると以後ずっと斜めになってしまうので、水平の向きだけに戻す
            owner.transform.rotation = Quaternion.Euler(0.0f, _yawDeg, 0.0f);

            DestroyChargeEffect();
            DestroyAimIndicator();
        }

        // ---- 溜め ----------------------------------------

        /// <summary>1回ぶんの溜めを始める。2回目以降は溜めが短くなり、予測表示も一瞬しか出ない</summary>
        private void BeginWindup(GorillaAI owner)
        {
            _phase = Phase.Windup;
            _elapsedTime = 0.0f;
            _hasLockedAim = false;
            _dashedDistance = 0.0f;
            _roundStartPosition = owner.transform.position;

            // 突進のたびに距離を短くする。勢いが落ちていく絵になり、場外へ飛び出しにくくもなる
            _dashDistanceThisRound = owner.ChargeMaxDistance * Mathf.Pow(owner.ChargeDistanceFalloff, _roundIndex);

            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed * WINDUP_SPEED_MULTIPLIER;
            owner.PlayAnimation(GorillaAI.ANIM_NORMAL_ATTACK);

            SpawnAimIndicator(owner);
            SpawnChargeEffect(owner);
        }

        /// <summary>
        /// 溜め。前半で沈み込みながら後ろへ反り、後半で狙いを固めて、最後に一気に前傾へ弾ける。
        /// 狙いを合わせる速さは進むほど落ちていき、固定の合図とともに予測表示が赤く点滅する。
        /// </summary>
        private void UpdateWindup(GorillaAI owner)
        {
            float windupTime = _roundIndex == 0 ? owner.ChargeWindupTime : owner.ChargeFollowUpWindupTime;
            float ratio = windupTime <= 0.0f ? 1.0f : Mathf.Clamp01(_elapsedTime / windupTime);
            float lockRatio = Mathf.Clamp01(owner.ChargeAimLockRatio);

            // ---- 狙い ----
            // 固定の割合を越えるまでは相手を追うが、追う速さは進むほど落ちる。
            // 「もう曲がらない」瞬間がはっきり伝わるようにするため
            if (ratio < lockRatio)
            {
                float turnSpeed = Mathf.Lerp(owner.ChargeAimTurnSpeedDeg, owner.ChargeAimTurnSpeedDeg * 0.25f,
                    lockRatio <= 0.0f ? 1.0f : ratio / lockRatio);
                TurnYawTowardsTarget(owner, turnSpeed);
            }
            else if (!_hasLockedAim)
            {
                _hasLockedAim = true;
                // 予測表示を「もう曲がらない」色に切り替える。点滅が速くなり、発射直前だと分かる
                if (_telegraph != null) _telegraph.SetLocked(true);
            }

            // ---- X軸の傾き ----
            _leanAngleDeg = CalcWindupLeanAngle(owner, ratio, lockRatio);

            // ---- 震え ----
            Vector2 jitter = Random.insideUnitCircle * (MAX_SHAKE_AMOUNT * ratio);
            owner.transform.position = _roundStartPosition + new Vector3(jitter.x, 0.0f, jitter.y);
            ApplyRotation(owner);
            UpdateAimIndicator(owner);

            if (_elapsedTime < windupTime) return;

            BeginDash(owner);
        }

        /// <summary>溜めの進み具合から、体をどれだけ傾けるかを求める</summary>
        private float CalcWindupLeanAngle(GorillaAI owner, float ratio, float lockRatio)
        {
            float back = -owner.ChargeLeanBackAngleDeg;
            float forward = owner.ChargeLeanForwardAngleDeg;

            // 沈み込み: 0 から後傾へ
            if (ratio < LEAN_BACK_END_RATIO)
            {
                return Mathf.Lerp(0.0f, back, ratio / LEAN_BACK_END_RATIO);
            }

            // 溜め切り: 後傾のまま保持
            if (ratio < lockRatio)
            {
                return back;
            }

            // 発射直前: 反りを一気に前傾へ返す(バネが弾けるように見せる)
            float releaseRatio = lockRatio >= 1.0f ? 1.0f : (ratio - lockRatio) / (1.0f - lockRatio);
            return Mathf.Lerp(back, forward, releaseRatio);
        }

        // ---- 突進 ----------------------------------------

        private void BeginDash(GorillaAI owner)
        {
            _phase = Phase.Dash;
            _elapsedTime = 0.0f;
            _dashedDistance = 0.0f;
            _leanAngleDeg = owner.ChargeLeanForwardAngleDeg;

            owner.transform.position = _roundStartPosition;
            DestroyChargeEffect();
            DestroyAimIndicator();

            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed * DASH_SPEED_MULTIPLIER;
            owner.PlayAnimation(GorillaAI.ANIM_RUN);
        }

        /// <summary>突進。前傾のまま走る。わずかに曲がるが、横へ跳ばれると追いつけない速さに抑える</summary>
        private void UpdateDash(GorillaAI owner)
        {
            // ほんの少しだけ相手を追う。完全な直線だと歩くだけで避けられてしまい、
            // かといって強く追うと飛び込みでも避けられなくなるので、その中間に置く
            TurnYawTowardsTarget(owner, owner.ChargeHomingSpeedDeg);
            ApplyRotation(owner);

            Vector3 direction = Quaternion.Euler(0.0f, _yawDeg, 0.0f) * Vector3.forward;
            float step = owner.ChargeSpeed * Time.deltaTime;
            owner.transform.position += direction * step;
            _dashedDistance += step;

            TryApplyDamageToLocalPlayer(owner, direction);

            if (!_hasHit && _dashedDistance < _dashDistanceThisRound) return;

            // 命中したらそこで打ち止め。吹き飛ばした相手を続けて轢かないようにする
            if (_hasHit || _roundIndex + 1 >= _totalRounds)
            {
                BeginBrake(owner);
                return;
            }

            BeginTurn(owner);
        }

        // ---- 急旋回 --------------------------------------

        /// <summary>次の突進へ向けて振り向く。滑りながら向きを変えるので、止まって見えない</summary>
        private void BeginTurn(GorillaAI owner)
        {
            _phase = Phase.Turn;
            _elapsedTime = 0.0f;

            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed * 1.5f;
            owner.PlayAnimation(GorillaAI.ANIM_RUN);
        }

        private void UpdateTurn(GorillaAI owner)
        {
            float turnTime = Mathf.Max(0.01f, owner.ChargeTurnTime);
            float ratio = Mathf.Clamp01(_elapsedTime / turnTime);

            TurnYawTowardsTarget(owner, TURN_SPEED_DEG);

            // 前傾から少し起き上がって、次の溜めの姿勢へ渡す
            _leanAngleDeg = Mathf.Lerp(owner.ChargeLeanForwardAngleDeg, 0.0f, ratio);
            ApplyRotation(owner);

            // 惰性で少し滑る。だんだん止まる
            float skidSpeed = owner.ChargeSpeed * TURN_SKID_SPEED_RATIO * (1.0f - ratio);
            Vector3 direction = Quaternion.Euler(0.0f, _yawDeg, 0.0f) * Vector3.forward;
            owner.transform.position += direction * (skidSpeed * Time.deltaTime);

            if (_elapsedTime < turnTime) return;

            _roundIndex++;
            BeginWindup(owner);
        }

        // ---- 急停止・硬直 --------------------------------

        private void BeginBrake(GorillaAI owner)
        {
            _phase = Phase.Brake;
            _elapsedTime = 0.0f;
            _leanAngleDeg = 0.0f;

            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;
            owner.PlayAnimation(GorillaAI.ANIM_IDLE);
            ApplyRotation(owner);
        }

        /// <summary>空振りのときは硬直を長くして、プレイヤーの反撃の的にする</summary>
        private void UpdateBrake(GorillaAI owner)
        {
            if (_elapsedTime < BRAKE_TIME) return;

            float staggerTime = _hasHit ? owner.ChargeHitStaggerTime : owner.ChargeMissStaggerTime;
            owner.ChangeState(new GorillaStateStagger(staggerTime));
        }

        // ---- 向きの操作 ----------------------------------

        /// <summary>水平の向きを相手の方へ、指定した速さだけ近づける</summary>
        private void TurnYawTowardsTarget(GorillaAI owner, float turnSpeedDeg)
        {
            if (owner.Target == null || turnSpeedDeg <= 0.0f) return;

            Vector3 toTarget = owner.Target.position - owner.transform.position;
            toTarget.y = 0.0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            float targetYaw = Quaternion.LookRotation(toTarget.normalized).eulerAngles.y;
            _yawDeg = Mathf.MoveTowardsAngle(_yawDeg, targetYaw, turnSpeedDeg * Time.deltaTime);
        }

        /// <summary>
        /// 水平の向きとX軸の傾きを合わせて反映する。
        /// 傾けると transform.forward が上下を向いてしまうため、進行方向は必ず _yawDeg から作ること。
        /// </summary>
        private void ApplyRotation(GorillaAI owner)
        {
            owner.transform.rotation = Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f);
        }

        // ---- 当たり判定 ----------------------------------

        /// <summary>
        /// 自分が操作しているローカルプレイヤーだけを対象に、体の周りの円で当たり判定を取る。
        /// (他の攻撃と同じ方式。全クライアントで同じ処理が走るため、各自が自分のぶんだけ
        ///  判定することで多重ダメージを避ける)
        /// </summary>
        private void TryApplyDamageToLocalPlayer(GorillaAI owner, Vector3 dashDirection)
        {
            if (_hasHit || owner.ChargeAttackDamage <= 0) return;

            PlayerAttack localAttack = PlayerAttack.Local;
            if (localAttack == null) return;

            PlayerHealth localHealth = localAttack.GetComponent<PlayerHealth>();
            if (localHealth == null || localHealth.IsDead) return;

            Vector3 toPlayer = localHealth.transform.position - owner.transform.position;
            toPlayer.y = 0.0f;
            if (toPlayer.magnitude > owner.ChargeHitRadius) return;

            _hasHit = true;

            // 進行方向へ大きく弾き飛ばす。轢かれた感を出すため通常より長い距離を指定する
            localHealth.ApplyDamage(
                owner.ChargeAttackDamage, -1, owner.transform.position - dashDirection,
                owner.ChargeKnockbackDistance, owner.ChargeKnockbackDurationSec, owner.ChargeKnockbackArcHeight);

            SpawnHitEffect(owner);
        }

        // ---- 演出 ----------------------------------------

        /// <summary>進路の予測表示(帯)を出す。他の攻撃と同じ表示を使うので、形だけで技を見分けられる</summary>
        private void SpawnAimIndicator(GorillaAI owner)
        {
            DestroyAimIndicator();

            // ゴリラの子にすると、体を傾けたときに表示ごと浮いてしまい、
            // 拡大率(ゴリラは2倍)も掛かってしまうので、独立して置いて毎フレーム合わせる
            _telegraph = GorillaAttackTelegraph.SpawnBand(
                owner.AttackTelegraphPrefab, owner.transform.position, _yawDeg,
                _dashDistanceThisRound, owner.ChargeHitRadius * 2.0f);
        }

        /// <summary>予測表示をゴリラの足元・水平の向きに合わせる</summary>
        private void UpdateAimIndicator(GorillaAI owner)
        {
            if (_telegraph == null) return;

            _telegraph.Follow(owner.transform.position, _yawDeg);
        }

        private void DestroyAimIndicator()
        {
            GorillaAttackTelegraph.Dismiss(_telegraph);
            _telegraph = null;
        }

        private void SpawnChargeEffect(GorillaAI owner)
        {
            DestroyChargeEffect();

            GameObject prefab = owner.ChargeAttackChargeEffectPrefab != null
                ? owner.ChargeAttackChargeEffectPrefab
                : owner.NormalAttackChargeEffectPrefab;
            if (prefab == null) return;

            Vector3 pos = owner.transform.position + Vector3.up * owner.NormalAttackChargeEffectHeight;
            _chargeEffectInstance = Object.Instantiate(prefab, pos, Quaternion.identity, owner.transform);
        }

        private void DestroyChargeEffect()
        {
            if (_chargeEffectInstance == null) return;
            Object.Destroy(_chargeEffectInstance);
            _chargeEffectInstance = null;
        }

        private void SpawnHitEffect(GorillaAI owner)
        {
            if (owner.NormalAttackHitEffectPrefab == null) return;

            Vector3 forward = Quaternion.Euler(0.0f, _yawDeg, 0.0f) * Vector3.forward;
            Vector3 pos = owner.transform.position
                + forward * owner.NormalAttackHitEffectForwardOffset
                + Vector3.up * owner.NormalAttackChargeEffectHeight;

            var instance = Object.Instantiate(owner.NormalAttackHitEffectPrefab, pos, Quaternion.LookRotation(forward));

            // ScalingMode が Shape のパーティクルは localScale が効かないため、Hierarchy に切り替えてから拡大する
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            instance.transform.localScale = Vector3.one * owner.NormalAttackHitEffectScale;
        }
    }
}
