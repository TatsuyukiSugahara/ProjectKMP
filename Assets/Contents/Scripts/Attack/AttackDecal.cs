using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectKMP.Attack
{
    /// <summary>
    /// 攻撃が地面に残す痕(デカール)。
    /// 攻撃側はプレハブ参照を持ち、命中・着地のタイミングで Spawn() を1行呼ぶだけで痕を残せる汎用機能。
    /// 見た目だけの演出なので、エフェクトと同じく全クライアントで動く処理から呼べば
    /// 追加の通信なしで全員の画面に痕が残る(ネットワーク同期は不要)。
    /// 表示時間が過ぎるとフェードアウトして自動で消える。
    /// 平らな地面向けに、地面すれすれの透過クアッドとして描画する方式
    /// (URPのDecal Renderer Featureを使わないため、描画設定の変更やモバイル負荷の追加がない)。
    /// </summary>
    public class AttackDecal : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Min(0.0f), Tooltip("痕が残る時間(秒)。0にすると消えない")]
        private float _lifetimeSec = 15.0f;

        [SerializeField, Min(0.0f), Tooltip("消えるときのフェードアウトにかける時間(秒)")]
        private float _fadeOutSec = 3.0f;

        [SerializeField, Tooltip("生成時にランダムに回転させ、同じ痕が並んでも単調に見えないようにする")]
        private bool _randomRotation = true;

        [SerializeField, Min(0.0f), Tooltip("地面から浮かせる高さ(メートル)。ちらつき(Zファイティング)を防ぐ")]
        private float _groundOffset = 0.02f;

        // ---- 内部状態 ------------------------------------

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        private Renderer[] _renderers;
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor = Color.white;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 痕を生成する。position は地面の座標(高さの浮かせ量はプレハブ側の設定を使う)。
        /// diameter は痕の直径(メートル)。攻撃の範囲に合わせて渡す。
        /// </summary>
        public static AttackDecal Spawn(AttackDecal prefab, Vector3 position, float diameter = 1.0f)
        {
            if (prefab == null) return null;

            AttackDecal instance = Instantiate(prefab);
            instance.transform.position = position + Vector3.up * instance._groundOffset;
            instance.transform.localScale = Vector3.one * Mathf.Max(0.01f, diameter);

            if (instance._randomRotation)
            {
                instance.transform.rotation = Quaternion.Euler(0.0f, UnityEngine.Random.Range(0.0f, 360.0f), 0.0f);
            }

            return instance;
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _propertyBlock = new MaterialPropertyBlock();

            if (_renderers.Length > 0 && _renderers[0].sharedMaterial != null
                && _renderers[0].sharedMaterial.HasProperty(BASE_COLOR_ID))
            {
                _baseColor = _renderers[0].sharedMaterial.GetColor(BASE_COLOR_ID);
            }
        }

        private void Start()
        {
            if (_lifetimeSec > 0.0f)
            {
                FadeOutAndDestroyAsync(destroyCancellationToken).Forget();
            }
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>表示時間が過ぎたらフェードアウトして自分を破棄する</summary>
        private async UniTaskVoid FadeOutAndDestroyAsync(CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_lifetimeSec), cancellationToken: ct);

            float elapsed = 0.0f;
            while (elapsed < _fadeOutSec)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += Time.deltaTime;
                SetAlpha(Mathf.Lerp(1.0f, 0.0f, Mathf.Clamp01(elapsed / _fadeOutSec)));
            }

            Destroy(gameObject);
        }

        /// <summary>マテリアルを複製せず、プロパティブロックで透明度だけ変える</summary>
        private void SetAlpha(float alpha01)
        {
            Color color = _baseColor;
            color.a = _baseColor.a * alpha01;

            foreach (var targetRenderer in _renderers)
            {
                if (targetRenderer == null) continue;
                targetRenderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BASE_COLOR_ID, color);
                targetRenderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
