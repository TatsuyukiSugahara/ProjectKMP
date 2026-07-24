using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectKMP.Sandbox
{
    /// <summary>
    /// 通信テスト用シーン単体の Android APK を書き出すビルドスクリプト。
    /// Build Settings のシーン構成(Title〜Result)には触らず、対象シーンを引数で明示する。
    /// </summary>
    public static class MultiplayTestBuilder
    {
        // ---- 定数 ----------------------------------------
        private const string SCENE_PATH      = "Assets/_Sandbox/Sugahara/MultiplayTest.unity";
        private const string BASE_IDENTIFIER = "jp.projectkmp.opencampus";
        private const string SANDBOX_SUFFIX  = ".sandbox"; // 本番APKと同一端末に共存させるため末尾を分ける
        private const string OUTPUT_DIR      = "Builds/Android";
        private const string APK_NAME        = "MultiplayTest.apk";

        // ---- 公開API -------------------------------------

        [MenuItem("ProjectKMP/Build/MultiplayTest APK (Android)")]
        public static void BuildAndroid()
        {
            // ターゲットが違う場合、切り替えでドメインリロードが走ってビルドが中断される。
            // そのため切り替えだけ先に済ませ、完了後に再実行してもらう。
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[Build] ビルドターゲットを Android に切り替えます。" +
                          "再インポートが終わったらメニューからもう一度実行してください。");
                EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android);
                return;
            }

            Directory.CreateDirectory(OUTPUT_DIR);

            // テストビルドの間だけパッケージ名を差し替え、終わったら必ず戻す
            string previousIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            bool previousAppBundle = EditorUserBuildSettings.buildAppBundle;

            try
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BASE_IDENTIFIER + SANDBOX_SUFFIX);
                EditorUserBuildSettings.buildAppBundle = false; // AAB ではなく APK を出す

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { SCENE_PATH },
                    locationPathName = Path.Combine(OUTPUT_DIR, APK_NAME),
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.Development, // 実機のログを追えるように開発ビルドにする
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                LogReport(report, options.locationPathName);
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, previousIdentifier);
                EditorUserBuildSettings.buildAppBundle = previousAppBundle;
                AssetDatabase.SaveAssets();
            }
        }

        // ---- 内部処理 ------------------------------------

        private static void LogReport(BuildReport report, string outputPath)
        {
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                float megaBytes = summary.totalSize / (1024.0f * 1024.0f);
                Debug.Log($"[Build] 成功 : {outputPath} / {megaBytes:F1} MB / {summary.totalTime.TotalMinutes:F1} 分");
                return;
            }

            Debug.LogError($"[Build] 失敗 : {summary.result} / エラー {summary.totalErrors} 件");

            // どのステップで転んだかが分かるよう、エラー行だけ抜き出す
            foreach (BuildStep step in report.steps)
            {
                foreach (BuildStepMessage message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                    {
                        Debug.LogError($"[Build] {step.name} : {message.content}");
                    }
                }
            }
        }
    }
}
