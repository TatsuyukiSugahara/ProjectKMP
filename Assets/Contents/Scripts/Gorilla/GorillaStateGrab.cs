using ProjectKMP.Battle;
using ProjectKMP.Core;
using ProjectKMP.Player;
using ProjectKMP.Presentation;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 掴みステート（近距離・1人狙い撃ち）。
    ///
    /// 大きな手を突き出して正面のプレイヤーを1人だけ掴み、握り締めたまま持ち上げる。
    /// 掴まれた本人は動けず、握られるたびに少しずつ削られ、時間切れで地面へ叩きつけられる。
    ///
    /// 抜け出す手は2つ。
    ///   ・掴まれた本人が攻撃ボタンを連打して自力でこじ開ける
    ///   ・仲間がボスを殴ってひるませる
    /// どちらも「その場で何かできる」ようにしてあるので、待たされるだけの時間にならない。
    ///
    /// 誰を掴んだかはゲームの状態そのものなので、決めるのは MasterClient だけ。
    /// 選ばれた ActorNumber は GorillaSyncData に載って全員に配られ、
    /// 掴まれた本人だけが自分のキャラを手の中へ動かす(他の人からは通常の位置同期で見える)。
    /// </summary>
    public class GorillaStateGrab : IGorillaState
    {
        /// <summary>溜め中のアニメーション再生速度倍率</summary>
        private const float WINDUP_SPEED_MULTIPLIER = 0.15f;

        /// <summary>手を突き出すのにかける時間(秒)</summary>
        private const float REACH_TIME = 0.16f;

        /// <summary>掴んだ相手を持ち上げるのにかける時間(秒)</summary>
        private const float LIFT_TIME = 0.32f;

        /// <summary>叩きつけに入ってから地面に着くまでの時間(秒)</summary>
        private const float SLAM_TIME = 0.22f;

        /// <summary>叩きつけてから硬直へ移るまでの余韻(秒)</summary>
        private const float SLAM_RECOVERY_TIME = 0.35f;

        /// <summary>握り締める周期(秒)。この間隔で締め付けダメージが入る</summary>
        private const float SQUEEZE_INTERVAL_SEC = 1.0f;

        /// <summary>握り締めたときに手が縮む量(倍率)</summary>
        private const float SQUEEZE_SCALE = 0.12f;

        /// <summary>助けを呼ぶ文字を出す間隔(秒)</summary>
        private const float CALL_INTERVAL_SEC = 1.2f;

        /// <summary>掴んでいる間の体の揺れ幅(メートル)。暴れる相手を押さえている感じを出す</summary>
        private const float HOLD_SHAKE_AMOUNT = 0.06f;

        private enum Phase
        {
            /// <summary>手を構えて狙う</summary>
            Windup,
            /// <summary>手を突き出して掴みにいく</summary>
            Reach,
            /// <summary>掴んで持ち上げている</summary>
            Hold,
            /// <summary>地面へ叩きつけている</summary>
            Slam,
            /// <summary>叩きつけ(または取り逃がし)の余韻</summary>
            Recover,
        }

        private Phase _phase;
        private float _elapsedTime;
        private float _baseAnimatorSpeed;

        private float _yawDeg;
        private float _leanAngleDeg;
        private Vector3 _originalPosition;

        private bool _hasLockedAim;
        private bool _hasCaught;
        private bool _wasRescued;

        /// <summary>掴んだ時点のボスのHP。ここから一定量減ったら解放する(仲間による救出)</summary>
        private int _bossHpAtGrab;

        /// <summary>掴まれた本人が連打で削る残りゲージ。0になると自力で抜け出せる</summary>
        private float _escapeRemain;

        private float _squeezeTimer;
        private float _callTimer;

        private GorillaAttackTelegraph _telegraph;
        private GameObject _hand;
        private Vector3 _handBaseScale;

        /// <summary>自分が操作しているキャラが掴まれている間だけ、止めておく移動スクリプト</summary>
        private LocalPlayerMover _lockedMover;

        public void Enter(GorillaAI owner)
        {
            _phase = Phase.Windup;
            _elapsedTime = 0.0f;
            _hasLockedAim = false;
            _hasCaught = false;
            _wasRescued = false;
            _originalPosition = owner.transform.position;
            _yawDeg = owner.transform.eulerAngles.y;
            _leanAngleDeg = 0.0f;
            _bossHpAtGrab = 0;
            _escapeRemain = owner.GrabEscapeMashCount;
            _squeezeTimer = 0.0f;
            _callTimer = 0.0f;

            _baseAnimatorSpeed = owner.Animator != null ? owner.Animator.speed : 1.0f;
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed * WINDUP_SPEED_MULTIPLIER;

            owner.PlayAnimation(GorillaAI.ANIM_SWEEP_ATTACK);

            SpawnHand(owner);

            _telegraph = GorillaAttackTelegraph.SpawnSector(
                owner.AttackTelegraphPrefab, _originalPosition, _yawDeg,
                owner.GrabReach, owner.GrabAngleDeg);

            owner.NotifyGrabUsed();
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Windup:  UpdateWindup(owner);  break;
                case Phase.Reach:   UpdateReach(owner);   break;
                case Phase.Hold:    UpdateHold(owner);    break;
                case Phase.Slam:    UpdateSlam(owner);    break;
                case Phase.Recover: UpdateRecover(owner); break;
            }
        }

        public void Exit(GorillaAI owner)
        {
            owner.transform.SetPositionAndRotation(_originalPosition, Quaternion.Euler(0.0f, _yawDeg, 0.0f));
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;

            GorillaAttackTelegraph.Dismiss(_telegraph);
            _telegraph = null;
            DestroyHand();

            // 途中でこのステートを抜けても、掴まれっぱなしで動けなくならないよう必ず解放する
            ReleaseLocalPlayer();
            if (owner.HasAuthority) owner.GrabbedActorNumber = GorillaAI.NO_GRAB;
        }

        // ---- 狙い ----------------------------------------

        /// <summary>手を後ろへ引いて構える。後半で狙いが固定される</summary>
        private void UpdateWindup(GorillaAI owner)
        {
            float windupTime = Mathf.Max(0.05f, owner.GrabWindupTime);
            float rate = Mathf.Clamp01(_elapsedTime / windupTime);
            float lockRatio = Mathf.Clamp01(owner.GrabAimLockRatio);

            if (rate < lockRatio)
            {
                TurnYawTowardsTarget(owner, owner.GrabAimTurnSpeedDeg);
            }
            else if (!_hasLockedAim)
            {
                _hasLockedAim = true;
                if (_telegraph != null) _telegraph.SetLocked(true);
            }

            _leanAngleDeg = Mathf.Lerp(0.0f, -16.0f, rate);
            owner.transform.rotation = Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f);
            if (_telegraph != null) _telegraph.Follow(_originalPosition, _yawDeg);

            // 掴む手を体の横まで引いて構える
            PlaceHand(owner, Mathf.Lerp(0.3f, -0.4f, rate), owner.GrabHoldHeight * 0.4f, 1.0f);

            if (_elapsedTime < windupTime) return;

            _phase = Phase.Reach;
            _elapsedTime = 0.0f;

            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;
        }

        /// <summary>手を前へ突き出す。伸ばし切った瞬間に掴めたかどうかが決まる</summary>
        private void UpdateReach(GorillaAI owner)
        {
            float rate = Mathf.Clamp01(_elapsedTime / REACH_TIME);

            _leanAngleDeg = Mathf.Lerp(-16.0f, 20.0f, rate);
            owner.transform.rotation = Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f);

            // 引いた手が一気に前へ伸びる
            PlaceHand(owner, Mathf.Lerp(-0.4f, owner.GrabReach * 0.75f, rate), owner.GrabHoldHeight * 0.4f, 1.0f);

            if (_elapsedTime < REACH_TIME) return;

            GorillaAttackTelegraph.Dismiss(_telegraph);
            _telegraph = null;

            // 誰を掴んだかを決めるのは MasterClient だけ。結果は同期で全員に配られる
            if (owner.HasAuthority) DecideCatch(owner);

            _hasCaught = owner.GrabbedActorNumber != GorillaAI.NO_GRAB;

            if (!_hasCaught)
            {
                // 空振り。硬直へ
                _phase = Phase.Recover;
                _elapsedTime = 0.0f;
                owner.PlayAnimation(GorillaAI.ANIM_IDLE);
                return;
            }

            _phase = Phase.Hold;
            _elapsedTime = 0.0f;
            _bossHpAtGrab = owner.BossCurrentHp;
            owner.PlayAnimation(GorillaAI.ANIM_IDLE);

            ScreenFlash.Play(new Color(1.0f, 0.3f, 0.2f, 0.25f), 0.2f);
        }

        /// <summary>正面の扇形にいる、一番近い生きているプレイヤーを1人選ぶ</summary>
        private void DecideCatch(GorillaAI owner)
        {
            PlayerHealth best = null;
            float bestDistance = float.MaxValue;

            Vector3 forward = Quaternion.Euler(0.0f, _yawDeg, 0.0f) * Vector3.forward;

            foreach (var player in Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (player == null || player.IsDead) continue;

                Vector3 toPlayer = player.transform.position - _originalPosition;
                toPlayer.y = 0.0f;

                float distance = toPlayer.magnitude;
                if (distance > owner.GrabReach) continue;

                // 正面の扇形の外にいる相手は掴めない
                if (distance > 0.01f)
                {
                    float angle = Vector3.Angle(forward, toPlayer / distance);
                    if (angle > owner.GrabAngleDeg * 0.5f) continue;
                }

                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = player;
            }

            owner.GrabbedActorNumber = best != null ? best.OwnerActorNumber : GorillaAI.NO_GRAB;

            if (best != null) Debug.Log($"[Gorilla] {best.name} を掴みました", owner);
        }

        // ---- 拘束 ----------------------------------------

        /// <summary>
        /// 掴んでいる間。手が握り締めるたびにダメージが入り、掴まれた本人は連打で抜け出せる。
        /// 仲間から見えるよう、助けを呼ぶ文字と輪を出し続ける。
        /// </summary>
        private void UpdateHold(GorillaAI owner)
        {
            // 押さえ込んでいる感じを出すための細かい揺れ
            Vector2 jitter = Random.insideUnitCircle * HOLD_SHAKE_AMOUNT;
            owner.transform.position = _originalPosition + new Vector3(jitter.x, 0.0f, jitter.y);
            owner.transform.rotation = Quaternion.Euler(20.0f, _yawDeg, 0.0f);

            float liftRate = Mathf.Clamp01(_elapsedTime / LIFT_TIME);
            float holdHeight = Mathf.Lerp(owner.GrabHoldHeight * 0.4f, owner.GrabHoldHeight, liftRate);

            // 握り締める周期に合わせて手を縮める。掴まれている側の緊張感になる
            float squeeze = 1.0f - SQUEEZE_SCALE * Mathf.Max(0.0f, Mathf.Sin(_squeezeTimer / SQUEEZE_INTERVAL_SEC * Mathf.PI * 2.0f));
            PlaceHand(owner, owner.GrabHoldForwardOffset, holdHeight, squeeze);

            HoldLocalPlayer(owner, liftRate);
            UpdateSqueeze(owner);
            UpdateRescueCall(owner);

            // 逃げ出す条件を見るのは MasterClient。解放も同期で全員に伝わる
            if (owner.HasAuthority && ShouldRelease(owner))
            {
                BeginFinish(owner, isRescued: true);
                return;
            }

            if (_elapsedTime < owner.GrabHoldSec) return;

            BeginFinish(owner, isRescued: false);
        }

        /// <summary>握り締めるたびに削る。掴まれ続けると危ないと分かるようにする</summary>
        private void UpdateSqueeze(GorillaAI owner)
        {
            _squeezeTimer += Time.deltaTime;
            if (_squeezeTimer < SQUEEZE_INTERVAL_SEC) return;

            _squeezeTimer = 0.0f;

            if (owner.GrabSqueezeDamage <= 0) return;

            PlayerHealth localHealth = FindLocalPlayerHealth();
            if (localHealth == null || localHealth.IsDead) return;
            if (!IsLocalGrabbed(owner, localHealth)) return;

            // 吹き飛ばすと手から抜けてしまうので、ダメージだけ入れる
            localHealth.ApplyDamage(owner.GrabSqueezeDamage, -1, owner.transform.position, 0.0f, 0.01f, 0.0f);
            HitStop.Play(0.04f, 0.15f, 0.08f);
        }

        /// <summary>
        /// 掴まれていない人にも状況が伝わるよう、助けを呼ぶ文字と輪を繰り返し出す。
        /// 全クライアントで同じタイミングに出るので、追加の通信はいらない。
        /// </summary>
        private void UpdateRescueCall(GorillaAI owner)
        {
            _callTimer += Time.deltaTime;
            if (_callTimer < CALL_INTERVAL_SEC) return;

            _callTimer = 0.0f;

            Vector3 handPoint = HandPoint(owner);
            Onomatopoeia.Play(handPoint + Vector3.up * 1.2f, "たすけて!", new Color(1.0f, 0.85f, 0.2f, 1.0f), 1.1f, 1.0f);

            // 足元に輪を出して「ここを殴れ」を示す
            ShockwaveRing.Play(_originalPosition, new Color(1.0f, 0.8f, 0.2f, 1.0f), 6.0f, 0.55f, 0.5f);
        }

        /// <summary>解放されるか。仲間の攻撃か、掴まれた本人の連打で決まる</summary>
        private bool ShouldRelease(GorillaAI owner)
        {
            // 掴まれた本人が倒れた/居なくなった場合も解放する
            if (owner.GrabbedActorNumber == GorillaAI.NO_GRAB) return true;

            // 本人が連打で抜け出した
            if (owner.EscapeRequested) return true;

            if (owner.GrabRescueDamage <= 0) return false;

            int dealt = _bossHpAtGrab - owner.BossCurrentHp;
            return dealt >= owner.GrabRescueDamage;
        }

        /// <summary>
        /// 掴まれているのが自分のキャラなら、手の中へ運んで操作を止める。
        /// あわせて連打の入力を読み、抜け出したら MasterClient へ伝える。
        /// 他の人のキャラは通常の位置同期で運ばれるので、こちらでは触らない。
        /// </summary>
        private void HoldLocalPlayer(GorillaAI owner, float liftRate)
        {
            PlayerHealth localHealth = FindLocalPlayerHealth();
            if (localHealth == null) return;

            if (!IsLocalGrabbed(owner, localHealth))
            {
                ReleaseLocalPlayer();
                return;
            }

            if (_lockedMover == null)
            {
                _lockedMover = localHealth.GetComponent<LocalPlayerMover>();
                if (_lockedMover != null) _lockedMover.enabled = false;
            }

            // 掴まれた瞬間だけ手元へ引き寄せ、あとは手の中に貼り付ける
            Vector3 handPoint = HandPoint(owner);
            localHealth.transform.position = liftRate >= 1.0f
                ? handPoint
                : Vector3.Lerp(localHealth.transform.position, handPoint, liftRate);

            // 暴れている見た目。攻撃ボタンを押すたびに大きく揺れる
            ReadEscapeInput(owner, localHealth);
        }

        /// <summary>攻撃ボタンの連打を読んで脱出ゲージを削る。押すたびに手応えを返す</summary>
        private void ReadEscapeInput(GorillaAI owner, PlayerHealth localHealth)
        {
            if (owner.GrabEscapeMashCount <= 0) return;
            if (!GameInput.AttackPressed) return;

            _escapeRemain -= 1.0f;

            // 押した手応え。手が跳ね、体が揺れる
            if (_hand != null) _hand.transform.localScale = _handBaseScale * 1.08f;
            HitFlash.Play(localHealth.transform, new Color(1.0f, 0.95f, 0.4f, 1.0f), 0.12f, 0.8f);

            if (_escapeRemain > 0.0f) return;

            // 抜け出した。決めるのは MasterClient なので、抜け出したことだけを伝える
            Onomatopoeia.Play(
                localHealth.transform.position + Vector3.up * 1.5f, "ぬけた!", new Color(0.4f, 1.0f, 0.6f, 1.0f), 1.3f, 0.8f);
            owner.RequestGrabEscape();
        }

        // ---- 決着 ----------------------------------------

        /// <summary>叩きつけ、または解放。どちらもここを通る</summary>
        private void BeginFinish(GorillaAI owner, bool isRescued)
        {
            _wasRescued = isRescued;
            _phase = isRescued ? Phase.Recover : Phase.Slam;
            _elapsedTime = 0.0f;

            if (owner.HasAuthority) owner.GrabbedActorNumber = GorillaAI.NO_GRAB;

            if (!isRescued)
            {
                owner.PlayAnimation(GorillaAI.ANIM_STAMP_ATTACK);
                return;
            }

            // 助けられた。手を開いて落とすだけ
            owner.PlayAnimation(GorillaAI.ANIM_HIT);
            ReleaseLocalPlayer();
            DestroyHand();
        }

        /// <summary>掴んだまま地面へ振り下ろす。着いた瞬間に大ダメージ</summary>
        private void UpdateSlam(GorillaAI owner)
        {
            float rate = Mathf.Clamp01(_elapsedTime / SLAM_TIME);

            // 手を頭上から地面まで一気に下ろす
            float height = Mathf.Lerp(owner.GrabHoldHeight, 0.4f, rate * rate);
            float forward = Mathf.Lerp(owner.GrabHoldForwardOffset, owner.GrabHoldForwardOffset + 1.2f, rate);
            PlaceHand(owner, forward, height, 1.0f);

            _leanAngleDeg = Mathf.Lerp(20.0f, 34.0f, rate);
            owner.transform.SetPositionAndRotation(
                _originalPosition, Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f));

            // 掴まれた本人は手と一緒に落ちる
            PlayerHealth localHealth = FindLocalPlayerHealth();
            if (localHealth != null && _lockedMover != null)
            {
                localHealth.transform.position = HandPoint(owner);
            }

            if (_elapsedTime < SLAM_TIME) return;

            SlamImpact(owner);
        }

        /// <summary>叩きつけの瞬間。ダメージ・衝撃波・かけらをまとめて出す</summary>
        private void SlamImpact(GorillaAI owner)
        {
            Vector3 forward = Quaternion.Euler(0.0f, _yawDeg, 0.0f) * Vector3.forward;
            Vector3 impact = _originalPosition + forward * (owner.GrabHoldForwardOffset + 1.2f);

            PlayerHealth localHealth = FindLocalPlayerHealth();
            bool wasLocalGrabbed = localHealth != null && _lockedMover != null;

            ReleaseLocalPlayer();
            DestroyHand();

            ShockwaveRing.Play(impact, new Color(1.0f, 0.3f, 0.15f, 1.0f), 9.0f, 0.45f, 1.1f);
            Field.GrassField.FlattenAt(impact, 6.0f, 1.0f);
            HitStop.Play(0.08f, 0.06f, 0.12f);
            ShakeCamera(owner, 0.55f);

            GorillaRockDebris.Burst(
                owner.RockThrowRockPrefab, impact, owner.RockThrowDebrisCount,
                owner.RockThrowRockScale * owner.RockThrowDebrisScale * 0.8f, 6.0f, 6.0f, 2.0f);

            _phase = Phase.Recover;
            _elapsedTime = 0.0f;

            if (!wasLocalGrabbed || localHealth == null || localHealth.IsDead) return;

            localHealth.ApplyDamage(
                owner.GrabSlamDamage, -1, owner.transform.position,
                owner.GrabSlamKnockbackDistance, 0.55f, 3.0f);
        }

        private void UpdateRecover(GorillaAI owner)
        {
            float rate = Mathf.Clamp01(_elapsedTime / SLAM_RECOVERY_TIME);
            _leanAngleDeg = Mathf.Lerp(_wasRescued ? 20.0f : 34.0f, 0.0f, rate);
            owner.transform.SetPositionAndRotation(
                _originalPosition, Quaternion.Euler(_leanAngleDeg, _yawDeg, 0.0f));

            if (_elapsedTime < SLAM_RECOVERY_TIME) return;

            // 掴めたときは大ダメージを取れているので長めの隙、空振りや救出ならさらに長い隙を残す
            float multiplier = _hasCaught && !_wasRescued ? 1.0f : 1.4f;
            owner.ChangeState(new GorillaStateStagger(owner.GrabStaggerTime * multiplier));
        }

        // ---- 手のモデル ----------------------------------

        private void SpawnHand(GorillaAI owner)
        {
            if (owner.GrabHandPrefab == null) return;

            _hand = Object.Instantiate(owner.GrabHandPrefab, owner.transform);
            _handBaseScale = Vector3.one * owner.GrabHandScale;
            _hand.transform.localScale = _handBaseScale;

            PlaceHand(owner, 0.3f, owner.GrabHoldHeight * 0.4f, 1.0f);
        }

        /// <summary>掴む手を、体からの前後・高さで置く。squeeze は握り締めの縮み具合</summary>
        private void PlaceHand(GorillaAI owner, float forwardOffset, float height, float squeeze)
        {
            if (_hand == null) return;

            _hand.transform.localPosition = new Vector3(0.0f, height, forwardOffset);
            _hand.transform.localRotation = Quaternion.identity;
            _hand.transform.localScale = _handBaseScale * squeeze;
        }

        private void DestroyHand()
        {
            if (_hand == null) return;
            Object.Destroy(_hand);
            _hand = null;
        }

        // ---- 補助 ----------------------------------------

        /// <summary>掴んでいる手の中の位置(正面やや上)</summary>
        private Vector3 HandPoint(GorillaAI owner)
        {
            if (_hand != null) return _hand.transform.position;

            Vector3 forward = Quaternion.Euler(0.0f, _yawDeg, 0.0f) * Vector3.forward;
            return _originalPosition + forward * owner.GrabHoldForwardOffset + Vector3.up * owner.GrabHoldHeight;
        }

        /// <summary>自分が操作しているキャラの体力。見つからなければ null</summary>
        private static PlayerHealth FindLocalPlayerHealth()
        {
            PlayerAttack localAttack = PlayerAttack.Local;
            return localAttack == null ? null : localAttack.GetComponent<PlayerHealth>();
        }

        /// <summary>掴まれているのが自分のキャラか</summary>
        private static bool IsLocalGrabbed(GorillaAI owner, PlayerHealth localHealth)
        {
            int grabbed = owner.GrabbedActorNumber;
            return grabbed != GorillaAI.NO_GRAB && grabbed == localHealth.OwnerActorNumber;
        }

        /// <summary>止めていた移動スクリプトを戻す。掴まれっぱなしを防ぐため必ず通す</summary>
        private void ReleaseLocalPlayer()
        {
            if (_lockedMover == null) return;

            _lockedMover.enabled = true;
            _lockedMover = null;
        }

        /// <summary>水平の向きを相手の方へ、指定した速さだけ近づける</summary>
        private void TurnYawTowardsTarget(GorillaAI owner, float turnSpeedDeg)
        {
            if (owner.Target == null || turnSpeedDeg <= 0.0f) return;

            Vector3 toTarget = owner.Target.position - owner.transform.position;
            toTarget.y = 0.0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            float targetYaw = Quaternion.LookRotation(toTarget.normalized).eulerAngles.y;
            _yawDeg = Mathf.MoveTowardsAngle(_yawDeg, targetYaw, turnSpeedDeg * Time.deltaTime);
        }

        private void ShakeCamera(GorillaAI owner, float amplitude)
        {
            var camera = Object.FindAnyObjectByType<ThirdPersonCamera>();
            if (camera == null) return;

            camera.Shake(amplitude, 0.35f);
        }
    }
}
