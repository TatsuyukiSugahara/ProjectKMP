using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// ターゲットカメラのボタンの見た目を、いま入っているかどうかに合わせる。
    ///
    /// 押している間だけ反応するボタンと違い、これは切り替えなので、
    /// 押した後の状態が見えないと入っているのか分からなくなる。
    /// 入っている間は色を変えて光らせ、切れている間は控えめに沈ませる。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class TargetButtonVisual : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>色が移り変わる速さ。大きいほど機敏</summary>
        private const float BLEND_SPEED = 10.0f;

        /// <summary>入っているときの脈打ちの速さ(1秒あたりの回数)</summary>
        private const float PULSE_HZ = 1.4f;

        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("切れているときの色。控えめにして視界の邪魔をしない")]
        private Color _offColor = new Color(1.0f, 1.0f, 1.0f, 0.55f);

        [SerializeField, Tooltip("入っているときの色。敵に出る印と同じ色にして対応を分からせる")]
        private Color _onColor = new Color(1.0f, 0.35f, 0.3f, 1.0f);

        [SerializeField, Min(0.0f), Tooltip("入っているときの脈打ちの幅。0で脈打たない")]
        private float _pulseScale = 0.06f;

        // ---- 内部状態 ------------------------------------

        private Image _image;
        private float _onAmount;

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void Update()
        {
            if (_image == null) return;

            bool on = IsLockedOn();

            _onAmount = Mathf.MoveTowards(_onAmount, on ? 1.0f : 0.0f, BLEND_SPEED * Time.unscaledDeltaTime);
            _image.color = Color.Lerp(_offColor, _onColor, _onAmount);

            if (_pulseScale <= 0.0f) return;

            // 入っている間だけ脈打たせる。切れているときは静かにしておく
            float pulse = 1.0f + _pulseScale * _onAmount * Mathf.Sin(Time.unscaledTime * PULSE_HZ * Mathf.PI * 2.0f);
            transform.localScale = Vector3.one * pulse;
        }

        /// <summary>用意された状態から読む。カメラを探し回る必要がない</summary>
        private bool IsLockedOn()
        {
            return Core.PlayerStatusHub.Local.LockTarget.CurrentValue != null;
        }
    }
}
