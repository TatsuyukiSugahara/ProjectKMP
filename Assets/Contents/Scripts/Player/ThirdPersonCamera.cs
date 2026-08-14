using UnityEngine;
using UnityEngine.InputSystem;
using ProjectKMP.UI;

namespace ProjectKMP.Player
{
    /// <summary>
    /// プレイヤーを一定距離から追いかけるサードパーソンカメラ。矢印キーで左右に回り込む。
    ///
    /// 上下の回り込みは持たない。見下ろし角は固定で、上下に振れると空や地面が映って
    /// 戦況が見えなくなるうえ、初めて触る人が構図を崩したまま戻せなくなるため。
    ///
    /// ターゲットカメラを入れると、ボスの方向へ水平角が自動で追従する。
    /// 距離、回転速度、追従の滑らかさはすべてインスペクタから調整できる。
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private const float STICK_DEAD_ZONE = 0.2f;

        // ---- インスペクタ設定 ------------------------------

        [Header("追従対象")]
        [SerializeField, Tooltip("追いかける対象(プレイヤー)")]
        private Transform _target;

        [SerializeField, Tooltip("注視点の高さ。対象の足元からの相対(メートル)")]
        private float _targetHeight = 1.5f;

        [Header("距離")]
        [SerializeField, Tooltip("対象からのカメラ距離(メートル)")]
        private float _distance = 5.5f;

        [SerializeField, Tooltip("ズームで縮められる最小距離(メートル)")]
        private float _minDistance = 2.0f;

        [SerializeField, Tooltip("ズームで伸ばせる最大距離(メートル)")]
        private float _maxDistance = 25.0f;

        [SerializeField, Tooltip("マウスホイール1目盛りあたりのズーム量(メートル)。0でズーム無効")]
        private float _zoomSpeed = 1.0f;

        [Header("回転(左右のみ)")]
        [SerializeField, Tooltip("左右回転の速さ(度/秒)")]
        private float _yawSpeedDeg = 120.0f;

        [SerializeField, Range(-20.0f, 60.0f), Tooltip("見下ろし角(度)。固定で、操作では変えられない")]
        private float _fixedPitchDeg = 16.0f;

        [SerializeField, Tooltip("スワイプでの回転の効き(度/ピクセル)。大きいほど少ない指の動きで回る")]
        private float _touchLookSensitivity = 0.18f;

        [Header("追従")]
        [SerializeField, Tooltip("追従の滑らかさ(秒)。0で遅れなく追従する")]
        private float _followSmoothTime = 0.08f;

        [SerializeField, Tooltip("起動時の水平角(度)")]
        private float _initialYawDeg = 0.0f;

        [Header("ターゲットカメラ")]
        [SerializeField, Tooltip("ボタンでボスの方向にカメラを固定できるようにする")]
        private bool _enableTargetCamera = true;

        [SerializeField, Tooltip("Fキーで切り替える")]
        private bool _useFKey = true;

        [SerializeField, Tooltip("ゲームパッドの右スティック押し込みで切り替える")]
        private bool _useGamepadStickPress = true;

        [SerializeField, Min(0.0f), Tooltip("狙いへ向き直る速さ(度/秒)。大きいほど機敏だが酔いやすい")]
        private float _lockTurnSpeedDeg = 360.0f;

        [SerializeField, Min(0.0f), Tooltip("ターゲット中に少し引く量(メートル)。相手と自分を同時に映すため")]
        private float _lockDistanceAdd = 1.5f;

        [Header("障害物")]
        [SerializeField, Tooltip("木や壁にカメラがめり込むとき、手前に寄せる")]
        private bool _avoidObstacles = true;

        [SerializeField, Tooltip("障害物として扱うレイヤー")]
        private LayerMask _obstacleMask = ~0;

        [SerializeField, Tooltip("障害物判定に使う球の半径(メートル)")]
        private float _obstacleRadius = 0.3f;

        [Header("演出")]
        [SerializeField, Min(0f), Tooltip("寄り・引きの寄せ量が変わる速さ(m/秒)")]
        private float _offsetBlendSpeed = 12.0f;

