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
    public async UniTask LoadSceneAsync(string sceneName,
        System.Threading.CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct, destroyCancellationToken);
        var linkedCt = linkedCts.Token;

        // シーン遷移前にServiceLocatorをクリア
        ServiceLocator.Clear();

        var op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        // ロードが90%完了するまで待つ（残り10%はallowSceneActivationを待っている）
        await UniTask.WaitUntil(() => op.progress >= 0.9f, cancellationToken: linkedCt);

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