using System.Collections.Generic;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// ボスの攻撃が「どこに当たるか」を溜めの間だけ地面に描く表示。
    ///
    /// 攻撃ごとに当たる形が違うので、扇形(頭突き・薙ぎ払い)・円(スタンプ・岩の着弾)・
    /// 帯(突進・破壊光線)の3種類を1つのコンポーネントで作り分ける。
    /// 全部の攻撃で同じ見た目・同じ点滅にすることで、プレイヤーは形を見ただけで
    /// 「今どこから逃げればいいか」が判断できるようになる。
    ///
    /// 見た目だけの演出なので、エフェクトと同じく全クライアントで動く処理から出せば
    /// 追加の通信なしで全員の画面に出る(ネットワーク同期は不要)。
    /// </summary>
    public class GorillaAttackTelegraph : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Min(0.0f), Tooltip("地面から浮かせる高さ(m)。ちらつき(Zファイティング)を防ぐ")]
        private float _groundOffset = 0.06f;

        [SerializeField, Min(0.02f), Tooltip("輪郭の線の太さ(m)")]
        private float _outlineThickness = 0.26f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("中の塗りの濃さ。輪郭に対する割合。0で塗らない")]
        private float _fillAlpha = 0.35f;

        [SerializeField, Min(0.0f), Tooltip("狙いが定まる前の明滅の速さ。0で明滅しない")]
        private float _pulseSpeed = 3.0f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("明滅で透明度をどれだけ揺らすか")]
        private float _pulseAmount = 0.25f;

        [SerializeField, Tooltip("狙いが固定された(もう避ける方向が変わらない)ときの色")]
        private Color _lockedColor = new Color(1.0f, 0.15f, 0.1f, 0.85f);

        [SerializeField, Min(0.0f), Tooltip("狙いが固定されたときの明滅の速さ。速いほど急かされる")]
        private float _lockedPulseSpeed = 14.0f;

        [SerializeField, Min(0.0f), Tooltip("消えるときにかける時間(秒)")]
        private float _fadeOutSec = 0.12f;

        // ---- 内部状態 ------------------------------------

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        /// <summary>円弧を何度ごとに区切るか。細かすぎても見た目は変わらないので粗めでよい</summary>
        private const float ARC_STEP_DEG = 6.0f;

        private MeshFilter _outlineFilter;
        private MeshRenderer _outlineRenderer;
        private Mesh _outlineMesh;

        private MeshFilter _fillFilter;
        private MeshRenderer _fillRenderer;
        private Mesh _fillMesh;

        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor = Color.white;
        private bool _isLocked;
        private bool _isFadingOut;
        private float _fadeElapsed;
        private bool _initialized;

        // 使い回しの作業用リスト。毎フレーム作り直さないためのもの
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();

        // ---- 公開API(生成) -------------------------------

        /// <summary>扇形(正面を中心とした範囲)の予測を出す。頭突き・薙ぎ払い用</summary>
        public static GorillaAttackTelegraph SpawnSector(
            GorillaAttackTelegraph prefab, Vector3 position, float yawDeg, float range, float angleDeg)
        {
            GorillaAttackTelegraph instance = Create(prefab, position, yawDeg);
            if (instance == null) return null;

            instance.BuildSector(Mathf.Max(0.1f, range), Mathf.Clamp(angleDeg, 1.0f, 360.0f));
            return instance;
        }

        /// <summary>円(足元や着弾地点を中心とした範囲)の予測を出す。スタンプ・岩の着弾用</summary>
        public static GorillaAttackTelegraph SpawnCircle(
            GorillaAttackTelegraph prefab, Vector3 position, float radius)
        {
            GorillaAttackTelegraph instance = Create(prefab, position, 0.0f);
            if (instance == null) return null;

            instance.BuildSector(Mathf.Max(0.1f, radius), 360.0f);
            return instance;
        }

        /// <summary>帯(正面へまっすぐ伸びる範囲)の予測を出す。突進・破壊光線用</summary>
        public static GorillaAttackTelegraph SpawnBand(
            GorillaAttackTelegraph prefab, Vector3 position, float yawDeg, float length, float width)
        {
            GorillaAttackTelegraph instance = Create(prefab, position, yawDeg);
            if (instance == null) return null;

            instance.BuildBand(Mathf.Max(0.1f, length), Mathf.Max(0.1f, width));
            return instance;
        }

        // ---- 公開API(操作) -------------------------------

        /// <summary>表示の位置と向きを合わせる。溜め中に狙いが動く攻撃では毎フレーム呼ぶ</summary>
        public void Follow(Vector3 position, float yawDeg)
        {
            transform.SetPositionAndRotation(
                position + Vector3.up * _groundOffset, Quaternion.Euler(0.0f, yawDeg, 0.0f));
        }

        /// <summary>
        /// 狙いが固定されたことを伝える。色が変わり点滅が速くなるので、
        /// プレイヤーは「もう向きは変わらない、あとは逃げるだけ」と分かる。
        /// </summary>
        public void SetLocked(bool locked)
        {
            _isLocked = locked;
        }

        /// <summary>すでに狙いが固定されているか</summary>
        public bool IsLocked => _isLocked;

        /// <summary>すっと消して自分を破棄する。攻撃が始まったタイミングで呼ぶ</summary>
        public void Dismiss()
        {
            if (_isFadingOut) return;

            _isFadingOut = true;
            _fadeElapsed = 0.0f;
        }

        /// <summary>表示を消す。null チェックを呼び出し側に書かずに済ませるための入口</summary>
        public static void Dismiss(GorillaAttackTelegraph instance)
        {
            if (instance == null) return;
            instance.Dismiss();
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            EnsureInitialized();

            if (_outlineRenderer.sharedMaterial != null && _outlineRenderer.sharedMaterial.HasProperty(BASE_COLOR_ID))
            {
                _baseColor = _outlineRenderer.sharedMaterial.GetColor(BASE_COLOR_ID);
            }
        }

        private void Update()
        {
            float fade = 1.0f;
            if (_isFadingOut)
            {
                _fadeElapsed += Time.deltaTime;
                fade = _fadeOutSec <= 0.0f ? 0.0f : Mathf.Clamp01(1.0f - _fadeElapsed / _fadeOutSec);
                if (fade <= 0.0f)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            Color color = _isLocked ? _lockedColor : _baseColor;
            float pulseSpeed = _isLocked ? _lockedPulseSpeed : _pulseSpeed;

            // 点滅は透明度だけを揺らす。形が変わらないので、範囲の読み取りを邪魔しない
            float pulse = pulseSpeed <= 0.0f
                ? 1.0f
                : 1.0f - _pulseAmount * (0.5f - 0.5f * Mathf.Cos(Time.time * pulseSpeed));

            ApplyColor(_outlineRenderer, new Color(color.r, color.g, color.b, color.a * pulse * fade));
            ApplyColor(_fillRenderer, new Color(color.r, color.g, color.b, color.a * _fillAlpha * pulse * fade));
        }

        private void OnDestroy()
        {
            if (_outlineMesh != null) Destroy(_outlineMesh);
            if (_fillMesh != null) Destroy(_fillMesh);
        }

        // ---- 内部処理(生成) ------------------------------

        private static GorillaAttackTelegraph Create(GorillaAttackTelegraph prefab, Vector3 position, float yawDeg)
        {
            if (prefab == null) return null;

            GorillaAttackTelegraph instance = Instantiate(prefab);
            instance.EnsureInitialized();
            instance.Follow(position, yawDeg);
            return instance;
        }

        /// <summary>描画に使うメッシュとレンダラーを用意する。塗り用は子オブジェクトとして作る</summary>
        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _outlineFilter = GetComponent<MeshFilter>();
            if (_outlineFilter == null) _outlineFilter = gameObject.AddComponent<MeshFilter>();

            _outlineRenderer = GetComponent<MeshRenderer>();
            if (_outlineRenderer == null) _outlineRenderer = gameObject.AddComponent<MeshRenderer>();

            _outlineMesh = new Mesh { name = "GorillaTelegraphOutline" };
            _outlineFilter.sharedMesh = _outlineMesh;

            // 塗りは輪郭より薄くしたいので、別のレンダラーに分けて色を個別に持たせる
            var fillObject = new GameObject("Fill");
            fillObject.transform.SetParent(transform, false);

            _fillFilter = fillObject.AddComponent<MeshFilter>();
            _fillRenderer = fillObject.AddComponent<MeshRenderer>();
            _fillRenderer.sharedMaterial = _outlineRenderer.sharedMaterial;
            _fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fillRenderer.receiveShadows = false;

            _outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _outlineRenderer.receiveShadows = false;

            _fillMesh = new Mesh { name = "GorillaTelegraphFill" };
            _fillFilter.sharedMesh = _fillMesh;

            _propertyBlock = new MaterialPropertyBlock();
        }

        private void ApplyColor(Renderer target, Color color)
        {
            if (target == null) return;

            target.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BASE_COLOR_ID, color);
            target.SetPropertyBlock(_propertyBlock);
        }

        // ---- 内部処理(形を作る) --------------------------

        /// <summary>
        /// 扇形を作る。角度に360を渡せば円になるので、円もこの経路で作っている。
        /// 輪郭は「外周の円弧」と、扇形のときだけ足す「両端の直線」で構成する。
        /// </summary>
        private void BuildSector(float range, float angleDeg)
        {
            bool isFullCircle = angleDeg >= 359.5f;
            float halfAngle = angleDeg * 0.5f;
            float inner = Mathf.Max(0.0f, range - _outlineThickness);

            // ---- 輪郭 ----
            _vertices.Clear();
            _triangles.Clear();
            AddArcBand(inner, range, -halfAngle, halfAngle);

            if (!isFullCircle)
            {
                // 扇形の両端(中心から外へ伸びる線)。円のときは要らない
                AddRadialEdge(range, -halfAngle);
                AddRadialEdge(range, halfAngle);
            }
            ApplyMesh(_outlineMesh);

            // ---- 塗り ----
            _vertices.Clear();
            _triangles.Clear();
            AddArcBand(0.0f, inner, -halfAngle, halfAngle);
            ApplyMesh(_fillMesh);
        }

        /// <summary>帯(正面へまっすぐ伸びる長方形)を作る</summary>
        private void BuildBand(float length, float width)
        {
            float half = width * 0.5f;
            float t = _outlineThickness;

            // ---- 輪郭(4辺) ----
            _vertices.Clear();
            _triangles.Clear();
            AddQuad(new Vector3(-half, 0.0f, 0.0f), new Vector3(-half + t, 0.0f, 0.0f),
                    new Vector3(-half + t, 0.0f, length), new Vector3(-half, 0.0f, length));
            AddQuad(new Vector3(half - t, 0.0f, 0.0f), new Vector3(half, 0.0f, 0.0f),
                    new Vector3(half, 0.0f, length), new Vector3(half - t, 0.0f, length));
            AddQuad(new Vector3(-half, 0.0f, 0.0f), new Vector3(half, 0.0f, 0.0f),
                    new Vector3(half, 0.0f, t), new Vector3(-half, 0.0f, t));
            AddQuad(new Vector3(-half, 0.0f, length - t), new Vector3(half, 0.0f, length - t),
                    new Vector3(half, 0.0f, length), new Vector3(-half, 0.0f, length));
            ApplyMesh(_outlineMesh);

            // ---- 塗り ----
            _vertices.Clear();
            _triangles.Clear();
            AddQuad(new Vector3(-half + t, 0.0f, t), new Vector3(half - t, 0.0f, t),
                    new Vector3(half - t, 0.0f, length - t), new Vector3(-half + t, 0.0f, length - t));
            ApplyMesh(_fillMesh);
        }

        /// <summary>内半径から外半径までの円弧の帯を足す。内半径0なら扇形の面になる</summary>
        private void AddArcBand(float innerRadius, float outerRadius, float startDeg, float endDeg)
        {
            if (outerRadius <= innerRadius) return;

            int steps = Mathf.Max(1, Mathf.CeilToInt((endDeg - startDeg) / ARC_STEP_DEG));
            for (int i = 0; i < steps; i++)
            {
                float a0 = Mathf.Lerp(startDeg, endDeg, i / (float)steps);
                float a1 = Mathf.Lerp(startDeg, endDeg, (i + 1) / (float)steps);

                Vector3 innerA = OnCircle(a0, innerRadius);
                Vector3 innerB = OnCircle(a1, innerRadius);
                Vector3 outerA = OnCircle(a0, outerRadius);
                Vector3 outerB = OnCircle(a1, outerRadius);

                AddQuad(innerA, outerA, outerB, innerB);
            }
        }

        /// <summary>扇形の端の直線(中心から外周まで)を足す</summary>
        private void AddRadialEdge(float range, float angleDeg)
        {
            Vector3 direction = OnCircle(angleDeg, 1.0f);
            Vector3 side = new Vector3(direction.z, 0.0f, -direction.x) * (_outlineThickness * 0.5f);

            AddQuad(-side, side, direction * range + side, direction * range - side);
        }

        /// <summary>正面(+Z)を0度として、指定した角度・半径の位置を返す</summary>
        private static Vector3 OnCircle(float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad) * radius, 0.0f, Mathf.Cos(rad) * radius);
        }

        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int index = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertices.Add(d);

            _triangles.Add(index);
            _triangles.Add(index + 2);
            _triangles.Add(index + 1);
            _triangles.Add(index);
            _triangles.Add(index + 3);
            _triangles.Add(index + 2);
        }

        private void ApplyMesh(Mesh mesh)
        {
            if (mesh == null) return;

            var normals = new Vector3[_vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;

            mesh.Clear();
            mesh.SetVertices(_vertices);
            mesh.SetTriangles(_triangles, 0);
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }
    }
}
