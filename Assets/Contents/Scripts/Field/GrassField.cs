using System.Collections.Generic;
using UnityEngine;

namespace ProjectKMP.Field
{
    /// <summary>
    /// 地面の上に小さな草を大量に生やす。
    /// 草は1本ずつのオブジェクトにすると重すぎるため、区画(チャンク)ごとに1枚のメッシュへまとめて描く。
    /// 区画に分けているのは、攻撃で草をなぎ倒すときに触れた区画だけ作り直せばよくするため。
    /// 生やす場所は「除外する面(土のプレイ範囲)の外側」または「生やす面(草のまだら)の内側」で、
    /// 判定を毎回やると重いので、はじめに格子状の可否表を作ってそれを引く。
    /// 配置は種(シード)から決めるので、追加の通信なしで全クライアントに同じ草が生える。
    /// </summary>
    public class GrassField : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private const string CHUNK_ROOT_NAME = "GrassBlades";

        /// <summary>草1本の形。根元2点・中間2点・先端1点</summary>
        public const int BLADE_VERTEX_COUNT = 5;

        // ---- インスペクタ設定 ------------------------------

        [Header("生やす範囲")]
        [SerializeField, Tooltip("草を生やす範囲(メートル)。このオブジェクトの中心から広がる")]
        private Vector2 _areaSize = new Vector2(60f, 60f);

        [SerializeField, Min(0f), Tooltip("1平方メートルあたりの株の数。1株から複数本の草が生える")]
        private float _clusterDensity = 5f;

        [SerializeField, Tooltip("1株から生える草の本数の範囲")]
        private Vector2Int _bladesPerCluster = new Vector2Int(4, 7);

        [SerializeField, Min(0f), Tooltip("1株の広がり(半径・メートル)。この中に草が散らばって1つの茂みに見える")]
        private float _clusterRadius = 0.07f;

        [SerializeField, Min(1f), Tooltip("1区画の大きさ(メートル)。攻撃で作り直す単位になる")]
        private float _chunkSize = 6f;

        [SerializeField, Tooltip("草の根元を地面からどれだけ浮かせるか(メートル)。地面と重なってちらつくのを防ぐ")]
        private float _baseHeight = 0.03f;

        [Header("生やす場所")]
        [SerializeField, Tooltip("ここには生やさない面(土のプレイ範囲など)。この面の内側を避ける")]
        private MeshFilter[] _excludeAreas;

        [SerializeField, Tooltip("除外した中でも、ここには生やす面(草のまだらなど)")]
        private MeshFilter[] _includeAreas;

        [SerializeField, Min(0.05f), Tooltip("生やせる場所を判定する格子の大きさ(メートル)。小さいほど境界が正確になる")]
        private float _maskCellSize = 0.25f;

        [SerializeField, Min(0f), Tooltip("除外する面の縁から、さらに何メートル草を離すか")]
        private float _excludeMargin = 0.3f;

        [Header("草の形")]
        [SerializeField, Tooltip("草の高さの範囲(メートル)")]
        private Vector2 _heightRange = new Vector2(0.25f, 0.50f);

        [SerializeField, Tooltip("草の根元の幅の範囲(メートル)")]
        private Vector2 _widthRange = new Vector2(0.045f, 0.09f);

        [SerializeField, Range(0f, 1f), Tooltip("草がもともと傾いている量。0で真っ直ぐ立つ")]
        private float _leanAmount = 0.5f;

        [SerializeField, Tooltip("草に使うマテリアル。裏面も描く設定にしておくこと")]
        private Material _material;

        [SerializeField, Tooltip("乱数シード。同じ値なら毎回同じ生え方になる")]
        private int _seed = 7777;

        [Header("なぎ倒し")]
        [SerializeField, Min(0f), Tooltip("倒れたまま起き上がらない時間(秒)。短いほど波が続けて来たときになびいて見える")]
        private float _recoverDelaySec = 0.15f;

        [SerializeField, Min(0.05f), Tooltip("倒れた草が元に戻るまでの時間(秒)。波の間隔より短くしないと倒れたままになる")]
        private float _recoverSec = 1.0f;

