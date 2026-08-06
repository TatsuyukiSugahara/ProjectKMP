using System;
using ProjectKMP.Attack;
using ProjectKMP.Player;
using R3;
using UnityEngine;

namespace ProjectKMP.Dog
{
    /// <summary>
    /// 犬(Husky)のアニメーション制御。
    ///
    /// 移動アニメは「実際に動いた距離」から速さを求めて切り替えるため、
    /// 入力を持つ自分のキャラでも、PhotonTransformView で位置だけ書き込まれる他人のキャラでも、
    /// 同じコードで正しく歩き/走りが再生される。
    ///
    /// 頭突き(Attack)は PlayerAttack が全クライアントで発火するイベントを購読して再生する。
    /// RPC はすでに PlayerAttack 側にあるので、アニメ用のRPCを追加する必要はない。
    /// </summary>
    public class DogAnimationDriver : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private const string ANIM_IDLE = "Idle_A";
        private const string ANIM_WALK = "Walk";
        private const string ANIM_RUN = "Run";
        private const string ANIM_ATTACK = "Attack";

        /// <summary>Idle_A/Walk/Run/Attack は Base Layer にのみ存在する(Shapekeyレイヤーとは分離)</summary>
        private const int BASE_LAYER = 0;

        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("再生対象の Animator。未設定なら子から自動で探す")]
        private Animator _animator;

        [SerializeField, Tooltip("攻撃を購読する相手。未設定なら同じオブジェクトから探す")]
        private PlayerAttack _playerAttack;

        [SerializeField, Tooltip("この速さ(m/秒)を超えたら歩きに切り替える")]
        private float _walkSpeedThreshold = 0.15f;

        [SerializeField, Tooltip("この速さ(m/秒)を超えたら走りに切り替える。ダッシュ時の速度に合わせる")]
        private float _runSpeedThreshold = 7.5f;

        [SerializeField, Tooltip("アニメを切り替えるときの補間時間(秒)")]
        private float _crossFadeSec = 0.15f;

        [SerializeField, Tooltip("頭突きモーションの長さ(秒)。この間は移動アニメに戻さない")]
        private float _attackDurationSec = 0.4166667f;

        [SerializeField, Tooltip("速さのなめらかさ。大きいほど反応が速い。通信のガタつきをならすために使う")]
        private float _speedSmoothing = 12.0f;

        // ---- 内部状態 ------------------------------------

        private IDisposable _attackSubscription;
        private Vector3 _lastPosition;
        private float _speed;
        private float _attackRemainSec;
        private string _currentState = string.Empty;
        private bool _isHoldingAttackPose;
        private bool _isPoseFrozen;
        private float _holdNormalizedTime;

        // ---- 公開API -------------------------------------

        /// <summary>頭突きモーションの再生中かどうか</summary>
        public bool IsAttacking => _attackRemainSec > 0.0f;

        /// <summary>アニメ判定に使っている水平方向の速さ(m/秒)</summary>
        public float CurrentSpeed => _speed;

        /// <summary>頭突き(Attack)モーションを再生する。全クライアントで呼ばれる想定</summary>
        public void PlayAttack()
        {
            if (_animator == null) return;

            _attackRemainSec = Mathf.Max(0.01f, _attackDurationSec);

            // 連続で噛みついたときも頭突きを出し直したいので、同じステートでも強制的に再生する
            CrossFadeTo(ANIM_ATTACK, true);
        }

        /// <summary>
        /// 頭突き(Attack)モーションを指定位置(0〜1)で一時停止して保持する。
        /// ビームの照射中など「頭を突き出したまま止める」演出に使う。
        /// 解除するまで移動アニメには戻らない。全クライアントで呼ばれる想定。
        /// </summary>
        public void HoldAttackPose(float freezeNormalizedTime)
        {
            if (_animator == null) return;

            _isHoldingAttackPose = true;
            _isPoseFrozen = false;
            _holdNormalizedTime = Mathf.Clamp01(freezeNormalizedTime);

            CrossFadeTo(ANIM_ATTACK, true);
        }

        /// <summary>
        /// 保持していた頭突きモーションを再開する。残りを最後まで再生してから移動アニメへ戻る。
        /// </summary>
        public void ReleaseAttackPose()
        {
            if (!_isHoldingAttackPose) return;

            _isHoldingAttackPose = false;
            _isPoseFrozen = false;

            if (_animator != null) _animator.speed = 1.0f;

            // 止めた位置から先の残り時間ぶんだけ、移動アニメへの復帰を待つ
            _attackRemainSec = Mathf.Max(0.01f, _attackDurationSec * (1.0f - _holdNormalizedTime));
        }

        // ---- Unityイベント -------------------------------

        private void Reset()
        {
            _animator = GetComponentInChildren<Animator>(true);
            _playerAttack = GetComponent<PlayerAttack>();
        }

        private void Awake()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
            if (_playerAttack == null) _playerAttack = GetComponent<PlayerAttack>();

            if (_animator == null)
            {
                Debug.LogError("[Dog] Animator が見つかりません。犬のモデルが子に入っているか確認してください", this);
            }

            _lastPosition = transform.position;
        }

        private void OnEnable()
        {
            if (_playerAttack == null) return;

            // RpcPlayAttack は全員のクライアントで呼ばれるので、購読するだけで頭突きが全画面で揃う
            _attackSubscription = _playerAttack.AttackStarted.Subscribe(OnAttackStarted);
        }

        private void OnDisable()
        {
            _attackSubscription?.Dispose();
            _attackSubscription = null;
        }

        private void Update()
        {
            UpdateSpeed();

            // ポーズ保持中は、頭を突き出した位置に達したところでアニメを止めて待つ
            if (_isHoldingAttackPose)
            {
                UpdateAttackPoseHold();
                return;
            }

            if (_attackRemainSec > 0.0f)
            {
                _attackRemainSec -= Time.deltaTime;
                if (_attackRemainSec > 0.0f) return;
            }

            ApplyMoveAnimation();
        }

        // ---- 内部処理 ------------------------------------

        private void OnAttackStarted(AttackData data)
        {
            PlayAttack();
        }

        /// <summary>頭突きモーションが指定位置まで進んだらアニメを一時停止する</summary>
        private void UpdateAttackPoseHold()
        {
            if (_isPoseFrozen || _animator == null) return;

            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(BASE_LAYER);
            if (state.IsName(ANIM_ATTACK) && state.normalizedTime >= _holdNormalizedTime)
            {
                _animator.speed = 0.0f;
                _isPoseFrozen = true;
            }
        }

        /// <summary>
        /// 前フレームからの移動量から速さを求める。
        /// CharacterController.velocity を使わないのは、他人のキャラでは値がゼロのままになるため。
        /// </summary>
        private void UpdateSpeed()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0.0f) return;

            Vector3 current = transform.position;
            Vector3 moved = current - _lastPosition;
            moved.y = 0.0f;
            _lastPosition = current;

            float instantSpeed = moved.magnitude / deltaTime;

            // 通信では位置がまとめて届くため、そのまま使うと歩き/停止がチカチカする
            float t = 1.0f - Mathf.Exp(-Mathf.Max(0.01f, _speedSmoothing) * deltaTime);
            _speed = Mathf.Lerp(_speed, instantSpeed, t);
        }

        /// <summary>いまの速さに合ったアニメを流す</summary>
        private void ApplyMoveAnimation()
        {
            if (_speed > _runSpeedThreshold) CrossFadeTo(ANIM_RUN);
            else if (_speed > _walkSpeedThreshold) CrossFadeTo(ANIM_WALK);
            else CrossFadeTo(ANIM_IDLE);
        }

        /// <summary>同じステートへの二重再生を避けつつ、なめらかに切り替える</summary>
        private void CrossFadeTo(string stateName, bool force = false)
        {
            if (_animator == null) return;
            if (!force && _currentState == stateName) return;

            _currentState = stateName;
            _animator.CrossFade(stateName, _crossFadeSec, BASE_LAYER);
        }
    }
}
