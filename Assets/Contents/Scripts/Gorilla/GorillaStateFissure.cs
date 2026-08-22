using System.Collections.Generic;
using ProjectKMP.Attack;
using ProjectKMP.Battle;
using ProjectKMP.Field;
using ProjectKMP.Player;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 地割れステート（中遠距離）。
    ///
    /// 両手を高く振り上げ、体ごと反ってから地面へ叩きつける。
    /// その衝撃で地面が裂け、割れ目に沿って岩が突き上がりながら正面へ走っていく。
    ///
    /// 他の攻撃が「点(円)」か「扇形」なのに対して、これは線で場所を奪う技。
    /// 立っていい場所が減るので、逃げる方向を考えさせることができる。
    /// フェーズが進むと裂け目が複数方向へ分かれ、逃げ道がさらに狭くなる。
    ///
    /// 裂け目は振り下ろす前に向きが固定されるので、横へ数メートル動けば必ずかわせる。
    /// 走っている先端に当たったときだけダメージなので、通り過ぎた後は安全。
    /// </summary>
    public class GorillaStateFissure : IGorillaState
    {
        /// <summary>溜め中のアニメーション再生速度倍率</summary>
        private const float WINDUP_SPEED_MULTIPLIER = 0.12f;

        /// <summary>振り上げきったときに後ろへ反る角度(度、X軸)。大きく取って「振り翳している」形にする</summary>
        private const float WINDUP_LEAN_BACK_DEG = 38.0f;

        /// <summary>叩きつけた瞬間の前傾角度(度、X軸)</summary>
        private const float SLAM_LEAN_FORWARD_DEG = 32.0f;

        /// <summary>溜め中の体の震え幅の最大値(メートル)</summary>
        private const float MAX_SHAKE_AMOUNT = 0.09f;

        /// <summary>振り下ろしにかける時間(秒)。この終わりで地面に着く</summary>
        private const float SLAM_MOTION_TIME = 0.16f;

        /// <summary>裂け目が走り終わってから硬直へ移るまでの余韻(秒)</summary>
        private const float RECOVER_TIME = 0.25f;

        /// <summary>岩の突起を置く間隔(メートル)。裂け目が伸びるたびに1本生やす</summary>
        private const float SPIKE_INTERVAL_METERS = 2.2f;

        /// <summary>振り上げた両手の左右の開き(ワールド基準のメートル)。手のボーンが取れないときの保険</summary>
        private const float HAND_SIDE_OFFSET = 0.8f;

        /// <summary>振り上げきったときの前方位置(ワールド基準のメートル)。頭の少し前で構える</summary>
        private const float RAISE_FORWARD_OFFSET = 0.2f;

        /// <summary>
        /// 叩きつけ終わりの拳の高さを、拳の大きさの何倍にするか。
        /// 拳の中心を置く高さなので、大きさを変えたときに地面へめり込んだり浮いたりしないよう比率で持つ。
        /// </summary>
        private const float SLAM_END_HEIGHT_RATIO = 0.55f;

        /// <summary>
        /// 拳を自分の向き(Z軸)まわりに倒す角度(度)。左右で符号を反転させて内向きに揃える。
        /// 逆向きに見えたらこの符号を反転させる。
        /// </summary>
        private const float HAND_ROLL_DEG = 90.0f;

        private enum Phase
        {
            /// <summary>両手を振り上げて溜める</summary>
            Windup,
            /// <summary>振り下ろす</summary>
            Slam,
            /// <summary>裂け目が走っている</summary>
            Running,
            /// <summary>走り終わった後の余韻</summary>
            Recover,
        }

        /// <summary>1本ぶんの裂け目</summary>
        private class Crack
        {
            /// <summary>走る向き(水平)</summary>
            public Vector3 Direction;

            /// <summary>いま先端が届いている距離(メートル)</summary>
            public float TravelledDistance;

            /// <summary>次に岩を生やす距離(メートル)</summary>
            public float NextSpikeDistance;

            /// <summary>この裂け目でもうダメージを与えたか。1本につき1回だけ当たる</summary>
            public bool HasHit;

            public GorillaAttackTelegraph Telegraph;
        }

        private Phase _phase;
        private float _elapsedTime;
        private float _baseAnimatorSpeed;

        private float _yawDeg;
        private float _leanAngleDeg;
        private Vector3 _originalPosition;

        private readonly List<Crack> _cracks = new List<Crack>();

        private GameObject _rightFist;
        private GameObject _leftFist;
        private GameObject _chargeEffectInstance;

        public void Enter(GorillaAI owner)
        {
            _phase = Phase.Windup;
            _elapsedTime = 0.0f;
            _originalPosition = owner.transform.position;
            _yawDeg = owner.transform.eulerAngles.y;
            _leanAngleDeg = 0.0f;
            _cracks.Clear();

            _baseAnimatorSpeed = owner.Animator != null ? owner.Animator.speed : 1.0f;
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed * WINDUP_SPEED_MULTIPLIER;

            owner.PlayAnimation(GorillaAI.ANIM_SWEEP_ATTACK);

            SpawnFists(owner);

            if (owner.StampAttackChargeEffectPrefab != null)
            {
                Vector3 pos = _originalPosition + Vector3.up * owner.FissureHandRaiseHeight;
                _chargeEffectInstance = Object.Instantiate(
                    owner.StampAttackChargeEffectPrefab, pos, Quaternion.identity, owner.transform);
            }

            owner.NotifyFissureUsed();
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Windup:  UpdateWindup(owner);  break;
                case Phase.Slam:    UpdateSlam(owner);    break;
                case Phase.Running: UpdateRunning(owner); break;
                case Phase.Recover: UpdateRecover(owner); break;
            }
        }

        public void Exit(GorillaAI owner)
        {
            owner.transform.SetPositionAndRotation(_originalPosition, Quaternion.Euler(0.0f, _yawDeg, 0.0f));
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;

            foreach (var crack in _cracks)
            {
                GorillaAttackTelegraph.Dismiss(crack.Telegraph);
                crack.Telegraph = null;
            }
            _cracks.Clear();

            DestroyFists();
            DestroyChargeEffect();
        }

        // ---- 振り上げ ------------------------------------

        /// <summary>
        /// 両手を頭上へ振り上げながら体ごと反る。
        /// 前半は狙いを追い、後半で向きが固定される。
        /// </summary>
        private void UpdateWindup(GorillaAI owner)
        {
            float windupTime = Mathf.Max(0.05f, owner.FissureWindupTime);
            float rate = Mathf.Clamp01(_elapsedTime / windupTime);
            float lockRatio = Mathf.Clamp01(owner.FissureAimLockRatio);

            if (rate < lockRatio)
            {
                TurnYawTowardsTarget(owner, owner.FissureAimTurnSpeedDeg);

                // 狙いが動いているうちは、予告も一緒に回して行き先を見せる
                if (_cracks.Count == 0) SpawnCracks(owner);
                UpdateTelegraphs(owner);
            }
            else if (_cracks.Count > 0 && _cracks[0].Telegraph != null && !_cracks[0].Telegraph.IsLocked)
            {
                foreach (var crack in _cracks)
                {
                    if (crack.Telegraph != null) crack.Telegraph.SetLocked(true);
                }
            }

            // 反りは終盤で一気に深くする。ぐっと溜めてから落とす形にするため
            float leanRate = rate * rate;
            _leanAngleDeg = Mathf.Lerp(0.0f, -WINDUP_LEAN_BACK_DEG, leanRate);

            Vector2 jitter = Random.insideUnitCircle * (MAX_SHAKE_AMOUNT * rate);
            owner.transform.position = _originalPosition + new Vector3(jitter.x, 0.0f, jitter.y);
            owner.transform.rotation = Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f);

            UpdateFists(owner, rate, isSlamming: false);

            if (_elapsedTime < windupTime) return;

            BeginSlam(owner);
        }

        // ---- 振り下ろし ----------------------------------

        private void BeginSlam(GorillaAI owner)
        {
            _phase = Phase.Slam;
            _elapsedTime = 0.0f;

            owner.transform.position = _originalPosition;
            DestroyChargeEffect();

            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;
            owner.PlayAnimation(GorillaAI.ANIM_STAMP_ATTACK);
        }

        /// <summary>両手を地面まで振り下ろす。着いた瞬間から裂け目が走り出す</summary>
        private void UpdateSlam(GorillaAI owner)
        {
            float rate = Mathf.Clamp01(_elapsedTime / SLAM_MOTION_TIME);

            // 反りから前傾へ一気に返す
            _leanAngleDeg = Mathf.Lerp(-WINDUP_LEAN_BACK_DEG, SLAM_LEAN_FORWARD_DEG, rate);
            owner.transform.rotation = Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f);

            UpdateFists(owner, rate, isSlamming: true);

            if (_elapsedTime < SLAM_MOTION_TIME) return;

            _phase = Phase.Running;
            _elapsedTime = 0.0f;

            // 拳が地面に着いた瞬間の衝撃
            ShockwaveRing.Play(_originalPosition, new Color(1.0f, 0.5f, 0.15f, 1.0f), 7.0f, 0.35f, 0.9f);
            GrassField.FlattenAt(_originalPosition, 5.0f, 0.9f);
            ShakeCamera(owner);

            // 拳の周りの土を跳ね上げる
            Vector3 forward = Quaternion.Euler(0.0f, _yawDeg, 0.0f) * Vector3.forward;
            GorillaRockDebris.Burst(
                owner.FissureSpikePrefab, _originalPosition + forward * owner.FissureHandForwardOffset,
                owner.FissureDebrisCount, owner.FissureSpikeScale * 0.3f, 5.0f, 5.5f, 1.8f);
        }

        // ---- 裂け目が走る --------------------------------

        /// <summary>裂け目の先端を前へ進める。通ったところに岩を生やし、先端に当たった相手を打ち上げる</summary>
        private void UpdateRunning(GorillaAI owner)
        {
            float step = owner.FissureSpeed * Time.deltaTime;
            bool allFinished = true;

            // 予告は裂け目が走り始めたら役目を終える
            foreach (var crack in _cracks)
            {
                GorillaAttackTelegraph.Dismiss(crack.Telegraph);
                crack.Telegraph = null;
            }

            foreach (var crack in _cracks)
            {
                if (crack.TravelledDistance >= owner.FissureLength) continue;

                allFinished = false;
                crack.TravelledDistance = Mathf.Min(owner.FissureLength, crack.TravelledDistance + step);

                Vector3 tip = _originalPosition + crack.Direction * crack.TravelledDistance;

                // 通り過ぎたところに岩を生やし、痕と倒れた草を残す
                if (crack.TravelledDistance >= crack.NextSpikeDistance)
                {
                    crack.NextSpikeDistance += SPIKE_INTERVAL_METERS;
                    SpawnSpike(owner, tip, crack);
                }

                TryApplyDamageToLocalPlayer(owner, crack, tip);
            }

            if (!allFinished) return;

            _phase = Phase.Recover;
            _elapsedTime = 0.0f;
        }

        /// <summary>裂け目の先端に岩を生やす。地面から突き上がってしばらく残る</summary>
        private void SpawnSpike(GorillaAI owner, Vector3 tip, Crack crack)
        {
            AttackDecal.Spawn(
                owner.FissureDecalPrefab != null ? owner.FissureDecalPrefab : owner.StampDecalPrefab,
                tip, owner.FissureWidth * 1.6f);
            GrassField.FlattenAt(tip, owner.FissureWidth * 1.2f, 1.0f);

            if (owner.FissureSpikePrefab == null) return;

            // 割れ目に沿って向きを揃えつつ、1本ずつ角度をばらして単調にならないようにする
            float yaw = Quaternion.LookRotation(crack.Direction).eulerAngles.y + Random.Range(-25.0f, 25.0f);
            var instance = Object.Instantiate(
                owner.FissureSpikePrefab, tip, Quaternion.Euler(0.0f, yaw, 0.0f));

            float scale = owner.FissureSpikeScale * Random.Range(0.7f, 1.25f);
            instance.transform.localScale = Vector3.one * scale;

            // せり上がって、しばらく残ってから沈む
            var riser = instance.AddComponent<GorillaRisingSpike>();
            riser.Play(scale * 1.4f, owner.FissureSpikeRiseSec, owner.FissureSpikeLifetimeSec);
        }

        private void UpdateRecover(GorillaAI owner)
        {
            // 前傾した姿勢からゆっくり戻す
            float rate = Mathf.Clamp01(_elapsedTime / RECOVER_TIME);
            _leanAngleDeg = Mathf.Lerp(SLAM_LEAN_FORWARD_DEG, 0.0f, rate);
            owner.transform.rotation = Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f);

            UpdateFists(owner, 1.0f, isSlamming: true);

            if (_elapsedTime < RECOVER_TIME) return;

            owner.ChangeState(new GorillaStateStagger(owner.FissureStaggerTime));
        }

        // ---- 両手の見た目 --------------------------------

        private void SpawnFists(GorillaAI owner)
        {
            GameObject prefab = owner.RushPunchFistPrefab;
            if (prefab == null) return;

            _rightFist = Object.Instantiate(prefab, owner.transform);
            _leftFist = Object.Instantiate(prefab, owner.transform);

            // ゴリラ本体はシーンで拡大されている。子にすると拡大率がそのまま掛かって
            // 拳が頭上はるか上に飛んでしまうので、大きさも位置もワールド基準で指定して割る
            float scale = owner.FissureHandScale / ParentScale(owner);
            _rightFist.transform.localScale = new Vector3(-scale, scale, scale);
            _leftFist.transform.localScale = Vector3.one * scale;

            UpdateFists(owner, 0.0f, isSlamming: false);
        }

        /// <summary>
        /// 両手の位置を更新する。振り上げでは頭上へ、振り下ろしでは地面まで下ろす。
        /// 拳のモデルは +Z が拳の向きなので、振り上げでは上、振り下ろしでは下を向かせる。
        /// </summary>
        private void UpdateFists(GorillaAI owner, float rate, bool isSlamming)
        {
            if (_rightFist == null || _leftFist == null) return;

            // 振り上げでは拳を上に、振り下ろしでは下に向ける
            float pitch = isSlamming ? Mathf.Lerp(-80.0f, 80.0f, rate) : Mathf.Lerp(-30.0f, -80.0f, rate);

            PlaceOneFist(owner, _rightFist, isRight: true, rate: rate, isSlamming: isSlamming, pitch: pitch);
            PlaceOneFist(owner, _leftFist, isRight: false, rate: rate, isSlamming: isSlamming, pitch: pitch);
        }

        /// <summary>
        /// 片手ぶんの配置。振り上げはモデル上の手の位置から始めて頭上へ、振り下ろしは頭上から地面へ。
        /// </summary>
        private void PlaceOneFist(GorillaAI owner, GameObject fist, bool isRight, float rate, bool isSlamming, float pitch)
        {
            float parentScale = ParentScale(owner);
            float sign = isRight ? 1.0f : -1.0f;

            // モデル上で実際に手がある場所。振り上げも振り下ろしも、この腕の真上・真正面を通す。
            // 左右の開きを固定値で決めていたときは腕から離れた位置を通ってしまい、
            // 拳だけが別に浮いているように見えていた
            Vector3 rest;
            bool hasAnchor = owner.TryGetHandAnchorLocal(isRight, out rest);
            if (!hasAnchor)
            {
                rest = new Vector3(
                    HAND_SIDE_OFFSET * sign, owner.FissureHandRaiseHeight * 0.3f, 0.6f) / parentScale;
            }

            float sideLocal = hasAnchor ? rest.x : (HAND_SIDE_OFFSET * sign / parentScale);

            Vector3 raised = new Vector3(
                sideLocal, owner.FissureHandRaiseHeight / parentScale, RAISE_FORWARD_OFFSET / parentScale);

            Vector3 local;
            if (isSlamming)
            {
                Vector3 slammed = new Vector3(
                    sideLocal,
                    owner.FissureHandScale * SLAM_END_HEIGHT_RATIO / parentScale,
                    owner.FissureHandForwardOffset / parentScale);
                local = Vector3.Lerp(raised, slammed, rate);
            }
            else
            {
                local = Vector3.Lerp(rest, raised, rate);
            }

            fist.transform.localPosition = local;

            // ゴリラの手は地面すれすれにあるので、そのまま置くと拳が地面へ潜る。
            // 体を傾けている間も効かせたいのでワールド座標で見る
            Vector3 world = fist.transform.position;
            float lowest = _originalPosition.y + owner.FissureHandScale * 0.45f;
            if (world.y < lowest)
            {
                world.y = lowest;
                fist.transform.position = world;
            }

            // Euler は Z→X→Y の順に適用されるので、先に拳自身の向き(Z軸)まわりへ倒してから
            // 上下へ向ける。左右は符号を反転させて、甲が内側を向くように揃える
            fist.transform.localRotation = Quaternion.Euler(pitch, 0.0f, -HAND_ROLL_DEG * sign);
        }

        /// <summary>
        /// ゴリラ本体に掛かっている拡大率。拳は子オブジェクトなのでこれがそのまま掛かる。
        /// 位置と大きさをワールド基準のメートルで書けるように、指定値をこの値で割って使う。
        /// </summary>
        private static float ParentScale(GorillaAI owner)
        {
            return Mathf.Max(0.001f, owner.transform.lossyScale.x);
        }

        private void DestroyFists()
        {
            if (_rightFist != null) { Object.Destroy(_rightFist); _rightFist = null; }
            if (_leftFist != null) { Object.Destroy(_leftFist); _leftFist = null; }
        }

        // ---- 裂け目の生成 --------------------------------

        /// <summary>フェーズなりの本数の裂け目を、正面を中心に扇状へ用意する</summary>
        private void SpawnCracks(GorillaAI owner)
        {
            int count = Mathf.Max(1, owner.RollFissureCount());

            for (int i = 0; i < count; i++)
            {
                var crack = new Crack { NextSpikeDistance = SPIKE_INTERVAL_METERS };
                _cracks.Add(crack);

                crack.Telegraph = GorillaAttackTelegraph.SpawnBand(
                    owner.AttackTelegraphPrefab, _originalPosition, _yawDeg,
                    owner.FissureLength, owner.FissureWidth * 2.0f);
            }

            UpdateTelegraphs(owner);
        }

        /// <summary>狙いが動いている間、裂け目の向きと予告の表示を今の向きに合わせ直す</summary>
        private void UpdateTelegraphs(GorillaAI owner)
        {
            int count = _cracks.Count;
            float spread = owner.FissureSpreadAngleDeg;

            for (int i = 0; i < count; i++)
            {
                float angle = count <= 1 ? 0.0f : (i - (count - 1) * 0.5f) * spread;
                float yaw = _yawDeg + angle;

                _cracks[i].Direction = Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.forward;
                if (_cracks[i].Telegraph != null) _cracks[i].Telegraph.Follow(_originalPosition, yaw);
            }
        }

        // ---- 向き・当たり判定 ----------------------------

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
        /// 自分が操作しているローカルプレイヤーだけを対象に、裂け目の先端で判定する。
        /// 通り過ぎた後は当たらないので、走ってくる先端を跨ぐか、横へ抜ければかわせる。
        /// (他の攻撃と同じ方式。各自が自分のぶんだけ判定することで多重ダメージを避ける)
        /// </summary>
        private void TryApplyDamageToLocalPlayer(GorillaAI owner, Crack crack, Vector3 tip)
        {
            if (crack.HasHit || owner.FissureDamage <= 0) return;

            PlayerAttack localAttack = PlayerAttack.Local;
            if (localAttack == null) return;

            PlayerHealth localHealth = localAttack.GetComponent<PlayerHealth>();
            if (localHealth == null || localHealth.IsDead) return;

            Vector3 toPlayer = localHealth.transform.position - tip;
            toPlayer.y = 0.0f;
            if (toPlayer.magnitude > owner.FissureWidth) return;

            crack.HasHit = true;

            // 地面が裂けて突き上げられる形なので、上へ強く打ち上げる
            localHealth.ApplyDamage(
                owner.FissureDamage, -1, _originalPosition,
                owner.FissureKnockbackDistance, 0.5f, owner.FissureKnockbackArcHeight);
        }

        private void ShakeCamera(GorillaAI owner)
        {
            var camera = Object.FindAnyObjectByType<ThirdPersonCamera>();
            if (camera == null) return;

            camera.Shake(owner.FissureCameraShake, 0.4f);
        }

        private void DestroyChargeEffect()
        {
            if (_chargeEffectInstance == null) return;
            Object.Destroy(_chargeEffectInstance);
            _chargeEffectInstance = null;
        }
    }
}
