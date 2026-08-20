using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// パーティクルを使わず、メッシュ+加算シェーダーの色だけで表現するシンプルなオーラエフェクト。
    /// 生成直後に短時間でフェードインし、以降はゆっくり明滅(パルス)しながら自転し続ける。
    /// 破棄タイミングは呼び出し側(GorillaStateSweepAttackなど)がDestroy()で管理する想定で、
    /// 自身では時間経過による自動破棄は行わない。
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class SimpleAuraEffect : MonoBehaviour
    {
        [SerializeField, Tooltip("フェードイン(透明→最大アルファ)にかける時間(秒)")]
        private float _fadeInDurationSec = 0.15f;

        [SerializeField, Range(0f, 1f), Tooltip("フェードイン後、明滅の基準となる最大アルファ値")]
        private float _maxAlpha = 0.5f;

        [SerializeField, Tooltip("明滅(パルス)の周期(秒)")]
        private float _pulseCycleSec = 0.6f;

        [SerializeField, Range(0f, 1f), Tooltip("明滅で変動するアルファの振れ幅(最大アルファに対する割合)")]
        private float _pulseAmplitudeRatio = 0.35f;

        [SerializeField, Tooltip("パルスに合わせて拡大縮小する度合い(基準スケールに対する割合)")]
        private float _pulseScaleAmplitudeRatio = 0.08f;

        [SerializeField, Tooltip("Y軸まわりの自転速度(度/秒)")]
        private float _spinSpeedDeg = 90.0f;

        private Renderer _renderer;
        private MaterialPropertyBlock _propertyBlock;
        private Vector3 _baseScale;
        private float _elapsedTime;
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
            _baseScale = transform.localScale;
        }

        private void Update()
        {
            _elapsedTime += Time.deltaTime;

            // フェードイン
            float fadeT = _fadeInDurationSec > 0f ? Mathf.Clamp01(_elapsedTime / _fadeInDurationSec) : 1f;

            // 明滅(サイン波でゆっくり呼吸するように)
            float pulsePhase = _pulseCycleSec > 0f ? (_elapsedTime / _pulseCycleSec) * Mathf.PI * 2f : 0f;
            float pulse = Mathf.Sin(pulsePhase) * 0.5f + 0.5f; // 0〜1

            float alpha = _maxAlpha * fadeT * (1f - _pulseAmplitudeRatio + _pulseAmplitudeRatio * pulse);

            float scaleMul = 1f + _pulseScaleAmplitudeRatio * (pulse * 2f - 1f);
            transform.localScale = _baseScale * scaleMul;

            transform.Rotate(Vector3.up, _spinSpeedDeg * Time.deltaTime, Space.Self);

            if (_renderer != null)
            {
                _renderer.GetPropertyBlock(_propertyBlock);
                Color baseColor = _renderer.sharedMaterial.GetColor(TintColorId);
                baseColor.a = alpha;
                _propertyBlock.SetColor(TintColorId, baseColor);
                _renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        /// <summary>基準スケール(パルスの基準となるスケール)を後から設定し直す。生成直後に呼ぶ想定</summary>
        public void SetBaseScale(Vector3 scale)
        {
            _baseScale = scale;
            transform.localScale = scale;
        }
    }
}
