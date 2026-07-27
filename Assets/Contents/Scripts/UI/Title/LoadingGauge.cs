using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.Title
{
    /// <summary>
    /// 読み込み進捗を表示するゲージ。IProgress&lt;float&gt; として SceneLoader に渡して使う。
    /// Image の Filled は切り口が角になるため、中身の幅そのものを伸ばして端を丸いまま保つ。
    /// </summary>
    public class LoadingGauge : MonoBehaviour, IProgress<float>
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("ゲージの外枠(伸びる範囲の基準になる)")]
        private RectTransform _trackRect;

        [SerializeField, Tooltip("伸び縮みする中身")]
        private RectTransform _fillRect;

        [SerializeField, Tooltip("中身の Image。進捗0のときに隠すのに使う")]
        private Image _fillImage;

        [SerializeField, Tooltip("外枠と中身のすきま(ピクセル)")]
        private float _padding = 6.0f;

        [SerializeField, Tooltip("パーセント表示。未設定なら数字は出さない")]
        private TMP_Text _percentText;

        [SerializeField, Tooltip("パーセントの表示書式")]
        private string _percentFormat = "{0}%";

        // ---- 公開API -------------------------------------

        /// <summary>進捗(0〜1)を反映する</summary>
        public void SetProgress(float value)
        {
            float ratio = Mathf.Clamp01(value);

            if (_trackRect != null && _fillRect != null)
            {
                float trackWidth = _trackRect.rect.width - _padding * 2.0f;
                float trackHeight = _trackRect.rect.height - _padding * 2.0f;

                // 進捗がわずかなときも高さぶんの幅を残し、つぶれた形にならないようにする
                float width = Mathf.Max(trackHeight, ratio * trackWidth);

                _fillRect.anchorMin = new Vector2(0.0f, 0.0f);
                _fillRect.anchorMax = new Vector2(0.0f, 1.0f);
                _fillRect.offsetMin = new Vector2(_padding, _padding);
                _fillRect.offsetMax = new Vector2(_padding + width, -_padding);

                if (_fillImage != null) _fillImage.enabled = ratio > 0.001f;
            }

            if (_percentText != null)
            {
                _percentText.text = string.Format(_percentFormat, Mathf.RoundToInt(ratio * 100.0f));
            }
        }

        /// <summary>IProgress の実装。SceneLoader から進捗が届く</summary>
        public void Report(float value)
        {
            SetProgress(value);
        }

        // ---- Unityイベント -------------------------------

        private void Reset()
        {
            _fillImage = GetComponentInChildren<Image>();
        }
    }
}
