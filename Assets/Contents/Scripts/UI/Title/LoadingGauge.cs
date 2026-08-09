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

        [Header("音")]
        [SerializeField, Tooltip("読み込み中に鳴らし続ける音。未設定なら鳴らさない")]
        private AudioClip _loopClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("鳴らしきったときの音量")]
        private float _loopVolume = 0.3f;

        [SerializeField, Tooltip("進捗0のときの高さ")]
        private float _minPitch = 0.85f;

        [SerializeField, Tooltip("進捗100%のときの高さ。上げるほど伸びている感じが強くなる")]
        private float _maxPitch = 1.8f;

        [SerializeField, Min(0.1f), Tooltip("鳴り始め・鳴り終わりの速さ。大きいほど機敏")]
        private float _fadeSpeed = 5.0f;

        // ---- 内部状態 ------------------------------------

        private AudioSource _source;
        private float _targetVolume;

        // ---- 公開API -------------------------------------

        /// <summary>進捗(0〜1)を反映する</summary>
        public void SetProgress(float value)
        {
            float ratio = Mathf.Clamp01(value);

            UpdateSound(ratio);

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

        private void Awake()
        {
            if (_loopClip == null) return;

            // 読み込み中だけ鳴る音なので、専用の再生口をその場で用意する
            _source = GetComponent<AudioSource>();
            if (_source == null) _source = gameObject.AddComponent<AudioSource>();

            _source.clip = _loopClip;
            _source.loop = true;
            _source.playOnAwake = false;

            // 距離で小さくならないよう2Dで鳴らす
            _source.spatialBlend = 0.0f;
            _source.volume = 0.0f;
        }

        private void Update()
        {
            if (_source == null) return;

            // 読み込み中は時間の進みが乱れることがあるので実時間で寄せる
            _source.volume = Mathf.MoveTowards(
                _source.volume, _targetVolume, _fadeSpeed * _loopVolume * Time.unscaledDeltaTime);

            if (_targetVolume > 0.0f || _source.volume > 0.001f || !_source.isPlaying) return;

            _source.Stop();
        }

        private void Reset()
        {
            _fillImage = GetComponentInChildren<Image>();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>
        /// 進捗に合わせて音の高さを上げていく。ゲージの伸びと音程の上がりが揃うと、
        /// 進んでいることが目と耳の両方で伝わる。
        /// 端(0と1)では鳴らさない。表示が出る前と、切り替わる瞬間に鳴り残らないようにする。
        /// </summary>
        private void UpdateSound(float ratio)
        {
            if (_source == null) return;

            bool active = ratio > 0.001f && ratio < 0.999f;
            _targetVolume = active ? _loopVolume : 0.0f;

            if (!active) return;

            _source.pitch = Mathf.Lerp(_minPitch, _maxPitch, ratio);
            if (!_source.isPlaying) _source.Play();
        }
    }
}
