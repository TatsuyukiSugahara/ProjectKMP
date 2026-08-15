using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// シーン遷移を一元管理するクラス。
/// UniTask でフェードアウト→ロード→フェードインの流れを管理する。
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>シーン名で遷移する</summary>
    public UniTask LoadSceneAsync(string sceneName,
        System.Threading.CancellationToken ct = default)
        => LoadSceneAsync(sceneName, null, 0.0f, ct);

    /// <summary>
    /// 進捗を受け取りながらシーン名で遷移する。
    /// progress には 0〜1 が渡る(ロード完了と最低表示時間の、遅いほうに合わせた値)。
    /// minimumDuration を指定すると、その秒数が経つまでシーンを切り替えずに待つ。
    /// ロードが一瞬で終わる軽いシーンでも、ローディング表示がきちんと見えるようにするための仕組み。
    /// </summary>
    public async UniTask LoadSceneAsync(string sceneName, IProgress<float> progress,
        float minimumDuration = 0.0f,
        System.Threading.CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct, destroyCancellationToken);
        var linkedCt = linkedCts.Token;

        // シーン遷移前にServiceLocatorをクリア
        ServiceLocator.Clear();

        // BGMを消しながら読み込む。読み込みには時間がかかるので、
        // 切り替わるころには鳴り終わっていて、場面がぶつ切りにならない
        ProjectKMP.Presentation.BgmPlayer.FadeOutCurrent();

        var op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        float startTime = Time.realtimeSinceStartup;
        progress?.Report(0.0f);

        // ロードが90%完了するまで待つ（残り10%はallowSceneActivationを待っている）
        while (true)
        {
            float elapsed = Time.realtimeSinceStartup - startTime;
            float loadRatio = Mathf.Clamp01(op.progress / 0.9f);
            float timeRatio = minimumDuration <= 0.0f ? 1.0f : Mathf.Clamp01(elapsed / minimumDuration);

            // 実進捗と経過時間の遅いほうに合わせると、ゲージが一瞬で振り切れない
            progress?.Report(Mathf.Min(loadRatio, timeRatio));

            if (op.progress >= 0.9f && elapsed >= minimumDuration) break;

            await UniTask.Yield(PlayerLoopTiming.Update, linkedCt);
        }

        progress?.Report(1.0f);

        op.allowSceneActivation = true;
        await UniTask.WaitUntil(() => op.isDone, cancellationToken: linkedCt);
    }

    // よく使うシーンへのショートカット
    public UniTask LoadTitle(System.Threading.CancellationToken ct = default)
        => LoadSceneAsync("Title", ct);

    public UniTask LoadLobby(System.Threading.CancellationToken ct = default)
        => LoadSceneAsync("Lobby", ct);

    public UniTask LoadBattle(System.Threading.CancellationToken ct = default)
        => LoadSceneAsync("Battle", ct);

    public UniTask LoadResult(System.Threading.CancellationToken ct = default)
        => LoadSceneAsync("Result", ct);
}
