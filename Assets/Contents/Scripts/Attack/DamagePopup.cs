using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace ProjectKMP.Attack
{
    /// <summary>
    /// 当たった位置に出るダメージの数字。少し上に浮きながら薄くなって消える。
    /// 出る位置は毎回ランダムにずらすので、連続で当てても数字が重ならない。
    /// カメラの方を向き続け、画面の外にはみ出しそうなときは端に寄せて必ず見えるようにする。
    /// 相手の体に隠れないよう、文字は常に手前に描かれる設定のマテリアルを使う。
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("参照")]
        [SerializeField, Tooltip("数字を出す文字。未設定なら子から探す")]
        private TMP_Text _text;

        [SerializeField, Tooltip("フェードに使う CanvasGroup。未設定なら自分から探す")]
        private CanvasGroup _group;

        [SerializeField, Tooltip("連携(同時ヒットボーナス)のときに数字の右隣へ出す絵。普段は隠しておく")]
        private RectTransform _comboIcon;

        [SerializeField, Tooltip("数字と絵のあいだの余白")]
        private float _comboIconGap = 10.0f;

        [Header("出る位置")]
        [SerializeField, Tooltip("画面の横方向・縦方向にランダムでずらす幅(メートル)。0にすると毎回同じ位置に出る")]
        private Vector2 _randomOffsetRange = new Vector2(0.35f, 0.25f);

        [SerializeField, Min(0.0f), Tooltip("カメラ側へ寄せる距離(メートル)。相手の体に数字が埋まるのを防ぐ")]
        private float _towardCameraOffset = 0.3f;

        [Header("画面内に収める")]
        [SerializeField, Tooltip("画面からはみ出しそうなときに端へ寄せて、必ず見えるようにする")]
        private bool _keepInsideScreen = true;

        [SerializeField, Range(0.0f, 0.45f), Tooltip("画面の端からどれだけ内側に留めるか。0.05なら画面の5%ぶん内側まで")]
        private float _screenMargin = 0.06f;

        [SerializeField, Min(0.5f), Tooltip("カメラからの最短距離(メートル)。カメラの後ろに回った数字を前に引き戻すのに使う")]
        private float _minCameraDistance = 2.0f;

        [Header("動き")]
        [SerializeField, Min(0.0f), Tooltip("消えるまでに上へ移動する距離(メートル)")]
        private float _riseDistance = 0.9f;

        [SerializeField, Min(0.1f), Tooltip("出てから消えるまでの秒数")]
        private float _lifeSeconds = 0.8f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("何割すぎてから薄くしはじめるか。0.4なら後半6割かけて消える")]
        private float _fadeStartRatio = 0.4f;

        [SerializeField, Tooltip("上へ移動する速さの変化。最初は速く、だんだん緩やかにすると気持ちよく見える")]
        private AnimationCurve _riseCurve = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);

        [Header("見た目")]
        [SerializeField, Min(1.0f), Tooltip("出た瞬間だけ少し大きく見せる倍率")]
        private float _popScale = 1.3f;

        [SerializeField, Range(0.01f, 1.0f), Tooltip("大きさが元に戻るまでの割合")]
        private float _popDuration = 0.15f;

        [SerializeField, Tooltip("常にカメラの方を向かせる")]
        private bool _faceCamera = true;

        // ---- 内部状態 ------------------------------------

        private Camera _camera;
        private Vector3 _startPosition;
        private Vector3 _baseScale;

        // ---- 公開API -------------------------------------

        /// <summary>数字を出して、上に浮きながら消えるまでを再生する</summary>
        public void Play(int damage)
        {
            Play(damage.ToString(), false);
        }

        /// <summary>
        /// 数字を出す。連携(同時ヒットボーナス)が乗った一撃なら、数字の右隣に握手の絵を並べる。
        /// 別の表示として出すと数字と離れてしまうので、同じ表示の中に入れている。
        /// </summary>
        public void Play(int damage, bool showComboIcon)
        {
            Play(damage.ToString(), showComboIcon);
        }

        /// <summary>好きな文字で同じ動きを再生する</summary>
        public void Play(string label)
        {
            Play(label, false);
        }

        /// <summary>
        /// 好きな文字で再生する。浮かせ方・画面内への収め方はダメージ表示と同じ仕組みを使い回す。
        /// </summary>
        public void Play(string label, bool showComboIcon)
        {
            if (_text == null) _text = GetComponentInChildren<TMP_Text>();
            if (_group == null) _group = GetComponent<CanvasGroup>();

            if (_text != null) _text.text = label;

            LayoutComboIcon(showComboIcon);

            _baseScale = transform.localScale;
            _camera = Camera.main;

            Vector3 right = _camera != null ? _camera.transform.right : Vector3.right;
            Vector3 up = _camera != null ? _camera.transform.up : Vector3.up;

            // 画面上での見え方でずらしたいので、カメラの右・上を基準にする
            Vector3 position = transform.position +
                right * UnityEngine.Random.Range(-_randomOffsetRange.x, _randomOffsetRange.x) +
                up * UnityEngine.Random.Range(-_randomOffsetRange.y, _randomOffsetRange.y);

            if (_camera != null && _towardCameraOffset > 0.0f)
            {
                Vector3 toCamera = _camera.transform.position - position;
                if (toCamera.sqrMagnitude > 0.0001f)
                {
                    position += toCamera.normalized * _towardCameraOffset;
                }
            }

            _startPosition = position;
            transform.position = ClampIntoScreen(position);

            FaceCamera();
            PlayAsync(destroyCancellationToken).Forget();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>
        /// 連携の絵を数字の右隣に置く。数字の幅は桁数で変わるので、
        /// 文字を確定させてから実際の幅を測って位置を決める。
        /// </summary>
        private void LayoutComboIcon(bool show)
        {
            if (_comboIcon == null) return;

            _comboIcon.gameObject.SetActive(show);
            if (!show || _text == null) return;

            _text.ForceMeshUpdate();

            float textHalf = _text.preferredWidth * 0.5f;
            float iconHalf = _comboIcon.sizeDelta.x * 0.5f;
            _comboIcon.anchoredPosition = new Vector2(textHalf + iconHalf + _comboIconGap, 0.0f);
        }

        /// <summary>上へ浮かせながら薄くしていき、終わったら自分を消す</summary>
        private async UniTaskVoid PlayAsync(CancellationToken ct)
        {
            try
            {
                float elapsed = 0.0f;

                while (elapsed < _lifeSeconds)
                {
                    elapsed += Time.deltaTime;
                    float ratio = Mathf.Clamp01(elapsed / _lifeSeconds);

                    // 上へ移動してから、画面からはみ出していれば内側に押し戻す
                    Vector3 risen = _startPosition + Vector3.up * (_riseDistance * _riseCurve.Evaluate(ratio));
                    transform.position = ClampIntoScreen(risen);

                    // 出た直後だけ大きく見せて、すぐ元に戻す
                    float pop = _popDuration <= 0.0f
                        ? 1.0f
                        : Mathf.Lerp(_popScale, 1.0f, Mathf.Clamp01(ratio / _popDuration));
                    transform.localScale = _baseScale * pop;

                    // 後半だけ薄くする
                    if (_group != null)
                    {
                        float fade = _fadeStartRatio >= 1.0f
                            ? 0.0f
                            : Mathf.InverseLerp(_fadeStartRatio, 1.0f, ratio);
                        _group.alpha = 1.0f - fade;
                    }

                    if (_faceCamera) FaceCamera();

                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 途中で消えただけなので何もしない
                return;
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// 画面からはみ出す位置なら、画面の内側に収まる位置に置きかえて返す。
        /// カメラから見た比率(ビューポート)で考えると、画面の端がそのまま 0 と 1 になるので扱いやすい。
        /// </summary>
        private Vector3 ClampIntoScreen(Vector3 worldPosition)
        {
            if (!_keepInsideScreen) return worldPosition;

            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return worldPosition;

            Vector3 viewport = _camera.WorldToViewportPoint(worldPosition);

            // z がマイナス = カメラの後ろ。この場合 x,y が反転して出てくるので直してから前に出す
            if (viewport.z < 0.0f)
            {
                viewport.x = 1.0f - viewport.x;
                viewport.y = 1.0f - viewport.y;
                viewport.z = _minCameraDistance;
            }
            else if (viewport.z < _minCameraDistance)
            {
                viewport.z = _minCameraDistance;
            }

            float margin = Mathf.Clamp(_screenMargin, 0.0f, 0.45f);
            viewport.x = Mathf.Clamp(viewport.x, margin, 1.0f - margin);
            viewport.y = Mathf.Clamp(viewport.y, margin, 1.0f - margin);

            return _camera.ViewportToWorldPoint(viewport);
        }

        /// <summary>カメラと同じ向きにして、常に正面から読めるようにする</summary>
        private void FaceCamera()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            transform.rotation = _camera.transform.rotation;
        }
    }
}
