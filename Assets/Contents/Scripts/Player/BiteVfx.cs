using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 噛みつき攻撃のエフェクト。上下の顎を開いてから一気に閉じ、インパクトを出して消える。
    /// </summary>
    public class BiteVfx : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------
        private const float IMPACT_POP_SCALE = 1.35f;

        // ---- パーツ参照 ----------------------------------
        [SerializeField] private Transform _upperJaw;
        [SerializeField] private Transform _lowerJaw;
        [SerializeField] private SpriteRenderer[] _fangRenderers;
        [SerializeField] private SpriteRenderer[] _bandRenderers;
        [SerializeField] private SpriteRenderer _impactRenderer;

        // ---- 見た目設定 ----------------------------------
        [SerializeField] private bool _showJawBand = true;
        [SerializeField] private bool _billboard = true;
        [SerializeField] private float _billboardRoll;
        [SerializeField] private bool _destroyOnFinish = true;

        // ---- タイミング設定(秒) --------------------------
        [SerializeField] private float _openTime = 0.12f;
        [SerializeField] private float _holdTime = 0.05f;
        [SerializeField] private float _snapTime = 0.045f;
        [SerializeField] private float _hitStopTime = 0.07f;
        [SerializeField] private float _fadeTime = 0.16f;

        // ---- 開き幅 --------------------------------------
        [SerializeField] private float _openDistance = 0.30f;
        [SerializeField] private float _closeDistance = 0.15f;

        // ---- 内部状態 ------------------------------------
        private CancellationTokenSource _cts;
        private Camera _camera;
        private bool _isPlaying;

        // ---- 公開API -------------------------------------

        /// <summary>再生中かどうか</summary>
        public bool IsPlaying => _isPlaying;

        /// <summary>暗い顎の帯の表示を切り替える</summary>
        public void SetJawBandVisible(bool visible)
        {
            _showJawBand = visible;
            ApplyJawBandVisibility();
        }

        /// <summary>噛みつきの向き(画面上の傾き)を指定する</summary>
        public void SetRoll(float degrees)
        {
            _billboardRoll = degrees;
        }

        /// <summary>1回だけ再生して自動的に消える使い捨てインスタンスを生成する</summary>
        public static BiteVfx Spawn(BiteVfx prefab, Vector3 position)
        {
            if (prefab == null) return null;

            BiteVfx instance = Instantiate(prefab, position, Quaternion.identity);
            instance._destroyOnFinish = true;
            instance.Play();
            return instance;
        }

        /// <summary>噛みつきエフェクトを再生する。再生中に呼ぶと最初からやり直す</summary>
        public void Play()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            PlayAndFinishAsync(_cts.Token).Forget();
        }

        /// <summary>噛みつきエフェクトを再生し、終わるまで待つ</summary>
        public async UniTask PlayAsync(CancellationToken token)
        {
            _isPlaying = true;
            ApplyJawBandVisibility();
            SetJawOffset(_closeDistance);
            SetJawAlpha(0f);
            SetImpactAlpha(0f);
            SetImpactScale(0.2f);

            await TweenAsync(_openTime, p =>
            {
                float e = 1f - Mathf.Pow(1f - p, 3f);
                SetJawOffset(Mathf.Lerp(_closeDistance, _openDistance, e));
                SetJawAlpha(p);
            }, token);

            if (_holdTime > 0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_holdTime), cancellationToken: token);
            }

            // 最後だけ加速させると噛んだ瞬間の手応えが出る
            await TweenAsync(_snapTime, p =>
            {
                SetJawOffset(Mathf.Lerp(_openDistance, _closeDistance, p * p * p));
            }, token);

            SetImpactAlpha(1f);
            await TweenAsync(_hitStopTime, p =>
            {
                SetImpactScale(Mathf.Lerp(0.2f, IMPACT_POP_SCALE, 1f - Mathf.Pow(1f - p, 3f)));
            }, token);

            await TweenAsync(_fadeTime, p =>
            {
                SetJawAlpha(1f - p);
                SetImpactAlpha(1f - p);
                SetImpactScale(Mathf.Lerp(IMPACT_POP_SCALE, IMPACT_POP_SCALE * 1.25f, p));
            }, token);

            _isPlaying = false;
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _camera = Camera.main;
            ApplyJawBandVisibility();
            SetJawOffset(_closeDistance);
            SetJawAlpha(0f);
            SetImpactAlpha(0f);
        }

        private void LateUpdate()
        {
            if (!_billboard) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;
            transform.rotation = _camera.transform.rotation * Quaternion.Euler(0f, 0f, _billboardRoll);
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>再生完了まで待ってから、必要なら自分を破棄する</summary>
        private async UniTaskVoid PlayAndFinishAsync(CancellationToken token)
        {
            bool canceled = await PlayAsync(token).SuppressCancellationThrow();
            if (canceled) return;
            if (_destroyOnFinish) Destroy(gameObject);
        }

        private async UniTask TweenAsync(float duration, Action<float> onUpdate, CancellationToken token)
        {
            if (duration <= 0f)
            {
                onUpdate(1f);
                return;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                onUpdate(Mathf.Clamp01(elapsed / duration));
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            onUpdate(1f);
        }

        private void SetJawOffset(float distance)
        {
            if (_upperJaw != null) _upperJaw.localPosition = new Vector3(0f, distance, 0f);
            if (_lowerJaw != null) _lowerJaw.localPosition = new Vector3(0f, -distance, 0f);
        }

        private void SetJawAlpha(float alpha)
        {
            SetAlpha(_fangRenderers, alpha);
            SetAlpha(_bandRenderers, alpha);
        }

        private void SetImpactAlpha(float alpha)
        {
            if (_impactRenderer == null) return;
            Color c = _impactRenderer.color;
            c.a = alpha;
            _impactRenderer.color = c;
        }

        private void SetImpactScale(float scale)
        {
            if (_impactRenderer == null) return;
            _impactRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private static void SetAlpha(SpriteRenderer[] renderers, float alpha)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                Color c = renderers[i].color;
                c.a = alpha;
                renderers[i].color = c;
            }
        }

        private void ApplyJawBandVisibility()
        {
            if (_bandRenderers == null) return;
            for (int i = 0; i < _bandRenderers.Length; i++)
            {
                if (_bandRenderers[i] != null) _bandRenderers[i].enabled = _showJawBand;
            }
        }
    }
}
