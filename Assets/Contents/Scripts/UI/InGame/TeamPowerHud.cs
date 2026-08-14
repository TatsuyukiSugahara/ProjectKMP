using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.InGame
{
    /// <summary>
    /// みんなで貯めるパワーのゲージと、合体必殺の案内。
    ///
    /// 見た目はHPバーに合わせる。同じ画面に別々の作りの帯が並ぶと、
    /// 情報の重さの違いではなく『作った人の違い』が見えてしまう。
    ///
    /// 枠と溝の絵、フォント、丸いアイコンは、いずれもHPバーから借りる。
    /// 探して見つからなければ、無くても成立する形に落とす。
    ///
    /// 表示に必要なものは実行時に自分で組み立てるので、事前の配置は要らない。
    /// </summary>
    public sealed class TeamPowerHud : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private const int SORTING_ORDER = 880;

        /// <summary>
        /// 画面下端からの高さ。自分のHPバーのすぐ上に置く。
        ///
        /// 画面の上はボスの情報の場所なので、そこへ並べると敵のゲージに見える。
        /// 自分たちが貯めるものなので、自分の情報のかたまりに加える。
        /// </summary>
        private const float BOTTOM_OFFSET = 86.0f;

        private static readonly Color POWER_COLOR = new Color(1.0f, 0.78f, 0.18f, 1.0f);
        private static readonly Color TRACK_COLOR = new Color(0.28f, 0.20f, 0.10f, 1.0f);
        private static readonly Color FRAME_COLOR = new Color(1.0f, 1.0f, 1.0f, 1.0f);

        // ---- 内部状態 ------------------------------------

        private static TeamPowerHud _instance;

        private RectTransform _fill;
        private TMP_Text _ratioLabel;
        private CanvasGroup _rootGroup;
        private CanvasGroup _eventGroup;
        private TMP_Text _eventTitle;
        private TMP_Text _eventHint;
        private float _eventPulse;

        /// <summary>HPバーから借りた見た目の材料</summary>
        private static TMP_FontAsset _font;
        private static Sprite _barSprite;
        private static Sprite _circleSprite;

        // ---- 公開API -------------------------------------

        public static TeamPowerHud Ensure()
        {
            if (_instance != null) return _instance;

            var go = new GameObject(nameof(TeamPowerHud));
            _instance = go.AddComponent<TeamPowerHud>();

            return _instance;
        }

        public void SetPower(float ratio01)
        {
            float ratio = Mathf.Clamp01(ratio01);

            if (_fill != null) _fill.anchorMax = new Vector2(ratio, 1.0f);
            if (_ratioLabel == null) return;

            _ratioLabel.text = ratio >= 0.999f ? "まんたん！" : Mathf.RoundToInt(ratio * 100.0f) + "%";
        }

        public void ShowJoin(int joined, int total)
        {
            if (_eventGroup != null) _eventGroup.alpha = 1.0f;
            if (_eventTitle != null) _eventTitle.text = "みんなで ひっさつ！";

            if (_eventHint == null) return;

            _eventHint.color = Color.white;
            _eventHint.text = "こうげきボタンを おせ！   " + joined + "/" + Mathf.Max(1, total) + " にん";
        }

        public void SetLocalJoined(bool joined)
        {
            if (!joined || _eventHint == null) return;

            _eventHint.text = "さんか せいこう！　みんなを おうえん！";
            _eventHint.color = new Color(0.65f, 1.0f, 0.7f, 1.0f);
        }

        public void PlayBurst(int participants, bool isFinish)
        {
            if (_eventGroup != null) _eventGroup.alpha = 1.0f;
            if (_eventTitle != null) _eventTitle.text = isFinish ? "みんなで ドッカーン！" : "わんぱくバースト！";

            if (_eventHint == null) return;

            _eventHint.color = Color.white;
            _eventHint.text = participants + " にんの パワー！";
        }

        /// <summary>
        /// ゲージごと出し入れする。
        ///
        /// 演出の最中に出ていると、見せたい絵の上に情報が乗って邪魔になる。
        /// 操作UIと同じタイミングで消えるよう、外から呼んでもらう。
        /// </summary>
        public static void SetVisible(bool visible)
        {
            if (_instance == null || _instance._rootGroup == null) return;

            _instance._rootGroup.alpha = visible ? 1.0f : 0.0f;

            // 消している間に案内が残ると、戻したときに古い文字が出てしまう
            if (!visible) _instance.HideEvent();
        }

        public void HideEvent()
        {
            if (_eventGroup != null) _eventGroup.alpha = 0.0f;
            if (_eventTitle != null) _eventTitle.rectTransform.localScale = Vector3.one;
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }

            _instance = this;

            CollectStyle();
            Build();

            SetPower(0.0f);
            HideEvent();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (_eventGroup == null || _eventGroup.alpha <= 0.0f) return;

            _eventPulse += Time.unscaledDeltaTime;

            float pulse = 1.0f + Mathf.Sin(_eventPulse * Mathf.PI * 5.0f) * 0.055f;
            if (_eventTitle != null) _eventTitle.rectTransform.localScale = Vector3.one * pulse;
        }

        /// <summary>
        /// 画面に出ているHPバーから、フォントと枠の絵を借りる。
        /// 同じ材料を使うことが、揃って見えるための一番確実な方法。
        /// </summary>
        private static void CollectStyle()
        {
            foreach (TMP_Text text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (text == null || text.font == null) continue;

                _font = text.font;
                break;
            }

            foreach (Image image in FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (image == null || image.sprite == null) continue;

                string name = image.sprite.name;

                if (_barSprite == null && name.Contains("Bar")) _barSprite = image.sprite;
                if (_circleSprite == null && name.Contains("Circle")) _circleSprite = image.sprite;
            }
        }

        private void Build()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            _rootGroup = canvasObject.GetComponent<CanvasGroup>();
            _rootGroup.blocksRaycasts = false;
            _rootGroup.interactable = false;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);

            BuildGauge(canvasObject.transform);
            BuildEvent(canvasObject.transform);
        }

        /// <summary>HPバーと同じ『丸いアイコン＋枠＋溝＋中身』の組み立て</summary>
        private void BuildGauge(Transform parent)
        {
            var rootObject = new GameObject("PowerGauge", typeof(RectTransform));
            rootObject.transform.SetParent(parent, false);

            RectTransform root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.0f);
            root.anchorMax = new Vector2(0.5f, 0.0f);
            root.pivot = new Vector2(0.5f, 0.0f);
            root.anchoredPosition = new Vector2(0.0f, BOTTOM_OFFSET);

            // HPバーと同じ大きさにする。並んだときに幅が違うと目に付く
            root.sizeDelta = new Vector2(560.0f, 46.0f);

            BuildIcon(root);

            // 枠。アイコンのぶん右へ寄せる
            RectTransform frame = CreateImage(root, "Frame", FRAME_COLOR, _barSprite);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = new Vector2(54.0f, 0.0f);
            frame.offsetMax = Vector2.zero;

            RectTransform track = CreateImage(frame, "Track", TRACK_COLOR, _barSprite);
            track.anchorMin = Vector2.zero;
            track.anchorMax = Vector2.one;
            track.offsetMin = new Vector2(6.0f, 6.0f);
            track.offsetMax = new Vector2(-6.0f, -6.0f);

            _fill = CreateImage(track, "Fill", POWER_COLOR, _barSprite);
            _fill.anchorMin = Vector2.zero;
            _fill.anchorMax = new Vector2(0.0f, 1.0f);
            _fill.offsetMin = new Vector2(4.0f, 4.0f);
            _fill.offsetMax = new Vector2(-4.0f, -4.0f);

            _ratioLabel = CreateText(track, "Ratio", "0%", 17.0f, TextAlignmentOptions.Center);
            Stretch(_ratioLabel.rectTransform);
        }

        /// <summary>左端の丸いしるし。HPバーの『HP』と同じ形にする</summary>
        private void BuildIcon(RectTransform parent)
        {
            RectTransform icon = CreateImage(parent, "Icon", FRAME_COLOR, _circleSprite);
            icon.anchorMin = new Vector2(0.0f, 0.5f);
            icon.anchorMax = new Vector2(0.0f, 0.5f);
            icon.pivot = new Vector2(0.0f, 0.5f);
            icon.anchoredPosition = Vector2.zero;
            icon.sizeDelta = new Vector2(46.0f, 46.0f);

            RectTransform inner = CreateImage(icon, "IconInner", new Color(0.20f, 0.14f, 0.06f, 1.0f), _circleSprite);
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(3.0f, 3.0f);
            inner.offsetMax = new Vector2(-3.0f, -3.0f);

            TMP_Text label = CreateText(icon, "IconLabel", "わざ", 16.0f, TextAlignmentOptions.Center);
            label.color = POWER_COLOR;
            Stretch(label.rectTransform);
        }

        private void BuildEvent(Transform parent)
        {
            var go = new GameObject("TeamEvent", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            RectTransform root = go.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(1200.0f, 250.0f);
            root.anchoredPosition = new Vector2(0.0f, 160.0f);

            _eventGroup = go.GetComponent<CanvasGroup>();
            _eventGroup.blocksRaycasts = false;
            _eventGroup.interactable = false;

            _eventTitle = CreateText(root, "Title", string.Empty, 72.0f, TextAlignmentOptions.Center);
            _eventTitle.rectTransform.anchorMin = new Vector2(0.0f, 0.45f);
            _eventTitle.rectTransform.anchorMax = Vector2.one;
            _eventTitle.rectTransform.offsetMin = Vector2.zero;
            _eventTitle.rectTransform.offsetMax = Vector2.zero;
            _eventTitle.color = POWER_COLOR;
            AddOutline(_eventTitle);

            _eventHint = CreateText(root, "Hint", string.Empty, 42.0f, TextAlignmentOptions.Center);
            _eventHint.rectTransform.anchorMin = Vector2.zero;
            _eventHint.rectTransform.anchorMax = new Vector2(1.0f, 0.45f);
            _eventHint.rectTransform.offsetMin = Vector2.zero;
            _eventHint.rectTransform.offsetMax = Vector2.zero;
            AddOutline(_eventHint);
        }

        // ---- 部品作り ------------------------------------

        private static RectTransform CreateImage(Transform parent, string name, Color color, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            // 借りられなければ、四角のまま出す。無くても表示は成立する
            if (sprite == null) return go.GetComponent<RectTransform>();

            image.sprite = sprite;
            image.type = Image.Type.Sliced;

            return go.GetComponent<RectTransform>();
        }

        private static TMP_Text CreateText(Transform parent, string name, string value, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;

            if (_font != null) text.font = _font;

            return text;
        }

        /// <summary>背景が明るくても読めるよう、濃い縁を付ける</summary>
        private static void AddOutline(TMP_Text text)
        {
            text.fontMaterial.EnableKeyword("OUTLINE_ON");
            text.outlineWidth = 0.24f;
            text.outlineColor = new Color32(30, 12, 46, 255);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
