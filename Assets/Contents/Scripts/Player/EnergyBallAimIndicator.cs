using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 元気玉スキルの照準表示。
    /// プレイヤーを中心とした射程の上限を示すリングと、着弾点を示す円マーカーを地面に描く。
    /// リングとマーカーのメッシュは実行時に生成する。
    /// </summary>
    public class EnergyBallAimIndicator : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("射程リングの描画先。未設定なら 'Range' という子から探す")]
        private MeshFilter _rangeMeshFilter;

        [SerializeField, Tooltip("着弾マーカーの描画先。未設定なら 'Marker' という子から探す")]
        private MeshFilter _markerMeshFilter;

        [SerializeField, Min(0.02f), Tooltip("射程リングの線の太さ(m)")]
        private float _ringThickness = 0.25f;

        [SerializeField, Min(0f), Tooltip("地面から浮かせる高さ(m)。ちらつきを防ぐ")]
        private float _groundOffset = 0.05f;

        [SerializeField, Min(0f), Tooltip("マーカーの明滅の速さ。0で明滅しない")]
        private float _pulseSpeed = 5f;

        [SerializeField, Range(0f, 1f), Tooltip("明滅で透明度をどれだけ揺らすか")]
        private float _pulseAmount = 0.35f;

        // ---- 内部状態 ------------------------------------

        private const int SEGMENTS = 48;
        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        private Mesh _ringMesh;
        private Mesh _discMesh;
        private Renderer _markerRenderer;
        private MaterialPropertyBlock _propertyBlock;
        private Color _markerBaseColor = Color.white;

        // ---- 公開API -------------------------------------

        /// <summary>射程の上限(半径)と着弾範囲(半径)に合わせて表示を作り直す</summary>
        public void Configure(float maxRange, float markerRadius)
        {
            EnsureInitialized();

            BuildRingMesh(_ringMesh, maxRange, _ringThickness);
            BuildDiscMesh(_discMesh, markerRadius);
        }

        /// <summary>着弾マーカーを指定のワールド座標へ動かす</summary>
        public void SetMarkerPosition(Vector3 worldPosition)
        {
            EnsureInitialized();
            if (_markerMeshFilter != null)
            {
                _markerMeshFilter.transform.position = worldPosition;
            }
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            EnsureInitialized();

            if (_markerRenderer != null && _markerRenderer.sharedMaterial != null
                && _markerRenderer.sharedMaterial.HasProperty(BASE_COLOR_ID))
            {
                _markerBaseColor = _markerRenderer.sharedMaterial.GetColor(BASE_COLOR_ID);
            }
        }

        private void Update()
        {
            if (_pulseSpeed <= 0f || _pulseAmount <= 0f || _markerRenderer == null) return;

            float wave = (Mathf.Sin(Time.time * _pulseSpeed) + 1f) * 0.5f;
            Color color = _markerBaseColor;
            color.a *= 1f - _pulseAmount * wave;

            _markerRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BASE_COLOR_ID, color);
            _markerRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void OnDestroy()
        {
            if (_ringMesh != null) Destroy(_ringMesh);
            if (_discMesh != null) Destroy(_discMesh);
        }

        // ---- 内部処理 ------------------------------------

        private void EnsureInitialized()
        {
            if (_propertyBlock != null) return;

            _propertyBlock = new MaterialPropertyBlock();

            if (_rangeMeshFilter == null)
            {
                Transform range = transform.Find("Range");
                if (range != null) _rangeMeshFilter = range.GetComponent<MeshFilter>();
            }

            if (_markerMeshFilter == null)
            {
                Transform marker = transform.Find("Marker");
                if (marker != null) _markerMeshFilter = marker.GetComponent<MeshFilter>();
            }

            if (_markerMeshFilter != null) _markerRenderer = _markerMeshFilter.GetComponent<Renderer>();

            _ringMesh = new Mesh { name = "EnergyBallRangeRing" };
            _discMesh = new Mesh { name = "EnergyBallMarkerDisc" };
            if (_rangeMeshFilter != null) _rangeMeshFilter.mesh = _ringMesh;
            if (_markerMeshFilter != null) _markerMeshFilter.mesh = _discMesh;
        }

        /// <summary>指定半径のリング(輪郭線)メッシュを作る</summary>
        private void BuildRingMesh(Mesh mesh, float radius, float thickness)
        {
            if (mesh == null) return;

            var vertices = new Vector3[SEGMENTS * 2];
            var triangles = new int[SEGMENTS * 6];
            float inner = Mathf.Max(0.01f, radius - thickness);

            for (int i = 0; i < SEGMENTS; i++)
            {
                float angle = i / (float)SEGMENTS * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = dir * inner + Vector3.up * _groundOffset;
                vertices[i * 2 + 1] = dir * radius + Vector3.up * _groundOffset;

                int next = (i + 1) % SEGMENTS;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = next * 2;
                triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = i * 2 + 1;
                triangles[t + 4] = next * 2;
                triangles[t + 5] = next * 2 + 1;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
        }

        /// <summary>指定半径の円盤メッシュを作る</summary>
        private void BuildDiscMesh(Mesh mesh, float radius)
        {
            if (mesh == null) return;

            var vertices = new Vector3[SEGMENTS + 1];
            var triangles = new int[SEGMENTS * 3];
            vertices[0] = Vector3.up * _groundOffset;

            for (int i = 0; i < SEGMENTS; i++)
            {
                float angle = i / (float)SEGMENTS * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, _groundOffset, Mathf.Sin(angle) * radius);

                int next = (i + 1) % SEGMENTS;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = next + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
        }
    }
}
