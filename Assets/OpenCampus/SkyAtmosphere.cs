using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectKMP.Field
{
    /// <summary>
    /// 空(Skybox)と場の明るさをまとめて扱う演出役。
    /// ふだんは草原に合う昼の青空。必殺技の発動中だけ夜へ落として、
    /// ポストエフェクト(露出を下げる・ブルーム・ヴィネット)で光とエフェクトを際立たせる。
    /// シーンには PF_Field_SkyAtmosphere プレハブを1つ置いて使う。
    /// </summary>
    [DisallowMultipleComponent]
    public class SkyAtmosphere : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("参照")]
        [SerializeField, Tooltip("空のマテリアル(ProjectKMP/Field/SkyDayNight)。実行中は複製を使うので元アセットは書き換わらない")]
        private Material _skyboxMaterial;

        [SerializeField, Tooltip("夜化のポストエフェクトを入れた Volume。ウェイトを 0→1 にして効かせる")]
        private Volume _nightVolume;

        [SerializeField, Tooltip("太陽にあたる平行光源。未設定なら Lighting設定の Sun かシーン内の平行光源を自動で探す")]
        private Light _sunLight;

        [Header("切り替えの速さ")]
        [SerializeField, Min(0.01f), Tooltip("夜へ落ちきるまでの時間(秒)")]
        private float _toNightSec = 0.5f;

        [SerializeField, Min(0.01f), Tooltip("昼へ戻りきるまでの時間(秒)。戻りはゆっくりのほうが余韻が出る")]
        private float _toDaySec = 1.4f;

        [Header("夜の見た目")]
        [SerializeField, Range(0f, 1f), Tooltip("夜の効き具合。1で真っ暗、下げるほど明るいまま。ポストエフェクト・太陽光・環境光・フォグにまとめて掛かる")]
        private float _nightStrength = 0.6f;

        [SerializeField, Range(0f, 1f), Tooltip("空だけの夜の効き具合。1なら地上が明るめでも空はしっかり夜(星が見える)")]
        private float _skyNightStrength = 1f;

        [SerializeField, Range(0f, 1f), Tooltip("夜のときの Volume のウェイト")]
        private float _nightVolumeWeight = 1f;

        [SerializeField, Min(0f), Tooltip("夜のときの平行光源の明るさ")]
        private float _nightSunIntensity = 0.28f;

        [SerializeField, Tooltip("夜のときの平行光源の色(月明かりの青)")]
        private Color _nightSunColor = new Color(0.48f, 0.62f, 1f, 1f);

        [SerializeField, Range(0f, 2f), Tooltip("夜のときの環境光の強さ")]
        private float _nightAmbientIntensity = 0.3f;

        [SerializeField, Tooltip("夜のときのフォグの色")]
        private Color _nightFogColor = new Color(0.05f, 0.08f, 0.16f, 1f);

        // ---- 定数 ----------------------------------------

        private static readonly int NIGHT_BLEND_ID = Shader.PropertyToID("_NightBlend");
        private static readonly int SUN_DIRECTION_ID = Shader.PropertyToID("_SunDirection");

        // ---- 内部状態 ------------------------------------

        /// <summary>夜にしてほしいと言っている人の数。1人でもいれば夜になる</summary>
        private int _nightRequestCount;

        private float _blend01;

        private Material _runtimeSkybox;
        private Material _previousSkybox;

        private float _daySunIntensity = 1f;
        private Color _daySunColor = Color.white;
        private float _dayAmbientIntensity = 1f;
        private Color _dayFogColor = Color.gray;

        // ---- 公開API -------------------------------------

        /// <summary>シーンに置かれているインスタンス。無ければ null</summary>
        public static SkyAtmosphere Instance { get; private set; }

        /// <summary>いま夜になっているか(申請が1件でもあるか)</summary>
        public bool IsNight => _nightRequestCount > 0;

        /// <summary>夜への遷移の進み具合(0=昼 1=夜)</summary>
        public float NightBlend01 => _blend01;

        /// <summary>
        /// 夜にしてほしいと申請する。複数のプレイヤーが同時に必殺技を撃っても
        /// 片方の終了でいきなり昼に戻らないよう、申請の数を数えている。
        /// シーンに SkyAtmosphere が無ければ何もしない。
        /// </summary>
        public static void RequestNight()
        {
            if (Instance != null) Instance.PushNight();
        }

        /// <summary>夜の申請を取り下げる。全員が取り下げると昼へ戻り始める</summary>
        public static void ReleaseNight()
        {
            if (Instance != null) Instance.PopNight();
        }

        /// <summary>夜の申請を1件足す</summary>
        public void PushNight()
        {
            _nightRequestCount++;
        }

        /// <summary>夜の申請を1件取り下げる</summary>
        public void PopNight()
        {
            _nightRequestCount = Mathf.Max(0, _nightRequestCount - 1);
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            Instance = this;

            ResolveSunLight();
            CacheDaySettings();
            SetUpRuntimeSkybox();

            _blend01 = 0f;
            Apply();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // シーンを抜けても昼の設定を残さないよう、触った分をすべて戻す
            RestoreDaySettings();

            if (_runtimeSkybox != null)
            {
                Destroy(_runtimeSkybox);
                _runtimeSkybox = null;
            }
        }

        private void Update()
        {
            float target = _nightRequestCount > 0 ? 1f : 0f;
            if (Mathf.Approximately(_blend01, target)) return;

            // 必殺技の演出中は Time.timeScale が落ちるので、暗転の速さは実時間で進める
            float duration = target > _blend01 ? _toNightSec : _toDaySec;
            _blend01 = Mathf.MoveTowards(_blend01, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, duration));
            Apply();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>いまの遷移具合を、空・ライト・環境光・フォグ・Volume へ一度に反映する</summary>
        private void Apply()
        {
            // 直線的に暗くすると切り替わりが硬いので、両端をなめらかにする
            float eased = Mathf.SmoothStep(0f, 1f, _blend01);

            // 地上の暗さと空の暗さは別々に効かせる。地上を明るく保ったまま空だけ夜にできる
            float t = eased * _nightStrength;
            float skyT = eased * _skyNightStrength;

            if (_runtimeSkybox != null)
            {
                _runtimeSkybox.SetFloat(NIGHT_BLEND_ID, skyT);
                if (_sunLight != null) _runtimeSkybox.SetVector(SUN_DIRECTION_ID, -_sunLight.transform.forward);
            }

            if (_nightVolume != null) _nightVolume.weight = t * _nightVolumeWeight;

            if (_sunLight != null)
            {
                _sunLight.intensity = Mathf.Lerp(_daySunIntensity, _nightSunIntensity, t);
                _sunLight.color = Color.Lerp(_daySunColor, _nightSunColor, t);
            }

            RenderSettings.ambientIntensity = Mathf.Lerp(_dayAmbientIntensity, _nightAmbientIntensity, t);
            RenderSettings.fogColor = Color.Lerp(_dayFogColor, _nightFogColor, t);
        }

        private void ResolveSunLight()
        {
            if (_sunLight != null) return;

            _sunLight = RenderSettings.sun;
            if (_sunLight != null) return;

            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                _sunLight = light;
                break;
            }
        }

        private void CacheDaySettings()
        {
            if (_sunLight != null)
            {
                _daySunIntensity = _sunLight.intensity;
                _daySunColor = _sunLight.color;
            }

            _dayAmbientIntensity = RenderSettings.ambientIntensity;
            _dayFogColor = RenderSettings.fogColor;
        }

        /// <summary>
        /// 空のマテリアルはアセットを直接書き換えると保存されてしまうので、複製を作ってそれを表示に使う。
        /// </summary>
        private void SetUpRuntimeSkybox()
        {
            Material source = _skyboxMaterial != null ? _skyboxMaterial : RenderSettings.skybox;
            if (source == null)
            {
                Debug.LogWarning("[SkyAtmosphere] 空のマテリアルが設定されていません");
                return;
            }

            _previousSkybox = RenderSettings.skybox;
            _runtimeSkybox = new Material(source);
            RenderSettings.skybox = _runtimeSkybox;
        }

        private void RestoreDaySettings()
        {
            if (_sunLight != null)
            {
                _sunLight.intensity = _daySunIntensity;
                _sunLight.color = _daySunColor;
            }

            RenderSettings.ambientIntensity = _dayAmbientIntensity;
            RenderSettings.fogColor = _dayFogColor;

            if (_previousSkybox != null) RenderSettings.skybox = _previousSkybox;
        }
    }
}
