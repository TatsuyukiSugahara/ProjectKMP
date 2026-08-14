using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using ProjectKMP.Attack;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Player
{
    /// <summary>
    /// とびこみ。山なりに跳んで前方へ高速で移動し、着地点に弱い攻撃判定を出す。
    /// 途中で相手にぶつかればそこで止まって当てる。
    ///
    /// 攻撃にも回避にも使えるのが狙い。上昇中だけ無敵にしてあるので、
    /// 相手の攻撃に合わせて跳べばかわせるが、着地したところを狙われると危ない。
    /// 「いつ跳ぶか」を選ばせるための作り。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerDiveSkill : MonoBehaviourPun
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("跳び方")]
        [SerializeField, Min(0.5f), Tooltip("前へ進む距離(メートル)")]
        private float _distance = 6.0f;

        [SerializeField, Min(0.1f), Tooltip("跳んでから着地するまでの時間(秒)")]
        private float _durationSec = 0.55f;

        [SerializeField, Min(0.1f), Tooltip("山なりの高さ(メートル)。高すぎると相手の上に乗ってしまう")]
        private float _peakHeight = 1.25f;

        [SerializeField, Min(0.1f), Tooltip("体の太さ。途中でぶつかるかを調べるのに使う(半径・メートル)")]
        private float _bodyRadius = 0.5f;

        [SerializeField, Min(0.05f), Tooltip("1回転にかける時間の割合(1で飛行中ずっと回る)")]
        private float _spinRatio = 1.0f;

        [Header("ぶつかったとき")]
        [SerializeField, Min(0.05f), Tooltip("相手にぶつかって跳ね返るのにかける時間(秒)")]
        private float _bounceDurationSec = 0.35f;

        [SerializeField, Min(0.0f), Tooltip("跳ね返るときに上がる高さ(メートル)")]
        private float _bounceHeight = 1.6f;

        [SerializeField, Min(0.0f), Tooltip("跳ね返るときに下がる距離(メートル)。相手に埋まったままにしない")]
        private float _bounceBackDistance = 1.2f;

        [SerializeField, Tooltip("回転させる見た目。未設定なら Model という名前の子を探す")]
        private Transform _spinTransform;

        [Header("攻撃")]
        [SerializeField, Min(1), Tooltip("着地(またはぶつかった位置)で与えるダメージ")]
        private int _damage = 8;

        [SerializeField, Min(0.1f), Tooltip("着地の攻撃が届く半径(メートル)")]
        private float _hitRadius = 1.6f;

        [SerializeField, Tooltip("当たり判定を取るレイヤー")]
        private LayerMask _targetLayers = ~0;

        [SerializeField, Min(0.1f), Tooltip("クールタイム(秒)")]
        private float _cooldownSec = 3.0f;

        [Header("次の技への繋ぎ")]
        [SerializeField, Min(0.0f), Tooltip("着地(またはぶつかった瞬間)から、次の技を強化できる受付時間(秒)")]
        private float _boostWindowSec = 0.5f;

        [Header("無敵")]
        [SerializeField, Tooltip("上昇中(山の頂点まで)は攻撃を受けない")]
        private bool _invincibleWhileRising = true;

        [Header("予測表示")]
        [SerializeField, Tooltip("押している間に出す予測。線と着地点の円で示す")]
        private DiveAimIndicator _aimIndicatorPrefab;

        [Header("演出")]
        [SerializeField, Tooltip("着地したときのエフェクト")]
        private GameObject _hitEffectPrefab;

        [SerializeField, Min(0.1f), Tooltip("着地エフェクトの大きさ")]
        private float _hitEffectScale = 1.2f;

        [SerializeField, Min(0.1f), Tooltip("着地エフェクトが消えるまでの時間(秒)")]
        private float _hitEffectLifeSec = 1.2f;

        [SerializeField, Tooltip("ダメージの数字")]
        private GameObject _damagePopupPrefab;

        [SerializeField, Tooltip("着地した地面に残す痕。未設定なら残さない")]
        private AttackDecal _landDecalPrefab;

        [SerializeField, Min(0.1f), Tooltip("痕の大きさを攻撃範囲の何倍にするか")]
        private float _landDecalScale = 1.4f;

        [SerializeField, Tooltip("跳んだ瞬間の音")]
        private AudioClip _jumpClip;

        [SerializeField, Tooltip("着地の音")]
        private AudioClip _landClip;

        [SerializeField, Tooltip("相手にぶつかったときの音。未設定なら着地の音で代用する")]
        private AudioClip _impactClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("音量")]
        private float _volume = 0.7f;

        [Header("入力")]
        [SerializeField, Tooltip("Eキーで出す")]
        private bool _useEKey = true;

        [SerializeField, Tooltip("ゲームパッドの左肩ボタンで出す")]
        private bool _useGamepadShoulder = true;

        [SerializeField, Tooltip("画面のとびこみボタンで出す")]
        private bool _useTouchButton = true;

        // ---- 内部状態 ------------------------------------

        private CharacterController _controller;
        private PlayerHealth _health;
        private PlayerBeamSkill _beamSkill;
        private MonoBehaviour _mover;
        private SquashStretch _squash;
        private ThirdPersonCamera _cameraController;

        private readonly Collider[] _overlapBuffer = new Collider[32];
        private readonly RaycastHit[] _castBuffer = new RaycastHit[16];

        /// <summary>着地点の高さを測るときに地面と見なすもの</summary>
        [SerializeField, Tooltip("着地点の地面を測る対象。空中から跳んだときの降り先を決めるのに使う")]
        private LayerMask _groundMask = ~0;

        private enum Phase { Ready, Aiming, Flying }

        private Phase _phase = Phase.Ready;
        private bool _wasHeldLastFrame;

        /// <summary>押した事実を覚えておく控え。押している間だけ残る</summary>
        private bool _pressBuffered;

        /// <summary>指を離したが、他の技の最中でまだ跳べない状態。空くと同時に跳ぶ</summary>
        private bool _diveReserved;

        /// <summary>狙っている向き(度)。キャラを回せない場面でも狙いだけは動かせるように別で持つ</summary>
        private float _aimYawDeg;
        private bool _isRising;
        private float _boostWindowEndTime;
        private float _cooldownRemainSec;
        private DiveAimIndicator _aimIndicatorInstance;
        private Collider[] _bossColliders;

        // ---- 公開API -------------------------------------

        /// <summary>このクライアントが操作しているとびこみ</summary>
        public static PlayerDiveSkill Local { get; private set; }

        /// <summary>クールタイムの残り具合(1=出した直後、0=出せる)。ボタンの表示に使う</summary>
        public float CooldownRatio01 =>
            _cooldownSec <= 0.0f ? 0.0f : Mathf.Clamp01(_cooldownRemainSec / _cooldownSec);

        /// <summary>いま跳んでいる最中か</summary>
        public bool IsFlying => _phase == Phase.Flying;

        /// <summary>いま予測を出して狙っている最中か</summary>
        public bool IsAiming => _phase == Phase.Aiming;

        /// <summary>跳んで上がっている最中か(山の頂点まで)。無敵の区間と同じ</summary>
        public bool IsRising => _phase == Phase.Flying && _isRising;

        /// <summary>
        /// 叩きつけた直後の受付時間の中か。ここでビームを撃てば強化される。
        /// 着地は音も揺れもある分かりやすい合図なので、狙って合わせられる。
        /// 時間が止まる演出を挟んでも狂わないよう、実時間で数える。
        /// </summary>
        public bool IsBoostWindowOpen => Time.unscaledTime <= _boostWindowEndTime;

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<PlayerHealth>();
            _beamSkill = GetComponent<PlayerBeamSkill>();

            // 移動スクリプトはシーンによって型が違うので、あるほうを使う
            _squash = GetComponentInChildren<SquashStretch>(true);
            _mover = GetComponent<PlayerMover>();
            if (_mover == null) _mover = GetComponent<LocalPlayerMover>();
        }

        private void Start()
        {
            if (IsOwner) Local = this;
        }

        private void OnDestroy()
        {
            DestroyAimIndicator();
            if (Local == this) Local = null;
        }

        private void Update()
        {
            if (!IsOwner) return;

            if (_cooldownRemainSec > 0.0f) _cooldownRemainSec -= Time.deltaTime;

            bool held = ReadHoldInput();
            bool pressedNow = held && !_wasHeldLastFrame;
            _wasHeldLastFrame = held;

            // 押した事実を覚えておき、離すまで持ち越す。
            // ビームの最中に押しても、出せるようになった瞬間に構えが始まる
            if (pressedNow) _pressBuffered = true;
            if (!held) _pressBuffered = false;

            switch (_phase)
            {
                case Phase.Ready:
                    // 押しっぱなしからの暴発を防ぐため、一度は押し直してもらう
                    if (_pressBuffered && CanDive()) StartAiming();
                    break;

                case Phase.Aiming:
                    if (!Battle.BattlePlayGate.IsPlayable || (_health != null && _health.IsDead))
                    {
                        CancelAiming();
                        break;
                    }

                    // 他の技が動いている間は移動の部品ごと止まっていることがある。
                    // 狙っている間だけ、こちらから向きを回してやる
                    UpdateAimYaw();
                    UpdateAim();

                    // 他の技が終わった瞬間に、待たせていた1発を出す
                    if (_diveReserved)
                    {
                        if (CanStartDiveNow()) StartDive();
                        break;
                    }

                    if (held) break;

                    // 跳べない間は予約だけして、狙いは出したままにする
                    if (CanStartDiveNow()) StartDive();
                    else _diveReserved = true;
                    break;
            }
        }

        // ---- 内部処理: 開始条件 ---------------------------

        /// <summary>
        /// 狙いに入れるか。他の技の最中でも狙いだけは始められる。
        /// 動けない時間に何もできないと、技を繋ぐたびに手が止まって気持ちよくないため。
        /// </summary>
        private bool CanDive()
        {
            if (_phase != Phase.Ready || _cooldownRemainSec > 0.0f) return false;
            if (!Battle.BattlePlayGate.IsPlayable) return false;
            if (_health != null && _health.IsDead) return false;

            return true;
        }

        /// <summary>
        /// いま跳んでよいか。他の技が動いている間は跳ばずに待つ。
        /// 割り込むと位置がずれて演出が破綻する。
        /// ただしビームを撃ち終わって降りているだけの間は、そのまま繋げたほうが気持ちいい
        /// </summary>
        private bool CanStartDiveNow()
        {
            if (!Battle.BattlePlayGate.IsPlayable) return false;
            if (_health != null && _health.IsDead) return false;

            if (_beamSkill != null && _beamSkill.IsBusy && !_beamSkill.IsFinishing) return false;

            // 投げ終わって降りているだけの間は、そのまま次へ繋げたほうが気持ちいい
            PlayerEnergyBallSkill energyBallSkill = GetComponent<PlayerEnergyBallSkill>();
            if (energyBallSkill != null && energyBallSkill.IsBusy && !energyBallSkill.IsDescending) return false;

            return true;
        }

        private bool ReadHoldInput()
        {
            if (_useEKey)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.eKey.isPressed) return true;
            }

            if (_useGamepadShoulder)
            {
                Gamepad gamepad = Gamepad.current;
                if (gamepad != null && gamepad.leftShoulder.isPressed) return true;
            }

            if (_useTouchButton)
            {
                UI.TouchControls touch = UI.TouchControls.Instance;
                if (touch != null && touch.DiveHeld) return true;
            }

            return false;
        }

        // ---- 内部処理: 予測表示 ---------------------------

        /// <summary>
        /// 予測を出す。ビームと違って移動は止めない。
        /// 避けるための技なので、狙っている間に位置を直せないと使いものにならない。
        /// </summary>
        /// <summary>
        /// 狙っている向きを更新する。
        ///
        /// 移動側が向きを回しているときは、キャラの向きがそのまま狙い。
        /// ビームの照射中のように向きを変えられない場面では、こちらで角度だけを動かす。
        /// キャラごと回すとビームまで振れてしまうため、キャラには触らない。
        /// </summary>
        private void UpdateAimYaw()
        {
            var localMover = _mover as LocalPlayerMover;
            if (localMover == null) { _aimYawDeg = transform.eulerAngles.y; return; }

            if (localMover.RotatesByInput) { _aimYawDeg = transform.eulerAngles.y; return; }

            if (!localMover.TryReadMoveDirection(out Vector3 direction)) return;

            float target = Quaternion.LookRotation(direction).eulerAngles.y;
            _aimYawDeg = Mathf.MoveTowardsAngle(_aimYawDeg, target, localMover.TurnSpeedDeg * Time.deltaTime);
        }

        /// <summary>狙っている向きのワールド方向</summary>
        private Vector3 AimDirection => Quaternion.Euler(0.0f, _aimYawDeg, 0.0f) * Vector3.forward;

        private void StartAiming()
        {
            _phase = Phase.Aiming;
            _aimYawDeg = transform.eulerAngles.y;

            if (_aimIndicatorPrefab == null) return;

            _aimIndicatorInstance = Instantiate(_aimIndicatorPrefab, transform);
            _aimIndicatorInstance.transform.localPosition = Vector3.zero;
            _aimIndicatorInstance.transform.localRotation = Quaternion.identity;
            _aimIndicatorInstance.Configure(_distance, _hitRadius);
        }

        private void CancelAiming()
        {
            _phase = Phase.Ready;
            _diveReserved = false;
            DestroyAimIndicator();
        }

        private void DestroyAimIndicator()
        {
            if (_aimIndicatorInstance == null) return;

            Destroy(_aimIndicatorInstance.gameObject);
            _aimIndicatorInstance = null;
        }

        /// <summary>飛ぶ道筋に相手がいるかを調べて、予測の色を切り替える</summary>
        private void UpdateAim()
        {
            if (_aimIndicatorInstance == null) return;

            Vector3 direction = AimDirection;

            // 予測はキャラの子なので、キャラの向きとの差だけ回してやる
            _aimIndicatorInstance.transform.localRotation =
                Quaternion.Euler(0.0f, _aimYawDeg - transform.eulerAngles.y, 0.0f);

            Vector3 start = transform.position;
            Vector3 end = start + direction * _distance;

            int count = Physics.OverlapCapsuleNonAlloc(
                start, end, _hitRadius, _overlapBuffer, _targetLayers, QueryTriggerInteraction.Collide);

            bool willHit = false;
            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapBuffer[i];
                if (collider == null) continue;
                if (collider.transform == transform || collider.transform.IsChildOf(transform)) continue;

                HitTarget target = collider.GetComponentInParent<HitTarget>();
                if (target == null || !target.CanBeHit || target.NetworkId == 0) continue;

                willHit = true;
                break;
            }

            _aimIndicatorInstance.SetWillHit(willHit);
        }

        /// <summary>
        /// 指定した場所の地面の高さを返す。自分自身は無視する。
        /// 見つからなければ、代わりの高さをそのまま返す。
        /// </summary>
        private float ResolveGroundY(Vector3 position, float fallbackY)
        {
            Vector3 origin = position + Vector3.up * 30.0f;

            int count = Physics.RaycastNonAlloc(
                origin, Vector3.down, _castBuffer, 100.0f, _groundMask, QueryTriggerInteraction.Ignore);

            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Transform hit = _castBuffer[i].transform;
                if (hit == null || hit == transform || hit.IsChildOf(transform)) continue;

                // 一番高いところを拾う。橋や台の上に降りたときに床下へ潜らせないため
                if (_castBuffer[i].point.y > best) best = _castBuffer[i].point.y;
            }

            return best > float.NegativeInfinity ? best : fallbackY;
        }

        // ---- 内部処理: 飛行 -------------------------------

        private void StartDive()
        {
            _diveReserved = false;
            DestroyAimIndicator();

            // ビームの降下から繋ぐ場合、あちらの移動を先に畳んでおく
            if (_beamSkill != null) _beamSkill.EndLeapNow();

            _phase = Phase.Flying;
            _cooldownRemainSec = _cooldownSec;

            // 狙っていた向きへ跳ぶ。キャラの向きも合わせておく
            Vector3 direction = AimDirection;
            transform.rotation = Quaternion.Euler(0.0f, _aimYawDeg, 0.0f);

            // 回転と音は全員の画面で見せたいので配る
            photonView.RPC(nameof(RpcDive), RpcTarget.All, direction);
        }

        [PunRPC]
        private void RpcDive(Vector3 direction)
        {
            SpinAsync(destroyCancellationToken).Forget();
            PlayClip(_jumpClip);

            // 跳んだ瞬間に縦へ伸ばす。飛び出す勢いは形の変化でいちばん伝わる
            if (_squash != null) _squash.Stretch(0.35f);

            // 実際に動かすのは操作している本人だけ。他人の位置は同期で届く
            if (IsOwner) FlyAsync(direction, destroyCancellationToken).Forget();
        }

        /// <summary>
        /// 山なりに跳ぶ。毎フレーム進む先を調べ、相手がいればそこで止めて当てる。
        /// 移動スクリプトは止めておく。両方が同時に動かすと、入力と跳躍がぶつかって暴れる。
        /// </summary>
        private async UniTaskVoid FlyAsync(Vector3 direction, CancellationToken token)
        {
            Vector3 start = transform.position;
            bool moverWasEnabled = _mover != null && _mover.enabled;
            bool hitApplied = false;
            if (_mover != null) _mover.enabled = false;

            SetInvincible(_invincibleWhileRising);
            _isRising = true;

            // 相手の体に乗り上げないよう、飛んでいる間だけ当たり判定を無視する。
            // 止まる判定は別に SphereCast で取っているので、すり抜けたりはしない
            SetBossCollisionIgnored(true);

            // 降り先は跳ぶ前に決めておく。飛びながら測ると、
            // 途中の起伏を拾って高さが暴れる
            float landingY = ResolveGroundY(start + direction * _distance, start.y);

            try
            {
                float elapsed = 0.0f;
                bool blocked = false;

                while (elapsed < _durationSec && !blocked)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.deltaTime;

                    float t = Mathf.Clamp01(elapsed / _durationSec);

                    // 頂点を過ぎたら無敵を切る。着地際は無防備というのが、この技の引き換え
                    if (t >= 0.5f)
                    {
                        SetInvincible(false);
                        _isRising = false;
                    }

                    // 跳び始めた高さから着地点の地面へ、進むにつれて降ろしていく。
                    // 始点の高さのままだと、空中から跳んだときに空中で終わってしまう
                    Vector3 next = start + direction * (_distance * t);
                    next.y = Mathf.Lerp(start.y, landingY, t) + _peakHeight * 4.0f * t * (1.0f - t);

                    Vector3 delta = next - transform.position;
                    float distance = delta.magnitude;
                    if (distance <= 0.0001f) continue;

                    // ぶつかるならその手前で止める。押し込むと相手の中に埋まってしまう
                    if (HasBlockerAhead(delta / distance, distance))
                    {
                        blocked = true;
                        break;
                    }

                    _controller.Move(delta);
                }

                if (blocked)
                {
                    // ぶつかった位置で当ててから跳ね返る。着地まで待つと、当たった手応えが遅れる
                    ApplyLandingHit(true);
                    hitApplied = true;

                    await BounceAsync(direction, token);
                }
            }
            catch (OperationCanceledException)
            {
                // 破棄されただけなので何もしない
            }
            finally
            {
                SetInvincible(false);
                SetBossCollisionIgnored(false);
                _isRising = false;

                // 死亡中は PlayerHealth 側が移動を止めているので、勝手に戻さない
                if (_mover != null && moverWasEnabled && (_health == null || !_health.IsDead))
                {
                    _mover.enabled = true;
                }

                _phase = Phase.Ready;

                // ぶつかった時点で当てていれば、二重には当てない
                if (!hitApplied) ApplyLandingHit();

                // 動き終わった時点で受付を開き直す。
                // ぶつかって跳ね返った場合、当てた瞬間から数えると跳ね返りで使い切ってしまう
                _boostWindowEndTime = Time.unscaledTime + _boostWindowSec;
            }
        }

        /// <summary>
        /// ぶつかったあとの跳ね返り。上へ弾みながら少し下がる。
        /// 相手に重なったまま止まると、当たり判定を戻した瞬間に押し出されて地面へ潜ってしまう。
        /// </summary>
        private async UniTask BounceAsync(Vector3 direction, CancellationToken token)
        {
            Vector3 start = transform.position;
            float elapsed = 0.0f;

            while (elapsed < _bounceDurationSec)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / _bounceDurationSec);

                Vector3 next = start
                    - direction * (_bounceBackDistance * t)
                    + Vector3.up * (_bounceHeight * 4.0f * t * (1.0f - t));

                _controller.Move(next - transform.position);
            }
        }

        /// <summary>進む先に相手がいるか。地面や壁は無視して、当てられる相手だけを見る</summary>
        private bool HasBlockerAhead(Vector3 direction, float distance)
        {
            int count = Physics.SphereCastNonAlloc(
                transform.position, _bodyRadius, direction, _castBuffer, distance,
                _targetLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _castBuffer[i].collider;
                if (collider == null) continue;
                if (collider.transform == transform || collider.transform.IsChildOf(transform)) continue;

                HitTarget target = collider.GetComponentInParent<HitTarget>();
                if (target == null || !target.CanBeHit || target.NetworkId == 0) continue;

                return true;
            }

            return false;
        }

        // ---- 内部処理: 着地の攻撃 -------------------------

        /// <param name="blocked">相手にぶつかって止まったか。鳴らす音を変えるのに使う</param>
        private void ApplyLandingHit(bool blocked = false)
        {
            // 叩きつけた瞬間から受付が開く
            _boostWindowEndTime = Time.unscaledTime + _boostWindowSec;

            // 着地の合図を全員へ送る。ここは本人でしか通らないので、
            // 送らないと他の人の画面ではとびこみが無音のまま終わってしまう
            photonView.RPC(nameof(RpcLanded), RpcTarget.All, blocked, transform.position);

            PlayLandingFeedback();
            SpawnLandDecal();

            Vector3 center = transform.position;
            int count = Physics.OverlapSphereNonAlloc(
                center, _hitRadius, _overlapBuffer, _targetLayers, QueryTriggerInteraction.Collide);

            bool combo = Battle.ComboBonus.IsActive;

            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapBuffer[i];
                if (collider == null) continue;
                if (collider.transform == transform || collider.transform.IsChildOf(transform)) continue;

                HitTarget target = collider.GetComponentInParent<HitTarget>();
                if (target == null || !target.CanBeHit || target.NetworkId == 0) continue;

                Vector3 point = collider.ClosestPoint(center);
                int damage = Battle.ComboBonus.Apply(_damage);

                photonView.RPC(nameof(RpcDiveHit), RpcTarget.All, point, target.NetworkId, damage, combo);
            }
        }

        /// <summary>
        /// 着地(またはぶつかった瞬間)の音を全員の画面で鳴らす。
        /// 相手に当たったときは地面とは違う詰まった音にして、当てたかどうかを耳でも分かるようにする。
        /// </summary>
        [PunRPC]
        private void RpcLanded(bool blocked, Vector3 landingPosition)
        {
            AudioClip clip = blocked && _impactClip != null ? _impactClip : _landClip;
            PlayClip(clip);

            // とびこみも小さな破壊の起点にする。RPC内なので全員の画面で同じ順に壊れる。
            float breakRadius = Mathf.Max(1.5f, _hitRadius * 1.35f);
            Field.BreakableTree.BreakInSphere(landingPosition, breakRadius);
            Field.BreakableProp.BreakInSphere(landingPosition, breakRadius);
        }

        [PunRPC]
        private void RpcDiveHit(Vector3 hitPoint, int targetNetworkId, int damage, bool combo, PhotonMessageInfo info)
        {
            if (_hitEffectPrefab != null)
            {
                AttackEffect.Spawn(_hitEffectPrefab, hitPoint, Quaternion.identity, _hitEffectScale, _hitEffectLifeSec);
            }

            if (_damagePopupPrefab != null)
            {
                GameObject popup = Instantiate(_damagePopupPrefab, hitPoint, Quaternion.identity);
                DamagePopup component = popup.GetComponent<DamagePopup>();
                if (component != null) component.Play(damage, combo);
            }

            HitTarget target = HitTarget.Find(targetNetworkId);
            if (target == null) return;

            int attackerActorNumber = info.Sender != null ? info.Sender.ActorNumber : -1;
            target.NotifyHit(hitPoint, attackerActorNumber, damage);

            Battle.HitFlash.PlayWhite(target.transform, 0.14f);
            Battle.Onomatopoeia.Play(hitPoint, "ドンッ！", new Color(1.0f, 0.85f, 0.55f, 1.0f), 1.2f);
        }

        // ---- 内部処理: 見た目と手応え ---------------------

        /// <summary>飛行中に前方へ1回転させる。3段目の回転攻撃と同じく、見た目の親だけを回す</summary>
        private async UniTaskVoid SpinAsync(CancellationToken token)
        {
            Transform spin = ResolveSpinTransform();
            if (spin == null) return;

            Quaternion originalRotation = spin.localRotation;
            float duration = Mathf.Max(0.05f, _durationSec * _spinRatio);

            try
            {
                float elapsed = 0.0f;
                while (elapsed < duration)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.deltaTime;

                    float angle = 360.0f * Mathf.Clamp01(elapsed / duration);
                    spin.localRotation = originalRotation * Quaternion.Euler(angle, 0.0f, 0.0f);
                }
            }
            catch (OperationCanceledException)
            {
                // 破棄されただけ
            }
            finally
            {
                if (spin != null) spin.localRotation = originalRotation;
            }
        }

        private Transform ResolveSpinTransform()
        {
            if (_spinTransform != null) return _spinTransform;

            _spinTransform = transform.Find("Model");
            return _spinTransform;
        }

        /// <summary>
        /// 着地した地面に痕を残す。跳んだ先が坂でもめり込まないよう、
        /// 足元から真下へ調べて地面の高さに合わせる。
        /// </summary>
        private void SpawnLandDecal()
        {
            if (_landDecalPrefab == null) return;

            Vector3 point = transform.position;

            // 自分自身に当たらないよう、少し上から調べる
            Vector3 origin = point + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4.0f,
                    _targetLayers, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
            }

            AttackDecal.Spawn(_landDecalPrefab, point, _hitRadius * 2.0f * _landDecalScale);
        }

        private void PlayLandingFeedback()
        {
            if (!IsOwner) return;

            Battle.HitStop.Play(0.04f, 0.08f, 0.1f);

            // 着地で潰す。伸びたぶんを潰しで受け止めると、動きが繋がって見える
            if (_squash != null) _squash.Squash(0.32f);

            // 着地の一瞬だけ周りを引かせる。踏みしめた重さが出る
            UI.BgmPlayer.Duck(0.3f, 0.08f, 0.3f);

            // 地面を走る輪。着地の重さは、キャラではなく地面の反応で伝わる
            Battle.ShockwaveRing.Play(transform.position, new Color(1.0f, 0.92f, 0.72f, 1.0f), 5.0f, 0.4f, 0.7f);

            if (_cameraController == null) _cameraController = FindAnyObjectByType<ThirdPersonCamera>();
            if (_cameraController != null) _cameraController.Shake(0.12f, 0.15f);
        }

        private void PlayClip(AudioClip clip)
        {
            if (clip == null || UI.UiSoundPlayer.Instance == null) return;

            UI.UiSoundPlayer.Instance.PlayOneShot(clip, _volume);
        }

        /// <summary>
        /// 相手の体との当たり判定を切り替える。切っておかないと、跳んだ勢いで頭の上に着地できてしまう。
        /// 無視の状態はコライダーを無効にすると解除されるので、戻し忘れても致命的にはならない。
        /// </summary>
        private void SetBossCollisionIgnored(bool ignored)
        {
            if (_bossColliders == null)
            {
                Monster.BossHealth boss = FindAnyObjectByType<Monster.BossHealth>(FindObjectsInactive.Include);
                if (boss == null) return;

                _bossColliders = boss.GetComponentsInChildren<Collider>(true);
            }

            foreach (Collider collider in _bossColliders)
            {
                if (collider == null || !collider.enabled) continue;
                if (_controller == null || !_controller.enabled) continue;

                Physics.IgnoreCollision(_controller, collider, ignored);
            }
        }

        private void SetInvincible(bool value)
        {
            if (_health == null) return;

            _health.SetInvincible(value);
        }

        /// <summary>このクライアントがこのキャラを操作しているか</summary>
        private bool IsOwner => photonView == null || !PhotonNetwork.IsConnected || photonView.IsMine;
    }
}
