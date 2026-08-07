using UnityEngine;

namespace ProjectKMP.Field
{
    /// <summary>
    /// 草原の1区画。担当ぶんの草を1枚のメッシュにまとめて描く。
    /// なぎ倒されるとその区画のメッシュだけを作り直し、時間が経つと少しずつ起き上がる。
    /// GrassField から作られる想定で、単体で使うことはない。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class GrassChunk : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>草の中間の高さ(根元から先端までの割合)</summary>
        private const float MID_HEIGHT_RATIO = 0.55f;

        /// <summary>中間の幅を根元の幅の何倍にするか</summary>
        private const float MID_WIDTH_RATIO = 0.45f;

        /// <summary>なぎ倒されたときに先端が横へ流れる量(草の高さに対する割合)</summary>
        private const float BEND_REACH = 0.9f;

        /// <summary>なぎ倒されたときに先端が下がる量(草の高さに対する割合)</summary>
        private const float BEND_SINK = 0.8f;

        // ---- 型 ------------------------------------------

        /// <summary>草の形の設定。GrassField から渡される</summary>
        public struct BladeShape
        {
            public Vector2 HeightRange;
            public Vector2 WidthRange;
            public float LeanAmount;
        }

        /// <summary>草1本ぶんの情報。メッシュはこれをもとに毎回組み立てる</summary>
        private struct Blade
        {
            public Vector3 Root;
            public Vector3 Facing;
            public Vector3 LeanDirection;
            public float LeanAmount;
            public float Height;
            public float Width;

            public Vector3 BendDirection;
            public float Bend01;
            public float RecoverDelayRemainSec;
        }

        // ---- 内部状態 ------------------------------------

        private Blade[] _blades;
        private Mesh _mesh;
        private Vector3[] _vertices;
        private Vector3[] _normals;
        private Vector2[] _uvs;

        private bool _isDirty;
        private bool _hasBentBlade;
        private float _recoverSec = 2.5f;

        // ---- 公開API -------------------------------------

        /// <summary>この区画の中心(ワールド座標)</summary>
        public Vector3 Center => transform.position;

        /// <summary>
        /// 担当ぶんの草を作る。根元の位置は生やせる場所だけに絞ったものが渡される。
        /// 向きは株の中心から外へ開く方向で、板の向きと傾く向きの両方に使う。
        /// </summary>
        public void Build(Vector3[] localRoots, Vector3[] directions, int seed, BladeShape shape, Material material)
        {
            var random = new System.Random(seed);

            _blades = new Blade[localRoots == null ? 0 : localRoots.Length];
            for (int i = 0; i < _blades.Length; i++)
            {
                Vector3 direction = directions != null && i < directions.Length ? directions[i] : Vector3.zero;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                }

                _blades[i] = new Blade
                {
                    Root = localRoots[i],
                    Facing = direction,
                    LeanDirection = direction,
                    LeanAmount = shape.LeanAmount * (0.4f + 0.6f * (float)random.NextDouble()),
                    Height = Mathf.Lerp(shape.HeightRange.x, shape.HeightRange.y, (float)random.NextDouble()),
                    Width = Mathf.Lerp(shape.WidthRange.x, shape.WidthRange.y, (float)random.NextDouble()),
                };
            }

