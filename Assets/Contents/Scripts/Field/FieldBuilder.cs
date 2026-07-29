using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProjectKMP.Field
{
    /// <summary>
    /// ゲームの1フィールドを構成する「地面・プレイ範囲・外周の林・見えない壁」をまとめて生成する。
    /// 地面全体を草の色にしたうえで、その上にプレイ範囲ぶんの土の面を重ねることで、
    /// 動ける範囲が見た目で分かるようにしている。土の縁は波打たせ、内側には草のまだらを散らして
    /// 人工的な直線に見えないようにしている。
    /// インスペクタのコンポーネント右クリックメニューから「フィールドを再構築」で作り直す。エディタ専用。
    /// </summary>
    public class FieldBuilder : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>Unity の Plane プリミティブはスケール1あたり10m四方</summary>
        private const float PLANE_UNIT_SIZE = 10f;

        /// <summary>林の外側に確保する地面の余白(メートル)</summary>
        private const float GROUND_SAFETY_MARGIN = 8f;

        private const string GROUND_NAME = "Ground";
        private const string PLAY_AREA_NAME = "PlayArea";
        private const string GRASS_PATCH_NAME = "GrassPatches";
        private const string TREE_ROOT_NAME = "Trees";
        private const string WALL_ROOT_NAME = "Walls";
        private const float WALL_THICKNESS = 1f;

        /// <summary>プレイ範囲の面を地面から浮かせる高さ。Zファイティングを避けるためだけの値</summary>
        private const float PLAY_AREA_HEIGHT = 0.01f;

        /// <summary>草のまだらをプレイ範囲の面からさらに浮かせる高さ</summary>
        private const float GRASS_PATCH_HEIGHT = 0.02f;

        private const string FIELD_ART_FOLDER = "Assets/Contents/Art/Field";
        private const string MESH_FOLDER = FIELD_ART_FOLDER + "/Meshes";
        private const string PLAY_AREA_MESH_PATH = MESH_FOLDER + "/MESH_Field_PlayArea.asset";
        private const string GRASS_PATCH_MESH_PATH = MESH_FOLDER + "/MESH_Field_GrassPatches.asset";

        /// <summary>草のまだら1つを何角形で作るか</summary>
        private const int GRASS_PATCH_SIDES = 24;

        /// <summary>まだらを縁からどれだけ離すか(メートル)。触れると外の草地とつながって見えてしまう</summary>
        private const float GRASS_PATCH_EDGE_MARGIN = 2f;

        /// <summary>まだらの形をゆがませる量。1つの半径がこの割合まで伸び縮みする</summary>
        private const float GRASS_PATCH_DISTORTION = 0.30f;

        // ---- インスペクタ設定 ------------------------------

        [Header("プレイ範囲")]
        [SerializeField, Tooltip("プレイヤーが動ける範囲(メートル)。見えない壁の内側の広さ")]
        private Vector2 _fieldSize = new Vector2(200f, 200f);

        [Header("地面")]
        [SerializeField, Tooltip("プレイ範囲の外側に伸ばす地面の幅(メートル)。林を覆うぶんは自動で確保される")]
        private float _groundMargin = 40f;

        [SerializeField, Tooltip("地面全体に貼るマテリアル。プレイ範囲の外側に見える色になる")]
        private Material _groundMaterial;

        [SerializeField, Tooltip("プレイ範囲に貼るマテリアル。動ける場所をこの色で示す。未設定なら面を作らない")]
        private Material _playAreaMaterial;

        [Header("プレイ範囲の縁")]
        [SerializeField, Tooltip("縁を壁からどれだけ内側に寄せるか(メートル)。ゆらぎの中心になる")]
        private float _edgeInset = 3f;

        [SerializeField, Tooltip("縁のゆらぎ幅(メートル)。0にすると直線的な四角になる")]
        private float _edgeWaveAmplitude = 3f;

        [SerializeField, Tooltip("縁を分割する間隔(メートル)。小さいほど滑らかになる")]
        private float _edgeSegmentLength = 0.5f;

        [Header("草のまだら")]
        [SerializeField, Tooltip("プレイ範囲の中に散らす草の数。0で草なし")]
        private int _grassPatchCount = 18;

        [SerializeField, Tooltip("草のまだら1つの大きさ(半径・メートル)の範囲")]
        private Vector2 _grassPatchRadiusRange = new Vector2(1.5f, 4f);

        [Header("外周の林")]
        [SerializeField, Tooltip("配置する木のプレハブ")]
        private GameObject _treePrefab;

        [SerializeField, Range(1, 20), Tooltip("外周を囲む木の列数。増やすほど外が見えなくなる")]
        private int _treeRows = 5;

        [SerializeField, Tooltip("列と列の間隔(メートル)")]
        private float _rowSpacing = 6f;

        [SerializeField, Tooltip("同じ列の木と木の基準間隔(メートル)。小さいほど密になる")]
        private float _treeSpacing = 7f;

        [SerializeField, Range(0f, 0.9f), Tooltip("間隔のばらつき。0で等間隔")]
        private float _spacingJitter = 0.35f;

        [SerializeField, Tooltip("1列目をプレイ範囲の境界からどれだけ外に出すか(メートル)")]
        private float _forestOffset = 2f;

        [SerializeField, Tooltip("各木の位置をランダムにずらす幅(メートル)。列の直線的な並びを崩す")]
        private float _positionJitter = 2.5f;

        [SerializeField, Tooltip("木のスケール範囲(最小・最大)")]
        private Vector2 _treeScaleRange = new Vector2(0.8f, 1.5f);

        [SerializeField, Tooltip("乱数シード。同じ値なら毎回同じ並びになる")]
        private int _seed = 12345;

        [Header("境界の壁")]
        [SerializeField, Tooltip("プレイ範囲の境界に見えない壁(BoxCollider)を作る")]
        private bool _createWalls = true;

        [SerializeField, Tooltip("見えない壁の高さ(メートル)")]
        private float _wallHeight = 30f;

        // ---- 公開API -------------------------------------

        /// <summary>プレイ範囲の広さ(メートル)</summary>
        public Vector2 FieldSize => _fieldSize;

        /// <summary>現在の設定値で地面・プレイ範囲・林・壁を作り直す</summary>
        [ContextMenu("フィールドを再構築")]
        public void Rebuild()
        {
#if UNITY_EDITOR
            float groundMargin = CalcGroundMargin();
            BuildGround(groundMargin);
            BuildPlayArea();
            int treeCount = BuildTrees();
            BuildWalls();
            Debug.Log($"[Field] 再構築しました: プレイ範囲 {_fieldSize.x}m x {_fieldSize.y}m / 地面 {_fieldSize.x + groundMargin * 2f}m x {_fieldSize.y + groundMargin * 2f}m / 木 {treeCount} 本({_treeRows}列) / 壁 {(_createWalls ? "あり" : "なし")}");
#else
            Debug.LogWarning("[Field] Rebuild はエディタ専用です");
#endif
        }

#if UNITY_EDITOR

        // ---- 内部処理: 地面 -------------------------------

        /// <summary>林がはみ出さないだけの地面余白を求める</summary>
        private float CalcGroundMargin()
        {
            float forestOuterEdge = _forestOffset + Mathf.Max(0, _treeRows - 1) * _rowSpacing + _positionJitter;
            float required = forestOuterEdge + GROUND_SAFETY_MARGIN;
            if (_groundMargin < required)
            {
                Debug.Log($"[Field] 林が地面からはみ出すため、地面の余白を {_groundMargin}m → {required}m に広げました");
                return required;
            }
            return _groundMargin;
        }

        /// <summary>地面(Plane)を生成またはリサイズする</summary>
        private void BuildGround(float groundMargin)
        {
            Transform ground = transform.Find(GROUND_NAME);
            if (ground == null)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Plane);
                go.name = GROUND_NAME;
                Undo.RegisterCreatedObjectUndo(go, "Create Ground");
                ground = go.transform;
                ground.SetParent(transform, false);
            }

            Undo.RecordObject(ground, "Resize Ground");
            ground.localPosition = Vector3.zero;
            ground.localRotation = Quaternion.identity;
            ground.localScale = new Vector3(
                (_fieldSize.x + groundMargin * 2f) / PLANE_UNIT_SIZE,
                1f,
                (_fieldSize.y + groundMargin * 2f) / PLANE_UNIT_SIZE);

            if (_groundMaterial != null)
            {
                MeshRenderer renderer = ground.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    Undo.RecordObject(renderer, "Set Ground Material");
                    renderer.sharedMaterial = _groundMaterial;
                }
            }

            // スケールを変えてもコライダーのバウンズが古いまま残ることがあるため、当たり判定を作り直す
            MeshCollider meshCollider = ground.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                Mesh mesh = meshCollider.sharedMesh;
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
            }
        }

        // ---- 内部処理: プレイ範囲と草のまだら ---------------

        /// <summary>プレイ範囲の面と、その中に散らす草のまだらを作り直す</summary>
        private void BuildPlayArea()
        {
            if (_playAreaMaterial == null)
            {
                DestroyChild(PLAY_AREA_NAME);
                DestroyChild(GRASS_PATCH_NAME);
                return;
            }

            // 木とは別の乱数列にして、林の並びが縁の形に影響されないようにする
            var random = new System.Random(_seed + 1);

            Vector3[] outline;
            Mesh areaMesh = SaveMeshAsset(CreatePlayAreaMesh(random, out outline), PLAY_AREA_MESH_PATH);
            EnsureFlatMeshObject(PLAY_AREA_NAME, PLAY_AREA_HEIGHT, areaMesh, _playAreaMaterial);

            Mesh patchMesh = CreateGrassPatchMesh(random, outline);
            if (patchMesh == null)
            {
                DestroyChild(GRASS_PATCH_NAME);
                DeleteMeshAsset(GRASS_PATCH_MESH_PATH);
                return;
            }

            // まだらは地面と同じ草の色を使うので、地面マテリアルをそのまま流用する
            EnsureFlatMeshObject(GRASS_PATCH_NAME, GRASS_PATCH_HEIGHT, SaveMeshAsset(patchMesh, GRASS_PATCH_MESH_PATH), _groundMaterial);
        }

        /// <summary>縁が波打つプレイ範囲のメッシュを作る。outline には縁の頂点列を返す</summary>
        private Mesh CreatePlayAreaMesh(System.Random random, out Vector3[] outline)
        {
            float halfWidth = _fieldSize.x * 0.5f;
            float halfDepth = _fieldSize.y * 0.5f;
            float perimeter = 4f * (halfWidth + halfDepth);
            int segments = Mathf.Clamp(Mathf.RoundToInt(perimeter / Mathf.Max(0.1f, _edgeSegmentLength)), 32, 4000);

            float phaseA = (float)random.NextDouble() * Mathf.PI * 2f;
            float phaseB = (float)random.NextDouble() * Mathf.PI * 2f;
            float phaseC = (float)random.NextDouble() * Mathf.PI * 2f;

            outline = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / segments;
                Vector2 point;
                Vector2 inward;
                GetPerimeterPointAndNormal(t * perimeter, halfWidth, halfDepth, out point, out inward);

                // 壁の内側 _edgeInset を中心に、±_edgeWaveAmplitude で揺らす
                float inset = Mathf.Max(0f, _edgeInset + _edgeWaveAmplitude * EdgeNoise(t, phaseA, phaseB, phaseC));
                Vector2 shifted = point + inward * inset;
                outline[i] = new Vector3(shifted.x, 0f, shifted.y);
            }

            // 中心から扇状に三角形を張る
            var vertices = new Vector3[segments + 1];
            vertices[0] = Vector3.zero;
            for (int i = 0; i < segments; i++) vertices[i + 1] = outline[i];

            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }

            return BuildFlatMesh("Field PlayArea", vertices, triangles);
        }

        /// <summary>プレイ範囲の中に草のまだらを散らしたメッシュを作る。1つも置けなければ null</summary>
        private Mesh CreateGrassPatchMesh(System.Random random, Vector3[] outline)
        {
            if (_grassPatchCount <= 0 || _groundMaterial == null) return null;

            float minRadius = Mathf.Max(0.1f, Mathf.Min(_grassPatchRadiusRange.x, _grassPatchRadiusRange.y));
            float maxRadius = Mathf.Max(minRadius, Mathf.Max(_grassPatchRadiusRange.x, _grassPatchRadiusRange.y));

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var centers = new List<Vector3>();
            var radii = new List<float>();

            int attempts = 0;
            int maxAttempts = _grassPatchCount * 50;

            while (centers.Count < _grassPatchCount && attempts < maxAttempts)
            {
                attempts++;

                float radius = Mathf.Lerp(minRadius, maxRadius, (float)random.NextDouble());
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;

                // まだらが縁からはみ出さないよう、その向きの縁までの距離から余裕を引く
                float limit = BoundaryRadius(outline, angle) - radius * (1f + GRASS_PATCH_DISTORTION) - GRASS_PATCH_EDGE_MARGIN;
                if (limit <= 0f) continue;

                // 平方根を取ることで、中心に偏らず面積あたり一様に散る
                float distance = Mathf.Sqrt((float)random.NextDouble()) * limit;
                var center = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

                bool overlapped = false;
                for (int i = 0; i < centers.Count; i++)
                {
                    if (Vector3.Distance(centers[i], center) < (radii[i] + radius) * 0.8f)
                    {
                        overlapped = true;
                        break;
                    }
                }
                if (overlapped) continue;

                centers.Add(center);
                radii.Add(radius);
                AppendGrassPatch(vertices, triangles, center, radius, random);
            }

            if (centers.Count == 0) return null;
            if (centers.Count < _grassPatchCount)
            {
                Debug.Log($"[Field] 草のまだらは {_grassPatchCount} 個中 {centers.Count} 個しか置けませんでした。数を減らすか半径を小さくしてください");
            }

            return BuildFlatMesh("Field GrassPatches", vertices.ToArray(), triangles.ToArray());
        }

        /// <summary>まだら1つぶんの多角形を頂点リストに足す</summary>
        private static void AppendGrassPatch(List<Vector3> vertices, List<int> triangles, Vector3 center, float radius, System.Random random)
        {
            float phaseA = (float)random.NextDouble() * Mathf.PI * 2f;
            float phaseB = (float)random.NextDouble() * Mathf.PI * 2f;

            int baseIndex = vertices.Count;
            vertices.Add(center);

            for (int i = 0; i < GRASS_PATCH_SIDES; i++)
            {
                float angle = Mathf.PI * 2f * i / GRASS_PATCH_SIDES;
                // 低い周波数の波を2つ重ねて、円をいびつな輪郭に崩す
                float distortion = 0.20f * Mathf.Sin(2f * angle + phaseA) + 0.10f * Mathf.Sin(5f * angle + phaseB);
                float r = radius * (1f + distortion);
                vertices.Add(center + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r));
            }

            for (int i = 0; i < GRASS_PATCH_SIDES; i++)
            {
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1 + i);
                triangles.Add(baseIndex + 1 + (i + 1) % GRASS_PATCH_SIDES);
            }
        }

        /// <summary>指定した向きに縁がどれだけ離れているかを返す</summary>
        private static float BoundaryRadius(Vector3[] outline, float angle)
        {
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float bestDot = -2f;
            float result = 0f;

            for (int i = 0; i < outline.Length; i++)
            {
                var point = new Vector2(outline[i].x, outline[i].z);
                float length = point.magnitude;
                if (length < 0.0001f) continue;

                float dot = Vector2.Dot(point / length, direction);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    result = length;
                }
            }

            return result;
        }

        /// <summary>縁のゆらぎ。1周でつながるよう、周期が整数の波だけを重ねている</summary>
        private static float EdgeNoise(float t, float phaseA, float phaseB, float phaseC)
        {
            float value = Mathf.Sin(Mathf.PI * 2f * 7f * t + phaseA)
                        + 0.5f * Mathf.Sin(Mathf.PI * 2f * 13f * t + phaseB)
                        + 0.25f * Mathf.Sin(Mathf.PI * 2f * 29f * t + phaseC);
            return value / 1.75f;
        }

        /// <summary>矩形の外周を一周する距離から、座標と内向きの方向(XZ平面)を求める</summary>
        private static void GetPerimeterPointAndNormal(float distance, float halfWidth, float halfDepth, out Vector2 point, out Vector2 inward)
        {
            float width = halfWidth * 2f;
            float depth = halfDepth * 2f;

            if (distance < width)
            {
                point = new Vector2(-halfWidth + distance, -halfDepth);
                inward = new Vector2(0f, 1f);
                return;
            }
            distance -= width;

            if (distance < depth)
            {
                point = new Vector2(halfWidth, -halfDepth + distance);
                inward = new Vector2(-1f, 0f);
                return;
            }
            distance -= depth;

            if (distance < width)
            {
                point = new Vector2(halfWidth - distance, halfDepth);
                inward = new Vector2(0f, -1f);
                return;
            }
            distance -= width;

            point = new Vector2(-halfWidth, halfDepth - distance);
            inward = new Vector2(1f, 0f);
        }

        /// <summary>上を向いた平らなメッシュを組み立てる</summary>
        private static Mesh BuildFlatMesh(string meshName, Vector3[] vertices, int[] triangles)
        {
            FixUpwardWinding(vertices, triangles);

            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                normals[i] = Vector3.up;
                uvs[i] = new Vector2(vertices[i].x, vertices[i].z) * 0.1f;
            }

            var mesh = new Mesh();
            mesh.name = meshName;
            mesh.indexFormat = vertices.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>面が下を向いていたら三角形の巻き順を反転する</summary>
        private static void FixUpwardWinding(Vector3[] vertices, int[] triangles)
        {
            if (triangles.Length < 3) return;

            Vector3 normal = Vector3.Cross(
                vertices[triangles[1]] - vertices[triangles[0]],
                vertices[triangles[2]] - vertices[triangles[0]]);
            if (normal.y >= 0f) return;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }
        }

        /// <summary>生成したメッシュをアセットとして保存する。保存しないとシーンを開き直したときに消えるため</summary>
        private static Mesh SaveMeshAsset(Mesh mesh, string path)
        {
            if (!AssetDatabase.IsValidFolder(MESH_FOLDER))
            {
                AssetDatabase.CreateFolder(FIELD_ART_FOLDER, "Meshes");
            }

            DeleteMeshAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        /// <summary>生成済みのメッシュアセットを消す</summary>
        private static void DeleteMeshAsset(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        /// <summary>当たり判定を持たない、平らな表示専用オブジェクトを用意する</summary>
        private Transform EnsureFlatMeshObject(string objectName, float height, Mesh mesh, Material material)
        {
            Transform child = transform.Find(objectName);
            if (child == null)
            {
                var go = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
                Undo.RegisterCreatedObjectUndo(go, "Create " + objectName);
                child = go.transform;
                child.SetParent(transform, false);
            }

            Undo.RecordObject(child, "Update " + objectName);
            child.localPosition = new Vector3(0f, height, 0f);
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;

            MeshFilter filter = child.GetComponent<MeshFilter>();
            if (filter == null) filter = Undo.AddComponent<MeshFilter>(child.gameObject);
            Undo.RecordObject(filter, "Set Mesh");
            filter.sharedMesh = mesh;

            MeshRenderer meshRenderer = child.GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = Undo.AddComponent<MeshRenderer>(child.gameObject);
            Undo.RecordObject(meshRenderer, "Set Material");
            meshRenderer.sharedMaterial = material;
            // 地面のすぐ上にある薄い面なので、影を落とすと境界にノイズが出る
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 当たり判定は地面(Ground)側だけで取る
            Collider unusedCollider = child.GetComponent<Collider>();
            if (unusedCollider != null) Undo.DestroyObjectImmediate(unusedCollider);

            return child;
        }

        /// <summary>指定した名前の子オブジェクトがあれば消す</summary>
        private void DestroyChild(string objectName)
        {
            Transform child = transform.Find(objectName);
            if (child != null) Undo.DestroyObjectImmediate(child.gameObject);
        }

        // ---- 内部処理: 林と壁 -----------------------------

        /// <summary>プレイ範囲の外側を囲む林を並べ直し、配置した本数を返す</summary>
        private int BuildTrees()
        {
            Transform root = transform.Find(TREE_ROOT_NAME);
            if (root == null)
            {
                GameObject go = new GameObject(TREE_ROOT_NAME);
                Undo.RegisterCreatedObjectUndo(go, "Create Trees Root");
                root = go.transform;
                root.SetParent(transform, false);
                root.localPosition = Vector3.zero;
            }

            // 再構築のたびに前回分を消してから並べ直す
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
            }

            if (_treePrefab == null)
            {
                Debug.LogWarning("[Field] Tree Prefab が未設定のため木を配置しませんでした");
                return 0;
            }

            var random = new System.Random(_seed);
            float spacing = Mathf.Max(1f, _treeSpacing);
            int count = 0;

            for (int row = 0; row < _treeRows; row++)
            {
                // プレイ範囲の境界から外へ向かって列を重ね、帯状の林にする
                float rowOffset = _forestOffset + row * _rowSpacing;
                float halfWidth = _fieldSize.x * 0.5f + rowOffset;
                float halfDepth = _fieldSize.y * 0.5f + rowOffset;

                float perimeter = 4f * (halfWidth + halfDepth);
                // 列ごとに開始位置をずらし、列同士が整列して視線が抜けるのを防ぐ
                float distance = spacing * 0.5f * row;

                while (distance < perimeter)
                {
                    Vector3 point = GetPerimeterPoint(distance, halfWidth, halfDepth);
                    point.x += (float)(random.NextDouble() * 2.0 - 1.0) * _positionJitter;
                    point.z += (float)(random.NextDouble() * 2.0 - 1.0) * _positionJitter;

                    // ばらつきでプレイ範囲の内側に入り込んだ木は、境界の外へ押し戻す
                    float insideX = _fieldSize.x * 0.5f - Mathf.Abs(point.x);
                    float insideZ = _fieldSize.y * 0.5f - Mathf.Abs(point.z);
                    if (insideX > 0f && insideZ > 0f)
                    {
                        if (insideX <= insideZ)
                        {
                            point.x = Mathf.Sign(point.x == 0f ? 1f : point.x) * (_fieldSize.x * 0.5f + 0.5f);
                        }
                        else
                        {
                            point.z = Mathf.Sign(point.z == 0f ? 1f : point.z) * (_fieldSize.y * 0.5f + 0.5f);
                        }
                    }

                    var tree = (GameObject)PrefabUtility.InstantiatePrefab(_treePrefab, root);
                    Undo.RegisterCreatedObjectUndo(tree, "Create Tree");
                    tree.name = $"PF_Tree_Tree_{row}_{count}";
                    tree.transform.localPosition = point;
                    tree.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                    float scale = Mathf.Lerp(_treeScaleRange.x, _treeScaleRange.y, (float)random.NextDouble());
                    tree.transform.localScale = Vector3.one * scale;
                    count++;

                    distance += spacing * (1f + (float)(random.NextDouble() * 2.0 - 1.0) * _spacingJitter);
                }
            }

            return count;
        }

        /// <summary>プレイ範囲の境界に見えない壁(BoxCollider)を4面作る</summary>
        private void BuildWalls()
        {
            Transform root = transform.Find(WALL_ROOT_NAME);
            if (root == null)
            {
                GameObject go = new GameObject(WALL_ROOT_NAME);
                Undo.RegisterCreatedObjectUndo(go, "Create Walls Root");
                root = go.transform;
                root.SetParent(transform, false);
                root.localPosition = Vector3.zero;
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
            }

            if (!_createWalls) return;

            float halfWidth = _fieldSize.x * 0.5f;
            float halfDepth = _fieldSize.y * 0.5f;
            float halfHeight = _wallHeight * 0.5f;
            float halfThickness = WALL_THICKNESS * 0.5f;

            // 壁の内側の面がちょうどプレイ範囲の境界に来るよう、厚みの半分だけ外へずらす
            CreateWall(root, "Wall_North", new Vector3(0f, halfHeight, halfDepth + halfThickness), new Vector3(_fieldSize.x + WALL_THICKNESS * 2f, _wallHeight, WALL_THICKNESS));
            CreateWall(root, "Wall_South", new Vector3(0f, halfHeight, -halfDepth - halfThickness), new Vector3(_fieldSize.x + WALL_THICKNESS * 2f, _wallHeight, WALL_THICKNESS));
            CreateWall(root, "Wall_East", new Vector3(halfWidth + halfThickness, halfHeight, 0f), new Vector3(WALL_THICKNESS, _wallHeight, _fieldSize.y + WALL_THICKNESS * 2f));
            CreateWall(root, "Wall_West", new Vector3(-halfWidth - halfThickness, halfHeight, 0f), new Vector3(WALL_THICKNESS, _wallHeight, _fieldSize.y + WALL_THICKNESS * 2f));
        }

        /// <summary>見た目を持たない壁を1枚作る</summary>
        private void CreateWall(Transform parent, string wallName, Vector3 localPosition, Vector3 size)
        {
            var go = new GameObject(wallName);
            Undo.RegisterCreatedObjectUndo(go, "Create Wall");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
        }

        /// <summary>矩形外周を一周する距離から座標を求める</summary>
        private Vector3 GetPerimeterPoint(float distance, float halfWidth, float halfDepth)
        {
            float width = halfWidth * 2f;
            float depth = halfDepth * 2f;

            if (distance < width)
            {
                return new Vector3(-halfWidth + distance, 0f, -halfDepth);
            }
            distance -= width;

            if (distance < depth)
            {
                return new Vector3(halfWidth, 0f, -halfDepth + distance);
            }
            distance -= depth;

            if (distance < width)
            {
                return new Vector3(halfWidth - distance, 0f, halfDepth);
            }
            distance -= width;

            return new Vector3(-halfWidth, 0f, halfDepth - distance);
        }

        /// <summary>選択時にプレイ範囲と地面の範囲をギズモ表示する</summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(new Vector3(0f, _wallHeight * 0.5f, 0f), new Vector3(_fieldSize.x, _wallHeight, _fieldSize.y));

            Gizmos.color = new Color(1f, 1f, 1f, 0.4f);
            float margin = CalcGroundMarginForGizmo();
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_fieldSize.x + margin * 2f, 0f, _fieldSize.y + margin * 2f));
        }

        /// <summary>ギズモ用。ログを出さずに地面余白を求める</summary>
        private float CalcGroundMarginForGizmo()
        {
            float forestOuterEdge = _forestOffset + Mathf.Max(0, _treeRows - 1) * _rowSpacing + _positionJitter;
            return Mathf.Max(_groundMargin, forestOuterEdge + GROUND_SAFETY_MARGIN);
        }

#endif
    }
}
