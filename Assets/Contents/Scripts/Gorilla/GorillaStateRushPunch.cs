using ProjectKMP.Battle;
using ProjectKMP.Player;
using ProjectKMP.Presentation;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 連続パンチステート（中距離）。
    ///
    /// 片手ずつ交互に拳を突き出しながら、殴れる距離を保って追いかけてくる技。
    /// 突進が「一撃で間合いを消す」のに対して、こちらは「下がっても下がっても寄ってくる」圧のかけ方をする。
    ///
    /// 一定速度で前進する作りだと、逃げる相手には置いていかれて遠くで空振りし続け、
    /// 逆に立ち止まっている相手は追い越してしまうため、前進量ではなく「相手との距離」を制御している。
    /// 1発ごとに前へ踏み込み、当たった瞬間に短いヒットストップと画面揺れを入れて手応えを出す。
    ///
    /// 1発ずつの判定は拳の先だけなので、横に回り込めば抜けられる。
    /// ただし前進しながらゆるく向きを追うため、まっすぐ下がるだけでは振り切れない。
    /// 締めの1発だけは大きく吹き飛ばし、打ち終わりに長い硬直が残るので、そこが反撃のタイミングになる。
    /// </summary>
    public class GorillaStateRushPunch : IGorillaState
    {
        /// <summary>溜め中のアニメーション再生速度倍率</summary>
        private const float WINDUP_SPEED_MULTIPLIER = 0.15f;

        /// <summary>連打中のアニメーション再生速度倍率。走りを速回しして手数の多さを出す</summary>
        private const float RUSH_SPEED_MULTIPLIER = 2.0f;

        /// <summary>RUSH_SPEED_MULTIPLIER を決めたときの1発の間隔(秒)。実際の間隔との比で再生速度を合わせる</summary>
        private const float RUSH_REFERENCE_INTERVAL = 0.3f;

        /// <summary>
        /// 打っている間の体の傾き(度、X軸)。マイナスで上体を起こす。
        /// ゴリラは四足なので手が地面すれすれにあり、そのまま拳を出すと地面に埋まる。
        /// 後ろへ反らせて前脚を浮かせることで、拳が地面から出る。
        /// </summary>
        private const float REAR_UP_DEG = -20.0f;

        /// <summary>反らせたときに後ろ足が地面へめり込まないよう持ち上げる計算に使う、後ろ足までの距離(メートル)</summary>
        private const float REAR_PIVOT_DISTANCE = 0.7f;

        /// <summary>溜め中の体の震え幅の最大値(メートル)</summary>
        private const float MAX_SHAKE_AMOUNT = 0.08f;

        /// <summary>1発の中で拳を外へ引ききるタイミング(0〜1)。ここまでが溜め</summary>
        private const float DRAW_RATIO = 0.30f;

        /// <summary>1発の中で拳が伸びきるタイミング(0〜1)。引ききってからここまでで打ち抜く</summary>
        private const float STRIKE_RATIO = 0.62f;

        /// <summary>1発の中で当たり判定が出るタイミング(0〜1)。拳がほぼ伸びきったところ</summary>
        private const float HIT_TIMING_RATIO = 0.56f;

        /// <summary>この距離ぶんは詰め寄らずに許容する(メートル)。毎フレーム前後してガタつくのを防ぐ</summary>
        private const float KEEP_DISTANCE_TOLERANCE = 0.35f;

        /// <summary>保ちたい距離からこれだけ離れると全速で詰める(メートル)</summary>
        private const float CATCHUP_RANGE = 4.0f;

        /// <summary>近づきすぎたときに下がる速さの倍率</summary>
        private const float BACKOFF_SPEED_RATIO = 0.35f;

        /// <summary>構えているときの拳の左右の開き(ワールド基準のメートル)</summary>
        private const float GUARD_SIDE_OFFSET = 0.85f;

        /// <summary>突き出しきったときの拳の左右の開き(ワールド基準のメートル)。当たり判定は正面なので中心へ寄せる</summary>
        private const float EXTEND_SIDE_OFFSET = 0.1f;

        /// <summary>
        /// 打つ前に拳を外へ引く量(ワールド基準のメートル)。
        /// ここから内へ切り込ませることでフックの弧になる。まっすぐ出すと突きに見えてしまう。
        /// </summary>
        private const float DRAW_SIDE_OFFSET = 1.05f;

        /// <summary>打つ前に拳を後ろへ引く量(ワールド基準のメートル)</summary>
        private const float DRAW_BACK_OFFSET = 0.35f;

        /// <summary>
        /// 弧のふくらみを決める制御点を、打つ距離の何割ぶん前に置くか。
        /// 大きいほど外を大きく回り込んでから内へ入ってくる。
        /// </summary>
        private const float HOOK_CONTROL_FORWARD_RATIO = 0.55f;

        /// <summary>構えているときの拳の前方位置(ワールド基準のメートル)。胸の前あたり</summary>
        private const float GUARD_FORWARD_OFFSET = 1.3f;

        /// <summary>打つ瞬間に拳が沈み込む量(ワールド基準のメートル)</summary>
        private const float PUNCH_DIP_HEIGHT = 0.35f;

        /// <summary>打ち出した瞬間に拳を大きく見せる割合。当たりの重さを見た目でも出す</summary>
        private const float FIST_PUNCH_SCALE_UP = 0.18f;

        /// <summary>打つ前に体をひねって溜める角度(度、Y軸)。打つ側の肩を後ろへ引く</summary>
        private const float BODY_TWIST_WINDUP_DEG = 28.0f;

        /// <summary>打ち抜いたときに体をひねる角度(度、Y軸)。打つ側の肩を前へ送り出す</summary>
        private const float BODY_TWIST_STRIKE_DEG = 44.0f;

        /// <summary>
        /// 拳を自分の向き(Z軸)まわりに倒す角度(度)。左右で符号を反転させて内向きに揃える。
        /// 地割れと同じ持ち方に見せるための回転。逆向きに見えたらこの符号を反転させる。
        /// </summary>
        private const float FIST_ROLL_DEG = 90.0f;

        /// <summary>打ち終わってから硬直へ移るまでの余韻(秒)</summary>
        private const float FINISH_TIME = 0.25f;

        private enum Phase
        {
            /// <summary>構えて溜める</summary>
            Windup,
            /// <summary>距離を保ちながら交互に打つ</summary>
            Rush,
            /// <summary>打ち終わりの余韻</summary>
            Finish,
        }

        private Phase _phase;
        private float _elapsedTime;
        private float _baseAnimatorSpeed;

        private float _yawDeg;
        private Vector3 _windupStartPosition;

        /// <summary>足元の高さ。反らせて浮かせた体を戻すときの基準</summary>
        private float _groundY;

        /// <summary>この技で何発打つか</summary>
        private int _totalPunches;

        /// <summary>いま何発目か(0始まり)</summary>
        private int _punchIndex;

        /// <summary>1発の中での進み具合(0〜1)</summary>
        private float _punchRatio;

        /// <summary>この1発でいままでに踏み込んだ距離(メートル)。差分だけ位置に足すために持つ</summary>
        private float _appliedLunge;

        private bool _hasHitThisPunch;
        private int _hitCount;

        private GameObject _rightFist;
        private GameObject _leftFist;
        private Vector3 _rightFistBaseScale;
        private Vector3 _leftFistBaseScale;

        private GorillaAttackTelegraph _telegraph;
        private ThirdPersonCamera _camera;

        /// <summary>いま打っているのが右手か。1発ごとに入れ替わる</summary>
        private bool IsRightPunch => (_punchIndex % 2) == 0;

        /// <summary>いま打っているのが締めの1発か</summary>
        private bool IsFinalPunch => _punchIndex >= _totalPunches - 1;

        public void Enter(GorillaAI owner)
        {
            _phase = Phase.Windup;
            _elapsedTime = 0.0f;
            _punchIndex = 0;
            _punchRatio = 0.0f;
            _appliedLunge = 0.0f;
            _hasHitThisPunch = false;
            _hitCount = 0;
            _yawDeg = owner.transform.eulerAngles.y;
            _windupStartPosition = owner.transform.position;
            _groundY = _windupStartPosition.y;
            _totalPunches = Mathf.Max(1, owner.RollRushPunchCount());

            // 1発ごとに探すと連打の途中で処理が重くなるので、入り口で一度だけ持っておく
            _camera = Object.FindAnyObjectByType<ThirdPersonCamera>();

            _baseAnimatorSpeed = owner.Animator != null ? owner.Animator.speed : 1.0f;
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed * WINDUP_SPEED_MULTIPLIER;

            owner.PlayAnimation(GorillaAI.ANIM_SWEEP_ATTACK);

            SpawnFists(owner);

            // 詰めてくる距離ぶんの帯を出す。相手に近づくほど減速するので、平均の前進量で見積もる
            float travel = owner.RushPunchSpeed * 0.6f * (owner.RushPunchInterval * _totalPunches);
            _telegraph = GorillaAttackTelegraph.SpawnBand(
                owner.AttackTelegraphPrefab, owner.transform.position, _yawDeg,
                travel + owner.RushPunchReach, owner.RushPunchHitRadius * 2.0f);

            owner.NotifyRushPunchUsed();
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Windup: UpdateWindup(owner); break;
                case Phase.Rush:   UpdateRush(owner);   break;
                case Phase.Finish: UpdateFinish(owner); break;
            }
        }

        public void Exit(GorillaAI owner)
        {
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;

            // 傾けたまま・浮かせたまま抜けると以後ずっとそのままになるので、必ず戻す
            Vector3 grounded = owner.transform.position;
            grounded.y = _groundY;
            owner.transform.SetPositionAndRotation(grounded, Quaternion.Euler(0.0f, _yawDeg, 0.0f));

            DestroyFists();
            GorillaAttackTelegraph.Dismiss(_telegraph);
            _telegraph = null;
            _camera = null;
        }

        // ---- 溜め ----------------------------------------

        /// <summary>構え。両拳を上げて低くなりながら震える</summary>
        private void UpdateWindup(GorillaAI owner)
        {
            float windupTime = Mathf.Max(0.05f, owner.RushPunchWindupTime);
            float rate = Mathf.Clamp01(_elapsedTime / windupTime);

            TurnYawTowardsTarget(owner, owner.RushPunchHomingSpeedDeg * 2.0f);

            Vector2 jitter = Random.insideUnitCircle * (MAX_SHAKE_AMOUNT * rate);
            Vector3 position = _windupStartPosition + new Vector3(jitter.x, 0.0f, jitter.y);
            position.y = _groundY + BodyLift() * rate;

            owner.transform.SetPositionAndRotation(
                position, Quaternion.Euler(REAR_UP_DEG * rate, _yawDeg, 0.0f));

            UpdateFistVisual(owner, 0.0f);
            if (_telegraph != null) _telegraph.Follow(owner.transform.position, _yawDeg);

            if (_elapsedTime < windupTime) return;

            BeginRush(owner);
        }

        // ---- 連打 ----------------------------------------

        private void BeginRush(GorillaAI owner)
        {
            _phase = Phase.Rush;
            _elapsedTime = 0.0f;
            _punchRatio = 0.0f;
            _appliedLunge = 0.0f;
            _hasHitThisPunch = false;

            owner.transform.position = _windupStartPosition;

            GorillaAttackTelegraph.Dismiss(_telegraph);
            _telegraph = null;

            // 間隔を変えても足の動きが打つ速さと合うように、再生速度を間隔から逆算する
            float interval = Mathf.Max(0.05f, owner.RushPunchInterval);
            float animationSpeed = RUSH_SPEED_MULTIPLIER * (RUSH_REFERENCE_INTERVAL / interval);
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed * animationSpeed;
            owner.PlayAnimation(GorillaAI.ANIM_RUN);
        }

        /// <summary>殴れる距離を保ちながら片手ずつ打つ。1発ぶんが終わるたびに手を入れ替える</summary>
        private void UpdateRush(GorillaAI owner)
        {
            float interval = Mathf.Max(0.05f, owner.RushPunchInterval);

            // ゆるく相手を追う。まっすぐ下がるだけでは振り切れないが、横へ回れば抜けられる速さにする
            TurnYawTowardsTarget(owner, owner.RushPunchHomingSpeedDeg);

            _punchRatio = Mathf.Clamp01(_elapsedTime / interval);

            // 体ごとひねる。拳だけ動いていると腕を出しているだけに見えるので、
            // 打つ側の肩を引いてから送り出すことで打撃の勢いを出す。
            // 当たり判定と拳の到達点は _yawDeg から作っているので、ここでひねっても狙いはズレない
            owner.transform.rotation = Quaternion.Euler(REAR_UP_DEG, _yawDeg + BodyTwistDeg(), 0.0f);

            UpdateApproach(owner);

            // 反らせたぶん体を持ち上げておく。前進処理は水平にしか動かさないのでここで高さを決める
            Vector3 lifted = owner.transform.position;
            lifted.y = _groundY + BodyLift();
            owner.transform.position = lifted;
            UpdateFistVisual(owner, _punchRatio);

            if (!_hasHitThisPunch && _punchRatio >= HIT_TIMING_RATIO)
            {
                _hasHitThisPunch = true;
                TryApplyDamageToLocalPlayer(owner);
            }

            if (_elapsedTime < interval) return;

            // 1発ぶん終わり。手を入れ替えて次へ
            _punchIndex++;
            _elapsedTime = 0.0f;
            _punchRatio = 0.0f;
            _appliedLunge = 0.0f;
            _hasHitThisPunch = false;

            if (_punchIndex < _totalPunches) return;

            BeginFinish(owner);
        }

        /// <summary>
        /// 殴れる距離を保つように前後する。
        ///
        /// 一定速度で前へ進むだけだと、逃げる相手には離され、止まっている相手は追い越してしまう。
        /// 距離そのものを目標にして、遠ければ強く詰め、近すぎれば少し下がることで、
        /// 常に拳が届く位置に居続ける。これがないと「遠くで手を振っているだけ」の画になる。
        /// </summary>
        private void UpdateApproach(GorillaAI owner)
        {
            Vector3 forward = Quaternion.Euler(0.0f, _yawDeg, 0.0f) * Vector3.forward;

            // 打つ瞬間だけ前へ踏み込む。1発ごとに体が入ることで殴っている感じが出る
            float lungeNow = Mathf.Sin(_punchRatio * Mathf.PI) * owner.RushPunchLungeDistance;
            float lungeDelta = lungeNow - _appliedLunge;
            _appliedLunge = lungeNow;

            float keep = owner.RushPunchKeepDistance;
            float distance = HorizontalDistanceToTarget(owner, keep);

            float speed = 0.0f;
            if (distance > keep + KEEP_DISTANCE_TOLERANCE)
            {
                // 離れているほど強く踏み込む。真後ろへ逃げる相手にも置いていかれない速さを出す
                float gain = Mathf.InverseLerp(keep, keep + CATCHUP_RANGE, distance);
                speed = Mathf.Lerp(owner.RushPunchSpeed * 0.5f, owner.RushPunchSpeed, gain);
            }
            else if (distance < keep - KEEP_DISTANCE_TOLERANCE)
            {
                // 近づきすぎ。めり込んで殴れなくなるので少しだけ下がる
                speed = -owner.RushPunchSpeed * BACKOFF_SPEED_RATIO;
            }

            owner.transform.position += forward * (speed * Time.deltaTime + lungeDelta);
        }

        /// <summary>
        /// いま打っている手に合わせた体のひねり(度、Y軸)。
        /// 溜めで打つ側の肩を後ろへ引き、打ち抜きで一気に前へ送り、戻しで正面へ返す。
        /// </summary>
        private float BodyTwistDeg()
        {
            // 右手なら右肩を前へ出したいので、マイナス方向へ回すのが「打ち抜き」になる
            float sign = IsRightPunch ? 1.0f : -1.0f;

            if (_punchRatio <= DRAW_RATIO)
            {
                float t = _punchRatio / DRAW_RATIO;
                return Mathf.Lerp(0.0f, BODY_TWIST_WINDUP_DEG, EaseOut(t)) * sign;
            }

            if (_punchRatio <= STRIKE_RATIO)
            {
                float t = (_punchRatio - DRAW_RATIO) / (STRIKE_RATIO - DRAW_RATIO);
                float eased = 1.0f - (1.0f - t) * (1.0f - t) * (1.0f - t);
                return Mathf.Lerp(BODY_TWIST_WINDUP_DEG, -BODY_TWIST_STRIKE_DEG, eased) * sign;
            }

            float back = (_punchRatio - STRIKE_RATIO) / (1.0f - STRIKE_RATIO);
            return Mathf.Lerp(-BODY_TWIST_STRIKE_DEG, 0.0f, EaseOut(back)) * sign;
        }

        /// <summary>
        /// 反らせたときに体を持ち上げる量(メートル)。
        /// 足元を軸に後ろへ倒すと後ろ足が地面より下へ行ってしまうので、その沈み込みぶんを打ち消す。
        /// </summary>
        private static float BodyLift()
        {
            return Mathf.Abs(Mathf.Sin(REAR_UP_DEG * Mathf.Deg2Rad)) * REAR_PIVOT_DISTANCE;
        }

        /// <summary>相手との水平距離。相手がいなければ保ちたい距離を返して動かさない</summary>
        private float HorizontalDistanceToTarget(GorillaAI owner, float fallback)
        {
            if (owner.Target == null) return fallback;

            Vector3 toTarget = owner.Target.position - owner.transform.position;
            toTarget.y = 0.0f;
            return toTarget.magnitude;
        }

        // ---- 打ち終わり ----------------------------------

        private void BeginFinish(GorillaAI owner)
        {
            _phase = Phase.Finish;
            _elapsedTime = 0.0f;

            DestroyFists();

            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;

            Vector3 grounded = owner.transform.position;
            grounded.y = _groundY;
            owner.transform.SetPositionAndRotation(grounded, Quaternion.Euler(0.0f, _yawDeg, 0.0f));

            owner.PlayAnimation(GorillaAI.ANIM_IDLE);
        }

        /// <summary>打ち終わったあとは長めの硬直。1発も当てられなかったときはさらに長くする</summary>
        private void UpdateFinish(GorillaAI owner)
        {
            if (_elapsedTime < FINISH_TIME) return;

            float staggerTime = owner.RushPunchStaggerTime * (_hitCount > 0 ? 1.0f : 1.3f);
            owner.ChangeState(new GorillaStateStagger(staggerTime));
        }

        // ---- 拳の見た目 ----------------------------------

        /// <summary>握り拳のモデルを両手ぶん出す。左右は拡大率の符号を反転させて作る</summary>
        private void SpawnFists(GorillaAI owner)
        {
            GameObject fistPrefab = owner.RushPunchFistPrefab;
            if (fistPrefab == null) return;

            // ゴリラ本体はシーンで拡大されている。子にすると拡大率がそのまま掛かってしまうので、
            // 見た目の大きさも位置もワールド基準で指定できるように、ここで割っておく
            float scale = owner.RushPunchFistScale / ParentScale(owner);

            _rightFistBaseScale = new Vector3(-scale, scale, scale);
            _rightFist = Object.Instantiate(fistPrefab, owner.transform);
            _rightFist.transform.localScale = _rightFistBaseScale;

            _leftFistBaseScale = new Vector3(scale, scale, scale);
            _leftFist = Object.Instantiate(fistPrefab, owner.transform);
            _leftFist.transform.localScale = _leftFistBaseScale;

            UpdateFistVisual(owner, 0.0f);
        }

        /// <summary>
        /// 拳の位置を更新する。
        ///
        /// 出しているのは打っている側の1つだけ。両方出していると体の幅に対して拳が近すぎて、
        /// 拳が2つ並んでいるだけの絵になってしまうため、待っている側は消しておく。
        /// </summary>
        private void UpdateFistVisual(GorillaAI owner, float ratio)
        {
            bool rightPunching = IsRightPunch;

            if (_rightFist != null) _rightFist.SetActive(rightPunching);
            if (_leftFist != null) _leftFist.SetActive(!rightPunching);

            if (rightPunching)
            {
                UpdateOneFist(owner, _rightFist, _rightFistBaseScale, isRight: true, ratio: ratio);
            }
            else
            {
                UpdateOneFist(owner, _leftFist, _leftFistBaseScale, isRight: false, ratio: ratio);
            }
        }

        /// <summary>
        /// 拳1つぶんの見た目を更新する。
        ///
        /// 1発を「外へ引く → 外を回りながら内へ打ち抜く → 戻す」の3段に分ける。
        /// 直線で伸ばすとフックではなく突きに見えるうえ、伸びきるまでが速すぎて
        /// 弧を描いていることが目で追えないので、引く時間をはっきり取っている。
        /// </summary>
        private void UpdateOneFist(GorillaAI owner, GameObject fist, Vector3 baseScale, bool isRight, float ratio)
        {
            if (fist == null) return;

            float parentScale = ParentScale(owner);
            float sign = isRight ? 1.0f : -1.0f;

            // 構えの位置はモデル上で実際に手がある場所。腕がアニメーションで動けば拳も一緒に動く
            Vector3 guardLocal;
            if (!owner.TryGetHandAnchorLocal(isRight, out guardLocal))
            {
                guardLocal = new Vector3(
                    GUARD_SIDE_OFFSET * sign,
                    owner.RushPunchFistHeight + PUNCH_DIP_HEIGHT,
                    GUARD_FORWARD_OFFSET) / parentScale;
            }

            // 引ききった位置。手の位置から外と後ろへ振りかぶる
            Vector3 drawLocal = guardLocal + new Vector3(
                DRAW_SIDE_OFFSET * sign, 0.0f, -DRAW_BACK_OFFSET) / parentScale;

            // 打ち切った位置は当たり判定と同じ距離・高さ。ここがズレていると
            // 「当たっているのに拳は遠く」「拳は届いているのに当たらない」という画になる。
            // 体をX軸で反らせているぶんローカル座標だと上へ流れてしまうので、
            // 一度ワールドで当たり判定と同じ点を作ってからローカルへ戻す
            Quaternion yaw = Quaternion.Euler(0.0f, _yawDeg, 0.0f);
            Vector3 groundPosition = owner.transform.position;
            groundPosition.y = _groundY;

            Vector3 strikeWorld = groundPosition
                + (yaw * Vector3.forward) * owner.RushPunchReach
                + (yaw * Vector3.right) * (EXTEND_SIDE_OFFSET * sign)
                + Vector3.up * owner.RushPunchFistHeight;
            Vector3 strikeLocal = owner.transform.InverseTransformPoint(strikeWorld);

            float extend;
            Vector3 local = SamplePath(owner, ratio, guardLocal, drawLocal, strikeLocal, parentScale, out extend);

            fist.transform.localPosition = local;

            // ゴリラを反らせても足りないぶんは、拳自身を地面の上へ押し上げる。
            // 体の傾きに関係なく効かせたいのでワールド座標で見る
            Vector3 world = fist.transform.position;
            float lowest = _groundY + owner.RushPunchFistScale * 0.45f;
            if (world.y < lowest)
            {
                world.y = lowest;
                fist.transform.position = world;
            }

            // 拳のモデルは +Z が殴る向きに作ってあるので、向き補正は要らない。
            // 引いている間は外を向き、打ち抜くにつれて内へ回る。腕をたたむ動きに見せる
            float twist = Mathf.Lerp(isRight ? 26.0f : -26.0f, isRight ? -32.0f : 32.0f, extend);
            fist.transform.localRotation = Quaternion.Euler(0.0f, twist, -FIST_ROLL_DEG * sign);

            // 伸びきった拳を少しだけ大きく見せる。手前に来るほど当たりが重く見える
            fist.transform.localScale = baseScale * (1.0f + FIST_PUNCH_SCALE_UP * extend);
        }

        /// <summary>
        /// 1発の中の進み具合から、拳の位置と伸び具合を求める。
        /// </summary>
        /// <param name="extend">0が構え、1が打ち切り。ひねりと拡大に使う</param>
        private Vector3 SamplePath(GorillaAI owner, float ratio, Vector3 guardLocal, Vector3 drawLocal,
            Vector3 strikeLocal, float parentScale, out float extend)
        {
            ratio = Mathf.Clamp01(ratio);

            // 溜め: 外へ引く。ここをはっきり見せないと弧が始まったことが分からない
            if (ratio <= DRAW_RATIO)
            {
                float t = ratio / DRAW_RATIO;
                extend = 0.0f;
                return Vector3.Lerp(guardLocal, drawLocal, EaseOut(t));
            }

            // 打ち抜き: 外を回り込んでから内へ切り込む。制御点を外の前方に置いて弧にする
            if (ratio <= STRIKE_RATIO)
            {
                float t = (ratio - DRAW_RATIO) / (STRIKE_RATIO - DRAW_RATIO);
                extend = 1.0f - (1.0f - t) * (1.0f - t) * (1.0f - t);

                Vector3 control = drawLocal + new Vector3(
                    0.0f, 0.0f, owner.RushPunchReach * HOOK_CONTROL_FORWARD_RATIO) / parentScale;
                return QuadraticBezier(drawLocal, control, strikeLocal, extend);
            }

            // 戻し
            float back = (ratio - STRIKE_RATIO) / (1.0f - STRIKE_RATIO);
            extend = (1.0f - back) * (1.0f - back);
            return Vector3.Lerp(strikeLocal, guardLocal, EaseOut(back));
        }

        private static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1.0f - (1.0f - t) * (1.0f - t);
        }

        private static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1.0f - t;
            return (u * u) * a + (2.0f * u * t) * b + (t * t) * c;
        }

        /// <summary>
        /// ゴリラ本体に掛かっている拡大率。拳は子オブジェクトなのでこれがそのまま掛かる。
        /// 位置と大きさをワールド基準のメートルで書けるように、指定値をこの値で割って使う。
        /// </summary>
        private static float ParentScale(GorillaAI owner)
        {
            return Mathf.Max(0.001f, owner.transform.lossyScale.x);
        }

        private void DestroyFists()
        {
            if (_rightFist != null) { Object.Destroy(_rightFist); _rightFist = null; }
            if (_leftFist != null) { Object.Destroy(_leftFist); _leftFist = null; }
        }

        // ---- 向き・当たり判定 ----------------------------

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

        /// <summary>
        /// 自分が操作しているローカルプレイヤーだけを対象に、突き出した拳の先で判定する。
        /// (他の攻撃と同じ方式。各自が自分のぶんだけ判定することで多重ダメージを避ける)
        /// </summary>
        private void TryApplyDamageToLocalPlayer(GorillaAI owner)
        {
            if (owner.RushPunchDamage <= 0) return;

            PlayerAttack localAttack = PlayerAttack.Local;
            if (localAttack == null) return;

            PlayerHealth localHealth = localAttack.GetComponent<PlayerHealth>();
            if (localHealth == null || localHealth.IsDead) return;

            // 判定の中心は拳が伸びきった先。体の周りではないので、横に回り込めば当たらない
            Vector3 direction = Quaternion.Euler(0.0f, _yawDeg, 0.0f) * Vector3.forward;
            Vector3 fistPoint = owner.transform.position + direction * owner.RushPunchReach;

            Vector3 toPlayer = localHealth.transform.position - fistPoint;
            toPlayer.y = 0.0f;
            if (toPlayer.magnitude > owner.RushPunchHitRadius) return;

            _hitCount++;

            bool isFinal = IsFinalPunch;

            // 連打の途中はほとんど吹き飛ばさない。押し出してしまうと自分で間合いを崩して空振りが続く
            int damage = isFinal
                ? Mathf.RoundToInt(owner.RushPunchDamage * owner.RushPunchFinishDamageMultiplier)
                : owner.RushPunchDamage;
            float knockback = isFinal ? owner.RushPunchFinishKnockbackDistance : owner.RushPunchKnockbackDistance;

            localHealth.ApplyDamage(
                damage, -1, owner.transform.position,
                knockback, isFinal ? 0.4f : 0.18f, isFinal ? 1.6f : 0.0f);

            PlayImpact(owner, fistPoint, direction, isFinal);
        }

        /// <summary>当たった瞬間の手応え。連打の途中は短く軽く、締めの1発だけしっかり止めて見せる</summary>
        private void PlayImpact(GorillaAI owner, Vector3 point, Vector3 direction, bool isFinal)
        {
            SpawnHitEffect(owner, point, direction, isFinal);

            HitStop.Play(
                isFinal ? 0.07f : 0.028f,
                isFinal ? 0.08f : 0.2f,
                isFinal ? 0.1f : 0.05f);

            if (_camera != null)
            {
                _camera.Shake(isFinal ? 0.5f : 0.16f, isFinal ? 0.3f : 0.1f);
            }

            if (!isFinal) return;

            Color color = new Color(1.0f, 0.62f, 0.25f, 1.0f);
            ScreenFlash.Play(new Color(color.r, color.g, color.b, 0.28f), 0.18f);
            Onomatopoeia.Play(point + Vector3.up * (owner.RushPunchFistHeight + 1.0f), "ドガッ", color, 1.25f, 0.7f);
        }

        private void SpawnHitEffect(GorillaAI owner, Vector3 position, Vector3 direction, bool isFinal)
        {
            if (owner.NormalAttackHitEffectPrefab == null) return;

            Vector3 pos = position + Vector3.up * owner.RushPunchFistHeight;
            var instance = Object.Instantiate(owner.NormalAttackHitEffectPrefab, pos, Quaternion.LookRotation(direction));

            // ScalingMode が Shape のパーティクルは localScale が効かないため、Hierarchy に切り替えてから拡大する
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            // 連打の途中は頭突きより小さく、締めの1発だけ大きく出して区切りを付ける
            float scale = owner.NormalAttackHitEffectScale * (isFinal ? 1.1f : 0.55f);
            instance.transform.localScale = Vector3.one * scale;
        }
    }
}
