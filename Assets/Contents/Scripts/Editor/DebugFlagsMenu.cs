using ProjectKMP.Battle;
using UnityEditor;

namespace ProjectKMP.EditorTools
{
    /// <summary>
    /// 確認用の切り替えをメニューから操作する。
    ///
    /// チェックが付いていれば、いま効いていることが見て分かる。
    /// インスペクタの数値を書き換える方式と違い、戻し忘れても
    /// ビルドには影響しないので、展示用のROMは安全なまま。
    /// </summary>
    public static class DebugFlagsMenu
    {
        private const string NO_COOLDOWN = "ProjectKMP/かくにん用/クールタイムを なくす";

        [MenuItem(NO_COOLDOWN, false, 100)]
        private static void ToggleNoCooldown()
        {
            DebugFlags.NoCooldown = !DebugFlags.NoCooldown;
        }

        [MenuItem(NO_COOLDOWN, true)]
        private static bool ToggleNoCooldownValidate()
        {
            Menu.SetChecked(NO_COOLDOWN, DebugFlags.NoCooldown);

            return true;
        }
    }
}
