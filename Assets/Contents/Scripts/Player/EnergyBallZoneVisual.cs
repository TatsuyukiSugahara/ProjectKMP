using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 元気玉の着弾後に残る残留ダメージ地帯の見た目。
    /// 指定の半径・時間で表示し、終わり際にフェードアウトして自分で消える。
    /// ダメージ判定そのものは PlayerEnergyBallSkill(本人のクライアント)が行うため、
    /// これは全クライアントに出る演出専用。
    /// </summary>
    public class EnergyBallZoneVisual : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Min(0.05f), Tooltip("消えるときのフェードアウトにかける時間(秒)")]
        private float _fadeOutSec = 0.6f;

        // ---- 内部状態 ------------------------------------

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        private Renderer[] _renderers;
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor = Color.white;
        private float _durationSec = 4f;
        private float _elapsedSec;
        private bool _isStopped;

        // ---- 公開API -------------------------------------

        /// <summary>地帯の見た目を生成する。radius は半径(m)、duration は表示時間(秒)</summary>
        public static EnergyBallZoneVisual Spawn(
            EnergyBallZoneVisual prefab, Vector3 position, float radius, float duration)
        {
            if (prefab == null) return null;

            EnergyBallZoneVisual instance = Instantiate(prefab, position, Quaternion.identity);
            // 子は半径1m基準で作ってあるので、ルートのスケール=半径になる
            instance.transform.localScale = Vector3.one * Mathf.Max(0.1f, radius);
            instance._durationSec = Mathf.Max(0.1f, duration);
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

        private void Update()
        {
            _elapsedSec += Time.deltaTime;
            float remain = _durationSec - _elapsedSec;

            if (remain <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (remain > _fadeOutSec) return;

            // 終わり際: 新しい粒は出さず、全体をじわっと透明にする
            if (!_isStopped)
            {
                _isStopped = true;
                foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            SetAlpha(Mathf.Clamp01(remain / _fadeOutSec));
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>マテリアルを複製せず、プロパティブロックで透明度だけ変える</summary>
        private void SetAlpha(float alpha01)
        {
            Color color = _baseColor;
            color.a *= alpha01;

            foreach (var targetRenderer in _renderers)
            {
                if (targetRenderer == null) continue;
                if (targetRenderer is ParticleSystemRenderer) continue;

                targetRenderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BASE_COLOR_ID, color);
                targetRenderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
