using System.Collections.Generic;
using ProjectKMP.Player;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// ターゲットカメラで狙っている相手に重ねる照準。
    ///
    /// カメラが勝手に向き直るだけだと『いま固定されているのか』が分かりにくいので、
    /// 狙っている相手を囲むことで、入っているかどうかを一目で伝える。
    ///
    /// 位置も大きさも、相手の見た目の当たり(Renderer の範囲)から毎フレーム割り出す。
    /// 決め打ちの大きさだと、遠い相手には大きすぎ、近い相手には小さすぎる絵になってしまう。
    /// 画面の外に出たときは隠す(端に張り付かせると、狙いが外れたのか分かりにくいため)。
    /// </summary>
    public class LockOnMarker : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>出るまで・消えるまでの速さ。大きいほど機敏</summary>
        private const float FADE_SPEED = 8.0f;

        /// <summary>脈打ちの速さ(1秒あたりの回数)</summary>
        private const float PULSE_HZ = 1.4f;

        /// <summary>大きさが移り変わる速さ。急に伸び縮みするとちらついて見える</summary>
        private const float SIZE_BLEND_SPEED = 12.0f;

        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("重ねる照準の絵。未設定なら何も出ない")]
        private Image _marker;

        [SerializeField, Min(1.0f), Tooltip("相手をどれだけ余裕を持って囲むか。1.0でぴったり")]
        private float _padding = 1.18f;

        [SerializeField, Range(0.0f, 0.5f), Tooltip("画面の高さに対する最小の大きさ。小さい相手が点にならないようにする")]
        private float _minSizeRatio = 0.07f;

        [SerializeField, Range(0.1f, 1.5f), Tooltip("画面の高さに対する最大の大きさ。近づいたときに画面を覆わないようにする")]
        private float _maxSizeRatio = 0.55f;

        [SerializeField, Min(0.0f), Tooltip("脈打ちの幅。0で脈打たない")]
        private float _pulseScale = 0.05f;

        // ---- 内部状態 ------------------------------------

        private ThirdPersonCamera _cameraController;
        private CanvasGroup _group;
        private float _visibility;

        private Transform _cachedTarget;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        private float _currentSize;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 照準をその場で消す。
        /// 普段は少しかけて消すが、クリア直後の画面を撮るときのように
        /// 消えるのを待てない場面ではこちらを使う。
        /// </summary>
        public void HideNow()
        {
            _visibility = 0.0f;

            if (_group != null) _group.alpha = 0.0f;
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_marker == null) return;

            _group = _marker.GetComponent<CanvasGroup>();
            if (_group == null) _group = _marker.gameObject.AddComponent<CanvasGroup>();

            _group.alpha = 0.0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        private void LateUpdate()
        {
            if (_marker == null) return;

            Transform target = ResolveLockTarget();

            // 出す・消すは滑らかに。パッと現れると何が起きたか分かりにくい
            float goal = target != null ? 1.0f : 0.0f;
            _visibility = Mathf.MoveTowards(_visibility, goal, FADE_SPEED * Time.unscaledDeltaTime);

            if (_group != null) _group.alpha = _visibility;
            if (_visibility <= 0.001f || target == null) return;

            PlaceOn(target);
        }

        /// <summary>いま狙っている相手を返す。カメラは後から現れることがあるので都度探し直す</summary>
        private Transform ResolveLockTarget()
        {
            if (_cameraController == null) _cameraController = FindAnyObjectByType<ThirdPersonCamera>();
            if (_cameraController == null) return null;

            return _cameraController.LockTarget;
        }

        /// <summary>相手を囲むように照準を置く。画面の外へ出たら隠す</summary>
        private void PlaceOn(Transform target)
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            var canvas = _marker.canvas;
            if (canvas == null) return;

            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;

            if (!TryGetWorldBounds(target, out Bounds bounds)) return;

            // 中心は体の真ん中。頭上に浮かせると、何を狙っているのか分かりにくい
            Vector3 screenCenter = camera.WorldToScreenPoint(bounds.center);

            // カメラの後ろに回った相手は、画面に出しても位置が嘘になる
            if (screenCenter.z <= 0.0f) { if (_group != null) _group.alpha = 0.0f; return; }

            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenCenter, uiCamera, out Vector2 localCenter))
            {
                _marker.rectTransform.anchoredPosition = localCenter;
            }

            float size = CalcSize(camera, canvas, canvasRect, uiCamera, bounds, localCenter);

            // 急に伸び縮みするとちらつくので、目標へ寄せていく
            _currentSize = _currentSize <= 0.0f
                ? size
                : Mathf.MoveTowards(_currentSize, size, canvasRect.rect.height * SIZE_BLEND_SPEED * Time.unscaledDeltaTime);

            _marker.rectTransform.sizeDelta = new Vector2(_currentSize, _currentSize);

            if (_pulseScale <= 0.0f) { _marker.rectTransform.localScale = Vector3.one; return; }

            // ゆっくり脈打たせて『生きている表示』にする。止まっていると絵の一部に見える
            float pulse = 1.0f + _pulseScale * Mathf.Sin(Time.unscaledTime * PULSE_HZ * Mathf.PI * 2.0f);
            _marker.rectTransform.localScale = Vector3.one * pulse;
        }

        /// <summary>
        /// 相手の範囲の角をすべて画面へ映して、囲むのに要る大きさを求める。
        /// 中心だけを見て距離から割り出す方法もあるが、それだと横に長い相手を囲めない。
        /// </summary>
        private float CalcSize(
            Camera camera, Canvas canvas, RectTransform canvasRect, Camera uiCamera, Bounds bounds, Vector2 localCenter)
        {
            Vector3 extents = bounds.extents;
            float reach = 0.0f;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y,
                    (i & 4) == 0 ? -extents.z : extents.z);

                Vector3 screen = camera.WorldToScreenPoint(bounds.center + corner);
                if (screen.z <= 0.0f) continue;

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect, screen, uiCamera, out Vector2 local)) continue;

                reach = Mathf.Max(reach, Mathf.Abs(local.x - localCenter.x));
                reach = Mathf.Max(reach, Mathf.Abs(local.y - localCenter.y));
            }

            float height = canvasRect.rect.height;

            return Mathf.Clamp(reach * 2.0f * _padding, height * _minSizeRatio, height * _maxSizeRatio);
        }

        /// <summary>
        /// 相手の見た目が占める範囲を求める。
        /// 相手が変わったときだけ Renderer を集め直し、範囲そのものは毎フレーム取り直す
        /// (歩いたり技を出したりで形が変わるため)。
        /// </summary>
        private bool TryGetWorldBounds(Transform target, out Bounds bounds)
        {
            if (_cachedTarget != target)
            {
                _cachedTarget = target;
                _renderers.Clear();

                // 見た目の本体だけを見る。エフェクトの粒まで含めると範囲が跳ね上がる
                foreach (var renderer in target.GetComponentsInChildren<Renderer>(false))
                {
                    if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) _renderers.Add(renderer);
                }
            }

            bounds = new Bounds(target.position, Vector3.one);

            bool first = true;
            foreach (var renderer in _renderers)
            {
                if (renderer == null || !renderer.enabled) continue;

                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return true;
        }
    }
}
