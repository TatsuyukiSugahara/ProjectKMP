using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// ゴリラ敵AI本体（ステートパターンのコンテキスト）。
    /// 待機→徘徊→追跡→攻撃範囲内?→攻撃タイプ判定→スタンプ攻撃/通常攻撃/破壊光線→硬直→再追跡、
    /// 見失ったら徘徊へ戻る、という一連の挙動を各ステートクラスに委譲して実行する。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class GorillaAI : MonoBehaviour
    {
        // ---- アニメーション名（AC_Gorillaのステート名に対応） ----
        public const string ANIM_IDLE          = "Idle_A";
        public const string ANIM_WALK          = "Walk";
        public const string ANIM_RUN           = "Run";
        public const string ANIM_JUMP          = "Jump";
        public const string ANIM_STAMP_ATTACK  = "Bounce"; // @todo 専用の踏みつけモーションがあれば差し替える
        public const string ANIM_NORMAL_ATTACK = "Attack";
        public const string ANIM_HIT           = "Hit";
        public const string ANIM_DEATH         = "Death";
        private const float ANIM_CROSSFADE = 0.15f;

        // ---- 索敵 ----
        [Header("索敵")]
        [SerializeField] private float _searchRadius = 8.0f;
        [SerializeField, Range(0f, 360f), Tooltip("正面を中心とした視野角(度)。索敵範囲内でも、この角度の外(背後など)にいる間は発見しない")]
        private float _viewAngle = 120.0f;
        [SerializeField] private float _loseSightRadius = 10.0f;

        // ---- 攻撃 ----
        [Header("攻撃")]
        [SerializeField] private float _attackRange = 2.5f;
        [SerializeField] private float _stampAttackNearDistance = 1.2f;
        [SerializeField, Range(0f, 1f)] private float _stampAttackProbability = 0.3f;
        [SerializeField] private float _normalAttackStaggerTime = 0.6f;
        [SerializeField] private float _stampAttackStaggerTime = 1.2f;

        // ---- 攻撃の当たり判定・ダメージ ----
        [Header("攻撃の当たり判定・ダメージ")]
        [SerializeField, Min(0), Tooltip("通常攻撃(頭突き)のダメージ")]
        private int _normalAttackDamage = 20;

        [SerializeField, Min(0f), Tooltip("通常攻撃の当たり判定が届く距離(メートル、体の中心から)")]
        private float _normalAttackHitRange = 3.0f;

        [SerializeField, Range(0f, 360f), Tooltip("通常攻撃の当たり判定の角度(度)。正面を中心とした扇形")]
        private float _normalAttackHitAngle = 120.0f;

        [SerializeField, Min(0), Tooltip("スタンプ攻撃(踏みつけ)のダメージ")]
        private int _stampAttackDamage = 30;

        [SerializeField, Min(0f), Tooltip("スタンプ攻撃の衝撃波が届く半径(メートル、着地点から)")]
        private float _stampAttackRadius = 3.5f;

        // ---- 移動 ----
        [Header("移動")]
        [SerializeField] private float _patrolSpeed = 1.5f;
        [SerializeField] private float _chaseSpeed = 3.0f;
        [SerializeField] private float _turnSpeedDeg = 180.0f;
        [SerializeField] private float _wanderRadius = 5.0f;
        [SerializeField] private float _idleTimeMin = 1.5f;
        [SerializeField] private float _idleTimeMax = 3.0f;
        [SerializeField, Tooltip("徘徊中、1回の移動が終わった後に立ち止まる時間の最小値(秒)")]
        private float _patrolWaitTimeMin = 1.0f;
        [SerializeField, Tooltip("徘徊中、1回の移動が終わった後に立ち止まる時間の最大値(秒)")]
        private float _patrolWaitTimeMax = 2.5f;

        // ---- アニメーション速度 ----
        [Header("アニメーション")]
        [SerializeField, Tooltip("Animatorの再生速度倍率。0.25で通常の1/4速(4倍遅く)になる")]
        private float _animationSpeed = 0.25f;

        // ---- ターゲット ----
        [Header("ターゲット")]
        [SerializeField] private Transform _target;
        [SerializeField, Tooltip("Targetが未設定のとき、何秒おきに再探索するか")]
        private float _targetSearchIntervalSec = 0.5f;

        private float _targetSearchTimer;

        // ---- スタンプ攻撃 ----
        [Header("スタンプ攻撃")]
        [SerializeField, Tooltip("スタンプ攻撃が着地した瞬間に出す衝撃波エフェクト")]
        private GameObject _stampImpactEffectPrefab;
        [SerializeField, Tooltip("衝撃波エフェクトの大きさ倍率。1で原寸"), Min(0.01f)]
        private float _stampImpactEffectScale = 0.5f;
        [SerializeField, Tooltip("着地点に残す地面を抉った痕(デカール)。未設定なら痕を残さない")]
        private ProjectKMP.Attack.AttackDecal _stampDecalPrefab;
        [SerializeField, Min(0.01f), Tooltip("痕の直径(メートル)。スタンプ攻撃の範囲に合わせる")]
        private float _stampDecalDiameter = 4.5f;

        // ---- 通常攻撃(頭突き)の予備動作 ----
        [Header("通常攻撃の予備動作")]
        [SerializeField, Tooltip("振りかぶり中に体に出すチャージエフェクト")]
        private GameObject _normalAttackChargeEffectPrefab;
        [SerializeField, Tooltip("チャージエフェクトを出す高さ(足元からのオフセット、メートル)")]
        private float _normalAttackChargeEffectHeight = 1.2f;
        [SerializeField, Tooltip("振りかぶり終了(振り切り開始)の瞬間に出す解放エフェクト")]
        private GameObject _normalAttackSwingEffectPrefab;
        [SerializeField, Tooltip("頭突きが命中した瞬間に出すヒットエフェクト")]
        private GameObject _normalAttackHitEffectPrefab;
        [SerializeField, Tooltip("ヒットエフェクトの大きさ倍率。1で原寸"), Min(0.01f)]
        private float _normalAttackHitEffectScale = 5f;
        [SerializeField, Tooltip("ヒットエフェクトを出す前方オフセット(メートル)")]
        private float _normalAttackHitEffectForwardOffset = 2f;

        // ---- スタンプ攻撃の予備動作 ----
        [Header("スタンプ攻撃の予備動作")]
        [SerializeField, Tooltip("頂点で溜めている間に体に出すチャージエフェクト")]
        private GameObject _stampAttackChargeEffectPrefab;
        [SerializeField, Tooltip("チャージエフェクトを出す高さ(足元からのオフセット、メートル)")]
        private float _stampAttackChargeEffectHeight = 1.2f;

        // ---- 破壊光線攻撃 ----
        [Header("破壊光線攻撃")]
        [SerializeField, Tooltip("この距離以内にいると破壊光線を使える(近すぎるとスタンプ/通常攻撃が優先される)")]
        private float _beamAttackRange = 6.0f;
        [SerializeField, Range(0f, 1f), Tooltip("射程内かつクールタイム明けのとき、破壊光線を選ぶ確率")]
        private float _beamAttackProbability = 0.5f;
        [SerializeField, Tooltip("破壊光線を撃った後、再び使えるようになるまでのクールタイム(秒)")]
        private float _beamAttackCooldownSec = 6.0f;
        [SerializeField, Tooltip("発射前の予備動作(狙い)の時間(秒)")]
        private float _beamWindupTime = 0.6f;
        [SerializeField, Tooltip("光線を出し続ける時間(秒)")]
        private float _beamDuration = 3.0f;
        [SerializeField, Tooltip("光線終了後、硬直ステートに留まる時間(秒)")]
        private float _beamStaggerTime = 1.0f;
        [SerializeField, Tooltip("光線が届く距離(メートル)")]
        private float _beamLength = 10.0f;
        [SerializeField, Min(0f), Tooltip("発射開始時、光線が0から実際の長さまで伸びきるのにかかる時間(秒)。0にすると一瞬で全長になる")]
        private float _beamGrowDuration = 0.2f;
        [SerializeField, Tooltip("光線の当たり判定の太さ(半径、メートル)")]
        private float _beamWidth = 1.2f;
        [SerializeField, Tooltip("光線を出す高さ(足元からのオフセット、メートル)")]
        private float _beamOriginHeight = 1.2f;
        [SerializeField, Tooltip("光線の発射位置を正面方向にずらす距離(メートル)。体に光線がめり込んで見えるのを防ぐ")]
        private float _beamOriginForwardOffset = 1.2f;
        [SerializeField, Min(0), Tooltip("光線に初めて当たった瞬間のダメージ")]
        private int _beamInitialDamage = 3;
        [SerializeField, Min(0), Tooltip("光線に当たり続けている間、一定間隔ごとに入るダメージ(初撃より弱くする想定)")]
        private int _beamContinuousDamage = 1;
        [SerializeField, Min(0.01f), Tooltip("継続ダメージが入る間隔(秒)。この間隔より短い周期では追加ダメージは入らない")]
        private float _beamTickIntervalSec = 0.5f;
        [SerializeField, Tooltip("予備動作中に体に出すチャージエフェクト")]
        private GameObject _beamChargeEffectPrefab;
        [SerializeField, Tooltip("発射中に出し続ける光線本体のエフェクト")]
        private GameObject _beamEffectPrefab;
        [SerializeField, Tooltip("発射中、体を震わせる揺れ幅(メートル)。頭突きモーションのまま止まって見えないようにするための演出")]
        private float _beamFiringShakeAmount = 0.06f;
        [SerializeField, Min(0.01f), Tooltip("発射終了時、光線がパッと消えず徐々に透明になっていく時間(秒)")]
        private float _beamFadeOutDuration = 0.8f;
        [SerializeField, Tooltip("光線の通り道の地面に残す痕(デカール)。未設定なら残さない")]
        private ProjectKMP.Attack.AttackDecal _beamDecalPrefab;
        [SerializeField, Min(0.1f), Tooltip("光線の痕を置く間隔(メートル)。光線が伸びてこの距離を越えるたびに1つ置く")]
        private float _beamDecalIntervalMeters = 2.0f;
        [SerializeField, Min(0.01f), Tooltip("光線の痕の大きさ倍率。1で光線の太さと同じ直径になり、大きくするほど太さより広がる")]
        private float _beamDecalWidthScale = 1.2f;

        private float _beamCooldownRemain;

        // ---- 内部状態 ----
        private Animator _animator;
        private IGorillaState _currentState;
        private Vector3 _homePosition;

        // ---- 死亡・復活(デバッグ用) ----
        private bool _isDead;
        private Vector3 _preDeathPosition;
        private Quaternion _preDeathRotation;
        private Vector3 _preDeathScale;

        /// <summary>死亡ステート中かどうか</summary>
        public bool IsDead => _isDead;

        public Animator Animator => _animator;
        public Transform Target => _target;
        public GameObject StampImpactEffectPrefab => _stampImpactEffectPrefab;
        public float StampImpactEffectScale => _stampImpactEffectScale;
        public ProjectKMP.Attack.AttackDecal StampDecalPrefab => _stampDecalPrefab;
        public float StampDecalDiameter => _stampDecalDiameter;
        public GameObject NormalAttackChargeEffectPrefab => _normalAttackChargeEffectPrefab;
        public float NormalAttackChargeEffectHeight => _normalAttackChargeEffectHeight;
        public GameObject NormalAttackSwingEffectPrefab => _normalAttackSwingEffectPrefab;
        public GameObject NormalAttackHitEffectPrefab => _normalAttackHitEffectPrefab;
        public float NormalAttackHitEffectScale => _normalAttackHitEffectScale;
        public float NormalAttackHitEffectForwardOffset => _normalAttackHitEffectForwardOffset;
        public GameObject StampAttackChargeEffectPrefab => _stampAttackChargeEffectPrefab;
        public float StampAttackChargeEffectHeight => _stampAttackChargeEffectHeight;
        public Vector3 HomePosition => _homePosition;
        public float PatrolSpeed => _patrolSpeed;
        public float ChaseSpeed => _chaseSpeed;
        public float TurnSpeedDeg => _turnSpeedDeg;
        public float WanderRadius => _wanderRadius;
        public float IdleTimeMin => _idleTimeMin;
        public float IdleTimeMax => _idleTimeMax;
        public float PatrolWaitTimeMin => _patrolWaitTimeMin;
        public float PatrolWaitTimeMax => _patrolWaitTimeMax;
        public float NormalAttackStaggerTime => _normalAttackStaggerTime;
        public float StampAttackStaggerTime => _stampAttackStaggerTime;

        // ---- 攻撃の当たり判定・ダメージの公開API ----
        public int NormalAttackDamage => _normalAttackDamage;
        public float NormalAttackHitRange => _normalAttackHitRange;
        public float NormalAttackHitAngle => _normalAttackHitAngle;
        public int StampAttackDamage => _stampAttackDamage;
        public float StampAttackRadius => _stampAttackRadius;

        // ---- 破壊光線攻撃の公開API ----
        public float BeamAttackRange => _beamAttackRange;
        public float BeamAttackProbability => _beamAttackProbability;
        public float BeamWindupTime => _beamWindupTime;
        public float BeamDuration => _beamDuration;
        public float BeamStaggerTime => _beamStaggerTime;
        public float BeamLength => _beamLength;
        public float BeamGrowDuration => _beamGrowDuration;
        public float BeamWidth => _beamWidth;
        public float BeamOriginHeight => _beamOriginHeight;
        public float BeamOriginForwardOffset => _beamOriginForwardOffset;
        public int BeamInitialDamage => _beamInitialDamage;
        public int BeamContinuousDamage => _beamContinuousDamage;
        public float BeamTickIntervalSec => _beamTickIntervalSec;
        public GameObject BeamChargeEffectPrefab => _beamChargeEffectPrefab;
        public GameObject BeamEffectPrefab => _beamEffectPrefab;
        public float BeamFiringShakeAmount => _beamFiringShakeAmount;
        public float BeamFadeOutDuration => _beamFadeOutDuration;
        public ProjectKMP.Attack.AttackDecal BeamDecalPrefab => _beamDecalPrefab;
        public float BeamDecalIntervalMeters => _beamDecalIntervalMeters;
        /// <summary>光線の痕の直径(メートル)。光線の太さ(半径×2)に倍率を掛けて求めるので、太さを変えても痕が追従する</summary>
        public float BeamDecalDiameter => _beamWidth * 2.0f * _beamDecalWidthScale;

        /// <summary>クールタイムが明けていて破壊光線を使えるか</summary>
        public bool CanUseBeamAttack => _beamCooldownRemain <= 0f;

        /// <summary>破壊光線を使ったことを伝え、クールタイムを開始する</summary>
        public void NotifyBeamAttackUsed()
        {
            _beamCooldownRemain = _beamAttackCooldownSec;
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _animator.speed = _animationSpeed;
        }

        private void Start()
        {
            _homePosition = transform.position;

            if (_target == null)
            {
                _target = FindDogTarget();
            }

            ChangeState(new GorillaStateIdle());
        }

        /// <summary>
        /// 追跡対象(プレイヤーが操作する犬 = Husky)を探す。
        /// Playerタグが付いていればそれを優先し、無ければ ProjectKMP.Player.PlayerMover を持つ
        /// オブジェクトを探す(ネットワーク越しの他人のキャラも含めて見つかる)。
        /// </summary>
        private Transform FindDogTarget()
        {
            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null)
            {
                return tagged.transform;
            }

            var mover = Object.FindObjectOfType<ProjectKMP.Player.PlayerMover>();
            if (mover != null)
            {
                return mover.transform;
            }

            return null;
        }

        private void Update()
        {
            if (_target == null)
            {
                _targetSearchTimer -= Time.deltaTime;
                if (_targetSearchTimer <= 0f)
                {
                    _targetSearchTimer = _targetSearchIntervalSec;
                    _target = FindDogTarget();
                }
            }

            if (_beamCooldownRemain > 0f)
            {
                _beamCooldownRemain -= Time.deltaTime;
            }

            _currentState?.Update(this);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // @todo 動作確認用デバッグ入力。Iキーで死亡⇔復活をトグルする
            // (このプロジェクトはInput System Packageを使用しているため、
            //  legacyのInput.GetKeyDownではなくKeyboard.currentを使う)
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.iKey.wasPressedThisFrame)
            {
                if (_isDead)
                {
                    Revive();
                }
                else
                {
                    ChangeState(new GorillaStateDeath());
                }
            }

            // @todo 動作確認用デバッグ入力。Oキーで頭突き(通常攻撃)モーションを再生する
            if (keyboard != null && keyboard.oKey.wasPressedThisFrame && !_isDead)
            {
                ChangeState(new GorillaStateNormalAttack());
            }

            // @todo 動作確認用デバッグ入力。Pキーで破壊光線モーションを再生する
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame && !_isDead)
            {
                ChangeState(new GorillaStateBeamAttack());
            }

            // @todo 動作確認用デバッグ入力。Lキーでスタンプ攻撃モーションを再生する
            if (keyboard != null && keyboard.lKey.wasPressedThisFrame && !_isDead)
            {
                ChangeState(new GorillaStateStampAttack());
            }
