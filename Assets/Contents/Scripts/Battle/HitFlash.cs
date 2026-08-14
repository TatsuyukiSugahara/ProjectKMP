using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 殴られた相手をじわっと光らせる。
    ///
    /// 手応えは殴る側ではなく殴られる側で作るほうが伝わる。
    /// 殴った側の演出をいくら足しても、相手が無反応だと当たった気がしない。
    ///
    /// 相手のスクリプトには一切触らない。当てた側から呼ぶと、
    /// この部品が相手へ勝手に取り付いて、見た目だけを一時的に変える。
    /// 担当が分かれているものへ手を入れずに反応を足すための作り。
    ///
    /// やり方は『元の見た目の上に光を足す』。真っ白に差し替えると強すぎるうえ、
    /// 相手が何だったのか一瞬わからなくなる。足す方式なら元の形と色が残る。
    /// </summary>
    public class HitFlash : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>光の強さの上限。1にすると白飛びするので、これくらいが上限</summary>
        private const float MAX_STRENGTH = 0.38f;

        // ---- 内部状態 ------------------------------------

        /// <summary>光を足す対象と、その元のマテリアル</summary>
        private class Entry
        {
            public Renderer Renderer;
            public Material[] Original;
            public Material[] WithGlow;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        /// <summary>上に重ねる光。強さを個別に変えるので、相手ごとに1つ持つ</summary>
        private Material _glowMaterial;

        private float _remainSec;
        private float _durationSec;
        private float _strength;
        private Color _color = Color.white;
        private bool _showing;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 相手を光らせる。相手側に何も用意されていなくても動く。
        /// durationSec は 0.08〜0.2 くらいが目安。長いと『光っている敵』になってしまう。
        /// strength は光の強さで、1.0 で上限。
        /// </summary>
        public static void Play(Transform target, Color color, float durationSec = 0.15f, float strength = 1.0f)
        {
            if (target == null) return;

            HitFlash flash = target.GetComponent<HitFlash>();
            if (flash == null) flash = target.gameObject.AddComponent<HitFlash>();

            flash.Begin(color, durationSec, strength);
        }

        /// <summary>白でひと光り。普段はこちらで足りる</summary>
        public static void PlayWhite(Transform target, float durationSec = 0.15f, float strength = 1.0f)
        {
            Play(target, Color.white, durationSec, strength);
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            _glowMaterial = CreateGlowMaterial();
            Collect();
        }

        private void OnDestroy()
        {
            if (_glowMaterial != null) Destroy(_glowMaterial);
        }

        /// <summary>光を足す対象を集める。相手の見た目は増えないので一度でよい</summary>
        private void Collect()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;

                Material[] original = renderer.sharedMaterials;
                if (original == null || original.Length == 0) continue;

                // 元のマテリアルの後ろに光を1枚足す。元の描画はそのまま残る
                var withGlow = new Material[original.Length + 1];
                for (int i = 0; i < original.Length; i++) withGlow[i] = original[i];
                withGlow[original.Length] = _glowMaterial;

                _entries.Add(new Entry { Renderer = renderer, Original = original, WithGlow = withGlow });
            }
        }

        /// <summary>
        /// 上に重ねるための光のマテリアルを作る。
        /// 色を『足す』設定にすることで、元の色を消さずに明るくできる。
        /// </summary>
        private static Material CreateGlowMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            var material = new Material(shader) { name = "HitFlashGlow" };

            // 半透明として扱わせたうえで、重ね方を『足し算』にする
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1.0f);
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)BlendMode.One);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;

            SetGlowColor(material, Color.clear);
            return material;
        }

        private static void SetGlowColor(Material material, Color color)
        {
            if (material == null) return;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private void Begin(Color color, float durationSec, float strength)
        {
            _color = color;
            _strength = Mathf.Clamp01(strength) * MAX_STRENGTH;
            _durationSec = Mathf.Max(0.02f, durationSec);
            _remainSec = _durationSec;

            if (_showing) return;

            _showing = true;
            foreach (Entry entry in _entries)
            {
                if (entry.Renderer == null) continue;

                entry.Renderer.sharedMaterials = entry.WithGlow;
            }
        }

        private void Update()
        {
            if (!_showing) return;

            // ヒットストップで時間が止まっている最中に出すので、実時間で数える
            _remainSec -= Time.unscaledDeltaTime;

            if (_remainSec <= 0.0f) { Hide(); return; }

            // ぱっと点いて、じわっと引く。角を丸めた減り方にして急に消えないようにする
            float ratio = _remainSec / _durationSec;
            float amount = _strength * ratio * ratio;

            SetGlowColor(_glowMaterial, new Color(_color.r * amount, _color.g * amount, _color.b * amount, amount));
        }

        /// <summary>重ねた光を外して元通りにする</summary>
        private void Hide()
        {
            _showing = false;
            _remainSec = 0.0f;

            SetGlowColor(_glowMaterial, Color.clear);

            foreach (Entry entry in _entries)
            {
                if (entry.Renderer == null) continue;

                entry.Renderer.sharedMaterials = entry.Original;
            }
        }

        private void OnDisable()
        {
            if (_showing) Hide();
        }
    }
}
