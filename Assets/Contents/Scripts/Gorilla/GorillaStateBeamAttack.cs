using UnityEngine;
using ProjectKMP.Player;

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
        private enum Phase { Windup, Firing, Recovery }

        private Phase _phase;
        private float _elapsedTime;
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

        public void Enter(GorillaAI owner)
        {
            _phase = Phase.Windup;
            _elapsedTime = 0f;
            _isPlayerInBeam = false;
            _isFirstHitPending = true;
            _tickTimer = 0f;
            _currentBeamLength = 0f;

            // 使ったことを即座に記録し、クールタイムを開始する
            owner.NotifyBeamAttackUsed();

            owner.PlayAnimation(GorillaAI.ANIM_NORMAL_ATTACK);

            if (owner.BeamChargeEffectPrefab != null)
            {
                Vector3 pos = GetBeamOrigin(owner);
                _chargeEffectInstance = Object.Instantiate(owner.BeamChargeEffectPrefab, pos, Quaternion.identity, owner.transform);
            }
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

        /// <summary>狙い中は目標の方を向き続ける(この間は移動しない)</summary>
        private void UpdateWindup(GorillaAI owner)
        {
            if (owner.Target != null)
            {
                owner.TurnTowards(owner.Target.position);
            }

            if (_elapsedTime < owner.BeamWindupTime) return;

            StartFiring(owner);
        }

        private void StartFiring(GorillaAI owner)
        {
            _phase = Phase.Firing;
            _elapsedTime = 0f;
            _currentBeamLength = 0f;

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

            // 発射の瞬間の正面方向に固定する(発射中は追尾しない)
            _beamOrigin = GetBeamOrigin(owner);
            _beamDirection = owner.transform.forward;

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
        }

        private void UpdateFiring(GorillaAI owner)
        {
            ApplyFiringShake(owner);
            UpdateBeamLength(owner);
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

            // 震えで動かした位置を基準位置に戻してから硬直へ
            owner.transform.position = _firingBasePosition;

            _phase = Phase.Recovery;
            _elapsedTime = 0f;
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

        /// <summary>光線の発射位置(体の高さ + 正面方向へのオフセット)を求める</summary>
        private Vector3 GetBeamOrigin(GorillaAI owner)
        {
            return owner.transform.position
                + Vector3.up * owner.BeamOriginHeight
                + owner.transform.forward * owner.BeamOriginForwardOffset;
        }

        private void ApplyDamage(PlayerHealth target, int damage)
        {
            if (damage <= 0) return;
            target.ApplyDamage(damage, -1);
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