        [SerializeField, Min(0f), Tooltip("視野角の寄せが変わる速さ(度/秒)")]
        private float _fovBlendSpeed = 60.0f;

        // ---- 内部状態 ------------------------------------

        private float _yawDeg;
        private float _pitchDeg;
        private float _currentDistance;
        private Vector3 _followVelocity;
        private bool _hasSnapped;
        private Camera _camera;
        private float _baseFieldOfView = 60f;
        private float _distanceOffsetTarget;
        private float _distanceOffsetCurrent;
        private float _fovOffsetTarget;
        private float _fovOffsetCurrent;

        /// <summary>ターゲット中に見ている相手。していなければ null</summary>
        private Transform _lockTarget;

        /// <summary>ボスを毎フレーム探し直さないための控え</summary>
        private Transform _bossTransform;

        private bool _togglePressedLastFrame;

        private float _shakeRemainSec;
        private float _shakeDurationSec;
        private float _shakeAmplitude;

        // ---- 公開API -------------------------------------

        /// <summary>追いかける対象</summary>
        public Transform Target
        {
            get => _target;
            set => _target = value;
        }

        /// <summary>対象からのカメラ距離(メートル)。最小・最大の範囲に丸められる</summary>
        public float Distance
        {
            get => _distance;
            set => _distance = Mathf.Clamp(value, _minDistance, _maxDistance);
        }

        /// <summary>
        /// いま追従を始めるとしたら、カメラがどの位置・向きになるかを返す。
        /// カットシーンのカメラから通常の追従へ滑らかに戻すために使う。
        /// 対象がまだ居ないときは false を返し、引数には今の値をそのまま入れる。
        /// </summary>
        public bool TryGetFollowPose(out Vector3 position, out Quaternion rotation)
        {
            position = transform.position;
            rotation = transform.rotation;

            if (_target == null) return false;

            position = CalcDesiredPosition();
            Vector3 focus = _target.position + Vector3.up * _targetHeight;
            rotation = Quaternion.LookRotation(focus - position, Vector3.up);
            return true;
        }

        /// <summary>
        /// 指定したワールド座標が画面に入るように、水平角をその方向へ即座に合わせる。
        /// カメラは対象の背後に回り込むため、「プレイヤーの背中越しに指定地点を見る」構図になる。
        /// 見下ろし角は固定なので変わらない。ゲーム開始時・リスポーン時にボスの方を向かせるのに使う。
        /// </summary>
        public void AimAt(Vector3 worldPosition)
        {
            if (_target == null) return;

            Vector3 toPoint = worldPosition - _target.position;
            toPoint.y = 0.0f;
            if (toPoint.sqrMagnitude < 0.0001f) return;

            _yawDeg = Quaternion.LookRotation(toPoint.normalized, Vector3.up).eulerAngles.y;
            SnapToTarget();
        }

        /// <summary>
        /// カメラの寄り(負の値で近づく)を指定する。0で元の距離に戻る。
        /// 溜め演出などで使う。指定した値へは少しずつ変化する。
        /// </summary>
        public void SetDistanceOffset(float offset)
        {
            _distanceOffsetTarget = offset;
        }

        /// <summary>視野角の寄せ(負の値で狭くなる)を指定する。0で元に戻る</summary>
        public void SetFovOffset(float offset)
        {
            _fovOffsetTarget = offset;
        }

        /// <summary>いまターゲットカメラで誰かを見ているか</summary>
        public bool IsLockedOn => _lockTarget != null;

        /// <summary>いま狙っている相手。狙っていなければ null。印を出す側が読む</summary>
        public Transform LockTarget => _lockTarget;

        /// <summary>
        /// ターゲットカメラを解く。クリアなど、操作が終わった場面で呼ぶ。
        /// すでに解けていれば何も起きない。
        /// </summary>
        public void ReleaseLockOn()
        {
            _lockTarget = null;
        }

