using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 爆発の瞬間に地面を横へ広がる衝撃波リングと光の閃光。
    /// 指定時間でリングが勢いよく広がりながら薄くなり、ライトも減衰して自分で消える。
    /// 見た目専用の演出(全クライアントで再生し、ネットワーク同期は不要)。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class EnergyShockwave : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Min(8), Tooltip("リングの分割数。多いほど滑らか")]
        private int _segments = 64;

        [SerializeField, Min(0.01f), Tooltip("リングの線の太さ(m)")]
        private float _ringThickness = 1.2f;

        [SerializeField, Min(0f), Tooltip("地面から浮かせる高さ(m)")]
        private float _groundOffset = 0.06f;

        [SerializeField, Tooltip("閃光のライト。未設定なら光らない")]
        private Light _flashLight;

        [SerializeField, Min(0f), Tooltip("閃光の最大の明るさ")]
        private float _flashIntensity = 25f;

        // ---- 内部状態 ------------------------------------

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        /// <summary>波が1回来るごとに弱くなる割合</summary>
        private const float WAVE_STRENGTH_DECAY = 0.72f;

        private MeshFilter _meshFilter;
        private Renderer _meshRenderer;
        private Mesh _mesh;
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor = Color.white;

        private float _startRadius = 1f;
        private float _endRadius = 10f;
        private float _durationSec = 0.4f;
        private float _elapsedSec;

        private bool _flattenGrass;
        private float _previousRadius;

        /// <summary>あと何回この輪を出すか</summary>
        private int _waveRemaining = 1;
        private float _waveIntervalSec;

        /// <summary>波が来るたびに弱くしていく倍率。減衰して収まっていくように見せる</summary>
        private float _waveStrength = 1f;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 衝撃波を生成する。startRadius から endRadius まで duration 秒で変化する。
        /// endRadius を startRadius より小さくすれば、外から内へ縮む収束リングになる。
        /// thickness に正の値を渡すと、プレハブの線の太さを上書きする。
        /// flattenGrass を true にすると、輪が通り過ぎた場所の草をなぎ倒していく。
        /// waveCount を2以上にすると、同じ輪を waveIntervalSec 間隔で繰り返し出す(草がなびいて見える)。
        /// </summary>
        public static EnergyShockwave Spawn(
            EnergyShockwave prefab, Vector3 position, float startRadius, float endRadius, float duration,
            float thickness = 0f, bool flattenGrass = false, int waveCount = 1, float waveIntervalSec = 0.2f)
        {
            if (prefab == null) return null;

            EnergyShockwave instance = Instantiate(prefab, position, Quaternion.identity);
            instance._startRadius = Mathf.Max(0.1f, startRadius);
            instance._endRadius = Mathf.Max(0.1f, endRadius);
            instance._durationSec = Mathf.Max(0.05f, duration);
            instance._flattenGrass = flattenGrass;
            instance._previousRadius = instance._startRadius;
            instance._waveRemaining = Mathf.Max(1, waveCount);
            instance._waveIntervalSec = Mathf.Max(0f, waveIntervalSec);
            if (thickness > 0f) instance._ringThickness = thickness;
            return instance;
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _propertyBlock = new MaterialPropertyBlock();

            _mesh = new Mesh { name = "EnergyShockwave" };
            _meshFilter.mesh = _mesh;

            if (_meshRenderer.sharedMaterial != null && _meshRenderer.sharedMaterial.HasProperty(BASE_COLOR_ID))
            {
                _baseColor = _meshRenderer.sharedMaterial.GetColor(BASE_COLOR_ID);
            }
        }

        private void Update()
        {
            _elapsedSec += Time.deltaTime;

            // 次の波が出るまでの待ち時間。この間は輪も光も消しておく
            if (_elapsedSec < 0f)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            float t = Mathf.Clamp01(_elapsedSec / _durationSec);

            // 勢いよく広がって減速する(イーズアウト)
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(_startRadius, _endRadius, eased);
            BuildRing(radius);

            // 前のフレームからの差ぶんだけを倒す。内側は倒し済みなので調べ直さない
            if (_flattenGrass)
            {
                Field.GrassField.FlattenRingAt(
                    transform.position,
                    Mathf.Min(_previousRadius, radius),
                    Mathf.Max(_previousRadius, radius),
                    _waveStrength);
                _previousRadius = radius;
            }

            // 広がりながら薄くなる
            float alpha = (1f - t) * _waveStrength;
            Color color = _baseColor;
            color.a *= alpha;
            _meshRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BASE_COLOR_ID, color);
            _meshRenderer.SetPropertyBlock(_propertyBlock);

            if (_flashLight != null) _flashLight.intensity = _flashIntensity * alpha;

            if (t < 1f) return;

            if (_waveRemaining > 1)
            {
                // 次の波を最初の半径からやり直す。弱くしていくことで収まっていくように見せる
                _waveRemaining--;
                _elapsedSec = -_waveIntervalSec;
                _previousRadius = _startRadius;
                _waveStrength *= WAVE_STRENGTH_DECAY;
                return;
            }

            Destroy(gameObject);
        }

        /// <summary>波と波の間、輪を消しておく</summary>
        private void SetVisible(bool visible)
        {
            if (_meshRenderer != null) _meshRenderer.enabled = visible;
            if (!visible && _flashLight != null) _flashLight.intensity = 0f;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>指定半径のリングメッシュを作り直す</summary>
        private void BuildRing(float radius)
        {
            var vertices = new Vector3[_segments * 2];
            var triangles = new int[_segments * 6];
            float inner = Mathf.Max(0.01f, radius - _ringThickness);

            for (int i = 0; i < _segments; i++)
            {
                float angle = i / (float)_segments * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = dir * inner + Vector3.up * _groundOffset;
                vertices[i * 2 + 1] = dir * radius + Vector3.up * _groundOffset;

                int next = (i + 1) % _segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = next * 2;
                triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = i * 2 + 1;
                triangles[t + 4] = next * 2;
                triangles[t + 5] = next * 2 + 1;
            }

            _mesh.Clear();
            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.RecalculateNormals();
        }
    }
}
