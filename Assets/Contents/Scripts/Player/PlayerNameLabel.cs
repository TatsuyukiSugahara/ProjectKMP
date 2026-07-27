using Photon.Pun;
using TMPro;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// キャラの頭の上に表示する名前。つねにカメラのほうを向く。
    /// シングルプレイ（オフラインモード）では表示しない。
    /// </summary>
    public class PlayerNameLabel : MonoBehaviourPun
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("名前を出すテキスト")]
        private TMP_Text _text;

        [SerializeField, Tooltip("カメラのほうへ向ける対象。未設定ならこのオブジェクト")]
        private Transform _billboardRoot;

        [SerializeField, Tooltip("名前が取れないときに使う名前")]
        private string _fallbackName = "ななしさん";

        [SerializeField, Tooltip("自分の名前の色")]
        private Color _ownColor = new Color(1.0f, 0.9f, 0.45f, 1.0f);

        [SerializeField, Tooltip("他の人の名前の色")]
        private Color _otherColor = Color.white;

        [SerializeField, Tooltip("シングルプレイ（オフライン）のときは名前を出さない")]
        private bool _hideOnSinglePlay = true;

        // ---- 内部状態 ------------------------------------

        private Transform _cameraTransform;

        // ---- 公開API -------------------------------------

        /// <summary>シングルプレイ（サーバーを使わない状態）かどうか</summary>
        public static bool IsSinglePlay => PhotonNetwork.OfflineMode || !PhotonNetwork.IsConnected;

        /// <summary>表示する名前を直接設定する</summary>
        public void SetName(string playerName)
        {
            if (_text != null) _text.text = string.IsNullOrWhiteSpace(playerName) ? _fallbackName : playerName;
        }

        /// <summary>名前ラベルの表示・非表示を切り替える</summary>
        public void SetVisible(bool visible)
        {
            Transform root = _billboardRoot != null ? _billboardRoot : transform;

            // 自分自身を消してしまうと復帰できないので、その場合はテキストだけ切り替える
            if (root == transform)
            {
                if (_text != null) _text.gameObject.SetActive(visible);
            }
            else
            {
                root.gameObject.SetActive(visible);
            }

            enabled = visible;
        }

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            if (_billboardRoot == null) _billboardRoot = transform;

            if (_hideOnSinglePlay && IsSinglePlay)
            {
                SetVisible(false);
                Debug.Log("[Player] シングルプレイのため名前ラベルを非表示にしました");
                return;
            }

            bool isMine = photonView == null || photonView.IsMine;

            string playerName = null;
            if (photonView != null && photonView.Owner != null) playerName = photonView.Owner.NickName;
            if (string.IsNullOrWhiteSpace(playerName)) playerName = PhotonNetwork.NickName;

            SetName(playerName);

            if (_text != null) _text.color = isMine ? _ownColor : _otherColor;
        }

        private void LateUpdate()
        {
            if (_billboardRoot == null) return;

            if (_cameraTransform == null)
            {
                Camera camera = Camera.main;
                if (camera == null) return;
                _cameraTransform = camera.transform;
            }

            // カメラと同じ向きに揃えると、裏返らずに常に読める
            _billboardRoot.rotation = _cameraTransform.rotation;
        }
    }
}
