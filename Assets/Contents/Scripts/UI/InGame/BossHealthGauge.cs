using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.InGame
{
    /// <summary>
    /// 画面上部に出すボスのHPゲージ。表示だけを受け持ち、HPの持ち主から
    /// SetHealth() を呼んでもらって更新する。まだボス側にHPが無いので、
    /// つなぎ込みは呼び出し1行で済むようにしてある。
    /// Image の Filled は切り口が角になるため、中身の幅そのものを伸ばして端を丸いまま保つ。
    /// </summary>
    public class BossHealthGauge : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("表示")]
        [SerializeField, Tooltip("表示・非表示に使う CanvasGroup")]
        private CanvasGroup _group;

        [SerializeField, Tooltip("ボスの名前を出す RawImage")]
        private RawImage _nameImage;

        [Header("ゲージ")]
        [SerializeField, Tooltip("ゲージの溝。伸びる範囲の基準になる")]
        private RectTransform _trackRect;

        [SerializeField, Tooltip("伸び縮みする中身")]
        private RectTransform _fillRect;

        [SerializeField, Tooltip("溝と中身のすきま(ピクセル)")]
        private float _padding = 4.0f;

        [SerializeField, Min(0.0f), Tooltip("減ったぶんが追いつくまでの秒数。0なら即座に反映する")]
        private float _animationSeconds = 0.25f;

        [Header("確認用")]
        [SerializeField, Range(0.0f, 1.0f), Tooltip("エディタで見た目を確かめるための値。実行中は使わない")]
        private float _previewRatio = 1.0f;

        // ---- 内部状態 ------------------------------------

        private float _displayRatio = 1.0f;
        private float _targetRatio = 1.0f;
        private CancellationTokenSource _animationCts;

        // ---- 公開API -------------------------------------

        /// <summary>いま表示している割合(0〜1)</summary>
        public float Ratio01 => _displayRatio;

        /// <summary>現在HPと最大HPを渡してゲージを更新する</summary>
        public void SetHealth(int current, int max)
        {
            SetRatio(max <= 0 ? 0.0f : current / (float)max);
        }

        /// <summary>割合(0〜1)でゲージを更新する。設定した秒数をかけて追従する</summary>
        public void SetRatio(float ratio01)
        {
            _targetRatio = Mathf.Clamp01(ratio01);

            if (_animationSeconds <= 0.0f || !isActiveAndEnabled)
            {
                SetRatioImmediate(_targetRatio);
                return;
            }

            RestartAnimation();
        }

        /// <summary>アニメーションせずに即座に反映する。戦闘開始時のリセットなどに使う</summary>
        public void SetRatioImmediate(float ratio01)
        {
            StopAnimation();
            _targetRatio = Mathf.Clamp01(ratio01);
            _displayRatio = _targetRatio;
            ApplyFill(_displayRatio);
        }

        /// <summary>ゲージ全体の表示・非表示を切り替える</summary>
        public void SetVisible(bool visible)
        {
            if (_group == null) return;
            _group.alpha = visible ? 1.0f : 0.0f;
        }

        /// <summary>ボスの名前画像を差し替える。ボスが増えたときに使う</summary>
        public void SetBossName(Texture nameTexture)
        {
            if (_nameImage != null) _nameImage.texture = nameTexture;
        }

        // ---- Unityイベント -------------------------------

        private void OnEnable()
        {
            ApplyFill(_displayRatio);
        }

        private void OnDisable()
        {
            StopAnimation();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;

            _displayRatio = _previewRatio;
            _targetRatio = _previewRatio;
            ApplyFill(_previewRatio);
        }
#endif

        // ---- 内部処理 ------------------------------------

        private void RestartAnimation()
        {
            StopAnimation();
            _animationCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            AnimateAsync(_animationCts.Token).Forget();
        }

        private void StopAnimation()
        {
            if (_animationCts == null) return;

            _animationCts.Cancel();
            _animationCts.Dispose();
            _animationCts = null;
        }

        /// <summary>今の表示値から目標値へ、決めた秒数で滑らかに寄せる</summary>
        private async UniTaskVoid AnimateAsync(CancellationToken ct)
        {
            try
            {
                float from = _displayRatio;
                float elapsed = 0.0f;

                while (elapsed < _animationSeconds)
                {
                    elapsed += Time.deltaTime;
                    _displayRatio = Mathf.Lerp(from, _targetRatio, Mathf.Clamp01(elapsed / _animationSeconds));
                    ApplyFill(_displayRatio);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }

                _displayRatio = _targetRatio;
                ApplyFill(_displayRatio);
            }
            catch (OperationCanceledException)
            {
                // 次の値が来て止めただけなので何もしない
            }
        }

        /// <summary>中身の幅を割合に合わせて変える</summary>
        private void ApplyFill(float ratio01)
        {
            if (_trackRect == null || _fillRect == null) return;

            float trackWidth = _trackRect.rect.width - _padding * 2.0f;
            float trackHeight = _trackRect.rect.height - _padding * 2.0f;

            // 残りわずかでも高さぶんの幅を残し、丸い端がつぶれないようにする。0のときだけ完全に消す
            float width = ratio01 <= 0.0f ? 0.0f : Mathf.Max(trackHeight, ratio01 * trackWidth);

            _fillRect.anchorMin = new Vector2(0.0f, 0.0f);
            _fillRect.anchorMax = new Vector2(0.0f, 1.0f);
            _fillRect.offsetMin = new Vector2(_padding, _padding);
            _fillRect.offsetMax = new Vector2(_padding + width, -_padding);
        }
    }
}
