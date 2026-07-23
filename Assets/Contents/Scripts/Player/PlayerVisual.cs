using Photon.Pun;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// プレイヤーの見た目を ActorNumber ごとに色分けする。自分のキャラだけ明るく表示する。
    /// </summary>
    public class PlayerVisual : MonoBehaviourPun
    {
        // ---- 定数 ----------------------------------------
        private static readonly Color[] ACTOR_COLORS =
        {
            new Color(0.20f, 0.55f, 1.00f),
            new Color(1.00f, 0.35f, 0.30f),
            new Color(0.25f, 0.85f, 0.45f),
            new Color(1.00f, 0.80f, 0.20f),
        };

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor"); // URP
        private static readonly int COLOR_ID      = Shader.PropertyToID("_Color");     // Built-in 互換

        // ---- 参照 ----------------------------------------
        [SerializeField] private Renderer _targetRenderer;

        private void Start()
        {
            if (_targetRenderer == null) _targetRenderer = GetComponentInChildren<Renderer>();
            if (_targetRenderer == null) return;

            int index = 0;
            if (photonView.Owner != null)
            {
                index = Mathf.Abs(photonView.Owner.ActorNumber - 1) % ACTOR_COLORS.Length;
            }

            Color color = ACTOR_COLORS[index];
            if (!photonView.IsMine) color *= 0.5f; // 自分と他人を一目で区別できるようにする
            color.a = 1.0f;

            // マテリアルを複製しないよう PropertyBlock で色を差し替える
            var block = new MaterialPropertyBlock();
            _targetRenderer.GetPropertyBlock(block);
            block.SetColor(BASE_COLOR_ID, color);
            block.SetColor(COLOR_ID, color);
            _targetRenderer.SetPropertyBlock(block);
        }
    }
}