            CreateMesh(material);
            RebuildMesh();
        }

        /// <summary>
        /// 中心から innerRadius〜outerRadius の輪の中にある草を、中心から離れる向きへ倒す。
        /// innerRadius に0を渡せば円の中すべてが対象になる。
        /// </summary>
        public void Flatten(
            Vector3 worldCenter, float innerRadius, float outerRadius,
            float recoverDelaySec, float recoverSec, float strength)
        {
            if (_blades == null) return;

            _recoverSec = Mathf.Max(0.05f, recoverSec);

            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
            float sqrInner = innerRadius * innerRadius;
            float sqrOuter = outerRadius * outerRadius;

            for (int i = 0; i < _blades.Length; i++)
            {
                Vector3 delta = _blades[i].Root - localCenter;
                delta.y = 0f;

                float sqrDistance = delta.sqrMagnitude;
                if (sqrDistance > sqrOuter || sqrDistance < sqrInner) continue;

                // 爆心と重なった草は倒れる向きが決まらないので、もともとの傾きの向きへ倒す
                _blades[i].BendDirection = delta.sqrMagnitude < 0.0001f
                    ? _blades[i].LeanDirection
                    : delta.normalized;

                // 弱い波が後から来ても、すでに深く倒れている草を起こしてしまわないようにする
                _blades[i].Bend01 = Mathf.Max(_blades[i].Bend01, Mathf.Clamp01(strength));
                _blades[i].RecoverDelayRemainSec = recoverDelaySec;

                _isDirty = true;
                _hasBentBlade = true;
            }
        }

        // ---- Unityイベント -------------------------------

        private void Update()
        {
            if (_hasBentBlade) UpdateRecover();
            if (_isDirty) RebuildMesh();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>倒れた草を少しずつ起き上がらせる</summary>
        private void UpdateRecover()
        {
            bool stillBent = false;

            for (int i = 0; i < _blades.Length; i++)
            {
                if (_blades[i].Bend01 <= 0f) continue;

                if (_blades[i].RecoverDelayRemainSec > 0f)
                {
                    _blades[i].RecoverDelayRemainSec -= Time.deltaTime;
                    stillBent = true;
                    continue;
                }

                _blades[i].Bend01 = Mathf.Max(0f, _blades[i].Bend01 - Time.deltaTime / _recoverSec);
                if (_blades[i].Bend01 > 0f) stillBent = true;

                _isDirty = true;
            }

            _hasBentBlade = stillBent;
        }

        private void CreateMesh(Material material)
        {
            int vertexCount = _blades.Length * GrassField.BLADE_VERTEX_COUNT;

            _vertices = new Vector3[vertexCount];
            _normals = new Vector3[vertexCount];
            _uvs = new Vector2[vertexCount];

            var triangles = new int[_blades.Length * 9];
            for (int i = 0; i < _blades.Length; i++)
            {
                int v = i * GrassField.BLADE_VERTEX_COUNT;
                int t = i * 9;

                triangles[t] = v;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;

                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;

                triangles[t + 6] = v + 2;
                triangles[t + 7] = v + 4;
                triangles[t + 8] = v + 3;

                // 根元は暗く、先端へ向かって明るくしたいときに使えるようUVを縦方向に張る
                _uvs[v] = new Vector2(0f, 0f);
                _uvs[v + 1] = new Vector2(1f, 0f);
                _uvs[v + 2] = new Vector2(0f, MID_HEIGHT_RATIO);
                _uvs[v + 3] = new Vector2(1f, MID_HEIGHT_RATIO);
                _uvs[v + 4] = new Vector2(0.5f, 1f);
            }

            _mesh = new Mesh { name = "GrassChunk" };
            _mesh.indexFormat = vertexCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.vertices = _vertices;
            _mesh.triangles = triangles;
            _mesh.uv = _uvs;

            GetComponent<MeshFilter>().sharedMesh = _mesh;

            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;

            // 草1本ずつの影は形が潰れて見えないうえ、枚数が多く重い
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>今の草の状態から頂点を組み立て直す</summary>
        private void RebuildMesh()
        {
            _isDirty = false;
            if (_mesh == null || _blades == null) return;

            for (int i = 0; i < _blades.Length; i++)
            {
                Blade blade = _blades[i];
                int v = i * GrassField.BLADE_VERTEX_COUNT;

                // 幅方向は草の向きに直交する水平方向
                var right = new Vector3(-blade.Facing.z, 0f, blade.Facing.x);
                float halfWidth = blade.Width * 0.5f;

                Vector3 mid = blade.Root + Offset(blade, MID_HEIGHT_RATIO);
                Vector3 tip = blade.Root + Offset(blade, 1f);

                _vertices[v] = blade.Root - right * halfWidth;
                _vertices[v + 1] = blade.Root + right * halfWidth;
                _vertices[v + 2] = mid - right * (halfWidth * MID_WIDTH_RATIO);
                _vertices[v + 3] = mid + right * (halfWidth * MID_WIDTH_RATIO);
                _vertices[v + 4] = tip;

                // 上向きに草の向きを少し混ぜると、1本ずつ明るさが変わって密集感が出る
                Vector3 normal = (Vector3.up * 3f + blade.Facing).normalized;
                for (int n = 0; n < GrassField.BLADE_VERTEX_COUNT; n++) _normals[v + n] = normal;
            }

            _mesh.vertices = _vertices;
            _mesh.normals = _normals;
            _mesh.RecalculateBounds();
        }

        /// <summary>根元からの高さの割合 t のときの、根元からのずれを求める</summary>
        private static Vector3 Offset(Blade blade, float t)
        {
            // もともとの傾き。先端ほど大きく曲がる
            Vector3 lean = blade.LeanDirection * (blade.LeanAmount * blade.Height * t * t);

            // なぎ倒されたぶん。横へ流れると同時に高さが下がる
            Vector3 bend = blade.BendDirection * (blade.Bend01 * blade.Height * BEND_REACH * t * t);
            float height = blade.Height * t * (1f - BEND_SINK * blade.Bend01);

            return lean + bend + Vector3.up * height;
        }
    }
}
