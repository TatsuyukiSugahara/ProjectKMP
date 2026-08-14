using System.Collections.Generic;
using Photon.Pun;
using ProjectKMP.Attack;
using ProjectKMP.Dog;
using ProjectKMP.Gorilla;
using ProjectKMP.UI;
using R3;
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
        private enum Phase { Ready, Aiming, Rising, Firing, Descending }

        // ---- 定数 ----------------------------------------

        private const int OVERLAP_BUFFER_SIZE = 32;

        /// <summary>着地できないまま降下し続けるのを防ぐ保険の時間(秒)</summary>
        private const float MAX_DESCEND_SEC = 3.0f;

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

        [Header("とびこみからの強化")]
        [SerializeField, Tooltip("とびこみで跳び上がっている最中に撃つと強くなる")]
        private bool _enableDiveBoost = true;

        [SerializeField, Min(1.0f), Tooltip("強化時のダメージ倍率")]
        private float _boostDamageScale = 3.0f;

        [SerializeField, Min(1.0f), Tooltip("強化時の太さの倍率")]
        private float _boostWidthScale = 2.0f;

        [SerializeField, Min(1.0f), Tooltip("強化時の射程の倍率")]
        private float _boostLengthScale = 1.5f;

        [SerializeField, Tooltip("発射位置の高さ(足元からのオフセット・m)")]
        private float _originHeight = 1.3f;

        [SerializeField, Tooltip("発射位置の前方オフセット(m)。体にビームが重ならないようにする")]
        private float _originForwardOffset = 0.8f;

        [Header("友達ビーム")]
        [SerializeField, Tooltip("何人かで合わせて撃つと強くなる")]
        private bool _enableFriendBeam = true;

        [SerializeField, Tooltip("2人・3人・4人で合わせたときのダメージ倍率")]
        private float[] _friendDamageScales = { 2.5f, 4.0f, 6.0f };

        [SerializeField, Tooltip("2人・3人・4人で合わせたときの太さの倍率")]
        private float[] _friendWidthScales = { 2.5f, 3.0f, 3.5f };

        [SerializeField, Tooltip("2人・3人・4人で合わせたときの射程の倍率")]
        private float[] _friendLengthScales = { 1.5f, 1.8f, 2.0f };

        [SerializeField, Tooltip("合体したビームの色。通常の青と見分けがつく色にする")]
        private Color _friendColor = new Color(1.0f, 0.85f, 0.35f, 1.0f);

        [SerializeField, Min(0.0f), Tooltip("合体時のカットインを出す時間(秒)。0で出さない")]
        private float _friendCutinSec = 0.9f;

        [SerializeField, Min(0.0f), Tooltip("合わせた相手との間に渡す光の橋を出す時間(秒)。0で出さない")]
        private float _friendBridgeSec = 0.8f;

        [SerializeField, Range(0.5f, 1.0f), Tooltip("合体時に残るクールタイムの割合。合わせたご褒美として短くする")]
        private float _friendCooldownScale = 0.5f;

        [Header("口元から出す")]
        [SerializeField, Tooltip("ビームの発射口にする Transform(犬の head ボーンなど)。未設定なら上の発射位置設定を使う")]
        private Transform _muzzleTransform;

        [SerializeField, Tooltip("発射口の微調整(m)。キャラの右/上/前を軸にしたオフセット")]
        private Vector3 _muzzleLocalOffset = new Vector3(0f, 0.02f, 0.35f);

        [SerializeField, Tooltip("発射の瞬間に口元で再生する噛みつきエフェクト。未設定なら出さない")]
        private BiteVfx _biteVfxPrefab;

        [SerializeField, Min(0.01f), Tooltip("噛みつきエフェクトの大きさ倍率")]
        private float _biteVfxScale = 1f;

        [Header("発射")]
        [SerializeField, Min(0.1f), Tooltip("ビームを照射し続ける時間(秒)。この間は移動できない")]
        private float _fireDurationSec = 2f;

        [SerializeField, Min(0f), Tooltip("ビームが根元から先端まで伸びきるまでの時間(秒)")]
        private float _growDurationSec = 0.2f;

        [SerializeField, Min(0.01f), Tooltip("照射終了後、ビームが消えるまでのフェード時間(秒)")]
        private float _fadeOutDurationSec = 0.5f;

        [SerializeField, Min(0f), Tooltip("発射の瞬間にカメラを揺らす大きさ。0で揺らさない")]
        private float _fireCameraShakeAmplitude = 0.18f;

        [SerializeField, Min(0f), Tooltip("発射の瞬間のカメラの揺れの長さ(秒)")]
        private float _fireCameraShakeDurationSec = 0.25f;

        [SerializeField, Min(0.0f), Tooltip("当たり始めに時間を止める長さ(秒)。0で止めない")]
        private float _initialHitStopSec = 0.06f;

        [SerializeField, Min(0.0f), Tooltip("照射中の継続ヒットで止める長さ(秒)。連続で当たるので短くする")]
        private float _tickHitStopSec = 0.02f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("止めている間の時間の速さ")]
        private float _hitStopTimeScale = 0.08f;

        [SerializeField, Min(0.0f), Tooltip("止めたあと通常の速さへ戻すのにかける秒数")]
        private float _hitStopRecoverSec = 0.08f;

        [Header("着弾点の押し広げ")]
        [SerializeField, Tooltip("着弾点から広がる輪。未設定なら出さない")]
        private EnergyShockwave _impactRingPrefab;

        [SerializeField, Min(0.0f), Tooltip("輪を出す間隔(秒)。0で出さない")]
        private float _impactRingIntervalSec = 0.3f;

        [SerializeField, Min(0.05f), Tooltip("輪の広がり始めの半径(メートル)")]
        private float _impactRingStartRadius = 0.3f;

        [SerializeField, Min(0.1f), Tooltip("輪が広がりきる半径(メートル)")]
        private float _impactRingEndRadius = 2.2f;

        [SerializeField, Min(0.05f), Tooltip("輪が広がりきるまでの時間(秒)")]
        private float _impactRingDurationSec = 0.45f;

        [SerializeField, Min(0.0f), Tooltip("輪の線の太さ(メートル)。0でプレハブの値のまま")]
        private float _impactRingThickness = 0.45f;

        [Header("足元の踏ん張り")]
        [SerializeField, Tooltip("撃っている本人の足元から広がる輪。未設定なら出さない")]
        private EnergyShockwave _muzzleRingPrefab;

        [SerializeField, Min(0.0f), Tooltip("輪を出す間隔(秒)。0で出さない。着弾点とずらすと重なって見えにくい")]
        private float _muzzleRingIntervalSec = 0.35f;

        [SerializeField, Min(0.05f), Tooltip("輪の広がり始めの半径(メートル)")]
        private float _muzzleRingStartRadius = 0.35f;

        [SerializeField, Min(0.1f), Tooltip("輪が広がりきる半径(メートル)")]
        private float _muzzleRingEndRadius = 1.7f;

        [SerializeField, Min(0.05f), Tooltip("輪が広がりきるまでの時間(秒)")]
        private float _muzzleRingDurationSec = 0.4f;

        [SerializeField, Min(0.0f), Tooltip("輪の線の太さ(メートル)。0でプレハブの値のまま")]
        private float _muzzleRingThickness = 0.3f;

        [Header("地面の痕")]
        [SerializeField, Tooltip("ビームが地面に残す痕(デカール)。未設定なら痕を残さない")]
        private AttackDecal _beamDecalPrefab;

        [SerializeField, Min(0.1f), Tooltip("痕を置く間隔(m)。ビームが伸びてこの距離を越えるたびに置く")]
        private float _decalIntervalMeters = 2f;

        [SerializeField, Min(0.1f), Tooltip("痕の直径をビームの太さ(直径)の何倍にするか")]
        private float _decalWidthScale = 1.2f;

        [SerializeField, Min(0f), Tooltip("ビームが草をなぎ倒す半径を、ビームの太さ(半径)の何倍にするか。0でなぎ倒さない")]
        private float _grassFlattenScale = 1.3f;

        [SerializeField, Min(0f), Tooltip("なぎ倒しの波をビームに沿って流す間隔(秒)。0で波を出さない")]
        private float _grassWaveIntervalSec = 0.25f;

        [SerializeField, Min(0.1f), Tooltip("なぎ倒しの波がビームに沿って進む速さ(m/秒)")]
        private float _grassWaveSpeed = 20f;

        [SerializeField, Tooltip("照射が当たった木を倒す")]
        private bool _breakTrees = true;

        [Header("跳び上がり")]
        [SerializeField, Min(0f), Tooltip("発射前に跳び上がる高さ(m)。0なら跳ばずにその場で撃つ")]
        private float _riseHeight = 2.5f;

        [SerializeField, Min(0.05f), Tooltip("跳び上がりにかける時間(秒)")]
        private float _riseDurationSec = 0.45f;

        [SerializeField, Min(0), Tooltip("跳び上がりながら何回転するか")]
        private int _spinTurns = 1;

        [SerializeField, Tooltip("回転軸(キャラのローカル軸)。(1,0,0)で前転、(-1,0,0)で後転、(0,1,0)でその場スピン")]
        private Vector3 _spinAxisLocal = Vector3.right;

        [SerializeField, Min(0.1f), Tooltip("照射後に降りてくる速さ(m/秒)")]
        private float _descendSpeed = 8f;

        [SerializeField, Tooltip("空中から撃つとき、狙いの表示と同じ地面の位置へ着弾するようビームを下向きに傾ける。切ると水平に撃つ")]
        private bool _aimAtGroundEnd = true;

        [Header("アニメーション")]
        [SerializeField, Range(0f, 1f), Tooltip("頭突きモーションのどの位置(0〜1)で止めて照射ポーズにするか")]
        private float _poseFreezeNormalizedTime = 0.5f;

        [Header("音")]
        [SerializeField, Tooltip("跳び上がって溜めている間の音")]
        private AudioClip _windupClip;

        [SerializeField, Tooltip("ビームを撃った瞬間の音")]
        private AudioClip _fireClip;

        [SerializeField, Tooltip("照射している間ずっと鳴らす音(ループ)")]
        private AudioClip _loopClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("発射など一発ものの音量")]
        private float _fireVolume = 0.85f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("照射中の音量。鳴り続けるので一発ものより控えめにする")]
        private float _loopVolume = 0.55f;

        [SerializeField, Range(0.5f, 1.0f), Tooltip("強化ビームのときの音の低さ。低いほど太く聞こえる")]
        private float _boostPitch = 0.82f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("他人のビームの立体感。1に近いほど距離で小さくなる")]
        private float _othersSpatialBlend = 0.85f;

        [SerializeField, Tooltip("他人のビームが聞こえなくなる距離(m)")]
        private float _othersMaxDistance = 35f;

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

        [SerializeField, Tooltip("ゲームパッドのRB(右肩ボタン)の長押しで狙う")]
        private bool _useGamepadShoulder = true;

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

        /// <summary>痕を落とす地面の高さ。口元は動くので、発射時の足元の高さを覚えておく</summary>
        private float _beamGroundY;

        private float _lengthScale = 1f;
        private float _widthScale = 1f;
        private float _damageScale = 1f;

        /// <summary>いま撃っているビームの長さ。強化中は伸びる</summary>
        private float CurrentBeamLength => _beamLength * _lengthScale;

        /// <summary>いま撃っているビームの太さ。強化中は太くなる</summary>
        private float CurrentBeamWidth => _beamWidth * _widthScale;

        /// <summary>地面を探すときの当たり一覧。自分やボスを飛ばすので複数受け取る</summary>
        private readonly RaycastHit[] _groundBuffer = new RaycastHit[8];

        /// <summary>いま流れている、なぎ倒しの波それぞれの根元からの距離</summary>
        private readonly List<float> _grassWaveDistances = new List<float>();
        private float _grassWaveTimerSec;

        private CharacterController _controller;
        private bool _leapActive;
        private bool _poseHeld;
        private float _leapElapsedSec;
        private float _leapStartYawDeg;
        private float _risenHeight;
        private float _descendElapsedSec;

        private GameObject _beamEffectInstance;

        /// <summary>照射中のループ音。途中で止める必要があるので専用の口を持つ</summary>
        private AudioSource _loopAudio;

        /// <summary>溜め・発射など一発ものをまとめて鳴らす口</summary>
        private AudioSource _oneShotAudio;

        private float _loopFadeRemainSec;
        private float _loopFadeDurationSec;
        private float _loopFadeStartVolume;
        private DestructionBeamVisual _beamVisual;

        /// <summary>いま合体している人数。合体していなければ0</summary>
        private int _friendMembers;

        /// <summary>指を離したが、他の技の最中でまだ撃てない状態。空くと同時に撃つ</summary>
        private bool _fireReserved;

        /// <summary>このキャラを操作している人の名前。取れなければ既定の呼び名を返す</summary>
        public string OwnerName
        {
            get
            {
                string nickName = photonView != null && photonView.Owner != null ? photonView.Owner.NickName : null;

                return string.IsNullOrWhiteSpace(nickName) ? "プレイヤー" : nickName;
            }
        }
        private BeamAimIndicator _aimIndicatorInstance;

        private LocalPlayerMover _mover;
        private DogAnimationDriver _animationDriver;
        private PlayerHealth _health;
        private PlayerAttack _playerAttack;
        private PlayerDiveSkill _diveSkill;

        /// <summary>死亡でスキルを中断するための購読</summary>
        private System.IDisposable _deathSubscription;

        // ---- 公開API -------------------------------------

        /// <summary>いま操作しているプレイヤーのビームスキル。UI から参照する</summary>
        public static PlayerBeamSkill Local { get; private set; }

        /// <summary>狙い中(長押し中)かどうか</summary>
        public bool IsAiming => _phase == Phase.Aiming;


        /// <summary>ビーム照射中かどうか</summary>
        public bool IsFiring => _phase == Phase.Firing;

        /// <summary>狙い中・跳び上がり中・照射中・降下中(この間は通常攻撃を出させない)</summary>
        public bool IsBusy => _phase != Phase.Ready;

        /// <summary>
        /// 照射を終えて降りている最中か。ここまで来れば撃ち終わっているので、
        /// 次の技へ繋いでも演出は破綻しない。
        /// </summary>
        public bool IsFinishing => _phase == Phase.Descending;

        /// <summary>
        /// 降下を切り上げて待機に戻す。次の技へ繋ぐときに使う。
        /// 両方が同時に体を動かすと暴れるので、譲る側をここで畳む。
        /// </summary>
        public void EndLeapNow()
        {
            if (_phase != Phase.Descending) return;

            EndLeap();
        }

        /// <summary>跳び上がってから着地するまでの間。この間は吹き飛ばされたくない</summary>
        public bool IsInBeamAction =>
            _phase == Phase.Rising || _phase == Phase.Firing || _phase == Phase.Descending;

        /// <summary>クールタイムの残り具合(1=撃った直後、0=撃てる)</summary>
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
            _diveSkill = GetComponent<PlayerDiveSkill>();

            // 死亡は被弾RPCから全クライアントで発火するので、各自の画面で同時に中断できる
            if (_health != null) _deathSubscription = _health.Died.Subscribe(_ => InterruptOnDeath());
        }

        private void Start()
        {
            if (!IsOwner) return;

            Local = this;

            // 合図は自分の画面に出すものなので、操作している本人の側で用意する
            if (_enableFriendBeam) FriendBeamSignal.Ensure();

            // ピンチの合図も自分の画面だけのもの。ここでまとめて用意する
            DangerVignette.Ensure();
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;

            _deathSubscription?.Dispose();
            _deathSubscription = null;
        }

        private void OnDisable()
        {
            // 無効化されたら狙いを解除し、移動ロックを残さない
            if (_phase == Phase.Aiming) CancelAiming();

            // 鳴りっぱなしで残らないよう、ここでも確実に止める
            StopLoopSound();

            // 合図を出したまま消えると、他の人がずっと待つことになる
            FriendBeam.EndAim(this);
        }

        private void Update()
        {
            // 照射が終わったあともループ音を絞り切るまで進める必要がある
            UpdateLoopFade();

            if (IsOwner) UpdateOwnerInput();

            // 跳び上がりと降下は座標を動かす処理なので、所有者だけが行う
            // (他のクライアントへは PhotonTransformView の位置同期で伝わる)
            if (_phase == Phase.Rising)
            {
                if (IsOwner) UpdateRising();
            }
            else if (_phase == Phase.Firing)
            {
                // 照射の進行は全クライアントで動かす(見た目とアニメを揃えるため)
                UpdateFiring();
            }
            else if (_phase == Phase.Descending)
            {
                if (IsOwner) UpdateDescending();
            }
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
                        break;
                    }

                    // とびこみの飛行中は移動の部品ごと止まっていて、向きも変えられない。
                    // 狙っている間だけ、こちらから回転だけを回してやる
                    KeepAimRotation();

                    // 他の技が終わった瞬間に、待たせていた1発を出す
                    if (_fireReserved)
                    {
                        UpdateAimHitState();
                        if (CanFireNow()) Fire();
                        break;
                    }

                    if (!held)
                    {
                        // 撃てない間は予約だけして、狙いは出したままにする
                        if (CanFireNow()) Fire();
                        else _fireReserved = true;
                        break;
                    }

                    // 他の技が動いている間は、そちらに動きを任せる。
                    // ここで移動を止めると、とびこみの飛行と取り合いになる
                    if (_mover != null && CanFireNow()) _mover.MoveLock = LocalPlayerMover.MovementLock.RotateOnly;

                    UpdateAimHitState();
                    break;
            }
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
        /// 移動の部品が止められている間、向きだけは入力どおりに回す。
        /// 止まっていなければ何もしない(そちらが同じことをしている)。
        /// </summary>
        private void KeepAimRotation()
        {
            if (_mover == null || _mover.enabled) return;

            _mover.RotateTowardInput();
        }

        /// <summary>
        /// いま撃ってよいか。他の技が動いている間は撃たずに待つ。
        /// 割り込むと位置や向きが取り合いになって、演出が破綻する。
        /// </summary>
        private bool CanFireNow()
        {
            if (!Battle.BattlePlayGate.IsPlayable) return false;
            if (_health != null && _health.IsDead) return false;

            // 投げ終わって降りているだけの間は、そのまま次へ繋げたほうが気持ちいい
            PlayerEnergyBallSkill energyBallSkill = GetComponent<PlayerEnergyBallSkill>();
            if (energyBallSkill != null && energyBallSkill.IsBusy && !energyBallSkill.IsDescending) return false;

            PlayerDiveSkill diveSkill = GetComponent<PlayerDiveSkill>();
            if (diveSkill != null && (diveSkill.IsFlying || diveSkill.IsAiming)) return false;

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

            if (_useGamepadShoulder)
            {
                Gamepad gamepad = Gamepad.current;
                if (gamepad != null && gamepad.rightShoulder.isPressed) held = true;
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

            // 狙いに入った時点で他の人に『合わせろ』の合図を出す。
            // 撃ってからでは間に合わないので、構えの段階から知らせる
            if (_enableFriendBeam && IsOwner) photonView.RPC(nameof(RpcBeginAim), RpcTarget.All);

            // 狙い中は移動せず、その場で向きだけ変えられるようにする。
            // ただし他の技が動いている間は触らない。動きの主導権を奪うと演出が壊れる
            if (_mover != null && CanFireNow()) _mover.MoveLock = LocalPlayerMover.MovementLock.RotateOnly;

            if (_aimIndicatorPrefab != null)
            {
                _aimIndicatorInstance = Instantiate(_aimIndicatorPrefab, transform);
                _aimIndicatorInstance.transform.localPosition = Vector3.zero;
                _aimIndicatorInstance.transform.localRotation = Quaternion.identity;
                _aimIndicatorInstance.Configure(CurrentBeamLength, CurrentBeamWidth);
            }
        }

        private void CancelAiming()
        {
            _phase = Phase.Ready;
            _fireReserved = false;

            if (_enableFriendBeam && IsOwner) photonView.RPC(nameof(RpcEndAim), RpcTarget.All);
            if (_mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.None;
            DestroyAimIndicator();
        }

        /// <summary>
        /// 指を離した瞬間。クールタイムを開始し、跳び上がってから照射を始める。
        /// 跳び上がりの高さが0のときは、その場ですぐ照射する。
        /// </summary>
        private void Fire()
        {
            _fireReserved = false;
            DestroyAimIndicator();
            _cooldownRemainSec = _cooldownSec;

            // 痕を落とす地面の高さは、真下を測って決める。
            // とびこみの最中など空中で撃つこともあるので、足元の高さをそのまま使うと痕が浮く
            _beamGroundY = ResolveGroundY();

            if (_riseHeight > 0f)
            {
                StartRising();
            }
            else
            {
                StartBeam();
            }
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

            photonView.RPC(nameof(RpcBeginLeap), RpcTarget.All);
        }

        private void UpdateRising()
        {
            if (_health != null && _health.IsDead) { AbortLeap(); return; }

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
            StartBeam();
        }

        /// <summary>照射を全クライアントで開始する。発射位置と向きは撃った本人が決めて配る</summary>
        private void StartBeam()
        {
            Vector3 origin = ResolveBeamOrigin();
            // とびこみで叩きつけた直後の受付時間に撃てたら強化する。
            // 跳び上がりの一瞬に合わせるのは難しすぎたので、音も揺れもある着地を合図にした
            bool boosted = _enableDiveBoost && _diveSkill != null && _diveSkill.IsBoostWindowOpen;

            // 見た目も当たり判定も全員で揃える必要があるので、強化したかどうかも配る
            photonView.RPC(nameof(RpcStartBeam), RpcTarget.All,
                origin, ResolveBeamDirection(origin), _beamGroundY, boosted);
        }

        private void UpdateDescending()
        {
            if (_health != null && _health.IsDead) { AbortLeap(); return; }

            _descendElapsedSec += Time.deltaTime;
            if (_controller != null) _controller.Move(Vector3.down * (_descendSpeed * Time.deltaTime));

            bool landed = _controller == null || _controller.isGrounded;
            if (landed || _descendElapsedSec >= MAX_DESCEND_SEC) EndLeap();
        }

        /// <summary>着地して操作を戻す</summary>
        private void EndLeap()
        {
            _phase = Phase.Ready;
            _leapActive = false;
            if (_mover != null) _mover.MoveLock = LocalPlayerMover.MovementLock.None;
        }

        /// <summary>照射前後に死んだときなど、空中で止まったままにならないよう後始末する</summary>
        private void AbortLeap()
        {
            ReleasePose();
            EndLeap();
        }

        /// <summary>
        /// 死亡した瞬間にビームを中断する。狙い中・跳び上がり中・照射中のどこで死んでも、
        /// エフェクトと当たり判定を止めて移動ロックを外す。
        /// 死亡は全クライアントで同時に流れてくるので、追加の通信なしで全員の画面から消える。
        /// クールタイムは戻さない(撃った扱いのままにする)。
        /// </summary>
        private void InterruptOnDeath()
        {
            if (_phase == Phase.Ready) return;

            DestroyAimIndicator();

            // 中断なので音も即座に切る。エフェクトだけ消えて音が残ると不自然になる
            StopLoopSound();

            // 中断なので、通常終了時のようにフェードアウトはさせず即座に消す
            _targetStates.Clear();
            _grassWaveDistances.Clear();
            if (_beamEffectInstance != null)
            {
                Destroy(_beamEffectInstance);
                _beamEffectInstance = null;
                _beamVisual = null;
            }

            ReleasePose();

            // 空中で止まったままにならないよう、跳び上がりの状態も畳んで移動ロックを外す
            _leapActive = false;
            EndLeap();

            Debug.Log("[PlayerBeamSkill] 死亡したためビームを中断しました");
        }

        /// <summary>
        /// 狙っている範囲に相手がいるかを調べて、照準の色を切り替える。
        /// 実際に照射したときと同じ形・同じ判定で調べるので、色が変わったら必ず当たる。
        /// </summary>
        private void UpdateAimHitState()
        {
            if (_aimIndicatorInstance == null) return;

            Vector3 origin = ResolveBeamOrigin();
            Vector3 direction = ResolveBeamDirection(origin);
            Vector3 endPoint = origin + direction * CurrentBeamLength;

            int count = Physics.OverlapCapsuleNonAlloc(
                origin, endPoint, CurrentBeamWidth, _overlapBuffer, _targetLayers, QueryTriggerInteraction.Collide);

            bool willHit = false;
            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapBuffer[i];
                if (collider == null) continue;
                if (collider.transform == transform || collider.transform.IsChildOf(transform)) continue;

                HitTarget target = collider.GetComponentInParent<HitTarget>();
                if (target == null || !target.CanBeHit) continue;

                // ダメージを届けられない相手は当たった扱いにしない
                if (target.NetworkId == 0) continue;

                willHit = true;
                break;
            }

            _aimIndicatorInstance.SetWillHit(willHit);
        }

        private void DestroyAimIndicator()
        {
            if (_aimIndicatorInstance == null) return;
            Destroy(_aimIndicatorInstance.gameObject);
            _aimIndicatorInstance = null;
        }

        // ---- RPC -----------------------------------------

        /// <summary>跳び上がりの開始。位置と回転は座標同期で伝わるので、ここではポーズだけ揃える</summary>
        /// <summary>狙いに入ったことを全員に知らせる。合図はこれを見て出る</summary>
        [PunRPC]
        private void RpcBeginAim()
        {
            FriendBeam.BeginAim(this);
        }

        /// <summary>狙いをやめたことを全員に知らせる</summary>
        [PunRPC]
        private void RpcEndAim()
        {
            FriendBeam.EndAim(this);
        }

        [PunRPC]
        private void RpcBeginLeap()
        {
            HoldPose();

            // 跳び上がりは全員の画面で見えているので、ここで鳴らせば全員に聞こえる
            PlayOneShotClip(_windupClip, 1f);
        }

        /// <summary>照射の開始。全員のクライアントで呼ばれ、見た目とアニメを揃える</summary>
        [PunRPC]
        private void RpcStartBeam(Vector3 origin, Vector3 direction, float groundY, bool boosted)
        {
            // 万一前回の照射が残っていたら片付けてから始める
            if (_phase == Phase.Firing) FinishFiring();

            _phase = Phase.Firing;
            _fireElapsedSec = 0f;
            _currentBeamLength = 0f;
            _beamOrigin = origin;
            _beamDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            _targetStates.Clear();

            _grassWaveDistances.Clear();
            _grassWaveTimerSec = 0f;

            // 強化の倍率は撃つたびに決まる。撃った本人の判定を全員がそのまま使う
            _lengthScale = boosted ? _boostLengthScale : 1f;
            _widthScale = boosted ? _boostWidthScale : 1f;
            _damageScale = boosted ? _boostDamageScale : 1f;

            // 空中から撃つと自分の足元は地面ではないので、撃った本人が測った高さを使う
            _beamGroundY = groundY;
            if (_muzzleTransform != null) _beamOrigin = ResolveBeamOrigin();

            // 発射口は体の前方に出ているので、根元(距離0)から痕を置いてよい
            _nextDecalDistance = 0f;

            if (_beamEffectPrefab != null)
            {
                _beamEffectInstance = Instantiate(
                    _beamEffectPrefab, _beamOrigin, Quaternion.LookRotation(_beamDirection));

                _beamVisual = _beamEffectInstance.GetComponent<DestructionBeamVisual>();
                if (_beamVisual != null)
                {
                    _beamVisual.Configure(_beamOrigin, _beamDirection, _currentBeamLength, CurrentBeamWidth);
                }

                // パーティクルが混ざっていた場合に備えて Hierarchy スケーリングにしておく
                foreach (var ps in _beamEffectInstance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
            }

            // 頭突きモーションを頭を突き出した位置で止めて照射ポーズにする
            HoldPose();

            // 口元で顎を開いて噛み閉じることで「がぶっと吐き出した」感じを付ける
            PlayBiteVfx();

            // 撃った本人の足元は、跳び上がった勢いで草がなぎ倒される
            if (_grassFlattenScale > 0f)
            {
                var feet = new Vector3(transform.position.x, _beamGroundY, transform.position.z);
                Field.GrassField.FlattenAt(feet, CurrentBeamWidth * _grassFlattenScale);
            }

            // 照射中は動けない。跳び上がっている間は重力も止めて空中に留める(本人のみ)
            if (IsOwner && _mover != null)
            {
                _mover.MoveLock = _leapActive
                    ? LocalPlayerMover.MovementLock.Frozen
                    : LocalPlayerMover.MovementLock.Full;
            }

            // 撃った本人の画面だけを揺らす(他人の発射で画面が揺れると見づらいため)
            if (IsOwner) PlayFireCameraShake();

            // 発射の一撃と、照射中のループを同時に始める。
            // 強化ビームは音を低くする。太さも威力も上がっているので、耳でも大きく感じさせたい
            float pitch = boosted ? _boostPitch : 1f;
            PlayOneShotClip(_fireClip, pitch);
            StartLoopSound(pitch);

            // 撃ち始めたので狙いの合図は下ろし、代わりに合体できるか調べる
            FriendBeam.EndAim(this);
            TryFormFriendBeam();
        }

        /// <summary>
        /// 音の口を必要になった時点で用意する。
        /// 自分のビームは距離で小さくならない2Dで鳴らし、他人のビームは3Dで鳴らす。
        /// 全員が2Dだと4人分の照射音が同じ音量で重なって、自分が撃っているのか分からなくなるため。
        /// </summary>
        private AudioSource EnsureAudioSource(ref AudioSource source, bool loop)
        {
            if (source != null) return source;

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;

            source.spatialBlend = IsOwner ? 0f : _othersSpatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 5f;
            source.maxDistance = _othersMaxDistance;

            return source;
        }

        /// <summary>溜め・発射など、鳴らしっぱなしにしない音を重ねて鳴らす</summary>
        private void PlayOneShotClip(AudioClip clip, float pitch)
        {
            if (clip == null) return;

            EnsureAudioSource(ref _oneShotAudio, false);
            _oneShotAudio.pitch = pitch;
            _oneShotAudio.PlayOneShot(clip, _fireVolume);
        }

        /// <summary>照射中のループを鳴らし始める</summary>
        private void StartLoopSound(float pitch)
        {
            if (_loopClip == null) return;

            EnsureAudioSource(ref _loopAudio, true);

            _loopFadeRemainSec = 0f;
            _loopAudio.clip = _loopClip;

            // 撃つたびに同じ聞こえ方だと作り物っぽくなるので、少しだけ散らす
            _loopAudio.pitch = pitch * Random.Range(0.96f, 1.04f);
            _loopAudio.volume = _loopVolume;
            _loopAudio.Play();

            // 毎回違う位置から流す。同じ場所から始めると耳が繰り返しを覚えてしまう
            _loopAudio.time = Random.Range(0f, Mathf.Max(0.01f, _loopClip.length - 0.05f));
        }

        /// <summary>照射が終わったので、指定の秒数でループを絞っていく</summary>
        private void FadeOutLoopSound(float durationSec)
        {
            if (_loopAudio == null || !_loopAudio.isPlaying) return;

            if (durationSec <= 0f) { StopLoopSound(); return; }

            _loopFadeDurationSec = durationSec;
            _loopFadeRemainSec = durationSec;
            _loopFadeStartVolume = _loopAudio.volume;
        }

        /// <summary>ループを即座に止める</summary>
        private void StopLoopSound()
        {
            _loopFadeRemainSec = 0f;

            if (_loopAudio == null || !_loopAudio.isPlaying) return;

            _loopAudio.Stop();
        }

        /// <summary>
        /// ループ音のフェードを進める。
        /// ヒットストップで時間が止まっても音だけは進めたいので、実時間で数える。
        /// </summary>
        private void UpdateLoopFade()
        {
            if (_loopFadeRemainSec <= 0f) return;

            _loopFadeRemainSec -= Time.unscaledDeltaTime;

            if (_loopFadeRemainSec <= 0f) { StopLoopSound(); return; }

            if (_loopAudio != null)
            {
                _loopAudio.volume = _loopFadeStartVolume * (_loopFadeRemainSec / _loopFadeDurationSec);
            }
        }

        /// <summary>発射の瞬間にカメラを短く揺らして「撃った感」を出す</summary>
        /// <summary>
        /// 受付時間のうちに撃った人がいれば、その全員をまとめて強化する。
        /// 先に撃っていた人は途中から太くなるが、それが『合わさった』合図にもなる。
        /// </summary>
        private void TryFormFriendBeam()
        {
            if (!_enableFriendBeam) return;

            var partners = FriendBeam.RegisterShot(this);
            if (partners.Count == 0) return;

            int members = Mathf.Min(partners.Count + 1, FriendBeam.MAX_MEMBERS);

            // リストは使い回しなので、強化をかける前に控えを取る
            var targets = new PlayerBeamSkill[partners.Count];
            for (int i = 0; i < partners.Count; i++) targets[i] = partners[i];

            // カットインに『誰と誰が合わせたか』を出すので、先に名前を揃える。
            // 自分を左、あとから合わせた人をまとめて右に置く
            string leftName = OwnerName;
            var rightBuilder = new System.Text.StringBuilder();
            foreach (PlayerBeamSkill target in targets)
            {
                if (target == null) continue;
                if (rightBuilder.Length > 0) rightBuilder.Append(" ＋ ");
                rightBuilder.Append(target.OwnerName);
            }

            string rightName = rightBuilder.ToString();

            ApplyFriendBoost(members, leftName, rightName);
            foreach (PlayerBeamSkill target in targets)
            {
                // 相手から見ると左右が入れ替わる。自分の名前が左に来るほうが分かりやすい
                if (target != null) target.ApplyFriendBoost(members, target.OwnerName, leftName);
            }

            // 光の橋はワールドに置かれるので視界を塞がない。
            // 参加していない人の画面にも出して、協力が起きたことを見せる
            if (_friendBridgeSec > 0f)
            {
                foreach (PlayerBeamSkill target in targets)
                {
                    if (target == null) continue;

                    FriendBeamBridge.Play(transform, target.transform, _friendColor, _friendBridgeSec);
                }
            }
        }

        /// <summary>
        /// 合体ぶんの強化をかける。照射中でなければ何もしない。
        /// あとから人数が増えたときだけ上書きするので、3人目・4人目にも追随できる。
        /// </summary>
        public void ApplyFriendBoost(int members, string leftName, string rightName)
        {
            if (members < 2 || _phase != Phase.Firing) return;
            if (_friendMembers >= members) return;

            _friendMembers = members;
            int index = members - 2;

            // とびこみ強化と掛け算にすると壊れるので、大きいほうだけを使う
            _damageScale = Mathf.Max(_damageScale, PickScale(_friendDamageScales, index));
            _widthScale = Mathf.Max(_widthScale, PickScale(_friendWidthScales, index));
            _lengthScale = Mathf.Max(_lengthScale, PickScale(_friendLengthScales, index));

            if (_beamVisual != null) _beamVisual.OverrideColor(_friendColor);

            // 合わせられたご褒美にクールタイムを削る。次も合わせたくなるようにする
            if (IsOwner) _cooldownRemainSec *= _friendCooldownScale;

            // カットインは合わせられた本人の画面にだけ出す。
            // 関わっていない人の画面にも出ると、ただ視界を塞ぐだけになる
            if (!IsOwner) return;

            // 同じ瞬間に何人ぶんも成立するので、演出は1回だけに間引く
            if (!FriendBeam.TryAnnounce(members)) return;

            if (_friendCutinSec > 0f) FriendBeamCutin.Play(leftName, rightName, members, _friendColor, _friendCutinSec);
        }

        /// <summary>人数ぶんの倍率を取り出す。設定が足りなければ最後の値を使い回す</summary>
        private static float PickScale(float[] scales, int index)
        {
            if (scales == null || scales.Length == 0) return 1f;

            return scales[Mathf.Clamp(index, 0, scales.Length - 1)];
        }

        private void PlayFireCameraShake()
        {
            if (_fireCameraShakeAmplitude <= 0f || _fireCameraShakeDurationSec <= 0f) return;

            ThirdPersonCamera playerCamera = FindAnyObjectByType<ThirdPersonCamera>();
            if (playerCamera != null) playerCamera.Shake(_fireCameraShakeAmplitude, _fireCameraShakeDurationSec);

            // 撃った足元から輪を走らせる。反動が地面に伝わったように見せる
            Battle.ShockwaveRing.Play(transform.position, new Color(0.7f, 0.92f, 1.0f, 1.0f), 4.0f, 0.35f, 0.5f);
        }

        /// <summary>ヒットの通知。全員のクライアントでエフェクトとダメージ処理を行う</summary>
        [PunRPC]
        private void RpcBeamHit(
            Vector3 hitPoint, int targetNetworkId, int damage, bool combo, bool initial, PhotonMessageInfo info)
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
                if (component != null) component.Play(damage, combo);
            }

            int attackerActorNumber = info.Sender != null ? info.Sender.ActorNumber : -1;
            target.NotifyHit(position, attackerActorNumber, damage);

            // 焼き始めは強く光らせ、照射中は薄く点滅させる。
            // ずっと同じ強さで光らせると、白いままの敵になってしまう
            Battle.HitFlash.Play(
                target.transform,
                new Color(0.85f, 0.95f, 1.0f, 1.0f),
                initial ? 0.14f : 0.05f);

            // 焼き始めだけ大きく出す。照射中ずっと同じ擬音だと、
            // どこで当たり始めたのかが分からなくなる
            if (initial) Battle.Onomatopoeia.Play(position, "ゴォォッ！", new Color(0.75f, 0.95f, 1.0f, 1.0f), 1.1f);
            else Battle.Onomatopoeia.Play(position, "ジジッ", new Color(0.7f, 0.88f, 1.0f, 1.0f), 0.45f);
        }

        // ---- 照射の進行(全クライアント) -------------------

        private void UpdateFiring()
        {
            _fireElapsedSec += Time.deltaTime;

            // 口元は頭のモーションで動くので、照射中も毎フレーム追従させる
            if (_muzzleTransform != null) _beamOrigin = ResolveBeamOrigin();

            UpdateBeamLength();
            SpawnBeamDecals();
            UpdateGrassWaves();
            SpawnImpactRings();
            SpawnMuzzleRings();

            // 木はシーンに置かれていて全クライアントに同じものがあり、この処理も全員で走る。
            // そのため追加の通信なしで全員の画面の同じ木が倒れる(デカールや草と同じ方式)
            if (_breakTrees)
            {
                Field.BreakableTree.BreakAlongBeam(_beamOrigin, _beamDirection, _currentBeamLength, CurrentBeamWidth);
                Field.BreakableProp.BreakAlongBeam(_beamOrigin, _beamDirection, _currentBeamLength, CurrentBeamWidth);
            }

            // 当たり判定は操作している本人だけが取る(二重ダメージを防ぐ)
            if (IsOwner) UpdateBeamHit();

            if (_fireElapsedSec >= _fireDurationSec) FinishFiring();
        }

        /// <summary>ビームを根元から徐々に伸ばし、見た目に反映する</summary>
        private void UpdateBeamLength()
        {
            if (_growDurationSec <= 0f)
            {
                _currentBeamLength = CurrentBeamLength;
            }
            else
            {
                float t = Mathf.Clamp01(_fireElapsedSec / _growDurationSec);
                _currentBeamLength = Mathf.Lerp(0f, CurrentBeamLength, t);
            }

            if (_beamVisual != null)
            {
                _beamVisual.Configure(_beamOrigin, _beamDirection, _currentBeamLength, CurrentBeamWidth);
            }
        }

        /// <summary>
        /// ビームが伸びて指定間隔を越えるたびに、その真下の地面へ痕(デカール)を置く。
        /// 見た目だけの演出なので、エフェクトと同じく全クライアントで動くこの処理から呼べば
        /// 追加の通信なしで全員の画面に痕が出る(ゴリラのビームと同じ方式)。
        /// </summary>
        /// <summary>
        /// 着弾点から輪を繰り返し広げて、削って押し広げている感じを出す。
        /// 輪は地面に平らに広がる作りなので、先端をそのまま使わず足元の高さへ落とす
        /// (胴体の高さに浮かせると、横から見たときに板が浮いて見えてしまう)。
        /// 照射開始からの経過時間が間隔をまたいだ瞬間に出すので、別途タイマーを持たなくてよい。
        /// </summary>
        private void SpawnImpactRings()
        {
            if (_impactRingPrefab == null || _impactRingIntervalSec <= 0f) return;

            // 伸びきる前は先端が根元と重なるので、ある程度伸びてから出す
            if (_currentBeamLength < CurrentBeamWidth) return;

            if (!CrossedInterval(_impactRingIntervalSec)) return;

            Vector3 point = _beamOrigin + _beamDirection * _currentBeamLength;
            point.y = _beamGroundY;

            // 草は照射の波が別に倒しているので、ここでは倒さない
            EnergyShockwave.Spawn(
                _impactRingPrefab, point, _impactRingStartRadius, _impactRingEndRadius,
                _impactRingDurationSec, _impactRingThickness, false, 1, 0f);
        }

        /// <summary>
        /// 撃っている本人の足元からも輪を広げる。反動を地面で踏ん張って受け止めている感じが出て、
        /// ビームの根元が地面から浮いて見えるのを抑えられる。
        /// </summary>
        private void SpawnMuzzleRings()
        {
            if (_muzzleRingPrefab == null) return;
            if (!CrossedInterval(_muzzleRingIntervalSec)) return;

            Vector3 point = transform.position;
            point.y = _beamGroundY;

            EnergyShockwave.Spawn(
                _muzzleRingPrefab, point, _muzzleRingStartRadius, _muzzleRingEndRadius,
                _muzzleRingDurationSec, _muzzleRingThickness, false, 1, 0f);
        }

        /// <summary>
        /// 照射開始からの経過時間が、指定した間隔をこのフレームでまたいだか。
        /// 撃つたびに経過時間が0から始まるので、タイマーを持たなくてもリセット漏れが起きない。
        /// </summary>
        private bool CrossedInterval(float intervalSec)
        {
            if (intervalSec <= 0f) return false;

            float previous = _fireElapsedSec - Time.deltaTime;
            return Mathf.FloorToInt(_fireElapsedSec / intervalSec)
                != Mathf.FloorToInt(previous / intervalSec);
        }

        private void SpawnBeamDecals()
        {
            if (_beamDecalPrefab == null) return;

            // ビームは体の高さから出ているので、発射時に覚えた足元の高さに落とす
            float groundY = _beamGroundY;

            while (_nextDecalDistance <= _currentBeamLength)
            {
                Vector3 point = _beamOrigin + _beamDirection * _nextDecalDistance;
                point.y = groundY;

                AttackDecal.Spawn(_beamDecalPrefab, point, CurrentBeamWidth * 2f * _decalWidthScale);
                _nextDecalDistance += _decalIntervalMeters;
            }
        }

        /// <summary>
        /// 照射している間、なぎ倒しの波を一定間隔でビームの根元から先端へ走らせる。
        /// 1回倒すだけだと草が伏せたままになるので、波が通るたびに倒れて起き上がり、なびいて見える。
        /// </summary>
        private void UpdateGrassWaves()
        {
            if (_grassFlattenScale <= 0f || _grassWaveIntervalSec <= 0f) return;

            _grassWaveTimerSec -= Time.deltaTime;
            if (_grassWaveTimerSec <= 0f)
            {
                _grassWaveTimerSec = _grassWaveIntervalSec;
                _grassWaveDistances.Add(0f);
            }

            float radius = CurrentBeamWidth * _grassFlattenScale;

            // 進めながら、先端を追い越した波は消す
            for (int i = _grassWaveDistances.Count - 1; i >= 0; i--)
            {
                float distance = _grassWaveDistances[i] + _grassWaveSpeed * Time.deltaTime;
                if (distance > _currentBeamLength)
                {
                    _grassWaveDistances.RemoveAt(i);
                    continue;
                }

                _grassWaveDistances[i] = distance;

                Vector3 point = _beamOrigin + _beamDirection * distance;
                point.y = _beamGroundY;
                Field.GrassField.FlattenAt(point, radius);
            }
        }

        private void FinishFiring()
        {
            _targetStates.Clear();
            _friendMembers = 0;

            // 見た目のフェードと同じ時間で音も絞る。絵と音の消え方を揃える
            FadeOutLoopSound(_fadeOutDurationSec);

            if (_beamEffectInstance != null)
            {
                if (_beamVisual != null) _beamVisual.FadeOut(_fadeOutDurationSec);
                else Destroy(_beamEffectInstance);

                _beamEffectInstance = null;
                _beamVisual = null;
            }

            // 止めていた頭突きモーションを再開し、最後まで再生してもらう
            ReleasePose();

            if (IsOwner && _leapActive)
            {
                // 空中にいるので、地面まで降りてから操作を返す
                _phase = Phase.Descending;
                _descendElapsedSec = 0f;
                return;
            }

            _phase = Phase.Ready;
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
                _beamOrigin, endPoint, CurrentBeamWidth, _overlapBuffer, _targetLayers, QueryTriggerInteraction.Collide);

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
                    SendBeamHit(target, collider, _initialDamage, true);

                    // 焼き始めの手応え。照射中はここが一番強く感じる場面
                    if (IsOwner) Battle.HitStop.Play(_initialHitStopSec, _hitStopTimeScale, _hitStopRecoverSec);

                    // 焼き始めだけ周りを引かせる。照射中ずっと絞ると音が痩せて聞こえる
                    UI.BgmPlayer.Duck(0.35f, 0.12f, 0.4f);

                    // 当たり始めの1回だけ決めゴマを出す。照射中ずっと出すと画面が点滅して見づらい
                    UI.ImpactFrame.Play(new Color(0.75f, 0.9f, 1.0f, 0.55f), 0.045f);
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
                    SendBeamHit(state.Target, state.Collider, _tickDamage, false);

                    // 継続はごく短く。長く止めるとカクついて照射が途切れて見える
                    if (IsOwner) Battle.HitStop.Play(_tickHitStopSec, _hitStopTimeScale, _hitStopRecoverSec);
                }
            }

            foreach (int id in _removeWork) _targetStates.Remove(id);
        }

        /// <param name="initial">焼き始めの一撃かどうか。照射中の継続ダメージなら false</param>
        private void SendBeamHit(HitTarget target, Collider collider, int damage, bool initial)
        {
            // 強化ぶんを先に掛けてから、そのうえに同時ヒットボーナスを掛ける
            damage = Mathf.RoundToInt(damage * _damageScale);

            // 他のプレイヤーが直前に当てていれば、同時ヒットボーナスを掛けてから配る
            bool combo = Battle.ComboBonus.IsActive;
            damage = Battle.ComboBonus.Apply(damage);

            if (damage <= 0) return;

            // ビームの軸上で相手に一番近い点を求め、そこから相手表面のヒット位置を出す
            Vector3 toTarget = target.transform.position - _beamOrigin;
            float along = Mathf.Clamp(Vector3.Dot(toTarget, _beamDirection), 0f, _currentBeamLength);
            Vector3 axisPoint = _beamOrigin + _beamDirection * along;
            Vector3 hitPoint = collider != null ? collider.ClosestPoint(axisPoint) : target.transform.position;

            photonView.RPC(nameof(RpcBeamHit), RpcTarget.All, hitPoint, target.NetworkId, damage, combo, initial);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>
        /// ビームの発射位置。口元の Transform が指定されていればそこ、無ければ足元からの高さ・前方オフセット。
        /// 頭のボーンは向きが独特なので、微調整のオフセットはキャラ本体の向きを基準に足す。
        /// </summary>
        /// <summary>
        /// 真下を調べて地面の高さを返す。自分の体とボスは足場として数えない。
        /// ボスの上を地面と見なすと、痕が相手の頭の高さに並んでしまう。
        /// 見つからなければ、いまの足元の高さで妥協する。
        /// </summary>
        private float ResolveGroundY()
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;

            int count = Physics.RaycastNonAlloc(
                origin, Vector3.down, _groundBuffer, 40f, _targetLayers, QueryTriggerInteraction.Ignore);

            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider collider = _groundBuffer[i].collider;
                if (collider == null) continue;
                if (collider.transform == transform || collider.transform.IsChildOf(transform)) continue;

                // 当てられる相手(ボスなど)の上は地面ではない
                if (collider.GetComponentInParent<HitTarget>() != null) continue;

                // いちばん高い足場を採る。低いところを拾うと痕が地面に埋まる
                if (_groundBuffer[i].point.y > best) best = _groundBuffer[i].point.y;
            }

            return float.IsNegativeInfinity(best) ? transform.position.y : best;
        }

        private Vector3 ResolveBeamOrigin()
        {
            if (_muzzleTransform != null)
            {
                return _muzzleTransform.position
                    + transform.right * _muzzleLocalOffset.x
                    + transform.up * _muzzleLocalOffset.y
                    + transform.forward * _muzzleLocalOffset.z;
            }

            return transform.position
                + Vector3.up * _originHeight
                + transform.forward * _originForwardOffset;
        }

        /// <summary>
        /// ビームの向き。空中から撃つときは、狙いの表示と同じ地面の位置(足元から前方 CurrentBeamLength)へ
        /// 着弾するように下向きへ傾ける。傾けない設定なら、そのまま正面へ水平に撃つ。
        /// </summary>
        private Vector3 ResolveBeamDirection(Vector3 origin)
        {
            Vector3 forward = transform.forward;
            if (!_aimAtGroundEnd) return forward;

            Vector3 target = transform.position + forward * CurrentBeamLength;
            target.y = _beamGroundY;

            Vector3 toTarget = target - origin;
            return toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : forward;
        }

        /// <summary>照射ポーズで固める。跳び上がり時と照射開始時の二重呼び出しを防ぐ</summary>
        private void HoldPose()
        {
            if (_poseHeld) return;

            _poseHeld = true;
            if (_animationDriver != null) _animationDriver.HoldAttackPose(_poseFreezeNormalizedTime);
        }

        private void ReleasePose()
        {
            if (!_poseHeld) return;

            _poseHeld = false;
            if (_animationDriver != null) _animationDriver.ReleaseAttackPose();
        }

        /// <summary>発射の瞬間に口元で噛みつきエフェクトを再生する</summary>
        private void PlayBiteVfx()
        {
            if (_biteVfxPrefab == null) return;

            BiteVfx bite = BiteVfx.Spawn(_biteVfxPrefab, _beamOrigin);
            if (bite != null && !Mathf.Approximately(_biteVfxScale, 1f))
            {
                bite.transform.localScale *= _biteVfxScale;
            }
        }

        /// <summary>このクライアントがこのキャラを操作しているか</summary>
        private bool IsOwner => photonView == null || photonView.IsMine;
    }
}
