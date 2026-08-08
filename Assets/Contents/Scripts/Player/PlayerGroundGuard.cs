using Photon.Pun;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 何らかの理由で床をすり抜けてしまったときに、プレイヤーを地面の上へ戻す保険。
    /// ボスのスタンプを真下で受けたときのように、めり込みの押し出しで地面の下へ潜っても
    /// そのまま落ち続けないようにする。位置を直すのは自分のキャラのクライアントだけで、
    /// 他クライアントへは PhotonTransformView の位置同期で伝わる。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerGroundGuard : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Min(0.5f), Tooltip("直近に接地していた高さからこれ以上下がったら、床をすり抜けたとみなす(メートル)")]
        private float _fallThresholdMeters = 3.0f;

        [SerializeField, Tooltip("接地位置が分からない場合でも、この高さより下に落ちたら無条件に引き戻す(メートル)")]
        private float _absoluteFloorY = -20.0f;

        [SerializeField, Tooltip("戻り先の地面を探すレイヤー")]
        private LayerMask _groundLayers = ~0;

        [SerializeField, Min(1.0f), Tooltip("地面を探すレイを飛ばす高さ(メートル)")]
        private float _probeHeightMeters = 50.0f;

        [SerializeField, Min(0.0f), Tooltip("地面へ戻すときに浮かせる高さ(メートル)")]
        private float _recoverLiftMeters = 0.5f;

        // ---- 内部状態 ------------------------------------

        private CharacterController _controller;
        private PlayerHealth _health;
        private Vector3 _lastGroundedPosition;

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<PlayerHealth>();
            _lastGroundedPosition = transform.position;
        }

        private void Start()
        {
            // 他人のキャラは位置同期で動くので、こちらで直す必要はない
            PhotonView view = GetComponent<PhotonView>();
            if (view != null && !view.IsMine) enabled = false;
        }

        /// <summary>移動やスキルが座標を動かしきったあとに調べたいので LateUpdate で見る</summary>
        private void LateUpdate()
        {
            // 死亡中は当たり判定を切って大きく吹き飛んでいる最中なので、位置はリスポーンに任せる
            if (_health != null && _health.IsDead) return;

            if (_controller.enabled && _controller.isGrounded)
            {
                _lastGroundedPosition = transform.position;
                return;
            }

            if (!IsFallenThrough()) return;

            Recover();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>スキルの跳び上がりは上方向なので、下向きのズレだけを見れば取り違えない</summary>
        private bool IsFallenThrough()
        {
            float y = transform.position.y;
            if (y < _absoluteFloorY) return true;
            return y < _lastGroundedPosition.y - _fallThresholdMeters;
        }

        /// <summary>真上から地面を探して立たせ直す。見つからなければ最後に接地していた位置へ戻す</summary>
        private void Recover()
        {
            // CharacterController が有効なままだと位置を代入しても戻されるので、一度切る。
            // 切っておくことで、地面を探すレイが自分自身に当たるのも防げる
            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;

            Vector3 target = _lastGroundedPosition;

            Vector3 origin = transform.position + Vector3.up * _probeHeightMeters;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                    _probeHeightMeters * 2.0f, _groundLayers, QueryTriggerInteraction.Ignore))
            {
                target = hit.point;
            }

            target.y += _recoverLiftMeters;
            transform.position = target;

            _controller.enabled = wasEnabled;
            _lastGroundedPosition = target;

            Debug.Log($"[PlayerGroundGuard] 床下に落ちたため {target} へ戻しました", this);
        }
    }
}
