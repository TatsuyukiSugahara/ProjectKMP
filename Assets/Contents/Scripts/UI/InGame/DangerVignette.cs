using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// HPが減ってきたら画面の縁を赤く脈打たせる。
    ///
    /// 数字のHPバーは戦っている最中は見ない。視界の端が赤く波打っていれば、
    /// 目を離さずに『まずい』と分かる。
    ///
    /// 減るほど濃く、速く打つ。段階を作らず連続で変えることで、
    /// じわじわ追い詰められる感覚が出る。
    ///
    /// 表示に必要なものは自分で組み立てるので、シーンへの事前配置は要らない。
    /// </summary>
    public class DangerVignette : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>操作UIより奥、ゲーム画面より手前に出す</summary>
        private const int SORTING_ORDER = 500;

        /// <summary>いちばん濃いときの不透明度</summary>
        private const float MAX_ALPHA = 0.45f;

        /// <summary>脈打ちの速さ(1秒あたりの回数)。HPが減るほどこの値に近づく</summary>
        private const float MAX_PULSE_HZ = 2.6f;
        private const float MIN_PULSE_HZ = 0.9f;

        /// <summary>濃さが移り変わる速さ。急に変わるとちらつく</summary>
        private const float BLEND_SPEED = 2.5f;

        // ---- 内部状態 ------------------------------------

        private static DangerVignette _instance;

        private Image _image;
        private float _danger;

        /// <summary>外から渡された危なさ。実際の濃さはここへ向かって少しずつ動く</summary>
        private float _dangerGoal;

        // ---- 公開API -------------------------------------

        /// <summary>いま出ている縁。Presenter が値を渡すのに使う</summary>
        public static DangerVignette Instance => _instance;

        /// <summary>表示を用意する。すでにあれば何もしない</summary>
        public static void Ensure()
        {
            if (_instance != null) return;

            var go = new GameObject(nameof(DangerVignette));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DangerVignette>();
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }

            _instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            float goal = ResolveDanger();

            _danger = Mathf.MoveTowards(_danger, goal, BLEND_SPEED * Time.unscaledDeltaTime);

            if (_image == null) return;

            if (_danger <= 0.001f) { _image.enabled = false; return; }

            _image.enabled = true;

            // 減るほど速く打つ。速さそのものが切迫感になる
            float hz = Mathf.Lerp(MIN_PULSE_HZ, MAX_PULSE_HZ, _danger);
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * hz * Mathf.PI * 2.0f);

            Color color = _image.color;
            _image.color = new Color(color.r, color.g, color.b, _danger * MAX_ALPHA * pulse);
        }

        /// <summary>危なさを外から設定する。0で安全、1で瀕死</summary>
        public void SetDanger(float danger01)
        {
            _dangerGoal = Mathf.Clamp01(danger01);
        }

        /// <summary>いまの危なさ</summary>
        private float ResolveDanger()
        {
            return _dangerGoal;
        }

        private void Build()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            var imageObject = new GameObject("Vignette", typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);

            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _image = imageObject.GetComponent<Image>();
            _image.raycastTarget = false;
            _image.color = new Color(0.85f, 0.08f, 0.08f, 0.0f);
            _image.sprite = CreateEdgeSprite();
            _image.enabled = false;
        }

        /// <summary>中央が透明で縁だけ濃いテクスチャを作る。画像アセットを持たずに済ませる</summary>
        private static Sprite CreateEdgeSprite()
        {
            const int SIZE = 128;
            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    // 縁からの距離を0〜1にして、外ほど濃くする
                    float edgeX = Mathf.Min(x, SIZE - 1 - x) / (SIZE * 0.5f);
                    float edgeY = Mathf.Min(y, SIZE - 1 - y) / (SIZE * 0.5f);
                    float inner = Mathf.Min(edgeX, edgeY);

                    float alpha = Mathf.Clamp01(1.0f - inner / 0.5f);
                    texture.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, alpha * alpha));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0.0f, 0.0f, SIZE, SIZE), new Vector2(0.5f, 0.5f));
        }
    }
}
