using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Player
{
    /// <summary>
    /// ネットワーク同期を伴わない、ローカル操作専用のプレイヤー移動。WASD で移動する。
    /// 移動方向はカメラ基準。矢印キーはサードパーソンカメラの回転に使うため、移動には割り当てない。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class LocalPlayerMover : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>接地判定を安定させるために常にかけておく下向き速度</summary>
        private const float GROUNDED_PULL = -2.0f;

        private const float STICK_DEAD_ZONE = 0.2f;

        // ---- インスペクタ設定 ------------------------------

        [Header("移動")]
        [SerializeField, Tooltip("移動速度(m/秒)")]
        private float _moveSpeed = 6.0f;

        [SerializeField, Tooltip("左Shift を押している間の速度倍率")]
        private float _sprintMultiplier = 1.8f;

        [SerializeField, Tooltip("進行方向へ向き直る速さ(度/秒)")]
        private float _turnSpeedDeg = 720.0f;

        [SerializeField, Tooltip("重力加速度(m/秒^2)。負の値を入れる")]
        private float _gravity = -20.0f;

        [Header("参照")]
        [SerializeField, Tooltip("移動方向の基準にするカメラ。未設定なら Camera.main を使う")]
        private Transform _cameraTransform;

        // ---- 内部状態 ------------------------------------

        private CharacterController _controller;
        private float _verticalVelocity;

        /// <summary>移動の制限方法</summary>
        public enum MovementLock
        {
            /// <summary>制限なし</summary>
            None,

            /// <summary>その場で向きだけ変えられる(ビームの狙い中など)</summary>
            RotateOnly,

            /// <summary>移動も向き変えもできない(ビームの照射中など)。重力は効く</summary>
            Full,

            /// <summary>スキルが座標を直接動かす間、移動・向き変え・重力をすべて止める(空中に浮かせるときなど)</summary>
            Frozen,
        }

        /// <summary>スキルなどから移動を一時的に制限する</summary>
        public MovementLock MoveLock { get; set; } = MovementLock.None;

        // ---- 公開API -------------------------------------

        /// <summary>いまの水平方向の速さ(m/秒)。アニメーション接続などに使う</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>移動の基準にするカメラを差し替える</summary>
        public void SetCamera(Transform cameraTransform)
        {
            _cameraTransform = cameraTransform;
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void Start()
        {
            if (_cameraTransform == null && Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }
        }

        private void Update()
        {
            // スキル側が座標を動かすので、移動・向き変え・重力のすべてをこちらでは行わない
            if (MoveLock == MovementLock.Frozen)
            {
                _verticalVelocity = 0.0f;
                CurrentSpeed = 0.0f;
                return;
            }

            Vector2 input = ReadMoveInput();
            Vector3 moveDir = ToWorldDirection(input);

            // 完全ロック中は向き変えもしない
            if (MoveLock == MovementLock.Full) moveDir = Vector3.zero;

            if (moveDir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeedDeg * Time.deltaTime);
            }

            // 接地中に重力を溜め続けると坂道で跳ねるため、接地したら一定値に戻す
            if (_controller.isGrounded && _verticalVelocity < 0.0f)
            {
                _verticalVelocity = GROUNDED_PULL;
            }
            else
            {
                _verticalVelocity += _gravity * Time.deltaTime;
            }

            float speed = _moveSpeed * (IsSprinting() ? _sprintMultiplier : 1.0f);

            // 向きだけ変えられるロック中は、回転はさせつつ水平移動を止める
            Vector3 horizontal = MoveLock == MovementLock.None ? moveDir * speed : Vector3.zero;
            Vector3 velocity = horizontal + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);

            CurrentSpeed = new Vector3(velocity.x, 0.0f, velocity.z).magnitude;
        }

        /// <summary>向きを変える速さ(度/秒)。狙いを自前で回す側が合わせるために読む</summary>
        public float TurnSpeedDeg => _turnSpeedDeg;

        /// <summary>
        /// いまこの部品が入力で向きを回しているか。
        /// 回していないなら、狙いを付けたい側が自前で角度を持つ必要がある。
        /// </summary>
        public bool RotatesByInput =>
            enabled && (MoveLock == MovementLock.None || MoveLock == MovementLock.RotateOnly);

        /// <summary>入力の向きをワールド座標で返す。入力が無ければ false</summary>
        public bool TryReadMoveDirection(out Vector3 direction)
        {
            direction = ToWorldDirection(ReadMoveInput());

            return direction.sqrMagnitude > 0.0001f;
        }

        /// <summary>
        /// 移動はせず、向きだけ入力に合わせて回す。
        /// とびこみの飛行中はこの部品ごと止められるため、
        /// 狙いを付けたい側から毎フレーム呼んでもらって回転だけを生かす。
        /// </summary>
        public void RotateTowardInput()
        {
            Vector3 moveDir = ToWorldDirection(ReadMoveInput());
            if (moveDir.sqrMagnitude <= 0.0001f) return;

            Quaternion look = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeedDeg * Time.deltaTime);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>WASD とゲームパッド左スティックから移動入力を取る</summary>
        private Vector2 ReadMoveInput()
        {
            // カットシーン中などは操作を受け付けない
            if (!Battle.BattlePlayGate.IsPlayable) return Vector2.zero;

            // 割り当ては表にまとめてある。機器ごとの分岐は要らない
            Vector2 value = Core.GameInput.Move;
            if (value.sqrMagnitude <= STICK_DEAD_ZONE * STICK_DEAD_ZONE) value = Vector2.zero;

            return Vector2.ClampMagnitude(value, 1.0f);
        }

        /// <summary>ダッシュ入力(左Shift / ゲームパッドの左スティック押し込み)</summary>
        private bool IsSprinting()
        {
            if (!Battle.BattlePlayGate.IsPlayable) return false;

            return Core.GameInput.SprintHeld;
        }

        /// <summary>入力をカメラ基準のワールド方向に変換する。カメラが無ければワールド軸をそのまま使う</summary>
        private Vector3 ToWorldDirection(Vector2 input)
        {
            if (_cameraTransform == null) return new Vector3(input.x, 0.0f, input.y);

            Vector3 forward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(_cameraTransform.right, Vector3.up).normalized;
            return forward * input.y + right * input.x;
        }
    }
}
