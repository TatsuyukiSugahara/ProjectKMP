using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.Presentation
{
    /// <summary>
    /// 倒したときの締めの演出。
    ///
    /// 必殺技と同じカットインで、犬が横からガブっと噛みつく。
    /// そのあと『がぶっと』『バスター』が飛び込んでくる。
    ///
    /// 帯や下じきは敷かない。必殺技のカットインと同じ見え方に揃えることで、
    /// 遊んでいる間に見慣れた絵のまま締められる。
    ///
    /// タイトルの題字を最後にもう一度見せて、遊び終わりを締める。
    /// </summary>
    public class FinishCutin : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private const int SORTING_ORDER = 950;

        private static readonly Color32 OUTLINE = new Color32(24, 58, 30, 255);

        /// <summary>題字の傾き。まっすぐだと止まって見える</summary>
        private const float TILT = -8.0f;

        /// <summary>カットインを見せる時間(秒)</summary>
        private const float CUTIN_SEC = 1.1f;

        // ---- 内部状態 ------------------------------------

        private static FinishCutin _instance;
        private static TMP_FontAsset _sharedFont;

        private Canvas _canvas;
        private RectTransform _shakeRoot;

        private Image _biteUpper;
        private Image _biteLower;

        private Image _fangTop;
        private Image _fangBottom;

        private TMP_Text _wordFirst;
        private TMP_Text _wordSecond;

        private Vector2 _biteAnchor;
        private float _shakeAmount;

        // ---- 公開API -------------------------------------

        /// <summary>締めの演出を流す。倒した相手の位置を渡すと、そこに牙の跡が出る</summary>
        public static void Play(Vector3 bossPosition, CancellationToken ct)
        {
            Ensure();
            if (_instance == null) return;

            _instance.PlayAsync(bossPosition, ct).Forget();
        }

        /// <summary>出ているものを片付ける</summary>
        public static void Clear()
        {
            if (_instance == null) return;

            _instance.HideAll();
        }

        private static void Ensure()
        {
            if (_instance != null) return;

            var go = new GameObject(nameof(FinishCutin));
            _instance = go.AddComponent<FinishCutin>();
        }

        // ---- 流れ ----------------------------------------

        private async UniTaskVoid PlayAsync(Vector3 bossPosition, CancellationToken ct)
        {
            HideAll();

            // ================= 牙の跡が食い込む =================
            await Wait(0.05f, ct);

            PlaceBite(bossPosition);

            _biteUpper.gameObject.SetActive(true);
            _biteLower.gameObject.SetActive(true);

            await Animate(0.12f, t =>
            {
                // 上下から挟み込む。離れた位置から一気に閉じる
                float gap = Mathf.Lerp(240.0f, 0.0f, t * t);

                _biteUpper.rectTransform.anchoredPosition = _biteAnchor + new Vector2(0.0f, gap);
                _biteLower.rectTransform.anchoredPosition = _biteAnchor - new Vector2(0.0f, gap);

                SetAlpha(_biteUpper, t);
                SetAlpha(_biteLower, t);
            }, ct);

            ImpactFrame.Play(new Color(1.0f, 1.0f, 1.0f, 0.8f), 0.05f);
            Shake(24.0f);

            // ================= カットインで噛みつく =================
            // 向きと位置はカットイン側の設定で決める。
            // 画角に合わせた微調整なので、インスペクタで見ながら直せるほうがよい
            SkillCutin.PlayFinish(CUTIN_SEC);

            await Wait(0.28f, ct);

            // ================= 『がぶっと』 =================
            _wordFirst.gameObject.SetActive(true);

            await Animate(0.26f, t =>
            {
                // 行きすぎてから戻る。飛び込んできた勢いが出る
                float x = t < 0.75f
                    ? Mathf.Lerp(-1800.0f, 40.0f, EaseOut(t / 0.75f))
                    : Mathf.Lerp(40.0f, -300.0f, (t - 0.75f) / 0.25f);

                MoveWord(_wordFirst, new Vector2(x, 230.0f), Mathf.Lerp(1.35f, 1.0f, EaseOut(t)), Mathf.Clamp01(t * 5.0f));
            }, ct);

            Shake(16.0f);

            // ================= 牙が噛み合う =================
            _fangTop.gameObject.SetActive(true);
            _fangBottom.gameObject.SetActive(true);

            await Animate(0.15f, t =>
            {
                // 加速しながら閉じる。等速だと噛んだ感じにならない
                float eased = t * t;

                _fangTop.rectTransform.anchoredPosition = new Vector2(0.0f, Mathf.Lerp(980.0f, 350.0f, eased));
                _fangBottom.rectTransform.anchoredPosition = new Vector2(0.0f, Mathf.Lerp(-980.0f, -350.0f, eased));
            }, ct);

            ImpactFrame.Play(new Color(1.0f, 1.0f, 1.0f, 0.95f), 0.07f);
            Shake(44.0f);

            // ================= 『バスター』 =================
            _wordSecond.gameObject.SetActive(true);

            await Animate(0.3f, t =>
            {
                // 逆から入れる。左右から挟むと画面が締まる
                float x = t < 0.7f
                    ? Mathf.Lerp(2000.0f, -40.0f, EaseOut(t / 0.7f))
                    : Mathf.Lerp(-40.0f, 220.0f, (t - 0.7f) / 0.3f);

                MoveWord(_wordSecond, new Vector2(x, -110.0f), Mathf.Lerp(1.5f, 1.0f, EaseOut(t)), Mathf.Clamp01(t * 5.0f));

                // 題字が入るのと入れ替わりに、牙が引いていく
                float retract = EaseOut(t);

                _fangTop.rectTransform.anchoredPosition = new Vector2(0.0f, Mathf.Lerp(350.0f, 980.0f, retract));
                _fangBottom.rectTransform.anchoredPosition = new Vector2(0.0f, Mathf.Lerp(-350.0f, -980.0f, retract));

                SetAlpha(_biteUpper, 1.0f - t);
                SetAlpha(_biteLower, 1.0f - t);
            }, ct);

            _fangTop.gameObject.SetActive(false);
            _fangBottom.gameObject.SetActive(false);
            _biteUpper.gameObject.SetActive(false);
            _biteLower.gameObject.SetActive(false);

            // ================= 題字がひと弾み =================
            await Animate(0.32f, t =>
            {
                float bounce = 1.0f + Mathf.Sin(t * Mathf.PI) * 0.07f;

                _wordFirst.rectTransform.localScale = Vector3.one * bounce;
                _wordSecond.rectTransform.localScale = Vector3.one * bounce;
            }, ct);

            // ================= 題字を引かせる =================
            // このあと『ゲームクリア』の表示へ移る。
            // 題字を出したままだと、二つの見せ場が重なって散らかる
            await Wait(0.35f, ct);

            Vector2 firstHome = _wordFirst.rectTransform.anchoredPosition;
            Vector2 secondHome = _wordSecond.rectTransform.anchoredPosition;

            await Animate(0.22f, t =>
            {
                float eased = t * t;

                // 入ってきたのと同じ向きへ抜ける。行き来が揃うと流れが読みやすい
                MoveWord(_wordFirst, firstHome + new Vector2(-2000.0f * eased, 0.0f), 1.0f, 1.0f - t);
                MoveWord(_wordSecond, secondHome + new Vector2(2200.0f * eased, 0.0f), 1.0f, 1.0f - t);
            }, ct);

            _wordFirst.gameObject.SetActive(false);
            _wordSecond.gameObject.SetActive(false);
        }

        // ---- 揺れ ----------------------------------------

        private void Shake(float amount)
        {
            _shakeAmount = amount;
        }

        private void Update()
        {
            if (_shakeRoot == null) return;

            if (_shakeAmount <= 0.01f)
            {
                _shakeRoot.anchoredPosition = Vector2.zero;
                return;
            }

            _shakeRoot.anchoredPosition = new Vector2(
                UnityEngine.Random.Range(-_shakeAmount, _shakeAmount),
                UnityEngine.Random.Range(-_shakeAmount, _shakeAmount));

            _shakeAmount = Mathf.Lerp(_shakeAmount, 0.0f, Time.unscaledDeltaTime * 12.0f);
        }

        // ---- 組み立て ------------------------------------

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

        private void Build()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SORTING_ORDER;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);

            var shakeObject = new GameObject("Shake", typeof(RectTransform));
            shakeObject.transform.SetParent(canvasObject.transform, false);

            _shakeRoot = shakeObject.GetComponent<RectTransform>();
            _shakeRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _shakeRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _shakeRoot.sizeDelta = Vector2.zero;

            // ボスに残る牙の跡。攻撃ボタンと同じ絵を使う
            _biteUpper = CreateImage(_shakeRoot, "BiteUpper", new Vector2(460.0f, 150.0f));

            _biteLower = CreateImage(_shakeRoot, "BiteLower", new Vector2(460.0f, 150.0f));
            _biteLower.rectTransform.localScale = new Vector3(1.0f, -1.0f, 1.0f);

            // 画面いっぱいの牙。攻撃ボタンと同じ絵をそのまま大きく使う
            _fangTop = CreateFang(_shakeRoot, true);
            _fangBottom = CreateFang(_shakeRoot, false);

            _wordFirst = CreateWord(_shakeRoot, "がぶっと", 170.0f);
            _wordSecond = CreateWord(_shakeRoot, "バスター", 250.0f);


            HideAll();
        }

        private void HideAll()
        {
            foreach (Component target in new Component[]
            {
                _biteUpper, _biteLower, _fangTop, _fangBottom, _wordFirst, _wordSecond,
            })
            {
                if (target != null) target.gameObject.SetActive(false);
            }

            _shakeAmount = 0.0f;
        }

        private static Image CreateImage(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.sprite = FinishCutinArt.Fang();
            image.raycastTarget = false;

            // 傷跡として残るので、白ではなく濃い色で焼き付ける
            image.color = new Color(0.11f, 0.24f, 0.13f, 1.0f);

            return image;
        }

        /// <summary>画面いっぱいの牙。下あごは裏返して使う</summary>
        private static Image CreateFang(Transform parent, bool top)
        {
            Image image = CreateImage(parent, top ? "FangTop" : "FangBottom", new Vector2(2700.0f, 860.0f));
            image.color = Color.white;

            image.rectTransform.anchoredPosition = new Vector2(0.0f, top ? 980.0f : -980.0f);
            image.rectTransform.localScale = new Vector3(1.0f, top ? 1.0f : -1.0f, 1.0f);

            return image;
        }

        private TMP_Text CreateWord(Transform parent, string label, float size)
        {
            var go = new GameObject("Word_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1600.0f, 340.0f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold | FontStyles.Italic;
            text.color = Color.white;
            text.raycastTarget = false;

            TMP_FontAsset font = RuntimeFont.Japanese();
            if (font != null) text.font = font;

            // 下じきを敷かないぶん、縁を厚くして背景から浮かせる
            text.fontMaterial.EnableKeyword("OUTLINE_ON");
            text.outlineWidth = 0.35f;
            text.outlineColor = OUTLINE;

            return text;
        }

        // ---- 動かす --------------------------------------

        private static void MoveWord(TMP_Text word, Vector2 position, float scale, float alpha)
        {
            if (word == null) return;

            word.rectTransform.anchoredPosition = position;
            word.rectTransform.localScale = Vector3.one * scale;
            word.rectTransform.localRotation = Quaternion.Euler(0.0f, 0.0f, TILT);

            Color color = word.color;
            word.color = new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>倒した相手の位置へ、牙の跡を置く</summary>
        private void PlaceBite(Vector3 worldPosition)
        {
            Camera camera = Camera.main;

            if (camera == null) { _biteAnchor = Vector2.zero; return; }

            Vector3 screen = camera.WorldToScreenPoint(worldPosition + Vector3.up * 2.0f);

            if (screen.z < 0.0f) { _biteAnchor = Vector2.zero; return; }

            var scaler = _canvas.GetComponent<CanvasScaler>();
            float scale = scaler != null && Screen.width > 0
                ? scaler.referenceResolution.x / Screen.width
                : 1.0f;

            _biteAnchor = new Vector2(
                (screen.x - Screen.width * 0.5f) * scale,
                (screen.y - Screen.height * 0.5f) * scale);
        }

        // ---- フォント ------------------------------------

        /// <summary>日本語が出せるフォントを探す。英字だけのものを掴むと題字が化ける</summary>
        private static TMP_FontAsset ResolveJapaneseFont()
        {
            if (_sharedFont != null) return _sharedFont;

            TMP_FontAsset firstFound = null;

            foreach (TMP_Text sample in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (sample == null || sample.font == null) continue;

                if (firstFound == null) firstFound = sample.font;
                if (!HasJapanese(sample.font)) continue;

                _sharedFont = sample.font;
                return _sharedFont;
            }

            _sharedFont = firstFound != null ? firstFound : TMP_Settings.defaultFontAsset;

            return _sharedFont;
        }

        /// <summary>題字の文字を出せるか。焼き込み済みだけでなく、フォント全体から探す</summary>
        private static bool HasJapanese(TMP_FontAsset font)
        {
            return font.HasCharacter('が', true, true)
                && font.HasCharacter('ぶ', true, true)
                && font.HasCharacter('タ', true, true);
        }

        // ---- 補助 ----------------------------------------

        private static async UniTask Wait(float seconds, CancellationToken ct)
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
        }

        private static async UniTask Animate(float durationSec, Action<float> apply, CancellationToken ct)
        {
            float elapsed = 0.0f;

            while (elapsed < durationSec)
            {
                elapsed += Time.unscaledDeltaTime;
                apply(Mathf.Clamp01(elapsed / durationSec));

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            apply(1.0f);
        }

        private static float EaseOut(float t)
        {
            return 1.0f - (1.0f - t) * (1.0f - t);
        }

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            if (graphic == null) return;

            Color color = graphic.color;
            graphic.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        }
    }
}
