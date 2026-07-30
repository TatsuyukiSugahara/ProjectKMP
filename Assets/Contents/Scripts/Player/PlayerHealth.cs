using System;
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

        private readonly ReactiveProperty<int> _hp = new ReactiveProperty<int>(0);
        private readonly ReactiveProperty<float> _respawnRemainingSec = new ReactiveProperty<float>(0.0f);
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

        /// <summary>死亡した瞬間に流れる。全クライアントで発火するので、死亡アニメーションを入れる場合はこれを購読する</summary>
        public Observable<Unit> Died => _died;

        /// <summary>リスポーンした瞬間に流れる。全クライアントで発火する</summary>
        public Observable<Unit> Revived => _revived;

        /// <summary>リスポーンまでの残り秒数。自分のキャラのクライアントでのみカウントダウンされる(UIのカウントダウン表示用)</summary>
        public ReadOnlyReactiveProperty<float> RespawnRemainingSec => _respawnRemainingSec;

        /// <summary>このプレイヤーを操作しているクライアントの ActorNumber</summary>
        public int OwnerActorNumber => photonView.Owner != null ? photonView.Owner.ActorNumber : -1;

        /// <summary>撃った側のクライアントから呼ぶ。HPに関わらず即座に全員に死亡を伝える(弾など)</summary>
        public void ApplyKill(int killerActorNumber)
        {
            if (_isDead) return;
            photonView.RPC(nameof(RpcOnKilled), RpcTarget.All, killerActorNumber);
        }

        /// <summary>
        /// ダメージ源のクライアントから呼ぶ。指定ぶんHPを減らし、全員に伝える。
        /// HPが0になった時点で ApplyKill と同じ死亡処理が走る。
        /// attackerActorNumber はプレイヤーによる攻撃でなければ -1 を渡してよい(撃破数の加算対象にならない)。
        /// </summary>
        public void ApplyDamage(int damage, int attackerActorNumber)
        {
            if (_isDead) return;
            if (damage <= 0) return;
            photonView.RPC(nameof(RpcOnDamaged), RpcTarget.All, damage, attackerActorNumber);
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
            _hp.Dispose();
            _respawnRemainingSec.Dispose();
            _died.Dispose();
            _revived.Dispose();
        }

        // ---- RPC -----------------------------------------

        [PunRPC]
        private void RpcOnDamaged(int damage, int attackerActorNumber, PhotonMessageInfo info)
        {
            if (_isDead) return;

            _hp.Value = Mathf.Max(0, _hp.Value - damage);

            if (_hp.Value <= 0)
            {
                Die(attackerActorNumber);
            }
        }

        [PunRPC]
        private void RpcOnKilled(int killerActorNumber)
        {
            if (_isDead) return;

            _hp.Value = 0;
            Die(killerActorNumber);
        }

        [PunRPC]
        private void RpcRevive(Vector3 position)
        {
            _isDead = false;
            _hp.Value = _maxHp;
            _respawnRemainingSec.Value = 0.0f;
            Teleport(position);
            SetAlive(true);
            SetDeathPose(false);
            _revived.OnNext(Unit.Default);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>死亡処理本体。ApplyKill(即死)・ApplyDamage(HP0到達)のどちらからも呼ばれる</summary>
        private void Die(int killerActorNumber)
        {
            _isDead = true;
            SetAlive(false);
            SetDeathPose(true);
            _died.OnNext(Unit.Default);

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
