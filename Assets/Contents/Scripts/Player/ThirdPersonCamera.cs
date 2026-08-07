using UnityEngine;
using UnityEngine.InputSystem;
using ProjectKMP.UI;

namespace ProjectKMP.Player
{
    /// <summary>
    /// プレイヤーを一定距離から追いかけるサードパーソンカメラ。矢印キーで左右・上下に回り込む。
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
        private float _distance = 8.0f;

        [SerializeField, Tooltip("ズームで縮められる最小距離(メートル)")]
        private float _minDistance = 2.0f;

        [SerializeField, Tooltip("ズームで伸ばせる最大距離(メートル)")]
        private float _maxDistance = 25.0f;

        [SerializeField, Tooltip("マウスホイール1目盛りあたりのズーム量(メートル)。0でズーム無効")]
        private float _zoomSpeed = 1.0f;

        [Header("回転(矢印キー)")]
        [SerializeField, Tooltip("左右回転の速さ(度/秒)")]
        private float _yawSpeedDeg = 120.0f;

        [SerializeField, Tooltip("上下回転の速さ(度/秒)")]
        private float _pitchSpeedDeg = 80.0f;

        [SerializeField, Tooltip("見下ろし角の下限(度)。マイナスで下から見上げる")]
        private float _minPitchDeg = -10.0f;

        [SerializeField, Tooltip("見下ろし角の上限(度)")]
        private float _maxPitchDeg = 70.0f;

        [SerializeField, Tooltip("上下の入力方向を反転する")]
        private bool _invertPitch = false;

        [SerializeField, Tooltip("スワイプでの回転の効き(度/ピクセル)。大きいほど少ない指の動きで回る")]
        private float _touchLookSensitivity = 0.18f;

        [Header("追従")]
        [SerializeField, Tooltip("追従の滑らかさ(秒)。0で遅れなく追従する")]
        private float _followSmoothTime = 0.08f;

        [SerializeField, Tooltip("起動時の水平角(度)")]
        private float _initialYawDeg = 0.0f;

        [SerializeField, Tooltip("起動時の見下ろし角(度)")]
        private float _initialPitchDeg = 20.0f;

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
        /// 見下ろし角は初期値に戻す。ゲーム開始時・リスポーン時にボスの方を向かせるのに使う。
        /// </summary>
        public void AimAt(Vector3 worldPosition)
        {
            if (_target == null) return;

            Vector3 toPoint = worldPosition - _target.position;
            toPoint.y = 0.0f;
            if (toPoint.sqrMagnitude < 0.0001f) return;

            _yawDeg = Quaternion.LookRotation(toPoint.normalized, Vector3.up).eulerAngles.y;
            _pitchDeg = Mathf.Clamp(_initialPitchDeg, _minPitchDeg, _maxPitchDeg);
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
            _pitchDeg = Mathf.Clamp(_initialPitchDeg, _minPitchDeg, _maxPitchDeg);
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

            ReadRotationInput();
            ReadZoomInput();
            ApplyTransform(CalcDesiredPosition(), false);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>矢印キーとゲームパッド右スティックから回転入力を取る</summary>
        private void ReadRotationInput()
        {
            Vector2 input = Vector2.zero;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rightArrowKey.isPressed) input.x += 1.0f;
                if (keyboard.leftArrowKey.isPressed) input.x -= 1.0f;
                if (keyboard.upArrowKey.isPressed) input.y += 1.0f;
                if (keyboard.downArrowKey.isPressed) input.y -= 1.0f;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                if (stick.sqrMagnitude > STICK_DEAD_ZONE * STICK_DEAD_ZONE) input += stick;
            }

            _yawDeg += input.x * _yawSpeedDeg * Time.deltaTime;
            _pitchDeg += (_invertPitch ? -input.y : input.y) * _pitchSpeedDeg * Time.deltaTime;

            // スマホは画面をなぞって回す。指の移動量にそのまま比例させる
            TouchControls touch = TouchControls.Instance;
            if (touch != null)
            {
                Vector2 swipe = touch.LookDelta;
                _yawDeg += swipe.x * _touchLookSensitivity;
                _pitchDeg += (_invertPitch ? -swipe.y : swipe.y) * _touchLookSensitivity;
            }
            _pitchDeg = Mathf.Clamp(_pitchDeg, _minPitchDeg, _maxPitchDeg);
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

            // 演出で寄せている量を足した距離を基準にする
            float desired = Mathf.Max(_minDistance, _distance + _distanceOffsetCurrent);
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
