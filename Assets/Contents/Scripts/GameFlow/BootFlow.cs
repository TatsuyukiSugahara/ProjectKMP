using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProjectKMP.GameFlow
{
    /// <summary>
    /// 起動直後にロゴを順番に見せる画面。
    /// 1枚あたり「フェードイン → 表示 → フェードアウト」で、合計が表示秒数になるよう配分する。
    /// 出てから一定時間が過ぎると、なにか押せば次へ飛ばせる(出た瞬間に飛ばせるとロゴが読めないため)。
    /// 全部見せ終わったらタイトルへ移る。
    /// </summary>
    public class BootFlow : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("参照")]
        [SerializeField, Tooltip("ロゴを表示する Image")]
        private Image _logoImage;

        [SerializeField, Tooltip("フェードに使う CanvasGroup")]
        private CanvasGroup _logoGroup;

        [Header("ロゴ")]
        [SerializeField, Tooltip("上から順に表示する")]
        private Sprite[] _logos = new Sprite[0];

        [Header("時間")]
        [SerializeField, Min(0.1f), Tooltip("ロゴ1枚を見せる合計秒数(フェードを含む)")]
        private float _durationSec = 3.0f;

        [SerializeField, Min(0.0f), Tooltip("浮かび上がるのにかける秒数")]
        private float _fadeInSec = 0.6f;

        [SerializeField, Min(0.0f), Tooltip("消えるのにかける秒数")]
        private float _fadeOutSec = 0.6f;

        [SerializeField, Min(0.0f), Tooltip("表示してから何秒後にスキップを受け付けるか")]
        private float _skipDelaySec = 1.5f;

        [SerializeField, Min(0.05f), Tooltip("スキップしたときに消えるまでの秒数。待たせないよう短くする")]
        private float _skipFadeOutSec = 0.2f;

        [SerializeField, Min(0.0f), Tooltip("ロゴとロゴのあいだの間(秒)")]
        private float _intervalSec = 0.25f;

        [Header("遷移")]
        [SerializeField, Tooltip("画面全体を覆う幕。タイトルへ移る直前に出して、切り替わりの瞬間を隠す")]
        private Image _curtainImage;

        [SerializeField, Min(0.05f), Tooltip("幕を出すのにかける秒数")]
        private float _curtainFadeSec = 0.5f;

        [SerializeField, Tooltip("すべて見せ終わったあとに読み込むシーン名")]
        private string _nextSceneName = "Title";

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            if (_logoGroup != null) _logoGroup.alpha = 0.0f;
            SetCurtainAlpha(0.0f);

            RunAsync(destroyCancellationToken).Forget();
        }

        // ---- 内部処理 ------------------------------------

        private async UniTaskVoid RunAsync(CancellationToken ct)
        {
            try
            {
                foreach (Sprite logo in _logos)
                {
                    if (logo == null) continue;

                    await ShowLogoAsync(logo, ct);

                    if (_intervalSec > 0.0f)
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(_intervalSec), cancellationToken: ct);
                    }
                }

                // 幕を下ろしてから移る。移った先も同じ色から明けるので、切り替わりが見えない
                await FadeCurtainAsync(ct);

                if (!string.IsNullOrEmpty(_nextSceneName)) SceneManager.LoadScene(_nextSceneName);
            }
            catch (OperationCanceledException)
            {
                // シーンを抜けただけなので何もしない
            }
        }

        /// <summary>
        /// ロゴを1枚見せる。経過時間から濃さを決めるので、
        /// フェードイン・表示・フェードアウトを1つのループで扱える。
        /// </summary>
        private async UniTask ShowLogoAsync(Sprite logo, CancellationToken ct)
        {
            if (_logoImage != null) _logoImage.sprite = logo;
            SetAlpha(0.0f);

            float elapsed = 0.0f;
            bool skipped = false;

            while (elapsed < _durationSec)
            {
                // 読む時間を確保してから受け付ける
                if (elapsed >= _skipDelaySec && HasInput())
                {
                    skipped = true;
                    break;
                }

                SetAlpha(CalcAlpha(elapsed));

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += Time.deltaTime;
            }

            if (!skipped)
            {
                SetAlpha(0.0f);
                return;
            }

            // 飛ばされたときも、いきなり消えると乱暴なので短く落とす
            float from = _logoGroup != null ? _logoGroup.alpha : 1.0f;
            float fade = 0.0f;
            while (fade < _skipFadeOutSec)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                fade += Time.deltaTime;
                SetAlpha(Mathf.Lerp(from, 0.0f, Mathf.Clamp01(fade / _skipFadeOutSec)));
            }

            SetAlpha(0.0f);
        }

        /// <summary>タイトルへ移る直前に幕を下ろす</summary>
        private async UniTask FadeCurtainAsync(CancellationToken ct)
        {
            if (_curtainImage == null) return;

            _curtainImage.gameObject.SetActive(true);

            float elapsed = 0.0f;
            while (elapsed < _curtainFadeSec)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += Time.deltaTime;
                SetCurtainAlpha(Mathf.Clamp01(elapsed / _curtainFadeSec));
            }

            SetCurtainAlpha(1.0f);
        }

        private void SetCurtainAlpha(float alpha)
        {
            if (_curtainImage == null) return;

            Color color = _curtainImage.color;
            color.a = alpha;
            _curtainImage.color = color;
        }

        /// <summary>経過時間から濃さを求める。前半で濃くし、後半で薄くする</summary>
        private float CalcAlpha(float elapsed)
        {
            if (_fadeInSec > 0.0f && elapsed < _fadeInSec) return elapsed / _fadeInSec;

            float fadeOutStart = _durationSec - _fadeOutSec;
            if (_fadeOutSec > 0.0f && elapsed > fadeOutStart)
            {
                return Mathf.Clamp01((_durationSec - elapsed) / _fadeOutSec);
            }

            return 1.0f;
        }

        private void SetAlpha(float alpha)
        {
            if (_logoGroup != null) _logoGroup.alpha = alpha;
        }

        /// <summary>このフレームに人が触ったか。展示なのでキーでもパッドでも画面でも受け付ける</summary>
        private bool HasInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;

            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return true;

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.buttonSouth.wasPressedThisFrame || gamepad.buttonEast.wasPressedThisFrame
                    || gamepad.buttonWest.wasPressedThisFrame || gamepad.buttonNorth.wasPressedThisFrame
                    || gamepad.startButton.wasPressedThisFrame)
                {
                    return true;
                }
            }

            Touchscreen touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }
    }
}
