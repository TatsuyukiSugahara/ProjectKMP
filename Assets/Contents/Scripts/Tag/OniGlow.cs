using UnityEngine;

namespace ProjectKMP.Tag
{
    /// <summary>
    /// 鬼のキャラ本体を赤く光らせる。頭上マーカーが隠れる角度でも鬼が分かるようにするための表示。
    /// </summary>
    public class OniGlow : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------
        private static readonly int EMISSION_COLOR_ID = Shader.PropertyToID("_EmissionColor");

        // ---- 参照 ----------------------------------------
        [SerializeField] private Renderer[] _targetRenderers;

        // ---- 調整パラメータ ------------------------------
        [SerializeField] private Color _glowColor     = new Color(1.0f, 0.15f, 0.12f);
        [SerializeField] private float _minIntensity  = 0.9f;
        [SerializeField] private float _maxIntensity  = 2.4f;
        [SerializeField] private float _pulseSpeed    = 4.0f;

        // ---- 内部状態 ------------------------------------
        private MaterialPropertyBlock _block;
        private bool _isGlowing;

        // ---- 公開API -------------------------------------

        /// <summary>光らせるかどうかを切り替える</summary>
        public void SetGlow(bool isGlowing)
        {
            _isGlowing = isGlowing;

            // 消すときは一度だけ黒を書き込めばよい
            if (!_isGlowing) Apply(Color.black);
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _block = new MaterialPropertyBlock();

            if (_targetRenderers == null || _targetRenderers.Length == 0)
            {
                _targetRenderers = GetComponentsInChildren<MeshRenderer>(true);
            }

            Apply(Color.black);
        }

        private void Update()
        {
            if (!_isGlowing) return;

            // 明滅させると、ただ赤いキャラではなく光っていることが伝わる
            float wave = (Mathf.Sin(Time.time * _pulseSpeed) + 1.0f) * 0.5f;
            float intensity = Mathf.Lerp(_minIntensity, _maxIntensity, wave);
            Apply(_glowColor * intensity);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>PlayerVisual が入れた色を消さないよう、既存の値を読んでから発光色だけ差し替える</summary>
        private void Apply(Color emission)
        {
            if (_targetRenderers == null) return;

            foreach (Renderer target in _targetRenderers)
            {
                if (target == null) continue;

                target.GetPropertyBlock(_block);
                _block.SetColor(EMISSION_COLOR_ID, emission);
                target.SetPropertyBlock(_block);
            }
        }
    }
}
