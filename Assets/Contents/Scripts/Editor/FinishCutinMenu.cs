using System.Threading;
using ProjectKMP.Presentation;
using UnityEditor;
using UnityEngine;

namespace ProjectKMP.EditorTools
{
    /// <summary>
    /// 締めの演出だけを流して確かめる。
    ///
    /// 本来はボスを倒さないと見られないため、
    /// 間合いや文字の位置を直すたびに数分かかっていた。
    ///
    /// 演出そのものは『流してくれ』と言われたら流すだけの作りなので、
    /// 外から呼べば単体で確かめられる。呼ぶ口をここに用意しておく。
    /// </summary>
    public static class FinishCutinMenu
    {
        // ---- 定数 ----------------------------------------

        private const string MENU_PLAY = "ProjectKMP/かくにん用/しめの えんしゅつを ながす";
        private const string MENU_PLAY_SLOW = "ProjectKMP/かくにん用/しめの えんしゅつを ながす(スロー付き)";

        /// <summary>本番と同じ遅さ。間合いはこの状態で見ないと分からない</summary>
        private const float SLOW_SCALE = 0.25f;

        private const float SLOW_DURATION_SEC = 2.5f;

        // ---- メニュー ------------------------------------

        [MenuItem(MENU_PLAY, false, 110)]
        private static void Play()
        {
            PlayInternal(false);
        }

        [MenuItem(MENU_PLAY_SLOW, false, 111)]
        private static void PlaySlow()
        {
            PlayInternal(true);
        }

        [MenuItem(MENU_PLAY, true)]
        [MenuItem(MENU_PLAY_SLOW, true)]
        private static bool Validate()
        {
            // 再生中でないと画面が無いので流せない
            return Application.isPlaying;
        }

        // ---- 内部処理 ------------------------------------

        private static void PlayInternal(bool slow)
        {
            FinishCutin.Clear();
            FinishCutin.Play(ResolveTargetPosition(), CancellationToken.None);

            if (!slow) return;

            EditorCoroutineSlow();
        }

        /// <summary>
        /// 噛み跡を出す場所を決める。
        /// ボスが居ればその位置、居なければ画面の真ん中あたりに出す。
        /// </summary>
        private static Vector3 ResolveTargetPosition()
        {
            var boss = Object.FindAnyObjectByType<Monster.BossHealth>(FindObjectsInactive.Include);
            if (boss != null) return boss.transform.position;

            Camera camera = Camera.main;
            if (camera != null) return camera.transform.position + camera.transform.forward * 12.0f;

            return Vector3.zero;
        }

        /// <summary>本番と同じ遅さにしてから、時間をかけて戻す</summary>
        private static void EditorCoroutineSlow()
        {
            Time.timeScale = SLOW_SCALE;

            double endTime = EditorApplication.timeSinceStartup + SLOW_DURATION_SEC;

            void Tick()
            {
                if (!Application.isPlaying || EditorApplication.timeSinceStartup >= endTime)
                {
                    Time.timeScale = 1.0f;
                    EditorApplication.update -= Tick;

                    return;
                }
            }

            EditorApplication.update += Tick;
        }
    }
}
