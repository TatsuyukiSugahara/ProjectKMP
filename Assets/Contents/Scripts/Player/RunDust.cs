using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 走っている間、足元に砂埃を残す。
    ///
    /// 技を出していない時間がいちばん長いので、
    /// ただ走っているだけの場面が良くなると全体の印象が上がる。
    ///
    /// 地面に触れた反応があるかどうかで『重さ』が変わる。
    /// 何も出ないと、地面の上を滑っているように見えてしまう。
    ///
    /// 砂埃は自分で作って自分で消える。画像もプレハブも要らない。
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
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "RunDust";

            // 当たり判定は要らない。付いたままだと自分の足を蹴ってしまう
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            // 足元の少し後ろへ置く。進んだ跡として残したい
            Vector3 position = transform.position - transform.forward * 0.3f;
            position += new Vector3(Random.Range(-0.15f, 0.15f), 0.06f, Random.Range(-0.15f, 0.15f));

            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(90.0f, Random.Range(0.0f, 360.0f), 0.0f);
            go.transform.localScale = Vector3.one * _startSize;

            var puff = go.AddComponent<DustPuff>();
            puff.Setup(_color, _lifeSec, _startSize, _startSize * _growth);
        }
    }

    /// <summary>砂埃ひとつぶん。広がりながら薄くなって消える</summary>
    public class DustPuff : MonoBehaviour
    {
        /// <summary>ぼかした丸の絵。全員で使い回す</summary>
        private static Texture2D _softTexture;

        private Renderer _renderer;
        private Material _materialInstance;

        private float _elapsed;
        private float _life = 0.45f;
        private float _startSize;
        private float _endSize;
        private Color _color = Color.white;

        /// <summary>中心が濃く、縁へ向かってぼける丸。一度作ったら使い回す</summary>
        private static Texture2D ResolveSoftTexture()
        {
            if (_softTexture != null) return _softTexture;

            const int SIZE = 64;
            _softTexture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

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
                    _softTexture.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, alpha * alpha));
                }
            }

            _softTexture.Apply();
            return _softTexture;
        }

        public void Setup(Color color, float lifeSec, float startSize, float endSize)
        {
            _color = color;
            _life = Mathf.Max(0.05f, lifeSec);
            _startSize = startSize;
            _endSize = endSize;

            _renderer = GetComponent<Renderer>();

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            _materialInstance = new Material(shader);

            // 半透明として描く。不透明のままだと四角い板が見えてしまう
            if (_materialInstance.HasProperty("_Surface")) _materialInstance.SetFloat("_Surface", 1.0f);
            if (_materialInstance.HasProperty("_SrcBlend")) _materialInstance.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_materialInstance.HasProperty("_DstBlend")) _materialInstance.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (_materialInstance.HasProperty("_ZWrite")) _materialInstance.SetInt("_ZWrite", 0);

            _materialInstance.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _materialInstance.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // 何も貼らないと四角い板がそのまま見える。中心が濃く縁がぼけた丸を貼る
            Texture2D texture = ResolveSoftTexture();
            if (_materialInstance.HasProperty("_BaseMap")) _materialInstance.SetTexture("_BaseMap", texture);
            if (_materialInstance.HasProperty("_MainTex")) _materialInstance.SetTexture("_MainTex", texture);

            _renderer.sharedMaterial = _materialInstance;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            Apply(0.0f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            float t = _elapsed / _life;
            if (t >= 1.0f) { Destroy(gameObject); return; }

            Apply(t);
        }

        private void Apply(float t)
        {
            // 最初に一気に広がってから緩む。等速だと煙ではなく円が育つだけに見える
            float eased = 1.0f - (1.0f - t) * (1.0f - t);
            transform.localScale = Vector3.one * Mathf.Lerp(_startSize, _endSize, eased);

            var color = new Color(_color.r, _color.g, _color.b, _color.a * (1.0f - t));

            if (_materialInstance == null) return;

            if (_materialInstance.HasProperty("_BaseColor")) _materialInstance.SetColor("_BaseColor", color);
            if (_materialInstance.HasProperty("_Color")) _materialInstance.SetColor("_Color", color);
        }

        private void OnDestroy()
        {
            if (_materialInstance != null) Destroy(_materialInstance);
        }
    }
}
