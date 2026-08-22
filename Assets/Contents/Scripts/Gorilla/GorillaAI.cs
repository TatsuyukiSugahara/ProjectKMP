using Photon.Pun;
using ProjectKMP.Attack;
using R3;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// ゴリラ敵AI本体（ステートパターンのコンテキスト）。
    /// 待機→徘徊→追跡→攻撃範囲内?→攻撃タイプ判定→スタンプ攻撃/通常攻撃/破壊光線→硬直→再追跡、
    /// 見失ったら徘徊へ戻る、という一連の挙動を各ステートクラスに委譲して実行する。
    ///
    /// オンライン時、行き先や攻撃の種類を決めるのは MasterClient だけ。
    /// ゲストは GorillaNetworkSync 経由で配られた位置とステートを再生するだけにして、
    /// 全員の画面で同じゴリラが同じタイミングで動くようにする。
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
        public const string ANIM_ROAR          = "Attack"; // @todo 専用の咆哮モーションがあれば差し替える
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
        [SerializeField] private float _sweepAttackStaggerTime = 0.8f;
        [SerializeField, Range(0f, 1f), Tooltip("スタンプ攻撃以外が選ばれたとき、通常攻撃(頭突き)ではなく薙ぎ払い攻撃を選ぶ確率")]
        private float _sweepAttackProbability = 0.7f;

        // ---- 突進攻撃(中距離) ----
        [Header("突進攻撃(中距離)")]
        [SerializeField, Min(0f), Tooltip("この距離より近いと突進しない(近すぎると近接技を優先する)")]
        private float _chargeAttackMinRange = 3.5f;

        [SerializeField, Min(0f), Tooltip("この距離以内にいると突進を使える(メートル)")]
        private float _chargeAttackRange = 12.0f;

        [SerializeField, Range(0f, 1f), Tooltip("射程内かつクールタイム明けのとき、突進を選ぶ確率")]
        private float _chargeAttackProbability = 0.7f;

        [SerializeField, Min(0f), Tooltip("突進を使った後、再び使えるようになるまでのクールタイム(秒)")]
        private float _chargeAttackCooldownSec = 6.0f;

        [SerializeField, Min(0.05f), Tooltip("1回目の突進前の溜め時間(秒)。この後半で向きが固定されるので、横へ逃げれば避けられる")]
        private float _chargeWindupTime = 1.0f;

        [SerializeField, Min(0.05f), Tooltip("2回目以降の突進前の溜め時間(秒)。短くして畳みかける")]
        private float _chargeFollowUpWindupTime = 0.3f;

        [SerializeField, Range(0.1f, 1.0f), Tooltip("溜めのうち、ここを超えたら狙いを固定する割合。以降は曲がらず、予測表示が赤く速く点滅する")]
        private float _chargeAimLockRatio = 0.85f;

        [SerializeField, Min(0f), Tooltip("溜め中に相手を追う速さ(度/秒)。固定に近づくほど遅くなる")]
        private float _chargeAimTurnSpeedDeg = 120.0f;

        [SerializeField, Min(0f), Tooltip("溜めで後ろへ反る角度(度、X軸)。大きいほど溜めている感が出る")]
        private float _chargeLeanBackAngleDeg = 28.0f;

        [SerializeField, Min(0f), Tooltip("突進中に前傾する角度(度、X軸)。低い姿勢で突っ込む絵になる")]
        private float _chargeLeanForwardAngleDeg = 18.0f;

        [SerializeField, Min(0f), Tooltip("突進中に相手を追う速さ(度/秒)。大きすぎると横へ跳んでも避けられなくなる")]
        private float _chargeHomingSpeedDeg = 45.0f;

        [SerializeField, Min(0.05f), Tooltip("突進を走り切ってから次の溜めに入るまでの、急旋回の時間(秒)")]
        private float _chargeTurnTime = 0.4f;

        [SerializeField, Range(0.1f, 1.0f), Tooltip("突進を繰り返すたびに距離へ掛ける倍率。1未満にすると後の突進ほど短くなり、場外へ出にくくなる")]
        private float _chargeDistanceFalloff = 0.8f;

        [SerializeField, Tooltip("フェーズごとの突進の連続回数。要素0がフェーズ1")]
        private int[] _phaseChargeCounts = { 1, 2, 2, 3 };


        [SerializeField, Min(0.1f), Tooltip("突進の速さ(m/秒)。プレイヤーの走りより速くしないと捕まえられない")]
        private float _chargeSpeed = 11.0f;

        [SerializeField, Min(0.1f), Tooltip("突進で進む最大距離(メートル)")]
        private float _chargeMaxDistance = 14.0f;

        [SerializeField, Min(0), Tooltip("突進が命中したときのダメージ")]
        private int _chargeAttackDamage = 35;

        [SerializeField, Min(0.1f), Tooltip("突進の当たり判定の半径(メートル)。小さいほど横に避けやすい")]
        private float _chargeHitRadius = 1.6f;

        [SerializeField, Min(0f), Tooltip("突進が命中したときの吹き飛び距離(メートル)")]
        private float _chargeKnockbackDistance = 8.0f;

        [SerializeField, Min(0.01f), Tooltip("突進の吹き飛びにかける時間(秒)")]
        private float _chargeKnockbackDurationSec = 0.5f;

        [SerializeField, Min(0f), Tooltip("突進の吹き飛びで浮き上がる高さ(メートル)")]
        private float _chargeKnockbackArcHeight = 2.5f;

        [SerializeField, Min(0f), Tooltip("突進が命中したときの硬直時間(秒)")]
        private float _chargeHitStaggerTime = 1.0f;

        [SerializeField, Min(0f), Tooltip("突進を空振りしたときの硬直時間(秒)。ここがプレイヤーの反撃チャンスになるので長めに取る")]
        private float _chargeMissStaggerTime = 1.7f;

        [SerializeField, Tooltip("突進の溜め中に出すチャージエフェクト。未設定なら通常攻撃のものを使う")]
        private GameObject _chargeAttackChargeEffectPrefab;

        // ---- 岩投げ(遠距離) ----
        [Header("岩投げ(遠距離)")]
        [SerializeField, Min(0f), Tooltip("この距離より近いと岩を投げない(近距離・中距離の技を優先する)")]
        private float _rockThrowMinRange = 10.0f;

        [SerializeField, Min(0f), Tooltip("この距離以内にいると岩を投げられる(メートル)。遠くへ逃げても安全でなくするための技")]
        private float _rockThrowRange = 28.0f;

        [SerializeField, Range(0f, 1f), Tooltip("射程内かつクールタイム明けのとき、岩投げを選ぶ確率")]
        private float _rockThrowProbability = 0.6f;

        [SerializeField, Min(0f), Tooltip("岩を投げた後、再び使えるようになるまでのクールタイム(秒)")]
        private float _rockThrowCooldownSec = 7.0f;

        [SerializeField, Min(0.05f), Tooltip("掘り起こし～持ち上げ～溜めきりまでの合計時間(秒)。この間に予告の輪から出れば避けられる")]
        private float _rockThrowWindupTime = 1.2f;

        [SerializeField, Min(0f), Tooltip("掘り起こし中に相手を追う速さ(度/秒)。持ち上げ切ると狙いが固定される")]
        private float _rockThrowAimTurnSpeedDeg = 150.0f;

        [SerializeField, Min(0), Tooltip("岩が当たったときのダメージ")]
        private int _rockThrowDamage = 25;

        [SerializeField, Min(0.1f), Tooltip("着弾の衝撃が届く半径(メートル)。予告の輪の大きさもこれに合わせる")]
        private float _rockThrowRadius = 4.0f;

        [SerializeField, Min(0f), Tooltip("岩投げ後の硬直時間(秒)")]
        private float _rockThrowStaggerTime = 1.2f;

        [SerializeField, Tooltip("投げる岩のモデル。未設定なら岩が出ないので、投擲の見た目にならない")]
        private GameObject _rockThrowRockPrefab;

        [SerializeField, Min(0.1f), Tooltip("岩の大きさ倍率")]
        private float _rockThrowRockScale = 1.6f;

        [SerializeField, Min(0f), Tooltip("岩を掘り起こす位置を、正面方向にどれだけずらすか(メートル)")]
        private float _rockThrowDigForwardOffset = 2.2f;

        [SerializeField, Min(0f), Tooltip("岩を構える高さ(足元からのオフセット、メートル)")]
        private float _rockThrowHoldHeight = 3.2f;

        [SerializeField, Tooltip("岩を構える位置を、正面方向にどれだけずらすか(メートル)")]
        private float _rockThrowHoldForwardOffset = 0.6f;

        [SerializeField, Min(1f), Tooltip("岩が飛ぶ速さ(m/秒)。飛行時間はここと距離から決まる")]
        private float _rockThrowSpeed = 22.0f;

        [SerializeField, Min(0f), Tooltip("岩が山なりに飛ぶときの高さ(メートル)")]
        private float _rockThrowArcHeight = 5.0f;

        [SerializeField, Tooltip("着弾地点に残す痕(デカール)。未設定ならスタンプ攻撃のものを使う")]
        private ProjectKMP.Attack.AttackDecal _rockThrowDecalPrefab;

        [SerializeField, Tooltip("着弾の衝撃波エフェクト。未設定ならスタンプ攻撃のものを使う")]
        private GameObject _rockThrowImpactEffectPrefab;

        [SerializeField, Min(0.01f), Tooltip("着弾エフェクトの大きさ倍率")]
        private float _rockThrowImpactEffectScale = 0.35f;

        [SerializeField, Min(0), Tooltip("着弾したときに飛び散る岩のかけらの数。0なら砕けない")]
        private int _rockThrowDebrisCount = 7;

        [SerializeField, Min(0.01f), Tooltip("かけら1個の大きさ。岩本体より小さくする")]
        private float _rockThrowDebrisScale = 0.45f;

        [SerializeField, Min(0f), Tooltip("かけらが横へ散る速さ(m/秒)")]
        private float _rockThrowDebrisSpreadSpeed = 7.0f;

        [SerializeField, Min(0f), Tooltip("かけらが上へ跳ね上がる速さ(m/秒)")]
        private float _rockThrowDebrisUpSpeed = 6.5f;

        [SerializeField, Min(0.2f), Tooltip("かけらが消えるまでの時間(秒)")]
        private float _rockThrowDebrisLifetimeSec = 2.5f;

        [SerializeField, Tooltip("フェーズごとに一度に投げる岩の数。要素0がフェーズ1。2個以上で連投になる")]
        private int[] _phaseRockThrowCounts = { 1, 1, 2, 3 };

        // ---- 連続パンチ(中距離) ----
        [Header("連続パンチ(中距離)")]
        [SerializeField, Min(0f), Tooltip("この距離より近いと連続パンチを使わない(近接技を優先する)")]
        private float _rushPunchMinRange = 2.5f;

        [SerializeField, Min(0f), Tooltip("この距離以内にいると連続パンチを使える(メートル)")]
        private float _rushPunchRange = 8.0f;

        [SerializeField, Range(0f, 1f), Tooltip("射程内かつクールタイム明けのとき、連続パンチを選ぶ確率")]
        private float _rushPunchProbability = 0.5f;

        [SerializeField, Min(0f), Tooltip("連続パンチを使った後のクールタイム(秒)")]
        private float _rushPunchCooldownSec = 8.0f;

        [SerializeField, Min(0.05f), Tooltip("打ち始める前の構えの時間(秒)")]
        private float _rushPunchWindupTime = 0.5f;

        [SerializeField, Min(0f), Tooltip("詰め寄るときの最大速度(m/秒)。逃げる相手に置いていかれない速さにする。近づくと自動で減速する")]
        private float _rushPunchSpeed = 7.5f;

        [SerializeField, Min(0.05f), Tooltip("1発あたりの時間(秒)。短いほど手数が多く見える")]
        private float _rushPunchInterval = 0.6f;

        [SerializeField, Min(0), Tooltip("1発あたりのダメージ。連打なので1発は軽くする")]
        private int _rushPunchDamage = 12;

        [SerializeField, Min(0.1f), Tooltip("拳が届く距離(メートル、体の中心から)")]
        private float _rushPunchReach = 3.2f;

        [SerializeField, Min(0.1f), Tooltip("拳の先の当たり判定の半径(メートル)")]
        private float _rushPunchHitRadius = 1.8f;

        [SerializeField, Min(0f), Tooltip("連打の途中1発あたりの吹き飛び距離(メートル)。押し出すと自分で間合いを崩すので短くする")]
        private float _rushPunchKnockbackDistance = 0.9f;

        [SerializeField, Min(0f), Tooltip("打ちながら相手を追う速さ(度/秒)。大きすぎると横へ回っても抜けられなくなる")]
        private float _rushPunchHomingSpeedDeg = 60.0f;

        [SerializeField, Min(0f), Tooltip("打ち終わった後の硬直時間(秒)。1発も当たらなかった場合はさらに長くなる")]
        private float _rushPunchStaggerTime = 1.4f;

        [SerializeField, Min(0.5f), Tooltip("打っている間に保とうとする相手との距離(メートル)。遠ければ詰め、近すぎれば少し下がる")]
        private float _rushPunchKeepDistance = 2.6f;

        [SerializeField, Min(0f), Tooltip("1発ごとに前へ踏み込む距離(メートル)。体が入ることで殴っている感じが出る")]
        private float _rushPunchLungeDistance = 0.55f;

        [SerializeField, Min(1f), Tooltip("締めの1発のダメージ倍率")]
        private float _rushPunchFinishDamageMultiplier = 2.2f;

        [SerializeField, Min(0f), Tooltip("締めの1発の吹き飛び距離(メートル)")]
        private float _rushPunchFinishKnockbackDistance = 6.0f;

        [SerializeField, Tooltip("フェーズごとの連打数。要素0がフェーズ1")]
        private int[] _phaseRushPunchCounts = { 4, 5, 6, 8 };

        [SerializeField, Tooltip("フェーズごとの1発の間隔の倍率。小さいほど速く殴る。要素0がフェーズ1。激昂(フェーズ3)から一気に速くして、見た目の変化と手強さを揃える")]
        private float[] _rushPunchIntervalPhaseMultipliers = { 1.0f, 0.9f, 0.5f, 0.45f };

        // ---- 連続パンチで使う拳 ----
        [SerializeField, Tooltip("連打で使う握り拳のモデル。未設定なら薙ぎ払いの手(開いた手)で代用する")]
        private GameObject _rushPunchFistPrefab;

        [SerializeField, Min(0.05f), Tooltip("拳のモデルの大きさ(ワールド基準のメートル)。ゴリラ本体の拡大率とは無関係に一定になる")]
        private float _rushPunchFistScale = 1.1f;

        [SerializeField, Min(0f), Tooltip("拳を構える高さ(ワールド基準のメートル、足元から)。胸の高さに合わせる")]
        private float _rushPunchFistHeight = 1.5f;

        // ---- 拳の付け根(ゴリラの実際の手の位置) ----
        [Header("拳の付け根")]
        [SerializeField, Tooltip("拳の構え位置をゴリラの前脚ボーンの先(実際の手の位置)に合わせる。切ると従来の固定位置になる")]
        private bool _useHandBoneAnchor = true;

        [SerializeField, Tooltip("右手にあたるボーンの名前")]
        private string _rightHandBoneName = "leg.F.R";

        [SerializeField, Tooltip("左手にあたるボーンの名前")]
        private string _leftHandBoneName = "leg.F.L";

        [SerializeField, Min(0f), Tooltip("前脚ボーンの根元から手先までの長さ(ゴリラのローカル単位)。ボーンは腕の付け根にあり、手先は+Y方向のこの距離")]
        private float _handBoneLength = 0.29f;

        // ---- 跳びかかり(遠中距離) ----
        [Header("跳びかかり(遠中距離)")]
        [SerializeField, Min(0f), Tooltip("この距離より近いと跳びかからない(近接技を優先する)")]
        private float _pounceMinRange = 7.0f;

        [SerializeField, Min(0f), Tooltip("この距離以内にいると跳びかかれる(メートル)")]
        private float _pounceRange = 20.0f;

        [SerializeField, Range(0f, 1f), Tooltip("射程内かつクールタイム明けのとき、跳びかかりを選ぶ確率")]
        private float _pounceProbability = 0.5f;

        [SerializeField, Min(0f), Tooltip("跳びかかりを使った後のクールタイム(秒)")]
        private float _pounceCooldownSec = 9.0f;

        [SerializeField, Min(0.05f), Tooltip("跳ぶ前に沈み込む時間(秒)。この間に着地点の輪が出る")]
        private float _pounceWindupTime = 0.7f;

        [SerializeField, Min(0.05f), Tooltip("跳んでから着地するまでの時間(秒)")]
        private float _pounceLeapDurationSec = 0.75f;

        [SerializeField, Min(0.1f), Tooltip("跳ぶ高さ(メートル)")]
        private float _pounceJumpHeight = 8.0f;

        [SerializeField, Min(1f), Tooltip("跳べる最大距離(メートル)。これより遠い相手には届く範囲までしか跳ばない")]
        private float _pounceMaxDistance = 18.0f;

        [SerializeField, Min(0), Tooltip("着地の踏み潰しダメージ")]
        private int _pounceDamage = 32;

        [SerializeField, Min(0.1f), Tooltip("着地の衝撃が届く半径(メートル)。予告の輪の大きさもこれに合わせる")]
        private float _pounceRadius = 5.0f;

        [SerializeField, Min(0f), Tooltip("着地で吹き飛ぶ距離(メートル)")]
        private float _pounceKnockbackDistance = 6.0f;

        [SerializeField, Min(0f), Tooltip("着地後の硬直時間(秒)")]
        private float _pounceStaggerTime = 1.5f;

        [SerializeField, Min(0f), Tooltip("着地でカメラを揺らす強さ")]
        private float _pounceCameraShake = 0.45f;

        [SerializeField, Tooltip("着地で飛び散る地面のかけら。未設定なら岩投げの岩を使う")]
        private GameObject _pounceDebrisPrefab;

        [SerializeField, Min(0), Tooltip("着地で飛び散るかけらの数。0なら飛ばさない")]
        private int _pounceDebrisCount = 12;

        [SerializeField, Min(0.01f), Tooltip("かけら1個の大きさ")]
        private float _pounceDebrisScale = 0.55f;

        // ---- 地割れ(中遠距離) ----
        [Header("地割れ(中遠距離)")]
        [SerializeField, Min(0f), Tooltip("この距離より近いと地割れを使わない")]
        private float _fissureMinRange = 4.0f;

        [SerializeField, Min(0f), Tooltip("この距離以内にいると地割れを使える(メートル)")]
        private float _fissureRange = 24.0f;

        [SerializeField, Range(0f, 1f), Tooltip("射程内かつクールタイム明けのとき、地割れを選ぶ確率")]
        private float _fissureProbability = 0.5f;

        [SerializeField, Min(0f), Tooltip("地割れを使った後のクールタイム(秒)")]
        private float _fissureCooldownSec = 8.0f;

        [SerializeField, Min(0.05f), Tooltip("拳を振り上げて溜める時間(秒)")]
        private float _fissureWindupTime = 0.8f;

        [SerializeField, Range(0.1f, 1f), Tooltip("溜めのうち、ここを超えたら向きを固定する割合")]
        private float _fissureAimLockRatio = 0.7f;

        [SerializeField, Min(0f), Tooltip("溜め中に相手を追う速さ(度/秒)")]
        private float _fissureAimTurnSpeedDeg = 120.0f;

        [SerializeField, Min(1f), Tooltip("裂け目が走る速さ(m/秒)")]
        private float _fissureSpeed = 26.0f;

        [SerializeField, Min(1f), Tooltip("裂け目が届く長さ(メートル)")]
        private float _fissureLength = 22.0f;

        [SerializeField, Min(0.1f), Tooltip("裂け目の当たり判定の太さ(半径、メートル)")]
        private float _fissureWidth = 1.8f;

        [SerializeField, Min(0), Tooltip("裂け目に当たったときのダメージ")]
        private int _fissureDamage = 22;

        [SerializeField, Min(0f), Tooltip("裂け目に当たったときの吹き飛び距離(メートル)")]
        private float _fissureKnockbackDistance = 4.0f;

        [SerializeField, Min(0f), Tooltip("裂け目に当たったときに打ち上げられる高さ(メートル)。突き上げられる形にする")]
        private float _fissureKnockbackArcHeight = 4.5f;

        [SerializeField, Min(0f), Tooltip("地割れ後の硬直時間(秒)")]
        private float _fissureStaggerTime = 1.4f;

        [SerializeField, Min(0f), Tooltip("複数本に分かれるときの、隣り合う裂け目の角度差(度)")]
        private float _fissureSpreadAngleDeg = 26.0f;

        [SerializeField, Tooltip("フェーズごとの裂け目の本数。要素0がフェーズ1")]
        private int[] _phaseFissureCounts = { 1, 1, 2, 3 };

        [SerializeField, Min(0f), Tooltip("地割れでカメラを揺らす強さ")]
        private float _fissureCameraShake = 0.5f;

        [SerializeField, Tooltip("裂け目の痕(デカール)。未設定ならスタンプ攻撃のものを使う")]
        private ProjectKMP.Attack.AttackDecal _fissureDecalPrefab;

        [SerializeField, Tooltip("割れ目から突き上がる岩。未設定なら岩は出ず、痕だけが残る")]
        private GameObject _fissureSpikePrefab;

        [SerializeField, Min(0.1f), Tooltip("突き上がる岩の大きさ")]
        private float _fissureSpikeScale = 1.4f;

        [SerializeField, Min(0.02f), Tooltip("岩が生えきるまでの時間(秒)")]
        private float _fissureSpikeRiseSec = 0.12f;

        [SerializeField, Min(0f), Tooltip("岩が残っている時間(秒)。この後で地面へ沈んで消える")]
        private float _fissureSpikeLifetimeSec = 2.1f;

        [SerializeField, Min(0f), Tooltip("両手を振り上げる高さ(ワールド基準のメートル、足元から)。ゴリラの背丈より少し上に振り翳す")]
        private float _fissureHandRaiseHeight = 3.9f;

        [SerializeField, Min(0f), Tooltip("振り下ろした拳を置く前方オフセット(ワールド基準のメートル)")]
        private float _fissureHandForwardOffset = 1.8f;

        [SerializeField, Min(0.1f), Tooltip("振り上げる拳のモデルの大きさ(ワールド基準のメートル)。ゴリラ本体の拡大率とは無関係に一定になる")]
        private float _fissureHandScale = 1.7f;

        [SerializeField, Min(0), Tooltip("拳が地面に着いた瞬間に跳ね上げる土のかけらの数")]
        private int _fissureDebrisCount = 8;

        // ---- 掴み(近距離・1人狙い撃ち) ----
        [Header("掴み(近距離)")]
        [SerializeField, Tooltip("掴みを行動に混ぜるか。オフにすると掴みを選ばなくなる。技そのものの処理は残してあるので、ここをオンにすればいつでも戻せる")]
        private bool _useGrab = false;

        [SerializeField, Min(0f), Tooltip("この距離以内にいると掴みにいける(メートル)")]
        private float _grabReach = 4.5f;

        [SerializeField, Range(0f, 360f), Tooltip("掴める範囲の角度(度)。正面を中心とした扇形")]
        private float _grabAngleDeg = 70.0f;

        [SerializeField, Range(0f, 1f), Tooltip("射程内かつクールタイム明けのとき、掴みを選ぶ確率")]
        private float _grabProbability = 0.35f;

        [SerializeField, Min(0f), Tooltip("掴みを使った後のクールタイム(秒)。頻発すると理不尽になるので長めに取る")]
        private float _grabCooldownSec = 14.0f;

        [SerializeField, Min(0.05f), Tooltip("手を構えて狙う時間(秒)")]
        private float _grabWindupTime = 0.6f;

        [SerializeField, Range(0.1f, 1f), Tooltip("溜めのうち、ここを超えたら狙いを固定する割合")]
        private float _grabAimLockRatio = 0.65f;

        [SerializeField, Min(0f), Tooltip("溜め中に相手を追う速さ(度/秒)")]
        private float _grabAimTurnSpeedDeg = 150.0f;

        [SerializeField, Min(0.5f), Tooltip("掴んだまま拘束する時間(秒)。仲間が助けに来られる長さにする")]
        private float _grabHoldSec = 4.0f;

        [SerializeField, Min(0), Tooltip("この量だけボスにダメージを与えると掴みが解ける。0なら救出できない")]
        private int _grabRescueDamage = 60;

        [SerializeField, Min(0f), Tooltip("掴んだ相手を持ち上げる高さ(足元からのオフセット、メートル)")]
        private float _grabHoldHeight = 3.0f;

        [SerializeField, Min(0f), Tooltip("掴んだ相手を構える位置の前方オフセット(メートル)")]
        private float _grabHoldForwardOffset = 2.0f;

        [SerializeField, Min(0), Tooltip("時間切れで叩きつけたときのダメージ。掴まれ切ると痛いようにする")]
        private int _grabSlamDamage = 45;

        [SerializeField, Min(0f), Tooltip("叩きつけたときの吹き飛び距離(メートル)")]
        private float _grabSlamKnockbackDistance = 7.0f;

        [SerializeField, Min(0f), Tooltip("掴み後の硬直時間(秒)。空振りのときはさらに長くなる")]
        private float _grabStaggerTime = 1.6f;

        [SerializeField, Tooltip("掴む手のモデル。未設定なら手が出ず、掴んでいる絵にならない")]
        private GameObject _grabHandPrefab;

        [SerializeField, Min(0.1f), Tooltip("掴む手のモデルの大きさ")]
        private float _grabHandScale = 1.6f;

        [SerializeField, Min(0), Tooltip("握り締めるたびに入るダメージ(1秒ごと)。掴まれ続けると危ないと分からせる")]
        private int _grabSqueezeDamage = 6;

        [SerializeField, Min(0), Tooltip("掴まれた本人が自力で抜け出すのに必要な攻撃ボタンの回数。0なら自力脱出できない")]
        private int _grabEscapeMashCount = 12;

        // ---- 咆哮 ----
        [Header("咆哮")]
        [SerializeField, Min(1f), Tooltip("空気の振動が届く半径(メートル)。この中にいると吹き飛ばされる")]
        private float _roarRadius = 14.0f;

        [SerializeField, Min(1f), Tooltip("振動が広がる速さ(m/秒)。速いほど鋭い衝撃に見える")]
        private float _roarWaveSpeed = 26.0f;

        [SerializeField, Min(0), Tooltip("咆哮のダメージ。倒すための技ではないので小さくする")]
        private int _roarDamage = 5;

        [SerializeField, Min(0f), Tooltip("咆哮で吹き飛ぶ距離(メートル)。近いほどこの値に近づく")]
        private float _roarKnockbackDistance = 9.0f;

        [SerializeField, Min(0.01f), Tooltip("咆哮の吹き飛びにかける時間(秒)")]
        private float _roarKnockbackDurationSec = 0.55f;

        [SerializeField, Min(0f), Tooltip("咆哮で吹き飛ぶときに浮き上がる高さ(メートル)")]
        private float _roarKnockbackArcHeight = 3.5f;

        [SerializeField, Min(0f), Tooltip("咆哮でカメラを揺らす強さ")]
        private float _roarCameraShake = 0.6f;

        // ---- 攻撃予測の表示 ----
        [Header("攻撃予測の表示")]
        [SerializeField, Tooltip("攻撃の当たる範囲を溜め中に地面へ出す表示。全ての攻撃で共通して使う。未設定なら表示しない")]
        private GorillaAttackTelegraph _attackTelegraphPrefab;

        [SerializeField, Tooltip("近接攻撃(頭突き・薙ぎ払い・スタンプ)でも予測を出す。近すぎて画面に入らない場合は切る")]
        private bool _showMeleeTelegraph = true;

        // ---- フェーズ(HPが減るほど激しくなる) ----
        [Header("フェーズ(HPが減るほど激しくなる)")]
        [SerializeField, Tooltip("HPの割合でフェーズを上げる。残りHPがこの値を下回るとフェーズが1つ進む。大きい順に並べる")]
        private float[] _phaseHpThresholds = { 0.75f, 0.5f, 0.25f };

        [SerializeField, Tooltip("フェーズごとの移動速度倍率。要素0がフェーズ1")]
        private float[] _phaseSpeedMultipliers = { 1.0f, 1.15f, 1.3f, 1.5f };

        [SerializeField, Tooltip("フェーズごとの硬直時間倍率。小さいほど手数が増える。要素0がフェーズ1")]
        private float[] _phaseStaggerMultipliers = { 1.0f, 0.85f, 0.7f, 0.55f };

        [SerializeField, Tooltip("フェーズごとの頭突きの連撃数。要素0がフェーズ1")]
        private int[] _phaseNormalAttackComboCounts = { 1, 2, 2, 3 };

        [SerializeField, Tooltip("フェーズが上がった瞬間に咆哮する")]
        private bool _roarOnPhaseUp = true;

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
        private float _beamAttackRange = 12.0f;
        [SerializeField, Range(0f, 1f), Tooltip("射程内かつクールタイム明けのとき、破壊光線を選ぶ確率")]
        private float _beamAttackProbability = 0.5f;
        [SerializeField, Tooltip("破壊光線を撃った後、再び使えるようになるまでのクールタイム(秒)")]
        private float _beamAttackCooldownSec = 10.0f;
        [SerializeField, Tooltip("発射前の予備動作(狙い)の時間(秒)")]
        private float _beamWindupTime = 3.0f;
        [SerializeField, Tooltip("光線を出し続ける時間(秒)")]
        private float _beamDuration = 1.4f;
        [SerializeField, Min(1), Tooltip("1回の破壊光線で何発撃つか。2以上で狙いを付け直して連射する")]
        private int _beamShotCount = 2;

        [SerializeField, Min(0.05f), Tooltip("連射のとき、次の1発までに狙いを付け直す時間(秒)")]
        private float _beamReaimTime = 0.55f;

        [SerializeField, Range(0f, 1f), Tooltip("溜めのどこで狙いを固定するか。ここを過ぎると光線はもう曲がらない")]
        private float _beamAimLockRatio = 0.75f;

        [SerializeField, Tooltip("光線終了後、硬直ステートに留まる時間(秒)")]
        private float _beamStaggerTime = 1.4f;
        [SerializeField, Tooltip("光線が届く距離(メートル)")]
        private float _beamLength = 16.0f;
        [SerializeField, Min(0f), Tooltip("発射開始時、光線が0から実際の長さまで伸びきるのにかかる時間(秒)。0にすると一瞬で全長になる")]
        private float _beamGrowDuration = 0.08f;
        [SerializeField, Tooltip("光線の当たり判定の太さ(半径、メートル)")]
        private float _beamWidth = 3.0f;
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
        private float _beamDecalIntervalMeters = 2.1f;
        [SerializeField, Min(0.01f), Tooltip("光線の痕の大きさ倍率。1で光線の太さと同じ直径になり、大きくするほど太さより広がる")]
        private float _beamDecalWidthScale = 1.2f;

        private float _beamCooldownRemain;
        private float _chargeCooldownRemain;
        private float _rockThrowCooldownRemain;
        private Transform _rightHandBone;
        private Transform _leftHandBone;

        private float _rushPunchCooldownRemain;
        private float _pounceCooldownRemain;
        private float _fissureCooldownRemain;
        private float _grabCooldownRemain;

        /// <summary>掴んでいるプレイヤーの ActorNumber。誰も掴んでいなければ NO_GRAB</summary>
        private int _grabbedActorNumber = NO_GRAB;

        /// <summary>掴まれた本人が連打で抜け出したいと申し出ているか</summary>
        private bool _escapeRequested;

        // ---- フェーズ ----
        private ProjectKMP.Monster.BossHealth _bossHealth;
        private GorillaTargetSelector _targetSelector;
        private int _phase = 1;

        // ---- 内部状態 ----
        private Animator _animator;
        private IGorillaState _currentState;
        private GorillaStateKind _currentStateKind = GorillaStateKind.None;
        private int _stateSequence;
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

        // ---- ネットワーク ---------------------------------

        /// <summary>
        /// Photon の部屋に入っていて、同期する相手がいる状態か。
        /// ひとりでの動作確認やモデル確認シーンでは false になり、同期処理そのものを行わない。
        /// </summary>
        public static bool IsPhotonReady =>
            (PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode) && PhotonNetwork.InRoom;

        /// <summary>
        /// AIの判断(行き先・攻撃の種類・ステート遷移)を行ってよい側か。
        /// オンラインなら MasterClient だけ、部屋に入っていなければ自分で判断する。
        /// </summary>
        public bool HasAuthority => IsPhotonReady ? PhotonNetwork.IsMasterClient : true;

        /// <summary>いま実行中のステートの種別。GorillaNetworkSync がこれを配る</summary>
        public GorillaStateKind CurrentStateKind => _currentStateKind;

        /// <summary>ステートを切り替えるたびに増える通し番号。同じ種別が連続しても再生し直せるようにする</summary>
        public int StateSequence => _stateSequence;

        /// <summary>
        /// MasterClient から配られたステートを適用する。GorillaNetworkSync から呼ばれる。
        /// ゲストはこの経路でしかステートが変わらない。
        /// </summary>
        public void ApplyNetworkState(GorillaStateKind kind)
        {
            if (kind == GorillaStateKind.None) return;

            // 死亡はHP同期で全員が同時に気づくため、各クライアントがすでにローカルで再生している。
            // 二重に入らないよう、同じ入口(PlayDeathLocally)へ流して弾く
            if (kind == GorillaStateKind.Death)
            {
                PlayDeathLocally();
                return;
            }

            // マスターがデバッグ復活したときに、ゲストの見た目も戻す
            if (_isDead) RestoreFromDeath();

            IGorillaState state = CreateState(kind);
            if (state == null) return;

            ChangeStateInternal(state, kind);
        }

        /// <summary>
        /// 権限に関わらず、その場で死亡ステートへ入る。
        /// ボスのHPは MonsterSyncObject で全員に配られていて、倒れる瞬間は各クライアントで
        /// 自然に揃うため、死亡演出だけは同期に載せず各自がローカルで再生する。
        /// 二重に呼ばれても2回目以降は何もしない。
        /// </summary>
        public void PlayDeathLocally()
        {
            if (_isDead) return;

            ChangeStateInternal(new GorillaStateDeath(), GorillaStateKind.Death);
        }

        /// <summary>ステート種別から、対応するステートのインスタンスを作る</summary>
        private IGorillaState CreateState(GorillaStateKind kind)
        {
            switch (kind)
            {
                case GorillaStateKind.Idle:         return new GorillaStateIdle();
                case GorillaStateKind.Patrol:       return new GorillaStatePatrol();
                case GorillaStateKind.Chase:        return new GorillaStateChase();
                case GorillaStateKind.NormalAttack: return new GorillaStateNormalAttack();
                case GorillaStateKind.SweepAttack:  return new GorillaStateSweepAttack();
                case GorillaStateKind.StampAttack:  return new GorillaStateStampAttack();
                case GorillaStateKind.BeamAttack:   return new GorillaStateBeamAttack();
                case GorillaStateKind.ChargeAttack: return new GorillaStateChargeAttack();
                case GorillaStateKind.RockThrow:    return new GorillaStateRockThrow();
                case GorillaStateKind.RushPunch:    return new GorillaStateRushPunch();
                case GorillaStateKind.Pounce:       return new GorillaStatePounce();
                case GorillaStateKind.Fissure:      return new GorillaStateFissure();
                case GorillaStateKind.Grab:         return new GorillaStateGrab();
                case GorillaStateKind.Roar:         return new GorillaStateRoar();
                // ゲストは自分から硬直を抜けない(抜けるタイミングもマスターが配る)ため、
                // 硬直時間は事実上無限にしておく
                case GorillaStateKind.Stagger:      return new GorillaStateStagger(float.MaxValue);
                case GorillaStateKind.Death:        return new GorillaStateDeath();
                default:                            return null;
            }
        }

        /// <summary>ステートのインスタンスから、ネットワークに載せられる種別へ変換する</summary>
        private static GorillaStateKind ToStateKind(IGorillaState state)
        {
            if (state is GorillaStateIdle)         return GorillaStateKind.Idle;
            if (state is GorillaStatePatrol)       return GorillaStateKind.Patrol;
            if (state is GorillaStateChase)        return GorillaStateKind.Chase;
            if (state is GorillaStateNormalAttack) return GorillaStateKind.NormalAttack;
            if (state is GorillaStateSweepAttack)  return GorillaStateKind.SweepAttack;
            if (state is GorillaStateStampAttack)  return GorillaStateKind.StampAttack;
            if (state is GorillaStateBeamAttack)   return GorillaStateKind.BeamAttack;
            if (state is GorillaStateChargeAttack) return GorillaStateKind.ChargeAttack;
            if (state is GorillaStateRockThrow)    return GorillaStateKind.RockThrow;
            if (state is GorillaStateRushPunch)    return GorillaStateKind.RushPunch;
            if (state is GorillaStatePounce)       return GorillaStateKind.Pounce;
            if (state is GorillaStateFissure)      return GorillaStateKind.Fissure;
            if (state is GorillaStateGrab)         return GorillaStateKind.Grab;
            if (state is GorillaStateRoar)         return GorillaStateKind.Roar;
            if (state is GorillaStateStagger)      return GorillaStateKind.Stagger;
            if (state is GorillaStateDeath)        return GorillaStateKind.Death;
            return GorillaStateKind.None;
        }

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

        /// <summary>追跡速度。フェーズが進むほど速くなる</summary>
        public float ChaseSpeed => _chaseSpeed * PhaseSpeedMultiplier;
        public float TurnSpeedDeg => _turnSpeedDeg;
        public float WanderRadius => _wanderRadius;
        public float IdleTimeMin => _idleTimeMin;
        public float IdleTimeMax => _idleTimeMax;
        public float PatrolWaitTimeMin => _patrolWaitTimeMin;
        public float PatrolWaitTimeMax => _patrolWaitTimeMax;
        // 硬直時間はフェーズが進むほど短くなる(＝手数が増える)
        public float NormalAttackStaggerTime => _normalAttackStaggerTime * PhaseStaggerMultiplier;
        public float StampAttackStaggerTime => _stampAttackStaggerTime * PhaseStaggerMultiplier;
        public float SweepAttackStaggerTime => _sweepAttackStaggerTime * PhaseStaggerMultiplier;

        // ---- 突進攻撃の公開API ---------------------------
        public float ChargeWindupTime => _chargeWindupTime;
        public float ChargeFollowUpWindupTime => _chargeFollowUpWindupTime;
        public float ChargeAimLockRatio => _chargeAimLockRatio;
        public float ChargeAimTurnSpeedDeg => _chargeAimTurnSpeedDeg;
        public float ChargeLeanBackAngleDeg => _chargeLeanBackAngleDeg;
        public float ChargeLeanForwardAngleDeg => _chargeLeanForwardAngleDeg;
        public float ChargeHomingSpeedDeg => _chargeHomingSpeedDeg;
        public float ChargeTurnTime => _chargeTurnTime;
        public float ChargeDistanceFalloff => _chargeDistanceFalloff;
        public float ChargeSpeed => _chargeSpeed;

        /// <summary>いまのフェーズでの突進の連続回数を返す</summary>
        public int RollChargeCount()
        {
            if (_phaseChargeCounts == null || _phaseChargeCounts.Length == 0) return 1;

            int index = Mathf.Clamp(_phase - 1, 0, _phaseChargeCounts.Length - 1);
            return Mathf.Max(1, _phaseChargeCounts[index]);
        }
        public float ChargeMaxDistance => _chargeMaxDistance;
        public int ChargeAttackDamage => _chargeAttackDamage;
        public float ChargeHitRadius => _chargeHitRadius;
        public float ChargeKnockbackDistance => _chargeKnockbackDistance;
        public float ChargeKnockbackDurationSec => _chargeKnockbackDurationSec;
        public float ChargeKnockbackArcHeight => _chargeKnockbackArcHeight;
        public float ChargeHitStaggerTime => _chargeHitStaggerTime * PhaseStaggerMultiplier;
        public float ChargeMissStaggerTime => _chargeMissStaggerTime * PhaseStaggerMultiplier;
        public GameObject ChargeAttackChargeEffectPrefab => _chargeAttackChargeEffectPrefab;

        /// <summary>クールタイムが明けていて突進を使えるか</summary>
        public bool CanUseChargeAttack => _chargeCooldownRemain <= 0f;

        /// <summary>突進を使ったことを伝え、クールタイムを開始する</summary>
        public void NotifyChargeAttackUsed()
        {
            _chargeCooldownRemain = _chargeAttackCooldownSec;
        }

        /// <summary>突進の射程内か(近すぎる場合は近接技を優先するので false)</summary>
        public bool IsPlayerInChargeRange()
        {
            if (_target == null) return false;

            float distance = GetDistanceToTarget();
            return distance >= _chargeAttackMinRange && distance <= _chargeAttackRange;
        }

        /// <summary>突進を使うかどうかの確率判定(射程・クールタイムは呼び出し側で確認済みの前提)</summary>
        public bool ShouldUseChargeAttack()
        {
            return Random.value < _chargeAttackProbability;
        }

        // ---- 岩投げの公開API -----------------------------
        public float RockThrowWindupTime => _rockThrowWindupTime;
        public float RockThrowAimTurnSpeedDeg => _rockThrowAimTurnSpeedDeg;
        public int RockThrowDamage => _rockThrowDamage;
        public float RockThrowRadius => _rockThrowRadius;
        public float RockThrowStaggerTime => _rockThrowStaggerTime * PhaseStaggerMultiplier;
        public GameObject RockThrowRockPrefab => _rockThrowRockPrefab;
        public float RockThrowRockScale => _rockThrowRockScale;
        public float RockThrowDigForwardOffset => _rockThrowDigForwardOffset;
        public float RockThrowHoldHeight => _rockThrowHoldHeight;
        public float RockThrowHoldForwardOffset => _rockThrowHoldForwardOffset;
        public float RockThrowSpeed => _rockThrowSpeed;
        public float RockThrowArcHeight => _rockThrowArcHeight;
        public ProjectKMP.Attack.AttackDecal RockThrowDecalPrefab => _rockThrowDecalPrefab;
        public GameObject RockThrowImpactEffectPrefab => _rockThrowImpactEffectPrefab;
        public float RockThrowImpactEffectScale => _rockThrowImpactEffectScale;

        /// <summary>クールタイムが明けていて岩を投げられるか</summary>
        public bool CanUseRockThrow => _rockThrowCooldownRemain <= 0f;

        /// <summary>岩を投げたことを伝え、クールタイムを開始する</summary>
        public void NotifyRockThrowUsed()
        {
            _rockThrowCooldownRemain = _rockThrowCooldownSec;
        }

        /// <summary>岩投げの射程内か(近すぎる場合は他の技を優先するので false)</summary>
        public bool IsPlayerInRockThrowRange()
        {
            if (_target == null) return false;

            float distance = GetDistanceToTarget();
            return distance >= _rockThrowMinRange && distance <= _rockThrowRange;
        }

        /// <summary>岩投げを使うかどうかの確率判定(射程・クールタイムは呼び出し側で確認済みの前提)</summary>
        public bool ShouldUseRockThrow()
        {
            return Random.value < _rockThrowProbability;
        }

        // ---- 連続パンチの公開API -------------------------
        public float RushPunchWindupTime => _rushPunchWindupTime;
        public float RushPunchSpeed => _rushPunchSpeed * PhaseSpeedMultiplier;
        /// <summary>
        /// 連打の1発ぶんの長さ(秒)。
        /// 他の技の硬直とは別に、フェーズごとの倍率で決める。
        /// 初心者が最初に当たる技なので、序盤はゆっくり見せて、激昂してから一気に速くする。
        /// </summary>
        public float RushPunchInterval =>
            _rushPunchInterval * GetPhaseValue(_rushPunchIntervalPhaseMultipliers, 1.0f);
        public int RushPunchDamage => _rushPunchDamage;
        public float RushPunchReach => _rushPunchReach;
        public float RushPunchHitRadius => _rushPunchHitRadius;
        public float RushPunchKnockbackDistance => _rushPunchKnockbackDistance;
        public float RushPunchHomingSpeedDeg => _rushPunchHomingSpeedDeg;
        public float RushPunchStaggerTime => _rushPunchStaggerTime * PhaseStaggerMultiplier;

        /// <summary>打っている間に保とうとする相手との距離(メートル)</summary>
        public float RushPunchKeepDistance => _rushPunchKeepDistance;

        /// <summary>1発ごとに前へ踏み込む距離(メートル)</summary>
        public float RushPunchLungeDistance => _rushPunchLungeDistance;

        /// <summary>締めの1発のダメージ倍率</summary>
        public float RushPunchFinishDamageMultiplier => _rushPunchFinishDamageMultiplier;

        /// <summary>締めの1発の吹き飛び距離(メートル)</summary>
        public float RushPunchFinishKnockbackDistance => _rushPunchFinishKnockbackDistance;

        /// <summary>連打で使う拳のモデル。未設定なら薙ぎ払いの手で代用する</summary>
        public GameObject RushPunchFistPrefab =>
            _rushPunchFistPrefab != null ? _rushPunchFistPrefab : _sweepFistEffectPrefab;

        /// <summary>拳のモデルの大きさ(ワールド基準のメートル)</summary>
        public float RushPunchFistScale => _rushPunchFistScale;

        /// <summary>拳を構える高さ(ワールド基準のメートル、足元から)</summary>
        public float RushPunchFistHeight => _rushPunchFistHeight;

        // ---- 拳の付け根 ----------------------------------

        /// <summary>
        /// 拳を構える位置(ゴリラのローカル座標)を返す。
        ///
        /// 前脚ボーンの先、つまりモデル上で実際に手がある場所を返すので、
        /// アニメーションで腕が動けば拳もそれに追従する。
        /// ボーンが見つからない場合や設定で切っている場合は false を返すので、
        /// 呼び出し側は従来の固定位置に切り替えること。
        /// </summary>
        /// <param name="isRight">右手なら true</param>
        /// <param name="localPosition">見つかったときの手の位置(ゴリラのローカル座標)</param>
        public bool TryGetHandAnchorLocal(bool isRight, out Vector3 localPosition)
        {
            localPosition = Vector3.zero;
            if (!_useHandBoneAnchor) return false;

            Transform bone = isRight
                ? ResolveHandBone(ref _rightHandBone, _rightHandBoneName)
                : ResolveHandBone(ref _leftHandBone, _leftHandBoneName);
            if (bone == null) return false;

            // ボーンは腕の付け根にあり、手先はボーンの +Y 方向にある
            Vector3 boneLocal = transform.InverseTransformPoint(bone.position);
            Vector3 downTheArm = transform.InverseTransformDirection(bone.up).normalized;
            localPosition = boneLocal + downTheArm * _handBoneLength;
            return true;
        }

        /// <summary>名前でボーンを探して覚えておく。毎フレーム探すと重いので一度だけ</summary>
        private Transform ResolveHandBone(ref Transform cache, string boneName)
        {
            if (cache != null) return cache;
            if (string.IsNullOrEmpty(boneName)) return null;

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name != boneName) continue;

                cache = child;
                return cache;
            }

            Debug.LogWarning($"[Gorilla] 手のボーンが見つかりません: {boneName}");
            return null;
        }

        /// <summary>クールタイムが明けていて連続パンチを使えるか</summary>
        public bool CanUseRushPunch => _rushPunchCooldownRemain <= 0f;

        /// <summary>連続パンチを使ったことを伝え、クールタイムを開始する</summary>
        public void NotifyRushPunchUsed()
        {
            _rushPunchCooldownRemain = _rushPunchCooldownSec;
        }

        /// <summary>連続パンチの射程内か(近すぎる場合は近接技を優先するので false)</summary>
        public bool IsPlayerInRushPunchRange()
        {
            if (_target == null) return false;

            float distance = GetDistanceToTarget();
            return distance >= _rushPunchMinRange && distance <= _rushPunchRange;
        }

        /// <summary>連続パンチを使うかどうかの確率判定(射程・クールタイムは呼び出し側で確認済みの前提)</summary>
        public bool ShouldUseRushPunch()
        {
            return Random.value < _rushPunchProbability;
        }

        /// <summary>いまのフェーズでの連打数を返す</summary>
        public int RollRushPunchCount()
        {
            if (_phaseRushPunchCounts == null || _phaseRushPunchCounts.Length == 0) return 4;

            int index = Mathf.Clamp(_phase - 1, 0, _phaseRushPunchCounts.Length - 1);
            return Mathf.Max(1, _phaseRushPunchCounts[index]);
        }

        // ---- 跳びかかりの公開API -------------------------
        public float PounceWindupTime => _pounceWindupTime;
        public float PounceLeapDurationSec => _pounceLeapDurationSec;
        public float PounceJumpHeight => _pounceJumpHeight;
        public float PounceMaxDistance => _pounceMaxDistance;
        public int PounceDamage => _pounceDamage;
        public float PounceRadius => _pounceRadius;
        public float PounceKnockbackDistance => _pounceKnockbackDistance;
        public float PounceStaggerTime => _pounceStaggerTime * PhaseStaggerMultiplier;
        public float PounceCameraShake => _pounceCameraShake;
        public int PounceDebrisCount => _pounceDebrisCount;
        public float PounceDebrisScale => _pounceDebrisScale;

        /// <summary>着地で飛び散るかけら。専用のものが無ければ岩投げの岩で代用する</summary>
        public GameObject PounceDebrisPrefab =>
            _pounceDebrisPrefab != null ? _pounceDebrisPrefab : _rockThrowRockPrefab;

        /// <summary>クールタイムが明けていて跳びかかれるか</summary>
        public bool CanUsePounce => _pounceCooldownRemain <= 0f;

        /// <summary>跳びかかりを使ったことを伝え、クールタイムを開始する</summary>
        public void NotifyPounceUsed()
        {
            _pounceCooldownRemain = _pounceCooldownSec;
        }

        /// <summary>跳びかかりの射程内か</summary>
        public bool IsPlayerInPounceRange()
        {
            if (_target == null) return false;

            float distance = GetDistanceToTarget();
            return distance >= _pounceMinRange && distance <= _pounceRange;
        }

        /// <summary>跳びかかりを使うかどうかの確率判定</summary>
        public bool ShouldUsePounce()
        {
            return Random.value < _pounceProbability;
        }

        // ---- 地割れの公開API -----------------------------
        public float FissureWindupTime => _fissureWindupTime;
        public float FissureAimLockRatio => _fissureAimLockRatio;
        public float FissureAimTurnSpeedDeg => _fissureAimTurnSpeedDeg;
        public float FissureSpeed => _fissureSpeed;
        public float FissureLength => _fissureLength;
        public float FissureWidth => _fissureWidth;
        public int FissureDamage => _fissureDamage;
        public float FissureKnockbackDistance => _fissureKnockbackDistance;
        public float FissureKnockbackArcHeight => _fissureKnockbackArcHeight;
        public float FissureStaggerTime => _fissureStaggerTime * PhaseStaggerMultiplier;
        public float FissureSpreadAngleDeg => _fissureSpreadAngleDeg;
        public float FissureCameraShake => _fissureCameraShake;
        public ProjectKMP.Attack.AttackDecal FissureDecalPrefab => _fissureDecalPrefab;
        public GameObject FissureSpikePrefab => _fissureSpikePrefab;
        public float FissureSpikeScale => _fissureSpikeScale;
        public float FissureSpikeRiseSec => _fissureSpikeRiseSec;
        public float FissureSpikeLifetimeSec => _fissureSpikeLifetimeSec;
        public float FissureHandRaiseHeight => _fissureHandRaiseHeight;
        public float FissureHandForwardOffset => _fissureHandForwardOffset;
        public float FissureHandScale => _fissureHandScale;
        public int FissureDebrisCount => _fissureDebrisCount;

        /// <summary>クールタイムが明けていて地割れを使えるか</summary>
        public bool CanUseFissure => _fissureCooldownRemain <= 0f;

        /// <summary>地割れを使ったことを伝え、クールタイムを開始する</summary>
        public void NotifyFissureUsed()
        {
            _fissureCooldownRemain = _fissureCooldownSec;
        }

        /// <summary>地割れの射程内か</summary>
        public bool IsPlayerInFissureRange()
        {
            if (_target == null) return false;

            float distance = GetDistanceToTarget();
            return distance >= _fissureMinRange && distance <= _fissureRange;
        }

        /// <summary>地割れを使うかどうかの確率判定</summary>
        public bool ShouldUseFissure()
        {
            return Random.value < _fissureProbability;
        }

        /// <summary>いまのフェーズでの裂け目の本数を返す</summary>
        public int RollFissureCount()
        {
            if (_phaseFissureCounts == null || _phaseFissureCounts.Length == 0) return 1;

            int index = Mathf.Clamp(_phase - 1, 0, _phaseFissureCounts.Length - 1);
            return Mathf.Max(1, _phaseFissureCounts[index]);
        }

        // ---- 掴みの公開API -------------------------------

        /// <summary>誰も掴んでいないことを表す値。ActorNumber と衝突しない値を使う</summary>
        public const int NO_GRAB = int.MinValue;

        public float GrabReach => _grabReach;
        public float GrabAngleDeg => _grabAngleDeg;
        public float GrabWindupTime => _grabWindupTime;
        public float GrabAimLockRatio => _grabAimLockRatio;
        public float GrabAimTurnSpeedDeg => _grabAimTurnSpeedDeg;
        public float GrabHoldSec => _grabHoldSec;
        public int GrabRescueDamage => _grabRescueDamage;
        public float GrabHoldHeight => _grabHoldHeight;
        public float GrabHoldForwardOffset => _grabHoldForwardOffset;
        public int GrabSlamDamage => _grabSlamDamage;
        public float GrabSlamKnockbackDistance => _grabSlamKnockbackDistance;
        public float GrabStaggerTime => _grabStaggerTime * PhaseStaggerMultiplier;
        public GameObject GrabHandPrefab => _grabHandPrefab;
        public float GrabHandScale => _grabHandScale;
        public int GrabSqueezeDamage => _grabSqueezeDamage;
        public int GrabEscapeMashCount => _grabEscapeMashCount;

        /// <summary>
        /// 掴まれた本人が連打で抜け出したいと申し出ているか。
        /// 実際に離すかどうかを決めるのは MasterClient なので、これは「申し出」に過ぎない。
        /// </summary>
        public bool EscapeRequested => _escapeRequested;

        /// <summary>
        /// 掴みから抜け出したいことを伝える。掴まれた本人のクライアントから呼ぶ。
        /// 自分がマスターならその場で受け付け、ゲストなら GorillaNetworkSync 経由でマスターへ届ける。
        /// </summary>
        public void RequestGrabEscape()
        {
            if (HasAuthority)
            {
                _escapeRequested = true;
                return;
            }

            var sync = GetComponent<GorillaNetworkSync>();
            if (sync != null) sync.SendGrabEscapeRequest();
        }

        /// <summary>抜け出しの申し出を受け付ける。GorillaNetworkSync から呼ばれる(MasterClient のみ)</summary>
        public void AcceptGrabEscapeRequest()
        {
            if (!HasAuthority) return;
            _escapeRequested = true;
        }

        /// <summary>抜け出しの申し出を捨てる。新しく掴みにいくときに呼ぶ</summary>
        public void ClearGrabEscapeRequest()
        {
            _escapeRequested = false;
        }

        /// <summary>
        /// 掴んでいるプレイヤーの ActorNumber。誰も掴んでいなければ NO_GRAB。
        /// 決めるのは MasterClient だけで、値は GorillaSyncData に載って全員へ配られる。
        /// </summary>
        public int GrabbedActorNumber
        {
            get => _grabbedActorNumber;
            set
            {
                if (!HasAuthority)
                {
                    Debug.LogWarning("[Gorilla] 掴む相手を決められるのは MasterClient だけです", this);
                    return;
                }
                _grabbedActorNumber = value;
            }
        }

        /// <summary>配られてきた「誰を掴んでいるか」を反映する。GorillaNetworkSync から呼ばれる</summary>
        public void ApplyNetworkGrabbedActorNumber(int actorNumber)
        {
            if (HasAuthority) return;
            _grabbedActorNumber = actorNumber;
        }

        /// <summary>ボスの現在HP。掴みの救出判定に使う。HPが無ければ0</summary>
        public int BossCurrentHp => _bossHealth != null ? _bossHealth.CurrentHp : 0;

        /// <summary>
        /// 掴みを使えるか。
        /// いまは行動に混ぜていないので既定では常に false を返す。
        /// 技の処理そのものは残してあるので、インスペクタの「掴みを行動に混ぜるか」を
        /// オンにすれば、そのまま元の挙動に戻る。
        /// </summary>
        public bool CanUseGrab => _useGrab && _grabCooldownRemain <= 0f;

        /// <summary>掴みを使ったことを伝え、クールタイムを開始する</summary>
        public void NotifyGrabUsed()
        {
            _grabCooldownRemain = _grabCooldownSec;

            // 前の掴みで押された連打が持ち越されないよう、始めるたびに捨てる
            _escapeRequested = false;
        }

        /// <summary>掴みの射程内か</summary>
        public bool IsPlayerInGrabRange()
        {
            return _target != null && GetDistanceToTarget() <= _grabReach;
        }

        /// <summary>掴みを使うかどうかの確率判定</summary>
        public bool ShouldUseGrab()
        {
            return Random.value < _grabProbability;
        }

        // ---- 咆哮の公開API -------------------------------
        public float RoarRadius => _roarRadius;
        public float RoarWaveSpeed => _roarWaveSpeed;
        public int RoarDamage => _roarDamage;
        public float RoarKnockbackDistance => _roarKnockbackDistance;
        public float RoarKnockbackDurationSec => _roarKnockbackDurationSec;
        public float RoarKnockbackArcHeight => _roarKnockbackArcHeight;
        public float RoarCameraShake => _roarCameraShake;

        // ---- 岩のかけらの公開API -------------------------
        public int RockThrowDebrisCount => _rockThrowDebrisCount;
        public float RockThrowDebrisScale => _rockThrowDebrisScale;
        public float RockThrowDebrisSpreadSpeed => _rockThrowDebrisSpreadSpeed;
        public float RockThrowDebrisUpSpeed => _rockThrowDebrisUpSpeed;
        public float RockThrowDebrisLifetimeSec => _rockThrowDebrisLifetimeSec;

        /// <summary>いまのフェーズで一度に投げる岩の数を返す</summary>
        public int RollRockThrowCount()
        {
            if (_phaseRockThrowCounts == null || _phaseRockThrowCounts.Length == 0) return 1;

            int index = Mathf.Clamp(_phase - 1, 0, _phaseRockThrowCounts.Length - 1);
            return Mathf.Max(1, _phaseRockThrowCounts[index]);
        }

        // ---- 攻撃予測の公開API ---------------------------

        /// <summary>攻撃の当たる範囲を地面に描く表示のプレハブ。全ての攻撃で共通して使う</summary>
        public GorillaAttackTelegraph AttackTelegraphPrefab => _attackTelegraphPrefab;

        /// <summary>近接攻撃でも予測を出すか</summary>
        public bool ShowMeleeTelegraph => _showMeleeTelegraph;

        /// <summary>近接攻撃用の予測表示。設定で切られていれば何も出さずに null を返す</summary>
        public GorillaAttackTelegraph MeleeTelegraphPrefab => _showMeleeTelegraph ? _attackTelegraphPrefab : null;

        // ---- フェーズの公開API ---------------------------

        /// <summary>いまのフェーズ(1から始まる)。HPが減るほど大きくなる</summary>
        public int Phase => _phase;

        /// <summary>いまのフェーズでの移動速度倍率</summary>
        public float PhaseSpeedMultiplier => GetPhaseValue(_phaseSpeedMultipliers, 1.0f);

        /// <summary>いまのフェーズでの硬直時間倍率</summary>
        public float PhaseStaggerMultiplier => GetPhaseValue(_phaseStaggerMultipliers, 1.0f);

        /// <summary>いまのフェーズでの頭突きの連撃数(この一撃のあとに続けて殴る回数)を返す</summary>
        public int RollNormalAttackComboCount()
        {
            int count = 1;
            if (_phaseNormalAttackComboCounts != null && _phaseNormalAttackComboCounts.Length > 0)
            {
                int index = Mathf.Clamp(_phase - 1, 0, _phaseNormalAttackComboCounts.Length - 1);
                count = Mathf.Max(1, _phaseNormalAttackComboCounts[index]);
            }

            // 連撃数は「1撃目のあと何回続けるか」なので、総数から1を引いて返す
            return count - 1;
        }

        /// <summary>フェーズ別の配列から、いまのフェーズに対応する値を取り出す</summary>
        private float GetPhaseValue(float[] values, float fallback)
        {
            if (values == null || values.Length == 0) return fallback;

            int index = Mathf.Clamp(_phase - 1, 0, values.Length - 1);
            return values[index];
        }

        /// <summary>
        /// ボスの残りHPからフェーズを求め直す。上がっていたら咆哮させる。
        /// HPは MonsterSyncObject で全員に配られているので、全クライアントで同じフェーズになる。
        /// </summary>
        private void UpdatePhase()
        {
            if (_bossHealth == null || _bossHealth.MaxHp <= 0) return;

            float ratio = Mathf.Clamp01(_bossHealth.CurrentHp / (float)_bossHealth.MaxHp);

            int newPhase = 1;
            if (_phaseHpThresholds != null)
            {
                foreach (float threshold in _phaseHpThresholds)
                {
                    if (ratio < threshold) newPhase++;
                }
            }

            if (newPhase <= _phase) return;

            _phase = newPhase;

            // 咆哮は「ここから激しくなる」の合図。ステートなので同期にも載る
            if (_roarOnPhaseUp && !_isDead) ChangeState(new GorillaStateRoar());
        }

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

        /// <summary>1回の破壊光線で撃つ発数</summary>
        public int BeamShotCount => Mathf.Max(1, _beamShotCount);

        /// <summary>連射のときに狙いを付け直す時間(秒)</summary>
        public float BeamReaimTime => _beamReaimTime;

        /// <summary>溜めのどこで狙いを固定するか(0〜1)</summary>
        public float BeamAimLockRatio => _beamAimLockRatio;
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

            _bossHealth = GetComponent<ProjectKMP.Monster.BossHealth>();
            _targetSelector = GetComponent<GorillaTargetSelector>();

            // 攻撃を受けたときの通知。狙う相手を決めるヘイトを溜めるのと、
            // 気づいていない(待機中・徘徊中)なら攻撃してきた犬の方へ振り向くのを兼ねる
            _hitTarget = GetComponent<HitTarget>();
            if (_hitTarget != null)
            {
                _hitSubscription = _hitTarget.Hit.Subscribe(OnHit);
            }
        }

        private void OnDestroy()
        {
            _hitSubscription?.Dispose();
            _hitSubscription = null;
        }

        /// <summary>
        /// 攻撃を受けたときの通知。ヒットは全クライアントで流れるが、狙う相手を決めるのは
        /// MasterClient だけなので、ヘイトを溜めるのも権限を持つ側だけにする。
        /// </summary>
        private void OnHit(HitTarget.HitInfo info)
        {
            if (HasAuthority && _targetSelector != null)
            {
                _targetSelector.AddHate(info.AttackerActorNumber, info.Damage);
            }

            OnHitWhileUnaware();
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

            // 向きはマスターが配るので、ゲストは自分で振り向かない
            if (!HasAuthority)
            {
                _isTurningToAttacker = false;
                return;
            }

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

            // 初期ステートだけはゲストも自分で入る。マスターから最初の値が届くまでの
            // わずかな間、ステート無しで固まって見えないようにするため
            ChangeStateInternal(new GorillaStateIdle(), GorillaStateKind.Idle);
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

            UpdateTarget();
            UpdateCooldowns();

            // フェーズはHP同期から求めるので全クライアントで同じになる。
            // ただし咆哮ステートへ入れるのは MasterClient だけ(ChangeState 側で弾かれる)
            UpdatePhase();

            _currentState?.Update(this);
            UpdateHitReactionTurn();

        }

        /// <summary>
        /// 狙う相手を決める。GorillaTargetSelector があれば「近さ + 与えてきたダメージ」で
        /// 定期的に選び直し、無ければ従来どおり最初に見つけた犬を追い続ける。
        /// </summary>
        private void UpdateTarget()
        {
            // 狙う相手を決めるのは MasterClient だけ。ゲストは配られた位置と
            // ステートを再生するだけなので、自分で選び直す必要がない
            if (HasAuthority && _targetSelector != null)
            {
                Transform picked = _targetSelector.Evaluate(transform.position, _target, Time.deltaTime);
                if (picked != null)
                {
                    _target = picked;
                    return;
                }
            }

            if (_target != null) return;

            _targetSearchTimer -= Time.deltaTime;
            if (_targetSearchTimer <= 0f)
            {
                _targetSearchTimer = _targetSearchIntervalSec;
                _target = FindDogTarget();
            }
        }

        /// <summary>各攻撃のクールタイムを進める</summary>
        private void UpdateCooldowns()
        {
            float delta = Time.deltaTime;
            if (_beamCooldownRemain > 0f)      _beamCooldownRemain      -= delta;
            if (_chargeCooldownRemain > 0f)    _chargeCooldownRemain    -= delta;
            if (_rockThrowCooldownRemain > 0f) _rockThrowCooldownRemain -= delta;
            if (_rushPunchCooldownRemain > 0f)  _rushPunchCooldownRemain  -= delta;
            if (_pounceCooldownRemain > 0f)     _pounceCooldownRemain     -= delta;
            if (_fissureCooldownRemain > 0f)    _fissureCooldownRemain    -= delta;
            if (_grabCooldownRemain > 0f)       _grabCooldownRemain       -= delta;
        }

        /// <summary>
        /// ステートを切り替える。現在のステートのExit()を呼んでから、新しいステートのEnter()を呼ぶ。
        /// ゲストはここを通っても切り替わらない(ステートを決めるのは MasterClient だけ)。
        /// 配られたステートを適用するのは ApplyNetworkState() の役目。
        /// </summary>
        public void ChangeState(IGorillaState newState)
        {
            if (!HasAuthority) return;

            ChangeStateInternal(newState, ToStateKind(newState));
        }

        /// <summary>権限チェックを通した後の、実際のステート切り替え処理</summary>
        private void ChangeStateInternal(IGorillaState newState, GorillaStateKind kind)
        {
            _currentState?.Exit(this);
            _currentState = newState;
            _currentStateKind = kind;

            // 「攻撃 → 硬直 → また同じ攻撃」でも配った側と受け取った側でズレないよう、
            // 切り替えのたびに番号を進める
            _stateSequence++;

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
            RestoreFromDeath();
            ChangeState(new GorillaStateIdle());
        }

        /// <summary>
        /// 死亡演出で変えた座標・回転・スケール・アニメーション速度を元に戻す。
        /// ステートの切り替えは行わないので、呼び出し側で続けて入るステートを決める。
        /// </summary>
        private void RestoreFromDeath()
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

        /// <summary>
        /// 目標地点へ向けて移動・旋回する。
        /// ゲストの位置は GorillaNetworkSync が配られた値で書き込むため、こちらでは動かさない。
        /// </summary>
        public void MoveTowards(Vector3 targetPosition, float speed)
        {
            if (!HasAuthority) return;

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

        /// <summary>
        /// その場で目標方向へ旋回のみ行う(移動しない)。
        /// 向きも同期対象なので、ゲストでは何もしない。
        /// </summary>
        public void TurnTowards(Vector3 targetPosition)
        {
            if (!HasAuthority) return;

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
