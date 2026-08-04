using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// ビームスキルの狙い中に足元へ出す、発射範囲と方向の表示。
    /// ビームの幅の帯 + 先端の矢印をひとつのメッシュとして生成し、地面すれすれに描画する。
    /// プレイヤーの子として生成されるので、向きを変えると一緒に回る。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BeamAimIndicator : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Min(0f), Tooltip("地面から浮かせる高さ(m)。ちらつき(Zファイティング)を防ぐ")]
        private float _groundOffset = 0.05f;

        [SerializeField, Min(0.1f), Tooltip("先端の矢印部分の長さ(m)")]
        private float _arrowLength = 1.2f;

        [SerializeField, Min(1f), Tooltip("矢印部分の幅を帯の幅の何倍にするか")]
        private float _arrowWidthScale = 1.8f;

        [SerializeField, Min(0f), Tooltip("明滅の速さ。0で明滅しない")]
        private float _pulseSpeed = 4f;

        [SerializeField, Range(0f, 1f), Tooltip("明滅で透明度をどれだけ揺らすか")]
        private float _pulseAmount = 0.3f;

        // ---- 内部状態 ------------------------------------

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor = Color.white;

        // ---- 公開API -------------------------------------

        /// <summary>ビームの長さと太さ(半径)に合わせて表示を作り直す</summary>
        public void Configure(float beamLength, float beamRadius)
        {
            EnsureInitialized();

            float halfWidth = Mathf.Max(0.05f, beamRadius);
            float arrowLength = Mathf.Min(_arrowLength, beamLength * 0.5f);
            float bodyLength = Mathf.Max(0.1f, beamLength - arrowLength);
            float arrowHalfWidth = halfWidth * _arrowWidthScale;
            float y = _groundOffset;

            // 帯(長方形)+ 矢印(三角形)。ローカル +Z がプレイヤーの正面
            var vertices = new Vector3[]
            {
                new Vector3(-halfWidth, y, 0f),
                new Vector3(halfWidth, y, 0f),
                new Vector3(-halfWidth, y, bodyLength),
                new Vector3(halfWidth, y, bodyLength),
                new Vector3(-arrowHalfWidth, y, bodyLength),
                new Vector3(arrowHalfWidth, y, bodyLength),
                new Vector3(0f, y, bodyLength + arrowLength),
            };

            var triangles = new int[]
            {
                0, 2, 1,
                1, 2, 3,
                4, 6, 5,
            };

            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;

            _mesh.Clear();
            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.normals = normals;
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
            if (_pulseSpeed <= 0f || _pulseAmount <= 0f) return;

            // sin波で透明度を揺らし、狙い中であることを目立たせる
            float wave = (Mathf.Sin(Time.time * _pulseSpeed) + 1f) * 0.5f;
            float alphaMul = 1f - _pulseAmount * wave;

            Color color = _baseColor;
            color.a *= alphaMul;

            _meshRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BASE_COLOR_ID, color);
            _meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        // ---- 内部処理 ------------------------------------

        private void EnsureInitialized()
        {
            if (_mesh != null) return;

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _propertyBlock = new MaterialPropertyBlock();

            _mesh = new Mesh { name = "BeamAimIndicator" };
            _meshFilter.mesh = _mesh;
        }
    }
}
