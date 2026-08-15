using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 『いま撃てば友達ビームになる』ことを見せる合図。
    ///
    /// 誰かがビームの狙いに入っている間と、撃ってからの受付時間だけ、
    /// 画面の端をうっすら光らせて『合わせろ』の文字を出し、
    /// 呼びかけている相手のほうへ矢印を向ける。
    ///
    /// 合図が無いと、来場者は合体ビームの存在に気づかないまま終わる。
    /// 声を掛け合うきっかけを作るのがこの表示の役目。
    ///
    /// 表示に必要なものは自分で組み立てるので、シーンへの事前配置は要らない。
    /// 出す相手は自分の画面だけ(呼びかけている本人には出ない)。
    /// </summary>
    public class FriendBeamSignal : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>他のUIより手前に出すための描画順</summary>
        private const int SORTING_ORDER = 900;

        /// <summary>出るまで・消えるまでの速さ。大きいほど機敏</summary>
        private const float FADE_SPEED = 6.0f;

        /// <summary>脈打ちの速さ(1秒あたりの回数)</summary>
        private const float PULSE_HZ = 2.2f;

        // ---- 内部状態 ------------------------------------

        private static FriendBeamSignal _instance;

        private CanvasGroup _group;
        private Image _edgeGlow;
        private Text _label;
        private RectTransform _arrow;

        private float _visibility;

        /// <summary>外から渡された呼びかけの相手</summary>
        private Transform _target;

        // ---- 公開API -------------------------------------

        /// <summary>合図を出せるようにする。すでにあれば何もしない</summary>
        public static void Ensure()
        {
            if (_instance != null) return;

            var go = new GameObject(nameof(FriendBeamSignal));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FriendBeamSignal>();
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }

            _instance = this;
            Build();
        }

        /// <summary>いま出ている合図。渡す側が使う</summary>
        public static FriendBeamSignal Instance => _instance;

        /// <summary>呼びかけている相手を外から渡す。空なら合図を消す</summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            // 呼びかけの相手は外から渡される。ここは受け取って出すだけ
            Transform target = _target;

            float goal = target != null ? 1.0f : 0.0f;
            _visibility = Mathf.MoveTowards(_visibility, goal, FADE_SPEED * Time.unscaledDeltaTime);

            if (_group != null) _group.alpha = _visibility;
            if (_visibility <= 0.001f) return;

            // 脈打たせて『急げ』を伝える。受付時間が短いので、止まっていると気づかれない
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * PULSE_HZ * Mathf.PI * 2.0f);
            if (_edgeGlow != null)
            {
                Color color = _edgeGlow.color;
                _edgeGlow.color = new Color(color.r, color.g, color.b, 0.22f * pulse);
            }

            if (_label != null) _label.transform.localScale = Vector3.one * (0.95f + 0.1f * pulse);

            PointArrowAt(target);
        }

        /// <summary>呼びかけている相手のほうへ矢印を向ける。画面の外にいても方向だけは分かる</summary>
        private void PointArrowAt(Transform target)
        {
            if (_arrow == null || target == null) return;

            Camera camera = Camera.main;
            if (camera == null) { _arrow.gameObject.SetActive(false); return; }

            _arrow.gameObject.SetActive(true);

            Vector3 viewport = camera.WorldToViewportPoint(target.position);

            // カメラの後ろにいるときは符号が反転するので、向きを裏返して辻褄を合わせる
            if (viewport.z < 0.0f) { viewport.x = 1.0f - viewport.x; viewport.y = 1.0f - viewport.y; }

            Vector2 fromCenter = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (fromCenter.sqrMagnitude < 0.0001f) fromCenter = Vector2.up;

            float angle = Mathf.Atan2(fromCenter.y, fromCenter.x) * Mathf.Rad2Deg;
            _arrow.localRotation = Quaternion.Euler(0.0f, 0.0f, angle - 90.0f);

            // 画面の中央から一定の距離に置く。端に張り付かせるより見失いにくい
            _arrow.anchoredPosition = fromCenter.normalized * 170.0f;
        }

        /// <summary>表示に必要なものを組み立てる</summary>
        private void Build()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);

            _group = canvasObject.AddComponent<CanvasGroup>();
            _group.alpha = 0.0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _edgeGlow = CreateEdgeGlow(canvasObject.transform);
            _label = CreateLabel(canvasObject.transform);
            _arrow = CreateArrow(canvasObject.transform);
        }

        /// <summary>画面の縁だけを光らせる。中央を塗ると肝心のボスが見えなくなる</summary>
        private static Image CreateEdgeGlow(Transform parent)
        {
            var go = new GameObject("EdgeGlow", typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = new Color(1.0f, 0.85f, 0.35f, 0.2f);
            image.sprite = CreateEdgeSprite();
            image.type = Image.Type.Simple;

            return image;
        }

        /// <summary>中央が透明で縁だけ濃いテクスチャを作る。画像アセットを持たずに済ませる</summary>
        private static Sprite CreateEdgeSprite()
        {
            const int SIZE = 64;
            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    // 縁からの距離を0〜1にして、外ほど濃くする
                    float edgeX = Mathf.Min(x, SIZE - 1 - x) / (SIZE * 0.5f);
                    float edgeY = Mathf.Min(y, SIZE - 1 - y) / (SIZE * 0.5f);
                    float inner = Mathf.Min(edgeX, edgeY);

                    float alpha = Mathf.Clamp01(1.0f - inner / 0.45f);
                    texture.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, alpha * alpha));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0.0f, 0.0f, SIZE, SIZE), new Vector2(0.5f, 0.5f));
        }

        private static Text CreateLabel(Transform parent)
        {
            var go = new GameObject("Label", typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1.0f);
            rect.anchorMax = new Vector2(0.5f, 1.0f);
            rect.pivot = new Vector2(0.5f, 1.0f);
            rect.anchoredPosition = new Vector2(0.0f, -140.0f);
            rect.sizeDelta = new Vector2(900.0f, 90.0f);

            var text = go.GetComponent<Text>();
            text.text = "ビームを合わせろ！";
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 54;
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(1.0f, 0.92f, 0.6f, 1.0f);
            text.raycastTarget = false;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.2f, 0.1f, 0.0f, 0.9f);
            outline.effectDistance = new Vector2(3.0f, -3.0f);

            return text;
        }

        /// <summary>呼びかけている相手の方向を指す三角</summary>
        private static RectTransform CreateArrow(Transform parent)
        {
            var go = new GameObject("Arrow", typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(70.0f, 70.0f);

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = new Color(1.0f, 0.85f, 0.35f, 0.95f);
            image.sprite = CreateArrowSprite();

            return rect;
        }

        private static Sprite CreateArrowSprite()
        {
            const int SIZE = 64;
            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    // 上を頂点にした三角。高さが上がるほど幅を狭める
                    float u = x / (float)(SIZE - 1);
                    float v = y / (float)(SIZE - 1);
                    float halfWidth = 0.5f * (1.0f - v);

                    bool inside = v > 0.15f && Mathf.Abs(u - 0.5f) < halfWidth;
                    texture.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, inside ? 1.0f : 0.0f));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0.0f, 0.0f, SIZE, SIZE), new Vector2(0.5f, 0.5f));
        }
    }
}
