using Photon.Pun;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// プレイヤーの見た目を管理する。
    /// ActorNumberごとの色分けは廃止し、Huskyの元テクスチャ色をそのまま表示する。
    /// </summary>
    public class PlayerVisual : MonoBehaviourPun
    {
        // ---- 参照 ----------------------------------------
        [SerializeField] private Renderer _targetRenderer;

        private void Start()
        {
            if (_targetRenderer == null) _targetRenderer = GetComponentInChildren<Renderer>();
            // 色分け機能は廃止。元のマテリアル(テクスチャ)色をそのまま使用するため、
            // MaterialPropertyBlockへの色設定は行わない。
        }
    }
}
