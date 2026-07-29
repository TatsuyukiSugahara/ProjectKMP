using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using ProjectKMP.Attack;
using ProjectKMP.UI;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 通常攻撃(かみつき)。操作している本人だけが入力と当たり判定を行い、
    /// 攻撃モーションとヒットエフェクトは RPC で全員のクライアントに配る。
    /// </summary>
    public class PlayerAttack : MonoBehaviourPun
    {
        // ---- 定数 ----------------------------------------

        private const int OVERLAP_BUFFER_SIZE = 32;

        // ---- インスペクタ設定 ------------------------------

        [Header("攻撃データ")]
        [SerializeField, Tooltip("使える攻撃の一覧。増やすと攻撃を切り替えられる")]
        private AttackData[] _attacks = new AttackData[0];

        [SerializeField, Tooltip("通常攻撃に使う攻撃データの番号")]
        private int _normalAttackIndex;

        [Header("判定の基準")]
        [SerializeField, Tooltip("判定の位置と向きの基準。未設定なら自分自身")]
        private Transform _hitOrigin;

        [Header("入力")]
        [SerializeField, Tooltip("スペースキーで攻撃する")]
        private bool _useSpaceKey = true;

        [SerializeField, Tooltip("Kキーで攻撃する(頭突き)")]
        private bool _useKKey = true;

        [SerializeField, Tooltip("ゲームパッドのAボタン(下ボタン)で攻撃する")]
        private bool _useGamepadSouth = true;

        [SerializeField, Tooltip("画面上の噛みつきボタンで攻撃する")]
        private bool _useTouchButton = true;

        [Header("デバッグ")]
        [SerializeField, Tooltip("当たった相手をコンソールに出す")]
        private bool _logHit = true;

        [SerializeField, Tooltip("選択中に判定の球をシーンビューへ表示する")]
        private bool _drawGizmo = true;

        // ---- 内部状態 ------------------------------------

        private readonly Collider[] _overlapBuffer = new Collider[OVERLAP_BUFFER_SIZE];
        private readonly HashSet<int> _hitObjectIds = new HashSet<int>();
        private readonly Subject<AttackData> _attackStarted = new Subject<AttackData>();
        private float _cooldownRemainSec;
        private float _cooldownTotalSec;
        private bool _isAttacking;

        // ---- 公開API -------------------------------------

        /// <summary>いま操作しているプレイヤーの攻撃。UI から参照する</summary>
        public static PlayerAttack Local { get; private set; }

        /// <summary>
        /// 攻撃モーションの開始。RPC 経由で全クライアントで発火するので、
        /// アニメーションやエフェクトなど「見た目」の再生はこれを購読すればよい。
        /// </summary>
        public Observable<AttackData> AttackStarted => _attackStarted;

        /// <summary>クールタイムの残り具合(1=撃った直後、0=撃てる)</summary>
        public float CooldownRatio01 =>
            _cooldownTotalSec <= 0f ? 0f : Mathf.Clamp01(_cooldownRemainSec / _cooldownTotalSec);

        /// <summary>攻撃モーション中かどうか</summary>
        public bool IsAttacking => _isAttacking;

        /// <summary>次に攻撃できるまでの残り秒数</summary>
        public float CooldownRemainSec => Mathf.Max(0f, _cooldownRemainSec);

        /// <summary>通常攻撃(かみつき)を出す。UIボタンからも呼べる</summary>
        public void TryNormalAttack()
        {
            TryAttack(_normalAttackIndex);
        }

        /// <summary>番号を指定して攻撃する。クールタイム中や攻撃中は何も起きない</summary>
        public bool TryAttack(int attackIndex)
        {
            if (!IsOwner) return false;
            if (_isAttacking) return false;
            if (_cooldownRemainSec > 0f) return false;

            AttackData data = GetAttack(attackIndex);
            if (data == null)
            {
                Debug.LogError($"[Attack] 攻撃データが設定されていません index={attackIndex}", this);
                return false;
            }

            _cooldownRemainSec = data.CooldownSec;
            _cooldownTotalSec = data.CooldownSec;
            photonView.RPC(nameof(RpcPlayAttack), RpcTarget.All, attackIndex);
            return true;
        }

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            if (IsOwner) Local = this;
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;
            _attackStarted.Dispose();
        }

        private void Update()
        {
            // 他人のキャラでは入力も判定も行わない
            if (!IsOwner) return;

            if (_cooldownRemainSec > 0f) _cooldownRemainSec -= Time.deltaTime;

            // 押しっぱなしの取りこぼしを防ぐため、クールタイム中でも入力自体は読み切る
            bool pressed = ReadAttackInput();
            if (!pressed) return;

            TryNormalAttack();
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmo) return;

            AttackData data = GetAttack(_normalAttackIndex);
            if (data == null || !data.DrawGizmo) return;

            Transform origin = _hitOrigin != null ? _hitOrigin : transform;
            Gizmos.color = data.GizmoColor;
            Gizmos.DrawSphere(origin.TransformPoint(data.HitOffset), data.HitRadius);
        }

        // ---- RPC -----------------------------------------

        /// <summary>攻撃の開始。全員のクライアントで呼ばれる</summary>
        [PunRPC]
        private void RpcPlayAttack(int attackIndex, PhotonMessageInfo info)
        {
            AttackData data = GetAttack(attackIndex);
            if (data == null) return;

            Transform origin = _hitOrigin != null ? _hitOrigin : transform;

            // 空振りでも出る攻撃エフェクトは全員のクライアントで再生する
            if (data.SwingEffectPrefab != null)
            {
                AttackEffect.Spawn(
                    data.SwingEffectPrefab,
                    origin.TransformPoint(data.SwingEffectOffset),
                    origin.rotation,
                    data.HitEffectScale,
                    data.HitEffectLifeSec);
            }

            // 攻撃モーションは全員のクライアントで再生する(頭突きアニメなど)
            _attackStarted.OnNext(data);

            // 当たり判定は操作している本人だけが取る。二重ヒットを防ぐため
            if (!IsOwner) return;
            RunHitDetectionAsync(data, attackIndex, destroyCancellationToken).Forget();
        }

        /// <summary>ヒットの通知。全員のクライアントでエフェクトを出す</summary>
        [PunRPC]
        private void RpcOnHit(int attackIndex, Vector3 hitPoint, int targetNetworkId, PhotonMessageInfo info)
        {
            AttackData data = GetAttack(attackIndex);
            if (data == null) return;

            HitTarget target = HitTarget.Find(targetNetworkId);

            // 相手ごとに専用のエフェクトが設定されていればそちらを優先する
            GameObject prefab = target != null && target.OverrideHitEffectPrefab != null
                ? target.OverrideHitEffectPrefab
                : data.HitEffectPrefab;

            Vector3 basePosition = target != null ? target.GetEffectPosition(hitPoint) : hitPoint;
            Vector3 position = basePosition + data.HitEffectOffset;

            Vector3 toTarget = position - transform.position;
            Quaternion rotation = toTarget.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(toTarget.normalized, Vector3.up)
                : transform.rotation;

            AttackEffect.Spawn(prefab, position, rotation, data.HitEffectScale, data.HitEffectLifeSec);

            int attackerActorNumber = info.Sender != null ? info.Sender.ActorNumber : -1;
            SpawnDamagePopup(data, hitPoint);

            if (target != null) target.NotifyHit(position, attackerActorNumber, data.AttackPower);

            if (_logHit)
            {
                string targetName = target != null ? target.name : "(不明)";
                Debug.Log($"[Attack] {data.DisplayName} がヒット target={targetName} pos={position}");
            }
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>当たった位置にダメージの数字を出す</summary>
        private void SpawnDamagePopup(AttackData data, Vector3 hitPoint)
        {
            if (data.DamagePopupPrefab == null) return;

            GameObject popup = Instantiate(data.DamagePopupPrefab, hitPoint + data.DamagePopupOffset, Quaternion.identity);
            DamagePopup component = popup.GetComponent<DamagePopup>();
            if (component != null) component.Play(data.AttackPower);
        }

        /// <summary>このクライアントがこのキャラを操作しているか</summary>
        private bool IsOwner => photonView == null || photonView.IsMine;

        private AttackData GetAttack(int index)
        {
            if (_attacks == null) return null;
            if (index < 0 || index >= _attacks.Length) return null;
            return _attacks[index];
        }

        /// <summary>スペース / ゲームパッドAボタン / 画面上の噛みつきボタン</summary>
        private bool ReadAttackInput()
        {
            bool pressed = false;

            if (_useSpaceKey)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) pressed = true;
            }

            if (_useKKey)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.kKey.wasPressedThisFrame) pressed = true;
            }

            if (_useGamepadSouth)
            {
                Gamepad gamepad = Gamepad.current;
                if (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame) pressed = true;
            }

            if (_useTouchButton)
            {
                TouchControls touch = TouchControls.Instance;
                // 押した瞬間を取りこぼさないよう、押されていなくても必ず読み取る
                if (touch != null && touch.ConsumeAttackPress()) pressed = true;
            }

            return pressed;
        }

        /// <summary>設定された時間だけ球の判定を出し、当たった相手を全員に伝える</summary>
        private async UniTaskVoid RunHitDetectionAsync(AttackData data, int attackIndex, CancellationToken token)
        {
            _isAttacking = true;
            _hitObjectIds.Clear();

            try
            {
                if (data.HitStartSec > 0f)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(data.HitStartSec), cancellationToken: token);
                }

                float elapsed = 0f;
                int hitCount = 0;

                // 1フレームだけでは速く動く相手をすり抜けるので、出ている間は毎フレーム調べる
                while (elapsed < data.HitDurationSec)
                {
                    hitCount += DetectHits(data, attackIndex, hitCount);
                    if (hitCount >= data.MaxHitCount) break;

                    elapsed += Time.deltaTime;
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
            }
            finally
            {
                _isAttacking = false;
            }
        }

        /// <summary>球の中の相手を調べ、当たった相手ぶんだけ RPC を送る。戻り値は新しく当たった数</summary>
        private int DetectHits(AttackData data, int attackIndex, int alreadyHitCount)
        {
            Transform origin = _hitOrigin != null ? _hitOrigin : transform;
            Vector3 center = origin.TransformPoint(data.HitOffset);

            int count = Physics.OverlapSphereNonAlloc(
                center, data.HitRadius, _overlapBuffer, data.TargetLayers, QueryTriggerInteraction.Collide);

            int newHitCount = 0;

            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapBuffer[i];
                if (collider == null) continue;

                // 自分自身には当たらない
                if (collider.transform == transform || collider.transform.IsChildOf(transform)) continue;

                HitTarget target = collider.GetComponentInParent<HitTarget>();
                GameObject targetObject = target != null ? target.gameObject : collider.gameObject;

                if (!data.CanHit(target, targetObject)) continue;

                // 同じ相手には1回の攻撃で1度だけ当たる
                int id = targetObject.GetInstanceID();
                if (!_hitObjectIds.Add(id)) continue;

                Vector3 hitPoint = collider.ClosestPoint(center);
                int targetNetworkId = target != null ? target.NetworkId : 0;

                photonView.RPC(nameof(RpcOnHit), RpcTarget.All, attackIndex, hitPoint, targetNetworkId);

                newHitCount++;
                if (alreadyHitCount + newHitCount >= data.MaxHitCount) break;
            }

            return newHitCount;
        }
    }
}