        [Header("生成")]
        [SerializeField, Tooltip("ゲーム開始時に自動で生やす")]
        private bool _buildOnStart = true;

        // ---- 内部状態 ------------------------------------

        private readonly List<GrassChunk> _chunks = new List<GrassChunk>();

        private bool[] _mask;
        private int _maskColumns;
        private int _maskRows;

        // ---- 公開API -------------------------------------

        /// <summary>シーンにある草原。攻撃側から草をなぎ倒すときに使う</summary>
        public static GrassField Instance { get; private set; }

        /// <summary>
        /// 指定した位置のまわりの草をなぎ倒す。攻撃が地面をなでた感じを出すのに使う。
        /// 見た目だけの処理なので、全クライアントでそれぞれ呼べば通信は要らない。
        /// </summary>
        public static void FlattenAt(Vector3 worldCenter, float radius, float strength = 1f)
        {
            if (Instance == null) return;
            Instance.Flatten(worldCenter, radius, strength);
        }

        /// <summary>広がる衝撃波に合わせて、通り過ぎた輪の中だけをなぎ倒す</summary>
        public static void FlattenRingAt(Vector3 worldCenter, float innerRadius, float outerRadius, float strength = 1f)
        {
            if (Instance == null) return;
            Instance.FlattenRing(worldCenter, innerRadius, outerRadius, strength);
        }

        /// <summary>指定した位置のまわりの草をなぎ倒す。strength は倒れの深さ(1で完全に伏せる)</summary>
        public void Flatten(Vector3 worldCenter, float radius, float strength = 1f)
        {
            FlattenRing(worldCenter, 0f, radius, strength);
        }

        /// <summary>
        /// 輪の形になぎ倒す。衝撃波が広がるのに合わせて毎フレーム呼ぶと、波が通ったところだけが倒れる。
        /// すでに倒した内側を調べ直さずに済むので、毎フレーム呼んでも軽い。
        /// </summary>
        public void FlattenRing(Vector3 worldCenter, float innerRadius, float outerRadius, float strength = 1f)
        {
            if (outerRadius <= 0f || strength <= 0f) return;

            // 区画の外接半径。これより遠い区画には1本も入らないので調べない
            float reach = _chunkSize * 0.75f + outerRadius;
            float sqrReach = reach * reach;

            for (int i = 0; i < _chunks.Count; i++)
            {
                GrassChunk chunk = _chunks[i];
                if (chunk == null) continue;

                Vector3 delta = chunk.Center - worldCenter;
                delta.y = 0f;
                if (delta.sqrMagnitude > sqrReach) continue;

                chunk.Flatten(worldCenter, innerRadius, outerRadius, _recoverDelaySec, _recoverSec, strength);
            }
        }