        /// <summary>
        /// ターゲットカメラを入れ直す。相手が居なければ何も起きない。
        /// 画面のボタンからも呼べるように公開している。
        /// </summary>
        public void ToggleLockOn()
        {
            if (!_enableTargetCamera) return;

            if (_lockTarget != null) { _lockTarget = null; return; }

            _lockTarget = ResolveBossTransform();
        }

        /// <summary>カメラを短時間揺らす。スキルの爆発など衝撃の演出に使う</summary>
        public void Shake(float amplitude, float durationSec)
        {
            _shakeAmplitude = amplitude;
            _shakeDurationSec = Mathf.Max(0.01f, durationSec);
            _shakeRemainSec = _shakeDurationSec;
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _yawDeg = _initialYawDeg;
            _pitchDeg = _fixedPitchDeg;
            _currentDistance = _distance;

            _camera = GetComponent<Camera>();
            if (_camera != null) _baseFieldOfView = _camera.fieldOfView;
        }

        private void Start()
        {
            // 対象はネットワーク生成後に入ることがあるので、ここでは無くても構わない
            if (_target == null) return;

            SnapToTarget();
        }

        private void LateUpdate()
        {
            // 対象が居なくても寄せは戻し続ける
            UpdateShotOffsets();

            if (_target == null) return;

            // 対象が後から入ったときは、一度だけ位置を合わせてから追従を始める
            if (!_hasSnapped) SnapToTarget();

            ReadLockOnInput();

            // ターゲット中は自動で向き直るので、手での回転入力は受け取らない
            if (_lockTarget != null) UpdateLockOnYaw();
            else ReadRotationInput();

            ReadZoomInput();
            ApplyTransform(CalcDesiredPosition(), false);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>
        /// 矢印キーとゲームパッド右スティックから左右の回転入力を取る。
        /// 上下は読まない(見下ろし角は固定)。
        /// </summary>
        private void ReadRotationInput()
        {
            float input = 0.0f;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rightArrowKey.isPressed) input += 1.0f;
                if (keyboard.leftArrowKey.isPressed) input -= 1.0f;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                if (stick.sqrMagnitude > STICK_DEAD_ZONE * STICK_DEAD_ZONE) input += stick.x;
            }

            _yawDeg += input * _yawSpeedDeg * Time.deltaTime;

            // スマホは画面をなぞって回す。指の横移動だけを使い、縦のなぞりは無視する
            TouchControls touch = TouchControls.Instance;
            if (touch != null) _yawDeg += touch.LookDelta.x * _touchLookSensitivity;

