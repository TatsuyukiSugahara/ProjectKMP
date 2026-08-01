using Photon.Pun;
using R3;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 自分が持っているキャラのときだけ操作系を有効にする門番。
    /// 他人のキャラは PhotonTransformView が位置を書き込むので、入力処理は止めておく。
    /// あわせて、操作可能になった瞬間(ゲーム開始時、カットシーン終了時・スキップ時)には
    /// シーンのボス(敵)を画面に捉えた向きへカメラを合わせる。
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class NetworkOwnerGate : MonoBehaviourPun
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("所有者のときだけ有効にするコンポーネント(移動スクリプトなど)")]
        private Behaviour[] _ownerOnlyBehaviours = new Behaviour[0];

        [SerializeField, Tooltip("所有者のとき、シーンのサードパーソンカメラを自分に向ける")]
        private bool _bindMainCamera = true;

        // ---- 内部状態 ------------------------------------

        private ThirdPersonCamera _camera;
        private System.IDisposable _playableSubscription;

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

        private void OnDestroy()
        {
            _playableSubscription?.Dispose();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>シーンにあるサードパーソンカメラを自分に追従させる</summary>
        private void BindCamera()
        {
            _camera = FindAnyObjectByType<ThirdPersonCamera>();
            if (_camera == null)
            {
                Debug.LogWarning("[Player] ThirdPersonCamera が見つかりません");
                return;
            }

            _camera.Target = transform;

            // 移動はカメラ基準なので、カメラ側も教えておく
            var mover = GetComponent<LocalPlayerMover>();
            if (mover != null) mover.SetCamera(_camera.transform);

            // ボス(敵)を画面に捉える向き合わせは、操作可能になった瞬間に行う。
            // カットシーン中はボス自身が移動してくるため、スポーン時に合わせると
            // スキップや演出終了後の最終位置とズレてしまう。
            // 購読時に現在値も流れるので、カットシーンの無いシーンでは即座に向く
            _playableSubscription = Battle.BattlePlayGate.OnChanged.Subscribe(playable =>
            {
                if (!playable) return;
                AimCameraAtBoss();
            });
        }

        /// <summary>シーンにボス(BossHealth)が居れば、カメラをその方向へ向ける。居ないシーンでは何もしない</summary>
        private void AimCameraAtBoss()
        {
            if (_camera == null) return;

            var boss = FindAnyObjectByType<ProjectKMP.Monster.BossHealth>();
            if (boss == null) return;

            _camera.AimAt(boss.transform.position);
        }
    }
}
