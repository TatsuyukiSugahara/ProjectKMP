using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.Presentation
{
    /// <summary>
    /// 画面全体を一瞬だけ光らせる。専用の Canvas を自分で作るのでシーンへの事前配置は不要。
    /// 自分の画面だけを光らせたいときに使う(全員の画面が光ると見づらいため、呼び出す側で絞る)。
    /// </summary>
    public class ScreenFlash : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>他のUIより手前に出すための描画順</summary>
        private const int SORTING_ORDER = 9000;

        // ---- 内部状態 ------------------------------------

        private static ScreenFlash _instance;

        private Image _image;
        private Color _color = Color.white;
        private float _durationSec;
        private float _elapsedSec;
        private bool _isPlaying;

        // ---- 公開API -------------------------------------

        /// <summary>画面を指定色で光らせ、duration 秒かけて消す</summary>
        public static void Play(Color color, float durationSec)
        {
            if (durationSec <= 0f) return;

            EnsureInstance();
            if (_instance != null) _instance.Begin(color, durationSec);
        }

        // ---- Unityイベント -------------------------------

        private void Update()
        {
            if (!_isPlaying) return;

            // ヒットストップ中でも同じ速さで消えるよう、実時間で数える
            _elapsedSec += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_elapsedSec / _durationSec);
            ApplyAlpha(_color.a * (1f - t));

            if (t >= 1f) _isPlaying = false;
        }

        // ---- 内部処理 ------------------------------------

        private static void EnsureInstance()
        {
            if (_instance != null) return;

            var root = new GameObject("ScreenFlash");
            DontDestroyOnLoad(root);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            var imageObject = new GameObject("Flash");
            imageObject.transform.SetParent(root.transform, false);

            var image = imageObject.AddComponent<Image>();

            // 画面いっぱいに広げ、タップやクリックの邪魔をしないようにする
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, 0f);

            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _instance = root.AddComponent<ScreenFlash>();
            _instance._image = image;
        }

        private void Begin(Color color, float durationSec)
        {
            _color = color;
            _durationSec = Mathf.Max(0.01f, durationSec);
            _elapsedSec = 0f;
            _isPlaying = true;
            ApplyAlpha(color.a);
        }

        private void ApplyAlpha(float alpha)
        {
            if (_image == null) return;

            Color color = _color;
            color.a = alpha;
            _image.color = color;
        }
    }
}
