using UnityEngine;

namespace ProjectKMP.UI.Title
{
    /// <summary>
    /// CanvasGroup の透明度を上下させて点滅させる。PressAnyKey の文字などに付ける。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class BlinkingText : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("点滅させる対象。未設定なら同じオブジェクトの CanvasGroup を使う")]
        private CanvasGroup _targetGroup;

        [SerializeField, Tooltip("1回の明滅にかかる秒数。小さいほど速く点滅する")]
        private float _cycleSeconds = 1.2f;

        [SerializeField, Range(0f, 1f), Tooltip("いちばん薄いときの不透明度")]
        private float _minAlpha = 0.15f;

        [SerializeField, Range(0f, 1f), Tooltip("いちばん濃いときの不透明度")]
        private float _maxAlpha = 1.0f;

        [SerializeField, Tooltip("オンにするとふわっとではなく、カチカチと切り替わる点滅になる")]
        private bool _useSquareWave = false;

        // ---- 内部状態 ------------------------------------

        private float _timer;

        // ---- Unityイベント -------------------------------

        private void Reset()
        {
            _targetGroup = GetComponent<CanvasGroup>();
        }

        private void Awake()
        {
            if (_targetGroup == null) _targetGroup = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            // 表示されるたびに、いちばん濃い状態から始める
            _timer = 0.0f;
        }

        private void Update()
        {
            if (_targetGroup == null || _cycleSeconds <= 0.0f) return;

            _timer += Time.unscaledDeltaTime;
            float phase = Mathf.Repeat(_timer / _cycleSeconds, 1.0f);

            float wave = _useSquareWave
                ? (phase < 0.5f ? 1.0f : 0.0f)
                : (Mathf.Cos(phase * Mathf.PI * 2.0f) + 1.0f) * 0.5f;

            _targetGroup.alpha = Mathf.Lerp(_minAlpha, _maxAlpha, wave);
        }
    }
}
