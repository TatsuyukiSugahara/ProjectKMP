using UnityEngine;
using ProjectKMP.Attack;
using ProjectKMP.Battle;
using ProjectKMP.Player;
using ProjectKMP.Presentation;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 破壊光線ステート（中距離から使う持続ダメージ攻撃）。
    /// 予備動作(狙い)→発射(正面固定方向に一定時間出しっぱなし)→硬直、という流れ。
    /// 発射開始時は光線の長さ0から実際の長さまで徐々に伸びていく。
    /// 発射中は光線の当たり判定内にいる時間に応じてダメージを与える。
    /// 初めて当たった瞬間は強めのダメージ、そのあとは光線内に居続けている間だけ
    /// 一定間隔ごとに弱めのダメージが入る(クールタイムを挟んだ連続ヒット)。
    /// 光線から一度出るとリセットされ、再度当たれば再び初撃扱いになる。
    /// </summary>
    public class GorillaStateBeamAttack : IGorillaState
    {
        private enum Phase { Windup, Firing, Reaim, Recovery }

        /// <summary>発射の瞬間に画面を止める長さ(秒)</summary>
        private const float FIRE_HIT_STOP_SEC = 0.05f;

        /// <summary>発射の瞬間のカメラ揺れの強さ</summary>
        private const float FIRE_SHAKE_AMOUNT = 0.55f;

        /// <summary>光線の色。発射演出をこの色で揃える。ボスの技なので赤で統一する</summary>
        private static readonly Color BEAM_COLOR = new Color(1.0f, 0.18f, 0.12f, 1.0f);

        /// <summary>溜め中に上を向く角度(度、X軸)。マイナスで上向き。天を仰いで力を溜める姿勢</summary>
        private const float CHARGE_PITCH_DEG = -38.0f;

        /// <summary>撃った瞬間に前へ突き出す角度(度、X軸)。上向きから一気に振り下ろして発射に見せる</summary>
        private const float FIRE_PITCH_DEG = 12.0f;

        /// <summary>発射時の前傾が水平へ戻るまでの時間(秒)</summary>
        private const float FIRE_PITCH_RECOVER_SEC = 0.25f;

        private Phase _phase;
        private float _elapsedTime;

        /// <summary>いま何発目を撃っているか(0始まり)</summary>
        private int _shotIndex;

        /// <summary>この技で何発撃つか</summary>
        private int _totalShots;

        /// <summary>狙いを固定したか。固定後は光線がもう曲がらない</summary>
        private bool _isAimLocked;

        private ThirdPersonCamera _camera;

        /// <summary>水平の向き。体をX軸で傾けると transform.forward が使えなくなるので別に持つ</summary>
        private float _yawDeg;
        private GameObject _chargeEffectInstance;
        private GameObject _beamEffectInstance;
        private DestructionBeamVisual _beamVisual;

        private Vector3 _beamOrigin;
        private Vector3 _beamDirection;

        /// <summary>今フレーム時点で実際に有効な光線の長さ(伸びている途中は徐々に増える)</summary>
        private float _currentBeamLength;

        /// <summary>光線内に居続けているか(前フレーム時点)</summary>
        private bool _isPlayerInBeam;

        /// <summary>今回の連続ヒットで、まだ初撃を与えていないか</summary>
        private bool _isFirstHitPending = true;

        /// <summary>次の継続ダメージまでの残り時間</summary>
        private float _tickTimer;

        /// <summary>発射中の震え演出の基準位置(この位置を中心に揺れる)</summary>
        private Vector3 _firingBasePosition;

        /// <summary>次に痕(デカール)を置く、光線の根元からの距離</summary>
        private float _nextBeamDecalDistance;
        private GorillaAttackTelegraph _telegraph;

        public void Enter(GorillaAI owner)
        {
            _phase = Phase.Windup;
            _elapsedTime = 0f;
            _isPlayerInBeam = false;
            _isFirstHitPending = true;
            _tickTimer = 0f;
            _currentBeamLength = 0f;
            _shotIndex = 0;
            _totalShots = owner.BeamShotCount;
            _isAimLocked = false;
            _yawDeg = owner.transform.eulerAngles.y;

            // 発射のたびに探すと重いので、入り口で一度だけ持っておく
            _camera = Object.FindAnyObjectByType<ThirdPersonCamera>();

            // 使ったことを即座に記録し、クールタイムを開始する
            owner.NotifyBeamAttackUsed();

            owner.PlayAnimation(GorillaAI.ANIM_NORMAL_ATTACK);

            if (owner.BeamChargeEffectPrefab != null)
            {
                Vector3 pos = GetBeamOrigin(owner);
                _chargeEffectInstance = Object.Instantiate(owner.BeamChargeEffectPrefab, pos, Quaternion.identity, owner.transform);
            }

            // 光線が通る帯を地面に出す。狙い中は向きが動くので、表示も一緒に回して
            // 「いまどこを狙われているか」が分かるようにする
            _telegraph = GorillaAttackTelegraph.SpawnBand(
                owner.AttackTelegraphPrefab, owner.transform.position, _yawDeg,
                owner.BeamLength, owner.BeamWidth * 2.0f);
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Windup:
                    UpdateWindup(owner);
                    break;
                case Phase.Firing:
                    UpdateFiring(owner);
                    break;
                case Phase.Reaim:
                    UpdateReaim(owner);
                    break;
                case Phase.Recovery:
                    UpdateRecovery(owner);
                    break;
            }
        }

        public void Exit(GorillaAI owner)
        {
            if (_phase == Phase.Firing)
            {
                owner.transform.position = _firingBasePosition;
            }

            // 傾けたまま抜けると以後ずっと斜めになってしまうので、水平の向きだけに戻す
            owner.transform.rotation = Quaternion.Euler(0.0f, _yawDeg, 0.0f);

            GorillaAttackTelegraph.Dismiss(_telegraph);
            _telegraph = null;

            if (_chargeEffectInstance != null)
            {
                Object.Destroy(_chargeEffectInstance);
                _chargeEffectInstance = null;
            }

            if (_beamEffectInstance != null)
            {
                Object.Destroy(_beamEffectInstance);
                _beamEffectInstance = null;
            }
        }

        /// <summary>
        /// 狙い中は目標の方を向き続ける(この間は移動しない)。
        /// ただし溜めの途中で狙いを固定し、そこから先はもう曲がらないようにする。
        /// 「あとは避けるだけ」という時間をはっきり作らないと、長い溜めがただの待ち時間になってしまう。
        /// </summary>
        private void UpdateWindup(GorillaAI owner)
        {
            float windupTime = Mathf.Max(0.05f, owner.BeamWindupTime);
            float lockTime = windupTime * Mathf.Clamp01(owner.BeamAimLockRatio);

            if (!_isAimLocked && _elapsedTime >= lockTime)
            {
                _isAimLocked = true;
                if (_telegraph != null) _telegraph.SetLocked(true);
            }

            AimAndPitch(owner, !_isAimLocked, _elapsedTime / windupTime, CHARGE_PITCH_DEG);

            if (_elapsedTime < windupTime) return;

            StartFiring(owner);
        }

        /// <summary>次の1発へ向けて狙いを付け直す。連射のときだけ通る</summary>
        private void UpdateReaim(GorillaAI owner)
        {
            float reaimTime = Mathf.Max(0.05f, owner.BeamReaimTime);
            float lockTime = reaimTime * Mathf.Clamp01(owner.BeamAimLockRatio);

            if (!_isAimLocked && _elapsedTime >= lockTime)
            {
                _isAimLocked = true;
                if (_telegraph != null) _telegraph.SetLocked(true);
            }

            // 2発目以降は溜めが短いので、上を向く角度も浅くして間延びさせない
            AimAndPitch(owner, !_isAimLocked, _elapsedTime / reaimTime, CHARGE_PITCH_DEG * 0.6f);

            if (_elapsedTime < reaimTime) return;

            StartFiring(owner);
        }

        /// <summary>
        /// 狙いを向けつつ、溜めの進み具合ぶんだけ上を向かせる。
        ///
        /// 体をX軸で傾けると transform.forward が上を向いてしまい、そのまま撃つと光線が空へ飛ぶ。
        /// 水平の向きは _yawDeg で別に持ち、傾きは見た目としてだけ乗せる。
        /// </summary>
        private void AimAndPitch(GorillaAI owner, bool canTurn, float rate, float targetPitchDeg)
        {
            // TurnTowards は水平の向きを回す処理なので、一度傾きを外してから呼ぶ
            owner.transform.rotation = Quaternion.Euler(0.0f, _yawDeg, 0.0f);

            if (canTurn && owner.Target != null)
            {
                owner.TurnTowards(owner.Target.position);
                _yawDeg = owner.transform.eulerAngles.y;
            }

            float eased = EaseOut(Mathf.Clamp01(rate));
            owner.transform.rotation = Quaternion.Euler(targetPitchDeg * eased, _yawDeg, 0.0f);

            if (_telegraph != null) _telegraph.Follow(owner.transform.position, _yawDeg);
        }

        private static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1.0f - (1.0f - t) * (1.0f - t);
        }

        private void StartFiring(GorillaAI owner)
        {
            _phase = Phase.Firing;
            _elapsedTime = 0f;
            _currentBeamLength = 0f;

            // 発射した瞬間に方向が確定する。以降は表示の役目が終わるので消す
            GorillaAttackTelegraph.Dismiss(_telegraph);
            _telegraph = null;

            if (_chargeEffectInstance != null)
            {
                Object.Destroy(_chargeEffectInstance);
                _chargeEffectInstance = null;
            }

            // 頭突きモーション(振りかぶり)は予備動作だけで終わらせ、発射中はIdleへ戻す。
            // ずっと頭突きの姿勢のまま光線を出し続けると違和感があるため、
            // ここからは代わりに体の震えで「力を放出し続けている感じ」を表現する
            owner.PlayAnimation(GorillaAI.ANIM_IDLE);
            _firingBasePosition = owner.transform.position;

            // 上を向いていた体を一気に振り下ろす。ここの落差が「撃った」の合図になる
            owner.transform.rotation = Quaternion.Euler(FIRE_PITCH_DEG, _yawDeg, 0.0f);

            // 発射の瞬間の水平方向に固定する(発射中は追尾しない)。
            // 体は傾いているので transform.forward ではなく _yawDeg から作る
            _beamOrigin = GetBeamOrigin(owner);
            _beamDirection = BeamForward();

            // 最初の痕は根元から1間隔ぶん先に置く(根元はゴリラの足元なので避ける)
            _nextBeamDecalDistance = owner.BeamDecalIntervalMeters;

            if (owner.BeamEffectPrefab != null)
            {
                _beamEffectInstance = Object.Instantiate(
                    owner.BeamEffectPrefab, _beamOrigin, Quaternion.LookRotation(_beamDirection));

                _beamVisual = _beamEffectInstance.GetComponent<DestructionBeamVisual>();
                if (_beamVisual != null)
                {
                    // 最初は長さ0から始め、徐々に伸ばしていく(UpdateFiringで毎フレーム更新する)
                    _beamVisual.Configure(_beamOrigin, _beamDirection, _currentBeamLength, owner.BeamWidth);
                }

                // 万一パーティクル系のエフェクトが混ざっていた場合に備えて、Hierarchyスケーリングにしておく
                var particleSystems = _beamEffectInstance.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in particleSystems)
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
            }

            PlayFireBurst(owner);
        }

        /// <summary>
        /// 発射した瞬間の演出。
        /// 長い溜めのあとに光線がすっと伸びるだけだと、いつ撃たれたのかが分からないので、
        /// 画面の停止・発光・砲口の衝撃波をまとめて出して「いま撃った」を明示する。
        /// </summary>
        private void PlayFireBurst(GorillaAI owner)
        {
            HitStop.Play(FIRE_HIT_STOP_SEC, 0.1f, 0.08f);
            ScreenFlash.Play(new Color(BEAM_COLOR.r, BEAM_COLOR.g, BEAM_COLOR.b, 0.3f), 0.2f);

            if (_camera != null) _camera.Shake(FIRE_SHAKE_AMOUNT, 0.28f);

            // 砲口に衝撃波を出す。光線そのものより一瞬だけ大きく見せて発射の圧を作る
            ShockwaveRing.Play(_beamOrigin, BEAM_COLOR, owner.BeamWidth * 3.0f, 0.35f, 1.2f);
            Onomatopoeia.Play(_beamOrigin + Vector3.up * 1.2f, "ゴォッ", BEAM_COLOR, 1.3f, 0.6f);

            SpawnMuzzleFlash(owner);
        }

        /// <summary>砲口の閃光。頭突きのヒットエフェクトを流用して大きく出す</summary>
        private void SpawnMuzzleFlash(GorillaAI owner)
        {
            if (owner.NormalAttackHitEffectPrefab == null) return;

            var instance = Object.Instantiate(
                owner.NormalAttackHitEffectPrefab, _beamOrigin, Quaternion.LookRotation(_beamDirection));

            // ScalingMode が Shape のパーティクルは localScale が効かないため、Hierarchy に切り替えてから拡大する
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            instance.transform.localScale = Vector3.one * (owner.NormalAttackHitEffectScale * 1.4f);
        }

        private void UpdateFiring(GorillaAI owner)
        {
            // 振り下ろした前傾をゆっくり水平へ戻す
            float recover = FIRE_PITCH_RECOVER_SEC <= 0.0f ? 1.0f : Mathf.Clamp01(_elapsedTime / FIRE_PITCH_RECOVER_SEC);
            owner.transform.rotation = Quaternion.Euler(
                Mathf.Lerp(FIRE_PITCH_DEG, 0.0f, EaseOut(recover)), _yawDeg, 0.0f);

            ApplyFiringShake(owner);
            UpdateBeamLength(owner);
            SpawnBeamDecals(owner);
            UpdateBeamHit(owner);

            if (_elapsedTime < owner.BeamDuration) return;

            // パッと消えず、少しずつ透明になってから消えるようにする(見た目のフェード自体は
            // インスタンス側で完了後に自分自身を破棄するので、ここでは参照を手放すだけでよい)
            if (_beamEffectInstance != null)
            {
                if (_beamVisual != null)
                {
                    _beamVisual.FadeOut(owner.BeamFadeOutDuration);
                }
                else
                {
                    Object.Destroy(_beamEffectInstance);
                }
                _beamEffectInstance = null;
                _beamVisual = null;
            }

            // 震えで動かした位置を基準位置に戻す
            owner.transform.position = _firingBasePosition;

            _shotIndex++;

            if (_shotIndex < _totalShots)
            {
                BeginReaim(owner);
                return;
            }

            _phase = Phase.Recovery;
            _elapsedTime = 0f;
        }

        /// <summary>次の1発へ。狙いの表示を出し直して、もう一度追尾できる状態に戻す</summary>
        private void BeginReaim(GorillaAI owner)
        {
            _phase = Phase.Reaim;
            _elapsedTime = 0f;
            _isAimLocked = false;
            _currentBeamLength = 0f;
            _isPlayerInBeam = false;
            _isFirstHitPending = true;
            _tickTimer = 0f;

            owner.PlayAnimation(GorillaAI.ANIM_NORMAL_ATTACK);

            _telegraph = GorillaAttackTelegraph.SpawnBand(
                owner.AttackTelegraphPrefab, owner.transform.position, _yawDeg,
                owner.BeamLength, owner.BeamWidth * 2.0f);

            if (owner.BeamChargeEffectPrefab != null)
            {
                _chargeEffectInstance = Object.Instantiate(
                    owner.BeamChargeEffectPrefab, GetBeamOrigin(owner), Quaternion.identity, owner.transform);
            }
        }

        /// <summary>光線の長さを0から実際の長さまで徐々に伸ばす。見た目と当たり判定の両方に反映する</summary>
        private void UpdateBeamLength(GorillaAI owner)
        {
            if (owner.BeamGrowDuration <= 0f)
            {
                _currentBeamLength = owner.BeamLength;
            }
            else
            {
                float t = Mathf.Clamp01(_elapsedTime / owner.BeamGrowDuration);
                _currentBeamLength = Mathf.Lerp(0f, owner.BeamLength, t);
            }

            if (_beamVisual != null)
            {
                _beamVisual.Configure(_beamOrigin, _beamDirection, _currentBeamLength, owner.BeamWidth);
            }
        }

        /// <summary>
        /// 光線が伸びて指定間隔を越えるたびに、その真下の地面へ痕(デカール)を置く。
        /// 見た目だけの演出なので、エフェクトと同じく全クライアントで動くこの処理から呼べば
        /// 追加の通信なしで全員の画面に痕が出る。
        /// </summary>
        private void SpawnBeamDecals(GorillaAI owner)
        {
            if (owner.BeamDecalPrefab == null) return;

            while (_nextBeamDecalDistance <= _currentBeamLength)
            {
                Vector3 point = _beamOrigin + _beamDirection * _nextBeamDecalDistance;

                // 光線は体の高さから出ているので、真下の地面(ゴリラの足元の高さ)に落とす
                point.y = owner.transform.position.y;

                AttackDecal.Spawn(owner.BeamDecalPrefab, point, owner.BeamDecalDiameter);
                _nextBeamDecalDistance += owner.BeamDecalIntervalMeters;
            }
        }

        /// <summary>発射中、体を小刻みに震わせる(頭突き姿勢のまま止まって見えるのを防ぐ演出)</summary>
        private void ApplyFiringShake(GorillaAI owner)
        {
            if (owner.BeamFiringShakeAmount <= 0f)
            {
                owner.transform.position = _firingBasePosition;
                return;
            }

            Vector2 jitter = Random.insideUnitCircle * owner.BeamFiringShakeAmount;
            owner.transform.position = _firingBasePosition + new Vector3(jitter.x, 0f, jitter.y);
        }

        private void UpdateRecovery(GorillaAI owner)
        {
            if (_elapsedTime < owner.BeamStaggerTime) return;

            if (owner.IsPlayerLost())
            {
                owner.ChangeState(new GorillaStatePatrol());
            }
            else
            {
                owner.ChangeState(new GorillaStateChase());
            }
        }

        /// <summary>
        /// 自分が操作しているローカルプレイヤーだけを対象に光線との当たり判定を取り、ダメージを与える。
        /// (ネットワーク越しの全クライアントで同じ処理が走るため、各自が自分のぶんだけ判定することで
        ///  多重ダメージを避ける。判定はローカル分のみなので通信量も増えない)
        /// </summary>
        private void UpdateBeamHit(GorillaAI owner)
        {
            PlayerAttack localAttack = PlayerAttack.Local;
            if (localAttack == null) return;

            PlayerHealth localHealth = localAttack.GetComponent<PlayerHealth>();
            if (localHealth == null || localHealth.IsDead) return;

            bool isInBeam = IsPositionInBeam(localHealth.transform.position, owner);

            if (isInBeam)
            {
                if (!_isPlayerInBeam)
                {
                    _isPlayerInBeam = true;
                    _tickTimer = 0f;

                    if (_isFirstHitPending)
                    {
                        _isFirstHitPending = false;
                        ApplyDamage(localHealth, owner.BeamInitialDamage);
                    }
                }
                else
                {
                    _tickTimer += Time.deltaTime;
                    if (_tickTimer >= owner.BeamTickIntervalSec)
                    {
                        _tickTimer -= owner.BeamTickIntervalSec;
                        ApplyDamage(localHealth, owner.BeamContinuousDamage);
                    }
                }
            }
            else
            {
                _isPlayerInBeam = false;
                _isFirstHitPending = true;
                _tickTimer = 0f;
            }
        }

        /// <summary>体の傾きを含まない、水平の正面方向</summary>
        private Vector3 BeamForward()
        {
            return Quaternion.Euler(0.0f, _yawDeg, 0.0f) * Vector3.forward;
        }

        /// <summary>光線の発射位置(体の高さ + 正面方向へのオフセット)を求める</summary>
        private Vector3 GetBeamOrigin(GorillaAI owner)
        {
            return owner.transform.position
                + Vector3.up * owner.BeamOriginHeight
                + BeamForward() * owner.BeamOriginForwardOffset;
        }

        private void ApplyDamage(PlayerHealth target, int damage)
        {
            if (damage <= 0) return;
            // 光線の発射位置を発生源として渡し、光線から離れる方向へ吹き飛ばす
            target.ApplyDamage(damage, -1, _beamOrigin);
        }

        /// <summary>点が光線の判定(始点から一定方向・現在の長さのカプセル)の中にあるか</summary>
        private bool IsPositionInBeam(Vector3 position, GorillaAI owner)
        {
            Vector3 toPoint = position - _beamOrigin;
            float along = Vector3.Dot(toPoint, _beamDirection);
            if (along < 0f || along > _currentBeamLength) return false;

            Vector3 closest = _beamOrigin + _beamDirection * along;
            float distance = Vector3.Distance(position, closest);
            return distance <= owner.BeamWidth;
        }
    }
}
