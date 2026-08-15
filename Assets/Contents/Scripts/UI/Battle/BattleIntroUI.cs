using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ProjectKMP.Presentation;

namespace ProjectKMP.UI.Battle
{
    /// <summary>
    /// カットシーン中の画面表示。暗転、ボスの名前、「バトルスタート」の帯、
    /// スキップの長押しゲージを持つ。進行の順番は BattleIntroDirector が決めるので、
    /// ここは見せ方だけを受け持つ。
    /// </summary>
    public class BattleIntroUI : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("暗転")]
        [SerializeField, Tooltip("画面全体をふさぐ黒。ここが明けてカットシーンが始まる")]
        private CanvasGroup _fadeGroup;

        [Header("ボスの名前")]
        [SerializeField, Tooltip("名前表示のまとまり。透明度で出し入れする")]
        private CanvasGroup _nameGroup;

        [SerializeField, Tooltip("名前の並び全体。出る瞬間だけ大きくする")]
        private RectTransform _nameRect;

        [SerializeField, Min(0.01f), Tooltip("名前が出るまでの時間(秒)")]
        private float _namePopSeconds = 0.22f;

        [SerializeField, Min(1.0f), Tooltip("出た瞬間の大きさの倍率")]
        private float _namePopScale = 1.35f;

        [Header("バトルスタート")]
        [SerializeField, Tooltip("バトルスタート表示のまとまり")]
        private CanvasGroup _battleStartGroup;

        [SerializeField, Tooltip("文字と、その背景の帯のまとまり。透明度で出し入れする")]
        private CanvasGroup _mainBandGroup;

        [SerializeField, Tooltip("上の帯。左から中央へ入ってきて、右へ抜ける")]
        private RectTransform _topBandRect;

        [SerializeField, Tooltip("下の帯。右から中央へ入ってきて、左へ抜ける")]
        private RectTransform _bottomBandRect;

        [SerializeField, Min(0.01f), Tooltip("帯が中央に来るまでの時間(秒)")]
        private float _bandEnterSeconds = 0.28f;

        [SerializeField, Min(0.0f), Tooltip("中央で見せている時間(秒)")]
        private float _bandHoldSeconds = 1.5f;

        [SerializeField, Min(0.01f), Tooltip("帯が画面外へ抜けるまでの時間(秒)")]
        private float _bandExitSeconds = 0.28f;

        [SerializeField, Min(0.0f), Tooltip("帯が動く距離(ピクセル)。画面幅より大きくしておく")]
        private float _bandTravelDistance = 1400.0f;

        [Header("スキップ")]
        [SerializeField, Tooltip("スキップ表示のまとまり")]
        private CanvasGroup _skipGroup;

        [SerializeField, Tooltip("長押しの進み具合を出す円形ゲージ(Image の Filled / Radial360)")]
        private Image _skipFillImage;

        [SerializeField, Tooltip("画面のどこを押しても長押しとして受け取る領域")]
        private BattleIntroHoldArea _holdArea;

        [SerializeField, Min(0.1f), Tooltip("スキップが成立するまでの長押し時間(秒)")]
        private float _skipHoldSeconds = 1.0f;

        [SerializeField, Min(0.0f), Tooltip("指を離したときにゲージが戻る速さの倍率")]
        private float _skipReleaseMultiplier = 2.0f;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 名前の出現と「バトルスタート」にかかる合計秒数。
        /// 進行役が演出全体の長さを見積もるのに使う(名前を見せている時間は進行役が持っている)。
        /// </summary>
        public float TotalSeconds => _namePopSeconds + _bandEnterSeconds + _bandHoldSeconds + _bandExitSeconds;

        /// <summary>演出の開始時に呼ぶ。暗転を張り、名前とバトルスタートを隠す</summary>
        public void Prepare()
        {
            gameObject.SetActive(true);

            SetAlpha(_fadeGroup, 1.0f);
            SetAlpha(_nameGroup, 0.0f);
            SetAlpha(_battleStartGroup, 0.0f);
            SetAlpha(_mainBandGroup, 0.0f);

            // スキップを出すかどうかは進行役が決めるので、ここでは隠しておく
            SetSkipAvailable(false);

            if (_nameRect != null) _nameRect.localScale = Vector3.one;
            if (_skipFillImage != null) _skipFillImage.fillAmount = 0.0f;
            SetAnchoredX(_topBandRect, -_bandTravelDistance);
            SetAnchoredX(_bottomBandRect, _bandTravelDistance);
        }

        /// <summary>
        /// スキップの受付と表示を切り替える。
        /// マルチプレイでは飛ばすかどうかをホストが決めるので、ホスト以外では出さない。
        /// </summary>
        public void SetSkipAvailable(bool available)
        {
            SetAlpha(_skipGroup, available ? 1.0f : 0.0f);

            // 見えていないボタンを押せてしまわないよう、受付そのものも止める
            if (_holdArea != null) _holdArea.gameObject.SetActive(available);
            if (_skipFillImage != null) _skipFillImage.fillAmount = 0.0f;
        }

        /// <summary>暗転を明ける。霧の奥からゴリラが見えてくる導入に使う</summary>
        public async UniTask FadeFromBlackAsync(float seconds, CancellationToken ct)
        {
            if (_fadeGroup == null) return;

            float elapsed = 0.0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(elapsed / seconds));
                _fadeGroup.alpha = 1.0f - t;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            _fadeGroup.alpha = 0.0f;
        }

        /// <summary>ボスの名前をポンと出して、指定の秒数だけ見せる</summary>
        public async UniTask ShowNameAsync(float holdSeconds, CancellationToken ct)
        {
            float elapsed = 0.0f;
            while (elapsed < _namePopSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _namePopSeconds);

                SetAlpha(_nameGroup, t);
                if (_nameRect != null) _nameRect.localScale = Vector3.one * Mathf.Lerp(_namePopScale, 1.0f, t);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            SetAlpha(_nameGroup, 1.0f);
            if (_nameRect != null) _nameRect.localScale = Vector3.one;

            await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), cancellationToken: ct);
        }

        /// <summary>
        /// 「バトルスタート」を出す。上下の帯が左右から中央に集まり、
        /// 決めた秒数だけ止まってから、そのまま同じ向きに流れて消える。
        /// </summary>
        public async UniTask ShowBattleStartAsync(CancellationToken ct)
        {
            SetAlpha(_battleStartGroup, 1.0f);

            // 入り。名前と入れ替わるように、名前は薄くしていく
            await SlideBandsAsync(-_bandTravelDistance, 0.0f, _bandTravelDistance, 0.0f, _bandEnterSeconds, true, ct);

            await UniTask.Delay(TimeSpan.FromSeconds(_bandHoldSeconds), cancellationToken: ct);

            // 出。来た向きのまま通り抜けさせる
            await SlideBandsAsync(0.0f, _bandTravelDistance, 0.0f, -_bandTravelDistance, _bandExitSeconds, false, ct);

            SetAlpha(_battleStartGroup, 0.0f);
        }

        /// <summary>
        /// Aボタン(またはキーボード・画面のどこか)の長押しを待つ。
        /// 決めた時間まで押し続けられたら true。途中で打ち切られたら false。
        /// </summary>
        public async UniTask<bool> WaitForSkipAsync(CancellationToken ct)
        {
            float held = 0.0f;

            try
            {
                while (true)
                {
                    if (IsSkipHeld()) held += Time.deltaTime;
                    else held = Mathf.Max(0.0f, held - Time.deltaTime * _skipReleaseMultiplier);

                    float ratio = Mathf.Clamp01(held / Mathf.Max(0.01f, _skipHoldSeconds));
                    if (_skipFillImage != null) _skipFillImage.fillAmount = ratio;

                    if (ratio >= 1.0f)
                    {
                        // 溜まりきった手応えを返す。長押しの受付は飛ばす権限を持つ側でしか回らないので、
                        // 押していた本人の画面でだけ鳴る
                        if (UiSoundPlayer.Instance != null) UiSoundPlayer.Instance.Play(UiSoundPlayer.SoundKind.Decide);
                        return true;
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>演出の終わりに呼ぶ。全部消して、押し込みの受付もやめる</summary>
        public void HideAll()
        {
            SetAlpha(_fadeGroup, 0.0f);
            SetAlpha(_nameGroup, 0.0f);
            SetAlpha(_battleStartGroup, 0.0f);
            SetAlpha(_mainBandGroup, 0.0f);
            SetAlpha(_skipGroup, 0.0f);
            gameObject.SetActive(false);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>ゲームパッドA / スペース / 画面押し込みのいずれかが押されているか</summary>
        private bool IsSkipHeld()
        {
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonSouth.isPressed) return true;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.spaceKey.isPressed || keyboard.enterKey.isPressed)) return true;

            return _holdArea != null && _holdArea.IsHeld;
        }

        /// <summary>上下の帯を横に動かす。enter が true なら文字を濃くし、名前を薄くする</summary>
        private async UniTask SlideBandsAsync(float topFrom, float topTo, float bottomFrom, float bottomTo,
            float seconds, bool enter, CancellationToken ct)
        {
            float elapsed = 0.0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(elapsed / seconds));

                SetAnchoredX(_topBandRect, Mathf.Lerp(topFrom, topTo, t));
                SetAnchoredX(_bottomBandRect, Mathf.Lerp(bottomFrom, bottomTo, t));
                SetAlpha(_mainBandGroup, enter ? t : 1.0f - t);
                if (enter) SetAlpha(_nameGroup, 1.0f - t);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            SetAnchoredX(_topBandRect, topTo);
            SetAnchoredX(_bottomBandRect, bottomTo);
            SetAlpha(_mainBandGroup, enter ? 1.0f : 0.0f);
            if (enter) SetAlpha(_nameGroup, 0.0f);
        }

        private static void SetAlpha(CanvasGroup group, float alpha)
        {
            if (group != null) group.alpha = alpha;
        }

        private static void SetAnchoredX(RectTransform rect, float x)
        {
            if (rect == null) return;
            Vector2 position = rect.anchoredPosition;
            rect.anchoredPosition = new Vector2(x, position.y);
        }
    }
}
