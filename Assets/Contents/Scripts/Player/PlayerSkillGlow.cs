using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// スキルの溜め中などにキャラ本体を光らせる。
    /// マテリアルを複製せず MaterialPropertyBlock で発光色だけを差し替える(鬼ごっこの OniGlow と同じ方式)。
    /// </summary>
    public class PlayerSkillGlow : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private static readonly int EMISSION_COLOR_ID = Shader.PropertyToID("_EmissionColor");

        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("光らせるレンダラー。未設定なら子の SkinnedMeshRenderer を自動で拾う")]
        private Renderer[] _targetRenderers;

        [SerializeField, Tooltip("発光色")]
        private Color _glowColor = new Color(0.3f, 0.8f, 1f);

        [SerializeField, Min(0f), Tooltip("最も暗いときの明るさ")]
        private float _minIntensity = 0.4f;

        [SerializeField, Min(0f), Tooltip("最も明るいときの明るさ")]
        private float _maxIntensity = 2.6f;

        [SerializeField, Tooltip("明滅の速さ")]
        private float _pulseSpeed = 8f;

        [SerializeField, Min(0.01f), Tooltip("点灯・消灯にかける時間(秒)")]
        private float _blendSec = 0.15f;

        // ---- 内部状態 ------------------------------------

        private MaterialPropertyBlock _block;
        private bool _isGlowing;
        private float _weight;

        // ---- 公開API -------------------------------------

        /// <summary>光らせるかどうかを切り替える。点灯・消灯は少しずつ行われる</summary>
        public void SetGlow(bool isGlowing)
        {
            _isGlowing = isGlowing;
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _block = new MaterialPropertyBlock();

            if (_targetRenderers == null || _targetRenderers.Length == 0)
            {
                _targetRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            }

            Apply(Color.black);
        }

        private void Update()
        {
            float target = _isGlowing ? 1f : 0f;

            // 消えきっているときは毎フレーム書き込まない
            if (_weight <= 0f && target <= 0f) return;

            _weight = Mathf.MoveTowards(_weight, target, Time.deltaTime / _blendSec);

            // 明滅させると「色が変わった」ではなく「光っている」と伝わる
            float wave = (Mathf.Sin(Time.time * _pulseSpeed) + 1f) * 0.5f;
            float intensity = Mathf.Lerp(_minIntensity, _maxIntensity, wave) * _weight;
            Apply(_glowColor * intensity);
        }

        // ---- 内部処理 ------------------------------------

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
