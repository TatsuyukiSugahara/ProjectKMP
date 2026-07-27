using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProjectKMP.Field
{
    /// <summary>
    /// ゲームの1フィールドを構成する「地面・外周の林・見えない壁」をまとめて生成する。
    /// プレイ範囲(_fieldSize)の外側に林を置き、地面は林を覆うところまで広げるため、
    /// 木が地面からはみ出すことはない。インスペクタのコンポーネント右クリックメニューから
    /// 「フィールドを再構築」で作り直す。エディタ専用。
    /// </summary>
    public class FieldBuilder : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>Unity の Plane プリミティブはスケール1あたり10m四方</summary>
        private const float PLANE_UNIT_SIZE = 10f;

        /// <summary>林の外側に確保する地面の余白(メートル)</summary>
        private const float GROUND_SAFETY_MARGIN = 8f;

        private const string GROUND_NAME = "Ground";
        private const string TREE_ROOT_NAME = "Trees";
        private const string WALL_ROOT_NAME = "Walls";
        private const float WALL_THICKNESS = 1f;

        // ---- インスペクタ設定 ------------------------------

        [Header("プレイ範囲")]
        [SerializeField, Tooltip("プレイヤーが動ける範囲(メートル)。見えない壁の内側の広さ")]
        private Vector2 _fieldSize = new Vector2(200f, 200f);

        [Header("地面")]
        [SerializeField, Tooltip("プレイ範囲の外側に伸ばす地面の幅(メートル)。林を覆うぶんは自動で確保される")]
        private float _groundMargin = 40f;

        [SerializeField, Tooltip("地面に貼るマテリアル")]
        private Material _groundMaterial;

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

        /// <summary>現在の設定値で地面・林・壁を作り直す</summary>
        [ContextMenu("フィールドを再構築")]
        public void Rebuild()
        {
#if UNITY_EDITOR
            float groundMargin = CalcGroundMargin();
            BuildGround(groundMargin);
            int treeCount = BuildTrees();
            BuildWalls();
            Debug.Log($"[Field] 再構築しました: プレイ範囲 {_fieldSize.x}m x {_fieldSize.y}m / 地面 {_fieldSize.x + groundMargin * 2f}m x {_fieldSize.y + groundMargin * 2f}m / 木 {treeCount} 本({_treeRows}列) / 壁 {(_createWalls ? "あり" : "なし")}");
#else
            Debug.LogWarning("[Field] Rebuild はエディタ専用です");
#endif
        }

#if UNITY_EDITOR

        // ---- 内部処理 ------------------------------------

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
        }

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
