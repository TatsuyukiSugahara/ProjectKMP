using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// シーン開始時に黒からフェードインする汎用コンポーネント。
    /// 画面全体を覆う黒Imageを割り当てておくと、開始と同時に明けていき、終わったら自動で消える。
    /// どのシーンでも使い回せる(現在はリザルトで使用)。
    /// </summary>
    public class SceneFadeIn : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("画面全体を覆う黒の Image")]
        private Image _fadeImage;

        [SerializeField, Min(0.01f), Tooltip("フェードインにかける時間(秒)")]
        private float _durationSec = 0.5f;

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            FadeInAsync(destroyCancellationToken).Forget();
        }

        // ---- 内部処理 ------------------------------------

        private async UniTaskVoid FadeInAsync(CancellationToken ct)
        {
            if (_fadeImage == null) return;

            _fadeImage.gameObject.SetActive(true);
            Color color = _fadeImage.color;
            color.a = 1.0f;
            _fadeImage.color = color;

            float elapsed = 0.0f;
            while (elapsed < _durationSec)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += Time.deltaTime;
                color.a = Mathf.Lerp(1.0f, 0.0f, Mathf.Clamp01(elapsed / _durationSec));
                _fadeImage.color = color;
            }

            // 明け切ったら描画ごと止めておく
            _fadeImage.gameObject.SetActive(false);
        }
    }
}
