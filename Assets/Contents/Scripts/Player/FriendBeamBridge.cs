using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 合体したプレイヤー同士をつなぐ光の橋。
    ///
    /// 画面のUIと違って、これはワールドに置かれるので視界を塞がない。
    /// 『この2人が合わせた』という一番大事な情報を、
    /// 画面を占領せずに伝えるのがこの表示の役目。
    ///
    /// 相手が画面の外にいると見えないので、画面枠の表示と組で使う。
    /// 合体は全員の画面で成立するため、これは参加していない人の画面にも出す
    /// (見ている側にも協力が起きたことが伝わる)。
    /// </summary>
    public class FriendBeamBridge : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>橋を分ける点の数。増やすほど弧がなめらかになる</summary>
        private const int POINT_COUNT = 20;

        /// <summary>胸の高さ(足元からのオフセット・m)。足元同士を結ぶと地面に埋まる</summary>
        private const float CHEST_HEIGHT = 1.2f;

        /// <summary>橋の反りを2人の距離の何倍にするか</summary>
        private const float ARC_RATIO = 0.18f;

        /// <summary>橋の太さ(m)</summary>
        private const float WIDTH = 0.35f;

        // ---- 内部状態 ------------------------------------

        private LineRenderer _line;
        private Material _materialInstance;

        private Transform _from;
        private Transform _to;
        private Color _color = Color.white;

        private float _elapsed;
        private float _duration = 0.8f;

        // ---- 公開API -------------------------------------

        /// <summary>2人の間に光の橋を渡す。どちらかが消えたら橋も消える</summary>
        public static void Play(Transform from, Transform to, Color color, float durationSec)
        {
            if (from == null || to == null) return;

            var go = new GameObject("FriendBeamBridge");
            var bridge = go.AddComponent<FriendBeamBridge>();

            bridge._from = from;
            bridge._to = to;
            bridge._color = color;
            bridge._duration = Mathf.Max(0.1f, durationSec);
            bridge.Setup();
        }

        // ---- 内部処理 ------------------------------------

        private void Setup()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = POINT_COUNT;
            _line.useWorldSpace = true;
            _line.numCapVertices = 4;
            _line.alignment = LineAlignment.View;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            _materialInstance = new Material(ResolveShader());
            _line.material = _materialInstance;

            SetColor(1.0f);
            UpdateShape(1.0f);
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
            // 撃っている最中に出るので、ヒットストップで時間が落ちていても実時間で進める
            _elapsed += Time.unscaledDeltaTime;

            float t = _elapsed / _duration;
            if (t >= 1.0f || _from == null || _to == null) { Destroy(gameObject); return; }

            // 終わりに向けて細く薄くする。パッと消えるより自然につながる
            float remain = 1.0f - t;

            UpdateShape(remain);
            SetColor(remain);
        }

        /// <summary>2人を弧で結ぶ。直線だと地形に埋まりやすく、つながった感じも薄い</summary>
        private void UpdateShape(float remain)
        {
            if (_line == null || _from == null || _to == null) return;

            Vector3 start = _from.position + Vector3.up * CHEST_HEIGHT;
            Vector3 end = _to.position + Vector3.up * CHEST_HEIGHT;
            float arc = Vector3.Distance(start, end) * ARC_RATIO;

            for (int i = 0; i < POINT_COUNT; i++)
            {
                float k = i / (float)(POINT_COUNT - 1);

                // 中央がいちばん高くなる反り
                Vector3 point = Vector3.Lerp(start, end, k) + Vector3.up * (arc * 4.0f * k * (1.0f - k));
                _line.SetPosition(i, point);
            }

            float width = WIDTH * remain;
            _line.startWidth = width;
            _line.endWidth = width;
        }

        private void SetColor(float alpha)
        {
            if (_line == null) return;

            var color = new Color(_color.r, _color.g, _color.b, alpha);

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
