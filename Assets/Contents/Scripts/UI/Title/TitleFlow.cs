using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ProjectKMP.Presentation;

namespace ProjectKMP.UI.Title
{
    /// <summary>
    /// タイトル画面の進行役。
    /// 「何かキーを押す/タッチ」→ メニューで遊び方を選び、
    /// ひとり用ならそのままゲームへ、みんな用は なまえ入力 → マッチング画面へ進む。
    /// </summary>
    public class TitleFlow : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>画面が切り替わった直後に決定入力を受け付けない時間(秒)</summary>
        private const float INPUT_GUARD_SECONDS = 0.2f;

        /// <summary>接続フェーズでゲージを進める上限。残りはシーン読み込みに使う</summary>
        private const float CONNECT_PROGRESS_CAP = 0.4f;

        /// <summary>メニューの選択肢</summary>
        private enum MenuChoice { SinglePlay, MultiPlay, Quit }

        // ---- インスペクタ設定 ------------------------------

        [Header("画面パーツ")]
        [SerializeField] private CanvasGroup _pressAnyKeyGroup;
        [SerializeField] private CanvasGroup _menuGroup;
        [SerializeField] private CanvasGroup _nameInputGroup;
        [SerializeField] private CanvasGroup _loadingGroup;

        [Header("メニュー")]
        [SerializeField, Tooltip("ひとりで遊ぶ。通信しないのですぐ始まる")]
        private Button _singlePlayButton;

        [SerializeField, Tooltip("みんなで遊ぶ。なまえを入れてマッチングへ進む")]
        private Button _multiPlayButton;

        [SerializeField] private Button _quitButton;

        [Header("なまえ入力")]
        [SerializeField] private TMP_InputField _nameInputField;
        [SerializeField] private Button _nameOkButton;
        [SerializeField] private Button _nameBackButton;

        [SerializeField, Tooltip("名前が空のときや、ひとり用のときに使う名前")]
        private string _defaultPlayerName = "ななしさん";

        [Header("通信")]
        [SerializeField, Tooltip("未設定なら NetworkManager.Instance を使う")]
        private NetworkManager _networkManager;

        [SerializeField, Tooltip("接続演出でゲージが上限まで伸びるのにかける秒数")]
        private float _connectGaugeSeconds = 3.0f;

        [SerializeField, Tooltip("エラー文言を見せる秒数")]
        private float _errorMessageSeconds = 2.5f;

        [Header("ロード")]
        [SerializeField] private LoadingGauge _loadingGauge;
        [SerializeField] private TMP_Text _loadingLabel;

        [SerializeField, Tooltip("マッチング画面のシーン名")]
        private string _lobbySceneName = "Lobby";

        [SerializeField, Tooltip("ひとり用でそのまま入るシーン名")]
        private string _inGameSceneName = "InGame";

        [SerializeField, Tooltip("ローディング表示を最低でも見せる秒数")]
        private float _minimumLoadingSeconds = 1.0f;

        [SerializeField, Tooltip("未設定なら SceneLoader.Instance を使う")]
        private SceneLoader _sceneLoader;

