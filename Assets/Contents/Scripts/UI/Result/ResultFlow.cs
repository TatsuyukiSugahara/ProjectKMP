using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectKMP.UI.Result
{
    /// <summary>
    /// リザルト画面の進行。右下のボタンか、ゲームパッドのBボタン(東ボタン)でタイトルへ戻る。
    /// 戻るときはルームから退出してからタイトルへ遷移する(各プレイヤーが自分のタイミングで戻れる)。
    /// </summary>
    public class ResultFlow : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("タイトルへ戻るボタン")]
        private Button _titleButton;

        [SerializeField, Tooltip("戻り先のタイトルシーン名")]
        private string _titleSceneName = "Title";

        // ---- 内部状態 ------------------------------------

        private bool _isLeaving;

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            if (_titleButton != null) _titleButton.onClick.AddListener(ReturnToTitle);
        }

        private void Update()
        {
            // ゲームパッドのBボタン(東ボタン)でも戻れるようにする
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame)
            {
                ReturnToTitle();
            }
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>ルームから抜けてタイトルへ戻る。二重実行は防ぐ</summary>
        private void ReturnToTitle()
        {
            if (_isLeaving) return;
            _isLeaving = true;

            if (_titleButton != null) _titleButton.interactable = false;
            LeaveAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid LeaveAsync(CancellationToken ct)
        {
            NetworkManager network = NetworkManager.Instance;
            if (network != null && PhotonNetwork.InRoom)
            {
                await network.LeaveRoomAsync(ct);
            }

            Debug.Log("[Result] タイトルへ戻ります");

            SceneLoader loader = SceneLoader.Instance;
            if (loader != null)
            {
                await loader.LoadSceneAsync(_titleSceneName, ct);
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(_titleSceneName);
            }
        }
    }
}
