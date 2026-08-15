using System.Collections.Generic;
using ProjectKMP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectKMP.Presentation;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// ボスの最後の1本に入ったときの演出をまとめる。
    ///
    /// 『あと少し』が伝わるかどうかで、終盤の集中の仕方が変わる。
    /// 赤いオーラ・画面の縁・曲の張り詰めを一度に切り替えて、
    /// 世界の空気が変わったことを見せる。
    ///
    /// ボスのスクリプトには触らない。当てた側から光らせるのと同じで、
    /// 相手の見た目へ後から重ねる形にしている。
    /// </summary>
    public class FinalPhaseDirector : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private const int SORTING_ORDER = 450;

        /// <summary>オーラの濃さの上限。強すぎるとボスの形が潰れる</summary>
        private const float AURA_MAX = 0.85f;

        /// <summary>脈打ちの速さ(1秒あたりの回数)</summary>
        private const float PULSE_HZ = 1.4f;

        // 出しておく時間(秒)。長いと戦いの邪魔になる
        private const float BANNER_SEC = 1.6f;

        // ---- 内部状態 ------------------------------------

        private static FinalPhaseDirector _instance;

        private readonly List<Renderer> _bossRenderers = new List<Renderer>();
        private readonly Dictionary<Renderer, Material[]> _originals = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Renderer, Material[]> _withAura = new Dictionary<Renderer, Material[]>();

        private Material _auraMaterial;
        private Light _auraLight;
        private TMP_Text _banner;
        private float _bannerElapsed;
        private bool _active;
        private float _elapsed;

        // ---- 公開API -------------------------------------

        /// <summary>最終フェーズへ入る</summary>
        public static void Begin()
        {
            if (_instance != null && _instance._active) return;

            Ensure();
            if (_instance == null) return;

            _instance.BeginInternal();
        }

        /// <summary>元へ戻す。撃破やリタイアのときに呼ぶ</summary>
        public static void End()
        {
            if (_instance == null || !_instance._active) return;

            _instance.EndInternal();
        }

        private static void Ensure()
        {
            if (_instance != null) return;

            var go = new GameObject(nameof(FinalPhaseDirector));
            _instance = go.AddComponent<FinalPhaseDirector>();
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }

            _instance = this;
        }

        private void OnDestroy()
        {
            if (_active) RestoreRenderers();
            if (_auraMaterial != null) Destroy(_auraMaterial);

            if (_instance == this) _instance = null;
        }

        private void BeginInternal()
        {
            _active = true;
            _elapsed = 0.0f;

            CollectBoss();
            ApplyAura(true);
            AttachLight();

            // 曲を1割ほど速く高くする。同じ曲でも『急かす曲』に変わる
            BgmPlayer.SetTension(1.08f, 1.0f, 1.2f);

            // 切り替わりを1回だけ強く見せる
            ImpactFrame.Play(new Color(1.0f, 0.25f, 0.2f, 0.6f), 0.08f);
            HitStop.Play(0.12f, 0.08f, 0.25f);

            Announce();
        }

        private void EndInternal()
        {
            _active = false;

            ApplyAura(false);
            BgmPlayer.ResetTension();

            if (_auraLight != null) Destroy(_auraLight.gameObject);

            // 合図が出たまま決着へ入ると、撮った絵に文字が写り込む
            if (_banner != null) _banner.gameObject.SetActive(false);

        }

        private void Update()
        {
            UpdateBanner();

            if (!_active) return;

            _elapsed += Time.unscaledDeltaTime;

            // ゆっくりした脈。速すぎると目が疲れ、遅すぎると気づかれない
            float pulse = 0.55f + 0.45f * Mathf.Sin(_elapsed * PULSE_HZ * Mathf.PI * 2.0f);

            if (_auraMaterial != null)
            {
                float amount = AURA_MAX * pulse;
                SetColor(_auraMaterial, new Color(1.0f * amount, 0.14f * amount, 0.08f * amount, amount));
            }

            // 光そのものも脈打たせる。マテリアルの重ねだけでは
            // 元の色が濃い相手に負けて、遠目には気づかれない
            if (_auraLight != null) _auraLight.intensity = Mathf.Lerp(1.5f, 6.0f, pulse);
        }

        // ---- ボスへ重ねる --------------------------------

        /// <summary>ボスの見た目を集める。相手のスクリプトには触らない</summary>
        private void CollectBoss()
        {
            _bossRenderers.Clear();
            _originals.Clear();
            _withAura.Clear();

            var boss = FindAnyObjectByType<Monster.BossHealth>(FindObjectsInactive.Include);
            if (boss == null) return;

            if (_auraMaterial == null) _auraMaterial = CreateAuraMaterial();

            foreach (Renderer renderer in boss.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;

                Material[] original = renderer.sharedMaterials;
                if (original == null || original.Length == 0) continue;

                // 元のマテリアルの後ろに光を1枚足す。元の描画はそのまま残る
                var withAura = new Material[original.Length + 1];
                for (int i = 0; i < original.Length; i++) withAura[i] = original[i];
                withAura[original.Length] = _auraMaterial;

                _bossRenderers.Add(renderer);
                _originals[renderer] = original;
                _withAura[renderer] = withAura;
            }
        }

        private void ApplyAura(bool on)
        {
            foreach (Renderer renderer in _bossRenderers)
            {
                if (renderer == null) continue;

                renderer.sharedMaterials = on ? _withAura[renderer] : _originals[renderer];
            }

            if (on || _auraMaterial == null) return;

            SetColor(_auraMaterial, Color.clear);
        }

        // ボスの体の中へ赤い光を置く。
        //
        // 色を重ねるだけだと、元の色が濃い相手では見分けが付かない。
        // 光は周りの地面や仲間まで赤く染めるので、遠目にも空気の変化が伝わる。
        private void AttachLight()
        {
            if (_auraLight != null) return;

            var boss = FindAnyObjectByType<Monster.BossHealth>(FindObjectsInactive.Include);
            if (boss == null) return;

            var go = new GameObject("FinalPhaseAuraLight");
            go.transform.SetParent(boss.transform, false);
            go.transform.localPosition = Vector3.up * 2.0f;

            _auraLight = go.AddComponent<Light>();
            _auraLight.type = LightType.Point;
            _auraLight.color = new Color(1.0f, 0.16f, 0.10f, 1.0f);
            _auraLight.range = 14.0f;
            _auraLight.intensity = 3.0f;

            // 影を落とすと重いうえ、赤い影が地面に出て汚くなる
            _auraLight.shadows = LightShadows.None;
        }

        private void RestoreRenderers()
        {
            ApplyAura(false);
        }

        /// <summary>色を足す設定のマテリアル。元の色を消さずに赤く光らせる</summary>
        private static Material CreateAuraMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            var material = new Material(shader) { name = "FinalPhaseAura" };

            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1.0f);
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)BlendMode.One);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;

            SetColor(material, Color.clear);
            return material;
        }

        private static void SetColor(Material material, Color color)
        {
            if (material == null) return;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        // ---- 画面の合図 ----------------------------------

        // 画面の真ん中あたりに一度だけ出して、すっと引く。
        // 出しっぱなしにすると戦いの邪魔になるので、伝わったら消す。
        private void ShowBanner()
        {
            if (_banner == null) BuildBanner();
            if (_banner == null) return;

            _bannerElapsed = 0.0f;
            _banner.gameObject.SetActive(true);
        }

        private void UpdateBanner()
        {
            if (_banner == null || !_banner.gameObject.activeSelf) return;

            _bannerElapsed += Time.unscaledDeltaTime;

            if (_bannerElapsed >= BANNER_SEC) { _banner.gameObject.SetActive(false); return; }

            float t = _bannerElapsed / BANNER_SEC;

            // 飛び出して、行きすぎてから収まる。まっすぐ大きくすると弾けた感じが出ない
            float scale = t < 0.18f
                ? Mathf.Lerp(0.4f, 1.25f, t / 0.18f)
                : Mathf.Lerp(1.25f, 1.0f, (t - 0.18f) / 0.82f);

            _banner.rectTransform.localScale = Vector3.one * scale;

            float alpha = t < 0.7f ? 1.0f : 1.0f - (t - 0.7f) / 0.3f;
            Color color = _banner.color;
            _banner.color = new Color(color.r, color.g, color.b, alpha);
        }

        private void BuildBanner()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);

            var textObject = new GameObject("Banner", typeof(RectTransform));
            textObject.transform.SetParent(canvasObject.transform, false);

            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1400.0f, 200.0f);
            rect.anchoredPosition = new Vector2(0.0f, 180.0f);

            _banner = textObject.AddComponent<TextMeshProUGUI>();
            _banner.text = "さいごの いっぽん！";
            _banner.fontSize = 96.0f;
            _banner.alignment = TextAlignmentOptions.Center;
            _banner.color = new Color(1.0f, 0.32f, 0.22f, 1.0f);
            _banner.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _banner.raycastTarget = false;

            // 画面のどこかに出ている文字からフォントを借りる
            foreach (TMP_Text sample in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (sample == null || sample.font == null) continue;

                _banner.font = sample.font;
                break;
            }

            _banner.fontMaterial.EnableKeyword("OUTLINE_ON");
            _banner.outlineWidth = 0.26f;
            _banner.outlineColor = new Color32(40, 8, 8, 255);

            textObject.SetActive(false);
        }

        // ---- 合図 ----------------------------------------

        /// <summary>切り替わったことを言葉でも伝える</summary>
        private void Announce()
        {
            var boss = FindAnyObjectByType<Monster.BossHealth>(FindObjectsInactive.Include);
            Vector3 position = boss != null
                ? boss.transform.position + Vector3.up * 4.0f
                : Vector3.zero;

            // 文字は画面へ直接出す。世界の中へ置くと、
            // ボスが画面の外にいるときに誰も気づかない
            ShowBanner();

            ShockwaveRing.Play(position, new Color(1.0f, 0.3f, 0.2f, 1.0f), 16.0f, 0.7f, 1.4f);

            BgmPlayer.Duck(0.7f, 0.25f, 0.8f);
        }
    }
}
