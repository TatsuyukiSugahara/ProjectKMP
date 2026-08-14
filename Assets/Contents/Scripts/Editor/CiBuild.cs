using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectKMP.EditorTools
{
    /// <summary>
    /// 自動ビルドから呼ぶ入口。
    ///
    /// メニューの切り替えはプロジェクトの設定に書き込まれるため、
    /// そのままだと『ひとり用にした状態』をコミットすることになる。
    /// 自動ビルドでは、その場限りの指定として引数から受け取る。
    ///
    /// 使い方(コマンドラインから):
    ///   Unity -batchmode -quit -projectPath . -executeMethod ProjectKMP.EditorTools.CiBuild.BuildAndroid -kmpSingleOnly
    ///
    /// 受け取る引数:
    ///   -kmpSingleOnly        ひとり用にする
    ///   -kmpDevelopment       確認用として組む
    ///   -kmpOutput <パス>     出力先を指定する
    /// </summary>
    public static class CiBuild
    {
        // ---- 定数 ----------------------------------------

        private const string SYMBOL_SINGLE_ONLY = "KMP_SINGLE_ONLY";
        private const string DEFAULT_OUTPUT_DIR = "Builds/Android";

        // ---- 入口 ----------------------------------------

        /// <summary>
        /// Unity が起動した時点で、引数を見て合言葉を仕込む。
        ///
        /// game-ci のような道具は自前のビルド処理を持っているので、
        /// こちらの入口が呼ばれない。起動の時点で仕込んでおけば、
        /// どのビルド処理を通っても同じ結果になる。
        ///
        /// 合言葉を変えるとスクリプトが組み直され、ここがもう一度通る。
        /// すでに入っていれば何もしないので、繰り返しにはならない。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ApplyFromCommandLine()
        {
            // 手元のUnityで動かしているときは、メニューの切り替えを尊重する
            if (!Application.isBatchMode) return;

            string[] args = Environment.GetCommandLineArgs();
            if (!HasFlag(args, "-kmpSingleOnly")) return;

            if (IsSingleOnlyApplied()) return;

            Debug.Log("[自動ビルド] ひとり用として組みます");
            ApplySingleOnly(true);
        }

        /// <summary>Android 向けに組む。-executeMethod から呼ぶ場合に使う</summary>
        public static void BuildAndroid()
        {
            string[] args = Environment.GetCommandLineArgs();

            bool singleOnly = HasFlag(args, "-kmpSingleOnly");
            bool development = HasFlag(args, "-kmpDevelopment");
            string output = GetValue(args, "-kmpOutput");

            Debug.Log("[自動ビルド] ひとり用=" + singleOnly + " 確認用=" + development);

            ApplySingleOnly(singleOnly);

            string path = string.IsNullOrEmpty(output) ? ResolveDefaultPath(singleOnly, development) : output;
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[自動ビルド] ふくめるシーンがありません");
                EditorApplication.Exit(1);
                return;
            }

            // 分割形式は端末へ入れるのが面倒なので、1つのファイルとして出す
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = development;

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[自動ビルド] できました: " + path +
                    " (" + (summary.totalSize / (1024.0 * 1024.0)).ToString("F1") + " MB)");

                EditorApplication.Exit(0);
                return;
            }

            Debug.LogError("[自動ビルド] 失敗しました 結果=" + summary.result + " エラー数=" + summary.totalErrors);
            EditorApplication.Exit(1);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>
        /// 合言葉を入れ替える。
        ///
        /// ここで書き換えた内容は、このあとのビルドに反映される。
        /// 自動ビルドは毎回まっさらな状態から始まるので、後片付けは要らない。
        /// </summary>
        private static void ApplySingleOnly(bool on)
        {
            foreach (NamedBuildTarget target in new[] { NamedBuildTarget.Android, NamedBuildTarget.Standalone })
            {
                List<string> symbols = PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(';')
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToList();

                symbols.Remove(SYMBOL_SINGLE_ONLY);
                if (on) symbols.Add(SYMBOL_SINGLE_ONLY);

                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
            }
        }

        private static bool IsSingleOnlyApplied()
        {
            return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android)
                .Split(';')
                .Any(s => s.Trim() == SYMBOL_SINGLE_ONLY);
        }

        private static string ResolveDefaultPath(bool singleOnly, bool development)
        {
            string project = Directory.GetParent(Application.dataPath).FullName;

            string suffix = development ? "dev" : "release";
            if (singleOnly) suffix += "_solo";

            string fileName = "GabuttoBuster_" + PlayerSettings.bundleVersion + "_" +
                DateTime.Now.ToString("yyyyMMdd_HHmm") + "_" + suffix + ".apk";

            return Path.Combine(project, DEFAULT_OUTPUT_DIR, fileName);
        }

        private static bool HasFlag(string[] args, string name)
        {
            return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;

                return args[i + 1];
            }

            return null;
        }
    }
}
