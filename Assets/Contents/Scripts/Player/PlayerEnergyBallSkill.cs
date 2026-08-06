using System.Collections.Generic;
using Photon.Pun;
using ProjectKMP.Attack;
using ProjectKMP.Dog;
using ProjectKMP.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 元気玉スキル。長押し(Eキー / ゲームパッドY / 画面の元気玉ボタン)で照準しながら
    /// 頭上にエネルギー玉をチャージし、離すと狙った場所へ振り下ろす。
    /// 照準は射程の上限(円)の中で移動入力を使って自由に選べる。
    /// チャージが完了する前に離すとキャンセル(クールタイムなし)。
    /// 着弾時に範囲爆発ダメージを与え、その後しばらく残留ダメージ地帯が残る。
    /// 発動・投擲・ヒットは RPC で全クライアントに配り、当たり判定は本人だけが取る
    /// (PlayerAttack / PlayerBeamSkill と同じ方式)。
    /// </summary>
    public class PlayerEnergyBallSkill : MonoBehaviourPun
    {
        private enum Phase { Ready, Aiming, Throwing, Impact, Exploding }

        // ---- 定数 ----------------------------------------

        private const int OVERLAP_BUFFER_SIZE = 32;
        private const float TURN_SPEED_DEG = 540f;
        private const float STICK_DEAD_ZONE = 0.2f;

        // ---- インスペクタ設定 ------------------------------

        [Header("ダメージ")]
        [SerializeField, Min(0), Tooltip("着弾時の爆発ダメージ(範囲内の敵全てに1回)")]
        private int _explosionDamage = 25;

        [SerializeField, Min(0), Tooltip("残留地帯の継続ダメージ(1回ぶん)")]
        private int _zoneTickDamage = 3;

        [SerializeField, Min(0.05f), Tooltip("残留地帯の継続ダメージが入る間隔(秒)")]
        private float _zoneTickIntervalSec = 0.5f;

        [SerializeField, Min(0f), Tooltip("残留地帯が残る時間(秒)。0にすると地帯なし")]
        private float _zoneDurationSec = 4f;

        [Header("クールタイム")]
        [SerializeField, Min(0f), Tooltip("投げてから次に使えるようになるまでの時間(秒)")]
        private float _cooldownSec = 15f;

        [Header("照準")]
        [SerializeField, Min(1f), Tooltip("着弾点を選べる範囲の上限(プレイヤー中心の半径・m)")]
        private float _maxRange = 8f;

        [SerializeField, Min(0.5f), Tooltip("照準マーカーの移動速度(m/秒)")]
        private float _markerMoveSpeed = 8f;

        [Header("チャージ")]
        [SerializeField, Min(0.1f), Tooltip("玉が完成するまでの時間(秒)。完成前に離すとキャンセル")]
        private float _chargeDurationSec = 1f;

        [SerializeField, Min(0.5f), Tooltip("玉を浮かべる高さ(プレイヤーの足元から・m)")]
        private float _ballHeight = 2.2f;

        [SerializeField, Min(0.1f), Tooltip("完成時の玉の大きさ(直径・m)")]
        private float _ballMaxScale = 1.5f;

        [Header("投擲")]
        [SerializeField, Min(1f), Tooltip("玉が飛ぶ速さ(m/秒)")]
        private float _throwSpeed = 14f;

        [SerializeField, Min(0f), Tooltip("投げたときの山なりの高さ(m)")]
        private float _arcHeight = 1.0f;

        [Header("爆発")]
        [SerializeField, Min(0.5f), Tooltip("爆発の半径(m)。着弾時の一発ダメージの範囲")]
        private float _explosionRadius = 3f;

        [SerializeField, Min(0.5f), Tooltip("残留ダメージ地帯の半径(m)")]
        private float _zoneRadius = 4.5f;

        [SerializeField, Min(0f), Tooltip("着弾後、玉がその場に残る時間(秒)。この後に爆発する")]
        private float _impactLingerSec = 0.35f;

        [SerializeField, Min(0.05f), Tooltip("爆発で玉が膨らみながら消えるまでの時間(秒)")]
        private float _explodeExpandSec = 0.45f;

        [SerializeField, Min(1f), Tooltip("爆発時に玉が元の何倍まで膨らむか")]
        private float _explodeScaleMul = 1.8f;

        [Header("ウルト演出")]
        [SerializeField, Tooltip("チャージ中、玉へエネルギーが収束するエフェクト。未設定なら出さない")]
        private GameObject _chargeConvergeEffectPrefab;

        [SerializeField, Min(0.1f), Tooltip("収束エフェクトの大きさ倍率")]
        private float _chargeEffectScale = 2f;

        [SerializeField, Tooltip("爆発時に地面へ広がる衝撃波。未設定なら出さない")]
        private EnergyShockwave _shockwavePrefab;

        [SerializeField, Min(0f), Tooltip("爆発時のカメラの揺れの強さ(m)。0で揺らさない")]
        private float _cameraShakeAmplitude = 0.4f;

        [SerializeField, Min(0f), Tooltip("カメラの揺れの長さ(秒)")]
        private float _cameraShakeDurationSec = 0.35f;

        [SerializeField, Min(0f), Tooltip("爆発時のヒットストップの長さ(秒)。0で無効")]
        private float _hitStopDurationSec = 0.07f;

        [SerializeField, Range(0.01f, 1f), Tooltip("ヒットストップ中の時間の速さ(1=通常)")]
        private float _hitStopTimeScale = 0.05f;

        [Header("地面の痕")]
        [SerializeField, Tooltip("着弾点の地面に残す痕(デカール)。未設定なら痕を残さない")]
        private AttackDecal _impactDecalPrefab;

        [SerializeField, Min(0.1f), Tooltip("痕の直径を爆発の直径の何倍にするか")]
        private float _decalWidthScale = 1.1f;

        [Header("参照")]
        [SerializeField, Tooltip("チャージ中に頭上へ出す玉のプレハブ")]
        private GameObject _ballPrefab;

        [SerializeField, Tooltip("着弾時の爆発エフェクトのプレハブ")]
        private GameObject _explosionEffectPrefab;

        [SerializeField, Min(0.1f), Tooltip("爆発エフェクトを消すまでの秒数")]
        private float _explosionEffectLifeSec = 2f;

        [SerializeField, Tooltip("残留地帯の見た目のプレハブ")]
        private EnergyBallZoneVisual _zoneVisualPrefab;

        [SerializeField, Tooltip("照準表示(射程の円 + 着弾マーカー)のプレハブ")]
        private EnergyBallAimIndicator _aimIndicatorPrefab;

        [SerializeField, Tooltip("ダメージの数字のプレハブ。未設定なら数字を出さない")]
        private GameObject _damagePopupPrefab;

        [Header("入力")]
        [SerializeField, Tooltip("Eキーの長押しで狙う")]
        private bool _useEKey = true;

        [SerializeField, Tooltip("ゲームパッドのYボタン(上ボタン)の長押しで狙う")]
        private bool _useGamepadNorth = true;

        [SerializeField, Tooltip("画面上の元気玉ボタンの長押しで狙う")]
        private bool _useTouchButton = true;

        [Header("当てる相手")]
        [SerializeField, Tooltip("判定を取るレイヤー。HitTarget が付いた相手(敵)にだけ当たる")]
        private LayerMask _targetLayers = ~0;

        // ---- 内部状態 ------------------------------------

        /// <summary>残留地帯の中にいる相手ごとの継続ダメージの状態</summary>
        private class TargetState
        {
            public HitTarget Target;
            public Collider Collider;
            public float TickTimer;
        }

        private readonly Collider[] _overlapBuffer = new Collider[OVERLAP_BUFFER_SIZE];
        private readonly Dictionary<int, TargetState> _zoneTargetStates = new Dictionary<int, TargetState>();
        private readonly HashSet<int> _presentThisFrame = new HashSet<int>();
        private readonly List<int> _removeWork = new List<int>();

        private Phase _phase = Phase.Ready;
        private float _cooldownRemainSec;
        private float _chargeElapsedSec;
        private bool _wasHeldLastFrame;
        private Vector3 _aimMarkerPosition;

        private GameObject _ballInstance;
        private EnergyBallAimIndicator _aimIndicatorInstance;

        private Vector3 _throwStart;
        private Vector3 _throwTarget;
        private float _throwElapsedSec;
        private float _throwTravelSec;

        private float _zoneRemainSec;
        private Vector3 _zonePosition;

        private float _impactElapsedSec;
        private float _impactStartScale;
        private float _explodeElapsedSec;
        private float _explodeStartScale;

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
        private readonly System.Collections.Generic.List<Renderer> _ballRenderers = new System.Collections.Generic.List<Renderer>();
        private readonly System.Collections.Generic.List<Color> _ballBaseColors = new System.Collections.Generic.List<Color>();
        private MaterialPropertyBlock _ballPropertyBlock;

        private GameObject _chargeEffectInstance;
        private float _hitStopRemainSec;

        private LocalPlayerMover _mover;
        private DogAnimationDriver _animationDriver;
        private PlayerHealth _health;
        private PlayerAttack _playerAttack;
        private PlayerBeamSkill _beamSkill;
        private Transform _cameraTransform;

        // ---- 公開API -------------------------------------

        /// <summary>いま操作しているプレイヤーの元気玉スキル。UI から参照する</summary>
        public static PlayerEnergyBallSkill Local { get; private set; }

        /// <summary>狙い中または投擲中(この間は他の攻撃を出させない)。着弾後の爆発演出中は含まない</summary>
        public bool IsBusy => _phase == Phase.Aiming || _phase == Phase.Throwing;

        /// <summary>クールタイムの残り具合(1=使った直後、0=使える)</summary>
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
            _beamSkill = GetComponent<PlayerBeamSkill>();
        }

        private void Start()
        {
            if (IsOwner) Local = this;
            if (Camera.main != null) _cameraTransform = Camera.main.transform;
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;

            // ヒットストップ中に破棄されても時間を止めたままにしない
            if (_hitStopRemainSec > 0f) Time.timeScale = 1f;
        }

        private void OnDisable()
        {
            if (_phase == Phase.Aiming && IsOwner) CancelAiming();
        }

        private void Update()
        {
            // ヒットストップの解除は実時間で数える(タイムスケールの影響を受けない)
            if (_hitStopRemainSec > 0f)
            {
                _hitStopRemainSec -= Time.unscaledDeltaTime;
                if (_hitStopRemainSec <= 0f) Time.timeScale = 1f;
            }

            if (IsOwner) UpdateOwnerInput();

            // チャージと投擲の見た目は全クライアントで動かす
            if (_phase == Phase.Aiming) UpdateBallCharge();
            if (_phase == Phase.Throwing) UpdateThrowing();
            if (_phase == Phase.Impact) UpdateImpact();
            if (_phase == Phase.Exploding) UpdateExploding();

            // 残留地帯のダメージは本人だけが取る
            if (IsOwner) UpdateZoneDamage();
        }

        // ---- 入力と状態遷移(本人のみ) ---------------------

        private void UpdateOwnerInput()
        {
            if (_cooldownRemainSec > 0f) _cooldownRemainSec -= Time.deltaTime;

            bool held = ReadHoldInput();
            bool pressedNow = held && !_wasHeldLastFrame;
            _wasHeldLastFrame = held;

            if (_phase == Phase.Ready)
            {
                if (pressedNow && CanStartAiming()) StartAiming();
                return;
            }

            if (_phase != Phase.Aiming) return;

            if (!Battle.BattlePlayGate.IsPlayable || (_health != null && _health.IsDead))
            {
                CancelAiming();
                return;
            }

            UpdateAimMarker();

            if (held) return;

            // チャージ完了前に離したらキャンセル(クールタイムなし)
            if (_chargeElapsedSec < _chargeDurationSec)
            {
                CancelAiming();
            }
            else
            {
                Throw();
            }
        }

        private bool CanStartAiming()
        {
            if (_cooldownRemainSec > 0f) return false;
            if (!Battle.BattlePlayGate.IsPlayable) return false;
            if (_health != null && _health.IsDead) return false;
            if (_playerAttack != null && _playerAttack.IsAttacking) return false;
            if (_beamSkill != null && _beamSkill.IsBusy) return false;
            return true;
        }

        /// <summary>Eキー / ゲームパッドY / 画面の元気玉ボタンのいずれかが押されているか</summary>
        private bool ReadHoldInput()
        {
            bool held = false;

            if (_useEKey)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.eKey.isPressed) held = true;
            }

            if (_useGamepadNorth)
            {
                Gamepad gamepad = Gamepad.current;
                if (gamepad != null && gamepad.buttonNorth.isPressed) held = true;
            }

            if (_useTouchButton)
            {
                TouchControls touch = TouchControls.Instance;
                if (touch != null && touch.EnergyBallHeld) held = true;
            }

            return held;
        }

        private void StartAiming()
        {
            // マーカーは正面の少し先から始める
            Vector3 start = transform.position + transform.forward * Mathf.Min(3f, _maxRange);
            start.y = transform.position.y;
            _aimMarkerPosition = start;

            // 狙い中は移動しない。向きはマーカーの方へスキル側で向ける
            if (_mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.Full;

            if (_aimIndicatorPrefab != null)
            {
                _aimIndicatorInstance = Instantiate(_aimIndicatorPrefab, transform.position, Quaternion.identity);
                _aimIndicatorInstance.Configure(_maxRange, _explosionRadius);
                _aimIndicatorInstance.SetMarkerPosition(_aimMarkerPosition);
            }

            photonView.RPC(nameof(RpcStartCharge), RpcTarget.All);
        }

        /// <summary>移動入力(WASD/左スティック/仮想スティック)で照準マーカーを動かし、射程内に収める</summary>
        private void UpdateAimMarker()
        {
            Vector2 input = ReadMoveInput();
            Vector3 moveDir = ToWorldDirection(input);
            _aimMarkerPosition += moveDir * (_markerMoveSpeed * Time.deltaTime);

            // 射程の円の中に収める
            Vector3 fromPlayer = _aimMarkerPosition - transform.position;
            fromPlayer.y = 0f;
            if (fromPlayer.magnitude > _maxRange)
            {
                fromPlayer = fromPlayer.normalized * _maxRange;
            }
            _aimMarkerPosition = transform.position + fromPlayer;
            _aimMarkerPosition.y = transform.position.y;

            // プレイヤーはマーカーの方を向く
            if (fromPlayer.sqrMagnitude > 0.01f)
            {
                Quaternion look = Quaternion.LookRotation(fromPlayer.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, TURN_SPEED_DEG * Time.deltaTime);
            }

            if (_aimIndicatorInstance != null)
            {
                _aimIndicatorInstance.transform.position = transform.position;
                _aimIndicatorInstance.SetMarkerPosition(_aimMarkerPosition);
            }
        }

        private void CancelAiming()
        {
            DestroyAimIndicator();
            if (_mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.None;
            photonView.RPC(nameof(RpcCancelCharge), RpcTarget.All);
        }

        private void Throw()
        {
            DestroyAimIndicator();
            _cooldownRemainSec = _cooldownSec;
            if (_mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.None;

            photonView.RPC(nameof(RpcThrow), RpcTarget.All, _aimMarkerPosition);
        }

        private void DestroyAimIndicator()
        {
            if (_aimIndicatorInstance == null) return;
            Destroy(_aimIndicatorInstance.gameObject);
            _aimIndicatorInstance = null;
        }

        // ---- RPC -----------------------------------------

        /// <summary>チャージ開始。全員のクライアントで頭上に玉を出して育て始める</summary>
        [PunRPC]
        private void RpcStartCharge()
        {
            DestroyBall();

            _phase = Phase.Aiming;
            _chargeElapsedSec = 0f;

            if (_ballPrefab != null)
            {
                _ballInstance = Instantiate(_ballPrefab, transform);
                _ballInstance.transform.localPosition = Vector3.up * _ballHeight;
                _ballInstance.transform.localScale = Vector3.zero;
                CacheBallRenderers();
            }

            // エネルギーが玉へ吸い込まれていく収束エフェクト
            if (_chargeConvergeEffectPrefab != null)
            {
                _chargeEffectInstance = Instantiate(_chargeConvergeEffectPrefab, transform);
                _chargeEffectInstance.transform.localPosition = Vector3.up * _ballHeight;
                _chargeEffectInstance.transform.localScale = Vector3.one * _chargeEffectScale;
            }
        }

        /// <summary>チャージのキャンセル。全員のクライアントで玉を消す</summary>
        [PunRPC]
        private void RpcCancelCharge()
        {
            _phase = Phase.Ready;
            DestroyBall();
            DestroyChargeEffect();
        }

        /// <summary>投擲の開始。全員のクライアントで玉を飛ばし、投げるモーションを再生する</summary>
        [PunRPC]
        private void RpcThrow(Vector3 target)
        {
            DestroyChargeEffect();

            if (_ballInstance == null)
            {
                // 玉が無い(生成に失敗した)場合でも着弾処理だけは行えるようにする
                _throwStart = transform.position + Vector3.up * _ballHeight;
            }
            else
            {
                _ballInstance.transform.SetParent(null);
                _ballInstance.transform.localScale = Vector3.one * _ballMaxScale;
                _throwStart = _ballInstance.transform.position;
            }

            _phase = Phase.Throwing;
            _throwTarget = target;
            _throwElapsedSec = 0f;

            float distance = Vector3.Distance(_throwStart, target);
            _throwTravelSec = Mathf.Max(0.2f, distance / _throwSpeed);

            // 投げる動作として頭突きモーションを流用する
            if (_animationDriver != null) _animationDriver.PlayAttack();
        }

        /// <summary>ヒットの通知。全員のクライアントでエフェクトとダメージ処理を行う</summary>
        [PunRPC]
        private void RpcEnergyBallHit(Vector3 hitPoint, int targetNetworkId, int damage, PhotonMessageInfo info)
        {
            HitTarget target = HitTarget.Find(targetNetworkId);
            if (target == null) return;

            Vector3 position = target.GetEffectPosition(hitPoint);

            if (_damagePopupPrefab != null)
            {
                GameObject popup = Instantiate(_damagePopupPrefab, hitPoint, Quaternion.identity);
                DamagePopup component = popup.GetComponent<DamagePopup>();
                if (component != null) component.Play(damage);
            }

            int attackerActorNumber = info.Sender != null ? info.Sender.ActorNumber : -1;
            target.NotifyHit(position, attackerActorNumber, damage);
        }

        // ---- チャージと投擲の進行(全クライアント) ---------

        /// <summary>チャージ中、玉を徐々に大きくする</summary>
        private void UpdateBallCharge()
        {
            _chargeElapsedSec += Time.deltaTime;

            if (_ballInstance == null) return;

            float t = Mathf.Clamp01(_chargeElapsedSec / _chargeDurationSec);
            // 最初に勢いよく育ち、完成に近づくほどゆっくりになる
            float eased = 1f - (1f - t) * (1f - t);
            _ballInstance.transform.localScale = Vector3.one * (_ballMaxScale * eased);
        }

        /// <summary>投げた玉を山なりに飛ばし、着弾したら爆発させる</summary>
        private void UpdateThrowing()
        {
            _throwElapsedSec += Time.deltaTime;
            float t = Mathf.Clamp01(_throwElapsedSec / _throwTravelSec);

            if (_ballInstance != null)
            {
                Vector3 position = Vector3.Lerp(_throwStart, _throwTarget, t);
                position += Vector3.up * (Mathf.Sin(t * Mathf.PI) * _arcHeight);
                _ballInstance.transform.position = position;
            }

            if (t >= 1f) StartImpact();
        }

        /// <summary>着弾。玉を着地したその場に残し、時間が来たら爆発させる</summary>
        private void StartImpact()
        {
            _phase = Phase.Impact;
            _impactElapsedSec = 0f;
            _impactStartScale = _ballInstance != null ? _ballInstance.transform.localScale.x : _ballMaxScale;
        }

        /// <summary>
        /// 着弾後の待ち。破裂の直前に玉をわずかに縮めて「タメ」を作る
        /// (縮んでから一気に膨らむことで破裂感を出す)。時間が来たら爆発を始める。
        /// </summary>
        private void UpdateImpact()
        {
            _impactElapsedSec += Time.deltaTime;

            if (_ballInstance != null && _impactLingerSec > 0f)
            {
                float t = Mathf.Clamp01(_impactElapsedSec / _impactLingerSec);
                float scale = Mathf.Lerp(_impactStartScale, _impactStartScale * 0.85f, t * t);
                _ballInstance.transform.localScale = Vector3.one * scale;
            }

            if (_impactElapsedSec < _impactLingerSec) return;
            StartExplosion();
        }

        /// <summary>爆発の開始。全クライアントで見た目を出し、本人だけがダメージを与える</summary>
        private void StartExplosion()
        {
            _phase = Phase.Exploding;
            _explodeElapsedSec = 0f;
            _explodeStartScale = _ballInstance != null ? _ballInstance.transform.localScale.x : _ballMaxScale;

            // 玉からの新しい粒は止め、膨らみながら消える本体だけを残す
            if (_ballInstance != null)
            {
                foreach (var ps in _ballInstance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            PlayExplosionImpact();

            if (_explosionEffectPrefab != null)
            {
                AttackEffect.Spawn(
                    _explosionEffectPrefab, _throwTarget, Quaternion.identity,
                    _explosionRadius / 3f, _explosionEffectLifeSec);
            }

            if (_zoneVisualPrefab != null && _zoneDurationSec > 0f)
            {
                EnergyBallZoneVisual.Spawn(_zoneVisualPrefab, _throwTarget, _zoneRadius, _zoneDurationSec);
            }

            // 着弾の痕。見た目だけの演出なので全クライアントのこの処理から呼べば追加の通信なしで揃う
            if (_impactDecalPrefab != null)
            {
                AttackDecal.Spawn(_impactDecalPrefab, _throwTarget, _explosionRadius * 2f * _decalWidthScale);
            }

            if (!IsOwner) return;

            // 爆発ダメージ(1回)
            ApplyAreaDamageOnce(_throwTarget, _explosionDamage);

            // 残留地帯を開始
            if (_zoneDurationSec > 0f)
            {
                _zoneRemainSec = _zoneDurationSec;
                _zonePosition = _throwTarget;
                _zoneTargetStates.Clear();
            }
        }

        /// <summary>爆発。玉を膨らませながら透明にしていき、消えきったら破棄する</summary>
        private void UpdateExploding()
        {
            _explodeElapsedSec += Time.deltaTime;
            float t = Mathf.Clamp01(_explodeElapsedSec / _explodeExpandSec);

            // 破裂感を出すため、立ち上がりを鋭く(3乗イーズアウト)
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);

            if (_ballInstance != null)
            {
                float scale = Mathf.Lerp(_explodeStartScale, _explodeStartScale * _explodeScaleMul, eased);
                _ballInstance.transform.localScale = Vector3.one * scale;

                // 序盤は明るいまま膨らみ、最後に一気に透明へ(だらだら薄くならない)
                SetBallAlpha(1f - t * t * t);
            }

            if (t >= 1f)
            {
                _phase = Phase.Ready;
                DestroyBall();
            }
        }

        /// <summary>玉のレンダラーを覚えておく(爆発時のフェードに使う)</summary>
        private void CacheBallRenderers()
        {
            if (_ballPropertyBlock == null) _ballPropertyBlock = new MaterialPropertyBlock();

            _ballRenderers.Clear();
            _ballBaseColors.Clear();
            if (_ballInstance == null) return;

            foreach (var ballRenderer in _ballInstance.GetComponentsInChildren<Renderer>(true))
            {
                if (ballRenderer is ParticleSystemRenderer) continue;

                Color color = Color.white;
                if (ballRenderer.sharedMaterial != null && ballRenderer.sharedMaterial.HasProperty(BASE_COLOR_ID))
                {
                    color = ballRenderer.sharedMaterial.GetColor(BASE_COLOR_ID);
                }

                _ballRenderers.Add(ballRenderer);
                _ballBaseColors.Add(color);
            }
        }

        /// <summary>マテリアルを複製せず、プロパティブロックで玉の透明度だけ変える</summary>
        private void SetBallAlpha(float alpha01)
        {
            if (_ballPropertyBlock == null) return;

            for (int i = 0; i < _ballRenderers.Count; i++)
            {
                Renderer ballRenderer = _ballRenderers[i];
                if (ballRenderer == null) continue;

                Color color = _ballBaseColors[i];
                color.a *= alpha01;

                ballRenderer.GetPropertyBlock(_ballPropertyBlock);
                _ballPropertyBlock.SetColor(BASE_COLOR_ID, color);
                ballRenderer.SetPropertyBlock(_ballPropertyBlock);
            }
        }

        private void DestroyBall()
        {
            if (_ballInstance == null) return;
            Destroy(_ballInstance);
            _ballInstance = null;
        }

        private void DestroyChargeEffect()
        {
            if (_chargeEffectInstance == null) return;
            Destroy(_chargeEffectInstance);
            _chargeEffectInstance = null;
        }

        /// <summary>衝撃波・カメラシェイク・ヒットストップで爆発の衝撃を演出する(全クライアント)</summary>
        private void PlayExplosionImpact()
        {
            if (_shockwavePrefab != null)
            {
                EnergyShockwave.Spawn(
                    _shockwavePrefab, _throwTarget + Vector3.up * 0.1f,
                    _ballMaxScale * 0.5f, _zoneRadius * 2.2f, 0.45f);
            }

            if (_cameraShakeAmplitude > 0f && _cameraShakeDurationSec > 0f)
            {
                ThirdPersonCamera playerCamera = FindAnyObjectByType<ThirdPersonCamera>();
                if (playerCamera != null) playerCamera.Shake(_cameraShakeAmplitude, _cameraShakeDurationSec);
            }

            if (_hitStopDurationSec > 0f)
            {
                Time.timeScale = _hitStopTimeScale;
                _hitStopRemainSec = _hitStopDurationSec;
            }
        }

        // ---- 当たり判定(本人のみ) -------------------------

        /// <summary>範囲内の HitTarget 全てに1回だけダメージを与える(爆発用)</summary>
        private void ApplyAreaDamageOnce(Vector3 center, int damage)
        {
            if (damage <= 0) return;

            _presentThisFrame.Clear();
            int count = Physics.OverlapSphereNonAlloc(
                center, _explosionRadius, _overlapBuffer, _targetLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapBuffer[i];
                HitTarget target = FindValidTarget(collider);
                if (target == null) continue;

                int id = target.GetInstanceID();
                if (!_presentThisFrame.Add(id)) continue;

                SendHit(target, collider, center, damage);
            }
        }

        /// <summary>残留地帯の中にいる敵へ一定間隔でダメージを与える</summary>
        private void UpdateZoneDamage()
        {
            if (_zoneRemainSec <= 0f) return;

            _zoneRemainSec -= Time.deltaTime;
            if (_zoneRemainSec <= 0f)
            {
                _zoneTargetStates.Clear();
                return;
            }

            _presentThisFrame.Clear();
            int count = Physics.OverlapSphereNonAlloc(
                _zonePosition, _zoneRadius, _overlapBuffer, _targetLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapBuffer[i];
                HitTarget target = FindValidTarget(collider);
                if (target == null) continue;

                int id = target.GetInstanceID();
                if (!_presentThisFrame.Add(id)) continue;

                if (_zoneTargetStates.TryGetValue(id, out TargetState state))
                {
                    state.Collider = collider;
                }
                else
                {
                    // 地帯に入った直後は初撃なし(爆発が初撃のぶん)。間隔が経ってから継続ダメージが入る
                    _zoneTargetStates[id] = new TargetState { Target = target, Collider = collider, TickTimer = 0f };
                }
            }

            _removeWork.Clear();
            foreach (var pair in _zoneTargetStates)
            {
                TargetState state = pair.Value;

                if (!_presentThisFrame.Contains(pair.Key) || state.Target == null)
                {
                    _removeWork.Add(pair.Key);
                    continue;
                }

                state.TickTimer += Time.deltaTime;
                while (state.TickTimer >= _zoneTickIntervalSec)
                {
                    state.TickTimer -= _zoneTickIntervalSec;
                    SendHit(state.Target, state.Collider, _zonePosition, _zoneTickDamage);
                }
            }

            foreach (int id in _removeWork) _zoneTargetStates.Remove(id);
        }

        /// <summary>攻撃対象として有効な HitTarget を取り出す。無効なら null</summary>
        private HitTarget FindValidTarget(Collider collider)
        {
            if (collider == null) return null;
            if (collider.transform == transform || collider.transform.IsChildOf(transform)) return null;

            HitTarget target = collider.GetComponentInParent<HitTarget>();
            if (target == null || !target.CanBeHit) return null;
            if (target.NetworkId == 0) return null;

            return target;
        }

        private void SendHit(HitTarget target, Collider collider, Vector3 center, int damage)
        {
            if (damage <= 0) return;

            Vector3 hitPoint = collider != null ? collider.ClosestPoint(center) : target.transform.position;
            photonView.RPC(nameof(RpcEnergyBallHit), RpcTarget.All, hitPoint, target.NetworkId, damage);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>WASD / 左スティック / 仮想スティックの入力を取る(照準マーカー移動用)</summary>
        private Vector2 ReadMoveInput()
        {
            Vector2 value = Vector2.zero;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) value.y += 1f;
                if (keyboard.sKey.isPressed) value.y -= 1f;
                if (keyboard.dKey.isPressed) value.x += 1f;
                if (keyboard.aKey.isPressed) value.x -= 1f;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.leftStick.ReadValue();
                if (stick.sqrMagnitude > STICK_DEAD_ZONE * STICK_DEAD_ZONE) value += stick;
            }

            TouchControls touch = TouchControls.Instance;
            if (touch != null)
            {
                Vector2 stick = touch.MoveValue;
                if (stick.sqrMagnitude > STICK_DEAD_ZONE * STICK_DEAD_ZONE) value += stick;
            }

            return Vector2.ClampMagnitude(value, 1f);
        }

        /// <summary>入力をカメラ基準のワールド方向に変換する</summary>
        private Vector3 ToWorldDirection(Vector2 input)
        {
            if (_cameraTransform == null)
            {
                if (Camera.main != null) _cameraTransform = Camera.main.transform;
                if (_cameraTransform == null) return new Vector3(input.x, 0f, input.y);
            }

            Vector3 forward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(_cameraTransform.right, Vector3.up).normalized;
            return forward * input.y + right * input.x;
        }

        /// <summary>このクライアントがこのキャラを操作しているか</summary>
        private bool IsOwner => photonView == null || photonView.IsMine;
    }
}
