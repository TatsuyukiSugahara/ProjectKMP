using ProjectKMP.Attack;
using ProjectKMP.Battle;
using ProjectKMP.Field;
using ProjectKMP.Player;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 跳びかかりステート（遠中距離）。
    ///
    /// 突進が「横に間合いを詰める」技なのに対して、こちらは縦に跳び越えてくる技。
    /// 離れた場所へ大きく跳んで、真上から着地して踏み潰す。
    ///
    /// 逃げた先に落ちてくるのが怖さの正体なので、着地点は跳ぶ前に決めて動かさない。
    /// 予告の輪から出れば必ず避けられるが、跳んでいる間はゴリラを殴れないので、
    /// 「距離を取っても仕切り直しにならない」という圧をかける役をもつ。
    /// </summary>
    public class GorillaStatePounce : IGorillaState
    {
        /// <summary>溜め中のアニメーション再生速度倍率。沈み込んで止まって見せる</summary>
        private const float WINDUP_SPEED_MULTIPLIER = 0.1f;

        /// <summary>溜めで沈み込む深さ(メートル)。低く構えてから跳ぶ</summary>
        private const float CROUCH_DEPTH = 0.5f;

        /// <summary>溜め中に前傾する角度(度、X軸)</summary>
        private const float CROUCH_LEAN_DEG = 26.0f;

        /// <summary>溜め中の体の震え幅の最大値(メートル)</summary>
        private const float MAX_SHAKE_AMOUNT = 0.1f;

        /// <summary>着地してから硬直へ移るまでの余韻(秒)</summary>
        private const float LANDING_RECOVERY_TIME = 0.25f;

        /// <summary>着地の衝撃で草をなぎ倒す範囲を、当たり判定の何倍にするか</summary>
        private const float GRASS_FLATTEN_SCALE = 1.6f;

        private enum Phase
        {
            /// <summary>沈み込んで溜める</summary>
            Crouch,
            /// <summary>跳んでいる</summary>
            Leap,
            /// <summary>着地の余韻</summary>
            Recover,
        }

        private Phase _phase;
        private float _elapsedTime;
        private float _baseAnimatorSpeed;

        private float _yawDeg;
        private Vector3 _startPosition;
        private Vector3 _landPosition;
        private bool _hasApplyDamage;

        private GorillaAttackTelegraph _telegraph;
        private GameObject _chargeEffectInstance;

        public void Enter(GorillaAI owner)
        {
            _phase = Phase.Crouch;
            _elapsedTime = 0.0f;
            _hasApplyDamage = false;
            _startPosition = owner.transform.position;
            _yawDeg = owner.transform.eulerAngles.y;

            // 着地点は跳ぶ前に決めて以降は動かさない。追尾しないからこそ避けられる技になる
            _landPosition = DecideLandPosition(owner);

            Vector3 toLand = _landPosition - _startPosition;
            toLand.y = 0.0f;
            if (toLand.sqrMagnitude > 0.0001f)
            {
                _yawDeg = Quaternion.LookRotation(toLand.normalized).eulerAngles.y;
            }

            _baseAnimatorSpeed = owner.Animator != null ? owner.Animator.speed : 1.0f;
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed * WINDUP_SPEED_MULTIPLIER;

            owner.PlayAnimation(GorillaAI.ANIM_JUMP);

            _telegraph = GorillaAttackTelegraph.SpawnCircle(
                owner.AttackTelegraphPrefab, _landPosition, owner.PounceRadius);
            if (_telegraph != null) _telegraph.SetLocked(true);

            if (owner.StampAttackChargeEffectPrefab != null)
            {
                Vector3 pos = _startPosition + Vector3.up * owner.StampAttackChargeEffectHeight;
                _chargeEffectInstance = Object.Instantiate(
                    owner.StampAttackChargeEffectPrefab, pos, Quaternion.identity, owner.transform);
            }

            owner.NotifyPounceUsed();
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Crouch:  UpdateCrouch(owner);  break;
                case Phase.Leap:    UpdateLeap(owner);    break;
                case Phase.Recover: UpdateRecover(owner); break;
            }
        }

        public void Exit(GorillaAI owner)
        {
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;

            // 空中や傾いたまま抜けないよう、地面の高さと水平の向きに戻す
            Vector3 position = owner.transform.position;
            position.y = _startPosition.y;
            owner.transform.SetPositionAndRotation(position, Quaternion.Euler(0.0f, _yawDeg, 0.0f));

            GorillaAttackTelegraph.Dismiss(_telegraph);
            _telegraph = null;
            DestroyChargeEffect();
        }

        // ---- 各フェーズ ----------------------------------

        /// <summary>沈み込み。低く構えて震えながら跳ぶ準備をする</summary>
        private void UpdateCrouch(GorillaAI owner)
        {
            float windupTime = Mathf.Max(0.05f, owner.PounceWindupTime);
            float rate = Mathf.Clamp01(_elapsedTime / windupTime);

            Vector2 jitter = Random.insideUnitCircle * (MAX_SHAKE_AMOUNT * rate);
            Vector3 position = _startPosition + new Vector3(jitter.x, 0.0f, jitter.y);
            position.y = _startPosition.y - CROUCH_DEPTH * rate;

            owner.transform.SetPositionAndRotation(
                position, Quaternion.Euler(CROUCH_LEAN_DEG * rate, _yawDeg, 0.0f));

            if (_elapsedTime < windupTime) return;

            BeginLeap(owner);
        }

        private void BeginLeap(GorillaAI owner)
        {
            _phase = Phase.Leap;
            _elapsedTime = 0.0f;

            DestroyChargeEffect();

            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;
            owner.PlayAnimation(GorillaAI.ANIM_JUMP);

            // 跳び上がる瞬間、足元の草を蹴散らす
            GrassField.FlattenAt(_startPosition, owner.PounceRadius * 0.7f, 0.8f);
        }

        /// <summary>跳躍。放物線を描いて着地点へ向かう。前半で反り、後半で前傾して落ちる</summary>
        private void UpdateLeap(GorillaAI owner)
        {
            float leapTime = Mathf.Max(0.05f, owner.PounceLeapDurationSec);
            float rate = Mathf.Clamp01(_elapsedTime / leapTime);

            Vector3 position = Vector3.Lerp(_startPosition, _landPosition, rate);
            position.y = _startPosition.y + Mathf.Sin(rate * Mathf.PI) * owner.PounceJumpHeight;

            // 上りは反り、下りは前傾。落ちてくる勢いが姿勢からも伝わる
            float lean = Mathf.Lerp(-CROUCH_LEAN_DEG, CROUCH_LEAN_DEG, rate);
            owner.transform.SetPositionAndRotation(position, Quaternion.Euler(lean, _yawDeg, 0.0f));

            if (rate < 1.0f) return;

            Land(owner);
        }

        /// <summary>着地。範囲ダメージ・衝撃波・痕・草なぎ倒しをまとめて出す</summary>
        private void Land(GorillaAI owner)
        {
            _phase = Phase.Recover;
            _elapsedTime = 0.0f;

            owner.transform.SetPositionAndRotation(_landPosition, Quaternion.Euler(0.0f, _yawDeg, 0.0f));
            owner.PlayAnimation(GorillaAI.ANIM_STAMP_ATTACK);

            GorillaAttackTelegraph.Dismiss(_telegraph);
            _telegraph = null;

            if (_hasApplyDamage) return;
            _hasApplyDamage = true;

            ShockwaveRing.Play(_landPosition, new Color(1.0f, 0.55f, 0.15f, 1.0f),
                owner.PounceRadius * 2.4f, 0.5f, 1.0f);
            GrassField.FlattenAt(_landPosition, owner.PounceRadius * GRASS_FLATTEN_SCALE, 1.0f);

            AttackDecal.Spawn(
                owner.RockThrowDecalPrefab != null ? owner.RockThrowDecalPrefab : owner.StampDecalPrefab,
                _landPosition, owner.PounceRadius * 2.0f);

            // 着地の衝撃で地面のかけらを跳ね上げる。かけらは自分で落ちて消える
            GorillaRockDebris.Burst(
                owner.PounceDebrisPrefab, _landPosition, owner.PounceDebrisCount, owner.PounceDebrisScale,
                owner.PounceRadius * 1.6f, 8.0f, 2.2f);

            ShakeCamera(owner);
            SpawnImpactEffect(owner);
            TryApplyDamageToLocalPlayer(owner);
        }

        private void UpdateRecover(GorillaAI owner)
        {
            if (_elapsedTime < LANDING_RECOVERY_TIME) return;

            owner.ChangeState(new GorillaStateStagger(owner.PounceStaggerTime));
        }

        // ---- 着地点の決定 --------------------------------

        /// <summary>
        /// 着地点を決める。相手の足元を狙うが、跳べる距離には上限があるので、
        /// 遠すぎる相手には届く範囲までしか跳ばない。
        /// </summary>
        private Vector3 DecideLandPosition(GorillaAI owner)
        {
            if (owner.Target == null) return _startPosition;

            Vector3 toTarget = owner.Target.position - _startPosition;
            toTarget.y = 0.0f;

            float distance = toTarget.magnitude;
            if (distance < 0.01f) return _startPosition;
            if (distance > owner.PounceMaxDistance)
            {
                toTarget = toTarget.normalized * owner.PounceMaxDistance;
            }

            Vector3 landing = _startPosition + toTarget;
            landing.y = _startPosition.y;
            return landing;
        }

        // ---- 当たり判定・演出 ----------------------------

        /// <summary>
        /// 自分が操作しているローカルプレイヤーだけを対象に、着地点からの円で判定する。
        /// (他の攻撃と同じ方式。各自が自分のぶんだけ判定することで多重ダメージを避ける)
        /// </summary>
        private void TryApplyDamageToLocalPlayer(GorillaAI owner)
        {
            if (owner.PounceDamage <= 0) return;

            PlayerAttack localAttack = PlayerAttack.Local;
            if (localAttack == null) return;

            PlayerHealth localHealth = localAttack.GetComponent<PlayerHealth>();
            if (localHealth == null || localHealth.IsDead) return;

            Vector3 toPlayer = localHealth.transform.position - _landPosition;
            toPlayer.y = 0.0f;
            if (toPlayer.magnitude > owner.PounceRadius) return;

            // 着地点を発生源にして、外側へ弾き飛ばす
            localHealth.ApplyDamage(
                owner.PounceDamage, -1, _landPosition,
                owner.PounceKnockbackDistance, 0.45f, 2.0f);
        }

        private void ShakeCamera(GorillaAI owner)
        {
            var camera = Object.FindAnyObjectByType<ThirdPersonCamera>();
            if (camera == null) return;

            camera.Shake(owner.PounceCameraShake, 0.35f);
        }

        private void SpawnImpactEffect(GorillaAI owner)
        {
            if (owner.StampImpactEffectPrefab == null) return;

            var instance = Object.Instantiate(owner.StampImpactEffectPrefab, _landPosition, Quaternion.identity);

            // ScalingMode が Shape のパーティクルは localScale が効かないため、Hierarchy に切り替えてから拡大する
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            // 跳びかかりはスタンプより広い範囲なので、エフェクトも大きめに出す
            instance.transform.localScale = Vector3.one * (owner.StampImpactEffectScale * 1.5f);
        }

        private void DestroyChargeEffect()
        {
            if (_chargeEffectInstance == null) return;
            Object.Destroy(_chargeEffectInstance);
            _chargeEffectInstance = null;
        }
    }
}
