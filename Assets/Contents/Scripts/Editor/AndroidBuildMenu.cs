using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectKMP.EditorTools
{
    /// <summary>
    /// Android 向けのビルドをメニューから一発で行う。
    ///
    /// 展示前は何度もビルドし直すことになる。そのたびに設定画面を開いて
    /// 出力先を打ち込んでいると、選び間違いが必ず起きる。
    ///
    /// 出力先も含めて決め打ちにすることで、誰がやっても同じ物が出来る。
    /// ファイル名に日時を入れるので、前のビルドを上書きしてしまうこともない。
    /// </summary>
    public static class AndroidBuildMenu
    {
        // ---- 定数 ----------------------------------------

        private const string ROOT = "ProjectKMP/ビルド/";

        /// <summary>出力先。プロジェクトの隣に置く</summary>
        private const string OUTPUT_DIR = "Builds/Android";

        // ---- メニュー ------------------------------------

        [MenuItem(ROOT + "Android: かくにん用をつくる (開発ビルド)", false, 10)]
        private static void BuildDevelopment()
        {
            Build(true);
        }

        [MenuItem(ROOT + "Android: てんじ用をつくる (製品ビルド)", false, 11)]
        private static void BuildRelease()
        {
            Build(false);
        }

        [MenuItem(ROOT + "出力さきフォルダをひらく", false, 30)]
        private static void OpenOutputFolder()
        {
            string dir = ResolveOutputDir();
            Directory.CreateDirectory(dir);

            EditorUtility.RevealInFinder(dir);
        }

        [MenuItem(ROOT + "いまの設定をたしかめる", false, 31)]
        private static void ShowSettings()
        {
            var android = NamedBuildTarget.Android;

            string message =
                "製品名: " + PlayerSettings.productName + "\n" +
                "バージョン: " + PlayerSettings.bundleVersion + " (" + PlayerSettings.Android.bundleVersionCode + ")\n" +
                "識別子: " + PlayerSettings.GetApplicationIdentifier(android) + "\n" +
                "対応CPU: " + PlayerSettings.Android.targetArchitectures + "\n" +
                "鍵ファイル: " + (HasKeystore() ? Path.GetFileName(PlayerSettings.Android.keystoreName) : "なし(デバッグ用の鍵で署名されます)") + "\n\n" +
                "ふくめるシーン:\n" + string.Join("\n", ResolveScenes());

            EditorUtility.DisplayDialog("いまのビルド設定", message, "とじる");
        }

        // ---- 内部処理 ------------------------------------

        private static void Build(bool development)
        {
            // 途中の変更を失わないよう、先に保存を促す。
            // 保存せずに進むと『直したはずの物が入っていない』が起きる
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[ビルド] 保存されなかったので中止しました");
                return;
            }

            string[] scenes = ResolveScenes();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("ビルドできません", "ふくめるシーンが1つもありません。", "とじる");
                return;
            }

            if (!PrepareSigning(development)) return;

            string dir = ResolveOutputDir();
            Directory.CreateDirectory(dir);

            // 日時を入れて、前のビルドを上書きしないようにする
            string suffix = development ? "dev" : "release";

            // 配布の形をファイル名に残す。あとから見て取り違えないようにする
            if (BuildProfileMenu.IsSingleOnly()) suffix += "_solo";
            string fileName = "GabuttoBuster_" + PlayerSettings.bundleVersion + "_" +
                DateTime.Now.ToString("yyyyMMdd_HHmm") + "_" + suffix + ".apk";

            string path = Path.Combine(dir, fileName);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            // 1つのファイルとして出す。分割形式は端末へ入れるのが面倒になる
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = development;

            Debug.Log("[ビルド] はじめます: " + path);

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                double megabytes = summary.totalSize / (1024.0 * 1024.0);

                Debug.Log("[ビルド] できました: " + path + " (" + megabytes.ToString("F1") + " MB / " +
                    summary.totalTime.TotalSeconds.ToString("F0") + " 秒)");

                EditorUtility.RevealInFinder(path);
                return;
            }

            Debug.LogError("[ビルド] 失敗しました。Console のエラーを見てください。" +
                " 結果=" + summary.result + " エラー数=" + summary.totalErrors);
        }

        /// <summary>
        /// 署名の支度をする。
        ///
        /// パスワードはプロジェクトに保存されないので、Unity を開き直すと消える。
        /// そのままビルドすると『署名できません』で止まるため、ここで面倒を見る。
        ///
        /// かくにん用は自分の端末へ入れるだけなので、鍵を使わない。
        /// パスワードを入れる手間が毎回かかると、試す回数が減ってしまう。
        /// </summary>
        private static bool PrepareSigning(bool development)
        {
            if (development)
            {
                PlayerSettings.Android.useCustomKeystore = false;
                return true;
            }

            if (!HasKeystore())
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "鍵ファイルがありません",
                    "署名の鍵が見つかりません。\n" +
                    "デバッグ用の鍵で署名されるので、配布には向きません。\n\nこのまま進めますか?",
                    "すすめる", "やめる");

                if (!proceed) return false;

                PlayerSettings.Android.useCustomKeystore = false;
                return true;
            }

            PlayerSettings.Android.useCustomKeystore = true;

            // 覚えているパスワードがあれば、それを使う
            if (AndroidKeystoreWindow.ApplySaved()) return true;

            if (!string.IsNullOrEmpty(PlayerSettings.Android.keystorePass)
                && !string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
            {
                return true;
            }

            EditorUtility.DisplayDialog(
                "パスワードが 要ります",
                "署名の鍵のパスワードが入っていません。\n\n" +
                "『ProjectKMP > ビルド > しょめいの パスワードを いれる』から入れてから、\n" +
                "もう一度ためしてください。",
                "わかった");

            AndroidKeystoreWindow.Open();
            return false;
        }

        /// <summary>ビルド設定で有効になっているシーンだけを取り出す</summary>
        private static string[] ResolveScenes()
        {
            var scenes = new List<string>();

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled) continue;
                if (string.IsNullOrEmpty(scene.path)) continue;

                scenes.Add(scene.path);
            }

            return scenes.ToArray();
        }

        private static string ResolveOutputDir()
        {
            string project = Directory.GetParent(Application.dataPath).FullName;

            return Path.Combine(project, OUTPUT_DIR);
        }

        private static bool HasKeystore()
        {
            string name = PlayerSettings.Android.keystoreName;

            return !string.IsNullOrEmpty(name) && File.Exists(name);
        }
    }
}
