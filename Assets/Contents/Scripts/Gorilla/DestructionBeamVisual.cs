using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 破壊光線の見た目を、始点・方向・長さ・太さに合わせて調整する。
    /// 外側の柔らかいグロー(Glow)と、内側の明るい芯(Core)の2枚のLineRendererを重ねて
    /// ビームらしい厚みを出す。テクスチャをUVスクロールさせてエネルギーが流れる演出にする。
    /// 発射終了時は FadeOut() を呼ぶことで、パッと消えず徐々に透明になってから自分で消える。
    /// </summary>
    public class DestructionBeamVisual : MonoBehaviour
    {
        [SerializeField, Tooltip("外側の柔らかいグロー部分")]
        private LineRenderer _glowLine;

        [SerializeField, Tooltip("内側の明るい芯の部分")]
        private LineRenderer _coreLine;

        [SerializeField, Tooltip("芯の太さをグローの太さに対してどれくらいの割合にするか")]
        private float _coreWidthRatio = 0.4f;

        [SerializeField, Tooltip("テクスチャが流れる速さ(UVスクロール)")]
        private float _scrollSpeed = 3.0f;

        [SerializeField, Tooltip("光線がゆらめく速さ")]
        private float _pulseSpeed = 14f;

        [SerializeField, Tooltip("光線の太さの揺れ幅(太さに対する割合)")]
        private float _pulseAmount = 0.12f;

        [Header("パーティクル(任意)")]
        [SerializeField, Tooltip("光線に沿ってきらきら飛び散るパーティクル。発生範囲と量が光線の長さに合わせて自動で伸びる。未設定でもよい")]
        private ParticleSystem[] _alongBeamParticles;

        [SerializeField, Min(0f), Tooltip("光線1mあたりの粒の発生量(毎秒)。沿いパーティクルにのみ使う")]
        private float _sparkleRatePerMeter = 8f;

        [SerializeField, Tooltip("光線の先端に追従するパーティクル(先端の光球など)。未設定でもよい")]
        private ParticleSystem[] _tipParticles;

        private float _baseGlowWidth = 1f;
        private float _baseCoreWidth = 1f;
        private Material _glowMaterialInstance;
        private Material _coreMaterialInstance;

        private Color _glowStartColor;
        private Color _glowEndColor;
        private Color _coreStartColor;
        private Color _coreEndColor;

        private bool _isFadingOut;
        private float _fadeElapsed;
        private float _fadeDuration;

        private void Awake()
        {
            if (_glowLine != null)
            {
                _glowMaterialInstance = _glowLine.material;
                _glowStartColor = _glowLine.startColor;
                _glowEndColor = _glowLine.endColor;
            }

            if (_coreLine != null)
            {
                _coreMaterialInstance = _coreLine.material;
                _coreStartColor = _coreLine.startColor;
                _coreEndColor = _coreLine.endColor;
            }
        }

        /// <summary>始点・方向(正規化不要)・長さ・太さ(半径)を指定して光線の見た目を配置する</summary>
        public void Configure(Vector3 origin, Vector3 direction, float length, float radius)
        {
            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Vector3 end = origin + dir * length;

            _baseGlowWidth = Mathf.Max(0.01f, radius * 2f);
            _baseCoreWidth = Mathf.Max(0.01f, _baseGlowWidth * _coreWidthRatio);

            SetLine(_glowLine, origin, end, _baseGlowWidth, _baseGlowWidth * 0.7f);
            SetLine(_coreLine, origin, end, _baseCoreWidth, _baseCoreWidth * 0.7f);

            UpdateParticles(end, length, radius);
        }

        /// <summary>
        /// パーティクルを光線の今の長さに合わせる。
        /// 沿いパーティクルは発生範囲(Box)を根元から先端まで引き伸ばし、量も長さに比例させる。
        /// 先端パーティクルは先端の位置へ動かす。
        /// </summary>
        private void UpdateParticles(Vector3 end, float length, float radius)
        {
            if (_alongBeamParticles != null)
            {
                foreach (var ps in _alongBeamParticles)
                {
                    if (ps == null) continue;

                    var shape = ps.shape;
                    shape.scale = new Vector3(radius * 2f, radius * 2f, Mathf.Max(0.01f, length));
                    shape.position = new Vector3(0f, 0f, length * 0.5f);

                    var emission = ps.emission;
                    emission.rateOverTimeMultiplier = _sparkleRatePerMeter * length;
                }
            }

            if (_tipParticles != null)
            {
                foreach (var ps in _tipParticles)
                {
                    if (ps == null) continue;
                    ps.transform.position = end;
                }
            }
        }

        /// <summary>徐々に透明にしてから自分自身を破棄する(パッと消えないようにする)</summary>
        public void FadeOut(float duration)
        {
            if (_isFadingOut) return;

            _isFadingOut = true;
            _fadeElapsed = 0f;
            _fadeDuration = Mathf.Max(0.01f, duration);

            // 新しい粒子は出さず、すでに出ている粒子だけ自然に消えるようにする
            var particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in particleSystems)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void SetLine(LineRenderer line, Vector3 start, Vector3 end, float startWidth, float endWidth)
        {
            if (line == null) return;

            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = startWidth;
            line.endWidth = endWidth;
        }

        private void Update()
        {
            if (_isFadingOut)
            {
                UpdateFadeOut();
                return;
            }

            // 発射中、少し脈打つように太さを揺らして電力っぽさを出す
            float pulse = 1f + Mathf.Sin(Time.time * _pulseSpeed) * _pulseAmount;

            if (_glowLine != null)
            {
                _glowLine.startWidth = _baseGlowWidth * pulse;
                _glowLine.endWidth = _baseGlowWidth * 0.7f * pulse;
            }

            if (_coreLine != null)
            {
                _coreLine.startWidth = _baseCoreWidth * pulse;
                _coreLine.endWidth = _baseCoreWidth * 0.7f * pulse;
            }

            // テクスチャを流してエネルギーが噴き出しているように見せる
            float scroll = Time.time * _scrollSpeed;
            if (_glowMaterialInstance != null) _glowMaterialInstance.mainTextureOffset = new Vector2(-scroll, 0f);
            if (_coreMaterialInstance != null) _coreMaterialInstance.mainTextureOffset = new Vector2(-scroll * 1.6f, 0f);
        }

        private void UpdateFadeOut()
        {
            _fadeElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_fadeElapsed / _fadeDuration);

            // 直線的にではなく、じわっと(最初はゆっくり、後半で消えきる)透明になるようにイーズをかける
            float eased = t * t * (3f - 2f * t); // smoothstep
            float alphaMul = 1f - eased;

            // 太さは0まで縮め切る(途中で破棄すると最後がプツッと切れて見えるため)
            float widthMul = 1f - eased;

            if (_glowLine != null)
            {
                _glowLine.startColor = MultiplyAlpha(_glowStartColor, alphaMul);
                _glowLine.endColor = MultiplyAlpha(_glowEndColor, alphaMul);
                _glowLine.startWidth = _baseGlowWidth * widthMul;
                _glowLine.endWidth = _baseGlowWidth * 0.7f * widthMul;
            }

            if (_coreLine != null)
            {
                _coreLine.startColor = MultiplyAlpha(_coreStartColor, alphaMul);
                _coreLine.endColor = MultiplyAlpha(_coreEndColor, alphaMul);
                _coreLine.startWidth = _baseCoreWidth * widthMul;
                _coreLine.endWidth = _baseCoreWidth * 0.7f * widthMul;
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }

        private static Color MultiplyAlpha(Color color, float mul)
        {
            color.a *= mul;
            return color;
        }

        private void OnDestroy()
        {
            if (_glowMaterialInstance != null) Destroy(_glowMaterialInstance);
            if (_coreMaterialInstance != null) Destroy(_coreMaterialInstance);
        }
    }
}
