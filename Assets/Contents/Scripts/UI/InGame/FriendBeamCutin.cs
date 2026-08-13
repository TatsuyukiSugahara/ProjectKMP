using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 友達ビームが成立したときの表示。
    ///
    /// 画面の四辺だけを金色に強く燃やし、隅に小さく人数と名前を出す。
    /// 中央には何も置かない。合体ビーム本体がいちばん見たいものなので、
    /// そこを塞ぐ表示は演出ではなく妨害になってしまう。
    ///
    /// 狙い中に出る合図(FriendBeamSignal)も画面端を薄く光らせているので、
    /// 『合図が強く燃え上がった』という地続きの見え方になる。
    ///
    /// 撃っている最中に出るものなので、時間は止めない。
    /// 表示に必要なものは自分で組み立てるので、シーンへの事前配置は要らない。
    /// </summary>
    public class FriendBeamCutin : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>合図より手前、必殺技のカットインより手前に出す</summary>
        private const int SORTING_ORDER = 950;

        /// <summary>全体のうち、燃え上がるのに使う割合</summary>
        private const float IGNITE_RATIO = 0.12f;

        /// <summary>全体のうち、消え始める割合</summary>
        private const float FADE_OUT_RATIO = 0.7f;

        /// <summary>隅の表示が滑り込んでくる距離(px)</summary>
        private const float SLIDE_X = 320.0f;

        /// <summary>枠の脈打ちの速さ(1秒あたりの回数)</summary>
        private const float PULSE_HZ = 5.0f;

        // ---- 内部状態 ------------------------------------

        private static FriendBeamCutin _instance;

        private CanvasGroup _group;
        private Image _frame;
        private RectTransform _corner;
        private Text _title;
        private Text _names;

        private bool _playing;
        private float _elapsed;
        private float _duration = 0.9f;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 表示を出す。合体できた本人の画面だけで呼ぶこと。
        /// leftName / rightName には合わせた人の名前を入れる。
        /// </summary>
        public static void Play(string leftName, string rightName, int members, Color color, float durationSec)
        {
            Ensure();
            if (_instance == null) return;

            _instance.Begin(leftName, rightName, members, color, durationSec);
        }

        /// <summary>表示を用意する。すでにあれば何もしない</summary>
        public static void Ensure()
        {
            if (_instance != null) return;

            var go = new GameObject(nameof(FriendBeamCutin));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FriendBeamCutin>();
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }

            _instance = this;
            Build();
            SetVisible(0.0f);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Begin(string leftName, string rightName, int members, Color color, float durationSec)
        {
            _duration = Mathf.Max(0.2f, durationSec);
            _elapsed = 0.0f;
            _playing = true;

            if (_title != null)
            {
                _title.text = "FRIEND BEAM ×" + members;
                _title.color = color;
            }

            if (_names != null) _names.text = leftName + " ＋ " + rightName;
            if (_frame != null) _frame.color = new Color(color.r, color.g, color.b, 0.0f);

            SetVisible(1.0f);
        }

        private void Update()
        {
            if (!_playing) return;

            // 撃っている最中に出るので、ヒットストップで時間が落ちていても実時間で進める
            _elapsed += Time.unscaledDeltaTime;

            float t = _elapsed / _duration;
            if (t >= 1.0f) { _playing = false; SetVisible(0.0f); return; }

            float strength = Strength(t);
            UpdateFrame(strength);
            UpdateCorner(t, strength);
        }

        /// <summary>燃え上がって、しばらく保って、引いていく</summary>
        private static float Strength(float t)
        {
            if (t < IGNITE_RATIO) return t / IGNITE_RATIO;
            if (t < FADE_OUT_RATIO) return 1.0f;

            return 1.0f - (t - FADE_OUT_RATIO) / (1.0f - FADE_OUT_RATIO);
        }

        /// <summary>四辺を燃やす。脈打たせて『燃えている』ことを伝える</summary>
        private void UpdateFrame(float strength)
        {
            if (_frame == null) return;

            float pulse = 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * PULSE_HZ * Mathf.PI * 2.0f);

            Color color = _frame.color;
            _frame.color = new Color(color.r, color.g, color.b, strength * pulse * 0.85f);
        }

        /// <summary>隅の表示を横から滑り込ませる</summary>
        private void UpdateCorner(float t, float strength)
        {
            if (_corner == null) return;

            // 減速しながら入る。等速だと差し込まれた勢いが出ない
            float k = Mathf.Clamp01(t / IGNITE_RATIO);
            float slide = Mathf.Lerp(-SLIDE_X, 0.0f, 1.0f - (1.0f - k) * (1.0f - k) * (1.0f - k));

            _corner.anchoredPosition = new Vector2(60.0f + slide, -60.0f);

            if (_group != null) _group.alpha = Mathf.Max(strength, 0.0f);
        }

        private void SetVisible(float alpha)
        {
            if (_group != null) _group.alpha = alpha;
            if (_frame != null)
            {
                Color color = _frame.color;
                _frame.color = new Color(color.r, color.g, color.b, alpha <= 0.0f ? 0.0f : color.a);
            }
        }

        // ---- 組み立て ------------------------------------

        private void Build()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);

            _group = canvasObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _frame = CreateFrame(canvasObject.transform);
            _corner = CreateCorner(canvasObject.transform, out _title, out _names);
        }

        /// <summary>四辺だけが濃い枠。中央は完全に透明なので、ビームを一切隠さない</summary>
        private static Image CreateFrame(Transform parent)
        {
            var go = new GameObject("Frame", typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = new Color(1.0f, 0.85f, 0.35f, 0.0f);
            image.sprite = CreateFrameSprite();

            return image;
        }

        /// <summary>縁から内側へ向かって薄くなるテクスチャ。画像アセットを持たずに済ませる</summary>
        private static Sprite CreateFrameSprite()
        {
            const int SIZE = 128;

            // 合図(薄い発光)より内側まで届かせる。合図が燃え上がったように見せたい
            const float REACH = 0.32f;

            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float edgeX = Mathf.Min(x, SIZE - 1 - x) / (SIZE * 0.5f);
                    float edgeY = Mathf.Min(y, SIZE - 1 - y) / (SIZE * 0.5f);
                    float inner = Mathf.Min(edgeX, edgeY);

                    // 縁に近いほど濃く。三乗にして中央側をきっぱり抜く
                    float alpha = Mathf.Clamp01(1.0f - inner / REACH);
                    alpha = alpha * alpha * alpha;

                    texture.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, alpha));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0.0f, 0.0f, SIZE, SIZE), new Vector2(0.5f, 0.5f));
        }

        /// <summary>左上の隅に置く小さな表示。人数と、合わせた相手の名前</summary>
        private static RectTransform CreateCorner(Transform parent, out Text title, out Text names)
        {
            var go = new GameObject("Corner");
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(0.0f, 1.0f);
            rect.pivot = new Vector2(0.0f, 1.0f);
            rect.sizeDelta = new Vector2(700.0f, 120.0f);

            title = CreateText(rect, "Title", 46, FontStyle.BoldAndItalic, new Vector2(0.0f, 0.0f), 56.0f);
            names = CreateText(rect, "Names", 30, FontStyle.Bold, new Vector2(0.0f, -54.0f), 44.0f);
            names.color = new Color(1.0f, 0.95f, 0.8f, 1.0f);

            return rect;
        }

        private static Text CreateText(
            Transform parent, string objectName, int fontSize, FontStyle style, Vector2 position, float height)
        {
            var go = new GameObject(objectName, typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(0.0f, 1.0f);
            rect.pivot = new Vector2(0.0f, 1.0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(700.0f, height);

            var text = go.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.05f, 0.0f, 0.95f);
            outline.effectDistance = new Vector2(3.0f, -3.0f);

            return text;
        }
    }
}