        /// <summary>今の設定で草を生やし直す</summary>
        [ContextMenu("草を生やす")]
        public void Rebuild()
        {
            Clear();

            if (_material == null)
            {
                Debug.LogWarning("[GrassField] マテリアルが未設定のため草を生やしませんでした");
                return;
            }

            BuildMask();

            Transform chunkRoot = CreateChild(CHUNK_ROOT_NAME, transform);

            int columns = Mathf.Max(1, Mathf.CeilToInt(_areaSize.x / _chunkSize));
            int rows = Mathf.Max(1, Mathf.CeilToInt(_areaSize.y / _chunkSize));

            float chunkWidth = _areaSize.x / columns;
            float chunkDepth = _areaSize.y / rows;

            // 生やせない場所は捨てるので、まず区画いっぱいに株の候補を撒いてから間引く
            int clusterAttempts = Mathf.Max(0, Mathf.RoundToInt(chunkWidth * chunkDepth * _clusterDensity));

            var shape = new GrassChunk.BladeShape
            {
                HeightRange = _heightRange,
                WidthRange = _widthRange,
                LeanAmount = _leanAmount,
            };

            var roots = new List<Vector3>(clusterAttempts * 6);
            var directions = new List<Vector3>(clusterAttempts * 6);
            int total = 0;

            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < columns; x++)
                {
                    var center = new Vector3(
                        -_areaSize.x * 0.5f + chunkWidth * (x + 0.5f),
                        _baseHeight,
                        -_areaSize.y * 0.5f + chunkDepth * (z + 0.5f));

                    // 区画ごとに違う種を使い、区画の境目で並びが繰り返さないようにする
                    var random = new System.Random(_seed + z * 1000 + x);

                    roots.Clear();
                    directions.Clear();

                    for (int i = 0; i < clusterAttempts; i++)
                    {
                        var local = new Vector3(
                            ((float)random.NextDouble() - 0.5f) * chunkWidth,
                            0f,
                            ((float)random.NextDouble() - 0.5f) * chunkDepth);

                        if (!IsGrassAllowed(center + local)) continue;

                        AppendCluster(local, random, roots, directions);
                    }

                    if (roots.Count == 0) continue;

                    Transform child = CreateChild("Chunk_" + x + "_" + z, chunkRoot);
                    child.localPosition = center;

                    var chunk = child.gameObject.AddComponent<GrassChunk>();
                    chunk.Build(roots.ToArray(), directions.ToArray(), _seed + z * 1000 + x, shape, _material);
                    _chunks.Add(chunk);
                    total += roots.Count;
                }
            }

            Debug.Log($"[GrassField] 草を {total} 本({_chunks.Count} 区画)生やしました");
        }

        /// <summary>生やした草をすべて消す</summary>
        [ContextMenu("草を消す")]
        public void Clear()
        {
            _chunks.Clear();

            Transform existing = transform.Find(CHUNK_ROOT_NAME);
            if (existing == null) return;

            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (_buildOnStart) Rebuild();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_areaSize.x, 0f, _areaSize.y));
        }

        /// <summary>
        /// 1株ぶんの草を足す。中心のまわりに放射状に散らし、外へ向かって開くように傾ける。
        /// 1本だけだと草に見えないので、まとまって生えているように見せるための処理。
        /// </summary>
        private void AppendCluster(Vector3 clusterCenter, System.Random random, List<Vector3> roots, List<Vector3> directions)
        {
            int minCount = Mathf.Max(1, Mathf.Min(_bladesPerCluster.x, _bladesPerCluster.y));
            int maxCount = Mathf.Max(minCount, Mathf.Max(_bladesPerCluster.x, _bladesPerCluster.y));
            int count = random.Next(minCount, maxCount + 1);

            // 開始角をずらして、株ごとに同じ形にならないようにする
            float baseAngle = (float)random.NextDouble() * Mathf.PI * 2f;

            for (int i = 0; i < count; i++)
            {
                float angle = baseAngle + Mathf.PI * 2f * (i + (float)random.NextDouble() * 0.6f - 0.3f) / count;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                // 平方根を取ると中心に固まらず、株の中で一様に散る
                float distance = Mathf.Sqrt((float)random.NextDouble()) * _clusterRadius;

                roots.Add(clusterCenter + direction * distance);
                directions.Add(direction);
            }
        }

        // ---- 内部処理: 生やせる場所の判定 -------------------

        /// <summary>除外する面と生やす面から、格子状の可否表を作る</summary>
        private void BuildMask()
        {
            _maskColumns = Mathf.Max(1, Mathf.CeilToInt(_areaSize.x / _maskCellSize));
            _maskRows = Mathf.Max(1, Mathf.CeilToInt(_areaSize.y / _maskCellSize));

            int cellCount = _maskColumns * _maskRows;
            var exclude = new bool[cellCount];
            var include = new bool[cellCount];

            RasterizeAreas(_excludeAreas, exclude);
            if (_excludeMargin > 0f) Dilate(exclude, Mathf.RoundToInt(_excludeMargin / _maskCellSize));
            RasterizeAreas(_includeAreas, include);

            _mask = new bool[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                // 生やす面の中は、除外する面の中でも優先して生やす
                _mask[i] = include[i] || !exclude[i];
            }
        }

        /// <summary>このオブジェクトを基準にした座標に草を生やしてよいか</summary>
        private bool IsGrassAllowed(Vector3 localPoint)
        {
            if (_mask == null) return true;

            int column = Mathf.FloorToInt((localPoint.x + _areaSize.x * 0.5f) / _maskCellSize);
            int row = Mathf.FloorToInt((localPoint.z + _areaSize.y * 0.5f) / _maskCellSize);

            if (column < 0 || column >= _maskColumns || row < 0 || row >= _maskRows) return false;
            return _mask[row * _maskColumns + column];
        }

        /// <summary>面のメッシュを格子に塗る</summary>
        private void RasterizeAreas(MeshFilter[] areas, bool[] cells)
        {
            if (areas == null) return;

            foreach (MeshFilter area in areas)
            {
                if (area == null || area.sharedMesh == null) continue;

                Vector3[] vertices = area.sharedMesh.vertices;
                int[] triangles = area.sharedMesh.triangles;

                // 面ごとに位置が違うので、このオブジェクトを基準にした座標へ直す
                var points = new Vector2[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 local = transform.InverseTransformPoint(area.transform.TransformPoint(vertices[i]));
                    points[i] = new Vector2(local.x, local.z);
                }

                for (int i = 0; i < triangles.Length; i += 3)
                {
                    RasterizeTriangle(points[triangles[i]], points[triangles[i + 1]], points[triangles[i + 2]], cells);
                }
            }
        }

        /// <summary>三角形1つぶんを格子に塗る</summary>
        private void RasterizeTriangle(Vector2 a, Vector2 b, Vector2 c, bool[] cells)
        {
            float halfWidth = _areaSize.x * 0.5f;
            float halfDepth = _areaSize.y * 0.5f;

            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
            float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

            int columnMin = Mathf.Clamp(Mathf.FloorToInt((minX + halfWidth) / _maskCellSize), 0, _maskColumns - 1);
            int columnMax = Mathf.Clamp(Mathf.CeilToInt((maxX + halfWidth) / _maskCellSize), 0, _maskColumns - 1);
            int rowMin = Mathf.Clamp(Mathf.FloorToInt((minY + halfDepth) / _maskCellSize), 0, _maskRows - 1);
            int rowMax = Mathf.Clamp(Mathf.CeilToInt((maxY + halfDepth) / _maskCellSize), 0, _maskRows - 1);

            for (int row = rowMin; row <= rowMax; row++)
            {
                float y = -halfDepth + (row + 0.5f) * _maskCellSize;
                for (int column = columnMin; column <= columnMax; column++)
                {
                    float x = -halfWidth + (column + 0.5f) * _maskCellSize;
                    if (!IsInsideTriangle(new Vector2(x, y), a, b, c)) continue;
                    cells[row * _maskColumns + column] = true;
                }
            }
        }

        private static bool IsInsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(point - a, b - a);
            float d2 = Cross(point - b, c - b);
            float d3 = Cross(point - c, a - c);

            bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;

            // 全ての辺に対して同じ側にあれば内側
            return !(hasNegative && hasPositive);
        }

        private static float Cross(Vector2 lhs, Vector2 rhs)
        {
            return lhs.x * rhs.y - lhs.y * rhs.x;
        }

        /// <summary>塗った範囲を指定マスぶん太らせる(縁に余白を作るため)</summary>
        private void Dilate(bool[] cells, int radius)
        {
            if (radius <= 0) return;

            var source = (bool[])cells.Clone();
            for (int row = 0; row < _maskRows; row++)
            {
                for (int column = 0; column < _maskColumns; column++)
                {
                    if (source[row * _maskColumns + column]) continue;

                    bool near = false;
                    for (int dz = -radius; dz <= radius && !near; dz++)
                    {
                        int z = row + dz;
                        if (z < 0 || z >= _maskRows) continue;

                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int x = column + dx;
                            if (x < 0 || x >= _maskColumns) continue;
                            if (!source[z * _maskColumns + x]) continue;

                            near = true;
                            break;
                        }
                    }

                    if (near) cells[row * _maskColumns + column] = true;
                }
            }
        }

        // ---- 内部処理: オブジェクト生成 ---------------------

        private Transform CreateChild(string childName, Transform parent)
        {
            var go = new GameObject(childName);

            // エディタでのプレビューはシーンに保存させない(保存すると巨大なシーンになる)
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;

            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
