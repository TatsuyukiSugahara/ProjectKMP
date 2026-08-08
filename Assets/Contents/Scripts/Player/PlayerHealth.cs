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
    /// HP・死亡・カウントダウンは Observable で公開しており、UI(PlayerHpHud)や
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

        // ---- 内部状態 ------------------------------------

        private CharacterController _controller;
        private Behaviour _mover;
        private bool _isDead;
        private Quaternion _bodyDefaultLocalRotation;
        private CancellationTokenSource _knockbackCts;

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

        /// <summary>撃った側のクライアントから呼ぶ。HPに関わらず即座に全員に死亡を伝える(弾など)。吹き飛びなし</summary>
        public void ApplyKill(int killerActorNumber)
        {
            ApplyKill(killerActorNumber, transform.position);
        }

        /// <summary>発生源の座標つきの即死。発生源から離れる方向へ死亡時の大きな吹き飛びが入る</summary>
        public void ApplyKill(int killerActorNumber, Vector3 sourcePosition)
        {
            if (_isDead) return;
            photonView.RPC(nameof(RpcOnKilled), RpcTarget.All, killerActorNumber, sourcePosition);
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
            if (_isDead) return;
            if (damage <= 0) return;
            photonView.RPC(nameof(RpcOnDamaged), RpcTarget.All, damage, attackerActorNumber, sourcePosition);
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
        }

        private void OnDestroy()
        {
            CancelKnockback();
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

        [PunRPC]
        private void RpcOnKilled(int killerActorNumber, Vector3 sourcePosition)
        {
            if (_isDead) return;

            _hp.Value = 0;
            _damaged.OnNext(_maxHp);
            Die(killerActorNumber, sourcePosition);
        }

        [PunRPC]
        private void RpcRevive(Vector3 position)
        {
            CancelKnockback();
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
        private void Die(int killerActorNumber, Vector3 sourcePosition)
        {
            _isDead = true;
            SetAlive(false);
            SetDeathPose(true);
            _died.OnNext(Unit.Default);

            // 死亡時は当たり判定が切れているので、放物線を描く大きな吹き飛びになる
            StartKnockback(sourcePosition, _deathKnockbackDistance, _deathKnockbackDurationSec, _deathKnockbackArcHeight);

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
