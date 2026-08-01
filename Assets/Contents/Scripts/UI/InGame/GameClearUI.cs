using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectKMP.UI.InGame
{
    /// <summary>
    /// ボス撃破時に画面中央へ出す「ゲームクリア」表示。
    /// 表示のオン・オフとポップ演出だけを受け持ち、GameClearDirector から呼ばれる。
    /// </summary>
    public class GameClearUI : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("表示・非表示に使う CanvasGroup")]
        private CanvasGroup _group;

        [SerializeField, Tooltip("ポップ演出で拡縮するルート")]
        private RectTransform _popRoot;

        [SerializeField, Min(0.01f), Tooltip("フェードインにかける時間(秒)")]
        private float _fadeInSec = 0.35f;

        [SerializeField, Min(1.0f), Tooltip("出た瞬間の拡大率。ここから等倍へ縮んで止まる")]
        private float _popScale = 1.25f;

        // ---- 公開API -------------------------------------

        /// <summary>「ゲームクリア」をポップしながら表示する</summary>
        public async UniTask ShowAsync(CancellationToken ct)
        {
            if (_group == null) return;

            float elapsed = 0.0f;
            while (elapsed < _fadeInSec)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / _fadeInSec);
                float eased = 1.0f - (1.0f - t) * (1.0f - t);

                _group.alpha = eased;
                if (_popRoot != null) _popRoot.localScale = Vector3.one * Mathf.Lerp(_popScale, 1.0f, eased);
            }

            _group.alpha = 1.0f;
            if (_popRoot != null) _popRoot.localScale = Vector3.one;
        }

        /// <summary>表示を隠す(シーン開始時の初期化用)</summary>
        public void Hide()
        {
            if (_group != null) _group.alpha = 0.0f;
        }
    }
}
