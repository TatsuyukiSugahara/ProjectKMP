using Photon.Pun;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 自分が持っているキャラのときだけ操作系を有効にする門番。
    /// 他人のキャラは PhotonTransformView が位置を書き込むので、入力処理は止めておく。
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class NetworkOwnerGate : MonoBehaviourPun
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("所有者のときだけ有効にするコンポーネント(移動スクリプトなど)")]
        private Behaviour[] _ownerOnlyBehaviours = new Behaviour[0];

        [SerializeField, Tooltip("所有者のとき、シーンのサードパーソンカメラを自分に向ける")]
        private bool _bindMainCamera = true;

        // ---- 公開API -------------------------------------

        /// <summary>このキャラを自分が操作するかどうか</summary>
        public bool IsOwner => photonView == null || photonView.IsMine;

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            bool isOwner = IsOwner;

            foreach (var behaviour in _ownerOnlyBehaviours)
            {
                if (behaviour != null) behaviour.enabled = isOwner;
            }

            if (!isOwner) return;

            if (_bindMainCamera) BindCamera();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>シーンにあるサードパーソンカメラを自分に追従させる</summary>
        private void BindCamera()
        {
            var camera = FindAnyObjectByType<ThirdPersonCamera>();
            if (camera == null)
            {
                Debug.LogWarning("[Player] ThirdPersonCamera が見つかりません");
                return;
            }

            camera.Target = transform;

            // 移動はカメラ基準なので、カメラ側も教えておく
            var mover = GetComponent<LocalPlayerMover>();
            if (mover != null) mover.SetCamera(camera.transform);
        }
    }
}