#endif
        }

        /// <summary>
        /// ステートを切り替える。現在のステートのExit()を呼んでから、新しいステートのEnter()を呼ぶ。
        /// </summary>
        public void ChangeState(IGorillaState newState)
        {
            _currentState?.Exit(this);
            _currentState = newState;
            _currentState?.Enter(this);
        }

        /// <summary>
        /// 死亡ステートに入る直前の座標・回転・スケールを記録する。
        /// GorillaStateDeath.Enter() から呼び出される想定。
        /// </summary>
        public void NotifyDeathStarted()
        {
            _preDeathPosition = transform.position;
            _preDeathRotation = transform.rotation;
            _preDeathScale = transform.localScale;
            _isDead = true;
        }

        /// <summary>死亡状態から復活させる(デバッグ用)。座標・回転・スケールを死亡前の状態に戻し、待機ステートへ遷移する</summary>
        public void Revive()
        {
            transform.position = _preDeathPosition;
            transform.rotation = _preDeathRotation;
            transform.localScale = _preDeathScale;
            _isDead = false;

            // Flipフェーズ中に止めたアニメーション再生速度を元に戻す
            if (_animator != null)
            {
                _animator.speed = _animationSpeed;
            }

            ChangeState(new GorillaStateIdle());
        }

        /// <summary>指定したアニメーションステートをクロスフェードで再生する</summary>
        public void PlayAnimation(string stateName)
        {
            if (_animator != null)
            {
                _animator.CrossFade(stateName, ANIM_CROSSFADE);
            }
        }

        /// <summary>ターゲットとの距離を取得する(ターゲット未設定時はfloat.MaxValue)</summary>
        public float GetDistanceToTarget()
        {
            if (_target == null)
            {
                return float.MaxValue;
            }
            return Vector3.Distance(transform.position, _target.position);
        }

        /// <summary>索敵範囲内 かつ 視野角内にPlayerがいるか(徘徊→追跡の判定)</summary>
        public bool IsPlayerFound()
        {
            if (_target == null)
            {
                return false;
            }

            // 距離判定(索敵範囲外なら見えない)
            if (GetDistanceToTarget() > _searchRadius)
            {
                return false;
            }

            // 視野角判定
            // 正面からの角度が_viewAngleの半分を超えていたら、索敵範囲内でも
            // 視野の外(背後など)にいるので発見できない扱いにする
            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                // ほぼ同一地点にいる場合は視野角に関わらず発見扱いにする
                return true;
            }

            float angle = Vector3.Angle(transform.forward, toTarget.normalized);
            return angle <= _viewAngle * 0.5f;
        }

        /// <summary>Playerを見失ったか(追跡→徘徊の判定)</summary>
        public bool IsPlayerLost()
        {
            return _target == null || GetDistanceToTarget() > _loseSightRadius;
        }

        /// <summary>攻撃範囲内か(距離判定)</summary>
        public bool IsPlayerInAttackRange()
        {
            return _target != null && GetDistanceToTarget() <= _attackRange;
        }

        /// <summary>破壊光線の射程内か(距離判定)</summary>
        public bool IsPlayerInBeamRange()
        {
            return _target != null && GetDistanceToTarget() <= _beamAttackRange;
        }

        /// <summary>攻撃タイプ判定(近距離 or 確率でスタンプ攻撃か通常攻撃かを決める)</summary>
        public bool ShouldUseStampAttack()
        {
            if (GetDistanceToTarget() < _stampAttackNearDistance)
            {
                return true;
            }
            return Random.value < _stampAttackProbability;
        }

        /// <summary>破壊光線を使うかどうかの確率判定(射程・クールタイムは呼び出し側で確認済みの前提)</summary>
        public bool ShouldUseBeamAttack()
        {
            return Random.value < _beamAttackProbability;
        }

        /// <summary>目標地点へ向けて移動・旋回する</summary>
        public void MoveTowards(Vector3 targetPosition, float speed)
        {
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }
            direction.Normalize();

            Quaternion look = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeedDeg * Time.deltaTime);
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, speed * Time.deltaTime);
        }

        /// <summary>その場で目標方向へ旋回のみ行う(移動しない)</summary>
        public void TurnTowards(Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }
            direction.Normalize();

            Quaternion look = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeedDeg * Time.deltaTime);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            UnityEditor.Handles.color = Color.yellow;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, _searchRadius);
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, _attackRange);
            UnityEditor.Handles.color = new Color(0.2f, 0.6f, 1f, 1f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, _beamAttackRange);

            // スタンプ攻撃の範囲(オレンジ)と通常攻撃の扇形(赤の面)
            UnityEditor.Handles.color = new Color(1f, 0.5f, 0f, 1f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, _stampAttackRadius);
            UnityEditor.Handles.color = new Color(1f, 0.2f, 0.2f, 0.15f);
            Vector3 hitBaseDir = transform.forward * _normalAttackHitRange;
            Quaternion hitLeftRot = Quaternion.AngleAxis(-_normalAttackHitAngle * 0.5f, Vector3.up);
            UnityEditor.Handles.DrawSolidArc(transform.position, Vector3.up, hitLeftRot * hitBaseDir, _normalAttackHitAngle, _normalAttackHitRange);

            // 視野角の扇形を表示
            UnityEditor.Handles.color = new Color(1f, 1f, 0f, 0.15f);
            Vector3 baseDir = transform.forward * _searchRadius;
            Quaternion leftRot = Quaternion.AngleAxis(-_viewAngle * 0.5f, Vector3.up);
            UnityEditor.Handles.DrawSolidArc(transform.position, Vector3.up, leftRot * baseDir, _viewAngle, _searchRadius);
        }
#endif
    }
}
