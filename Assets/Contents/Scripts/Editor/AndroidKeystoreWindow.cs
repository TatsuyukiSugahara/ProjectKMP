using UnityEditor;
using UnityEngine;

namespace ProjectKMP.EditorTools
{
    /// <summary>
    /// 署名の鍵のパスワードを入れる小さな窓。
    ///
    /// パスワードはプロジェクトに保存されない。Unity を開き直すたびに消えるので、
    /// メニューからビルドしようとすると『署名できません』で止まる。
    ///
    /// ここで入れたものはこの端末にだけ覚えさせる。
    /// プロジェクトへは書き込まないので、他の人へ渡ることはない。
    /// </summary>
    public class AndroidKeystoreWindow : EditorWindow
    {
        // ---- 定数 ----------------------------------------

        private const string KEY_STORE_PASS = "ProjectKMP.Android.KeystorePass";
        private const string KEY_ALIAS_PASS = "ProjectKMP.Android.KeyaliasPass";

        // ---- 内部状態 ------------------------------------

        private string _keystorePass = string.Empty;
        private string _aliasPass = string.Empty;
        private bool _remember = true;

        // ---- 公開API -------------------------------------

        [MenuItem("ProjectKMP/ビルド/しょめいの パスワードを いれる", false, 20)]
        public static void Open()
        {
            var window = GetWindow<AndroidKeystoreWindow>(true, "しょめいの パスワード");
            window.minSize = new Vector2(420.0f, 220.0f);
            window.Load();
        }

        /// <summary>
        /// 覚えているパスワードを設定へ流し込む。ビルドの直前に呼ぶ。
        /// 入っていなければ false を返す。
        /// </summary>
        public static bool ApplySaved()
        {
            string keystorePass = EditorPrefs.GetString(KEY_STORE_PASS, string.Empty);
            string aliasPass = EditorPrefs.GetString(KEY_ALIAS_PASS, string.Empty);

            if (string.IsNullOrEmpty(keystorePass) || string.IsNullOrEmpty(aliasPass)) return false;

            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasPass = aliasPass;

            return true;
        }

        // ---- 内部処理 ------------------------------------

        private void Load()
        {
            _keystorePass = EditorPrefs.GetString(KEY_STORE_PASS, string.Empty);
            _aliasPass = EditorPrefs.GetString(KEY_ALIAS_PASS, string.Empty);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("鍵ファイル", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(PlayerSettings.Android.keystoreName)
                ? "(設定されていません)"
                : PlayerSettings.Android.keystoreName, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.LabelField("べつ名 = " + PlayerSettings.Android.keyaliasName, EditorStyles.miniLabel);

            EditorGUILayout.Space();

            _keystorePass = EditorGUILayout.PasswordField("鍵ファイルの パスワード", _keystorePass);
            _aliasPass = EditorGUILayout.PasswordField("べつ名の パスワード", _aliasPass);

            EditorGUILayout.Space();
            _remember = EditorGUILayout.ToggleLeft("この端末に おぼえておく", _remember);

            EditorGUILayout.HelpBox(
                "おぼえた内容はこの端末にだけ残ります。プロジェクトには書き込まれないので、" +
                "他の人のところへは渡りません。",
                MessageType.Info);

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GameObjectButton("けす"))
                {
                    EditorPrefs.DeleteKey(KEY_STORE_PASS);
                    EditorPrefs.DeleteKey(KEY_ALIAS_PASS);

                    _keystorePass = string.Empty;
                    _aliasPass = string.Empty;
                }

                if (GameObjectButton("ほぞん"))
                {
                    if (_remember)
                    {
                        EditorPrefs.SetString(KEY_STORE_PASS, _keystorePass);
                        EditorPrefs.SetString(KEY_ALIAS_PASS, _aliasPass);
                    }

                    PlayerSettings.Android.keystorePass = _keystorePass;
                    PlayerSettings.Android.keyaliasPass = _aliasPass;

                    Close();
                }
            }
        }

        private static bool GameObjectButton(string label)
        {
            return GUILayout.Button(label, GUILayout.Height(28.0f));
        }
    }
}
