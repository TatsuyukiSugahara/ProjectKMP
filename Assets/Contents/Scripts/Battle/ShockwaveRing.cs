using UnityEngine;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 地面を走る衝撃波の輪。
    ///
    /// 着地や爆発の瞬間に、平たい輪が外へ広がって消える。
    /// トゥーン調の絵では、煙や火花よりもこれが一番『効いた』感じを作る。
    ///
    /// 線を並べて描くので、画像もマテリアルも用意しなくてよい。
    /// 出したら自分で消えるので、呼ぶ側は後始末を気にしなくてよい。
    /// </summary>
    public class ShockwaveRing : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>輪を作る点の数。増やすほど滑らかになる</summary>
        private const int SEGMENTS = 48;

        /// <summary>地面から浮かせる高さ(メートル)。埋まるとちらつくので少し上げる</summary>
        private const float GROUND_OFFSET = 0.08f;

        // ---- 内部状態 ------------------------------------

        private LineRenderer _line;
        private Material _materialInstance;

        private float _elapsed;
        private float _duration = 0.45f;
        private float _startRadius;
        private float _endRadius = 6.0f;
        private float _startWidth = 0.8f;
        private Color _color = Color.white;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 衝撃波を1つ出す。
        /// endRadius は『どこまで広がるか』、durationSec は『どれだけ速いか』。
        /// 速いほど鋭く、遅いほど重い衝撃に見える。
        /// </summary>
        public static void Play(
            Vector3 position, Color color, float endRadius = 6.0f, float durationSec = 0.45f, float width = 0.8f)
        {
            var go = new GameObject("ShockwaveRing");
            go.transform.position = position + Vector3.up * GROUND_OFFSET;

            var ring = go.AddComponent<ShockwaveRing>();
            ring._color = color;
            ring._endRadius = Mathf.Max(0.1f, endRadius);
            ring._duration = Mathf.Max(0.05f, durationSec);
            ring._startWidth = Mathf.Max(0.01f, width);
            ring._startRadius = endRadius * 0.15f;

            ring.Setup();
        }

        // ---- 内部処理 ------------------------------------

        private void Setup()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = SEGMENTS + 1;
            _line.useWorldSpace = false;
            _line.loop = false;
            _line.alignment = LineAlignment.TransformZ;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            _materialInstance = new Material(ResolveShader());
            _line.material = _materialInstance;

            Apply(0.0f);
        }

        /// <summary>描画に使えるシェーダーを探す。環境で名前が違うので順に当たる</summary>
        private static Shader ResolveShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            return shader;
        }

        private void Update()
        {
            // ヒットストップ中に出すことがあるので、実時間で進める
            _elapsed += Time.unscaledDeltaTime;

            float t = _elapsed / _duration;
            if (t >= 1.0f) { Destroy(gameObject); return; }

            Apply(t);
        }

        private void Apply(float t)
        {
            if (_line == null) return;

            // 最初に一気に広がり、終わりへ向けて緩む。等速だと『広がった』だけで衝撃に見えない
            float eased = 1.0f - (1.0f - t) * (1.0f - t) * (1.0f - t);
            float radius = Mathf.Lerp(_startRadius, _endRadius, eased);

            for (int i = 0; i <= SEGMENTS; i++)
            {
                float angle = i / (float)SEGMENTS * Mathf.PI * 2.0f;

                // 地面に寝かせたいので、XZ 平面に置く
                _line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius));
            }

            // 広がるほど細く薄く。太いまま広がると輪ではなく円盤に見える
            float fade = 1.0f - t;
            float width = _startWidth * fade;

            _line.startWidth = width;
            _line.endWidth = width;

            var color = new Color(_color.r, _color.g, _color.b, fade);
            _line.startColor = color;
            _line.endColor = color;

            if (_materialInstance == null) return;

            // シェーダーごとに色のプロパティ名が違うので、あるものすべてに入れる
            if (_materialInstance.HasProperty("_BaseColor")) _materialInstance.SetColor("_BaseColor", color);
            if (_materialInstance.HasProperty("_Color")) _materialInstance.SetColor("_Color", color);
            if (_materialInstance.HasProperty("_TintColor")) _materialInstance.SetColor("_TintColor", color);
        }

        private void OnDestroy()
        {
            if (_materialInstance != null) Destroy(_materialInstance);
        }
    }
}
