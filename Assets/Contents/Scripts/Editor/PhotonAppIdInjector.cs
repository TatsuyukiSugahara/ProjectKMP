using System.Linq;
using Photon.Pun;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectKMP.EditorTools
{
    /// <summary>
    /// 覚えている AppId を、再生するときとビルドするときだけ Photon の設定へ流し込む。
    /// 終わったら消して、プロジェクトのファイルへ残らないようにする。
    ///
    /// AppId の入れかたは ProjectKMP > Photon > AppId を いれる から。
    /// </summary>
    [InitializeOnLoad]
    public static class PhotonAppIdInjector
    {
        static PhotonAppIdInjector()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // ビルドが途中で失敗すると後始末が呼ばれず、AppId が設定に残ったままになる。
            // Unity を開き直したときやスクリプトを書き直したときに掃除しておく
            EditorApplication.delayCall += CleanUpLeftover;
        }

        // ---- 内部処理 ------------------------------------

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // 再生中だけメモリ上に入れる。アセットは書き換えない
                    if (!PhotonAppIdWindow.ApplySaved(false))
                    {
                        Debug.LogWarning(
                            "[Photon] AppId が入っていません。" +
                            "メニューの ProjectKMP > Photon > AppId を いれる から入れてください");
                    }
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    PhotonAppIdWindow.Clear(true);
                    break;
            }
        }

        private static void CleanUpLeftover()
        {
            // 自動ビルドは毎回まっさらな状態から始まるので掃除は要らない。
            // ビルド中の書き込みと入れ違いになるのを避けるため、ここでは何もしない
            if (Application.isBatchMode) return;

            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (BuildPipeline.isBuildingPlayer) return;

            ServerSettings settings = PhotonNetwork.PhotonServerSettings;
            if (settings == null) return;
            if (string.IsNullOrEmpty(settings.AppSettings.AppIdRealtime)) return;

            PhotonAppIdWindow.Clear(true);

            Debug.Log(
                "[Photon] 設定に残っていた AppId を消しました。" +
                "AppId は ProjectKMP > Photon > AppId を いれる で管理します");
        }
    }

    /// <summary>
    /// ビルドの前後で AppId を出し入れする。
    /// Resources の中身はビルドへ焼き込まれるので、ビルド前だけはアセットへ書き込む必要がある。
    /// </summary>
    public class PhotonAppIdBuildHook : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        /// <summary>
        /// ビルド直前に AppId を設定へ書き込む。
        ///
        /// ひとり用ビルドはオフラインで動くのでサーバーに繋がらず、AppId は要らない。
        /// それ以外で入っていなければ、繋がらないものが出来上がるのでビルドを止める。
        /// </summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (PhotonAppIdWindow.ApplySaved(true)) return;

            if (IsSingleOnly(report))
            {
                Debug.LogWarning("[Photon] AppId なしで組みます(ひとり用なのでサーバーには繋ぎません)");
                return;
            }

            throw new BuildFailedException(
                "Photon の AppId が入っていません。\n" +
                "手元のUnity: メニューの ProjectKMP > Photon > AppId を いれる から入れてください。\n" +
                "自動ビルド: customParameters に " + PhotonAppIdWindow.ARG_APP_ID + " <AppId> を渡すか、" +
                "環境変数 " + PhotonAppIdWindow.ENV_APP_ID + " を設定してください。");
        }

        /// <summary>ひとり用ビルド(KMP_SINGLE_ONLY 付き)かどうか</summary>
        private static bool IsSingleOnly(BuildReport report)
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(report.summary.platform);
            NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(group);

            return PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(';')
                .Any(s => s.Trim() == "KMP_SINGLE_ONLY");
        }

        /// <summary>ビルドが終わったら設定から AppId を消す</summary>
        public void OnPostprocessBuild(BuildReport report)
        {
            PhotonAppIdWindow.Clear(true);
        }
    }
}
