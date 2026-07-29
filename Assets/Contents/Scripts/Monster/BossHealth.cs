using System;
using Photon.Pun;
using ProjectKMP.Attack;
using ProjectKMP.UI.InGame;
using R3;
using UnityEngine;
using UnityEngine.Events;

namespace ProjectKMP.Monster
{
    /// <summary>
    /// ボスのHP。攻撃が当たったら AttackData の攻撃力ぶん減らし、画面上部のゲージに反映する。
    /// 最大HPは人数が多いほど増える。決めるのは MasterClient だけで、値は SyncObject 経由で
    /// 全員に配られるため、ゲストは配られた最大HPをそのまま使う(自分で計算し直さない)。
    /// HPを実際に減らすのも MasterClient だけなので、クライアントごとにズレない。
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class BossHealth : MonoBehaviourPun
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("最大HP")]
        [SerializeField, Min(1), Tooltip("ひとりで遊ぶときの最大HP。ここを基準に人数ぶん増える")]
        private int _baseMaxHp = 100;

        [SerializeField, Min(0), Tooltip("プレイヤーが1人増えるごとに足すHP。0にすると人数に関係なく基礎値のまま")]
        private int _hpPerExtraPlayer = 50;

        [SerializeField, Min(1), Tooltip("HPの計算に使う人数の上限。大人数でも長引きすぎないよう頭打ちにする")]
        private int _playerCountLimit = 8;

        [Header("参照")]
        [SerializeField, Tooltip("攻撃を受け取る HitTarget。未設定なら同じ GameObject から探す")]
        private HitTarget _hitTarget;

        [SerializeField, Tooltip("HPの同期。未設定なら同じ GameObject から探す")]
        private MonsterSyncObject _sync;

        [SerializeField, Tooltip("画面上部のHPゲージ。未設定ならシーンから探す")]
        private BossHealthGauge _gauge;

        [Header("動作確認")]
        [SerializeField, Tooltip("Photon に繋がっていないときも、このクライアントだけでHPを動かす")]
        private bool _allowOfflineTest = true;

        [SerializeField, Tooltip("ダメージと最大HPの決定をコンソールに出す")]
        private bool _logDamage = true;

        [Header("イベント")]
        [SerializeField, Tooltip("HPが0になったときに実行したい処理")]
        private UnityEvent _onDefeated;

        // ---- 内部状態 ------------------------------------

        private IDisposable _hitSubscription;
        private IDisposable _syncSubscription;

        /// <summary>開始時に人数から決めた最大HP。以降は人数が変わっても動かさない</summary>
        private int _resolvedMaxHp;

        /// <summary>Photon に繋がっていないときだけ使う、このクライアント限りのHP</summary>
        private int _offlineHp;

        private bool _isDefeated;

        // ---- 公開API -------------------------------------

        /// <summary>ひとりで遊ぶときの最大HP</summary>
        public int BaseMaxHp => _baseMaxHp;

        /// <summary>実際に使われている最大HP(人数ぶん増えた後の値)</summary>
        public int MaxHp => _resolvedMaxHp;

        /// <summary>すでに倒されているか</summary>
        public bool IsDefeated => _isDefeated;

        /// <summary>
        /// 指定した人数のときの最大HPを返す。
        /// 基礎値 + 1人あたりの加算 x (人数 - 1)。人数は上限で頭打ちにする。
        /// </summary>
        public int CalcMaxHp(int playerCount)
        {
            int count = Mathf.Clamp(playerCount, 1, Mathf.Max(1, _playerCountLimit));
            return _baseMaxHp + _hpPerExtraPlayer * (count - 1);
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            if (_hitTarget == null) _hitTarget = GetComponent<HitTarget>();
            if (_sync == null) _sync = GetComponent<MonsterSyncObject>();
            if (_gauge == null) _gauge = FindAnyObjectByType<BossHealthGauge>(FindObjectsInactive.Include);
        }

        private void Start()
        {
            if (_hitTarget == null)
            {
                Debug.LogError("[Boss] HitTarget が見つからないためダメージを受け取れません", this);
                return;
            }

            int playerCount = GetPlayerCount();
            _resolvedMaxHp = CalcMaxHp(playerCount);
            _offlineHp = _resolvedMaxHp;

            if (_sync != null)
            {
                // 受け取った値でゲージを更新する。自分がマスターでも同じ経路を通す
                _syncSubscription = _sync.Value.Subscribe(OnSyncValueChanged);

                // 最大HPと初期HPを決めるのは MasterClient だけ。ゲストは配られた値に従う
                if (HasAuthority)
                {
                    if (_logDamage) Debug.Log($"[Boss] 最大HP {_resolvedMaxHp} に決定 (プレイヤー {playerCount} 人 / 基礎 {_baseMaxHp} + {_hpPerExtraPlayer} x {playerCount - 1})", this);

                    _sync.SetValue(data =>
                    {
                        data.MaxHP = _resolvedMaxHp;
                        data.HP = _resolvedMaxHp;
                        data.State = MonsterState.Idle;
                    });
                }
            }
            else
            {
                Debug.LogWarning("[Boss] MonsterSyncObject が無いため、HPはこのクライアント限りになります", this);
                ApplyToGauge(_offlineHp, _resolvedMaxHp, true);
            }

            _hitSubscription = _hitTarget.Hit.Subscribe(OnHit);

            if (_gauge != null) _gauge.SetVisible(true);
        }

        private void OnDestroy()
        {
            _hitSubscription?.Dispose();
            _syncSubscription?.Dispose();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>Photon が動いているか(オフラインモードも含む)</summary>
        private bool IsPhotonReady => PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode;

        /// <summary>HPを実際に減らしてよい側か</summary>
        private bool HasAuthority => IsPhotonReady ? PhotonNetwork.IsMasterClient : _allowOfflineTest;

        /// <summary>いま部屋にいる人数。部屋に入っていなければ1人として扱う</summary>
        private int GetPlayerCount()
        {
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            {
                return Mathf.Max(1, PhotonNetwork.CurrentRoom.PlayerCount);
            }
            return 1;
        }

        /// <summary>ヒットは全クライアントで流れてくるが、HPを減らすのは権限を持つ側だけ</summary>
        private void OnHit(HitTarget.HitInfo info)
        {
            if (_isDefeated) return;
            if (info.Damage <= 0) return;
            if (!HasAuthority) return;

            if (IsPhotonReady && _sync != null)
            {
                _sync.SetValue(data =>
                {
                    // 初期化前に当たった場合に備えて、最大HPもここで埋めておく
                    if (data.MaxHP <= 0) { data.MaxHP = _resolvedMaxHp; data.HP = _resolvedMaxHp; }

                    data.HP = Mathf.Max(0, data.HP - info.Damage);
                    if (data.HP <= 0) data.State = MonsterState.Dead;
                });

                if (_logDamage) Debug.Log($"[Boss] ダメージ {info.Damage} / 残り {_sync.Value.CurrentValue.HP} / {_sync.Value.CurrentValue.MaxHP}", this);
                return;
            }

            // Photon に繋がっていないときの動作確認用
            _offlineHp = Mathf.Max(0, _offlineHp - info.Damage);
            if (_logDamage) Debug.Log($"[Boss] ダメージ {info.Damage} / 残り {_offlineHp} / {_resolvedMaxHp} (オフライン)", this);

            ApplyToGauge(_offlineHp, _resolvedMaxHp, false);
            CheckDefeated(_offlineHp);
        }

        /// <summary>配られてきたHPをゲージに反映する</summary>
        private void OnSyncValueChanged(MonsterSyncData data)
        {
            // MaxHP が 0 のうちはまだ MasterClient が初期値を配っていない
            if (data == null || data.MaxHP <= 0) return;

            // 最大HPはマスターが決めた値が正。ゲストは自分の計算結果を上書きする
            _resolvedMaxHp = data.MaxHP;

            bool isFirst = !_isDefeated && data.HP == data.MaxHP;
            ApplyToGauge(data.HP, data.MaxHP, isFirst);
            CheckDefeated(data.HP);
        }

        private void ApplyToGauge(int current, int max, bool immediate)
        {
            if (_gauge == null) return;

            if (immediate) _gauge.SetRatioImmediate(max <= 0 ? 0.0f : current / (float)max);
            else _gauge.SetHealth(current, max);
        }

        private void CheckDefeated(int currentHp)
        {
            if (_isDefeated || currentHp > 0) return;

            _isDefeated = true;
            Debug.Log("[Boss] 倒れました", this);

            // これ以上攻撃が当たらないようにする
            if (_hitTarget != null) _hitTarget.SetCanBeHit(false);

            _onDefeated?.Invoke();
        }
    }
}