            _pitchDeg = _fixedPitchDeg;
        }

        /// <summary>ターゲットカメラの入り切りを読む。押した瞬間だけを拾う</summary>
        private void ReadLockOnInput()
        {
            if (!_enableTargetCamera) return;

            bool pressed = false;

            Keyboard keyboard = Keyboard.current;
            if (_useFKey && keyboard != null && keyboard.fKey.isPressed) pressed = true;

            Gamepad gamepad = Gamepad.current;
            if (_useGamepadStickPress && gamepad != null && gamepad.rightStickButton.isPressed) pressed = true;

            TouchControls touch = TouchControls.Instance;
            if (touch != null && touch.TargetHeld) pressed = true;

            // 押しっぱなしで切り替わり続けないよう、離してから次を受け付ける
            if (pressed && !_togglePressedLastFrame) ToggleLockOn();

            _togglePressedLastFrame = pressed;

            // 相手が倒れて消えたら自動で解く。見えないものを見続けても仕方がない
            if (_lockTarget != null && !_lockTarget.gameObject.activeInHierarchy) _lockTarget = null;
        }

        /// <summary>
        /// ターゲット中の水平角。プレイヤーから相手への向きへ、少しずつ回り込む。
        /// 一瞬で向くと画面が飛んで酔うので、速さに上限をかける。
        /// </summary>
        private void UpdateLockOnYaw()
        {
            if (_lockTarget == null || _target == null) return;

            Vector3 toTarget = _lockTarget.position - _target.position;
            toTarget.y = 0.0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            float desiredYaw = Quaternion.LookRotation(toTarget.normalized, Vector3.up).eulerAngles.y;
            _yawDeg = Mathf.MoveTowardsAngle(_yawDeg, desiredYaw, _lockTurnSpeedDeg * Time.deltaTime);
            _pitchDeg = _fixedPitchDeg;
        }

        /// <summary>ボスを探す。一度見つけたら控えておき、毎フレーム探し直さない</summary>
        private Transform ResolveBossTransform()
        {
            if (_bossTransform != null && _bossTransform.gameObject.activeInHierarchy) return _bossTransform;

            Monster.BossHealth boss = FindAnyObjectByType<Monster.BossHealth>();
            _bossTransform = boss != null ? boss.transform : null;

            return _bossTransform;
        }

        /// <summary>マウスホイールでカメラ距離を変える</summary>
        private void ReadZoomInput()
        {
            if (Mathf.Approximately(_zoomSpeed, 0.0f)) return;

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0.0f)) return;

            // ホイール1目盛りは環境によって値が違うため、符号だけを使う
            Distance = _distance - Mathf.Sign(scroll) * _zoomSpeed;
        }

        /// <summary>
        /// 演出用の寄せ(距離と視野角)を指定値へ少しずつ近づける。
        /// ヒットストップやスローの最中でも同じ速さで動くよう、実時間で進める。
        /// </summary>
        private void UpdateShotOffsets()
        {
            _distanceOffsetCurrent = Mathf.MoveTowards(
                _distanceOffsetCurrent, _distanceOffsetTarget, _offsetBlendSpeed * Time.unscaledDeltaTime);

            _fovOffsetCurrent = Mathf.MoveTowards(
                _fovOffsetCurrent, _fovOffsetTarget, _fovBlendSpeed * Time.unscaledDeltaTime);

            if (_camera != null) _camera.fieldOfView = Mathf.Max(10f, _baseFieldOfView + _fovOffsetCurrent);
        }

        /// <summary>回転と距離から、カメラが居るべき位置を求める</summary>
        private Vector3 CalcDesiredPosition()
        {
            Vector3 focus = _target.position + Vector3.up * _targetHeight;
            Quaternion rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0.0f);

            // 演出で寄せている量を足した距離を基準にする。
            // ターゲット中は相手と自分を同時に映したいので、少しだけ引く
            float lockAdd = _lockTarget != null ? _lockDistanceAdd : 0.0f;
            float desired = Mathf.Max(_minDistance, _distance + _distanceOffsetCurrent + lockAdd);
            Vector3 offset = rotation * Vector3.back * desired;

            float distance = desired;
            if (_avoidObstacles && Physics.SphereCast(focus, _obstacleRadius, offset.normalized, out RaycastHit hit, desired, _obstacleMask, QueryTriggerInteraction.Ignore))
            {
                distance = Mathf.Max(_minDistance, hit.distance);
            }

            _currentDistance = distance;
            return focus + rotation * Vector3.back * distance;
        }

        /// <summary>対象の位置へ即座に合わせる</summary>
        private void SnapToTarget()
        {
            ApplyTransform(CalcDesiredPosition(), true);
            _hasSnapped = true;
        }

        /// <summary>位置と向きを反映する。snap が true なら補間せず即座に移動する</summary>
        private void ApplyTransform(Vector3 desiredPosition, bool snap)
        {
            if (snap || _followSmoothTime <= 0.0f)
            {
                transform.position = desiredPosition;
                _followVelocity = Vector3.zero;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _followVelocity, _followSmoothTime);
            }

            // 揺れの演出。残り時間に応じて減衰しながらランダムにずらす
            if (_shakeRemainSec > 0f)
            {
                _shakeRemainSec -= Time.deltaTime;
                float strength = _shakeDurationSec <= 0f ? 0f : Mathf.Clamp01(_shakeRemainSec / _shakeDurationSec);
                transform.position += Random.insideUnitSphere * (_shakeAmplitude * strength);
            }

            Vector3 focus = _target.position + Vector3.up * _targetHeight;
            transform.rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);
        }
    }
}
