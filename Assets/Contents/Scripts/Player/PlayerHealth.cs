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
    /// 死亡時は見た目(Body)をお腹が上を向く形にひっくり返し、生き返るときに元へ戻す。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerHealth : MonoBehaviourPun
    {
        // ---- 定数 ----------------------------------------
        private const float RESPAWN_DELAY_SEC = 3.0f;
        private const float RESPAWN_RADIUS    = 8.0f;
        private const float RESPAWN_HEIGHT    = 1.0f;

        // ---- HP -------------------------------------------
        [Header("HP")]
        [SerializeField, Min(1), Tooltip("最大HP。弾は1発で即死(ApplyKill)だが、段階ダメージを与える攻撃(破壊光線など)はここから減っていく")]
        private int _maxHp = 10;

        // ---- 参照 ----------------------------------------
        [SerializeField] private BiteVfx _biteVfxPrefab;
        [SerializeField] private float _biteVfxHeight = 1.1f;

        [Header("死亡ポーズ")]
        [SerializeField, Tooltip("死亡時にひっくり返す見た目のルート。未設定なら子の\"Body\"を自動で探す")]
        private Transform _bodyRoot;

        // ---- 内部状態 ------------------------------------
        private CharacterController _controller;
        private PlayerMover _mover;
        private Renderer[] _renderers;
        private bool _isDead;
        private int _currentHp;
        private readonly Subject<int> _hpChanged = new Subject<int>();
        private Quaternion _bodyDefaultLocalRotation;

        // ---- 公開API -------------------------------------

        public bool IsDead => _isDead;

        /// <summary>最大HP</summary>
        public int MaxHp => _maxHp;

        /// <summary>現在のHP</summary>
        public int CurrentHp => _currentHp;

        /// <summary>HPが変化するたびに、変化後の値を流す(HPバーなどのUIから購読する想定)</summary>
        public Observable<int> HpChanged => _hpChanged;

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
            _mover = GetComponent<PlayerMover>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _currentHp = _maxHp;

            if (_bodyRoot == null)
            {
                Transform found = transform.Find("Body");
                _bodyRoot = found != null ? found : transform;
            }
            _bodyDefaultLocalRotation = _bodyRoot.localRotation;
        }

        private void OnDestroy()
        {
            _hpChanged.Dispose();
        }

        // ---- RPC -----------------------------------------

        [PunRPC]
        private void RpcOnDamaged(int damage, int attackerActorNumber, PhotonMessageInfo info)
        {
            if (_isDead) return;

            _currentHp = Mathf.Max(0, _currentHp - damage);
            _hpChanged.OnNext(_currentHp);

            if (_currentHp <= 0)
            {
                Die(attackerActorNumber);
            }
        }

        [PunRPC]
        private void RpcOnKilled(int killerActorNumber)
        {
            if (_isDead) return;

            _currentHp = 0;
            _hpChanged.OnNext(_currentHp);
            Die(killerActorNumber);
        }

        [PunRPC]
        private void RpcRevive(Vector3 position)
        {
            _isDead = false;
            _currentHp = _maxHp;
            _hpChanged.OnNext(_currentHp);
            Teleport(position);
            SetAlive(true);
            SetDeathPose(false);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>死亡処理本体。ApplyKill(即死)・ApplyDamage(HP0到達)のどちらからも呼ばれる</summary>
        private void Die(int killerActorNumber)
        {
            _isDead = true;
            SetAlive(false);
            SetDeathPose(true);

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
            await UniTask.Delay(TimeSpan.FromSeconds(RESPAWN_DELAY_SEC),
                cancellationToken: destroyCancellationToken);

            Vector2 circle = UnityEngine.Random.insideUnitCircle * RESPAWN_RADIUS;
            Vector3 position = new Vector3(circle.x, RESPAWN_HEIGHT, circle.y);

            photonView.RPC(nameof(RpcRevive), RpcTarget.All, position);
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

            if (_mover != null && photonView.IsMine) _mover.enabled = alive;
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
