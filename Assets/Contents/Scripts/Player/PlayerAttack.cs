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

        [SerializeField, Tooltip("通常攻撃に使う攻撃データの番号。コンボを使わないときのみ有効")]
        private int _normalAttackIndex;

        [Header("コンボ")]
        [SerializeField, Tooltip("続けて出す攻撃の順番。_attacks の番号を並べる。空ならコンボにならない")]
        private int[] _comboAttackIndices = new int[0];

        [SerializeField, Min(0.05f), Tooltip("前の攻撃からこの秒数を超えて間が空くと、1段目に戻る")]
        private float _comboResetSec = 1.2f;

        [Header("ジャスト入力")]
        [SerializeField, Min(0.0f), Tooltip("クールタイム終了直前のこの秒数だけ、次の一撃を受け付ける。0でジャストを使わない")]
        private float _justWindowSec = 0.25f;

        [SerializeField, Min(1.0f), Tooltip("ジャスト成功時の威力倍率")]
        private float _justDamageMultiplier = 1.4f;

        [SerializeField, Tooltip("ジャスト成功時の音")]
        private AudioClip _justClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("ジャスト成功音の音量")]
        private float _justVolume = 0.8f;

        [Header("回転攻撃")]
        [SerializeField, Tooltip("回転させる見た目。未設定なら Model という名前の子を探す")]
        private Transform _spinTransform;

        [SerializeField, Tooltip("何段目で回転するか(_comboAttackIndices の位置)。-1で回転しない")]
        private int _spinComboStep = 2;

        [SerializeField, Min(0.05f), Tooltip("1回転にかける秒数")]
        private float _spinDurationSec = 0.45f;

        [SerializeField, Min(0.0f), Tooltip("回転中に跳び上がる高さ(メートル)。体が地面に潜らない高さにする")]
        private float _spinJumpHeight = 0.9f;

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
        [Header("当たった手応え(自分の画面だけ)")]
        [SerializeField, Min(0.0f), Tooltip("当たった瞬間に時間を止める長さ(秒)。0で止めない")]
        private float _hitStopSec = 0.05f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("止めている間の時間の速さ")]
        private float _hitStopTimeScale = 0.05f;

        [SerializeField, Min(0.0f), Tooltip("止めたあと、通常の速さへ戻すのにかける秒数")]
        private float _hitStopRecoverSec = 0.1f;

        [SerializeField, Min(0.0f), Tooltip("当たった瞬間のカメラの揺れ幅。0で揺らさない")]
        private float _cameraShakeAmplitude = 0.1f;

        [SerializeField, Min(0.0f), Tooltip("カメラの揺れの長さ(秒)")]
        private float _cameraShakeSec = 0.12f;

        [SerializeField, Tooltip("当たった瞬間の音")]
        private AudioClip _hitClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("当たった音の音量")]
        private float _hitVolume = 0.7f;

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
        private ThirdPersonCamera _cameraController;
        private int _comboStep;
        private float _lastAttackTime;
        private bool _isSpinning;
        private bool _justBuffered;
        private bool _currentAttackIsJust;

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

        /// <summary>いまのコンボの段数(0が1段目)</summary>
        public int ComboStep => _comboStep;

        /// <summary>いまジャスト入力の受付中か。ボタンを光らせるのに使う</summary>
        public bool IsInJustWindow =>
            _justWindowSec > 0.0f && _cooldownRemainSec > 0.0f && _cooldownRemainSec <= _justWindowSec;

        /// <summary>
        /// 通常攻撃を出す。UIボタンからも呼べる。
        /// 続けて押していれば段が進み、間が空くと1段目へ戻る。
        /// </summary>
        public void TryNormalAttack()
        {
            TryNormalAttack(false);
        }

        // ---- 内部処理: コンボ ------------------------------

        private void TryNormalAttack(bool isJust)
        {
            // 間が空いていれば、出す前に段を戻しておく
            if (Time.time - _lastAttackTime > _comboResetSec) _comboStep = 0;

            _currentAttackIsJust = isJust;

            if (!TryAttack(ResolveComboAttackIndex(_comboStep)))
            {
                _currentAttackIsJust = false;
                return;
            }

            _lastAttackTime = Time.time;
            _comboStep = HasCombo ? (_comboStep + 1) % _comboAttackIndices.Length : 0;

            if (isJust) PlayJustFeedback();
        }

        /// <summary>
        /// クールタイム中に押されたとき。終わり際の受付内なら次の一撃を予約し、
        /// 早すぎればコンボを切る。これで連打では段が進まなくなる。
        /// </summary>
        private void OnPressedWhileCooling()
        {
            if (IsInJustWindow)
            {
                _justBuffered = true;
                return;
            }

            _justBuffered = false;
            _comboStep = 0;
        }

        private void PlayJustFeedback()
        {
            if (_justClip == null || UI.UiSoundPlayer.Instance == null) return;

            UI.UiSoundPlayer.Instance.PlayOneShot(_justClip, _justVolume);
        }

        /// <summary>番号を指定して攻撃する。クールタイム中や攻撃中は何も起きない</summary>
        public bool TryAttack(int attackIndex)
        {
            if (!IsOwner) return false;
            if (_isAttacking) return false;
            if (_cooldownRemainSec > 0f) return false;

            // ビームスキルの狙い中・照射中は通常攻撃を出さない
            PlayerBeamSkill beamSkill = GetComponent<PlayerBeamSkill>();
            if (beamSkill != null && beamSkill.IsBusy) return false;

            // 元気玉スキルの狙い中・投擲中も通常攻撃を出さない
            PlayerEnergyBallSkill energyBallSkill = GetComponent<PlayerEnergyBallSkill>();
            if (energyBallSkill != null && energyBallSkill.IsBusy) return false;

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

            // 押しっぱなしの取りこぼしを防ぐため、クールタイム中でも入力自体は読み切る
            bool pressed = ReadAttackInput();

            // 共有必殺の受付中は同じ攻撃ボタンを参加入力として使う。
            // クールタイム中でも参加でき、通常攻撃へ入力を漏らさない。
            if (Battle.TeamPowerDirector.TryConsumeJoinInput(pressed)) return;

            if (_cooldownRemainSec > 0f)
            {
                if (pressed) OnPressedWhileCooling();

                _cooldownRemainSec -= Time.deltaTime;
                if (_cooldownRemainSec > 0f) return;

                // 明けた瞬間。受付内で押していれば続き、逃していれば1段目へ戻る
                if (_justBuffered)
                {
                    _justBuffered = false;
                    TryNormalAttack(true);
                }
                else _comboStep = 0;

                return;
            }

            if (pressed) TryNormalAttack(false);
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

            // 回転はどのクライアントでも見せたいので、番号から判断してここで回す
            if (IsSpinAttack(attackIndex)) SpinAsync(destroyCancellationToken).Forget();

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
        private void RpcOnHit(int attackIndex, Vector3 hitPoint, int targetNetworkId, int damage, bool combo, PhotonMessageInfo info)
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
            SpawnDamagePopup(data, hitPoint, damage, combo);

            if (target != null) target.NotifyHit(position, attackerActorNumber, damage);

            // 殴られた側を光らせる。当たった手応えは相手の反応でいちばん伝わる
            if (target != null) Battle.HitFlash.PlayWhite(target.transform, 0.1f);

            // 数字だけでは噛んだ手応えが出ない。擬音を弾けさせて音の代わりに絵で伝える
            Battle.Onomatopoeia.Play(position, "ガブッ！", new Color(1.0f, 0.95f, 0.85f, 1.0f), 0.6f);

            // 手応えは当てた本人にだけ返す。他人の画面まで止めると位置同期の補間がガタつく
            if (IsOwner) PlayHitFeedback();

            if (_logHit)
            {
                string targetName = target != null ? target.name : "(不明)";
                Debug.Log($"[Attack] {data.DisplayName} がヒット target={targetName} pos={position}");
            }
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>
        /// 当てた瞬間の手応え。時間を一瞬止め、カメラを揺らし、音を鳴らす。
        /// どれも自分の画面だけの演出なので通信はしない。
        /// </summary>
        private void PlayHitFeedback()
        {
            Battle.HitStop.Play(_hitStopSec, _hitStopTimeScale, _hitStopRecoverSec);

            // 噛みついた瞬間に縦へ縮める。噛む力が入ったように見える
            SquashStretch squash = GetComponentInChildren<SquashStretch>(true);
            if (squash != null) squash.Squash(0.18f);

            if (_cameraShakeAmplitude > 0.0f && _cameraShakeSec > 0.0f)
            {
                ThirdPersonCamera playerCamera = ResolveCamera();
                if (playerCamera != null) playerCamera.Shake(_cameraShakeAmplitude, _cameraShakeSec);
            }

            if (_hitClip != null && UI.UiSoundPlayer.Instance != null)
            {
                UI.UiSoundPlayer.Instance.PlayOneShot(_hitClip, _hitVolume);
            }
        }

        /// <summary>揺らすカメラを探す。毎回探すと重いので一度見つけたら覚えておく</summary>
        private ThirdPersonCamera ResolveCamera()
        {
            if (_cameraController == null) _cameraController = FindAnyObjectByType<ThirdPersonCamera>();
            return _cameraController;
        }

        /// <summary>当たった位置にダメージの数字を出す</summary>
        private void SpawnDamagePopup(AttackData data, Vector3 hitPoint, int damage, bool combo)
        {
            if (data.DamagePopupPrefab == null) return;

            GameObject popup = Instantiate(data.DamagePopupPrefab, hitPoint + data.DamagePopupOffset, Quaternion.identity);
            DamagePopup component = popup.GetComponent<DamagePopup>();
            if (component != null) component.Play(damage, combo);
        }

        /// <summary>コンボが設定されているか</summary>
        private bool HasCombo => _comboAttackIndices != null && _comboAttackIndices.Length > 0;

        /// <summary>その段で使う攻撃データの番号を返す</summary>
        private int ResolveComboAttackIndex(int step)
        {
            if (!HasCombo) return _normalAttackIndex;

            return _comboAttackIndices[Mathf.Clamp(step, 0, _comboAttackIndices.Length - 1)];
        }

        /// <summary>
        /// この攻撃が回転する段のものか。段の数え方はクライアントごとに違いうるので、
        /// 送られてきた攻撃データの番号で判断する。
        /// </summary>
        private bool IsSpinAttack(int attackIndex)
        {
            if (!HasCombo) return false;
            if (_spinComboStep < 0 || _spinComboStep >= _comboAttackIndices.Length) return false;

            return _comboAttackIndices[_spinComboStep] == attackIndex;
        }

        private Transform ResolveSpinTransform()
        {
            if (_spinTransform != null) return _spinTransform;

            _spinTransform = transform.Find("Model");
            return _spinTransform;
        }

        /// <summary>
        /// 締めの一撃で前方へ1回転させる。アニメーションは噛みつきのままなので、
        /// 見た目の差はこの回転で付ける。骨ではなく見た目の親を回すだけなので、
        /// アニメーションとは喧嘩しない。
        /// </summary>
        private async UniTaskVoid SpinAsync(CancellationToken token)
        {
            if (_isSpinning) return;

            Transform spin = ResolveSpinTransform();
            if (spin == null) return;

            _isSpinning = true;
            Quaternion originalRotation = spin.localRotation;
            Vector3 originalPosition = spin.localPosition;

            try
            {
                float elapsed = 0f;
                while (elapsed < _spinDurationSec)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.deltaTime;

                    float t = Mathf.Clamp01(elapsed / _spinDurationSec);

                    // 等速だと機械的なので、溜めと抜けを付ける
                    float angle = Mathf.SmoothStep(0f, 360f, t);
                    spin.localRotation = originalRotation * Quaternion.Euler(angle, 0f, 0f);

                    // 体の真ん中を軸に回すので、浮かせないと下半分が地面に潜る。
                    // 山なりに上げ下げすると、跳んで回った動きに見える
                    Vector3 position = originalPosition;
                    position.y += _spinJumpHeight * 4.0f * t * (1.0f - t);
                    spin.localPosition = position;
                }
            }
            catch (OperationCanceledException)
            {
                // 破棄されただけなので何もしない
            }
            finally
            {
                // 途中で止まっても、傾いたまま・浮いたままにしない
                if (spin != null)
                {
                    spin.localRotation = originalRotation;
                    spin.localPosition = originalPosition;
                }

                _isSpinning = false;
            }
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
            // カットシーン中は攻撃できない。スキップの長押しと取り違えないためでもある
            if (!Battle.BattlePlayGate.IsPlayable) return false;

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

                // 他のプレイヤーが直前に当てていれば、同時ヒットボーナスを掛けてから配る。
                // 掛けたあとの値と、乗ったかどうかを送るので、全員が同じ数字と表示になる
                // ジャスト成功なら威力を上げ、そのうえに連携の倍率を掛ける
                int basePower = _currentAttackIsJust
                    ? Mathf.RoundToInt(data.AttackPower * _justDamageMultiplier)
                    : data.AttackPower;

                bool combo = Battle.ComboBonus.IsActive;
                int damage = Battle.ComboBonus.Apply(basePower);

                photonView.RPC(nameof(RpcOnHit), RpcTarget.All, attackIndex, hitPoint, targetNetworkId, damage, combo);

                newHitCount++;
                if (alreadyHitCount + newHitCount >= data.MaxHitCount) break;
            }

            return newHitCount;
        }
    }
}
