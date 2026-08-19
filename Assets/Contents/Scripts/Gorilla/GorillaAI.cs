using ProjectKMP.Attack;
using R3;
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
        public const string ANIM_SWEEP_ATTACK  = "Attack"; // @todo 専用の薙ぎ払いモーションがあれば差し替える
        public const string ANIM_HIT           = "Hit";
        public const string ANIM_DEATH         = "Death";
        private const float ANIM_CROSSFADE = 0.15f;

        // @todo 動作確認用デバッグ入力を許可するシーン名。モデル確認用のサンドボックスシーンでのみ有効にする
        // デバッグ入力(1/K/O/P/Lキーなど)を有効にするシーン名一覧。
        // 誤操作でモーションが暴発しないよう、モデル確認用サンドボックス(ModelCheck)のみで有効にする
        private static readonly string[] DEBUG_INPUT_SCENE_NAMES = { "ModelCheck" };

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
        [SerializeField] private float _sweepAttackStaggerTime = 0.8f;
        [SerializeField, Range(0f, 1f), Tooltip("スタンプ攻撃以外が選ばれたとき、通常攻撃(頭突き)ではなく薙ぎ払い攻撃を選ぶ確率")]
        private float _sweepAttackProbability = 0.7f;

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

        [SerializeField, Min(0), Tooltip("薙ぎ払い攻撃のダメージ")]
        private int _sweepAttackDamage = 25;

        [SerializeField, Min(0f), Tooltip("薙ぎ払い攻撃の当たり判定が届く距離(メートル、体の中心から)")]
        private float _sweepAttackHitRange = 6.0f;

        [SerializeField, Range(0f, 360f), Tooltip("薙ぎ払い攻撃の当たり判定の角度(度)。正面を中心とした扇形。通常攻撃より広く取る")]
        private float _sweepAttackHitAngle = 220.0f;

        [SerializeField, Min(0f), Tooltip("薙ぎ払い攻撃が命中したときの吹き飛び距離(メートル)。通常の被弾よりずっと大きく吹き飛ばす")]
        private float _sweepAttackKnockbackDistance = 10.0f;

        [SerializeField, Min(0.01f), Tooltip("薙ぎ払い攻撃の吹き飛びにかける時間(秒)。距離が大きいぶん、通常の被弾より長めにして自然に見せる")]
        private float _sweepAttackKnockbackDurationSec = 0.6f;

        [SerializeField, Min(0f), Tooltip("薙ぎ払い攻撃で吹き飛ぶときに、上空へ浮き上がる高さ(メートル)。0だと水平にしか吹き飛ばない。正の値で放物線を描いて宙を巻き込むように吹き飛ぶ")]
        private float _sweepAttackKnockbackArcHeight = 4.0f;

        [SerializeField, Min(0f), Tooltip("薙ぎ払い攻撃の当たり判定(SweepAttackHitRange)のうち、ゴリラ本体からこの距離以内で命中した相手は挟み潰し候補になる(SweepAttackPalmCrushAngleの角度条件も参照)。これより遠く(当たり判定の外縁)でギリギリ当たった場合は、挟みきれず吹き飛ぶだけになる")]
        private float _sweepAttackPalmCrushRadius = 5.5f;

        [SerializeField, Range(0f, 180f), Tooltip("薙ぎ払い攻撃で挟み潰し(ぺっちゃんこ)になるために許容する、正面からの角度(度)。両手のひらが閉じるのは正面付近だけなので、これより横にズレて当たった場合(=片手だけ当たった場合)は挟み潰さず吹き飛ばすだけにする")]
        private float _sweepAttackPalmCrushAngleDeg = 30.0f;

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

        // ---- 未発見時の被弾リアクション ----
        [Header("未発見時の被弾リアクション")]
        [SerializeField, Tooltip("待機中・徘徊中(未発見)に攻撃を受けたとき、犬の方へ振り向く速さ(度/秒)。通常の旋回速度より遅くして、じわっと振り向く演出にする")]
        private float _hitReactionTurnSpeedDeg = 240.0f;

        /// <summary>未発見時に被弾し、犬の方へ振り向いている最中かどうか</summary>
        private bool _isTurningToAttacker;

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

        // ---- 薙ぎ払い攻撃の予備動作・エフェクト ----
        [Header("薙ぎ払い攻撃")]
        [SerializeField, Tooltip("振りかぶり中に体に出すチャージエフェクト。未設定なら出さない")]
        private GameObject _sweepAttackChargeEffectPrefab;
        [SerializeField, Tooltip("チャージエフェクトを出す高さ(足元からのオフセット、メートル)")]
        private float _sweepAttackChargeEffectHeight = 1.2f;
        [SerializeField, Tooltip("振りかぶり中に出す「力を溜めている感」のあるオーラエフェクト(パーティクル不使用、メッシュ+加算シェーダーで表現)。未設定なら出さない")]
        private GameObject _sweepAttackChargeAuraEffectPrefab;
        [SerializeField, Min(0.01f), Tooltip("チャージオーラエフェクトの大きさ倍率。1で原寸")]
        private float _sweepAttackChargeAuraEffectScale = 1.0f;
        [SerializeField, Tooltip("チャージオーラエフェクトを出す高さ(足元からのオフセット、メートル)。体を包み込むように中心付近に出す")]
        private float _sweepAttackChargeAuraHeight = 1.2f;
        [SerializeField, Min(0.01f), Tooltip("チャージオーラエフェクトを手のひらにも重ねて出すときの大きさ倍率。体用のSweepAttackChargeAuraEffectScaleとは別に、手のひらを包む小さめのサイズを指定する")]
        private float _sweepAttackHandAuraEffectScale = 0.25f;
        [SerializeField, Tooltip("チャージオーラエフェクト内の「上昇する光の線」部分だけ、さらに上へずらす高さ(メートル)。魔法陣本体の位置は変えない")]
        private float _sweepAttackChargeAuraRiseLineHeightOffset = 0.5f;
        [SerializeField, Tooltip("薙ぎ払いエフェクト代わりに使う拳モデル(SimpleHandsのプレハブ)。振り切り中、正面から反対側まで弧を描くように動かす")]
        private GameObject _sweepFistEffectPrefab;
        [SerializeField, Tooltip("拳モデルを出す高さ(足元からのオフセット、メートル)")]
        private float _sweepFistEffectHeight = 0.3f;
        [SerializeField, Tooltip("拳モデルを出す前方オフセット(メートル)")]
        private float _sweepFistEffectForwardOffset = 2.0f;
        [SerializeField, Min(0.01f), Tooltip("拳モデルの大きさ倍率。1で原寸")]
        private float _sweepFistEffectScale = 1.0f;
        [SerializeField, Min(0.01f), Tooltip("拳モデルの厚み(高さ方向)だけにかける追加倍率。SimpleHandsの元モデルが平たいため、SweepFistEffectScaleとは別に厚みだけ膨らませる用")]
        private float _sweepFistEffectThicknessScale = 3.0f;
        [SerializeField, Tooltip("両拳が正面で当たる瞬間(命中タイミング)に、両拳の間へ出すインパクトエフェクト。未設定なら出さない")]
        private GameObject _sweepImpactEffectPrefab;
        [SerializeField, Min(0.01f), Tooltip("インパクトエフェクトの大きさ倍率。1で原寸")]
        private float _sweepImpactEffectScale = 1.0f;
        [SerializeField, Tooltip("命中エフェクトに重ねて出す、より衝撃感のある2つ目のインパクトエフェクト。未設定なら出さない")]
        private GameObject _sweepImpactEffectPrefab2;
        [SerializeField, Min(0.01f), Tooltip("2つ目のインパクトエフェクトの大きさ倍率。1で原寸")]
        private float _sweepImpactEffectScale2 = 1.0f;

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
        private float _teamPowerStunRemain;

        // ---- 未発見時の被弾リアクション ----
        private HitTarget _hitTarget;
        private System.IDisposable _hitSubscription;

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
        public float SweepAttackStaggerTime => _sweepAttackStaggerTime;

        // ---- 攻撃の当たり判定・ダメージの公開API ----
        public int NormalAttackDamage => _normalAttackDamage;
        public float NormalAttackHitRange => _normalAttackHitRange;
        public float NormalAttackHitAngle => _normalAttackHitAngle;
        public int StampAttackDamage => _stampAttackDamage;
        public float StampAttackRadius => _stampAttackRadius;
        public int SweepAttackDamage => _sweepAttackDamage;
        public float SweepAttackHitRange => _sweepAttackHitRange;
        public float SweepAttackHitAngle => _sweepAttackHitAngle;
        public float SweepAttackKnockbackDistance => _sweepAttackKnockbackDistance;
        public float SweepAttackKnockbackDurationSec => _sweepAttackKnockbackDurationSec;
        public float SweepAttackKnockbackArcHeight => _sweepAttackKnockbackArcHeight;
        public float SweepAttackPalmCrushRadius => _sweepAttackPalmCrushRadius;
        public float SweepAttackPalmCrushAngleDeg => _sweepAttackPalmCrushAngleDeg;
        public GameObject SweepAttackChargeEffectPrefab => _sweepAttackChargeEffectPrefab;
        public float SweepAttackChargeEffectHeight => _sweepAttackChargeEffectHeight;
        public GameObject SweepAttackChargeAuraEffectPrefab => _sweepAttackChargeAuraEffectPrefab;
        public float SweepAttackChargeAuraEffectScale => _sweepAttackChargeAuraEffectScale;
        public float SweepAttackChargeAuraHeight => _sweepAttackChargeAuraHeight;
        public float SweepAttackHandAuraEffectScale => _sweepAttackHandAuraEffectScale;
        public float SweepAttackChargeAuraRiseLineHeightOffset => _sweepAttackChargeAuraRiseLineHeightOffset;
        public GameObject SweepFistEffectPrefab => _sweepFistEffectPrefab;
        public float SweepFistEffectHeight => _sweepFistEffectHeight;
        public float SweepFistEffectForwardOffset => _sweepFistEffectForwardOffset;
        public float SweepFistEffectScale => _sweepFistEffectScale;
        public float SweepFistEffectThicknessScale => _sweepFistEffectThicknessScale;
        public GameObject SweepImpactEffectPrefab => _sweepImpactEffectPrefab;
        public float SweepImpactEffectScale => _sweepImpactEffectScale;
        public GameObject SweepImpactEffectPrefab2 => _sweepImpactEffectPrefab2;
        public float SweepImpactEffectScale2 => _sweepImpactEffectScale2;

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

        /// <summary>共有必殺中、AIを止めて大きくのけぞらせる。</summary>
        public void BeginTeamPowerStun(float durationSec)
        {
            if (_isDead) return;
            _teamPowerStunRemain = Mathf.Max(_teamPowerStunRemain, durationSec);
            PlayAnimation(ANIM_HIT);
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _animator.speed = _animationSpeed;

            // 気づいていない(待機中・徘徊中)ときに攻撃を受けたら、攻撃してきた犬の方へ振り向く
            _hitTarget = GetComponent<HitTarget>();
            if (_hitTarget != null)
            {
                _hitSubscription = _hitTarget.Hit.Subscribe(_ => OnHitWhileUnaware());
            }
        }

        private void OnDestroy()
        {
            _hitSubscription?.Dispose();
            _hitSubscription = null;
        }

        /// <summary>
        /// 被弾したときの通知。待機中・徘徊中(＝まだ犬に気づいていない)であれば、
        /// その場で即座に「気づいた」扱いにして追跡ステートへ移行し、あわせて
        /// 攻撃してきた犬の方へ振り向くフラグを立てる。実際の回頭は Update() で毎フレーム
        /// 少しずつ行う(即座に向き直すと不自然なので、素早いがゆっくり振り向く演出にする)。
        /// </summary>
        private void OnHitWhileUnaware()
        {
            if (_isDead) return;
            if (_target == null) return;
            if (!(_currentState is GorillaStateIdle || _currentState is GorillaStatePatrol)) return;

            _isTurningToAttacker = true;

            // 「気づいていない」を今の被弾で終わらせ、即座に追跡へ移行する。
            // (振り向き自体はここではなく UpdateHitReactionTurn が毎フレーム進める)
            ChangeState(new GorillaStateChase());
        }

        /// <summary>
        /// 未発見時の被弾リアクションで振り向いている最中なら、毎フレーム少しずつ犬の方へ回頭する。
        /// OnHitWhileUnaware で追跡ステートへ切り替えた直後の1瞬だけ、通常の旋回速度より
        /// 速く・ヒットストップの影響を受けずに向き直すための演出なので、対象を見失うか
        /// ほぼ向き終えたら自動的に終了する。
        /// </summary>
        private void UpdateHitReactionTurn()
        {
            if (!_isTurningToAttacker) return;

            if (_isDead || _target == null)
            {
                _isTurningToAttacker = false;
                return;
            }

            Vector3 direction = _target.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                _isTurningToAttacker = false;
                return;
            }

            Quaternion look = Quaternion.LookRotation(direction.normalized);
            // ヒットストップ(Time.timeScaleを一瞬落とす演出)に巻き込まれて振り向きが遅く見えないよう、
            // スケールされない実時間で回頭させる
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _hitReactionTurnSpeedDeg * Time.unscaledDeltaTime);

            if (Quaternion.Angle(transform.rotation, look) < 1.0f)
            {
                _isTurningToAttacker = false;
            }
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
            if (_teamPowerStunRemain > 0.0f)
            {
                _teamPowerStunRemain -= Time.unscaledDeltaTime;
                if (_teamPowerStunRemain <= 0.0f && !_isDead) PlayAnimation(ANIM_IDLE);
                return;
            }

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
            UpdateHitReactionTurn();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // @todo 動作確認用デバッグ入力。ModelCheckシーン(モデル確認用のサンドボックス)でのみ有効にする。
            // 他のシーン(InGameなど)では誤操作でモーションが暴発しないようにする
            string activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool isDebugInputAllowedScene = false;
            foreach (var sceneName in DEBUG_INPUT_SCENE_NAMES)
            {
                if (activeSceneName == sceneName) { isDebugInputAllowedScene = true; break; }
            }
            if (!isDebugInputAllowedScene)
            {
                return;
            }

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

            // @todo 動作確認用デバッグ入力。Kキーで薙ぎ払い攻撃モーションを再生する
            if (keyboard != null && keyboard.kKey.wasPressedThisFrame && !_isDead)
            {
                ChangeState(new GorillaStateSweepAttack());
            }

            // @todo 動作確認用デバッグ入力。1キーでも薙ぎ払い攻撃モーションを再生する(テスト用の別キー)
            if (keyboard != null && keyboard.digit1Key.wasPressedThisFrame && !_isDead)
            {
                ChangeState(new GorillaStateSweepAttack());
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

        /// <summary>
        /// 攻撃タイプ判定(近距離 or 確率でスタンプ攻撃か通常攻撃かを決める)。
        /// 通常攻撃(頭突き)は正面の扇形にしか当たらないため、対象が背後など扇形の外にいるときは
        /// 振り向いても間に合わない(=不発になる)ことを避けるため、向き不問のスタンプ攻撃を強制する。
        /// これにより「背後に回り込めば一方的に殴れる」抜け道を塞ぐ。
        /// </summary>
        public bool ShouldUseStampAttack()
        {
            if (GetDistanceToTarget() < _stampAttackNearDistance)
            {
                return true;
            }

            // 薙ぎ払い(側面まで届く広い扇形)でも捉えられないほど真後ろにいるときだけ、
            // スタンプ攻撃(向き不問)を強制する。側面(通常攻撃の外・薙ぎ払いの内)は
            // GorillaStateChase側で薙ぎ払い攻撃を強制するため、ここでは弾かない
            if (IsTargetOutsideSweepAttackCone())
            {
                return true;
            }

            return Random.value < _stampAttackProbability;
        }

        /// <summary>スタンプ攻撃以外が選ばれたとき、通常攻撃(頭突き)ではなく薙ぎ払い攻撃を選ぶかどうかの確率判定</summary>
        public bool ShouldUseSweepAttack()
        {
            return Random.value < _sweepAttackProbability;
        }

        /// <summary>対象が通常攻撃の命中扇形(正面 ±NormalAttackHitAngle/2)の外にいるか</summary>
        public bool IsTargetOutsideNormalAttackCone()
        {
            if (_target == null) return false;

            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return false;

            float angle = Vector3.Angle(transform.forward, toTarget.normalized);
            return angle > _normalAttackHitAngle * 0.5f;
        }

        /// <summary>対象が薙ぎ払い攻撃の命中扇形(正面 ±SweepAttackHitAngle/2)の外(=ほぼ真後ろ)にいるか</summary>
        private bool IsTargetOutsideSweepAttackCone()
        {
            if (_target == null) return false;

            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return false;

            float angle = Vector3.Angle(transform.forward, toTarget.normalized);
            return angle > _sweepAttackHitAngle * 0.5f;
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
