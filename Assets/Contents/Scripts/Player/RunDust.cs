using ProjectKMP.Core;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 走っている間、足元に砂埃を残す。
    ///
    /// 技を出していない時間がいちばん長いので、
    /// ただ走っているだけの場面が良くなると全体の印象が上がる。
    ///
    /// 砂埃は使い回す。1人あたり毎秒10個ほど出るので、20人なら毎秒200個。
    /// 作って捨てを繰り返すと片付けの処理が積み上がり、時々画面が引っかかる。
    /// </summary>
    public class RunDust : MonoBehaviour
    {
        // ---- 設定 ----------------------------------------

        [SerializeField, Min(0.0f), Tooltip("この速さを超えたら出しはじめる(m/秒)")]
        private float _minSpeed = 2.5f;

        [SerializeField, Min(0.02f), Tooltip("出す間隔(秒)。短いほど濃く尾を引く")]
        private float _intervalSec = 0.09f;

        [SerializeField, Min(0.05f), Tooltip("1つが消えるまでの時間(秒)")]
        private float _lifeSec = 0.45f;

        [SerializeField, Min(0.01f), Tooltip("出たときの大きさ(メートル)")]
        private float _startSize = 0.25f;

        [SerializeField, Min(1.0f), Tooltip("消えるまでに何倍まで広がるか")]
        private float _growth = 3.2f;

        [SerializeField, Tooltip("砂埃の色")]
        private Color _color = new Color(0.72f, 0.60f, 0.45f, 0.55f);

        // ---- 内部状態 ------------------------------------

        private float _timer;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;

        // ---- 内部処理 ------------------------------------

        private void Update()
        {
            // 移動の部品は操作している本人にしか付かない。
            // 他の人の犬にも砂埃を出したいので、実際に進んだ距離から速さを測る
            float speed = MeasureSpeed();

            // 止まっているときに数えていると、走り出した瞬間にまとめて出てしまう
            if (speed < _minSpeed) { _timer = 0.0f; return; }

            _timer += Time.deltaTime;
            if (_timer < _intervalSec) return;

            _timer = 0.0f;
            Spawn();
        }

        /// <summary>1フレームで進んだ距離から、いまの速さを求める。上下の動きは数えない</summary>
        private float MeasureSpeed()
        {
            Vector3 current = transform.position;

            if (!_hasLastPosition)
            {
                _lastPosition = current;
                _hasLastPosition = true;

                return 0.0f;
            }

            Vector3 delta = current - _lastPosition;
            delta.y = 0.0f;
            _lastPosition = current;

            if (Time.deltaTime <= 0.0f) return 0.0f;

            return delta.magnitude / Time.deltaTime;
        }

        private void Spawn()
        {
            // 足元の少し後ろへ置く。進んだ跡として残したい
            Vector3 position = transform.position - transform.forward * 0.3f;
            position += new Vector3(Random.Range(-0.15f, 0.15f), 0.06f, Random.Range(-0.15f, 0.15f));

            DustPuff.Play(position, _color, _lifeSec, _startSize, _startSize * _growth);
        }
    }

    /// <summary>砂埃ひとつぶん。広がりながら薄くなって消え、終わったら次の人へ回る</summary>
    public class DustPuff : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
        private static readonly int COLOR_ID = Shader.PropertyToID("_Color");

        // ---- 内部状態 ------------------------------------

        /// <summary>使い回しの置き場。全員で1つを共有する</summary>
        private static GameObjectPool _pool;

        /// <summary>形と材質は全員で共有する。1つずつ作ると数が増えるほど重くなる</summary>
        private static UnityEngine.Mesh _sharedMesh;
        private static Material _sharedMaterial;
        private static Texture2D _sharedTexture;

        private Renderer _renderer;
        private MaterialPropertyBlock _block;

        private float _elapsed;
        private float _life = 0.45f;
        private float _startSize;
        private float _endSize;
        private Color _color = Color.white;

        // ---- 公開API -------------------------------------

        /// <summary>砂埃を1つ出す。使い終わったら自分で戻る</summary>
        public static void Play(Vector3 position, Color color, float lifeSec, float startSize, float endSize)
        {
            if (_pool == null) _pool = new GameObjectPool(CreateOne, 16);

            GameObject go = _pool.Rent();
            go.transform.SetPositionAndRotation(
                position, Quaternion.Euler(90.0f, Random.Range(0.0f, 360.0f), 0.0f));

            var puff = go.GetComponent<DustPuff>();
            puff.Begin(color, lifeSec, startSize, endSize);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>1つぶんの入れ物を作る。ここは足りなくなったときだけ通る</summary>
        private static GameObject CreateOne()
        {
            var go = new GameObject("DustPuff", typeof(MeshFilter), typeof(MeshRenderer), typeof(DustPuff));

            go.GetComponent<MeshFilter>().sharedMesh = ResolveMesh();

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ResolveMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return go;
        }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        private void Begin(Color color, float lifeSec, float startSize, float endSize)
        {
            _color = color;
            _life = Mathf.Max(0.05f, lifeSec);
            _startSize = startSize;
            _endSize = endSize;
            _elapsed = 0.0f;

            Apply(0.0f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            float t = _elapsed / _life;
            if (t >= 1.0f) { _pool.Return(gameObject); return; }

            Apply(t);
        }

        private void Apply(float t)
        {
            // 最初に一気に広がってから緩む。等速だと煙ではなく円が育つだけに見える
            float eased = 1.0f - (1.0f - t) * (1.0f - t);
            transform.localScale = Vector3.one * Mathf.Lerp(_startSize, _endSize, eased);

            var color = new Color(_color.r, _color.g, _color.b, _color.a * (1.0f - t));

            // 材質は共有しているので、濃さだけを1つずつ上書きする
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BASE_COLOR_ID, color);
            _block.SetColor(COLOR_ID, color);
            _renderer.SetPropertyBlock(_block);
        }

        // ---- 共有する材料 --------------------------------

        /// <summary>板1枚ぶんの形。作りかけの部品を使うより軽い</summary>
        private static UnityEngine.Mesh ResolveMesh()
        {
            if (_sharedMesh != null) return _sharedMesh;

            _sharedMesh = new UnityEngine.Mesh { name = "DustQuad" };

            _sharedMesh.SetVertices(new System.Collections.Generic.List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0.0f), new Vector3(0.5f, -0.5f, 0.0f),
                new Vector3(0.5f, 0.5f, 0.0f), new Vector3(-0.5f, 0.5f, 0.0f),
            });

            _sharedMesh.SetUVs(0, new System.Collections.Generic.List<Vector2>
            {
                new Vector2(0.0f, 0.0f), new Vector2(1.0f, 0.0f),
                new Vector2(1.0f, 1.0f), new Vector2(0.0f, 1.0f),
            });

            _sharedMesh.SetTriangles(new int[] { 0, 2, 1, 0, 3, 2 }, 0);
            _sharedMesh.RecalculateNormals();
            _sharedMesh.RecalculateBounds();

            return _sharedMesh;
        }

        private static Material ResolveMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            _sharedMaterial = new Material(shader) { name = "DustPuff" };

            // 半透明として描く。不透明のままだと四角い板が見えてしまう
            if (_sharedMaterial.HasProperty("_Surface")) _sharedMaterial.SetFloat("_Surface", 1.0f);
            if (_sharedMaterial.HasProperty("_SrcBlend")) _sharedMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_sharedMaterial.HasProperty("_DstBlend")) _sharedMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (_sharedMaterial.HasProperty("_ZWrite")) _sharedMaterial.SetInt("_ZWrite", 0);

            _sharedMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _sharedMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            Texture2D texture = ResolveSoftTexture();
            if (_sharedMaterial.HasProperty("_BaseMap")) _sharedMaterial.SetTexture("_BaseMap", texture);
            if (_sharedMaterial.HasProperty("_MainTex")) _sharedMaterial.SetTexture("_MainTex", texture);

            return _sharedMaterial;
        }

        /// <summary>中心が濃く、縁へ向かってぼける丸。一度作ったら使い回す</summary>
        private static Texture2D ResolveSoftTexture()
        {
            if (_sharedTexture != null) return _sharedTexture;

            const int SIZE = 64;
            _sharedTexture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float dx = (x + 0.5f) / SIZE - 0.5f;
                    float dy = (y + 0.5f) / SIZE - 0.5f;

                    // 中心からの距離を 0〜1 にして、外へ行くほど薄くする
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) / 0.5f;
                    float alpha = Mathf.Clamp01(1.0f - distance);

                    // 二乗して縁を柔らかくする。そのままだと輪郭が硬く残る
                    _sharedTexture.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, alpha * alpha));
                }
            }

            _sharedTexture.Apply();
            return _sharedTexture;
        }
    }
}
