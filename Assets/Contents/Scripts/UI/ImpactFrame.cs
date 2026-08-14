using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 決めの一撃で、ほんの数フレームだけ画面を塗りつぶす。
    ///
    /// アニメの『決めゴマ』にあたるもの。0.03秒ほどしか出ないので、
    /// 見ている人は絵として認識せず『重い一撃だった』とだけ感じる。
    ///
    /// 画面フラッシュ(ScreenFlash)との違いは、薄く光らせるのではなく
    /// 一瞬だけ完全に塗りつぶすところ。だからこそ打撃の切れ目が立つ。
    ///
    /// ヒットストップで時間が止まっている最中に出すものなので、実時間で数える。
    /// </summary>
    public class ImpactFrame : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>他のどの表示よりも手前に出す</summary>
        private const int SORTING_ORDER = 1000;

        // ---- 内部状態 ------------------------------------

        private static ImpactFrame _instance;

        private Image _image;
        private float _remainSec;
        private float _durationSec;
        private Color _color = Color.white;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 一瞬だけ画面を塗る。
        /// durationSec は 0.03〜0.06 くらいが目安。長くすると『見えて』しまい、ただの目潰しになる。
        /// </summary>
        public static void Play(Color color, float durationSec = 0.04f)
        {
            Ensure();
            if (_instance == null) return;

            _instance.Begin(color, durationSec);
        }

        /// <summary>
        /// 白でひとコマ。intensity は濃さで、1.0 で真っ白。
        /// 何度も出る技ほど薄くしないと、連打で目が痛くなる。
        /// </summary>
        public static void PlayWhite(float durationSec = 0.04f, float intensity = 1.0f)
        {
            Play(new Color(1.0f, 1.0f, 1.0f, Mathf.Clamp01(intensity)), durationSec);
        }

        /// <summary>表示を用意する。すでにあれば何もしない</summary>
        public static void Ensure()
        {
            if (_instance != null) return;

            var go = new GameObject(nameof(ImpactFrame));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ImpactFrame>();
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

        private void Begin(Color color, float durationSec)
        {
            _color = color;
            _durationSec = Mathf.Max(0.01f, durationSec);
            _remainSec = _durationSec;

            Apply(1.0f);
        }

        private void Update()
        {
            if (_remainSec <= 0.0f) return;

            // ヒットストップ中に出すので、止まった時間ではなく実時間で数える
            _remainSec -= Time.unscaledDeltaTime;

            if (_remainSec <= 0.0f) { Apply(0.0f); return; }

            // 終わりぎわだけ一気に抜く。だらだら薄くすると『光った』に見えてしまう
            float ratio = _remainSec / _durationSec;
            Apply(ratio > 0.35f ? 1.0f : ratio / 0.35f);
        }

        private void Apply(float alpha)
        {
            if (_image == null) return;

            // 指定された色の透明度を上限として使う。技ごとに眩しさを変えられる
            _image.color = new Color(_color.r, _color.g, _color.b, alpha * _color.a);
            _image.enabled = alpha > 0.001f;
        }

        private void Build()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            var imageObject = new GameObject("Fill", typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);

            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _image = imageObject.GetComponent<Image>();
            _image.raycastTarget = false;
            _image.enabled = false;
        }
    }
}
