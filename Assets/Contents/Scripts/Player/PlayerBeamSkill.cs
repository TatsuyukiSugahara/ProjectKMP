using System.Collections.Generic;
using Photon.Pun;
using ProjectKMP.Attack;
using ProjectKMP.Dog;
using ProjectKMP.Gorilla;
using ProjectKMP.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Player
{
    /// <summary>
    /// プレイヤーのビームスキル(ゴリラの破壊光線のプレイヤー版)。
    /// 長押し(ゲームパッドB / Rキー / 画面のスキルボタン)で狙いを付け、
    /// 離した瞬間にその方向へ一定時間ビームを照射する。
    /// 狙い中は移動せずその場で向きだけ変えられ、足元に発射範囲と方向の表示が出る。
    /// 発射・ヒットは RPC で全クライアントに配り、当たり判定は操作している本人だけが取る
    /// (PlayerAttack と同じ方式。二重ダメージを防ぐ)。
    /// ダメージは「当たった瞬間の初撃」と「当たり続けている間の継続ダメージ」の2種類。
    /// </summary>
    public class PlayerBeamSkill : MonoBehaviourPun
    {
        private enum Phase { Ready, Aiming, Firing }

        // ---- 定数 ----------------------------------------

        private const int OVERLAP_BUFFER_SIZE = 32;

        // ---- インスペクタ設定 ------------------------------

        [Header("ダメージ")]
        [SerializeField, Min(0), Tooltip("ビームに当たった瞬間の初撃ダメージ")]
        private int _initialDamage = 10;

        [SerializeField, Min(0), Tooltip("ビームに当たり続けている間の継続ダメージ(1回ぶん)")]
        private int _tickDamage = 2;

        [SerializeField, Min(0.05f), Tooltip("継続ダメージが入る間隔(秒)")]
        private float _tickIntervalSec = 0.5f;

        [Header("クールタイム")]
        [SerializeField, Min(0f), Tooltip("発射してから次に使えるようになるまでの時間(秒)")]
        private float _cooldownSec = 10f;

        [Header("ビーム形状")]
        [SerializeField, Min(0.1f), Tooltip("ビームの長さ(m)")]
        private float _beamLength = 10f;

        [SerializeField, Min(0.05f), Tooltip("ビームの太さ(半径・m)。当たり判定と見た目の両方に使う")]
        private float _beamWidth = 0.8f;

        [SerializeField, Tooltip("発射位置の高さ(足元からのオフセット・m)")]
        private float _originHeight = 0.6f;

        [SerializeField, Tooltip("発射位置の前方オフセット(m)。体にビームが重ならないようにする")]
        private float _originForwardOffset = 0.8f;

        [Header("発射")]
        [SerializeField, Min(0.1f), Tooltip("ビームを照射し続ける時間(秒)。この間は移動できない")]
        private float _fireDurationSec = 2f;

        [SerializeField, Min(0f), Tooltip("ビームが根元から先端まで伸びきるまでの時間(秒)")]
        private float _growDurationSec = 0.2f;

        [SerializeField, Min(0.01f), Tooltip("照射終了後、ビームが消えるまでのフェード時間(秒)")]
        private float _fadeOutDurationSec = 0.5f;

        [Header("地面の痕")]
        [SerializeField, Tooltip("ビームが地面に残す痕(デカール)。未設定なら痕を残さない")]
        private AttackDecal _beamDecalPrefab;

        [SerializeField, Min(0.1f), Tooltip("痕を置く間隔(m)。ビームが伸びてこの距離を越えるたびに置く")]
        private float _decalIntervalMeters = 2f;

        [SerializeField, Min(0.1f), Tooltip("痕の直径をビームの太さ(直径)の何倍にするか")]
        private float _decalWidthScale = 1.2f;

        [Header("アニメーション")]
        [SerializeField, Range(0f, 1f), Tooltip("頭突きモーションのどの位置(0〜1)で止めて照射ポーズにするか")]
        private float _poseFreezeNormalizedTime = 0.5f;

        [Header("参照")]
        [SerializeField, Tooltip("ビームの見た目のプレハブ(DestructionBeamVisual 付き)")]
        private GameObject _beamEffectPrefab;

        [SerializeField, Tooltip("狙い中に足元へ出す発射範囲・方向の表示")]
        private BeamAimIndicator _aimIndicatorPrefab;

        [SerializeField, Tooltip("ダメージの数字のプレハブ。未設定なら数字を出さない")]
        private GameObject _damagePopupPrefab;

        [SerializeField, Tooltip("ヒット時に出すエフェクト。未設定なら出さない")]
        private GameObject _hitEffectPrefab;

        [SerializeField, Min(0.01f), Tooltip("ヒットエフェクトの大きさ倍率")]
        private float _hitEffectScale = 1f;

        [SerializeField, Tooltip("ヒットエフェクトを消すまでの秒数")]
        private float _hitEffectLifeSec = 1.5f;

        [Header("入力")]
        [SerializeField, Tooltip("Rキーの長押しで狙う")]
        private bool _useRKey = true;

        [SerializeField, Tooltip("ゲームパッドのBボタン(右ボタン)の長押しで狙う")]
        private bool _useGamepadEast = true;

        [SerializeField, Tooltip("画面上のスキルボタンの長押しで狙う")]
        private bool _useTouchButton = true;

        [Header("当てる相手")]
        [SerializeField, Tooltip("判定を取るレイヤー。HitTarget が付いた相手(敵)にだけ当たる")]
        private LayerMask _targetLayers = ~0;

        // ---- 内部状態 ------------------------------------

        /// <summary>ビーム内にいる相手ごとの継続ダメージの状態</summary>
        private class TargetState
        {
            public HitTarget Target;
            public Collider Collider;
            public float TickTimer;
            public bool JustEntered;
        }

        private readonly Collider[] _overlapBuffer = new Collider[OVERLAP_BUFFER_SIZE];
        private readonly Dictionary<int, TargetState> _targetStates = new Dictionary<int, TargetState>();
        private readonly HashSet<int> _presentThisFrame = new HashSet<int>();
        private readonly List<int> _removeWork = new List<int>();

        private Phase _phase = Phase.Ready;
        private float _cooldownRemainSec;
        private float _fireElapsedSec;
        private float _currentBeamLength;
        private Vector3 _beamOrigin;
        private Vector3 _beamDirection;
        private bool _wasHeldLastFrame;

        /// <summary>次に痕(デカール)を置く、ビームの根元からの距離</summary>
        private float _nextDecalDistance;

        private GameObject _beamEffectInstance;
        private DestructionBeamVisual _beamVisual;
        private BeamAimIndicator _aimIndicatorInstance;

        private LocalPlayerMover _mover;
        private DogAnimationDriver _animationDriver;
        private PlayerHealth _health;
        private PlayerAttack _playerAttack;

        // ---- 公開API -------------------------------------

        /// <summary>いま操作しているプレイヤーのビームスキル。UI から参照する</summary>
        public static PlayerBeamSkill Local { get; private set; }

        /// <summary>狙い中(長押し中)かどうか</summary>
        public bool IsAiming => _phase == Phase.Aiming;

        /// <summary>ビーム照射中かどうか</summary>
        public bool IsFiring => _phase == Phase.Firing;

        /// <summary>狙い中または照射中(この間は通常攻撃を出させない)</summary>
        public bool IsBusy => _phase != Phase.Ready;

        /// <summary>クールタイムの残り具合(1=撃った直後、0=撃てる)</summary>
        public float CooldownRatio01 =>
            _cooldownSec <= 0f ? 0f : Mathf.Clamp01(_cooldownRemainSec / _cooldownSec);

        /// <summary>次に使えるまでの残り秒数</summary>
        public float CooldownRemainSec => Mathf.Max(0f, _cooldownRemainSec);

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _mover = GetComponent<LocalPlayerMover>();
            _animationDriver = GetComponent<DogAnimationDriver>();
            _health = GetComponent<PlayerHealth>();
            _playerAttack = GetComponent<PlayerAttack>();
        }

        private void Start()
        {
            if (IsOwner) Local = this;
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        private void OnDisable()
        {
            // 無効化されたら狙いを解除し、移動ロックを残さない
            if (_phase == Phase.Aiming) CancelAiming();
        }

        private void Update()
        {
            if (IsOwner) UpdateOwnerInput();

            // 照射の進行は全クライアントで動かす(見た目とアニメを揃えるため)
            if (_phase == Phase.Firing) UpdateFiring();
        }

        // ---- 入力と状態遷移(本人のみ) ---------------------

        private void UpdateOwnerInput()
        {
            if (_cooldownRemainSec > 0f) _cooldownRemainSec -= Time.deltaTime;

            bool held = ReadHoldInput();
            bool pressedNow = held && !_wasHeldLastFrame;
            _wasHeldLastFrame = held;

            switch (_phase)
            {
                case Phase.Ready:
                    // 押しっぱなしからの自動発動を防ぐため、押した瞬間だけ受け付ける
                    if (pressedNow && CanStartAiming()) StartAiming();
                    break;

                case Phase.Aiming:
                    if (!Battle.BattlePlayGate.IsPlayable || (_health != null && _health.IsDead))
                    {
                        CancelAiming();
                    }
                    else if (!held)
                    {
                        Fire();
                    }
                    break;
            }
        }

        private bool CanStartAiming()
        {
            if (_cooldownRemainSec > 0f) return false;
            if (!Battle.BattlePlayGate.IsPlayable) return false;
            if (_health != null && _health.IsDead) return false;
            if (_playerAttack != null && _playerAttack.IsAttacking) return false;

            // 元気玉スキルの最中はビームを出せない
            PlayerEnergyBallSkill energyBallSkill = GetComponent<PlayerEnergyBallSkill>();
            if (energyBallSkill != null && energyBallSkill.IsBusy) return false;

            return true;
        }

        /// <summary>Rキー / ゲームパッドB / 画面のスキルボタンのいずれかが押されているか</summary>
        private bool ReadHoldInput()
        {
            bool held = false;

            if (_useRKey)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.rKey.isPressed) held = true;
            }

            if (_useGamepadEast)
            {
                Gamepad gamepad = Gamepad.current;
                if (gamepad != null && gamepad.buttonEast.isPressed) held = true;
            }

            if (_useTouchButton)
            {
                TouchControls touch = TouchControls.Instance;
                if (touch != null && touch.SkillHeld) held = true;
            }

            return held;
        }

        private void StartAiming()
        {
            _phase = Phase.Aiming;

            // 狙い中は移動せず、その場で向きだけ変えられるようにする
            if (_mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.RotateOnly;

            if (_aimIndicatorPrefab != null)
            {
                _aimIndicatorInstance = Instantiate(_aimIndicatorPrefab, transform);
                _aimIndicatorInstance.transform.localPosition = Vector3.zero;
                _aimIndicatorInstance.transform.localRotation = Quaternion.identity;
                _aimIndicatorInstance.Configure(_beamLength, _beamWidth);
            }
        }

        private void CancelAiming()
        {
            _phase = Phase.Ready;
            if (_mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.None;
            DestroyAimIndicator();
        }

        /// <summary>指を離した瞬間。クールタイムを開始し、全クライアントで照射を始める</summary>
        private void Fire()
        {
            DestroyAimIndicator();
            _cooldownRemainSec = _cooldownSec;

            Vector3 origin = transform.position
                + Vector3.up * _originHeight
                + transform.forward * _originForwardOffset;

            photonView.RPC(nameof(RpcStartBeam), RpcTarget.All, origin, transform.forward);
        }

        private void DestroyAimIndicator()
        {
            if (_aimIndicatorInstance == null) return;
            Destroy(_aimIndicatorInstance.gameObject);
            _aimIndicatorInstance = null;
        }

        // ---- RPC -----------------------------------------

        /// <summary>照射の開始。全員のクライアントで呼ばれ、見た目とアニメを揃える</summary>
        [PunRPC]
        private void RpcStartBeam(Vector3 origin, Vector3 direction)
        {
            // 万一前回の照射が残っていたら片付けてから始める
            if (_phase == Phase.Firing) FinishFiring();

            _phase = Phase.Firing;
            _fireElapsedSec = 0f;
            _currentBeamLength = 0f;
            _beamOrigin = origin;
            _beamDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            _targetStates.Clear();

            // 最初の痕は根元から1間隔ぶん先に置く(根元はプレイヤーの足元なので避ける)
            _nextDecalDistance = _decalIntervalMeters;

            if (_beamEffectPrefab != null)
            {
                _beamEffectInstance = Instantiate(
                    _beamEffectPrefab, _beamOrigin, Quaternion.LookRotation(_beamDirection));

                _beamVisual = _beamEffectInstance.GetComponent<DestructionBeamVisual>();
                if (_beamVisual != null)
                {
                    _beamVisual.Configure(_beamOrigin, _beamDirection, _currentBeamLength, _beamWidth);
                }

                // パーティクルが混ざっていた場合に備えて Hierarchy スケーリングにしておく
                foreach (var ps in _beamEffectInstance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
            }

            // 頭突きモーションを頭を突き出した位置で止めて照射ポーズにする
            if (_animationDriver != null) _animationDriver.HoldAttackPose(_poseFreezeNormalizedTime);

            // 照射中は移動も向き変えもできない(本人のみ)
            if (IsOwner && _mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.Full;
        }

        /// <summary>ヒットの通知。全員のクライアントでエフェクトとダメージ処理を行う</summary>
        [PunRPC]
        private void RpcBeamHit(Vector3 hitPoint, int targetNetworkId, int damage, PhotonMessageInfo info)
        {
            HitTarget target = HitTarget.Find(targetNetworkId);
            if (target == null) return;

            Vector3 position = target.GetEffectPosition(hitPoint);

            if (_hitEffectPrefab != null)
            {
                Vector3 toTarget = position - transform.position;
                Quaternion rotation = toTarget.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(toTarget.normalized, Vector3.up)
                    : transform.rotation;
                AttackEffect.Spawn(_hitEffectPrefab, position, rotation, _hitEffectScale, _hitEffectLifeSec);
            }

            if (_damagePopupPrefab != null)
            {
                GameObject popup = Instantiate(_damagePopupPrefab, hitPoint, Quaternion.identity);
                DamagePopup component = popup.GetComponent<DamagePopup>();
                if (component != null) component.Play(damage);
            }

            int attackerActorNumber = info.Sender != null ? info.Sender.ActorNumber : -1;
            target.NotifyHit(position, attackerActorNumber, damage);
        }

        // ---- 照射の進行(全クライアント) -------------------

        private void UpdateFiring()
        {
            _fireElapsedSec += Time.deltaTime;

            UpdateBeamLength();
            SpawnBeamDecals();

            // 当たり判定は操作している本人だけが取る(二重ダメージを防ぐ)
            if (IsOwner) UpdateBeamHit();

            if (_fireElapsedSec >= _fireDurationSec) FinishFiring();
        }

        /// <summary>ビームを根元から徐々に伸ばし、見た目に反映する</summary>
        private void UpdateBeamLength()
        {
            if (_growDurationSec <= 0f)
            {
                _currentBeamLength = _beamLength;
            }
            else
            {
                float t = Mathf.Clamp01(_fireElapsedSec / _growDurationSec);
                _currentBeamLength = Mathf.Lerp(0f, _beamLength, t);
            }

            if (_beamVisual != null)
            {
                _beamVisual.Configure(_beamOrigin, _beamDirection, _currentBeamLength, _beamWidth);
            }
        }

        /// <summary>
        /// ビームが伸びて指定間隔を越えるたびに、その真下の地面へ痕(デカール)を置く。
        /// 見た目だけの演出なので、エフェクトと同じく全クライアントで動くこの処理から呼べば
        /// 追加の通信なしで全員の画面に痕が出る(ゴリラのビームと同じ方式)。
        /// </summary>
        private void SpawnBeamDecals()
        {
            if (_beamDecalPrefab == null) return;

            // ビームは体の高さから出ているので、発射時の足元の高さに落とす
            float groundY = _beamOrigin.y - _originHeight;

            while (_nextDecalDistance <= _currentBeamLength)
            {
                Vector3 point = _beamOrigin + _beamDirection * _nextDecalDistance;
                point.y = groundY;

                AttackDecal.Spawn(_beamDecalPrefab, point, _beamWidth * 2f * _decalWidthScale);
                _nextDecalDistance += _decalIntervalMeters;
            }
        }

        private void FinishFiring()
        {
            _phase = Phase.Ready;
            _targetStates.Clear();

            if (_beamEffectInstance != null)
            {
                if (_beamVisual != null) _beamVisual.FadeOut(_fadeOutDurationSec);
                else Destroy(_beamEffectInstance);

                _beamEffectInstance = null;
                _beamVisual = null;
            }

            // 止めていた頭突きモーションを再開し、最後まで再生してもらう
            if (_animationDriver != null) _animationDriver.ReleaseAttackPose();

            if (IsOwner && _mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.None;
        }

        // ---- 当たり判定(本人のみ) -------------------------

        /// <summary>
        /// ビームのカプセル内にいる HitTarget を調べてダメージを与える。
        /// 入った瞬間は初撃、入り続けている間は一定間隔で継続ダメージ。
        /// 一度出ると状態がリセットされ、再度入れば再び初撃になる(ゴリラのビームと同じ挙動)。
        /// </summary>
        private void UpdateBeamHit()
        {
            _presentThisFrame.Clear();

            Vector3 endPoint = _beamOrigin + _beamDirection * _currentBeamLength;
            int count = Physics.OverlapCapsuleNonAlloc(
                _beamOrigin, endPoint, _beamWidth, _overlapBuffer, _targetLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapBuffer[i];
                if (collider == null) continue;
                if (collider.transform == transform || collider.transform.IsChildOf(transform)) continue;

                HitTarget target = collider.GetComponentInParent<HitTarget>();
                if (target == null || !target.CanBeHit) continue;

                // ネットワークIDが無い相手はダメージを届けられないので対象外
                if (target.NetworkId == 0) continue;

                int id = target.GetInstanceID();
                if (!_presentThisFrame.Add(id)) continue;

                if (_targetStates.TryGetValue(id, out TargetState state))
                {
                    state.Collider = collider;
                    state.JustEntered = false;
                }
                else
                {
                    // 入った瞬間: 初撃ダメージ
                    _targetStates[id] = new TargetState
                    {
                        Target = target,
                        Collider = collider,
                        TickTimer = 0f,
                        JustEntered = true,
                    };
                    SendBeamHit(target, collider, _initialDamage);
                }
            }

            // 継続ダメージと、ビームから出た相手の掃除
            _removeWork.Clear();
            foreach (var pair in _targetStates)
            {
                TargetState state = pair.Value;

                if (!_presentThisFrame.Contains(pair.Key))
                {
                    _removeWork.Add(pair.Key);
                    continue;
                }

                if (state.JustEntered) continue;
                if (state.Target == null) { _removeWork.Add(pair.Key); continue; }

                state.TickTimer += Time.deltaTime;
                while (state.TickTimer >= _tickIntervalSec)
                {
                    state.TickTimer -= _tickIntervalSec;
                    SendBeamHit(state.Target, state.Collider, _tickDamage);
                }
            }

            foreach (int id in _removeWork) _targetStates.Remove(id);
        }

        private void SendBeamHit(HitTarget target, Collider collider, int damage)
        {
            if (damage <= 0) return;

            // ビームの軸上で相手に一番近い点を求め、そこから相手表面のヒット位置を出す
            Vector3 toTarget = target.transform.position - _beamOrigin;
            float along = Mathf.Clamp(Vector3.Dot(toTarget, _beamDirection), 0f, _currentBeamLength);
            Vector3 axisPoint = _beamOrigin + _beamDirection * along;
            Vector3 hitPoint = collider != null ? collider.ClosestPoint(axisPoint) : target.transform.position;

            photonView.RPC(nameof(RpcBeamHit), RpcTarget.All, hitPoint, target.NetworkId, damage);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>このクライアントがこのキャラを操作しているか</summary>
        private bool IsOwner => photonView == null || photonView.IsMine;
    }
}
