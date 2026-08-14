using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ProjectKMP.EditorTools
{
    /// <summary>
    /// 出来たROMを Android の端末へ入れる。
    ///
    /// 毎回ファイルを探して端末へコピーして、端末側で開いて…とやっていると
    /// 試す回数が減る。ケーブルで繋いだままメニューを選ぶだけで済むようにする。
    ///
    /// 端末とのやり取りには adb を使う。Android の開発道具に付いてくるもので、
    /// Unity が Android 用の一式を入れていれば、たいてい一緒に入っている。
    /// </summary>
    public static class AndroidDeployMenu
    {
        // ---- 定数 ----------------------------------------

        private const string ROOT = "ProjectKMP/ビルド/";
        private const string OUTPUT_DIR = "Builds/Android";

        // ---- メニュー ------------------------------------

        [MenuItem(ROOT + "端末に いれて うごかす", false, 40)]
        private static void InstallAndLaunch()
        {
            string apk = FindNewestApk();
            if (apk == null) return;

            if (!Install(apk)) return;

            Launch();
        }

        [MenuItem(ROOT + "端末に いれるだけ", false, 41)]
        private static void InstallOnly()
        {
            string apk = FindNewestApk();
            if (apk == null) return;

            Install(apk);
        }

        [MenuItem(ROOT + "つながっている端末を みる", false, 42)]
        private static void ShowDevices()
        {
            string adb = ResolveAdb();
            if (adb == null) { ShowAdbMissing(); return; }

            string output = Run(adb, "devices -l", out int code);

            EditorUtility.DisplayDialog("つながっている端末",
                string.IsNullOrWhiteSpace(output) ? "(応答がありません)" : output, "とじる");
        }

        [MenuItem(ROOT + "端末の ログを ながす", false, 43)]
        private static void OpenLogcat()
        {
            string adb = ResolveAdb();
            if (adb == null) { ShowAdbMissing(); return; }

            // ログは流し続けるものなので、別の窓で開く。
            // ここで待つとエディタが止まってしまう
            string identifier = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
            string command = "\"" + adb + "\" logcat -s Unity:V " + identifier + ":V";

            OpenInTerminal(command);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>出力先で一番新しい APK を返す</summary>
        private static string FindNewestApk()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OUTPUT_DIR);

            if (!Directory.Exists(dir))
            {
                EditorUtility.DisplayDialog("ROM が ありません",
                    "まだ1つもビルドされていません。\n先に『Android: かくにん用をつくる』を実行してください。", "わかった");
                return null;
            }

            string newest = null;
            DateTime newestTime = DateTime.MinValue;

            foreach (string path in Directory.GetFiles(dir, "*.apk"))
            {
                DateTime time = File.GetLastWriteTime(path);
                if (time <= newestTime) continue;

                newestTime = time;
                newest = path;
            }

            if (newest != null) return newest;

            EditorUtility.DisplayDialog("ROM が ありません",
                "出力先に APK が見つかりませんでした。\n" + dir, "わかった");

            return null;
        }

        /// <summary>端末へ入れる。すでに入っていれば上書きする</summary>
        private static bool Install(string apk)
        {
            string adb = ResolveAdb();
            if (adb == null) { ShowAdbMissing(); return false; }

            if (!HasDevice(adb))
            {
                EditorUtility.DisplayDialog("端末が つながっていません",
                    "ケーブルで繋いで、端末側で『USBデバッグ』を許可してください。\n\n" +
                    "端末に確認の窓が出ていないか見てみてください。", "わかった");
                return false;
            }

            Debug.Log("[転送] 入れています: " + Path.GetFileName(apk));
            EditorUtility.DisplayProgressBar("端末へ 転送中", Path.GetFileName(apk), 0.5f);

            // -r は上書き。付けないと『すでに入っています』で失敗する
            string output = Run(adb, "install -r \"" + apk + "\"", out int code);

            EditorUtility.ClearProgressBar();

            if (code == 0 && output.Contains("Success"))
            {
                Debug.Log("[転送] 入りました: " + Path.GetFileName(apk));
                return true;
            }

            Debug.LogError("[転送] 失敗しました\n" + output);

            EditorUtility.DisplayDialog("入れられませんでした",
                "署名が前のものと違う場合は、端末から一度アンインストールしてください。\n\n" + output, "わかった");

            return false;
        }

        /// <summary>端末側でアプリを起動する</summary>
        private static void Launch()
        {
            string adb = ResolveAdb();
            if (adb == null) return;

            string identifier = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);

            Run(adb, "shell monkey -p " + identifier + " -c android.intent.category.LAUNCHER 1", out int code);

            Debug.Log("[転送] 起動しました: " + identifier);
        }

        private static bool HasDevice(string adb)
        {
            string output = Run(adb, "devices", out int code);

            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("List of devices")) continue;

                // 『つながっている』と言えるのは device と出ている行だけ。
                // unauthorized は許可待ち、offline は繋がりかけ
                if (trimmed.EndsWith("\tdevice") || trimmed.EndsWith(" device")) return true;
            }

            return false;
        }

        /// <summary>adb の場所を探す。Unity が入れた一式の中にあることが多い</summary>
        private static string ResolveAdb()
        {
            var candidates = new System.Collections.Generic.List<string>();

            string sdk = EditorPrefs.GetString("AndroidSdkRoot", string.Empty);
            if (!string.IsNullOrEmpty(sdk)) candidates.Add(Path.Combine(sdk, "platform-tools/adb"));

            // Unity に同梱されている一式
            string editor = Path.GetDirectoryName(EditorApplication.applicationPath);
            if (!string.IsNullOrEmpty(editor))
            {
                candidates.Add(Path.Combine(editor, "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"));
                candidates.Add(Path.Combine(editor, "Unity.app/Contents/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"));
            }

            candidates.Add(Path.Combine(EditorApplication.applicationPath,
                "Contents/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"));

            // 自分で入れた場合の置き場所
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(home, "Library/Android/sdk/platform-tools/adb"));
            candidates.Add("/usr/local/bin/adb");
            candidates.Add("/opt/homebrew/bin/adb");

            foreach (string path in candidates)
            {
                string normalized = path;
                if (Application.platform == RuntimePlatform.WindowsEditor) normalized += ".exe";

                if (File.Exists(normalized)) return normalized;
            }

            return null;
        }

        private static void ShowAdbMissing()
        {
            EditorUtility.DisplayDialog("adb が 見つかりません",
                "端末とやり取りする道具(adb)が見つかりませんでした。\n\n" +
                "Unity の設定 > External Tools で Android SDK の場所を確かめてください。", "わかった");
        }

        /// <summary>道具を動かして、出てきた文字を返す</summary>
        private static string Run(string fileName, string arguments, out int exitCode)
        {
            exitCode = -1;

            try
            {
                var info = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(info))
                {
                    var builder = new StringBuilder();
                    builder.Append(process.StandardOutput.ReadToEnd());
                    builder.Append(process.StandardError.ReadToEnd());

                    process.WaitForExit(120000);
                    exitCode = process.ExitCode;

                    return builder.ToString();
                }
            }
            catch (Exception e)
            {
                return "実行できませんでした: " + e.Message;
            }
        }

        /// <summary>流し続けるものは、別の窓へ渡す</summary>
        private static void OpenInTerminal(string command)
        {
            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    Process.Start("cmd.exe", "/k " + command);
                    return;
                }

                string script = "tell application \"Terminal\" to do script \"" +
                    command.Replace("\"", "\\\"") + "\"";

                Process.Start("osascript", "-e '" + script + "'");
            }
            catch (Exception e)
            {
                Debug.LogError("[転送] ログの窓を開けませんでした: " + e.Message);
            }
        }
    }
}
