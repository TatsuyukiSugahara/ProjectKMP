using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// ゴリラ敵AI本体（ステートパターンのコンテキスト）。
    /// 待機→徘徊→追跡→攻撃範囲内?→攻撃タイプ判定→スタンプ攻撃/通常攻撃→硬直→再追跡、
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

        // ---- 内部状態 ----
        private Animator _animator;
        private IGorillaState _currentState;
        private Vector3 _homePosition;

        public Animator Animator => _animator;
        public Transform Target => _target;
        public GameObject StampImpactEffectPrefab => _stampImpactEffectPrefab;
        public float StampImpactEffectScale => _stampImpactEffectScale;
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

            _currentState?.Update(this);
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

        /// <summary>攻撃タイプ判定(近距離 or 確率でスタンプ攻撃か通常攻撃かを決める)</summary>
        public bool ShouldUseStampAttack()
        {
            if (GetDistanceToTarget() < _stampAttackNearDistance)
            {
                return true;
            }
            return Random.value < _stampAttackProbability;
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

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            UnityEditor.Handles.color = Color.yellow;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, _searchRadius);
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, _attackRange);

            // 視野角の扇形を表示
            UnityEditor.Handles.color = new Color(1f, 1f, 0f, 0.15f);
            Vector3 baseDir = transform.forward * _searchRadius;
            Quaternion leftRot = Quaternion.AngleAxis(-_viewAngle * 0.5f, Vector3.up);
            UnityEditor.Handles.DrawSolidArc(transform.position, Vector3.up, leftRot * baseDir, _viewAngle, _searchRadius);
        }
#endif
    }
}
