using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.Title
{
    /// <summary>
    /// ローディング中に走って見せるコマ送りアニメーション。
    /// RawImage のテクスチャを順番に差し替えるだけの軽い作り。
    /// 素材が DDS で Sprite に変換できないため、Image ではなく RawImage を使っている。
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class LoadingRunAnimation : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("差し替え先の RawImage。未設定なら自分に付いているものを使う")]
        private RawImage _target;

        [SerializeField, Tooltip("順番に切り替えるコマ。上から順に再生して最後まで行ったら先頭に戻る")]
        private Texture[] _frames;

        [SerializeField, Range(1.0f, 30.0f), Tooltip("1秒あたりのコマ数。大きいほど速く走る")]
        private float _framesPerSecond = 10.0f;

        // ---- 内部状態 ------------------------------------

        private int _frameIndex;

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            if (_target == null) _target = GetComponent<RawImage>();
        }

        private void OnEnable()
        {
            _frameIndex = 0;
            ApplyFrame();
            PlayAsync(destroyCancellationToken).Forget();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>オブジェクトが消えるまでコマを送り続ける</summary>
        private async UniTaskVoid PlayAsync(CancellationToken ct)
        {
            if (_frames == null || _frames.Length <= 1) return;

            try
            {
                // ローディング中は Time.timeScale を落とすことがあるので、実時間で待つ
                var interval = TimeSpan.FromSeconds(1.0 / Mathf.Max(1.0f, _framesPerSecond));

                while (isActiveAndEnabled && !ct.IsCancellationRequested)
                {
                    await UniTask.Delay(interval, true, cancellationToken: ct);

                    _frameIndex = (_frameIndex + 1) % _frames.Length;
                    ApplyFrame();
                }
            }
            catch (OperationCanceledException)
            {
                // 画面が閉じて止まっただけなので何もしない
            }
        }

        /// <summary>今のコマを RawImage に反映する</summary>
        private void ApplyFrame()
        {
            if (_target == null || _frames == null || _frames.Length == 0) return;

            Texture frame = _frames[Mathf.Clamp(_frameIndex, 0, _frames.Length - 1)];
            if (frame != null) _target.texture = frame;
        }
    }
}
