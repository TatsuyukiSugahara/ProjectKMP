using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.InGame
{
    /// <summary>
    /// 画面下部に出す自分のHPゲージ。表示だけを受け持ち、PlayerHpPresenter から SetHealth() で更新される。
    /// 見た目はボスゲージ(BossHealthGauge)と同じ「白フレーム+暗色トラック+明色フィル」構成。
    /// 減ったぶんは残像がワンテンポ遅れて追いかけ、残量に応じて色が 緑→黄→赤 と変わる
    /// (トラックもフィル色を暗くした色で連動させ、ボスゲージと同じ配色関係を保つ)。
    /// Image の Filled は切り口が角になるため、中身の幅そのものを伸ばして端を丸いまま保つ。
    /// </summary>
    public class PlayerHpGauge : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("ゲージ")]
        [SerializeField, Tooltip("ゲージの溝。伸びる範囲の基準になる")]
        private RectTransform _trackRect;

        [SerializeField, Tooltip("溝の Image。フィル色を暗くした色で連動させる")]
        private Image _trackImage;

        [SerializeField, Tooltip("伸び縮みする中身")]
        private RectTransform _fillRect;

        [SerializeField, Tooltip("中身の色を変える Image")]
        private Image _fillImage;

        [SerializeField, Tooltip("減ったぶんを遅れて追いかける残像")]
        private RectTransform _ghostRect;

        [SerializeField, Tooltip("HPの数値表示(現在値 / 最大値)")]
        private TextMeshProUGUI _numberLabel;

        [SerializeField, Tooltip("溝と中身のすきま(ピクセル)")]
        private float _padding = 4.0f;

        [Header("残像")]
        [SerializeField, Min(0.0f), Tooltip("ダメージ後、残像が追いかけ始めるまでの秒数")]
        private float _ghostDelaySec = 0.3f;

        [SerializeField, Min(0.01f), Tooltip("残像が追いつく速さ(ゲージ全体を1とした割合/秒)")]
        private float _ghostCatchupSpeed = 1.2f;

        [Header("色")]
        [SerializeField, Tooltip("HPが多いときの色")]
        private Color _colorHigh = new Color(0.31f, 0.75f, 0.23f);

        [SerializeField, Tooltip("HPが半分あたりの色")]
        private Color _colorMid = new Color(0.94f, 0.76f, 0.23f);

        [SerializeField, Tooltip("HPが少ないときの色")]
        private Color _colorLow = new Color(0.90f, 0.34f, 0.42f);

        [SerializeField, Range(0.0f, 1.0f), Tooltip("溝の暗さ。フィル色をこの割合だけ黒へ寄せた色を溝に使う(ボスゲージの配色関係に合わせた値)")]
        private float _trackDarkness = 0.6f;

        // ---- 内部状態 ------------------------------------

        private float _ratio = 1.0f;
        private float _ghostRatio = 1.0f;
        private float _ghostWaitRemainSec;

        // ---- 公開API -------------------------------------

        /// <summary>現在HPと最大HPを渡してゲージを更新する</summary>
        public void SetHealth(int current, int max)
        {
            float ratio = max <= 0 ? 0.0f : Mathf.Clamp01(current / (float)max);

            // 回復・リスポーンで増えたときは残像を出さず、即座にそろえる
            if (ratio >= _ratio)
            {
                _ghostRatio = ratio;
                _ghostWaitRemainSec = 0.0f;
            }
            else
            {
                // 連続ダメージ中は残像を維持したまま、追いかけ開始だけ遅らせ直す
                _ghostWaitRemainSec = _ghostDelaySec;
            }

            _ratio = ratio;

            if (_numberLabel != null) _numberLabel.text = $"{Mathf.Max(0, current)} / {max}";
            ApplyColor(ratio);
            ApplyFill();
        }

        // ---- Unityイベント -------------------------------

        private void Update()
        {
            if (_ghostRatio <= _ratio) return;

            if (_ghostWaitRemainSec > 0.0f)
            {
                _ghostWaitRemainSec -= Time.deltaTime;
                return;
            }

            _ghostRatio = Mathf.MoveTowards(_ghostRatio, _ratio, _ghostCatchupSpeed * Time.deltaTime);
            ApplyFill();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>残量に応じたフィル色と、それを暗くした溝色を反映する</summary>
        private void ApplyColor(float ratio01)
        {
            Color fillColor = EvaluateColor(ratio01);
            if (_fillImage != null) _fillImage.color = fillColor;

            // ボスゲージの「明るいフィル+同系の暗いトラック」の関係を保つ
            if (_trackImage != null)
            {
                Color trackColor = Color.Lerp(fillColor, Color.black, _trackDarkness);
                trackColor.a = 1.0f;
                _trackImage.color = trackColor;
            }
        }

        /// <summary>残量に応じて 緑→黄→赤 を補間する</summary>
        private Color EvaluateColor(float ratio01)
        {
            if (ratio01 >= 0.5f)
            {
                return Color.Lerp(_colorMid, _colorHigh, (ratio01 - 0.5f) * 2.0f);
            }
            return Color.Lerp(_colorLow, _colorMid, ratio01 * 2.0f);
        }

        /// <summary>中身と残像の幅を、それぞれの割合に合わせて変える</summary>
        private void ApplyFill()
        {
            ApplyWidth(_fillRect, _ratio);
            ApplyWidth(_ghostRect, _ghostRatio);
        }

        private void ApplyWidth(RectTransform rect, float ratio01)
        {
            if (_trackRect == null || rect == null) return;

            float trackWidth = _trackRect.rect.width - _padding * 2.0f;
            float trackHeight = _trackRect.rect.height - _padding * 2.0f;

            // 残りわずかでも高さぶんの幅を残し、丸い端がつぶれないようにする。0のときだけ完全に消す
            float width = ratio01 <= 0.0f ? 0.0f : Mathf.Max(trackHeight, ratio01 * trackWidth);

            rect.anchorMin = new Vector2(0.0f, 0.0f);
            rect.anchorMax = new Vector2(0.0f, 1.0f);
            rect.offsetMin = new Vector2(_padding, _padding);
            rect.offsetMax = new Vector2(_padding + width, -_padding);
        }
    }
}
