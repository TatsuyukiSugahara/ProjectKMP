using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// オーラの明かりをゆっくり明滅させる。
    ///
    /// 粒だけだと画面の中で浮いて見えるので、周りの地面や自分の体を照らして
    /// 「熱を持っている」ように見せるための光。強さが一定だと置物に見えるため、
    /// 呼吸のようにゆらがせている。
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class GorillaAuraLight : MonoBehaviour
    {
        // ---- 設定 ----------------------------------------

        [SerializeField, Min(0f), Tooltip("いちばん弱いときの明るさ")]
        private float _minIntensity = 2.2f;

        [SerializeField, Min(0f), Tooltip("いちばん強いときの明るさ")]
        private float _maxIntensity = 4.2f;

        [SerializeField, Min(0.05f), Tooltip("明滅の周期(秒)")]
        private float _cycleSec = 1.1f;

        [SerializeField, Min(0f), Tooltip("周期に対してずらす揺らぎの周期(秒)。0にすると綺麗すぎる明滅になる")]
        private float _flickerCycleSec = 0.27f;

        [SerializeField, Range(0f, 1f), Tooltip("揺らぎの混ざる割合")]
        private float _flickerRatio = 0.25f;

        // ---- 内部状態 ------------------------------------

        private Light _light;
        private float _elapsedTime;

        // ---- Unity ---------------------------------------

        private void Awake()
        {
            _light = GetComponent<Light>();

            // 全員が同じ拍で光ると機械的に見えるので、出た位置で位相をずらす
            _elapsedTime = Mathf.Abs(transform.position.x + transform.position.z) % _cycleSec;
        }

        private void Update()
        {
            if (_light == null) return;

            _elapsedTime += Time.deltaTime;

            float slow = Mathf.Sin(_elapsedTime / _cycleSec * Mathf.PI * 2.0f) * 0.5f + 0.5f;
            float fast = _flickerCycleSec > 0.0f
                ? Mathf.Sin(_elapsedTime / _flickerCycleSec * Mathf.PI * 2.0f) * 0.5f + 0.5f
                : 0.0f;

            float blended = Mathf.Lerp(slow, fast, _flickerRatio);
            _light.intensity = Mathf.Lerp(_minIntensity, _maxIntensity, blended);
        }
    }
}
