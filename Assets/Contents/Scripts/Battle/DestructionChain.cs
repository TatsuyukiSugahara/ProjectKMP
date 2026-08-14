using UnityEngine;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 短時間に続けて壊れたフィールド物を「破壊連鎖」として数える。
    /// 木そのものは各クライアントで同じように倒れ、ゲージ加算だけをMasterClientが採用する。
    /// </summary>
    public static class DestructionChain
    {
        private const float CHAIN_WINDOW_SEC = 1.1f;
        private const float POWER_PER_BREAK_RATIO = 0.006f;
        private const float MAX_POWER_PER_CHAIN_RATIO = 0.12f;

        private static float _lastBreakTime = -999.0f;
        private static int _chainCount;
        private static float _grantedRatio;

        public static int CurrentCount =>
            Time.unscaledTime - _lastBreakTime <= CHAIN_WINDOW_SEC ? _chainCount : 0;

        public static void NotifyBreak(Vector3 position)
        {
            float now = Time.unscaledTime;
            if (now - _lastBreakTime > CHAIN_WINDOW_SEC)
            {
                _chainCount = 0;
                _grantedRatio = 0.0f;
            }

            _lastBreakTime = now;
            _chainCount++;

            float grant = Mathf.Min(POWER_PER_BREAK_RATIO, MAX_POWER_PER_CHAIN_RATIO - _grantedRatio);
            if (grant > 0.0f)
            {
                _grantedRatio += grant;
                TeamPowerDirector.Active?.AddDestructionPower(grant);
            }

            if (_chainCount == 3)
            {
                Onomatopoeia.Play(position + Vector3.up * 2.0f, "3れんさ！",
                    new Color(0.55f, 0.9f, 1.0f, 1.0f), 1.4f, 0.8f);
            }
            else if (_chainCount == 6)
            {
                Onomatopoeia.Play(position + Vector3.up * 2.4f, "スーパーはかい！",
                    new Color(1.0f, 0.8f, 0.2f, 1.0f), 2.0f, 0.95f);
                ShockwaveRing.Play(position, new Color(1.0f, 0.75f, 0.18f, 1.0f), 7.0f, 0.5f, 0.7f);
            }
            else if (_chainCount == 10)
            {
                Onomatopoeia.Play(position + Vector3.up * 2.8f, "ウルトラれんさ！",
                    new Color(1.0f, 0.42f, 0.18f, 1.0f), 2.5f, 1.1f);
                ShockwaveRing.Play(position, Color.white, 10.0f, 0.65f, 1.0f);
            }
        }
    }
}
