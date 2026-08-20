using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using ProjectKMP.Battle;
using R3;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 被弾による死亡とリスポーンを扱う。
    /// 弾(PlayerShooter)は1発で即死させる(ApplyKill)が、ゴリラの破壊光線のような
    /// 段階ダメージを与える攻撃は ApplyDamage でHPを削り、0になった時点で死亡させる。
    /// 撃った側(またはダメージ源)のクライアントが ApplyKill / ApplyDamage を呼び、
    /// そこから全員に RPC が飛ぶ。
    /// 発生源の座標つきで呼ばれた場合、発生源から離れる方向へ吹き飛ぶ。
    /// 通常被弾は CharacterController 移動の小さな吹き飛び、死亡時は当たり判定を切ったうえで
    /// 放物線を描いて画面端まで飛ぶような大きな吹き飛びになる(いずれも距離・時間は調整可能)。
    /// 死亡後は「死亡アニメーション(現状0秒=即待機) → リスポーン待機(カウントダウン) → ランダム地点で復活」の順に進む。
    /// HP・死亡・カウントダウンは Observable で公開しており、UI(PlayerHpPresenter)や
    /// アニメーション側(死亡モーションを入れる場合は Died を購読)がここへつなぐ。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerHealth : MonoBehaviourPun
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("HP")]
        [SerializeField, Min(1), Tooltip("最大HP。弾は1発で即死(ApplyKill)だが、段階ダメージを与える攻撃(破壊光線など)はここから減っていく")]
        private int _maxHp = 10;

        [Header("リスポーン")]
        [SerializeField, Min(0.0f), Tooltip("死亡してからリスポーンするまでの待機秒数。UIのカウントダウンもこの値を使う")]
        private float _respawnDelaySec = 3.0f;

        [SerializeField, Min(0.0f), Tooltip("リスポーン地点を選ぶ円の最小半径(メートル)。フィールド中央(ボス付近)を避けたいときに広げる")]
        private float _respawnMinRadius = 0.0f;

        [SerializeField, Min(0.0f), Tooltip("リスポーン地点を選ぶ円の最大半径(メートル)。壁の内側に収める")]
        private float _respawnMaxRadius = 8.0f;

        [SerializeField, Tooltip("リスポーンさせる高さ(メートル)")]
        private float _respawnHeight = 1.0f;

        [Header("吹き飛び")]
        [SerializeField, Min(0.0f), Tooltip("被弾時に発生源と反対方向へ吹き飛ぶ距離(メートル)。0で吹き飛びなし")]
        private float _knockbackDistance = 2.5f;

        [SerializeField, Min(0.01f), Tooltip("被弾時の吹き飛びにかける時間(秒)")]
        private float _knockbackDurationSec = 0.25f;

        [SerializeField, Tooltip("ビーム照射中は吹き飛ばされないようにする(狙いがブレて演出が崩れるため)")]
        private bool _blockKnockbackWhileBeam = true;

        [SerializeField, Min(0.0f), Tooltip("死亡時に吹き飛ぶ距離(メートル)。画面端まで飛ぶような大きめの値にする。0で吹き飛びなし")]
        private float _deathKnockbackDistance = 16.0f;

        [SerializeField, Min(0.01f), Tooltip("死亡時の吹き飛びにかける時間(秒)")]
        private float _deathKnockbackDurationSec = 0.7f;

        [SerializeField, Min(0.0f), Tooltip("死亡時の吹き飛びで浮き上がる高さ(メートル)。放物線の頂点の高さ")]
        private float _deathKnockbackArcHeight = 3.0f;

        [Header("死亡演出")]
        [SerializeField, Min(0.0f), Tooltip("死亡アニメーションを見せる秒数。この時間が過ぎてからリスポーン待機(カウントダウン)に入る。0なら即リスポーン待機へ")]
        private float _deathAnimationSec = 0.0f;

        [SerializeField, Tooltip("死亡中に無効化する操作系コンポーネント(自分のキャラのみ)。PlayerMover / LocalPlayerMover は未設定でも自動で対象になる")]
        private Behaviour[] _disableWhileDead = new Behaviour[0];

        [SerializeField] private BiteVfx _biteVfxPrefab;
        [SerializeField] private float _biteVfxHeight = 1.1f;

        [Header("死亡ポーズ")]
        [SerializeField, Tooltip("死亡時にひっくり返す見た目のルート。未設定なら子の\"Body\"を自動で探す")]
        private Transform _bodyRoot;

        [Header("ぺっちゃんこ演出(手のひらで挟み潰されたとき)")]
        [SerializeField, Min(0.01f), Tooltip("挟み潰されてぺっちゃんこになるまでの時間(秒)")]
        private float _crushDurationSec = 0.15f;

        [SerializeField, Min(0f), Tooltip("ぺっちゃんこになったときのX(左右、手のひらに挟まれる方向)のスケール倍率。0に近いほど横に薄く潰れる")]
        private float _crushSquashScaleX = 0.05f;

        [SerializeField, Min(1f), Tooltip("ぺっちゃんこになったときのYZ(高さ・前後)方向のスケール倍率。横に潰れたぶん、これらの方向へ広がる")]
        private float _crushSquashScaleYZMultiplier = 1.5f;

        // ---- 内部状態 ------------------------------------

        private CharacterController _controller;
        private Behaviour _mover;
        private bool _isDead;
        private bool _isInvincible;
        private Quaternion _bodyDefaultLocalRotation;
        private Vector3 _bodyDefaultLocalScale;
        private CancellationTokenSource _knockbackCts;
        private CancellationTokenSource _crushCts;

        private readonly ReactiveProperty<int> _hp = new ReactiveProperty<int>(0);
        private readonly ReactiveProperty<float> _respawnRemainingSec = new ReactiveProperty<float>(0.0f);
        private readonly Subject<int> _damaged = new Subject<int>();
        private readonly Subject<Unit> _died = new Subject<Unit>();
        private readonly Subject<Unit> _revived = new Subject<Unit>();

        // ---- 公開API -------------------------------------

        public bool IsDead => _isDead;

        /// <summary>最大HP</summary>
        public int MaxHp => _maxHp;

        /// <summary>現在のHP</summary>
        public int CurrentHp => _hp.Value;

        /// <summary>死亡してからリスポーンするまでの待機秒数(カウントダウンの総時間)</summary>
        public float RespawnDelaySec => _respawnDelaySec;

        /// <summary>HPが変化するたびに、変化後の値を流す(HPバーなどのUIから購読する想定。購読時に現在値も流れる)</summary>
        public Observable<int> HpChanged => _hp;

        /// <summary>ダメージを受けた瞬間に、その一撃ぶんのダメージ量を流す。全クライアントで発火する(ヒットエフェクトなどの演出用)</summary>
        public Observable<int> Damaged => _damaged;

        /// <summary>死亡した瞬間に流れる。全クライアントで発火するので、死亡アニメーションを入れる場合はこれを購読する</summary>
        public Observable<Unit> Died => _died;

        /// <summary>リスポーンした瞬間に流れる。全クライアントで発火する</summary>
        public Observable<Unit> Revived => _revived;

        /// <summary>リスポーンまでの残り秒数。自分のキャラのクライアントでのみカウントダウンされる(UIのカウントダウン表示用)</summary>
        public ReadOnlyReactiveProperty<float> RespawnRemainingSec => _respawnRemainingSec;

        /// <summary>このプレイヤーを操作しているクライアントの ActorNumber</summary>
        public int OwnerActorNumber => photonView.Owner != null ? photonView.Owner.ActorNumber : -1;

        /// <summary>いま攻撃を受け付けない状態か</summary>
        public bool IsInvincible => _isInvincible;

        /// <summary>
        /// 無敵の入り切り。とびこみの上昇中など、避けている最中に使う。
        /// ダメージは与える側のクライアントから呼ばれるが、ゴリラの攻撃は当たった本人の
        /// クライアントで判定しているので、ここで弾けば通信を増やさずに済む。
        /// </summary>
        public void SetInvincible(bool value)
        {
            _isInvincible = value;
        }

        /// <summary>撃った側のクライアントから呼ぶ。HPに関わらず即座に全員に死亡を伝える(弾など)。吹き飛びなし</summary>
        public void ApplyKill(int killerActorNumber)
        {
            ApplyKill(killerActorNumber, transform.position);
        }

        /// <summary>発生源の座標つきの即死。発生源から離れる方向へ死亡時の大きな吹き飛びが入る</summary>
        public void ApplyKill(int killerActorNumber, Vector3 sourcePosition)
        {
            if (_isDead || _isInvincible) return;
            photonView.RPC(nameof(RpcOnKilled), RpcTarget.All, killerActorNumber, sourcePosition);
        }

        /// <summary>
        /// 発生源の座標つきの即死。ただし吹き飛ばさず、その場でぺっちゃんこに潰れる演出で倒す。
        /// (例: 薙ぎ払い攻撃で両手のひらの間に挟み込まれた場合など)
        /// </summary>
        public void ApplyCrushKill(int killerActorNumber, Vector3 sourcePosition)
        {
            if (_isDead || _isInvincible) return;
            photonView.RPC(nameof(RpcOnCrushed), RpcTarget.All, killerActorNumber, sourcePosition);
        }

        /// <summary>
        /// ダメージ源のクライアントから呼ぶ。指定ぶんHPを減らし、全員に伝える。吹き飛びなし。
        /// HPが0になった時点で ApplyKill と同じ死亡処理が走る。
        /// attackerActorNumber はプレイヤーによる攻撃でなければ -1 を渡してよい(撃破数の加算対象にならない)。
        /// </summary>
        public void ApplyDamage(int damage, int attackerActorNumber)
        {
            ApplyDamage(damage, attackerActorNumber, transform.position);
        }

        /// <summary>発生源の座標つきのダメージ。発生源から離れる方向へ吹き飛ぶ(死亡した場合は大きな吹き飛びになる)</summary>
        public void ApplyDamage(int damage, int attackerActorNumber, Vector3 sourcePosition)
        {
            if (_isDead || _isInvincible) return;
            if (damage <= 0) return;
            photonView.RPC(nameof(RpcOnDamaged), RpcTarget.All, damage, attackerActorNumber, sourcePosition);
        }

        /// <summary>
        /// 発生源の座標つきのダメージ。吹き飛び距離を通常の被弾より強く(または弱く)指定したいときに使う。
        /// (例: 薙ぎ払い攻撃のように、通常の被弾よりずっと大きく吹き飛ばしたい攻撃)
        /// 死亡した場合はこの値を無視し、通常通り死亡時の大きな吹き飛びになる
        /// </summary>
        public void ApplyDamage(int damage, int attackerActorNumber, Vector3 sourcePosition, float knockbackDistanceOverride)
        {
            ApplyDamage(damage, attackerActorNumber, sourcePosition, knockbackDistanceOverride, _knockbackDurationSec);
        }

        /// <summary>吹き飛び距離に加えて、吹き飛びにかける時間も指定できる版。距離を大きくする場合は時間も伸ばすと自然に見える</summary>
        public void ApplyDamage(int damage, int attackerActorNumber, Vector3 sourcePosition, float knockbackDistanceOverride, float knockbackDurationOverrideSec)
        {
            ApplyDamage(damage, attackerActorNumber, sourcePosition, knockbackDistanceOverride, knockbackDurationOverrideSec, 0.0f);
        }

        /// <summary>
        /// 吹き飛び距離・時間に加えて、放物線を描いて浮き上がる高さ(arcHeight)も指定できる版。
        /// 0なら死亡時以外の通常の被弾と同じく水平にしか吹き飛ばない。正の値を渡すと、死亡時の吹き飛びと同じように
        /// 上空へ浮き上がりながら吹き飛ぶようになる(例: 薙ぎ払い攻撃で上空を巻き込むように吹き飛ばしたい場合)
        /// </summary>
        public void ApplyDamage(int damage, int attackerActorNumber, Vector3 sourcePosition, float knockbackDistanceOverride, float knockbackDurationOverrideSec, float knockbackArcHeightOverride)
        {
            if (_isDead || _isInvincible) return;
            if (damage <= 0) return;
            photonView.RPC(nameof(RpcOnDamagedWithKnockback), RpcTarget.All, damage, attackerActorNumber, sourcePosition, knockbackDistanceOverride, knockbackDurationOverrideSec, knockbackArcHeightOverride);
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            // 移動スクリプトはシーンによって型が違うので、あるほうを使う
            _mover = GetComponent<PlayerMover>();
            if (_mover == null) _mover = GetComponent<LocalPlayerMover>();

            _hp.Value = _maxHp;

            if (_bodyRoot == null)
            {
                Transform found = transform.Find("Body");
                _bodyRoot = found != null ? found : transform;
            }
            _bodyDefaultLocalRotation = _bodyRoot.localRotation;
            _bodyDefaultLocalScale = _bodyRoot.localScale;
            _isInvincible = false;
        }

        /// <summary>
        /// 自分の状態を、画面が見る場所へ流し込む。
        ///
        /// 画面がこの部品を探しに来ると、生まれる順番に左右されて不安定になる。
        /// こちらから渡しておけば、画面は用意された場所を見るだけで済む。
        ///
        /// 流すのは操作している本人のぶんだけ。他の人の体力は画面に出さない。
        /// </summary>
        private void PublishStatus()
        {
            if (!photonView.IsMine) return;

            Core.PlayerStatusHub.Local.SetHp(_hp.Value, _maxHp);
            Core.PlayerStatusHub.Local.SetDead(_isDead);
            Core.PlayerStatusHub.Local.SetRespawn(_respawnRemainingSec.Value, _respawnDelaySec);
        }

        private void Update()
        {
            PublishStatus();
        }

        private void OnDestroy()
        {
            CancelKnockback();
            _crushCts?.Cancel();
            _crushCts?.Dispose();
            _hp.Dispose();
            _respawnRemainingSec.Dispose();
            _damaged.Dispose();
            _died.Dispose();
            _revived.Dispose();
        }

        // ---- RPC -----------------------------------------

        [PunRPC]
        private void RpcOnDamaged(int damage, int attackerActorNumber, Vector3 sourcePosition, PhotonMessageInfo info)
        {
            if (_isDead) return;

            _hp.Value = Mathf.Max(0, _hp.Value - damage);
            _damaged.OnNext(damage);

            if (_hp.Value <= 0)
            {
                Die(attackerActorNumber, sourcePosition);
            }
            else
            {
                // 生存中の被弾は小さく吹き飛ぶ。移動は自分のクライアントが行い、他クライアントへは位置同期で伝わる。
                // ただしビーム照射中だけは、押されると狙いがブレてしまうので吹き飛ばさない
                if (!IsKnockbackBlocked())
                {
                    StartKnockback(sourcePosition, _knockbackDistance, _knockbackDurationSec, 0.0f);
                }
            }
        }

        /// <summary>RpcOnDamagedの、吹き飛び距離を指定できる版。ロジックはRpcOnDamagedと同じで、吹き飛び距離だけ引数で上書きする</summary>
        [PunRPC]
        private void RpcOnDamagedWithKnockback(int damage, int attackerActorNumber, Vector3 sourcePosition, float knockbackDistanceOverride, float knockbackDurationOverrideSec, float knockbackArcHeightOverride, PhotonMessageInfo info)
        {
            if (_isDead) return;

            _hp.Value = Mathf.Max(0, _hp.Value - damage);
            _damaged.OnNext(damage);

            if (_hp.Value <= 0)
            {
                Die(attackerActorNumber, sourcePosition);
            }
            else
            {
                if (!IsKnockbackBlocked())
                {
                    StartKnockback(sourcePosition, knockbackDistanceOverride, knockbackDurationOverrideSec, knockbackArcHeightOverride);
                }
            }
        }

        [PunRPC]
        private void RpcOnKilled(int killerActorNumber, Vector3 sourcePosition)
        {
            if (_isDead) return;

            _hp.Value = 0;
            _damaged.OnNext(_maxHp);
            Die(killerActorNumber, sourcePosition);
        }

        /// <summary>RpcOnKilledの、吹き飛ばさずぺっちゃんこに潰れる版</summary>
        [PunRPC]
        private void RpcOnCrushed(int killerActorNumber, Vector3 sourcePosition)
        {
            if (_isDead) return;

            _hp.Value = 0;
            _damaged.OnNext(_maxHp);
            Die(killerActorNumber, sourcePosition, isCrushed: true);
        }

        [PunRPC]
        private void RpcRevive(Vector3 position)
        {
            CancelKnockback();
            _crushCts?.Cancel();
            _crushCts?.Dispose();
            _crushCts = null;
            _bodyRoot.localScale = _bodyDefaultLocalScale;
            _isDead = false;
            _hp.Value = _maxHp;
            _respawnRemainingSec.Value = 0.0f;
            Teleport(position);
            SetAlive(true);
            SetDeathPose(false);

            // リスポーン直後はボス(敵)を画面に捉えた向きから再開する(自分のキャラのみ)
            if (photonView.IsMine) AimCameraAtBoss();

            _revived.OnNext(Unit.Default);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>死亡処理本体。ApplyKill(即死)・ApplyDamage(HP0到達)のどちらからも呼ばれる</summary>
        private void Die(int killerActorNumber, Vector3 sourcePosition, bool isCrushed = false)
        {
            _isDead = true;
            SetAlive(false);
            _died.OnNext(Unit.Default);

            if (isCrushed)
            {
                // 挟み潰された場合、通常死亡の"お腹が上"ポーズ(ローカルZ軸90度回転)を適用すると
                // ローカルY軸が世界の上方向からズレてしまい、後段のスケール潰しが縦(上下)に効かなくなる。
                // そのため通常の死亡ポーズは適用せず、直立姿勢のまま真上から押し潰したような見た目にする
                SetDeathPose(false);

                _crushCts?.Cancel();
                _crushCts?.Dispose();
                _crushCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
                PlayCrushVisualAsync(_crushCts.Token).Forget();
            }
            else
            {
                SetDeathPose(true);

                // 死亡時は当たり判定が切れているので、放物線を描く大きな吹き飛びになる
                StartKnockback(sourcePosition, _deathKnockbackDistance, _deathKnockbackDurationSec, _deathKnockbackArcHeight);
            }

            // 被弾エフェクトは全員のクライアントで出す。この RPC が全員に届くので追加の通信は不要
            if (_biteVfxPrefab != null)
            {
                BiteVfx.Spawn(_biteVfxPrefab, transform.position + Vector3.up * _biteVfxHeight);
            }

            // 死んだ本人が自分の死亡数を加算し、復帰も自分で行う
            if (photonView.IsMine)
            {
                BattleScore.AddLocalDeath();
                RespawnAfterDelayAsync().Forget();
            }

            // 倒した本人(プレイヤーの攻撃であれば)が自分の撃破数を加算する。
            // killerActorNumber が -1 (モンスターなど、プレイヤーではないダメージ源)のときは加算しない
            if (killerActorNumber >= 0 && killerActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
            {
                BattleScore.AddLocalKill();
            }
        }

        // ---- 吹き飛び ------------------------------------

        /// <summary>いま吹き飛ばしを止めたい状態か。死亡時の吹き飛びには使わない</summary>
        private bool IsKnockbackBlocked()
        {
            if (!_blockKnockbackWhileBeam) return false;

            // 被弾時にしか呼ばれないので、毎フレームの取得にはならない
            PlayerBeamSkill beamSkill = GetComponent<PlayerBeamSkill>();
            return beamSkill != null && beamSkill.IsInBeamAction;
        }

        /// <summary>
        /// 発生源から離れる方向への吹き飛びを開始する。
        /// 位置は所有者のクライアントだけが動かし、他クライアントへは PhotonTransformView の位置同期で伝わる。
        /// 発生源が自分と同じ座標(発生源なしの旧APIから呼ばれた場合)のときは何もしない。
        /// </summary>
        private void StartKnockback(Vector3 sourcePosition, float distance, float durationSec, float arcHeight)
        {
            if (!photonView.IsMine) return;
            if (distance <= 0.0f) return;

            Vector3 direction = transform.position - sourcePosition;
            direction.y = 0.0f;

            // 真上・真下から受けた攻撃(ボスのスタンプを真下で受けた場合など)は水平方向が決まらない。
            // そのまま吹き飛びを止めると発生源の真下に留まり続けてしまうので、向いている方向へ逃がす
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = transform.forward;
                direction.y = 0.0f;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            }

            direction.Normalize();

            CancelKnockback();
            _knockbackCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            KnockbackAsync(direction, distance, durationSec, arcHeight, _knockbackCts.Token).Forget();
        }

        private void CancelKnockback()
        {
            if (_knockbackCts == null) return;
            _knockbackCts.Cancel();
            _knockbackCts.Dispose();
            _knockbackCts = null;
        }

        /// <summary>
        /// 手のひらで挟み潰されたときの演出。体のスケールを短時間で押し潰した形に変形させる。
        /// 潰れたままリスポーンまで維持し、RpcRevive で元のスケールに戻す。
        /// </summary>
        private async UniTaskVoid PlayCrushVisualAsync(CancellationToken ct)
        {
            // 手のひらで左右から挟まれるイメージなので、体の左右(ローカルX)方向を潰し、
            // 潰れたぶん高さ(Y)と前後(Z)へ広がるようにする
            Vector3 targetScale = new Vector3(
                _bodyDefaultLocalScale.x * _crushSquashScaleX,
                _bodyDefaultLocalScale.y * _crushSquashScaleYZMultiplier,
                _bodyDefaultLocalScale.z * _crushSquashScaleYZMultiplier);

            try
            {
                float elapsed = 0.0f;
                while (elapsed < _crushDurationSec)
                {
                    ct.ThrowIfCancellationRequested();
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / _crushDurationSec);
                    _bodyRoot.localScale = Vector3.Lerp(_bodyDefaultLocalScale, targetScale, t);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                _bodyRoot.localScale = targetScale;
            }
            catch (System.OperationCanceledException)
            {
                // キャンセル時(リスポーン等)は呼び出し元でスケールを戻すので何もしない
            }
        }

        /// <summary>
        /// 減速しながら指定距離だけ吹き飛ぶ。arcHeight が正なら放物線の浮き上がりも加える(死亡時用)。
        /// CharacterController が有効な間は Move で動かして壁に引っかかり、無効(死亡中)なら座標を直接動かして突き抜ける。
        /// </summary>
        private async UniTaskVoid KnockbackAsync(Vector3 direction, float distance, float durationSec, float arcHeight, CancellationToken ct)
        {
            try
            {
                float elapsed = 0.0f;
                float prevHorizontal = 0.0f;
                float prevVertical = 0.0f;

                while (elapsed < durationSec)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    elapsed += Time.deltaTime;

                    float t = Mathf.Clamp01(elapsed / durationSec);

                    // 水平方向: ease-out で勢いよく飛び出して減速する
                    float horizontal = distance * (1.0f - (1.0f - t) * (1.0f - t));

                    // 垂直方向: 頂点 arcHeight の放物線(開始高さに戻ってくる)
                    float vertical = arcHeight * 4.0f * t * (1.0f - t);

                    Vector3 delta = direction * (horizontal - prevHorizontal) + Vector3.up * (vertical - prevVertical);
                    prevHorizontal = horizontal;
                    prevVertical = vertical;

                    if (_controller != null && _controller.enabled)
                    {
                        _controller.Move(delta);
                    }
                    else
                    {
                        transform.position += delta;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // リスポーンや新しい吹き飛びで止めただけなので何もしない
            }
        }

        private async UniTaskVoid RespawnAfterDelayAsync()
        {
            var ct = destroyCancellationToken;

            // 先に残り秒数を満タンにしておき、死亡アニメーション中もカウントダウンUIが総時間から表示できるようにする
            _respawnRemainingSec.Value = _respawnDelaySec;

            // 死亡アニメーションを見せる時間。現状は0秒なので、即リスポーン待機に入る
            if (_deathAnimationSec > 0.0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_deathAnimationSec), cancellationToken: ct);
            }

            // リスポーン待機。毎フレーム残り秒数を流し、UIがカウントダウン表示する
            float remaining = _respawnDelaySec;
            while (remaining > 0.0f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                remaining -= Time.deltaTime;
                _respawnRemainingSec.Value = Mathf.Max(0.0f, remaining);
            }

            photonView.RPC(nameof(RpcRevive), RpcTarget.All, PickRespawnPosition());
        }

        /// <summary>ドーナツ状の範囲から、面積が偏らないようにランダムなリスポーン位置を選ぶ</summary>
        private Vector3 PickRespawnPosition()
        {
            float min = Mathf.Min(_respawnMinRadius, _respawnMaxRadius);
            float max = Mathf.Max(_respawnMinRadius, _respawnMaxRadius);

            // 半径を単純な乱数にすると中心寄りに偏るため、面積が均等になるように選ぶ
            float radius = Mathf.Sqrt(Mathf.Lerp(min * min, max * max, UnityEngine.Random.value));
            float radian = UnityEngine.Random.value * 2.0f * Mathf.PI;

            return new Vector3(Mathf.Cos(radian) * radius, _respawnHeight, Mathf.Sin(radian) * radius);
        }

        /// <summary>
        /// 自分を追いかけているサードパーソンカメラを、ボス(敵)の方へ向ける。
        /// ボスやカメラが居ないシーン(Battleなど)では何もしない。
        /// </summary>
        private void AimCameraAtBoss()
        {
            var camera = FindAnyObjectByType<ThirdPersonCamera>();
            if (camera == null || camera.Target != transform) return;

            var boss = FindAnyObjectByType<ProjectKMP.Monster.BossHealth>();
            if (boss == null) return;

            camera.AimAt(boss.transform.position);
        }

        /// <summary>CharacterController が有効なままだと位置を代入しても戻されるので、一度切る</summary>
        private void Teleport(Vector3 position)
        {
            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = wasEnabled;
        }

        private void SetAlive(bool alive)
        {
            // 死亡中も見た目(お腹が上のポーズ)を見せたいので、Rendererはここでは触らない

            // 死亡中は弾が当たらないよう当たり判定も切る
            _controller.enabled = alive;

            // 操作系は自分のキャラだけ切り替える。他人のキャラは NetworkOwnerGate が無効化済みで、
            // ここで有効に戻してしまうと入力が二重に走るため触らない
            if (!photonView.IsMine) return;

            if (_mover != null) _mover.enabled = alive;

            foreach (var behaviour in _disableWhileDead)
            {
                if (behaviour != null) behaviour.enabled = alive;
            }
        }

        /// <summary>死亡ポーズ(お腹が上)にする/元の姿勢に戻す</summary>
        private void SetDeathPose(bool isDead)
        {
            if (_bodyRoot == null) return;

            // 正面方向(ローカルZ軸)を軸に180度倒すことで、進行方向を保ったまま
            // 上下だけがひっくり返り、お腹が上を向いた状態になる
            _bodyRoot.localRotation = isDead
                ? _bodyDefaultLocalRotation * Quaternion.Euler(0f, 0f, 90f)
                : _bodyDefaultLocalRotation;
        }
    }
}
