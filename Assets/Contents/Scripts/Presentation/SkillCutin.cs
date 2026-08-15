using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.Presentation
{
    /// <summary>
    /// 必殺技の発動カットイン。
    /// 本編から離れた場所に置いた専用ステージの犬を、専用カメラで RenderTexture に描き、
    /// それを画面右側へスライドインさせて見せる(オフスクリーンレンダリング)。
    /// 発動中は時間の流れを落としているので、動きはすべて実時間で進める。
    /// 発動した本人の画面にだけ出す想定で、通信はしない。
    /// </summary>
    public class SkillCutin : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("参照")]
        [SerializeField, Tooltip("カットインを描く専用カメラ。出ていない間は止める")]
        private Camera _cutinCamera;

        [SerializeField, Tooltip("犬とライトを置いたステージ。出ていない間は丸ごと消す")]
        private GameObject _stage;

        [SerializeField, Tooltip("カットインの犬の Animator")]
        private Animator _dogAnimator;

        [SerializeField, Tooltip("表示を丸ごと出し入れする CanvasGroup")]
        private CanvasGroup _canvasGroup;

        [SerializeField, Tooltip("スライドさせる枠。ここに帯と犬の絵が入っている")]
        private RectTransform _panel;

        [SerializeField, Tooltip("背景に流す集中線。未設定なら流さない")]
        private RawImage _speedLines;

        [Header("長さ")]
        [SerializeField, Min(0.05f), Tooltip("滑り込んでくる時間(秒)")]
        private float _slideInSec = 0.12f;

        [SerializeField, Min(0.05f), Tooltip("引っ込む時間(秒)")]
        private float _slideOutSec = 0.14f;

        [SerializeField, Min(0.1f), Tooltip("長さを指定せずに再生したときの表示時間(秒)")]
        private float _defaultDurationSec = 0.6f;

        [Header("スライド")]
        [SerializeField, Min(0f), Tooltip("画面外へどれだけ逃がすか(px)。パネルの幅より大きくする")]
        private float _slideDistance = 900f;

        [SerializeField, Min(0f), Tooltip("滑り込みで行きすぎて戻る量(px)。勢いが出る")]
        private float _overshoot = 45f;

        [Header("カメラの寄り")]
        [SerializeField, Tooltip("出た瞬間のカメラ位置(ステージ内のローカル座標)")]
        private Vector3 _cameraStartLocalPos = new Vector3(0f, 1.15f, 3.4f);

        [SerializeField, Tooltip("引っ込む直前のカメラ位置。顔へ寄せる")]
        private Vector3 _cameraEndLocalPos = new Vector3(0f, 0.95f, 1.6f);

        [SerializeField, Range(10f, 90f), Tooltip("出た瞬間の視野角")]
        private float _cameraStartFov = 46f;

        [SerializeField, Range(10f, 90f), Tooltip("寄りきったときの視野角")]
        private float _cameraEndFov = 33f;

        [Header("集中線")]
        [SerializeField, Tooltip("集中線が流れる速さ")]
        private float _speedLineScrollSpeed = 2.2f;

        [Header("締めの演出")]
        [SerializeField, Tooltip("締めのときに犬を回す角度。90で横を向く")]
        private float _finishYawDeg = 90.0f;

        [SerializeField, Tooltip("締めのときに犬をずらす量。回すと枠から外れるので、見える位置へ寄せる")]
        private Vector3 _finishLocalOffset = new Vector3(-0.6f, 0.0f, 0.0f);

        [Header("アニメ")]
        [SerializeField, Tooltip("カットインで再生するステート名")]
        private string _attackStateName = "Attack";

        [SerializeField, Range(0.1f, 2f), Tooltip("カットイン中のアニメの速さ")]
        private float _animatorSpeed = 0.8f;

        // ---- 内部状態 ------------------------------------

        private static SkillCutin _instance;

        private Vector2 _shownAnchoredPosition;
        private float _elapsedSec;
        private float _durationSec;
        private bool _playing;

        /// <summary>ずらす前の犬の位置</summary>
        private Vector3 _dogHomeLocalPosition;

        // ---- 公開API -------------------------------------

        /// <summary>シーンに置かれているカットイン。無ければ null</summary>
        public static SkillCutin Instance => _instance;

        /// <summary>いま出ているか</summary>
        public bool IsPlaying => _playing;

        /// <summary>
        /// カットインを出す。durationSec に0以下を渡すと既定の長さになる。
        /// シーンにカットインが無ければ何もしない。
        /// </summary>
        public static void Play(float durationSec = 0f)
        {
            if (_instance != null) _instance.PlayInternal(durationSec, 0.0f, Vector3.zero);
        }

        /// <summary>
        /// 向きを指定して出す。
        ///
        /// 締めの演出では横から噛みつく姿を見せたい。
        /// カメラを動かすと画角の調整がやり直しになるので、犬のほうを回す。
        /// </summary>
        public static void PlayFinish(float durationSec)
        {
            if (_instance == null) return;

            _instance.PlayInternal(durationSec, _instance._finishYawDeg, _instance._finishLocalOffset);
        }

        /// <summary>カットインをすぐ引っ込める(技がキャンセルされたときなど)</summary>
        public static void Cancel()
        {
            if (_instance != null) _instance.Hide();
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _instance = this;

            if (_panel != null) _shownAnchoredPosition = _panel.anchoredPosition;

            // ずらす前の位置を控える。毎回ここから測らないと、呼ぶたびにずれていく
            if (_dogAnimator != null) _dogHomeLocalPosition = _dogAnimator.transform.localPosition;
            Hide();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (!_playing) return;

            // 溜め中は Time.timeScale を落としているので、実時間で進める
            _elapsedSec += Time.unscaledDeltaTime;

            float total = _durationSec;
            if (_elapsedSec >= total) { Hide(); return; }

            ApplySlide(total);
            ApplyCameraPush(total);
            ApplySpeedLines();
        }

        // ---- 内部処理 ------------------------------------

        private void PlayInternal(float durationSec, float yawDeg, Vector3 localOffset)
        {
            _durationSec = durationSec > 0f ? durationSec : _defaultDurationSec;
            _elapsedSec = 0f;
            _playing = true;

            if (_stage != null) _stage.SetActive(true);
            if (_cutinCamera != null) _cutinCamera.enabled = true;
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;

            if (_dogAnimator != null)
            {
                // 指定された向きへ回す。横を向けると、噛みつく動きが分かりやすい
                _dogAnimator.transform.localRotation = Quaternion.Euler(0.0f, yawDeg, 0.0f);

                // 回すと体が枠から外れる。見える位置まで寄せ直す
                _dogAnimator.transform.localPosition = _dogHomeLocalPosition + localOffset;

                // 時間が止まっていてもモーションは動かしたい
                _dogAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
                _dogAnimator.speed = _animatorSpeed;
                _dogAnimator.Play(_attackStateName, 0, 0f);
            }

            ApplySlide(_durationSec);
            ApplyCameraPush(_durationSec);
        }

        /// <summary>出ていない間はカメラもステージも止めて、描画コストをゼロにする</summary>
        private void Hide()
        {
            _playing = false;

            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            if (_cutinCamera != null) _cutinCamera.enabled = false;
            if (_stage != null) _stage.SetActive(false);
            if (_panel != null) _panel.anchoredPosition = _shownAnchoredPosition + Vector2.right * _slideDistance;
        }

        /// <summary>滑り込み → 表示 → 引っ込みの位置を求める</summary>
        private void ApplySlide(float total)
        {
            if (_panel == null) return;

            float offset;
            if (_elapsedSec < _slideInSec)
            {
                // 行きすぎてから戻ることで、勢いよく飛び込んできたように見せる
                float t = Mathf.Clamp01(_elapsedSec / _slideInSec);
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                offset = Mathf.Lerp(_slideDistance, -_overshoot, eased);
            }
            else if (_elapsedSec > total - _slideOutSec)
            {
                float t = Mathf.Clamp01((_elapsedSec - (total - _slideOutSec)) / _slideOutSec);
                offset = Mathf.Lerp(0f, _slideDistance, t * t);
            }
            else
            {
                // 行きすぎたぶんをゆっくり戻して、止まっている間もわずかに動かす
                float t = Mathf.Clamp01((_elapsedSec - _slideInSec) / Mathf.Max(0.01f, total - _slideInSec - _slideOutSec));
                offset = Mathf.Lerp(-_overshoot, 0f, t);
            }

            _panel.anchoredPosition = _shownAnchoredPosition + Vector2.right * offset;
        }

        /// <summary>出ている間ずっと犬の顔へ寄せていく</summary>
        private void ApplyCameraPush(float total)
        {
            if (_cutinCamera == null) return;

            float t = total <= 0f ? 1f : Mathf.Clamp01(_elapsedSec / total);
            float eased = 1f - (1f - t) * (1f - t);

            _cutinCamera.transform.localPosition = Vector3.Lerp(_cameraStartLocalPos, _cameraEndLocalPos, eased);
            _cutinCamera.fieldOfView = Mathf.Lerp(_cameraStartFov, _cameraEndFov, eased);
        }

        private void ApplySpeedLines()
        {
            if (_speedLines == null) return;

            Rect uv = _speedLines.uvRect;
            uv.x += _speedLineScrollSpeed * Time.unscaledDeltaTime;
            if (uv.x > 1f) uv.x -= 1f;
            _speedLines.uvRect = uv;
        }
    }
}
