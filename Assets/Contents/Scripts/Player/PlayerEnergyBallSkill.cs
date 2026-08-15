using System.Collections.Generic;
using Photon.Pun;
using ProjectKMP.Attack;
using ProjectKMP.Dog;
using ProjectKMP.UI;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 元気玉スキル。長押し(Qキー / ゲームパッドY / 画面の元気玉ボタン)で照準しながら
    /// 頭上にエネルギー玉をチャージし、離すと狙った場所へ振り下ろす。
    /// 照準は射程の上限(円)の中で移動入力を使って自由に選べる。
    /// チャージが完了する前に離すとキャンセル(クールタイムなし)。
    /// 着弾時に範囲爆発ダメージを与え、その後しばらく残留ダメージ地帯が残る。
    /// 発動・投擲・ヒットは RPC で全クライアントに配り、当たり判定は本人だけが取る
    /// (PlayerAttack / PlayerBeamSkill と同じ方式)。
    /// </summary>
    public class PlayerEnergyBallSkill : MonoBehaviourPun
    {
        private enum Phase { Ready, Aiming, Rising, WindUp, Throwing, Impact, Exploding, Descending }

        // ---- 定数 ----------------------------------------

        private const int OVERLAP_BUFFER_SIZE = 32;
        private const float TURN_SPEED_DEG = 540f;
        private const float STICK_DEAD_ZONE = 0.2f;

        /// <summary>着地できないまま降下し続けるのを防ぐ保険の時間(秒)</summary>
        private const float MAX_DESCEND_SEC = 3.0f;

        /// <summary>RpcThrow が届かないまま固まらないよう、振りかぶりを打ち切るまでの余裕(秒)</summary>
        private const float LEAP_VISUAL_TIMEOUT_SEC = 3.0f;

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

        [Header("発動時の演出")]
        [SerializeField, Range(0.1f, 1f), Tooltip("チャージ中の時間の速さ(1=通常)。溜めの間だけ世界がゆっくりになる")]
        private float _chargeTimeScale = 0.75f;

        [SerializeField, Tooltip("発動中は自分の画面だけ空を夜に落として、玉やエフェクトの光を際立たせる(シーンに SkyAtmosphere がある場合のみ)")]
        private bool _useNightAtmosphere = true;

        [SerializeField, Tooltip("跳び上がりから投げるまでの間、自分の画面にカットインを出す(シーンに SkillCutin がある場合のみ)")]
        private bool _useCutin = true;

        [SerializeField, Min(0f), Tooltip("カットインを長めに出す時間(秒)。跳び上がり+振りかぶりの長さに足される")]
        private float _cutinExtraSec = 0.1f;

        [SerializeField, Tooltip("チャージ中にカメラを寄せる量(m)。負の値で近づく")]
        private float _chargeCameraDistanceOffset = -2.5f;

        [SerializeField, Tooltip("チャージ中の視野角の寄せ(度)。負の値で狭くなる")]
        private float _chargeCameraFovOffset = -8f;

        [SerializeField, Min(0f), Tooltip("チャージ中、足元に収束リングを出す間隔(秒)。0で出さない")]
        private float _chargeRingIntervalSec = 0.3f;

        [SerializeField, Min(0.05f), Tooltip("収束リングが縮みきるまでの時間(秒)")]
        private float _chargeRingDurationSec = 0.45f;

        [SerializeField, Tooltip("チャージ中にキャラ本体を光らせる。未設定なら光らせない")]
        private PlayerSkillGlow _skillGlow;

        [SerializeField, Tooltip("チャージ完了の瞬間に画面を光らせる色(アルファが強さ)")]
        private Color _chargeCompleteFlashColor = new Color(0.75f, 0.95f, 1f, 0.5f);

        [SerializeField, Min(0f), Tooltip("チャージ完了の閃光が消えるまでの時間(秒)。0で光らせない")]
        private float _chargeCompleteFlashSec = 0.25f;

        [Header("発動後の演出")]
        [SerializeField, Min(0f), Tooltip("ヒットストップから通常の速さへ戻すのにかける時間(秒)。0で即座に戻す")]
        private float _hitStopRecoverSec = 0.2f;

        [SerializeField, Tooltip("衝撃波を2枚出す(速い薄いリング + 遅れて広がる太いリング)")]
        private bool _useDoubleShockwave = true;

        [SerializeField, Min(0), Tooltip("爆心の周りに散らすひび割れの数。0で出さない")]
        private int _crackDecalCount = 5;

        [SerializeField, Min(0.05f), Tooltip("ひび割れ1枚の大きさを爆発の直径の何倍にするか")]
        private float _crackDecalScale = 0.45f;

        [SerializeField, Min(0.0f), Tooltip("爆心からこの範囲にある木を倒す(メートル)。0で倒さない")]
        private float _treeBreakRadius = 5.0f;

        [SerializeField, Tooltip("衝撃波が通ったところの草をなぎ倒す")]
        private bool _flattenGrass = true;

        [SerializeField, Min(1), Tooltip("爆発の衝撃波を何回続けて出すか。回を追うごとに弱くなる")]
        private int _shockwaveCount = 4;

        [SerializeField, Min(0f), Tooltip("衝撃波と衝撃波の間隔(秒)")]
        private float _shockwaveIntervalSec = 0.18f;

        [Header("発動(跳び上がり・1回転)")]
        [SerializeField, Min(0f), Tooltip("投げる前に跳び上がる高さ(m)。0なら跳ばずにその場で投げる")]
        private float _riseHeight = 2.5f;

        [SerializeField, Min(0.05f), Tooltip("跳び上がりにかける時間(秒)")]
        private float _riseDurationSec = 0.45f;

        [SerializeField, Min(0), Tooltip("跳び上がりながら何回転するか")]
        private int _spinTurns = 1;

        [SerializeField, Tooltip("回転軸(キャラのローカル軸)。(1,0,0)で前転、(-1,0,0)で後転、(0,1,0)でその場スピン")]
        private Vector3 _spinAxisLocal = Vector3.right;

        [SerializeField, Min(0.1f), Tooltip("投げ終わったあと降りてくる速さ(m/秒)")]
        private float _descendSpeed = 8f;

        [Header("振りかぶり")]
        [SerializeField, Min(0f), Tooltip("投げる直前に玉を引きつける時間(秒)。この“タメ”が投げた感を作る")]
        private float _windUpSec = 0.16f;

        [SerializeField, Min(0f), Tooltip("振りかぶりで玉を後ろへ引く距離(m)")]
        private float _windUpBackDistance = 1.0f;

        [SerializeField, Tooltip("振りかぶりで玉を持ち上げる高さ(m)")]
        private float _windUpUpDistance = 0.4f;

        [SerializeField, Min(1f), Tooltip("振りかぶり中に玉が膨らむ倍率")]
        private float _windUpBallScale = 1.12f;

        [Header("射出の勢い")]
        [SerializeField, Range(1f, 4f), Tooltip("射出の加速の鋭さ。1で等速、大きいほど最初に一気に飛び出す")]
        private float _launchSharpness = 2.2f;

        [SerializeField, Range(0f, 3f), Tooltip("山なりの頂点を手前へ寄せる量。大きいほど終わりぎわが急降下になる")]
        private float _fallBoost = 1.2f;

        [SerializeField, Range(0f, 1f), Tooltip("飛び出した瞬間、玉が進行方向へ伸びる量")]
        private float _launchStretch = 0.5f;

        [SerializeField, Min(0.01f), Tooltip("伸びが元に戻るまでの時間(秒)")]
        private float _stretchRecoverSec = 0.25f;

        [SerializeField, Min(0f), Tooltip("飛行中に玉が自転する速さ(度/秒)")]
        private float _ballSpinSpeed = 320f;

        [SerializeField, Range(0f, 0.5f), Tooltip("飛行中に玉が脈打つ大きさ")]
        private float _ballPulseAmount = 0.06f;

        [SerializeField, Min(0f), Tooltip("脈動の速さ(回/秒)")]
        private float _ballPulseHz = 6f;

        [Header("投げの反動")]
        [SerializeField, Min(0f), Tooltip("投げた瞬間のカメラの揺れの強さ(m)。0で揺らさない")]
        private float _throwShakeAmplitude = 0.22f;

        [SerializeField, Min(0f), Tooltip("投げた瞬間のカメラの揺れの長さ(秒)")]
        private float _throwShakeDurationSec = 0.22f;

        [SerializeField, Tooltip("投げた瞬間に視野角を広げる量(度)。0で広げない")]
        private float _throwFovKick = 7f;

        [SerializeField, Min(0.01f), Tooltip("広げた視野角が戻るまでの時間(秒)")]
        private float _throwFovKickSec = 0.3f;

        [SerializeField, Min(0f), Tooltip("投げた瞬間のヒットストップの長さ(秒)。0で無効")]
        private float _throwHitStopSec = 0.04f;

        [SerializeField, Min(0f), Tooltip("投げた反動で後ろへ下がる速さ(m/秒)。降りながら後退する")]
        private float _throwRecoilSpeed = 1.6f;

        [Header("発射の余波・着弾予告")]
        [SerializeField, Tooltip("投げた瞬間に足元へ衝撃波を出し、草をなぎ倒す")]
        private bool _useLaunchShockwave = true;

        [SerializeField, Min(0.5f), Tooltip("足元の衝撃波が広がる半径(m)")]
        private float _launchShockwaveRadius = 5f;

        [SerializeField, Tooltip("飛行中、着弾点に縮んでいく予告リングを出す")]
        private bool _useImpactWarning = true;

        [Header("地面の痕")]
        [SerializeField, Tooltip("着弾点の地面に残す痕(デカール)。未設定なら痕を残さない")]
        private AttackDecal _impactDecalPrefab;

        [SerializeField, Min(0.1f), Tooltip("痕の直径を爆発の直径の何倍にするか")]
        private float _decalWidthScale = 1.1f;

        [Header("音")]
        [SerializeField, Tooltip("溜めている間ずっと鳴らす音。完成かキャンセルで止まる")]
        private AudioClip _chargeClip;

        [SerializeField, Tooltip("溜めが完成した瞬間の音")]
        private AudioClip _chargeCompleteClip;

        [SerializeField, Tooltip("投げた瞬間の音")]
        private AudioClip _throwClip;

        [SerializeField, Tooltip("爆発の音")]
        private AudioClip _explosionClip;

        [SerializeField, Tooltip("カットインが出る瞬間の音。カットインと同じく自分の画面だけで鳴る")]
        private AudioClip _cutinClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("必殺技の音の大きさ")]
        private float _skillVolume = 0.85f;

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
        [SerializeField, Tooltip("Qキーの長押しで狙う")]
        private bool _useQKey = true;

        [SerializeField, Tooltip("ゲームパッドの LT と RT の同時長押しで狙う")]
        private bool _useGamepadTriggers = true;

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

        private CharacterController _controller;
        private bool _leapActive;
        private float _leapElapsedSec;
        private float _leapStartYawDeg;
        private float _risenHeight;
        private float _windUpElapsedSec;
        private float _descendElapsedSec;
        private bool _descendActive;

        /// <summary>全クライアントで玉の見た目を動かすための、跳び上がり開始からの経過時間</summary>
        private float _leapVisualElapsedSec;

        /// <summary>投げる先。指を離した時点で決まり、振りかぶりが終わってから配る</summary>
        private Vector3 _pendingTarget;

        /// <summary>足元の高さ。空中から投げるので、跳ぶ前の地面の高さを覚えておく</summary>
        private float _groundY;

        private float _stretchRemainSec;
        private float _ballSpinAngleDeg;
        private float _fovKickRemainSec;

        /// <summary>玉の見た目の子。伸び縮みと脈動をここに掛ける(根本は軌跡の太さに影響するので触らない)</summary>
        private readonly List<Transform> _ballVisuals = new List<Transform>();
        private readonly List<Vector3> _ballVisualBaseScales = new List<Vector3>();

        /// <summary>玉に付いているライト。夜の演出中に飛ぶので、周りを照らすと迫力が出る</summary>
        private Light _ballLight;
        private float _ballLightBaseIntensity;

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
        private readonly System.Collections.Generic.List<Renderer> _ballRenderers = new System.Collections.Generic.List<Renderer>();
        private readonly System.Collections.Generic.List<Color> _ballBaseColors = new System.Collections.Generic.List<Color>();
        private MaterialPropertyBlock _ballPropertyBlock;

        private GameObject _chargeEffectInstance;

        /// <summary>溜め音は途中で止める必要があるので、専用の口を持つ</summary>
        private AudioSource _chargeAudio;

        /// <summary>一発ものの音をまとめて鳴らす口</summary>
        private AudioSource _skillAudio;
        private bool _chargeSlowActive;
        private float _chargeRingTimerSec;
        private bool _chargeCompleteFlashed;
        private bool _nightMoodActive;
        private ThirdPersonCamera _cameraController;

        private LocalPlayerMover _mover;

        /// <summary>指を離したが、他の技の最中でまだ投げられない状態。空くと同時に投げる</summary>
        private bool _throwReserved;

        /// <summary>前のフレームの自分の位置。動いたぶんだけマーカーを運ぶのに使う</summary>
        private Vector3 _aimAnchorPosition;
        private DogAnimationDriver _animationDriver;
        private PlayerHealth _health;
        private PlayerAttack _playerAttack;
        private PlayerBeamSkill _beamSkill;

        /// <summary>死亡でスキルを中断するための購読</summary>
        private System.IDisposable _deathSubscription;
        private Transform _cameraTransform;

        // ---- 公開API -------------------------------------

        /// <summary>いま操作しているプレイヤーの元気玉スキル。UI から参照する</summary>
        public static PlayerEnergyBallSkill Local { get; private set; }

        /// <summary>狙い中・跳び上がり中・投擲中・着地待ち(この間は他の攻撃を出させない)。着弾後の爆発演出中は含まない</summary>
        public bool IsBusy =>
            _phase == Phase.Aiming || _phase == Phase.Rising || _phase == Phase.WindUp
            || _phase == Phase.Throwing || _descendActive;

        /// <summary>
        /// 投げ終わって降りているだけの間。
        /// もう玉は手を離れているので、ここから次の技へ繋げても演出は壊れない。
        /// </summary>
        public bool IsDescending => _descendActive;

        /// <summary>跳び上がってから着地するまでの間。この間は吹き飛ばされたくない</summary>
        public bool IsInThrowAction =>
            _phase == Phase.Rising || _phase == Phase.WindUp || _descendActive;

        /// <summary>クールタイムの残り具合(1=使った直後、0=使える)</summary>
        public float CooldownRatio01 =>
            _cooldownSec <= 0f ? 0f : Mathf.Clamp01(_cooldownRemainSec / _cooldownSec);

        /// <summary>次に使えるまでの残り秒数</summary>
        public float CooldownRemainSec => Mathf.Max(0f, _cooldownRemainSec);

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _mover = GetComponent<LocalPlayerMover>();
            _animationDriver = GetComponent<DogAnimationDriver>();
            _health = GetComponent<PlayerHealth>();
            _playerAttack = GetComponent<PlayerAttack>();
            _beamSkill = GetComponent<PlayerBeamSkill>();

            // 死亡は被弾RPCから全クライアントで発火するので、各自の画面で同時に中断できる
            if (_health != null) _deathSubscription = _health.Died.Subscribe(_ => InterruptOnDeath());
        }

        private void Start()
        {
            if (IsOwner) Local = this;
            if (Camera.main != null) _cameraTransform = Camera.main.transform;
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;

            _deathSubscription?.Dispose();
            _deathSubscription = null;

            // 演出の途中で消えても、空が夜のまま戻らなくならないようにする
            ReleaseNightMood();

            // 演出で時間をいじったまま破棄されても、遅いままにしない
            Battle.HitStop.ClearSlow(this);
        }

        private void OnDisable()
        {
            if (_phase == Phase.Aiming && IsOwner) CancelAiming();
        }

        private void Update()
        {
            if (IsOwner) UpdateOwnerInput();

            UpdateFovKick();

            // チャージと投擲の見た目は全クライアントで動かす
            if (_phase == Phase.Aiming) UpdateBallCharge();
            if (_phase == Phase.Rising || _phase == Phase.WindUp) UpdateLeapVisual();
            if (_phase == Phase.Rising && IsOwner) UpdateRising();
            if (_phase == Phase.WindUp && IsOwner) UpdateWindUp();
            if (_descendActive && IsOwner) UpdateDescending();
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

            // 狙い始めた時点で他の技が動いていると、ここを掛けられない。
            // 掛けられるようになったら止める。溜めながら走り回れるのは意図と違う
            if (_mover != null && CanThrowNow() && _mover.MoveLock != LocalPlayerMover.MovementLock.Full)
            {
                _mover.MoveLock = LocalPlayerMover.MovementLock.Full;
            }

            UpdateAimMarker();

            // 他の技が終わった瞬間に、待たせていた1発を出す
            if (_throwReserved)
            {
                if (CanThrowNow()) Throw();
                return;
            }

            if (held) return;

            // チャージ完了前に離したらキャンセル(クールタイムなし)
            if (_chargeElapsedSec < _chargeDurationSec)
            {
                CancelAiming();
                return;
            }

            // 投げられない間は予約だけして、狙いは出したままにする
            if (CanThrowNow()) Throw();
            else _throwReserved = true;
        }

        /// <summary>
        /// 狙いに入れるか。他の技の最中でも狙いだけは始められる。
        /// 動けない時間に何もできないと、技を繋ぐたびに手が止まって気持ちよくないため。
        /// </summary>
        private bool CanStartAiming()
        {
            if (_cooldownRemainSec > 0f) return false;
            if (!Battle.BattlePlayGate.IsPlayable) return false;
            if (_health != null && _health.IsDead) return false;
            if (_playerAttack != null && _playerAttack.IsAttacking) return false;
            return true;
        }

        /// <summary>
        /// いま投げてよいか。他の技が動いている間は投げずに待つ。
        /// 割り込むと位置や向きが取り合いになって、演出が破綻する。
        /// </summary>
        private bool CanThrowNow()
        {
            if (!Battle.BattlePlayGate.IsPlayable) return false;
            if (_health != null && _health.IsDead) return false;

            if (_beamSkill != null && _beamSkill.IsBusy) return false;

            PlayerDiveSkill diveSkill = GetComponent<PlayerDiveSkill>();
            if (diveSkill != null && (diveSkill.IsFlying || diveSkill.IsAiming)) return false;

            return true;
        }

        /// <summary>Qキー / ゲームパッドY / 画面の元気玉ボタンのいずれかが押されているか</summary>
        private bool ReadHoldInput()
        {
            bool held = false;

            if (_useQKey)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.qKey.isPressed) held = true;
            }

            if (_useGamepadTriggers)
            {
                Gamepad gamepad = Gamepad.current;

                // 両方のトリガーを引いている間だけ。片方では出ないようにして、
                // 必殺技が偶然出てしまうのを防ぐ
                if (gamepad != null && gamepad.leftTrigger.isPressed && gamepad.rightTrigger.isPressed) held = true;
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
            _aimAnchorPosition = transform.position;

            // 狙い中は移動しない。向きはマーカーの方へスキル側で向ける。
            // ただし他の技が動いている間は触らない。動きの主導権を奪うと演出が壊れる
            if (_mover != null && CanThrowNow()) _mover.MoveLock = LocalPlayerMover.MovementLock.Full;

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
            // とびこみの最中など、狙っている間に自分が動くことがある。
            // 動いたぶんだけマーカーも運ばないと、射程の縁に張り付いて動かせなくなる
            Vector3 moved = transform.position - _aimAnchorPosition;
            moved.y = 0.0f;
            _aimMarkerPosition += moved;
            _aimAnchorPosition = transform.position;

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

            // プレイヤーはマーカーの方を向く。
            // ただし他の技が動いている間は向きを変えない。
            // ビームの向きはキャラの向きから決まるので、回すと照射中の光線まで振れてしまう
            if (fromPlayer.sqrMagnitude > 0.01f && CanThrowNow())
            {
                Quaternion look = Quaternion.LookRotation(fromPlayer.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, TURN_SPEED_DEG * Time.deltaTime);
            }

            if (_aimIndicatorInstance != null)
            {
                _aimIndicatorInstance.transform.position = transform.position;
                _aimIndicatorInstance.SetMarkerPosition(_aimMarkerPosition);
                _aimIndicatorInstance.SetWillHit(HasTargetInBlastRange());
            }
        }

        private void CancelAiming()
        {
            _throwReserved = false;
            DestroyAimIndicator();
            if (_mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.None;
            photonView.RPC(nameof(RpcCancelCharge), RpcTarget.All);
        }

        /// <summary>
        /// 指を離した瞬間。クールタイムを始め、前転しながら跳び上がる。
        /// 頂点で振りかぶり、そこから投げる(跳び上がりの高さが0なら、その場で振りかぶって投げる)。
        /// </summary>
        private void Throw()
        {
            _throwReserved = false;
            DestroyAimIndicator();
            _cooldownRemainSec = Battle.DebugFlags.ApplyCooldown(_cooldownSec);

            _pendingTarget = _aimMarkerPosition;
            _groundY = transform.position.y;

            if (_riseHeight > 0f) StartRising();
            else StartWindUp();
        }

        /// <summary>その場で1回転しながら跳び上がる。頂点でちょうど元の向きに戻る</summary>
        private void StartRising()
        {
            _phase = Phase.Rising;
            _leapActive = true;
            _leapElapsedSec = 0f;
            _leapStartYawDeg = transform.eulerAngles.y;
            _risenHeight = 0f;

            // 重力で落ちないよう、この間の座標はこちらで動かす
            if (_mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.Frozen;

            photonView.RPC(nameof(RpcBeginLeap), RpcTarget.All, _leapStartYawDeg);
        }

        private void UpdateRising()
        {
            if (_health != null && _health.IsDead) { AbortThrow(); return; }

            _leapElapsedSec += Time.deltaTime;
            float t = Mathf.Clamp01(_leapElapsedSec / _riseDurationSec);

            // 勢いよく上がってから頂点で止まるように減速させる
            float eased = 1f - (1f - t) * (1f - t);
            float targetHeight = _riseHeight * eased;
            if (_controller != null) _controller.Move(Vector3.up * (targetHeight - _risenHeight));
            _risenHeight = targetHeight;

            // 上がりきったところで回転がちょうど1周ぶん終わるようにする。
            // 元の向きに指定軸まわりの回転を掛けるので、軸はキャラのローカル軸になる
            Quaternion baseRotation = Quaternion.Euler(0f, _leapStartYawDeg, 0f);
            Vector3 axis = _spinAxisLocal.sqrMagnitude > 0.0001f ? _spinAxisLocal.normalized : Vector3.right;
            transform.rotation = baseRotation * Quaternion.AngleAxis(360f * _spinTurns * t, axis);

            if (t < 1f) return;

            transform.rotation = Quaternion.Euler(0f, _leapStartYawDeg, 0f);
            StartWindUp();
        }

        /// <summary>頂点で玉を後ろへ引く“タメ”。ここで一拍おくことで、投げた瞬間の勢いが際立つ</summary>
        private void StartWindUp()
        {
            _phase = Phase.WindUp;
            _windUpElapsedSec = 0f;

            // 跳ばずに投げる設定のときは、ここが全クライアントへの合図になる
            if (!_leapActive)
            {
                _leapStartYawDeg = transform.eulerAngles.y;
                photonView.RPC(nameof(RpcBeginLeap), RpcTarget.All, _leapStartYawDeg);
            }
        }

        private void UpdateWindUp()
        {
            if (_health != null && _health.IsDead) { AbortThrow(); return; }

            _windUpElapsedSec += Time.deltaTime;
            if (_windUpElapsedSec < _windUpSec) return;

            photonView.RPC(nameof(RpcThrow), RpcTarget.All, _pendingTarget, _groundY);
        }

        /// <summary>投げ終わり。空中にいるなら地面まで降りてから操作を返す</summary>
        private void StartDescend()
        {
            if (!_leapActive) { EndLeap(); return; }

            _descendActive = true;
            _descendElapsedSec = 0f;
        }

        private void UpdateDescending()
        {
            _descendElapsedSec += Time.deltaTime;

            if (_controller != null)
            {
                // 投げた反動で少し後ろへ下がりながら降りる
                Vector3 back = Quaternion.Euler(0f, _leapStartYawDeg, 0f) * Vector3.back;
                _controller.Move(
                    Vector3.down * (_descendSpeed * Time.deltaTime) + back * (_throwRecoilSpeed * Time.deltaTime));
            }

            bool landed = _controller == null || _controller.isGrounded;
            if (landed || _descendElapsedSec >= MAX_DESCEND_SEC) EndLeap();
        }

        /// <summary>着地して操作を戻す</summary>
        private void EndLeap()
        {
            _descendActive = false;
            _leapActive = false;
            if (IsOwner && _mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.None;
        }

        /// <summary>投げきる前に死んだときなど、空中で止まったままにならないよう全員で後始末する</summary>
        private void AbortThrow()
        {
            photonView.RPC(nameof(RpcAbortThrow), RpcTarget.All);
        }

        /// <summary>投げの途中経過をすべて片付けて、待機状態に戻す</summary>
        private void CleanUpThrow()
        {
            _phase = Phase.Ready;
            if (IsOwner) SkillCutin.Cancel();
            DestroyBall();
            DestroyChargeEffect();
            EndChargePresentation();
            ReleaseNightMood();
            EndLeap();
        }

        /// <summary>
        /// 死亡した瞬間に必殺技を中断する。まだ玉を投げていない(溜め・跳び上がり・振りかぶり)なら
        /// 玉ごと取り消し、投げたあとの玉は手を離れているのでそのまま飛ばして着弾させる。
        /// どちらの場合も、空中で止まったままにならないよう跳び上がりと溜め演出は畳む。
        /// 死亡は全クライアントで同時に流れてくるので、追加の通信なしで全員の画面が揃う。
        /// </summary>
        private void InterruptOnDeath()
        {
            bool beforeRelease =
                _phase == Phase.Aiming || _phase == Phase.Rising || _phase == Phase.WindUp;

            if (beforeRelease)
            {
                DestroyAimIndicator();
                CleanUpThrow();
                Debug.Log("[PlayerEnergyBallSkill] 死亡したため必殺技を中断しました");
                return;
            }

            // 投げたあとの死亡。玉はそのまま飛ばし、本体の跳び上がりと演出だけ畳む
            if (!_leapActive && !_descendActive) return;

            EndChargePresentation();
            ReleaseNightMood();
            EndLeap();
            Debug.Log("[PlayerEnergyBallSkill] 死亡したため投げ動作を中断しました");
        }

        /// <summary>
        /// 着弾範囲に相手がいるか。爆発と同じ半径で調べるので、色が変わったら必ず巻き込める。
        /// 玉は山なりに飛ぶが、当たるかどうかを決めるのは着弾点の周りだけ。
        /// </summary>
        private bool HasTargetInBlastRange()
        {
            int count = Physics.OverlapSphereNonAlloc(
                _aimMarkerPosition, _explosionRadius, _overlapBuffer, _targetLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapBuffer[i];
                if (collider == null) continue;
                if (collider.transform == transform || collider.transform.IsChildOf(transform)) continue;

                HitTarget target = collider.GetComponentInParent<HitTarget>();
                if (target == null || !target.CanBeHit) continue;
                if (target.NetworkId == 0) continue;

                return true;
            }

            return false;
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

            _chargeRingTimerSec = 0f;
            _chargeCompleteFlashed = false;

            // 空を夜に落として光を際立たせる(暗転するのは発動した本人の画面だけ)
            RequestNightMood();

            // キャラ本体の発光は全員の画面で見せる(誰が溜めているかが分かる)
            if (_skillGlow != null) _skillGlow.SetGlow(true);

            // 時間を落とすのとカメラを寄せるのは、溜めている本人の画面だけ
            if (!IsOwner) return;

            SetChargeSlow(true);

            // 溜めは全クライアントで進むので、ここで鳴らせば全員に聞こえる
            StartChargeSound();
            ApplyChargeCamera(true);
        }

        /// <summary>チャージのキャンセル。全員のクライアントで玉を消す</summary>
        [PunRPC]
        private void RpcCancelCharge()
        {
            _phase = Phase.Ready;

            StopChargeSound();
            DestroyBall();
            DestroyChargeEffect();
            EndChargePresentation();
            ReleaseNightMood();
        }

        /// <summary>
        /// 跳び上がりの開始。全員のクライアントで玉を体から切り離し、頭上に浮かせたままにする。
        /// 体の位置と回転は座標同期で伝わるので、ここでは玉の扱いだけ揃える。
        /// </summary>
        [PunRPC]
        private void RpcBeginLeap(float startYawDeg)
        {
            // 跳ばない設定のときは跳び上がりを飛ばして、いきなり振りかぶりから始める
            _phase = _riseHeight > 0f ? Phase.Rising : Phase.WindUp;
            _leapStartYawDeg = startYawDeg;
            _leapVisualElapsedSec = 0f;

            // 体と一緒に玉まで回ってしまわないよう、玉は切り離して位置だけ追わせる
            if (_ballInstance != null)
            {
                _ballInstance.transform.SetParent(null);
                _ballInstance.transform.rotation = Quaternion.identity;
                _ballInstance.transform.localScale = Vector3.one * _ballMaxScale;
            }

            // 収束エフェクトは体に付いていて一緒に回ってしまうので、ここで畳む
            DestroyChargeEffect();

            // カットインは発動した本人の画面にだけ。跳び上がり〜振りかぶりのちょうど裏で流す
            if (_useCutin && IsOwner)
            {
                SkillCutin.Play(RiseVisualSec + _windUpSec + _cutinExtraSec);

                // 指を離した瞬間に発動するので、完成音の余韻がまだ残っている。
                // 重ねたままだと二つの音が混ざって両方とも弱くなるため、先に畳む
                StopSkillClips();

                // 絵と音は同時でないと切れ味が出ない。鳴らす相手も画面と揃えて本人だけにする
                PlaySkillClip(_cutinClip);
            }
        }

        /// <summary>投げきれなかったときの後始末を全員で行う</summary>
        [PunRPC]
        private void RpcAbortThrow()
        {
            CleanUpThrow();
        }

        /// <summary>投擲の開始。全員のクライアントで玉を飛ばし、投げるモーションと反動を再生する</summary>
        [PunRPC]
        private void RpcThrow(Vector3 target, float groundY)
        {
            StopChargeSound();
            PlaySkillClip(_throwClip);

            DestroyChargeEffect();
            EndChargePresentation();

            _groundY = groundY;

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

            // 飛び出した瞬間だけ玉を進行方向へ引き伸ばす
            _stretchRemainSec = _stretchRecoverSec;
            _ballSpinAngleDeg = 0f;

            // 投げる動作として頭突きモーションを流用する
            if (_animationDriver != null) _animationDriver.PlayAttack();

            PlayThrowImpact();

            // 空中から投げているので、ここから降りて着地する
            if (IsOwner) StartDescend();
        }

        /// <summary>ヒットの通知。全員のクライアントでエフェクトとダメージ処理を行う</summary>
        [PunRPC]
        private void RpcEnergyBallHit(
            Vector3 hitPoint, int targetNetworkId, int damage, bool combo, bool burst, PhotonMessageInfo info)
        {
            HitTarget target = HitTarget.Find(targetNetworkId);
            if (target == null) return;

            Vector3 position = target.GetEffectPosition(hitPoint);

            if (_damagePopupPrefab != null)
            {
                DamagePopup component = DamagePopup.Spawn(_damagePopupPrefab, hitPoint);
                if (component != null) component.Play(damage, combo);
            }

            int attackerActorNumber = info.Sender != null ? info.Sender.ActorNumber : -1;
            target.NotifyHit(position, attackerActorNumber, damage);

            // 爆発は長く、残り火は短く光らせる
            Battle.HitFlash.Play(
                target.transform,
                new Color(1.0f, 0.95f, 0.85f, 1.0f),
                burst ? 0.18f : 0.05f);

            // 着弾の爆発だけ大きく出す。残り火のダメージまで『ドカーン！』だと、
            // いつ爆発したのか分からなくなる
            if (burst) Battle.Onomatopoeia.Play(position, "ドカーン！", new Color(1.0f, 0.9f, 0.6f, 1.0f), 1.6f);
            else Battle.Onomatopoeia.Play(position, "ジュッ", new Color(1.0f, 0.75f, 0.45f, 1.0f), 0.5f);
        }

        // ---- チャージと投擲の進行(全クライアント) ---------

        /// <summary>チャージ中、玉を徐々に大きくする</summary>
        private void UpdateBallCharge()
        {
            _chargeElapsedSec += Time.deltaTime;

            SpawnChargeRing();
            CheckChargeComplete();

            if (_ballInstance == null) return;

            float t = Mathf.Clamp01(_chargeElapsedSec / _chargeDurationSec);
            // 最初に勢いよく育ち、完成に近づくほどゆっくりになる
            float eased = 1f - (1f - t) * (1f - t);
            _ballInstance.transform.localScale = Vector3.one * (_ballMaxScale * eased);
        }

        /// <summary>
        /// 投げた玉を飛ばす。等速で動かすと「置きに行った」動きになるので、
        /// 最初に一気に加速し、山なりの頂点を手前へ寄せて終わりぎわに落とす。
        /// </summary>
        private void UpdateThrowing()
        {
            _throwElapsedSec += Time.deltaTime;
            float t = Mathf.Clamp01(_throwElapsedSec / _throwTravelSec);

            // 立ち上がりを鋭くした進み具合。_launchSharpness が1なら等速になる
            float progress = 1f - Mathf.Pow(1f - t, _launchSharpness);

            // 山なりの頂点を手前へ寄せると、後半が急降下になって落ちてくる感じが出る
            float arc = Mathf.Sin(Mathf.Pow(progress, 1f / (1f + _fallBoost)) * Mathf.PI) * _arcHeight;

            Vector3 position = Vector3.Lerp(_throwStart, _throwTarget, progress) + Vector3.up * arc;

            if (_ballInstance != null)
            {
                Vector3 delta = position - _ballInstance.transform.position;
                _ballInstance.transform.position = position;
                UpdateBallFlightLook(delta);
            }

            if (t >= 1f) StartImpact();
        }

        /// <summary>
        /// 飛行中の玉の向き・伸び・脈動。根本を進行方向へ向けておくと、
        /// 子に掛けた伸縮がそのまま流線形になる(根本の大きさは軌跡の太さに響くので触らない)。
        /// </summary>
        private void UpdateBallFlightLook(Vector3 delta)
        {
            if (_ballInstance == null) return;

            if (delta.sqrMagnitude > 0.000001f)
            {
                _ballSpinAngleDeg += _ballSpinSpeed * Time.deltaTime;
                _ballInstance.transform.rotation =
                    Quaternion.LookRotation(delta.normalized) * Quaternion.Euler(0f, 0f, _ballSpinAngleDeg);
            }

            if (_stretchRemainSec > 0f) _stretchRemainSec -= Time.deltaTime;

            float stretch01 = _stretchRecoverSec <= 0f ? 0f : Mathf.Clamp01(_stretchRemainSec / _stretchRecoverSec);
            float stretch = _launchStretch * stretch01;
            float pulse = 1f + Mathf.Sin(_throwElapsedSec * _ballPulseHz * Mathf.PI * 2f) * _ballPulseAmount;

            // 進行方向(ローカルZ)へ伸ばし、そのぶん横をすぼめる
            ApplyBallVisualScale(
                new Vector3(1f - stretch * 0.35f, 1f - stretch * 0.35f, 1f + stretch) * pulse);
        }

        /// <summary>玉の見た目の子に倍率を掛ける</summary>
        private void ApplyBallVisualScale(Vector3 factor)
        {
            for (int i = 0; i < _ballVisuals.Count; i++)
            {
                Transform visual = _ballVisuals[i];
                if (visual == null) continue;

                Vector3 baseScale = _ballVisualBaseScales[i];
                visual.localScale = new Vector3(
                    baseScale.x * factor.x, baseScale.y * factor.y, baseScale.z * factor.z);
            }
        }

        /// <summary>
        /// 跳び上がり中と振りかぶり中、玉を頭上に浮かせておく。
        /// 全クライアントが同じ時間で同じ動きをするので、通信は跳び上がりの合図だけで足りる。
        /// </summary>
        private void UpdateLeapVisual()
        {
            _leapVisualElapsedSec += Time.deltaTime;

            // 合図だけ届いて投擲が届かなかったときに、固まったままにならないための保険
            if (_leapVisualElapsedSec > RiseVisualSec + _windUpSec + LEAP_VISUAL_TIMEOUT_SEC)
            {
                CleanUpThrow();
                return;
            }

            if (_ballInstance == null) return;

            float windUp01 = _windUpSec <= 0f
                ? 1f
                : Mathf.Clamp01((_leapVisualElapsedSec - RiseVisualSec) / _windUpSec);

            // 引き始めは速く、引ききる手前でためる
            float pull = 1f - (1f - windUp01) * (1f - windUp01);
            Vector3 forward = Quaternion.Euler(0f, _leapStartYawDeg, 0f) * Vector3.forward;

            _ballInstance.transform.position = transform.position + Vector3.up * _ballHeight
                - forward * (_windUpBackDistance * pull)
                + Vector3.up * (_windUpUpDistance * pull);

            _ballInstance.transform.localScale =
                Vector3.one * (_ballMaxScale * Mathf.Lerp(1f, _windUpBallScale, pull));
        }

        /// <summary>跳び上がりに使う時間。跳ばない設定なら0</summary>
        private float RiseVisualSec => _riseHeight > 0f ? _riseDurationSec : 0f;

        /// <summary>投げた瞬間の手ごたえ。足元の余波・着弾予告・カメラの反応をまとめて出す</summary>
        private void PlayThrowImpact()
        {
            // 足元の余波は全員に見せる。飛び出した勢いで草がなぎ倒される
            if (_useLaunchShockwave && _shockwavePrefab != null)
            {
                Vector3 feet = new Vector3(transform.position.x, _groundY + 0.1f, transform.position.z);
                EnergyShockwave.Spawn(
                    _shockwavePrefab, feet, 0.5f, _launchShockwaveRadius, 0.45f, 0.6f, _flattenGrass);
            }

            // 着弾点に縮んでいくリングを出して、どこへ落ちてくるかを見せる
            if (_useImpactWarning && _shockwavePrefab != null)
            {
                Vector3 warning = new Vector3(_throwTarget.x, _groundY + 0.05f, _throwTarget.z);
                EnergyShockwave.Spawn(
                    _shockwavePrefab, warning, _explosionRadius * 2.2f, _explosionRadius * 0.4f,
                    _throwTravelSec + _impactLingerSec, 0.35f, false);
            }

            // ここから先は投げた本人の画面だけ(他人の投擲で画面が揺れると見づらい)
            if (!IsOwner) return;

            if (_throwShakeAmplitude > 0f && _throwShakeDurationSec > 0f)
            {
                ThirdPersonCamera playerCamera = ResolveCamera();
                if (playerCamera != null) playerCamera.Shake(_throwShakeAmplitude, _throwShakeDurationSec);
            }

            if (!Mathf.Approximately(_throwFovKick, 0f) && _throwFovKickSec > 0f)
            {
                _fovKickRemainSec = _throwFovKickSec;
                ApplyFovKick();
            }

            Battle.HitStop.Play(_throwHitStopSec, _hitStopTimeScale, _hitStopRecoverSec);
        }

        /// <summary>広げた視野角をなめらかに戻す。時間が止まっている間も進むよう実時間で数える</summary>
        private void UpdateFovKick()
        {
            if (_fovKickRemainSec <= 0f) return;

            _fovKickRemainSec -= Time.unscaledDeltaTime;
            ApplyFovKick();
        }

        private void ApplyFovKick()
        {
            ThirdPersonCamera playerCamera = ResolveCamera();
            if (playerCamera == null) return;

            float t = _throwFovKickSec <= 0f ? 0f : Mathf.Clamp01(_fovKickRemainSec / _throwFovKickSec);
            playerCamera.SetFovOffset(_throwFovKick * t);
        }

        /// <summary>着弾。玉を着地したその場に残し、時間が来たら爆発させる</summary>
        private void StartImpact()
        {
            _phase = Phase.Impact;
            _impactElapsedSec = 0f;
            _impactStartScale = _ballInstance != null ? _ballInstance.transform.localScale.x : _ballMaxScale;

            // 伸びと脈動を元に戻してから破裂の演出に入る
            ApplyBallVisualScale(Vector3.one);
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

            PlaySkillClip(_explosionClip);
            PlayExplosionImpact();

            // 木はシーンに置かれていて全クライアントに同じものがあり、この処理も全員で走る。
            // そのため追加の通信なしで全員の画面の同じ木が倒れる(デカールや草と同じ方式)
            Field.BreakableTree.BreakInSphere(_throwTarget, _treeBreakRadius);
            Field.BreakableProp.BreakInSphere(_throwTarget, _treeBreakRadius);

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
                SpawnCrackDecals();
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

                // 爆発の光が消えきってから昼へ戻す(戻りはゆっくりで余韻を残す)
                ReleaseNightMood();
            }
        }

        /// <summary>玉のレンダラーを覚えておく(爆発時のフェードに使う)</summary>
        private void CacheBallRenderers()
        {
            if (_ballPropertyBlock == null) _ballPropertyBlock = new MaterialPropertyBlock();

            _ballRenderers.Clear();
            _ballBaseColors.Clear();
            _ballVisuals.Clear();
            _ballVisualBaseScales.Clear();
            if (_ballInstance == null) return;

            _ballLight = _ballInstance.GetComponentInChildren<Light>(true);
            _ballLightBaseIntensity = _ballLight != null ? _ballLight.intensity : 0f;

            // 伸縮を掛ける対象。粒(パーティクル)は伸ばすと不自然なので外す
            foreach (Transform child in _ballInstance.transform)
            {
                if (child.GetComponent<ParticleSystem>() != null) continue;

                _ballVisuals.Add(child);
                _ballVisualBaseScales.Add(child.localScale);
            }

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
            if (_ballLight != null) _ballLight.intensity = _ballLightBaseIntensity * alpha01;

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
                // 速く走る薄いリング。これが通ったところの草をなぎ倒す。
                // 1回だと草が一度倒れて終わりなので、弱まりながら何度も走らせて余韻を作る
                EnergyShockwave.Spawn(
                    _shockwavePrefab, _throwTarget + Vector3.up * 0.1f,
                    _ballMaxScale * 0.5f, _zoneRadius * 2.2f, 0.45f, 0.5f, _flattenGrass,
                    _shockwaveCount, _shockwaveIntervalSec);

                // 遅れて広がる太いリング。2枚重なると規模が大きく見える
                if (_useDoubleShockwave)
                {
                    EnergyShockwave.Spawn(
                        _shockwavePrefab, _throwTarget + Vector3.up * 0.05f,
                        _ballMaxScale * 0.3f, _zoneRadius * 3.2f, 0.9f, 2.0f, _flattenGrass,
                        Mathf.Max(1, _shockwaveCount - 1), _shockwaveIntervalSec * 1.6f);
                }
            }

            if (_cameraShakeAmplitude > 0f && _cameraShakeDurationSec > 0f)
            {
                ThirdPersonCamera playerCamera = ResolveCamera();
                if (playerCamera != null) playerCamera.Shake(_cameraShakeAmplitude, _cameraShakeDurationSec);
            }

            Battle.HitStop.Play(_hitStopDurationSec, _hitStopTimeScale, _hitStopRecoverSec);

            // 周りの音を引かせて、爆発だけを前に出す。
            // 音量を上げるより、周りが引いたほうが大きく聞こえる
            UI.BgmPlayer.Duck(0.55f, 0.18f, 0.5f);

            // 一番強い技なので、決めゴマも輪も他より大きく長くする。技の格の差を絵で見せる
            UI.ImpactFrame.PlayWhite(0.06f, 0.85f);
            Battle.ShockwaveRing.Play(transform.position, new Color(0.75f, 0.9f, 1.0f, 1.0f), 12.0f, 0.55f, 1.2f);
        }

        // ---- 発動時の演出 ----------------------------------

        /// <summary>足元から玉へ力が集まる感じを出すため、外から内へ縮むリングを繰り返し出す</summary>
        private void SpawnChargeRing()
        {
            if (_shockwavePrefab == null || _chargeRingIntervalSec <= 0f) return;

            _chargeRingTimerSec -= Time.deltaTime;
            if (_chargeRingTimerSec > 0f) return;

            _chargeRingTimerSec = _chargeRingIntervalSec;
            EnergyShockwave.Spawn(
                _shockwavePrefab, transform.position + Vector3.up * 0.1f,
                _maxRange * 0.45f, 0.3f, _chargeRingDurationSec, 0.35f, _flattenGrass);
        }

        /// <summary>溜めが完成した瞬間に画面を光らせ、投げられる状態になったことを伝える</summary>
        private void CheckChargeComplete()
        {
            if (_chargeCompleteFlashed) return;
            if (_chargeElapsedSec < _chargeDurationSec) return;

            _chargeCompleteFlashed = true;

            // 唸りを断ち切って澄んだ一撃を鳴らす。切り替わりが「完成した」の合図になる
            StopChargeSound();
            PlaySkillClip(_chargeCompleteClip);

            // 他人の溜め完了で画面が光ると見づらいので、自分のときだけ光らせる
            if (IsOwner && _chargeCompleteFlashSec > 0f)
            {
                ScreenFlash.Play(_chargeCompleteFlashColor, _chargeCompleteFlashSec);
            }
        }

        /// <summary>溜めの演出(発光・スロー・カメラの寄り)を元に戻す</summary>
        private void EndChargePresentation()
        {
            if (_skillGlow != null) _skillGlow.SetGlow(false);

            if (!IsOwner) return;

            SetChargeSlow(false);
            ApplyChargeCamera(false);
        }

        /// <summary>空を夜に落とすよう申請する(発動した本人の画面のみ)。二重に申請しないよう自分の申請状態を持つ</summary>
        private void RequestNightMood()
        {
            if (!_useNightAtmosphere || _nightMoodActive) return;

            // 他人の必殺技で自分の画面まで暗くなると見づらいので、暗転するのは発動した本人の画面だけ
            if (!IsOwner) return;

            _nightMoodActive = true;
            Field.SkyAtmosphere.RequestNight();
        }

        /// <summary>夜の申請を取り下げる。申請していなければ何もしない</summary>
        private void ReleaseNightMood()
        {
            if (!_nightMoodActive) return;

            _nightMoodActive = false;
            Field.SkyAtmosphere.ReleaseNight();
        }

        private void ApplyChargeCamera(bool enabled)
        {
            ThirdPersonCamera playerCamera = ResolveCamera();
            if (playerCamera == null) return;

            playerCamera.SetDistanceOffset(enabled ? _chargeCameraDistanceOffset : 0f);
            playerCamera.SetFovOffset(enabled ? _chargeCameraFovOffset : 0f);
        }

        private ThirdPersonCamera ResolveCamera()
        {
            if (_cameraController == null) _cameraController = FindAnyObjectByType<ThirdPersonCamera>();
            return _cameraController;
        }

        // ---- 時間の速さ ------------------------------------

        /// <summary>溜め中のスロー。掛けっぱなしにならないよう、必ず対で取り下げる</summary>
        /// <summary>
        /// 音の口を必要になった時点で用意する。
        /// 距離で小さくならない2Dで鳴らす。狭い戦場なので、誰の必殺技も同じ迫力で聞かせたい。
        /// </summary>
        private AudioSource EnsureAudioSource(ref AudioSource source, bool loop)
        {
            if (source != null) return source;

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;

            return source;
        }

        private void StartChargeSound()
        {
            if (_chargeClip == null) return;

            EnsureAudioSource(ref _chargeAudio, false);
            _chargeAudio.clip = _chargeClip;
            _chargeAudio.volume = _skillVolume;

            // 溜めの長さを変えても最後まで鳴りきるよう、音の速さを合わせる
            _chargeAudio.pitch = _chargeDurationSec > 0.05f
                ? Mathf.Clamp(_chargeClip.length / _chargeDurationSec, 0.5f, 2f)
                : 1f;

            _chargeAudio.Play();
        }

        private void StopChargeSound()
        {
            if (_chargeAudio == null || !_chargeAudio.isPlaying) return;

            _chargeAudio.Stop();
        }

        /// <summary>
        /// 鳴っている一発ものを止める。次の音を立てたいときだけ使う。
        /// 溜め音は別の口なので、これでは止まらない。
        /// </summary>
        private void StopSkillClips()
        {
            if (_skillAudio == null || !_skillAudio.isPlaying) return;

            _skillAudio.Stop();
        }

        private void PlaySkillClip(AudioClip clip)
        {
            if (clip == null) return;

            EnsureAudioSource(ref _skillAudio, false);

            // 溜め・完成・投げ・爆発が重なっても切れないよう、重ねて鳴らす
            _skillAudio.PlayOneShot(clip, _skillVolume);
        }

        private void SetChargeSlow(bool enabled)
        {
            if (_chargeSlowActive == enabled) return;

            _chargeSlowActive = enabled;

            if (enabled) Battle.HitStop.SetSlow(this, _chargeTimeScale);
            else Battle.HitStop.ClearSlow(this);
        }


        // ---- 発動後の演出 ----------------------------------

        /// <summary>
        /// 爆心の周りにひび割れを散らす。着弾点から作った擬似乱数で位置を決めるので、
        /// 追加の通信なしで全クライアントの同じ場所に出る。
        /// </summary>
        private void SpawnCrackDecals()
        {
            if (_crackDecalCount <= 0) return;

            float size = _explosionRadius * 2f * _crackDecalScale;
            for (int i = 0; i < _crackDecalCount; i++)
            {
                float angle = Hash01(i * 2) * Mathf.PI * 2f;
                float distance = Mathf.Lerp(_explosionRadius * 0.5f, _zoneRadius * 0.9f, Hash01(i * 2 + 1));

                Vector3 point = _throwTarget + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                AttackDecal.Spawn(_impactDecalPrefab, point, size);
            }
        }

        /// <summary>着弾点と番号から 0〜1 の擬似乱数を作る(全クライアントで同じ値になる)</summary>
        private float Hash01(int index)
        {
            float seed = _throwTarget.x * 12.9898f + _throwTarget.z * 78.233f + index * 37.719f;
            float value = Mathf.Sin(seed) * 43758.5453f;
            return value - Mathf.Floor(value);
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

                SendHit(target, collider, center, damage, true);
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
                    SendHit(state.Target, state.Collider, _zonePosition, _zoneTickDamage, false);
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

        /// <param name="burst">着弾の爆発かどうか。残留地帯の継続ダメージなら false</param>
        private void SendHit(HitTarget target, Collider collider, Vector3 center, int damage, bool burst)
        {
            // 他のプレイヤーが直前に当てていれば、同時ヒットボーナスを掛けてから配る
            bool combo = Battle.ComboBonus.IsActive;
            damage = Battle.ComboBonus.Apply(damage);

            if (damage <= 0) return;

            Vector3 hitPoint = collider != null ? collider.ClosestPoint(center) : target.transform.position;
            photonView.RPC(nameof(RpcEnergyBallHit), RpcTarget.All, hitPoint, target.NetworkId, damage, combo, burst);
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
