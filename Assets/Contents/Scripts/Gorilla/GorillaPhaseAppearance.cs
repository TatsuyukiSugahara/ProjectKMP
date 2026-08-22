using ProjectKMP.Battle;
using ProjectKMP.Field;
using ProjectKMP.Player;
using ProjectKMP.Presentation;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// フェーズが上がったときの見た目の変化をまとめて受け持つ。
    ///
    /// HPが半分を切ると体が金色になり、さらに4分の1を切ると空が夜へ変わってオーラを纏う。
    /// 「ここから別物になる」ことを色と環境で見せるのが役目で、強さの数値そのものは GorillaAI 側が持つ。
    ///
    /// フェーズは同期済みのHPから各クライアントがそれぞれ同じ値を出しているので、
    /// この処理は全員の画面でひとりでに揃う(追加の通信は要らない)。
    /// </summary>
    [RequireComponent(typeof(GorillaAI))]
    public class GorillaPhaseAppearance : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
        private static readonly int BASE_MAP_ID = Shader.PropertyToID("_BaseMap");
        private static readonly int TINT_COLOR_ID = Shader.PropertyToID("_TintColor");

        // ---- 設定 ----------------------------------------

        [Header("金色になるフェーズ")]
        [SerializeField, Min(2), Tooltip("このフェーズ以上で体が金色になる。既定の閾値ならフェーズ3=HP50%")]
        private int _goldPhase = 3;

        [SerializeField, ColorUsage(false, true), Tooltip("毛の金色。1を超える値にすると光って見える")]
        private Color _goldColor = new Color(1.25f, 0.82f, 0.22f, 1.0f);

        [SerializeField, Tooltip("色を塗り替えるテクスチャのマス目。ゴリラの色は8x8のパレットを引いて出しているので、毛のマス(5,6)だけを指すと毛だけが金色になる")]
        private Vector2Int[] _furTexels = { new Vector2Int(5, 6) };

        [Header("夜になるフェーズ")]
        [SerializeField, Min(2), Tooltip("このフェーズ以上で空が夜になりオーラを纏う。既定の閾値ならフェーズ4=HP25%")]
        private int _nightPhase = 4;

        [SerializeField, ColorUsage(false, true), Tooltip("夜になってからの毛の色。金色をさらに強める")]
        private Color _rageColor = new Color(1.9f, 1.15f, 0.28f, 1.0f);

        [SerializeField, Tooltip("空を夜に落とす。必殺技と同じ仕組みを使うので、必殺技が終わっても夜のまま残る")]
        private bool _useNightSky = true;

        [Header("オーラ")]
        [SerializeField, Tooltip("纏うオーラのプレハブ(PF_Gorilla_RageAura を想定)")]
        private GameObject _auraPrefab;

        [SerializeField, Min(0.1f), Tooltip("オーラの大きさの倍率。1でプレハブそのまま(ゴリラの実寸に合わせて作ってある)")]
        private float _auraScale = 1.0f;

        [SerializeField, Tooltip("オーラを置く高さ(メートル)")]
        private float _auraHeight = 0.9f;

        [SerializeField, ColorUsage(false, true), Tooltip("オーラの色")]
        private Color _auraColor = new Color(2.0f, 1.3f, 0.25f, 1.0f);

        [Header("フェーズが上がった瞬間の演出")]
        [SerializeField, Min(0f), Tooltip("画面を止める長さ(秒)")]
        private float _burstHitStopSec = 0.09f;

        [SerializeField, Min(0f), Tooltip("カメラ揺れの強さ")]
        private float _burstCameraShake = 0.7f;

        [SerializeField, Min(0f), Tooltip("広がる輪の大きさ(メートル)")]
        private float _burstRingRadius = 9.0f;

        // ---- 内部状態 ------------------------------------

        private GorillaAI _ai;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;

        /// <summary>いま見た目に反映しているフェーズ。ここと実際のフェーズがズレたら切り替える</summary>
        private int _appliedPhase;

        /// <summary>夜をお願い済みか。何度も積むと必殺技が終わったときに戻らなくなる</summary>
        private bool _hasRequestedNight;

        private GameObject _auraInstance;

        /// <summary>毛だけ塗り替えたテクスチャ。作り直すたびに前のものを捨てる</summary>
        private Texture2D _recoloredTexture;

        // ---- Unity ---------------------------------------

        private void Awake()
        {
            _ai = GetComponent<GorillaAI>();
            _block = new MaterialPropertyBlock();

            var found = GetComponentsInChildren<Renderer>(true);
            var list = new System.Collections.Generic.List<Renderer>(found.Length);
            foreach (Renderer renderer in found)
            {
                // パーティクルは色の指定の仕方が違うので触らない
                if (renderer is ParticleSystemRenderer) continue;
                list.Add(renderer);
            }
            _renderers = list.ToArray();
        }

        private void Start()
        {
            // 途中参加したときは、すでに超えているぶんを演出なしでいきなり反映する
            _appliedPhase = _ai != null ? _ai.Phase : 1;
            ApplyPhaseLook(_appliedPhase);
        }

        private void Update()
        {
            if (_ai == null) return;

            int phase = _ai.Phase;
            if (phase <= _appliedPhase) return;

            _appliedPhase = phase;
            ApplyPhaseLook(phase);
            PlayPhaseBurst(phase);
        }

        // ---- 見た目の切り替え ----------------------------

        /// <summary>
        /// そのフェーズの見た目にする。
        /// 途中から色を混ぜていくと中途半端な色の時間ができるので、切り替えは一瞬で行い、
        /// 目の切り替わりは PlayPhaseBurst のフラッシュで隠す。
        /// </summary>
        private void ApplyPhaseLook(int phase)
        {
            if (phase >= _nightPhase)
            {
                ApplyBodyColor(_rageColor);
                EnsureAura(_auraColor, _auraScale);
                RequestNightSky();
                return;
            }

            if (phase >= _goldPhase)
            {
                ApplyBodyColor(_goldColor);
                return;
            }

            ClearBodyColor();
        }

        /// <summary>
        /// 毛のところだけ塗り替える。
        ///
        /// ゴリラの色は8x8のパレットテクスチャを引いて出していて、毛・素肌・目がそれぞれ別のマスを見ている。
        /// 全体に色を掛けると目や顔まで金色になってしまうので、パレットを1枚コピーして
        /// 毛のマスだけ書き換えたものを描画側に渡す。マテリアル本体は触らないので、
        /// 他の場所のゴリラや元アセットには影響しない。
        /// </summary>
        private void ApplyBodyColor(Color furColor)
        {
            if (_renderers == null) return;

            Texture2D palette = BuildFurRecoloredTexture(furColor);
            if (palette == null) return;

            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null) continue;

                renderer.GetPropertyBlock(_block, 0);
                _block.SetTexture(BASE_MAP_ID, palette);
                _block.SetColor(BASE_COLOR_ID, Color.white);
                renderer.SetPropertyBlock(_block, 0);
            }
        }

        /// <summary>
        /// 元のパレットを1枚コピーして、毛のマスと同じ色のところだけ塗り替えたものを作る。
        ///
        /// 元テクスチャは読み取り不可の設定なので、そのままでは中身を取り出せない。
        /// いちど描画してから読み戻すことで、インポート設定を変えずに色を取り出している。
        /// 保存先を16bit浮動小数にしているのは、1を超える明るさ(光って見える金色)を持たせるため。
        /// </summary>
        private Texture2D BuildFurRecoloredTexture(Color furColor)
        {
            Texture source = FindSourceBaseMap();
            if (source == null) return null;

            int width = source.width;
            int height = source.height;

            RenderTexture temporary = RenderTexture.GetTemporary(
                width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;

            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;

            var palette = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
            palette.filterMode = FilterMode.Point;
            palette.wrapMode = TextureWrapMode.Clamp;
            palette.ReadPixels(new Rect(0.0f, 0.0f, width, height), 0, 0);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);

            RepaintMatchingTexels(palette, furColor);
            palette.Apply(false, false);

            if (_recoloredTexture != null) Destroy(_recoloredTexture);
            _recoloredTexture = palette;
            return palette;
        }

        /// <summary>
        /// 指定したマスと同じ色のマスをまとめて塗り替える。
        /// パレットは同じ色が何マスも並んでいるので、1マスだけ塗ると継ぎ目が出ることがある。
        /// </summary>
        private void RepaintMatchingTexels(Texture2D palette, Color furColor)
        {
            if (_furTexels == null) return;

            foreach (Vector2Int texel in _furTexels)
            {
                int x = Mathf.Clamp(texel.x, 0, palette.width - 1);
                int y = Mathf.Clamp(texel.y, 0, palette.height - 1);
                Color reference = palette.GetPixel(x, y);

                for (int py = 0; py < palette.height; py++)
                {
                    for (int px = 0; px < palette.width; px++)
                    {
                        if (!IsSameColor(palette.GetPixel(px, py), reference)) continue;
                        palette.SetPixel(px, py, furColor);
                    }
                }
            }
        }

        private static bool IsSameColor(Color a, Color b)
        {
            const float TOLERANCE = 0.004f;
            return Mathf.Abs(a.r - b.r) < TOLERANCE
                && Mathf.Abs(a.g - b.g) < TOLERANCE
                && Mathf.Abs(a.b - b.b) < TOLERANCE;
        }

        /// <summary>塗り替えの元になるテクスチャを、いま描画に使っているマテリアルから探す</summary>
        private Texture FindSourceBaseMap()
        {
            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null) continue;

                UnityEngine.Material material = renderer.sharedMaterial;
                if (material == null) continue;
                if (!material.HasProperty(BASE_MAP_ID)) continue;

                Texture texture = material.GetTexture(BASE_MAP_ID);
                if (texture != null) return texture;
            }
            return null;
        }

        private void ClearBodyColor()
        {
            if (_renderers == null) return;

            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null) continue;

                _block.Clear();
                renderer.SetPropertyBlock(_block, 0);
            }
        }

        private void OnDestroy()
        {
            if (_recoloredTexture != null) Destroy(_recoloredTexture);
        }

        /// <summary>
        /// オーラを1つだけ出す。すでに出ていれば何もしない。
        ///
        /// ゴリラはシーンで拡大して置かれているので、そのまま付けると倍率ぶん大きくなってしまう。
        /// 見た目の大きさと高さをメートルで指定できるように、親の拡大率で割り戻している。
        /// </summary>
        private void EnsureAura(Color color, float scale)
        {
            if (_auraPrefab == null || _auraInstance != null) return;

            float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);

            _auraInstance = Instantiate(_auraPrefab, transform);
            _auraInstance.transform.localPosition = Vector3.up * (_auraHeight / parentScale);
            _auraInstance.transform.localRotation = Quaternion.identity;
            _auraInstance.transform.localScale = Vector3.one * (scale / parentScale);

            var simple = _auraInstance.GetComponent<SimpleAuraEffect>();
            if (simple != null) simple.SetBaseScale(_auraInstance.transform.localScale);

            TintAura(color);
        }

        /// <summary>
        /// オーラに色を乗せる。
        /// パーティクルは色の指定の仕方が違う(マテリアルではなく粒の開始色)ので、両方に対応しておく。
        /// 濃さは作り込んだ側の値を残したいので、透明度だけはプレハブのものを引き継ぐ。
        /// </summary>
        private void TintAura(Color color)
        {
            var systems = _auraInstance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.MainModule main = system.main;
                float alpha = main.startColor.color.a;
                main.startColor = new Color(color.r, color.g, color.b, alpha);
            }

            var lights = _auraInstance.GetComponentsInChildren<Light>(true);
            foreach (Light light in lights)
            {
                // ライトの色は1を超えられないので、明るさぶんは Intensity 側に任せて色味だけ合わせる
                light.color = new Color(
                    Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1.0f);
            }

            if (systems.Length > 0) return;

            var renderer = _auraInstance.GetComponent<Renderer>();
            if (renderer == null) return;

            renderer.GetPropertyBlock(_block);
            _block.SetColor(TINT_COLOR_ID, color);
            renderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// 空を夜に落とす。必殺技と同じ仕組み(お願いした数を数える方式)なので、
        /// こちらがお願いを持ち続けているかぎり、必殺技が終わって1つ返しても夜のまま残る。
        /// </summary>
        private void RequestNightSky()
        {
            if (!_useNightSky || _hasRequestedNight) return;

            _hasRequestedNight = true;
            SkyAtmosphere.RequestNight();
            Debug.Log("[Gorilla] フェーズが上がったので空を夜にしました");
        }

        // ---- 切り替わった瞬間の演出 ----------------------

        private void PlayPhaseBurst(int phase)
        {
            if (phase < _goldPhase) return;

            bool isRage = phase >= _nightPhase;
            Color color = isRage ? _auraColor : _goldColor;
            Color flat = new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1.0f);

            HitStop.Play(_burstHitStopSec, 0.06f, 0.14f);
            ScreenFlash.Play(new Color(flat.r, flat.g, flat.b, isRage ? 0.5f : 0.38f), isRage ? 0.35f : 0.26f);
            HitFlash.Play(transform, flat, 0.7f, 1.0f);

            var camera = Object.FindAnyObjectByType<ThirdPersonCamera>();
            if (camera != null) camera.Shake(_burstCameraShake * (isRage ? 1.3f : 1.0f), 0.5f);

            // 輪を少しずつ遅らせて3枚。1枚だけだと一瞬で終わってしまう
            float radius = _burstRingRadius * (isRage ? 1.35f : 1.0f);
            ShockwaveRing.Play(transform.position, flat, radius, 0.55f, 1.4f);
            ShockwaveRing.Play(transform.position, flat, radius * 0.7f, 0.75f, 1.0f);
            ShockwaveRing.Play(transform.position, flat, radius * 1.25f, 0.95f, 0.7f);

            GrassField.FlattenAt(transform.position, radius * 0.6f, 1.0f);

            Onomatopoeia.Play(
                transform.position + Vector3.up * 3.8f,
                isRage ? "極・激昂" : "激昂",
                flat, isRage ? 1.7f : 1.35f, 1.0f);

            BgmPlayer.Duck(isRage ? 0.75f : 0.55f, 0.25f, 0.7f);

            Debug.Log($"[Gorilla] フェーズ{phase}の見た目に切り替えました");
        }
    }
}
