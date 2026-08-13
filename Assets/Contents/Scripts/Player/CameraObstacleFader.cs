using System.Collections.Generic;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// カメラと自分の間に入った木を、ディザリングで透かす。
    ///
    /// 木には当たり判定が無いので、物理ではなく見た目の範囲(Renderer の箱)で
    /// 線を遮っているかを調べる。木は動かないので、この調べ方でも十分足りる。
    ///
    /// 半透明にせずディザリング(画素を市松に抜く)にしているのは、
    /// 半透明にすると描画順が絡んで、木の向こうの木が透けて見えるなどの崩れが出るため。
    ///
    /// 透かしている間だけ専用のマテリアルに差し替え、戻るときは元へ返す。
    /// </summary>
    public class CameraObstacleFader : MonoBehaviour
    {
        // ---- 設定 ----------------------------------------

        [Header("対象")]
        [SerializeField, Tooltip("透かす対象をまとめている親。未設定なら Trees という名前を探す")]
        private Transform _targetsRoot;

        [SerializeField, Tooltip("ディザリング用のシェーダー。未設定なら透かさない")]
        private Shader _ditherShader;

        [Header("判定")]
        [SerializeField, Min(0.0f), Tooltip("注視点の高さ。カメラ本体の設定と合わせる(メートル)")]
        private float _focusHeight = 1.5f;

        [SerializeField, Min(0.0f), Tooltip("遮っていると見なす太さ(メートル)。大きいほど早く透ける")]
        private float _blockRadius = 0.7f;

        [SerializeField, Min(0.0f), Tooltip("カメラの手前これだけは無視する(メートル)。顔の真横の木で透けないようにする")]
        private float _nearSkip = 0.5f;

        [Header("透け方")]
        [SerializeField, Range(0.0f, 1.0f), Tooltip("透かしたときの濃さ。0で完全に消える")]
        private float _fadedAmount = 0.25f;

        [SerializeField, Min(0.1f), Tooltip("透ける・戻るの速さ。大きいほどキビキビする")]
        private float _fadeSpeed = 6.0f;

        // ---- 内部状態 ------------------------------------

        /// <summary>透かす候補1本ぶんの控え</summary>
        private class Entry
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
            public Material[] DitherMaterials;
            public float Amount = 1.0f;
            public bool Swapped;
        }

        private static readonly int FADE_ID = Shader.PropertyToID("_Fade");
        private static readonly int BASE_MAP_ID = Shader.PropertyToID("_BaseMap");
        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<Material, Material> _ditherCache = new Dictionary<Material, Material>();

        private ThirdPersonCamera _cameraController;
        private MaterialPropertyBlock _block;

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            _cameraController = GetComponent<ThirdPersonCamera>();
            if (_cameraController == null) _cameraController = FindAnyObjectByType<ThirdPersonCamera>();

            _block = new MaterialPropertyBlock();

            Collect();
        }

        private void OnDestroy()
        {
            // 差し替え用に作ったマテリアルは自分で片付ける
            foreach (var material in _ditherCache.Values)
            {
                if (material != null) Destroy(material);
            }

            _ditherCache.Clear();
        }

        /// <summary>透かす候補を集める。木は増えないので一度だけでよい</summary>
        private void Collect()
        {
            Transform root = _targetsRoot;
            if (root == null)
            {
                GameObject found = GameObject.Find("Trees");
                if (found != null) root = found.transform;
            }

            if (root == null || _ditherShader == null) return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;

                Material[] originals = renderer.sharedMaterials;
                var dithers = new Material[originals.Length];

                for (int i = 0; i < originals.Length; i++) dithers[i] = ResolveDitherMaterial(originals[i]);

                _entries.Add(new Entry
                {
                    Renderer = renderer,
                    OriginalMaterials = originals,
                    DitherMaterials = dithers,
                });
            }
        }

        /// <summary>
        /// 元のマテリアルに見た目を合わせた、ディザリング用のマテリアルを返す。
        /// 同じ元マテリアルには同じものを使い回す(木はほぼ全部同じ材質のため)。
        /// </summary>
        private Material ResolveDitherMaterial(Material source)
        {
            if (source == null) return null;
            if (_ditherCache.TryGetValue(source, out Material cached)) return cached;

            var material = new Material(_ditherShader);

            if (source.HasProperty(BASE_MAP_ID)) material.SetTexture(BASE_MAP_ID, source.GetTexture(BASE_MAP_ID));
            if (source.HasProperty(BASE_COLOR_ID)) material.SetColor(BASE_COLOR_ID, source.GetColor(BASE_COLOR_ID));

            _ditherCache.Add(source, material);
            return material;
        }

        private void LateUpdate()
        {
            if (_entries.Count == 0) return;

            Transform target = _cameraController != null ? _cameraController.Target : null;
            if (target == null) return;

            Vector3 focus = target.position + Vector3.up * _focusHeight;
            Vector3 toCamera = transform.position - focus;

            float length = toCamera.magnitude;
            if (length <= 0.01f) return;

            Vector3 direction = toCamera / length;

            // カメラのすぐ手前は見ない。顔の真横にある木で画面が透けるのを防ぐ
            float checkLength = Mathf.Max(0.0f, length - _nearSkip);

            var ray = new Ray(focus, direction);

            foreach (Entry entry in _entries)
            {
                if (entry.Renderer == null) continue;

                bool blocking = entry.Renderer.isVisible && IsBlocking(entry.Renderer, ray, checkLength);
                float goal = blocking ? _fadedAmount : 1.0f;

                entry.Amount = Mathf.MoveTowards(entry.Amount, goal, _fadeSpeed * Time.deltaTime);

                Apply(entry);
            }
        }

        /// <summary>見た目の箱が、注視点からカメラへの線を遮っているか</summary>
        private bool IsBlocking(Renderer renderer, Ray ray, float length)
        {
            Bounds bounds = renderer.bounds;

            // 線を太さのある棒として扱いたいので、箱のほうを膨らませて代用する
            bounds.Expand(_blockRadius * 2.0f);

            if (!bounds.IntersectRay(ray, out float distance)) return false;

            return distance <= length;
        }

        /// <summary>いまの濃さを見た目へ反映する。完全に戻ったら元のマテリアルへ返す</summary>
        private void Apply(Entry entry)
        {
            bool needDither = entry.Amount < 0.999f;

            if (needDither != entry.Swapped)
            {
                entry.Renderer.sharedMaterials = needDither ? entry.DitherMaterials : entry.OriginalMaterials;
                entry.Swapped = needDither;

                // 元に戻すときは上書きも消す。残すと元のマテリアルに影響が出る
                if (!needDither) { entry.Renderer.SetPropertyBlock(null); return; }
            }

            if (!needDither) return;

            entry.Renderer.GetPropertyBlock(_block);
            _block.SetFloat(FADE_ID, entry.Amount);
            entry.Renderer.SetPropertyBlock(_block);
        }
    }
}
