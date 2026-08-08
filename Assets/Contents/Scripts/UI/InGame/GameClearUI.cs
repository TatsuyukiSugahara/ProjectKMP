using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectKMP.UI.InGame
{
    /// <summary>
    /// ボス撃破時に画面中央へ出す「ゲームクリア」表示。
    /// 文字がポップしたあと、上下の牙が開いた状態から「がぶっ」と噛み閉じて、
    /// 噛んだ瞬間に全体が弾む。表示の進行は GameClearDirector から呼ばれる。
    /// </summary>
    public class GameClearUI : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("表示・非表示に使う CanvasGroup")]
        private CanvasGroup _group;

        [SerializeField, Tooltip("ポップ演出で拡縮するルート")]
        private RectTransform _popRoot;

        [SerializeField, Min(0.01f), Tooltip("文字のフェードインにかける時間(秒)")]
        private float _fadeInSec = 0.35f;

        [SerializeField, Min(1.0f), Tooltip("文字が出た瞬間の拡大率。ここから等倍へ縮んで止まる")]
        private float _popScale = 1.25f;

        [Header("がぶっと閉じる牙")]
        [SerializeField, Tooltip("牙ぜんたいのルート。噛みつくまでは隠しておく")]
        private RectTransform _biteRoot;

        [SerializeField, Tooltip("上あごの牙")]
        private RectTransform _fangUpper;

        [SerializeField, Tooltip("下あごの牙")]
        private RectTransform _fangLower;

        [SerializeField, Min(0.0f), Tooltip("閉じた状態の牙の位置(中心からのずれ、ピクセル)")]
        private float _fangClosedOffset = 38.0f;

        [SerializeField, Min(0.0f), Tooltip("開いた状態の牙の位置(中心からのずれ、ピクセル)")]
        private float _fangOpenOffset = 200.0f;

        [SerializeField, Min(0.0f), Tooltip("文字が出てから噛みつくまでの間(秒)")]
        private float _biteDelaySec = 0.15f;

        [SerializeField, Min(0.01f), Tooltip("がぶっと閉じるのにかける時間(秒)。短いほど鋭く見える")]
        private float _fangSnapSec = 0.09f;

        [SerializeField, Min(1.0f), Tooltip("噛んだ瞬間に全体を弾ませる拡大率")]
        private float _bitePunchScale = 1.12f;

        [SerializeField, Min(0.01f), Tooltip("弾みが収まるまでの時間(秒)")]
        private float _bitePunchSec = 0.18f;

        [Header("シーン遷移のフェード")]
        [SerializeField, Tooltip("画面全体を覆う黒。リザルトへ移る前のフェードアウトに使う")]
        private UnityEngine.UI.Image _fadeImage;

        [SerializeField, Min(0.01f), Tooltip("フェードアウトにかける時間(秒)")]
        private float _fadeOutSec = 0.6f;

        [Header("音")]
        [SerializeField, Tooltip("「ゲームクリア」が出た瞬間のファンファーレ")]
        private AudioClip _clearClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("ファンファーレの音量")]
        private float _clearVolume = 0.8f;

        [SerializeField, Tooltip("牙が閉じきった瞬間の音")]
        private AudioClip _biteClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("噛みつき音の音量")]
        private float _biteVolume = 0.8f;

        // ---- 公開API -------------------------------------

        /// <summary>「ゲームクリア」を表示する。文字のポップ→牙の噛みつき→弾み、の順に進む</summary>
        public async UniTask ShowAsync(CancellationToken ct)
        {
            if (_group == null) return;

            // 文字が出るのに合わせて鳴らす。撃破は全クライアントに届くので、各自の画面で鳴る
            Play(_clearClip, _clearVolume);

            // 牙は噛みつく瞬間まで隠しておく
            if (_biteRoot != null) _biteRoot.gameObject.SetActive(false);

            // 1) 文字のポップ表示
            await TweenAsync(_fadeInSec, t =>
            {
                float eased = 1.0f - (1.0f - t) * (1.0f - t);
                _group.alpha = eased;
                if (_popRoot != null) _popRoot.localScale = Vector3.one * Mathf.Lerp(_popScale, 1.0f, eased);
            }, ct);

            // 2) ひと呼吸おいて、開いた牙を出す
            if (_biteRoot == null || _fangUpper == null || _fangLower == null) return;

            if (_biteDelaySec > 0.0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_biteDelaySec), cancellationToken: ct);
            }

            SetFangOffset(_fangOpenOffset);
            _biteRoot.gameObject.SetActive(true);

            // 3) がぶっと閉じる。加速しながら閉じると噛んだ手応えが出る
            await TweenAsync(_fangSnapSec, t =>
            {
                SetFangOffset(Mathf.Lerp(_fangOpenOffset, _fangClosedOffset, t * t * t));
            }, ct);

            // 閉じきった瞬間に鳴らす。弾みの演出と同時になる
            Play(_biteClip, _biteVolume);

            // 4) 噛んだ瞬間、全体を弾ませて衝撃を出す
            await TweenAsync(_bitePunchSec, t =>
            {
                float eased = 1.0f - (1.0f - t) * (1.0f - t);
                if (_popRoot != null) _popRoot.localScale = Vector3.one * Mathf.Lerp(_bitePunchScale, 1.0f, eased);
            }, ct);
        }

        /// <summary>画面全体を黒にフェードアウトする。シーン遷移の直前に呼ぶ</summary>
        public async UniTask FadeOutAsync(CancellationToken ct)
        {
            if (_fadeImage == null) return;

            _fadeImage.gameObject.SetActive(true);
            await TweenAsync(_fadeOutSec, t =>
            {
                Color color = _fadeImage.color;
                color.a = t;
                _fadeImage.color = color;
            }, ct);
        }

        /// <summary>表示を隠す(シーン開始時の初期化用)</summary>
        public void Hide()
        {
            if (_group != null) _group.alpha = 0.0f;
            if (_biteRoot != null) _biteRoot.gameObject.SetActive(false);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>シーンの UiSoundPlayer から鳴らす。置かれていなければ何もしない</summary>
        private static void Play(AudioClip clip, float volume)
        {
            if (clip == null || UiSoundPlayer.Instance == null) return;

            UiSoundPlayer.Instance.PlayOneShot(clip, volume);
        }

        /// <summary>上下の牙を中心から指定ぶんだけ離す</summary>
        private void SetFangOffset(float offset)
        {
            if (_fangUpper != null) _fangUpper.anchoredPosition = new Vector2(0.0f, offset);
            if (_fangLower != null) _fangLower.anchoredPosition = new Vector2(0.0f, -offset);
        }

        private async UniTask TweenAsync(float duration, Action<float> onUpdate, CancellationToken ct)
        {
            if (duration <= 0.0f)
            {
                onUpdate(1.0f);
                return;
            }

            float elapsed = 0.0f;
            while (elapsed < duration)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += Time.deltaTime;
                onUpdate(Mathf.Clamp01(elapsed / duration));
            }

            onUpdate(1.0f);
        }
    }
}
