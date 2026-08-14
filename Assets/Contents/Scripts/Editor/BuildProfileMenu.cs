using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace ProjectKMP.EditorTools
{
    /// <summary>
    /// 配布用に『ひとり用』へ切り替えるためのメニュー。
    ///
    /// 実行時に選ぶ形にすると、配布したROMからマルチプレイへ入る道が残る。
    /// ビルドの設定そのものを切り替えることで、そのコードごとROMから消える。
    ///
    /// 切り替えるとUnityがスクリプトを組み直すので、少し待つ必要がある。
    /// その間にビルドを始めると古いままのものが出来てしまうため、
    /// 切り替えとビルドは別の操作に分けている。
    /// </summary>
    public static class BuildProfileMenu
    {
        // ---- 定数 ----------------------------------------

        private const string MENU = "ProjectKMP/ビルド/ひとり用に する";
        private const string SYMBOL = "KMP_SINGLE_ONLY";

        /// <summary>切り替える対象。Androidだけでなく、確認用のPCにも合わせる</summary>
        private static readonly NamedBuildTarget[] TARGETS =
        {
            NamedBuildTarget.Android,
            NamedBuildTarget.Standalone,
        };

        // ---- メニュー ------------------------------------

        [MenuItem(MENU, false, 1)]
        private static void Toggle()
        {
            bool next = !IsSingleOnly();

            foreach (NamedBuildTarget target in TARGETS) SetSymbol(target, next);

            string message = next
                ? "ひとり用に切り替えました。タイトルからマルチプレイが消えます。"
                : "通常の作りへ戻しました。マルチプレイが選べるようになります。";

            EditorUtility.DisplayDialog("配布の形を切り替えました",
                message + "\n\nスクリプトの組み直しが終わってからビルドしてください。", "わかった");
        }

        [MenuItem(MENU, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MENU, IsSingleOnly());

            return true;
        }

        // ---- 公開API -------------------------------------

        /// <summary>いまひとり用になっているか。ビルドのファイル名にも使う</summary>
        public static bool IsSingleOnly()
        {
            return GetSymbols(NamedBuildTarget.Android).Contains(SYMBOL);
        }

        // ---- 内部処理 ------------------------------------

        private static List<string> GetSymbols(NamedBuildTarget target)
        {
            string raw = PlayerSettings.GetScriptingDefineSymbols(target);

            return raw.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        }

        private static void SetSymbol(NamedBuildTarget target, bool on)
        {
            List<string> symbols = GetSymbols(target);

            if (on)
            {
                if (symbols.Contains(SYMBOL)) return;

                symbols.Add(SYMBOL);
            }
            else symbols.Remove(SYMBOL);

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
        }
    }
}
