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

        private MeshFilter _meshFilter;
        private Renderer _meshRenderer;
        private Mesh _mesh;
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor = Color.white;

        private float _startRadius = 1f;
        private float _endRadius = 10f;
        private float _durationSec = 0.4f;
        private float _elapsedSec;

        // ---- 公開API -------------------------------------

        /// <summary>衝撃波を生成する。startRadius から endRadius まで duration 秒で広がる</summary>
        public static EnergyShockwave Spawn(
            EnergyShockwave prefab, Vector3 position, float startRadius, float endRadius, float duration)
        {
            if (prefab == null) return null;

            EnergyShockwave instance = Instantiate(prefab, position, Quaternion.identity);
            instance._startRadius = Mathf.Max(0.1f, startRadius);
            instance._endRadius = Mathf.Max(startRadius + 0.1f, endRadius);
            instance._durationSec = Mathf.Max(0.05f, duration);
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
            float t = Mathf.Clamp01(_elapsedSec / _durationSec);

            // 勢いよく広がって減速する(イーズアウト)
            float eased = 1f - (1f - t) * (1f - t);
            BuildRing(Mathf.Lerp(_startRadius, _endRadius, eased));

            // 広がりながら薄くなる
            float alpha = 1f - t;
            Color color = _baseColor;
            color.a *= alpha;
            _meshRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BASE_COLOR_ID, color);
            _meshRenderer.SetPropertyBlock(_propertyBlock);

            if (_flashLight != null) _flashLight.intensity = _flashIntensity * alpha;

            if (t >= 1f) Destroy(gameObject);
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
