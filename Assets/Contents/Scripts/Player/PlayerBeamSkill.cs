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

        [SerializeField, Tooltip("発射位置の高さ(足元からのオフセット・m)")]
        private float _originHeight = 1.3f;

        [SerializeField, Tooltip("発射位置の前方オフセット(m)。体にビームが重ならないようにする")]
        private float _originForwardOffset = 0.8f;

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

        /// <summary>痕を落とす地面の高さ。口元は動くので、発射時の足元の高さを覚えておく</summary>
        private float _beamGroundY;

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
        private DestructionBeamVisual _beamVisual;
        private BeamAimIndicator _aimIndicatorInstance;

        private LocalPlayerMover _mover;
        private DogAnimationDriver _animationDriver;
        private PlayerHealth _health;
        private PlayerAttack _playerAttack;

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

            // 死亡は被弾RPCから全クライアントで発火するので、各自の画面で同時に中断できる
            if (_health != null) _deathSubscription = _health.Died.Subscribe(_ => InterruptOnDeath());
        }

        private void Start()
        {
            if (IsOwner) Local = this;
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
        }

        private void Update()
        {
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

        /// <summary>
        /// 指を離した瞬間。クールタイムを開始し、跳び上がってから照射を始める。
        /// 跳び上がりの高さが0のときは、その場ですぐ照射する。
        /// </summary>
        private void Fire()
        {
            DestroyAimIndicator();
            _cooldownRemainSec = _cooldownSec;

            // 痕を落とす地面の高さは、跳び上がる前のいまの足元で決める
            _beamGroundY = transform.position.y;

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
            photonView.RPC(nameof(RpcStartBeam), RpcTarget.All, origin, ResolveBeamDirection(origin), _beamGroundY);
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

        private void DestroyAimIndicator()
        {
            if (_aimIndicatorInstance == null) return;
            Destroy(_aimIndicatorInstance.gameObject);
            _aimIndicatorInstance = null;
        }

        // ---- RPC -----------------------------------------

        /// <summary>跳び上がりの開始。位置と回転は座標同期で伝わるので、ここではポーズだけ揃える</summary>
        [PunRPC]
        private void RpcBeginLeap()
        {
            HoldPose();
        }

        /// <summary>照射の開始。全員のクライアントで呼ばれ、見た目とアニメを揃える</summary>
        [PunRPC]
        private void RpcStartBeam(Vector3 origin, Vector3 direction, float groundY)
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
            HoldPose();

            // 口元で顎を開いて噛み閉じることで「がぶっと吐き出した」感じを付ける
            PlayBiteVfx();

            // 撃った本人の足元は、跳び上がった勢いで草がなぎ倒される
            if (_grassFlattenScale > 0f)
            {
                var feet = new Vector3(transform.position.x, _beamGroundY, transform.position.z);
                Field.GrassField.FlattenAt(feet, _beamWidth * _grassFlattenScale);
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
        }

        /// <summary>発射の瞬間にカメラを短く揺らして「撃った感」を出す</summary>
        private void PlayFireCameraShake()
        {
            if (_fireCameraShakeAmplitude <= 0f || _fireCameraShakeDurationSec <= 0f) return;

            ThirdPersonCamera playerCamera = FindAnyObjectByType<ThirdPersonCamera>();
            if (playerCamera != null) playerCamera.Shake(_fireCameraShakeAmplitude, _fireCameraShakeDurationSec);
        }

        /// <summary>ヒットの通知。全員のクライアントでエフェクトとダメージ処理を行う</summary>
        [PunRPC]
        private void RpcBeamHit(Vector3 hitPoint, int targetNetworkId, int damage, bool combo, PhotonMessageInfo info)
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
                Field.BreakableTree.BreakAlongBeam(_beamOrigin, _beamDirection, _currentBeamLength, _beamWidth);
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
            if (_currentBeamLength < _beamWidth) return;

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

                AttackDecal.Spawn(_beamDecalPrefab, point, _beamWidth * 2f * _decalWidthScale);
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

            float radius = _beamWidth * _grassFlattenScale;

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

                    // 焼き始めの手応え。照射中はここが一番強く感じる場面
                    if (IsOwner) Battle.HitStop.Play(_initialHitStopSec, _hitStopTimeScale, _hitStopRecoverSec);
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

                    // 継続はごく短く。長く止めるとカクついて照射が途切れて見える
                    if (IsOwner) Battle.HitStop.Play(_tickHitStopSec, _hitStopTimeScale, _hitStopRecoverSec);
                }
            }

            foreach (int id in _removeWork) _targetStates.Remove(id);
        }

        private void SendBeamHit(HitTarget target, Collider collider, int damage)
        {
            // 他のプレイヤーが直前に当てていれば、同時ヒットボーナスを掛けてから配る
            bool combo = Battle.ComboBonus.IsActive;
            damage = Battle.ComboBonus.Apply(damage);

            if (damage <= 0) return;

            // ビームの軸上で相手に一番近い点を求め、そこから相手表面のヒット位置を出す
            Vector3 toTarget = target.transform.position - _beamOrigin;
            float along = Mathf.Clamp(Vector3.Dot(toTarget, _beamDirection), 0f, _currentBeamLength);
            Vector3 axisPoint = _beamOrigin + _beamDirection * along;
            Vector3 hitPoint = collider != null ? collider.ClosestPoint(axisPoint) : target.transform.position;

            photonView.RPC(nameof(RpcBeamHit), RpcTarget.All, hitPoint, target.NetworkId, damage, combo);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>
        /// ビームの発射位置。口元の Transform が指定されていればそこ、無ければ足元からの高さ・前方オフセット。
        /// 頭のボーンは向きが独特なので、微調整のオフセットはキャラ本体の向きを基準に足す。
        /// </summary>
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
        /// ビームの向き。空中から撃つときは、狙いの表示と同じ地面の位置(足元から前方 _beamLength)へ
        /// 着弾するように下向きへ傾ける。傾けない設定なら、そのまま正面へ水平に撃つ。
        /// </summary>
        private Vector3 ResolveBeamDirection(Vector3 origin)
        {
            Vector3 forward = transform.forward;
            if (!_aimAtGroundEnd) return forward;

            Vector3 target = transform.position + forward * _beamLength;
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
