using System;
using System.Linq;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace ProjectKMP.EditorTools
{
    /// <summary>
    /// Photon の AppId を入れる小さな窓。
    ///
    /// AppId はプロジェクトに保存されない。Photon の設定ファイルは GitHub に公開されているので、
    /// 入れたままにすると誰でも見られる状態になり、同時接続の枠を勝手に使われてしまう。
    ///
    /// ここで入れたものはこの端末にだけ覚えさせる。
    /// 再生ボタンを押したときとビルドするときに、自動で設定へ流し込む。
    /// </summary>
    public class PhotonAppIdWindow : EditorWindow
    {
        // ---- 定数 ----------------------------------------

        private const string KEY_APP_ID = "ProjectKMP.Photon.AppIdRealtime";
        private const string DASHBOARD_URL = "https://dashboard.photonengine.com/";

        /// <summary>自動ビルドで渡すときの引数名。CiBuild と同じ形にそろえている</summary>
        public const string ARG_APP_ID = "-kmpPhotonAppId";

        /// <summary>自動ビルドで渡すときの環境変数名</summary>
        public const string ENV_APP_ID = "PROJECTKMP_PHOTON_APPID";

        // ---- 内部状態 ------------------------------------

        private string _appId = string.Empty;

        // ---- 公開API -------------------------------------

        [MenuItem("ProjectKMP/Photon/AppId を いれる", false, 20)]
        public static void Open()
        {
            var window = GetWindow<PhotonAppIdWindow>(true, "Photon の AppId");
            window.minSize = new Vector2(460.0f, 300.0f);
            window.Load();
        }

        /// <summary>この端末に覚えている AppId。入っていなければ空文字を返す</summary>
        public static string Saved => EditorPrefs.GetString(KEY_APP_ID, string.Empty);

        /// <summary>
        /// 実際に使う AppId を決める。
        ///
        /// GitHub Actions のような自動ビルドでは、この端末の記憶(EditorPrefs)が空なので、
        /// 引数か環境変数から受け取れるようにしている。
        ///
        /// 優先順位: 起動時の引数 → 環境変数 → この端末の記憶
        /// </summary>
        public static string Resolve()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], ARG_APP_ID, StringComparison.OrdinalIgnoreCase)) continue;

                string fromArg = args[i + 1].Trim();
                if (ServerSettings.IsAppId(fromArg)) return fromArg;
            }

            string fromEnv = Environment.GetEnvironmentVariable(ENV_APP_ID);
            if (!string.IsNullOrEmpty(fromEnv) && ServerSettings.IsAppId(fromEnv.Trim())) return fromEnv.Trim();

            return Saved;
        }

        /// <summary>
        /// 使う AppId を Photon の設定へ流し込む。再生前とビルド前に呼ぶ。
        /// 入っていなければ false を返す。
        /// </summary>
        /// <param name="saveAsset">
        /// アセットへ書き込んで保存するなら true。
        /// ビルドでは Resources の中身がそのまま焼き込まれるので true が必要。
        /// 再生するだけならメモリ上の値で足りるので false にして、アセットを汚さない。
        /// </param>
        public static bool ApplySaved(bool saveAsset)
        {
            string appId = Resolve();
            if (!ServerSettings.IsAppId(appId)) return false;

            ServerSettings settings = PhotonNetwork.PhotonServerSettings;
            if (settings == null) return false;

            settings.AppSettings.AppIdRealtime = appId;

            if (saveAsset)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            return true;
        }

        /// <summary>
        /// 設定から AppId を消す。再生の終わりとビルドの後に呼ぶ。
        /// </summary>
        /// <param name="saveAsset">アセットへ空を書き込んで保存し直すなら true</param>
        public static void Clear(bool saveAsset)
        {
            ServerSettings settings = PhotonNetwork.PhotonServerSettings;
            if (settings == null) return;

            bool hadValue = !string.IsNullOrEmpty(settings.AppSettings.AppIdRealtime);
            if (!hadValue) return;

            settings.AppSettings.AppIdRealtime = string.Empty;

            // 再生中やビルド中に入れた値は、放っておくとアセットへ焼き付いて
            // そのまま commit されてしまう。空にした状態で保存し直しておく
            if (saveAsset)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }

        // ---- 内部処理 ------------------------------------

        private void Load()
        {
            _appId = Saved;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("いまの じょうたい", EditorStyles.boldLabel);

            bool hasSaved = ServerSettings.IsAppId(Saved);
            EditorGUILayout.LabelField(hasSaved
                ? "この端末に おぼえています"
                : "まだ 入っていません(このままだと オンラインに つながりません)",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("AppId", EditorStyles.boldLabel);
            _appId = EditorGUILayout.TextField("AppId (Realtime)", _appId);

            if (!string.IsNullOrEmpty(_appId) && !ServerSettings.IsAppId(_appId))
            {
                EditorGUILayout.HelpBox(
                    "AppId の かたちが ちがいます。" +
                    "「e3c003fd-fe68-4033-af50-947c49601b1e」のような 36文字の ならびを 入れてください。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "おぼえた内容はこの端末にだけ残ります。プロジェクトには書き込まれないので、" +
                "他の人のところへは渡りません。\n" +
                "再生ボタンを押したときと、ビルドするときに、自動で入ります。",
                MessageType.Info);

            EditorGUILayout.Space();

            if (GameObjectButton("Photon の サイトを ひらく (AppId を コピーする)"))
            {
                Application.OpenURL(DASHBOARD_URL);
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GameObjectButton("けす"))
                {
                    EditorPrefs.DeleteKey(KEY_APP_ID);
                    Clear(true);

                    _appId = string.Empty;
                }

                using (new EditorGUI.DisabledScope(!ServerSettings.IsAppId(_appId)))
                {
                    if (GameObjectButton("ほぞん"))
                    {
                        EditorPrefs.SetString(KEY_APP_ID, _appId.Trim());

                        Debug.Log("[Photon] AppId をこの端末に おぼえました");
                        Close();
                    }
                }
            }
        }

        private static bool GameObjectButton(string label)
        {
            return GUILayout.Button(label, GUILayout.Height(28.0f));
        }
    }
}
