using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectKMP.Gorilla;
using ProjectKMP.Player;
using ProjectKMP.UI;
using ProjectKMP.UI.Battle;
using ProjectKMP.UI.InGame;
using UnityEngine;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// インゲームに入った直後のカットシーンを進行させる。
    /// 暗転が明けると濃い霧。カメラは中央付近に据えたまま、その霧の奥から
    /// ゴリラが少しずつ近づいてくる(霧は薄めず、距離で抜けさせる)。
    /// 近づいたら足元のアップに切り替わり、中央へジャンプ。着地したら名前を出し、
    /// 「バトルスタート」の裏で通常の追従カメラへ滑らかに戻る。
    /// 見せているだけの演出なので各クライアントがローカルに再生する(同期しない)。
    /// 終わりのゴリラの位置は必ず着地点に揃えるので、誰かがスキップしてもズレは残らない。
    /// </summary>
    public class BattleIntroDirector : MonoBehaviour
    {
        // ---- 内部クラス ----------------------------------

        /// <summary>カメラの構図。混ぜて使うので順番に意味はない</summary>
        private enum Shot
        {
            /// <summary>霧の奥から近づいてくるのを据えたカメラで見る</summary>
            Lane,
            /// <summary>地面すれすれで足元を追う</summary>
            Feet,
            /// <summary>引いて全身を見せる</summary>
            Wide,
        }

        // ---- インスペクタ設定 ------------------------------

        [Header("参照")]
        [SerializeField, Tooltip("演出で動かすゴリラのAI。未設定ならシーンから探す")]
        private GorillaAI _gorillaAI;

        [SerializeField, Tooltip("カットシーンで動かすカメラ。未設定なら Main Camera を使う")]
        private Camera _camera;

        [SerializeField, Tooltip("通常時の追従カメラ。演出中は止める。未設定ならカメラから探す")]
        private ThirdPersonCamera _thirdPersonCamera;

        [SerializeField, Tooltip("名前や「バトルスタート」を出す画面表示。未設定ならシーンから探す")]
        private BattleIntroUI _ui;

        [SerializeField, Tooltip("演出中は隠すボスHPゲージ。未設定ならシーンから探す")]
        private BossHealthGauge _bossGauge;

        [SerializeField, Tooltip("演出中は隠すタッチ操作UI。未設定ならシーンから探す")]
        private TouchControls _touchControls;

        [Header("導入: 暗転と霧")]
        [SerializeField, Min(0.0f), Tooltip("暗転が明けるまでの時間(秒)。明けた先は霧だけが見えている")]
        private float _fadeInSeconds = 1.0f;

        [SerializeField, Tooltip("霧を出す。ゴリラが霧の奥から現れる導入になる")]
        private bool _useFog = true;

        [SerializeField, Tooltip("霧の色")]
        private Color _fogColor = new Color(0.80f, 0.82f, 0.85f, 1.0f);

        [SerializeField, Min(0.0f), Tooltip("霧の濃さ。0.09 くらいで 20m 先は見えず、10m でうっすら見えてくる")]
        private float _fogStartDensity = 0.09f;

        [SerializeField, Min(0.0f), Tooltip("晴れたあとに残す霧の濃さ。0で完全に消える")]
        private float _fogEndDensity = 0.0f;

        [SerializeField, Min(0.1f), Tooltip("足元カットに入ってから霧が晴れきるまでの時間(秒)")]
        private float _fogClearSeconds = 2.6f;

        [Header("動き")]
        [SerializeField, Tooltip("ジャンプして着地する場所(ステージ中央)")]
        private Vector3 _landPosition = Vector3.zero;

        [SerializeField, Tooltip("どちら側の森から走ってくるか。着地点から見た方向")]
        private Vector3 _comeFromDirection = new Vector3(0.0f, 0.0f, 1.0f);

        [SerializeField, Min(1.0f), Tooltip("走り出す位置の、着地点からの距離(メートル)。霧で見えない距離にする")]
        private float _runStartDistance = 22.0f;

        [SerializeField, Min(1.0f), Tooltip("霧から抜けて足元カットに切り替わる位置の、着地点からの距離(メートル)")]
        private float _approachEndDistance = 10.0f;

        [SerializeField, Min(1.0f), Tooltip("ジャンプに切り替わる位置の、着地点からの距離(メートル)")]
        private float _jumpStartDistance = 6.0f;

        [SerializeField, Min(0.1f), Tooltip("霧の奥から近づいてくる時間(秒)")]
        private float _approachSeconds = 2.4f;

        [SerializeField, Min(0.1f), Tooltip("足元を追う時間(秒)。この間にカメラが地面すれすれまで下りる")]
        private float _runSeconds = 1.0f;

        [SerializeField, Min(0.1f), Tooltip("ジャンプしている時間(秒)")]
        private float _jumpSeconds = 0.8f;

        [SerializeField, Min(0.0f), Tooltip("ジャンプの高さ(メートル)")]
        private float _jumpHeight = 3.2f;

        [SerializeField, Min(0.1f), Tooltip("着地してからカメラが引き切るまでの時間(秒)")]
        private float _landSeconds = 0.7f;

        [SerializeField, Min(1.0f), Tooltip("演出中のアニメ再生速度。通常時は遅くしてあるので等速に戻す")]
        private float _introAnimationSpeed = 1.0f;

        [Header("カメラ: 霧の奥を見る")]
        [SerializeField, Tooltip("着地点からどれだけ手前にカメラを据えるか(メートル)")]
        private float _laneDistance = 5.0f;

        [SerializeField, Tooltip("カメラの高さ(メートル)")]
        private float _laneHeight = 1.15f;

        [SerializeField, Tooltip("ゴリラのどのあたりを見るか(メートル)")]
        private float _laneLookHeight = 1.1f;

        [Header("カメラ: 足元のアップ")]
        [SerializeField, Tooltip("ゴリラの前方どれだけにカメラを置くか(メートル)。大きいほど寄ってくる感じになる")]
        private float _feetLead = 3.2f;

        [SerializeField, Tooltip("横方向のずらし(メートル)")]
        private float _feetSide = 1.6f;

        [SerializeField, Tooltip("カメラの高さ(メートル)。低くすると足元だけが映る")]
        private float _feetHeight = 0.35f;

        [SerializeField, Tooltip("見る高さ(メートル)")]
        private float _feetLookHeight = 0.35f;

        [Header("カメラ: 引きの構図")]
        [SerializeField, Tooltip("着地点からの距離(メートル)")]
        private float _wideDistance = 7.0f;

        [SerializeField, Tooltip("カメラの高さ(メートル)")]
        private float _wideHeight = 1.3f;

        [SerializeField, Tooltip("見る高さ(メートル)。低くするとゴリラが画面の上寄りに映り、下に名前を置ける")]
        private float _wideLookHeight = 0.9f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("ジャンプ終わりまでにどれだけ引くか。0で足元のまま、1で引き切る")]
        private float _jumpCameraBlend = 0.45f;

        [Header("着地の揺れ")]
        [SerializeField, Min(0.0f), Tooltip("揺れの大きさ(メートル)")]
        private float _shakeStrength = 0.35f;

        [SerializeField, Min(0.01f), Tooltip("揺れが収まるまでの時間(秒)")]
        private float _shakeSeconds = 0.25f;

        [Header("名前表示")]
        [SerializeField, Min(0.0f), Tooltip("「ゴリラ ゴリラ ゴリラ」を見せている時間(秒)")]
        private float _nameHoldSeconds = 1.0f;

        [Header("通常カメラへの復帰")]
        [SerializeField, Min(0.0f), Tooltip("バトルスタートが出てから、カメラが戻りはじめるまでの待ち(秒)")]
        private float _returnDelaySeconds = 0.7f;

        [SerializeField, Min(0.01f), Tooltip("通常の追従カメラの位置まで滑らかに戻る時間(秒)")]
        private float _returnSeconds = 1.1f;

        [Header("動作確認")]
        [SerializeField, Tooltip("シーン開始時に自動で再生する。切ると演出なしで始まる")]
        private bool _playOnStart = true;

        // ---- 内部状態 ------------------------------------

        private Transform _gorilla;
        private Animator _animator;
        private float _savedAnimatorSpeed = 1.0f;
        private float _groundY;

        private bool _fogApplied;
        private bool _savedFogEnabled;
        private FogMode _savedFogMode;
        private Color _savedFogColor;
        private float _savedFogDensity;

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            ResolveReferences();

            // 演出をしないときも、前のシーンの状態が残らないように必ず入れ直す
            BattlePlayGate.SetPlayable(!_playOnStart);
            if (!_playOnStart) return;

            _groundY = _gorilla != null ? _gorilla.position.y : 0.0f;

            // 演出中に自分で歩き出さないよう止めておく。
            // Start はここでは走らず、最後に enabled を戻したときに走るので、
            // そのとき居る場所(着地点)がそのまま徘徊の起点になる
            if (_gorillaAI != null) _gorillaAI.enabled = false;
            if (_thirdPersonCamera != null) _thirdPersonCamera.enabled = false;
        }

        private void Start()
        {
            if (!_playOnStart) return;
            PlayAsync(destroyCancellationToken).Forget();
        }

        private void OnDisable()
        {
            // 途中でシーンを抜けても霧が残らないようにする
            RestoreFog();
        }

        // ---- 内部処理 ------------------------------------

        private void ResolveReferences()
        {
            if (_gorillaAI == null) _gorillaAI = FindAnyObjectByType<GorillaAI>(FindObjectsInactive.Include);
            if (_gorillaAI != null) _gorilla = _gorillaAI.transform;
            if (_gorilla != null) _animator = _gorilla.GetComponent<Animator>();

            if (_camera == null) _camera = Camera.main;
            if (_thirdPersonCamera == null && _camera != null) _thirdPersonCamera = _camera.GetComponent<ThirdPersonCamera>();

            if (_ui == null) _ui = FindAnyObjectByType<BattleIntroUI>(FindObjectsInactive.Include);
            if (_bossGauge == null) _bossGauge = FindAnyObjectByType<BossHealthGauge>(FindObjectsInactive.Include);
            if (_touchControls == null) _touchControls = FindAnyObjectByType<TouchControls>(FindObjectsInactive.Include);

            if (_gorilla == null) Debug.LogError("[BattleIntro] ゴリラが見つからないため演出できません", this);
            if (_camera == null) Debug.LogError("[BattleIntro] カメラが見つからないため演出できません", this);
        }

        private async UniTaskVoid PlayAsync(CancellationToken token)
        {
            if (_gorilla == null || _camera == null) { Finish(); return; }

            CancellationTokenSource cts = null;
            try
            {
                // 他のコンポーネントの Start が終わってから始める(ゲージの初期表示より後に隠したいため)
                await UniTask.Yield(PlayerLoopTiming.Update, token);

                PrepareStage();

                cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                WatchSkipAsync(cts).Forget();

                try
                {
                    await RunSequenceAsync(cts.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    Debug.Log("[BattleIntro] スキップされました");
                }
            }
            catch (OperationCanceledException)
            {
                // シーンを抜けた。後片付けは不要
                return;
            }
            finally
            {
                if (cts != null) { cts.Cancel(); cts.Dispose(); }
            }

            Finish();
        }

        /// <summary>Aボタンの長押しを見張り、成立したら演出を打ち切る</summary>
        private async UniTaskVoid WatchSkipAsync(CancellationTokenSource cts)
        {
            try
            {
                if (_ui == null) return;
                if (await _ui.WaitForSkipAsync(cts.Token)) cts.Cancel();
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        /// <summary>演出を始める前に、邪魔な表示を隠して暗転と霧を張る</summary>
        private void PrepareStage()
        {
            if (_animator != null)
            {
                _savedAnimatorSpeed = _animator.speed;
                _animator.speed = _introAnimationSpeed;
            }

            if (_bossGauge != null) _bossGauge.SetVisible(false);
            if (_touchControls != null) _touchControls.SetControlsVisible(false);
            if (_ui != null) _ui.Prepare();

            ApplyFog();
        }

        private async UniTask RunSequenceAsync(CancellationToken ct)
        {
            Vector3 runDirection = CalcRunDirection();
            Vector3 start = _landPosition - runDirection * _runStartDistance;
            Vector3 approachEnd = _landPosition - runDirection * _approachEndDistance;
            Vector3 jumpStart = _landPosition - runDirection * _jumpStartDistance;

            _gorilla.SetPositionAndRotation(OnGround(start), Quaternion.LookRotation(runDirection, Vector3.up));

            // 暗転が明ける前に構図を合わせておく。1フレーム目からブレない
            ApplyCamera(_gorilla.position, runDirection, Shot.Lane, Shot.Lane, 0.0f, Vector3.zero);

            // 1) 霧の奥から近づいてくる。カメラは中央付近に据えたまま。
            //    霧は薄めない。遠いほど霧で見えないので、近づくだけで自然に姿が浮かび上がる
            PlayGorillaAnimation(GorillaAI.ANIM_RUN);
            await UniTask.WhenAll(
                MoveGorillaAsync(start, approachEnd, runDirection, _approachSeconds, 0.0f, Shot.Lane, Shot.Lane, 0.0f, 0.0f, ct),
                _ui.FadeFromBlackAsync(_fadeInSeconds, ct));

            // 2) 足元のアップへ。カメラが地面すれすれまで下りながら寄る。ここから霧も晴れていく
            ClearFogAsync(ct).Forget();
            await MoveGorillaAsync(approachEnd, jumpStart, runDirection, _runSeconds, 0.0f, Shot.Lane, Shot.Feet, 0.0f, 1.0f, ct);

            // 3) 中央へジャンプ。ここからカメラが引きはじめる
            PlayGorillaAnimation(GorillaAI.ANIM_JUMP);
            await MoveGorillaAsync(jumpStart, _landPosition, runDirection, _jumpSeconds, _jumpHeight, Shot.Feet, Shot.Wide, 0.0f, _jumpCameraBlend, ct);

            // 4) 着地。揺らしながら引き切って全身を見せる
            PlayGorillaAnimation(GorillaAI.ANIM_IDLE);
            await LandAsync(runDirection, ct);

            // 5) 名前を出す
            await _ui.ShowNameAsync(_nameHoldSeconds, ct);

            // 6) バトルスタート。その裏で通常の追従カメラの位置へ滑らかに戻す
            ReturnCameraAsync(ct).Forget();
            await _ui.ShowBattleStartAsync(ct);
        }

        /// <summary>ゴリラを from から to へ動かしつつ、カメラの構図も混ぜて進める</summary>
        private async UniTask MoveGorillaAsync(Vector3 from, Vector3 to, Vector3 runDirection, float seconds,
            float arcHeight, Shot shotFrom, Shot shotTo, float blendFrom, float blendTo, CancellationToken ct)
        {
            float elapsed = 0.0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);

                // 山なりに飛ばす。sin なので離陸と着地でちょうど高さ0になる
                float height = arcHeight * Mathf.Sin(t * Mathf.PI);
                _gorilla.position = OnGround(Vector3.Lerp(from, to, t) + Vector3.up * height);

                ApplyCamera(_gorilla.position, runDirection, shotFrom, shotTo, Mathf.Lerp(blendFrom, blendTo, t), Vector3.zero);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            _gorilla.position = OnGround(to);
        }

        /// <summary>着地の瞬間。カメラを揺らしながら引きの構図まで持っていく</summary>
        private async UniTask LandAsync(Vector3 runDirection, CancellationToken ct)
        {
            float elapsed = 0.0f;
            while (elapsed < _landSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _landSeconds);

                // 着地直後がいちばん強く、_shakeSeconds でゼロになる
                float shake = _shakeStrength * Mathf.Clamp01(1.0f - elapsed / _shakeSeconds);
                var offset = new Vector3(
                    UnityEngine.Random.Range(-shake, shake),
                    UnityEngine.Random.Range(-shake, shake),
                    0.0f);

                ApplyCamera(_landPosition, runDirection, Shot.Feet, Shot.Wide, Mathf.Lerp(_jumpCameraBlend, 1.0f, t), offset);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            ApplyCamera(_landPosition, runDirection, Shot.Feet, Shot.Wide, 1.0f, Vector3.zero);
        }

        /// <summary>
        /// カットシーンのカメラから、通常の追従カメラの位置へイージングしながら戻す。
        /// 追従先は毎フレーム聞き直すので、途中で対象が動いても最後にズレない。
        /// </summary>
        private async UniTask ReturnCameraAsync(CancellationToken ct)
        {
            if (_thirdPersonCamera == null || _camera == null) return;

            if (_returnDelaySeconds > 0.0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_returnDelaySeconds), cancellationToken: ct);
            }

            Vector3 fromPosition = _camera.transform.position;
            Quaternion fromRotation = _camera.transform.rotation;

            float elapsed = 0.0f;
            while (elapsed < _returnSeconds)
            {
                elapsed += Time.deltaTime;

                // 動きはじめと止まりぎわを緩めて、切り替わりを目立たせない
                float t = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(elapsed / _returnSeconds));

                // プレイヤーがまだ生成されていなければ戻す先が分からないので、そのままにする
                if (!_thirdPersonCamera.TryGetFollowPose(out Vector3 toPosition, out Quaternion toRotation)) return;

                _camera.transform.position = Vector3.Lerp(fromPosition, toPosition, t);
                _camera.transform.rotation = Quaternion.Slerp(fromRotation, toRotation, t);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        /// <summary>演出を終えて、通常のインゲームに戻す。スキップでもここを必ず通る</summary>
        private void Finish()
        {
            if (_ui != null) _ui.HideAll();
            RestoreFog();

            // スキップされても最終状態を揃える
            if (_gorilla != null)
            {
                _gorilla.SetPositionAndRotation(OnGround(_landPosition), Quaternion.LookRotation(CalcRunDirection(), Vector3.up));
            }

            if (_animator != null) _animator.speed = _savedAnimatorSpeed;
            if (_gorillaAI != null) _gorillaAI.enabled = true;
            if (_thirdPersonCamera != null) _thirdPersonCamera.enabled = true;

            if (_bossGauge != null) _bossGauge.SetVisible(true);
            if (_touchControls != null) _touchControls.SetControlsVisible(true);

            BattlePlayGate.SetPlayable(true);
            Debug.Log("[BattleIntro] カットシーン終了。操作を解放しました");
        }

        // ---- カメラ --------------------------------------

        /// <summary>2つの構図を blend01 で混ぜてカメラに反映する。混ぜるだけなので途中で止めても破綻しない</summary>
        private void ApplyCamera(Vector3 gorillaPosition, Vector3 runDirection, Shot from, Shot to, float blend01, Vector3 shakeOffset)
        {
            GetShot(from, gorillaPosition, runDirection, out Vector3 fromPosition, out Vector3 fromLook);
            GetShot(to, gorillaPosition, runDirection, out Vector3 toPosition, out Vector3 toLook);

            float t = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(blend01));
            Vector3 position = Vector3.Lerp(fromPosition, toPosition, t) + shakeOffset;
            Vector3 target = Vector3.Lerp(fromLook, toLook, t);

            _camera.transform.position = position;
            _camera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
        }

        /// <summary>指定の構図でのカメラ位置と注視点を求める</summary>
        private void GetShot(Shot shot, Vector3 gorillaPosition, Vector3 runDirection, out Vector3 position, out Vector3 lookAt)
        {
            switch (shot)
            {
                case Shot.Lane:
                    // 着地点の少し先に据えて、走ってくる方向を見る。
                    // カメラが動かないぶん、遠くの霧から出てくる過程がそのまま見える
                    position = _landPosition + runDirection * _laneDistance + Vector3.up * _laneHeight;
                    lookAt = gorillaPosition + Vector3.up * _laneLookHeight;
                    return;

                case Shot.Feet:
                    // 進行方向の斜め前・地面すれすれ。走ってくる足がレンズに近づいて見える
                    Vector3 right = Vector3.Cross(Vector3.up, runDirection).normalized;
                    position = gorillaPosition + runDirection * _feetLead + right * _feetSide + Vector3.up * _feetHeight;
                    lookAt = gorillaPosition + Vector3.up * _feetLookHeight;
                    return;

                default:
                    // ゴリラは進行方向を向いているので、その前に立つと正面から見た構図になる。
                    // 見る高さを低めにすると、ゴリラが画面の上寄りに映って下に名前を置ける
                    position = _landPosition + runDirection * _wideDistance + Vector3.up * _wideHeight;
                    lookAt = _landPosition + Vector3.up * _wideLookHeight;
                    return;
            }
        }

        // ---- 霧 ------------------------------------------

        /// <summary>霧を張る。元の設定は覚えておいて最後に戻す</summary>
        private void ApplyFog()
        {
            if (!_useFog || _fogApplied) return;

            _savedFogEnabled = RenderSettings.fog;
            _savedFogMode = RenderSettings.fogMode;
            _savedFogColor = RenderSettings.fogColor;
            _savedFogDensity = RenderSettings.fogDensity;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogDensity = _fogStartDensity;

            _fogApplied = true;
        }

        /// <summary>霧をだんだん晴らす</summary>
        private async UniTask ClearFogAsync(CancellationToken ct)
        {
            if (!_fogApplied) return;

            float elapsed = 0.0f;
            while (elapsed < _fogClearSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(elapsed / _fogClearSeconds));
                RenderSettings.fogDensity = Mathf.Lerp(_fogStartDensity, _fogEndDensity, t);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            RenderSettings.fogDensity = _fogEndDensity;
        }

        /// <summary>張った霧を元の設定に戻す</summary>
        private void RestoreFog()
        {
            if (!_fogApplied) return;

            RenderSettings.fog = _savedFogEnabled;
            RenderSettings.fogMode = _savedFogMode;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogDensity = _savedFogDensity;

            _fogApplied = false;
        }

        // ---- 小さな計算 ----------------------------------

        /// <summary>ゴリラが進む向き。「どこから来るか」の逆になる</summary>
        private Vector3 CalcRunDirection()
        {
            Vector3 direction = _comeFromDirection;
            direction.y = 0.0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            return -direction.normalized;
        }

        /// <summary>高さを地面に合わせる。ジャンプ中の持ち上げぶんは足したまま残す</summary>
        private Vector3 OnGround(Vector3 position)
        {
            return new Vector3(position.x, _groundY + Mathf.Max(0.0f, position.y - _groundY), position.z);
        }

        private void PlayGorillaAnimation(string stateName)
        {
            if (_animator != null) _animator.CrossFade(stateName, 0.12f);
        }
    }
}
