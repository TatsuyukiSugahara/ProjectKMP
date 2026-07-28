using Photon.Pun;
using UnityEngine;

namespace ProjectKMP.Sandbox
{
    /// <summary>
    /// ModelCheck シーンを単体再生したときに Photon をオフラインモードで起動する。
    /// Title シーンの NetworkManager を経由しないため、PlayerMover 等の photonView.IsMine 判定を
    /// 通すための _Sandbox 専用の簡易初期化。
    /// PhotonView.Awake()(実行順 -16000)が ViewID の所有者解決を行うより前に、ここでルームへの
    /// 参加を終わらせておく必要があるため、この .cs.meta の executionOrder を -32000 にしてある。
    /// </summary>
    public class ModelCheckOfflineBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (PhotonNetwork.InRoom) return;

            PhotonNetwork.OfflineMode = true;
            PhotonNetwork.CreateRoom("ModelCheckOffline");
        }
    }
}
