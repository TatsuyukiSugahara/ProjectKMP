using System.Collections.Generic;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// とびこみの予測表示。通り道をカプセル(両端が丸い帯)の輪郭で示し、着地点に円を重ねる。
    /// 実際の当たり判定も「進む線に沿ったカプセル」と「着地点の球」なので、
    /// 表示と判定の形が一致する。見えている通りに当たる、が守られる。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class DiveAimIndicator : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Min(0.0f), Tooltip("地面から浮かせる高さ(m)。ちらつきを防ぐ")]
        private float _groundOffset = 0.05f;

        [SerializeField, Min(0.05f), Tooltip("通り道のカプセルの太さ(半径・m)")]
        private float _pathRadius = 0.5f;

        [SerializeField, Min(0.02f), Tooltip("輪郭の線の太さ(m)")]
        private float _outlineThickness = 0.14f;

        [SerializeField, Min(0.02f), Tooltip("着地点の円の線の太さ(m)")]
        private float _circleThickness = 0.18f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("中の塗りの濃さ。輪郭に対する割合。0で塗らない")]
        private float _fillAlpha = 0.28f;

        [SerializeField, Min(0.0f), Tooltip("明滅の速さ。0で明滅しない")]
        private float _pulseSpeed = 4.0f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("明滅で透明度をどれだけ揺らすか")]
        private float _pulseAmount = 0.3f;

        [SerializeField, Tooltip("通り道に相手がいるときの色")]
        private Color _hitColor = new Color(1.0f, 0.35f, 0.3f, 0.75f);

        [SerializeField, Min(0.0f), Tooltip("相手がいるときの明滅の速さ")]
        private float _hitPulseSpeed = 9.0f;

        // ---- 内部状態 ------------------------------------

        /// <summary>半円ひとつぶんの分割数。増やすほど滑らか</summary>
        private const int CAP_SEGMENTS = 16;

        private const int CIRCLE_SEGMENTS = 40;
        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor = Color.white;
        private bool _willHit;

        private MeshFilter _fillMeshFilter;
        private MeshRenderer _fillMeshRenderer;
        private Mesh _fillMesh;
        private MaterialPropertyBlock _fillPropertyBlock;

        // ---- 公開API -------------------------------------

        /// <summary>跳ぶ距離と着地点の半径に合わせて表示を作り直す</summary>
        public void Configure(float distance, float landingRadius)
        {
            EnsureInitialized();

            float radius = Mathf.Max(0.1f, landingRadius);
            float length = Mathf.Max(0.1f, distance);

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            BuildCapsuleOutline(vertices, triangles, length, _pathRadius, _outlineThickness);
            BuildRing(vertices, triangles, length, radius, _circleThickness);
            ApplyMesh(_mesh, vertices, triangles);

            // 中を塗る面は別のメッシュにする。輪郭より薄くしたいので、色を分けて持たせる
            var fillVertices = new List<Vector3>();
            var fillTriangles = new List<int>();

            BuildCapsuleFill(fillVertices, fillTriangles, length, _pathRadius);
            BuildDisc(fillVertices, fillTriangles, length, radius);
            ApplyMesh(_fillMesh, fillVertices, fillTriangles);
        }

        private static void ApplyMesh(Mesh mesh, List<Vector3> vertices, List<int> triangles)
        {
            if (mesh == null) return;

            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.normals = normals;
        }

        /// <summary>通り道に相手がいるかを伝える。色と明滅の速さが切り替わる</summary>
        public void SetWillHit(bool willHit)
        {
            _willHit = willHit;
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            EnsureInitialized();

            if (_meshRenderer.sharedMaterial != null && _meshRenderer.sharedMaterial.HasProperty(BASE_COLOR_ID))
            {
                _baseColor = _meshRenderer.sharedMaterial.GetColor(BASE_COLOR_ID);
            }
        }

        private void Update()
        {
            if (_pulseAmount <= 0.0f) return;

            float speed = _willHit ? _hitPulseSpeed : _pulseSpeed;
            if (speed <= 0.0f) return;

            float wave = (Mathf.Sin(Time.time * speed) + 1.0f) * 0.5f;

            Color color = _willHit ? _hitColor : _baseColor;
            color.a *= 1.0f - _pulseAmount * wave;

            _meshRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BASE_COLOR_ID, color);
            _meshRenderer.SetPropertyBlock(_propertyBlock);

            if (_fillMeshRenderer == null) return;

            Color fill = color;
            fill.a *= _fillAlpha;

            _fillMeshRenderer.enabled = _fillAlpha > 0.001f;
            _fillMeshRenderer.GetPropertyBlock(_fillPropertyBlock);
            _fillPropertyBlock.SetColor(BASE_COLOR_ID, fill);
            _fillMeshRenderer.SetPropertyBlock(_fillPropertyBlock);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_fillMesh != null) Destroy(_fillMesh);
        }

        // ---- 内部処理 ------------------------------------

        private void EnsureInitialized()
        {
            if (_mesh != null) return;

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _propertyBlock = new MaterialPropertyBlock();

            _mesh = new Mesh { name = "DiveAimIndicator" };
            _meshFilter.mesh = _mesh;

            CreateFillRenderer();
        }

        /// <summary>
        /// 中を塗る面を子として用意する。輪郭と濃さを分けたいので描画を別にする。
        /// 少しだけ低い位置に置いて、輪郭より奥に描かれるようにしている。
        /// </summary>
        private void CreateFillRenderer()
        {
            var go = new GameObject("Fill");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0.0f, -0.005f, 0.0f);

            _fillMeshFilter = go.AddComponent<MeshFilter>();
            _fillMeshRenderer = go.AddComponent<MeshRenderer>();
            _fillMeshRenderer.sharedMaterial = _meshRenderer.sharedMaterial;
            _fillMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fillMeshRenderer.receiveShadows = false;

            _fillPropertyBlock = new MaterialPropertyBlock();

            _fillMesh = new Mesh { name = "DiveAimIndicatorFill" };
            _fillMeshFilter.mesh = _fillMesh;
        }

        /// <summary>
        /// カプセル(両端が半円の帯)の輪郭を描く。
        /// 外側と内側で同じ形を半径だけ変えて作り、そのあいだを埋めれば線になる。
        /// </summary>
        private void BuildCapsuleOutline(List<Vector3> vertices, List<int> triangles,
            float length, float radius, float thickness)
        {
            float half = thickness * 0.5f;
            float outer = radius + half;
            float inner = Mathf.Max(0.01f, radius - half);

            int start = vertices.Count;
            int count = CAP_SEGMENTS * 2;

            for (int i = 0; i < count; i++)
            {
                GetCapsulePoint(i, length, out Vector2 direction, out float centerZ);

                vertices.Add(new Vector3(direction.x * inner, _groundOffset, centerZ + direction.y * inner));
                vertices.Add(new Vector3(direction.x * outer, _groundOffset, centerZ + direction.y * outer));
            }

            AddBandTriangles(triangles, start, count);
        }

        /// <summary>
        /// カプセルの輪郭を1周ぶんの点に分ける。前半は先端の半円、後半は足元の半円。
        /// 半円どうしを繋ぐと、まっすぐな側面が自然にできる。
        /// </summary>
        private static void GetCapsulePoint(int index, float length, out Vector2 direction, out float centerZ)
        {
            if (index < CAP_SEGMENTS)
            {
                // 先端側: 右から上を通って左へ
                float t = index / (float)(CAP_SEGMENTS - 1);
                float angle = Mathf.Lerp(Mathf.PI * 0.5f, -Mathf.PI * 0.5f, t);
                direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                centerZ = length;
                return;
            }

            // 足元側: 左から下を通って右へ
            float back = (index - CAP_SEGMENTS) / (float)(CAP_SEGMENTS - 1);
            float backAngle = Mathf.Lerp(-Mathf.PI * 0.5f, -Mathf.PI * 1.5f, back);
            direction = new Vector2(Mathf.Sin(backAngle), Mathf.Cos(backAngle));
            centerZ = 0.0f;
        }

        /// <summary>着地点の輪。中は塗らないので、下の地面や相手が見える</summary>
        private void BuildRing(List<Vector3> vertices, List<int> triangles,
            float centerZ, float radius, float thickness)
        {
            int start = vertices.Count;
            float inner = Mathf.Max(0.01f, radius - thickness);

            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float angle = i / (float)CIRCLE_SEGMENTS * Mathf.PI * 2.0f;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);

                vertices.Add(new Vector3(x * inner, _groundOffset, centerZ + z * inner));
                vertices.Add(new Vector3(x * radius, _groundOffset, centerZ + z * radius));
            }

            AddBandTriangles(triangles, start, CIRCLE_SEGMENTS);
        }

        /// <summary>カプセルの中身を塗る。凸な形なので、中心から扇状に張れば埋まる</summary>
        private void BuildCapsuleFill(List<Vector3> vertices, List<int> triangles, float length, float radius)
        {
            int start = vertices.Count;
            int count = CAP_SEGMENTS * 2;

            vertices.Add(new Vector3(0.0f, _groundOffset, length * 0.5f));

            for (int i = 0; i < count; i++)
            {
                GetCapsulePoint(i, length, out Vector2 direction, out float centerZ);
                vertices.Add(new Vector3(direction.x * radius, _groundOffset, centerZ + direction.y * radius));
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;

                triangles.Add(start);
                triangles.Add(start + 1 + next);
                triangles.Add(start + 1 + i);
            }
        }

        /// <summary>着地点の円を塗る</summary>
        private void BuildDisc(List<Vector3> vertices, List<int> triangles, float centerZ, float radius)
        {
            int start = vertices.Count;

            vertices.Add(new Vector3(0.0f, _groundOffset, centerZ));

            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float angle = i / (float)CIRCLE_SEGMENTS * Mathf.PI * 2.0f;
                vertices.Add(new Vector3(
                    Mathf.Cos(angle) * radius, _groundOffset, centerZ + Mathf.Sin(angle) * radius));
            }

            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                int next = (i + 1) % CIRCLE_SEGMENTS;

                triangles.Add(start);
                triangles.Add(start + 1 + next);
                triangles.Add(start + 1 + i);
            }
        }

        /// <summary>内側と外側の点を交互に並べた帯を、三角形でつなぐ</summary>
        private static void AddBandTriangles(List<int> triangles, int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;

                triangles.Add(start + i * 2);
                triangles.Add(start + next * 2);
                triangles.Add(start + i * 2 + 1);
                triangles.Add(start + i * 2 + 1);
                triangles.Add(start + next * 2);
                triangles.Add(start + next * 2 + 1);
            }
        }
    }
}
