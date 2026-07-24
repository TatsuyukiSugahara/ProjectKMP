using Photon.Pun;
using ProjectKMP.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.Player
{
    /// <summary>
    /// プレイヤーの移動制御。所有者(photonView.IsMine)のクライアントだけが入力を受け付ける。
    /// 他クライアントへの位置・回転の送信は PhotonTransformView が担当する。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMover : MonoBehaviourPun
    {
        // ---- 定数 ----------------------------------------
        private const float STICK_DEAD_ZONE = 0.2f;
        private const float GROUNDED_PULL   = -2.0f; // 接地判定を安定させるための下向き速度

        // ---- 調整パラメータ ------------------------------
        [SerializeField] private float _moveSpeed    = 5.0f;
        [SerializeField] private float _turnSpeedDeg = 720.0f;
        [SerializeField] private float _gravity      = -20.0f;

        // ---- 内部状態 ------------------------------------
        private CharacterController _controller;
        private Transform _cameraTransform;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void Start()
        {
            // 他人のキャラで入力処理を回す意味は無いので、所有者以外は自分を無効化する
            if (!photonView.IsMine)
            {
                enabled = false;
                return;
            }

            if (Camera.main != null) _cameraTransform = Camera.main.transform;
        }

        private void Update()
        {
            Vector3 moveDir = ToWorldDirection(ReadMoveInput());

            if (moveDir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, _turnSpeedDeg * Time.deltaTime);
            }

            if (_controller.isGrounded && _verticalVelocity < 0.0f)
            {
                _verticalVelocity = GROUNDED_PULL;
            }
            else
            {
                _verticalVelocity += _gravity * Time.deltaTime;
            }

            Vector3 velocity = moveDir * _moveSpeed + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>キーボード(WASD/矢印)・ゲームパッド左スティック・仮想スティックを合成して取得する</summary>
        private Vector2 ReadMoveInput()
        {
            Vector2 value = Vector2.zero;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)    value.y += 1.0f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)  value.y -= 1.0f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) value.x += 1.0f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)  value.x -= 1.0f;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.leftStick.ReadValue();
                if (stick.sqrMagnitude > STICK_DEAD_ZONE * STICK_DEAD_ZONE) value += stick;
            }

            // タッチ端末では画面上の仮想スティックからも受け取る(未配置なら null が返る)
            VirtualStick virtualStick = ServiceLocator.TryGet<VirtualStick>();
            if (virtualStick != null)
            {
                Vector2 touch = virtualStick.Value;
                if (touch.sqrMagnitude > STICK_DEAD_ZONE * STICK_DEAD_ZONE) value += touch;
            }

            return Vector2.ClampMagnitude(value, 1.0f);
        }

        /// <summary>入力をカメラ基準のワールド方向に変換する。カメラが無ければワールド軸をそのまま使う</summary>
        private Vector3 ToWorldDirection(Vector2 input)
        {
            if (_cameraTransform == null) return new Vector3(input.x, 0.0f, input.y);

            Vector3 forward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
            Vector3 right   = Vector3.ProjectOnPlane(_cameraTransform.right,   Vector3.up).normalized;
            return forward * input.y + right * input.x;
        }
    }
}
