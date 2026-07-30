using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.InGame
{
    /// <summary>
    /// 死亡中に画面中央へ出す、リスポーンまでのカウントダウン表示。
    /// 数字は切り上げ秒(5→1)、円ゲージは残り時間の割合で減っていく。
    /// 表示のオン・オフと値の更新だけを受け持ち、PlayerHpHud から呼ばれる。
    /// </summary>
    public class RespawnCountdownView : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("表示・非表示に使う CanvasGroup")]
        private CanvasGroup _group;

        [SerializeField, Tooltip("残り時間で減っていく円ゲージ(Filled / Radial360)")]
        private Image _ringImage;

        [SerializeField, Tooltip("残り秒数の数字")]
        private TextMeshProUGUI _numberLabel;

        // ---- 内部状態 ------------------------------------

        private float _totalSec = 1.0f;

        // ---- 公開API -------------------------------------

        /// <summary>カウントダウンを総時間つきで表示する</summary>
        public void Show(float totalSec)
        {
            _totalSec = Mathf.Max(0.01f, totalSec);
            UpdateRemaining(_totalSec);
            SetVisible(true);
        }

        /// <summary>残り秒数を反映する</summary>
        public void UpdateRemaining(float remainingSec)
        {
            float clamped = Mathf.Clamp(remainingSec, 0.0f, _totalSec);

            if (_ringImage != null) _ringImage.fillAmount = clamped / _totalSec;
            if (_numberLabel != null) _numberLabel.text = Mathf.CeilToInt(clamped).ToString();
        }

        /// <summary>カウントダウンを隠す</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        // ---- 内部処理 ------------------------------------

        private void SetVisible(bool visible)
        {
            if (_group == null) return;

            _group.alpha = visible ? 1.0f : 0.0f;

            // 死亡中でもカメラのスワイプ操作などを邪魔しないよう、レイキャストは常に通す
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }
    }
}