        [Header("メッセージ")]
        [SerializeField] private string _connectingMessage = "つないでいます";
        [SerializeField] private string _loadingMessage = "よみこみちゅう";
        [SerializeField] private string _inProgressMessage = "いま あそんでいます。おわるまで まってね";
        [SerializeField] private string _crowdedMessage = "いま こんでいます。すこし まってね";
        [SerializeField] private string _roomFullMessage = "いま まんいんです。すこし まってね";
        [SerializeField] private string _failedMessage = "つながりませんでした";

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
#if KMP_SINGLE_ONLY
            // 配布用はひとりで遊ぶ形に絞る。
            //
            // 隠すだけだと、メニューの並びを面倒みている側が毎フレーム出し直してしまう。
            // 物ごと消せば、誰が何をしても戻らない。
            //
            // 消すのは Start ではなく Awake。並びを組み立てる前に消さないと、
            // 一覧へ数えられてから消えることになり、空の位置が回ってくる。
            if (_multiPlayButton != null) Destroy(_multiPlayButton.gameObject);
#endif
        }

        private void Start()
        {
            RunAsync(destroyCancellationToken).Forget();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>タイトル画面の一連の流れ</summary>
        private async UniTaskVoid RunAsync(CancellationToken ct)
        {
            SetVisible(_pressAnyKeyGroup, true);
            SetVisible(_menuGroup, false);
            SetVisible(_nameInputGroup, false);
            SetVisible(_loadingGroup, false);

            await UniTask.WaitUntil(IsAnyInputPressed, cancellationToken: ct);

            // 最初の一押しにも手応えを返す。ここはボタンではないので自分で鳴らす
            if (UiSoundPlayer.Instance != null) UiSoundPlayer.Instance.Play(UiSoundPlayer.SoundKind.Decide);

            SetVisible(_pressAnyKeyGroup, false);

            // 失敗したらメニューまで戻ってやり直せるようにループする
            while (true)
            {
                SetVisible(_menuGroup, true);
                await GuardAsync(_menuGroup, _singlePlayButton, ct);

                MenuChoice choice = await WaitForMenuChoiceAsync(ct);
                SetVisible(_menuGroup, false);

                if (choice == MenuChoice.Quit)
                {
                    Quit();
                    return;
                }

                if (choice == MenuChoice.SinglePlay)
                {
                    if (await StartSinglePlayAsync(ct)) return;
                    continue;
                }

                // みんなで遊ぶ: なまえを入れてからマッチングへ
                SetVisible(_nameInputGroup, true);
                await GuardAsync(_nameInputGroup, _nameOkButton, ct);
                if (_nameInputField != null) _nameInputField.ActivateInputField();

                bool decided = await WaitForTwoChoiceAsync(_nameOkButton, _nameBackButton, ct);
                SetVisible(_nameInputGroup, false);
                if (!decided) continue;

                if (await StartMultiPlayAsync(ct)) return;
            }
        }

        /// <summary>ひとり用。通信せずにそのままゲームへ入る</summary>
        private async UniTask<bool> StartSinglePlayAsync(CancellationToken ct)
        {
            NetworkManager network = _networkManager != null ? _networkManager : NetworkManager.Instance;
            SceneLoader loader = _sceneLoader != null ? _sceneLoader : SceneLoader.Instance;

            SetVisible(_loadingGroup, true);
            _loadingGauge?.SetProgress(0.0f);
            SetLabel(_loadingMessage);

            if (loader == null)
            {
                Debug.LogError("[Title] SceneLoader がシーンにありません");
                await ShowErrorAsync(_failedMessage, ct);
                return false;
            }

            if (network != null)
            {
                network.SetNickName(string.Empty, _defaultPlayerName);
                // オフラインモードにしておくと、インゲームのネットワーク生成をそのまま使える
                await network.StartOfflineModeAsync(ct);
            }

            Debug.Log($"[Title] ひとりで遊ぶ → {_inGameSceneName}");
            await loader.LoadSceneAsync(_inGameSceneName, _loadingGauge, _minimumLoadingSeconds, ct);
            return true;
        }

        /// <summary>みんな用。接続してマッチングし、成功したらマッチング画面へ移る</summary>
        private async UniTask<bool> StartMultiPlayAsync(CancellationToken ct)
        {
            NetworkManager network = _networkManager != null ? _networkManager : NetworkManager.Instance;
            SceneLoader loader = _sceneLoader != null ? _sceneLoader : SceneLoader.Instance;

            SetVisible(_loadingGroup, true);
            _loadingGauge?.SetProgress(0.0f);
            SetLabel(_connectingMessage);

            if (network == null || loader == null)
            {
                Debug.LogError("[Title] NetworkManager か SceneLoader がシーンにありません");
                await ShowErrorAsync(_failedMessage, ct);
                return false;
            }

            string playerName = _nameInputField != null ? _nameInputField.text : string.Empty;
            network.SetNickName(playerName, _defaultPlayerName);

            // 接続中は実進捗が取れないので、時間で少しずつ伸ばして待たせる
            var gaugeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            AnimateConnectingGaugeAsync(gaugeCts.Token).Forget();

            ConnectResult connect = await network.ConnectAndJoinLobbyAsync(ct);
            if (connect != ConnectResult.Success)
            {
                gaugeCts.Cancel();
                await ShowErrorAsync(connect == ConnectResult.Full ? _crowdedMessage : _failedMessage, ct);
                return false;
            }

            MatchResult match = await network.FindMatchAsync(ct);
            gaugeCts.Cancel();

            if (match == MatchResult.GameInProgress)
            {
                await ShowErrorAsync(_inProgressMessage, ct);
                return false;
            }

            if (match == MatchResult.RoomFull)
            {
                await ShowErrorAsync(_roomFullMessage, ct);
                return false;
            }

            if (match == MatchResult.Failed)
            {
                await ShowErrorAsync(_failedMessage, ct);
                return false;
            }

            Debug.Log($"[Title] マッチング成功({match}) → {_lobbySceneName} へ");
            SetLabel(_loadingMessage);

            // 接続ぶん進んだところから続きを伸ばす
            var progress = new RangeProgress(_loadingGauge, CONNECT_PROGRESS_CAP, 1.0f);
            await loader.LoadSceneAsync(_lobbySceneName, progress, _minimumLoadingSeconds, ct);
            return true;
        }

        /// <summary>メニューでどれが押されたかを待つ</summary>
        private async UniTask<MenuChoice> WaitForMenuChoiceAsync(CancellationToken ct)
        {
            var completion = new UniTaskCompletionSource<MenuChoice>();

            void OnSingle() => completion.TrySetResult(MenuChoice.SinglePlay);
            void OnMulti() => completion.TrySetResult(MenuChoice.MultiPlay);
            void OnQuit() => completion.TrySetResult(MenuChoice.Quit);

            if (_singlePlayButton != null) _singlePlayButton.onClick.AddListener(OnSingle);
            if (_multiPlayButton != null) _multiPlayButton.onClick.AddListener(OnMulti);
            if (_quitButton != null) _quitButton.onClick.AddListener(OnQuit);

            try
            {
                return await completion.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                if (_singlePlayButton != null) _singlePlayButton.onClick.RemoveListener(OnSingle);
                if (_multiPlayButton != null) _multiPlayButton.onClick.RemoveListener(OnMulti);
                if (_quitButton != null) _quitButton.onClick.RemoveListener(OnQuit);
            }
        }

        /// <summary>2つのボタンのどちらが押されたかを待つ。第1引数が押されたら true</summary>
        private async UniTask<bool> WaitForTwoChoiceAsync(Button positive, Button negative, CancellationToken ct)
        {
            var completion = new UniTaskCompletionSource<bool>();

            void OnPositive() => completion.TrySetResult(true);
            void OnNegative() => completion.TrySetResult(false);

            if (positive != null) positive.onClick.AddListener(OnPositive);
            if (negative != null) negative.onClick.AddListener(OnNegative);

            try
            {
                return await completion.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                if (positive != null) positive.onClick.RemoveListener(OnPositive);
                if (negative != null) negative.onClick.RemoveListener(OnNegative);
            }
        }

        /// <summary>接続待ちのあいだ、ゲージを上限までゆっくり伸ばす</summary>
        private async UniTaskVoid AnimateConnectingGaugeAsync(CancellationToken ct)
        {
            try
            {
                float elapsed = 0.0f;
                while (!ct.IsCancellationRequested)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float ratio = Mathf.Clamp01(elapsed / Mathf.Max(0.1f, _connectGaugeSeconds)) * CONNECT_PROGRESS_CAP;
                    _loadingGauge?.SetProgress(ratio);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 接続が終わって止めただけなので何もしない
            }
        }

        /// <summary>エラー文言をしばらく見せてからローディングを閉じる</summary>
        private async UniTask ShowErrorAsync(string message, CancellationToken ct)
        {
            Debug.LogWarning($"[Title] {message}");
            SetLabel(message);
            _loadingGauge?.SetProgress(0.0f);
            await UniTask.Delay(TimeSpan.FromSeconds(_errorMessageSeconds), true, cancellationToken: ct);
            SetVisible(_loadingGroup, false);
        }

        /// <summary>直前の入力が決定として拾われないよう、少し待ってから操作を受け付ける</summary>
        private async UniTask GuardAsync(CanvasGroup group, Button firstSelected, CancellationToken ct)
        {
            if (group != null) group.interactable = false;
            await UniTask.Delay(TimeSpan.FromSeconds(INPUT_GUARD_SECONDS), true, cancellationToken: ct);
            if (group != null) group.interactable = true;

            // 選ぶのはパッドのときだけ。マウスや指では、触っていないボタンが
            // 選択の色で光ってしまい、押せる場所を取り違えさせる
            if (InputModeTracker.Current != InputMode.Gamepad) return;

            if (firstSelected != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(firstSelected.gameObject);
            }
        }

        /// <summary>キーボード・コントローラー・マウス・タッチのいずれかで入力があったか</summary>
        private static bool IsAnyInputPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame) return true;

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.buttonSouth.wasPressedThisFrame) return true;
                if (gamepad.buttonEast.wasPressedThisFrame) return true;
                if (gamepad.buttonWest.wasPressedThisFrame) return true;
                if (gamepad.buttonNorth.wasPressedThisFrame) return true;
                if (gamepad.startButton.wasPressedThisFrame) return true;
                if (gamepad.selectButton.wasPressedThisFrame) return true;
            }

            return false;
        }

        private void SetLabel(string message)
        {
            if (_loadingLabel != null) _loadingLabel.text = message;
        }

        /// <summary>表示・非表示と入力受付をまとめて切り替える</summary>
        private static void SetVisible(CanvasGroup group, bool visible)
        {
            if (group == null) return;

            group.alpha = visible ? 1.0f : 0.0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        /// <summary>ゲームを終了する。エディタでは再生を止める</summary>
        private static void Quit()
        {
            Debug.Log("[Title] ゲームを終了します");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- 内部クラス ----------------------------------

        /// <summary>0〜1の進捗を、指定した区間に置き換えて渡す</summary>
        private sealed class RangeProgress : IProgress<float>
        {
            private readonly IProgress<float> _target;
            private readonly float _from;
            private readonly float _to;

            public RangeProgress(IProgress<float> target, float from, float to)
            {
                _target = target;
                _from = from;
                _to = to;
            }

            public void Report(float value)
            {
                _target?.Report(Mathf.Lerp(_from, _to, Mathf.Clamp01(value)));
            }
        }
    }
}
