using System.Collections.Generic;
using ProjectKMP.Attack;
using ProjectKMP.Battle;
using ProjectKMP.Field;
using ProjectKMP.Player;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 岩投げステート（遠距離）。
    ///
    /// 遠くまで逃げていれば安全、という状態をなくすための技。
    /// 足元の地面から岩を掘り起こし、頭上へ持ち上げて放り投げる。
    /// フェーズが進むと掘り起こす岩が増え、扇状にばらまく「連投」になる。
    ///
    /// 流れ:
    ///   1. 掘り起こし … 前傾して地面に手を突っ込み、岩がせり上がってくる
    ///   2. 持ち上げ   … 岩を頭上へ。ここで着弾地点が決まり、予告の輪が出る
    ///   3. 溜め       … 後ろへ反って狙いを固定。予告が赤く点滅する
    ///   4. 投擲       … 少しずつ間を空けて1個ずつ投げる。岩は放物線を描いて飛ぶ
    ///   5. 着弾       … 衝撃波と範囲ダメージ。岩は砕けてかけらが飛び散る
    ///
    /// 着弾地点は持ち上げの時点で決まって以降は動かないので、予告の輪から出れば必ず避けられる。
    /// ゴリラ本体はその場から動かないため、避けたあとに距離を詰める時間もプレイヤー側に残る。
    /// </summary>
    public class GorillaStateRockThrow : IGorillaState
    {
        /// <summary>掘り起こしにかける時間の割合(溜め全体に対する)</summary>
        private const float DIG_RATIO = 0.4f;

        /// <summary>掘り起こしで飛ばす土くれの数(岩1個あたり)</summary>
        private const int DIG_DEBRIS_COUNT = 9;

        /// <summary>掘り起こしの土くれの大きさを、着弾のかけらの何倍にするか</summary>
        private const float DIG_DEBRIS_SCALE_RATIO = 0.7f;

        /// <summary>掘り跡の大きさを、岩の何倍にするか</summary>
        private const float DIG_DECAL_SCALE_RATIO = 2.2f;

        /// <summary>掘り起こしのカメラ揺れの強さ</summary>
        private const float DIG_CAMERA_SHAKE = 0.18f;

        /// <summary>持ち上げにかける時間の割合(溜め全体に対する)。この後は溜めきって投げる</summary>
        private const float LIFT_RATIO = 0.75f;

        /// <summary>掘り起こし中のアニメーション再生速度倍率</summary>
        private const float WINDUP_SPEED_MULTIPLIER = 0.2f;

        /// <summary>掘り起こし中に前傾する角度(度、X軸)。地面へ手を突っ込む姿勢</summary>
        private const float DIG_LEAN_FORWARD_DEG = 22.0f;

        /// <summary>投げる直前に後ろへ反る角度(度、X軸)</summary>
        private const float THROW_LEAN_BACK_DEG = 24.0f;

        /// <summary>投げ切った瞬間に前へ振り抜く角度(度、X軸)</summary>
        private const float THROW_FOLLOW_THROUGH_DEG = 20.0f;

        /// <summary>溜め中の体の震え幅の最大値(メートル)</summary>
        private const float MAX_SHAKE_AMOUNT = 0.07f;

        /// <summary>最後の岩が着弾してから硬直へ移るまでの余韻(秒)</summary>
        private const float FOLLOW_THROUGH_TIME = 0.3f;

        /// <summary>岩が飛んでいる間の1秒あたりの回転量(度)</summary>
        private const float ROCK_SPIN_SPEED_DEG = 220.0f;

        /// <summary>連投のとき、1個ずつ投げる間隔(秒)</summary>
        private const float THROW_INTERVAL_SEC = 0.18f;

        /// <summary>連投のとき、頭上で構える岩を横に並べる間隔(メートル)</summary>
        private const float HOLD_SIDE_SPACING = 1.3f;

        private enum Phase
        {
            /// <summary>掘り起こし・持ち上げ・溜め。全部まとめて1つの時間で進める</summary>
            Windup,
            /// <summary>岩が飛んでいる</summary>
            Flying,
            /// <summary>投げ切った後の振り抜き</summary>
            FollowThrough,
        }

        /// <summary>投げる岩1個ぶんの情報</summary>
        private class ThrownRock
        {
            public Transform Rock;
            public Vector3 ImpactPosition;
            public GorillaAttackTelegraph Telegraph;

            /// <summary>投げ始めるまでの待ち時間(秒)。連投で1個ずつずらすためのもの</summary>
            public float LaunchDelaySec;

            public Vector3 LaunchPosition;
            public float FlightDurationSec;
            public float FlightElapsedSec;
            public bool IsLaunched;
            public bool HasLanded;
        }

        private Phase _phase;
        private float _elapsedTime;
        private float _baseAnimatorSpeed;

        private float _yawDeg;
        private float _leanAngleDeg;
        private Vector3 _originalPosition;

        private bool _hasLockedAim;
        private bool _hasSpawnedRocks;

        private readonly List<ThrownRock> _rocks = new List<ThrownRock>();

        public void Enter(GorillaAI owner)
        {
            _phase = Phase.Windup;
            _elapsedTime = 0.0f;
            _hasLockedAim = false;
            _hasSpawnedRocks = false;
            _originalPosition = owner.transform.position;
            _yawDeg = owner.transform.eulerAngles.y;
            _leanAngleDeg = 0.0f;
            _rocks.Clear();

            _baseAnimatorSpeed = owner.Animator != null ? owner.Animator.speed : 1.0f;
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed * WINDUP_SPEED_MULTIPLIER;

            owner.PlayAnimation(GorillaAI.ANIM_NORMAL_ATTACK);
            owner.NotifyRockThrowUsed();
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Windup:        UpdateWindup(owner);        break;
                case Phase.Flying:        UpdateFlying(owner);        break;
                case Phase.FollowThrough: UpdateFollowThrough(owner); break;
            }
        }

        public void Exit(GorillaAI owner)
        {
            owner.transform.SetPositionAndRotation(_originalPosition, Quaternion.Euler(0.0f, _yawDeg, 0.0f));
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;

            foreach (var rock in _rocks)
            {
                GorillaAttackTelegraph.Dismiss(rock.Telegraph);
                rock.Telegraph = null;
                if (rock.Rock != null) Object.Destroy(rock.Rock.gameObject);
                rock.Rock = null;
            }
            _rocks.Clear();
        }

        // ---- 溜め(掘り起こし → 持ち上げ → 溜めきり) ------

        private void UpdateWindup(GorillaAI owner)
        {
            float windupTime = Mathf.Max(0.05f, owner.RockThrowWindupTime);
            float ratio = Mathf.Clamp01(_elapsedTime / windupTime);

            if (ratio < DIG_RATIO) UpdateDigPhase(owner, ratio / DIG_RATIO);
            else if (ratio < LIFT_RATIO) UpdateLiftPhase(owner, (ratio - DIG_RATIO) / (LIFT_RATIO - DIG_RATIO));
            else UpdateAimPhase(owner, (ratio - LIFT_RATIO) / (1.0f - LIFT_RATIO));

            // 震えは溜まるほど大きく
            Vector2 jitter = Random.insideUnitCircle * (MAX_SHAKE_AMOUNT * ratio);
            owner.transform.position = _originalPosition + new Vector3(jitter.x, 0.0f, jitter.y);
            owner.transform.rotation = Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f);

            if (_elapsedTime < windupTime) return;

            BeginThrow(owner);
        }

        /// <summary>掘り起こし。前傾して地面に手を突っ込み、足元から岩がせり上がってくる</summary>
        private void UpdateDigPhase(GorillaAI owner, float rate)
        {
            // 相手の方を向きながら屈む
            TurnYawTowardsTarget(owner, owner.RockThrowAimTurnSpeedDeg);
            _leanAngleDeg = Mathf.Lerp(0.0f, DIG_LEAN_FORWARD_DEG, rate);

            if (!_hasSpawnedRocks)
            {
                _hasSpawnedRocks = true;
                SpawnRocks(owner);
                PlayDigBurst(owner);
            }

            // 地面の下から、ゴリラの正面の足元へせり上がってくる
            float eased = rate * rate * (3.0f - 2.0f * rate); // smoothstep
            for (int i = 0; i < _rocks.Count; i++)
            {
                if (_rocks[i].Rock == null) continue;

                Vector3 digPoint = DigPoint(owner, i);
                Vector3 position = digPoint;
                position.y = Mathf.Lerp(digPoint.y - owner.RockThrowRockScale, digPoint.y, eased);

                _rocks[i].Rock.position = position;
                _rocks[i].Rock.localScale = Vector3.one * (owner.RockThrowRockScale * Mathf.Lerp(0.35f, 1.0f, eased));
            }
        }

        /// <summary>持ち上げ。岩を頭上へ運ぶ。ここで着弾地点が決まり、予告の輪が出る</summary>
        private void UpdateLiftPhase(GorillaAI owner, float rate)
        {
            TurnYawTowardsTarget(owner, owner.RockThrowAimTurnSpeedDeg);

            // 屈んだ姿勢から起き上がる
            _leanAngleDeg = Mathf.Lerp(DIG_LEAN_FORWARD_DEG, 0.0f, rate);

            DecideImpactPositions(owner);

            float eased = rate * rate * (3.0f - 2.0f * rate);
            for (int i = 0; i < _rocks.Count; i++)
            {
                if (_rocks[i].Rock == null) continue;

                _rocks[i].Rock.position = Vector3.Lerp(DigPoint(owner, i), HoldPoint(owner, i), eased);
                _rocks[i].Rock.Rotate(Vector3.up, ROCK_SPIN_SPEED_DEG * 0.3f * Time.deltaTime, Space.World);
            }
        }

        /// <summary>溜めきり。後ろへ反って狙いを固定する</summary>
        private void UpdateAimPhase(GorillaAI owner, float rate)
        {
            // ここから先は向きも着弾地点も変えない
            _leanAngleDeg = Mathf.Lerp(0.0f, -THROW_LEAN_BACK_DEG, rate);

            if (!_hasLockedAim)
            {
                _hasLockedAim = true;
                foreach (var rock in _rocks)
                {
                    if (rock.Telegraph != null) rock.Telegraph.SetLocked(true);
                }
            }

            for (int i = 0; i < _rocks.Count; i++)
            {
                if (_rocks[i].Rock != null) _rocks[i].Rock.position = HoldPoint(owner, i);
            }
        }

        // ---- 投擲 ----------------------------------------

        private void BeginThrow(GorillaAI owner)
        {
            _phase = Phase.Flying;
            _elapsedTime = 0.0f;

            owner.transform.position = _originalPosition;
            _leanAngleDeg = THROW_FOLLOW_THROUGH_DEG;

            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;
            owner.PlayAnimation(GorillaAI.ANIM_STAMP_ATTACK);

            // 1個ずつ間を空けて投げる。同時に投げると1発の大技に見えてしまう
            for (int i = 0; i < _rocks.Count; i++)
            {
                _rocks[i].LaunchDelaySec = THROW_INTERVAL_SEC * i;
            }
        }

        /// <summary>岩が順に飛んでいく。全部が着弾したら振り抜きへ移る</summary>
        private void UpdateFlying(GorillaAI owner)
        {
            owner.transform.rotation = Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f);

            bool allLanded = true;

            for (int i = 0; i < _rocks.Count; i++)
            {
                ThrownRock rock = _rocks[i];
                if (rock.HasLanded) continue;

                allLanded = false;

                // まだ投げる番が来ていない岩は頭上で待たせる
                if (!rock.IsLaunched)
                {
                    if (_elapsedTime < rock.LaunchDelaySec)
                    {
                        if (rock.Rock != null) rock.Rock.position = HoldPoint(owner, i);
                        continue;
                    }

                    Launch(owner, rock, i);
                }

                UpdateOneRockFlight(owner, rock);
            }

            if (!allLanded) return;

            _phase = Phase.FollowThrough;
            _elapsedTime = 0.0f;
        }

        /// <summary>1個を投げ始める。飛行時間は距離なりに決める</summary>
        private void Launch(GorillaAI owner, ThrownRock rock, int index)
        {
            rock.IsLaunched = true;
            rock.FlightElapsedSec = 0.0f;
            rock.LaunchPosition = rock.Rock != null ? rock.Rock.position : HoldPoint(owner, index);

            float distance = Vector3.Distance(rock.LaunchPosition, rock.ImpactPosition);
            rock.FlightDurationSec = Mathf.Clamp(distance / Mathf.Max(1.0f, owner.RockThrowSpeed), 0.25f, 1.6f);
        }

        /// <summary>1個ぶんの放物線を進める。着弾したら衝撃波と範囲ダメージ</summary>
        private void UpdateOneRockFlight(GorillaAI owner, ThrownRock rock)
        {
            rock.FlightElapsedSec += Time.deltaTime;
            float rate = Mathf.Clamp01(rock.FlightElapsedSec / rock.FlightDurationSec);

            if (rock.Rock != null)
            {
                Vector3 position = Vector3.Lerp(rock.LaunchPosition, rock.ImpactPosition, rate);
                // 山なりに飛ばす。sin で上げると、頂点が中間に来て自然な弧になる
                position.y += Mathf.Sin(rate * Mathf.PI) * owner.RockThrowArcHeight;
                rock.Rock.position = position;
                rock.Rock.Rotate(new Vector3(1.0f, 0.35f, 0.0f), ROCK_SPIN_SPEED_DEG * Time.deltaTime, Space.World);
            }

            if (rate < 1.0f) return;

            rock.HasLanded = true;
            Impact(owner, rock);
        }

        /// <summary>着弾。衝撃波・痕・かけら・範囲ダメージを出して岩を消す</summary>
        private void Impact(GorillaAI owner, ThrownRock rock)
        {
            GorillaAttackTelegraph.Dismiss(rock.Telegraph);
            rock.Telegraph = null;

            if (rock.Rock != null)
            {
                Object.Destroy(rock.Rock.gameObject);
                rock.Rock = null;
            }

            ShockwaveRing.Play(rock.ImpactPosition, new Color(1.0f, 0.6f, 0.2f, 1.0f),
                owner.RockThrowRadius * 2.2f, 0.45f, 0.7f);

            AttackDecal.Spawn(
                owner.RockThrowDecalPrefab != null ? owner.RockThrowDecalPrefab : owner.StampDecalPrefab,
                rock.ImpactPosition, owner.RockThrowRadius * 2.0f);

            // 砕けたかけらを飛び散らせる。かけらは自分で落ちて消えるので、
            // このステートが終わった後も転がり続ける
            GorillaRockDebris.Burst(
                owner.RockThrowRockPrefab, rock.ImpactPosition, owner.RockThrowDebrisCount,
                owner.RockThrowRockScale * owner.RockThrowDebrisScale,
                owner.RockThrowDebrisSpreadSpeed, owner.RockThrowDebrisUpSpeed, owner.RockThrowDebrisLifetimeSec);

            SpawnImpactEffect(owner, rock.ImpactPosition);
            TryApplyDamageToLocalPlayer(owner, rock.ImpactPosition);
        }

        private void UpdateFollowThrough(GorillaAI owner)
        {
            // 振り抜いた姿勢からゆっくり戻す
            float rate = Mathf.Clamp01(_elapsedTime / FOLLOW_THROUGH_TIME);
            _leanAngleDeg = Mathf.Lerp(THROW_FOLLOW_THROUGH_DEG, 0.0f, rate);
            owner.transform.rotation = Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f);

            if (_elapsedTime < FOLLOW_THROUGH_TIME) return;

            owner.ChangeState(new GorillaStateStagger(owner.RockThrowStaggerTime));
        }

        // ---- 岩 ------------------------------------------

        /// <summary>
        /// 岩を抉り出した瞬間の演出。
        ///
        /// 何もないところから岩がせり上がってくるだけだと、地面から出てきたように見えない。
        /// 掘った場所ごとに土煙・衝撃波・掘り跡を出して「地面を割って引き抜いた」ことを見せる。
        /// 見た目だけの処理なので、全クライアントで動くこの経路から出せば同期は要らない。
        /// </summary>
        private void PlayDigBurst(GorillaAI owner)
        {
            Color soil = new Color(0.72f, 0.56f, 0.38f, 1.0f);

            for (int i = 0; i < _rocks.Count; i++)
            {
                Vector3 digPoint = DigPoint(owner, i);

                // 掘り返した土。着弾のかけらより小さく、低く飛ばす
                GorillaRockDebris.Burst(
                    owner.RockThrowRockPrefab, digPoint, DIG_DEBRIS_COUNT,
                    owner.RockThrowRockScale * owner.RockThrowDebrisScale * DIG_DEBRIS_SCALE_RATIO,
                    owner.RockThrowDebrisSpreadSpeed * 0.6f,
                    owner.RockThrowDebrisUpSpeed * 0.75f,
                    owner.RockThrowDebrisLifetimeSec * 0.7f);

                ShockwaveRing.Play(digPoint, soil, owner.RockThrowRockScale * 2.4f, 0.35f, 0.8f);

                // 岩を抜いた跡。投げ終わった後も残って、どこから掘ったかが分かる
                AttackDecal.Spawn(
                    owner.RockThrowDecalPrefab != null ? owner.RockThrowDecalPrefab : owner.StampDecalPrefab,
                    digPoint, owner.RockThrowRockScale * DIG_DECAL_SCALE_RATIO);

                GrassField.FlattenAt(digPoint, owner.RockThrowRockScale * 1.6f, 0.9f);
            }

            // 擬音とカメラ揺れは1回だけ。岩の数だけ重ねると音も揺れもうるさくなる
            Onomatopoeia.Play(
                _originalPosition + Vector3.up * 2.0f, "ゴゴッ", soil, 1.0f, 0.5f);

            var camera = Object.FindAnyObjectByType<ThirdPersonCamera>();
            if (camera != null) camera.Shake(DIG_CAMERA_SHAKE, 0.3f);
        }

        /// <summary>フェーズなりの数だけ岩を用意する</summary>
        private void SpawnRocks(GorillaAI owner)
        {
            int count = Mathf.Max(1, owner.RollRockThrowCount());
            if (owner.RockThrowRockPrefab == null) return;

            for (int i = 0; i < count; i++)
            {
                Vector3 digPoint = DigPoint(owner, i);
                var instance = Object.Instantiate(
                    owner.RockThrowRockPrefab, digPoint - Vector3.up * owner.RockThrowRockScale, Random.rotation);

                var rock = new ThrownRock { Rock = instance.transform };
                rock.Rock.localScale = Vector3.one * (owner.RockThrowRockScale * 0.35f);
                _rocks.Add(rock);
            }
        }

        /// <summary>
        /// 着弾地点を決める。連投のときは相手を中心に横へ扇状にばらす。
        /// 追尾させないからこそ、動けば避けられる技になる。
        /// </summary>
        private void DecideImpactPositions(GorillaAI owner)
        {
            if (_rocks.Count == 0 || _rocks[0].Telegraph != null) return;

            Vector3 center = owner.Target != null ? owner.Target.position : owner.transform.position;
            center.y = _originalPosition.y;

            // 相手から見て左右の方向。この向きに並べると、逃げ道が横にずれていく
            Vector3 forward = center - _originalPosition;
            forward.y = 0.0f;
            Vector3 side = forward.sqrMagnitude > 0.0001f
                ? Vector3.Cross(Vector3.up, forward.normalized)
                : Vector3.right;

            // 隣り合う着弾がつながって逃げ場が無くならないよう、半径の1.6倍ずつ離す
            float spacing = owner.RockThrowRadius * 1.6f;

            for (int i = 0; i < _rocks.Count; i++)
            {
                float offset = (i - (_rocks.Count - 1) * 0.5f) * spacing;
                _rocks[i].ImpactPosition = center + side * offset;

                _rocks[i].Telegraph = GorillaAttackTelegraph.SpawnCircle(
                    owner.AttackTelegraphPrefab, _rocks[i].ImpactPosition, owner.RockThrowRadius);
            }
        }

        /// <summary>岩を掘り起こす位置(ゴリラの正面の足元)。連投のときは横に並べる</summary>
        private Vector3 DigPoint(GorillaAI owner, int index)
        {
            Quaternion yaw = Quaternion.Euler(0.0f, _yawDeg, 0.0f);
            Vector3 forward = yaw * Vector3.forward;
            Vector3 right = yaw * Vector3.right;

            float offset = (index - (Mathf.Max(1, _rocks.Count) - 1) * 0.5f) * HOLD_SIDE_SPACING;
            return _originalPosition + forward * owner.RockThrowDigForwardOffset + right * offset;
        }

        /// <summary>岩を構える位置(頭上)。連投のときは横に並べる</summary>
        private Vector3 HoldPoint(GorillaAI owner, int index)
        {
            Quaternion yaw = Quaternion.Euler(0.0f, _yawDeg, 0.0f);
            Vector3 forward = yaw * Vector3.forward;
            Vector3 right = yaw * Vector3.right;

            float offset = (index - (Mathf.Max(1, _rocks.Count) - 1) * 0.5f) * HOLD_SIDE_SPACING;
            return _originalPosition
                + Vector3.up * owner.RockThrowHoldHeight
                + forward * owner.RockThrowHoldForwardOffset
                + right * offset;
        }

        // ---- 当たり判定・演出 ----------------------------

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
        /// 自分が操作しているローカルプレイヤーだけを対象に、着弾地点からの円で判定する。
        /// (他の攻撃と同じ方式。各自が自分のぶんだけ判定することで多重ダメージを避ける)
        /// </summary>
        private void TryApplyDamageToLocalPlayer(GorillaAI owner, Vector3 impactPosition)
        {
            if (owner.RockThrowDamage <= 0) return;

            PlayerAttack localAttack = PlayerAttack.Local;
            if (localAttack == null) return;

            PlayerHealth localHealth = localAttack.GetComponent<PlayerHealth>();
            if (localHealth == null || localHealth.IsDead) return;

            Vector3 toPlayer = localHealth.transform.position - impactPosition;
            toPlayer.y = 0.0f;
            if (toPlayer.magnitude > owner.RockThrowRadius) return;

            localHealth.ApplyDamage(owner.RockThrowDamage, -1, impactPosition);
        }

        private void SpawnImpactEffect(GorillaAI owner, Vector3 impactPosition)
        {
            GameObject prefab = owner.RockThrowImpactEffectPrefab != null
                ? owner.RockThrowImpactEffectPrefab
                : owner.StampImpactEffectPrefab;
            if (prefab == null) return;

            var instance = Object.Instantiate(prefab, impactPosition, Quaternion.identity);

            // ScalingMode が Shape のパーティクルは localScale が効かないため、Hierarchy に切り替えてから拡大する
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            instance.transform.localScale = Vector3.one * owner.RockThrowImpactEffectScale;
        }
    }
}
