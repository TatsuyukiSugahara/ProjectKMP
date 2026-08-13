using ProjectKMP.UI;
using UnityEditor;

namespace ProjectKMP.EditorTools
{
    /// <summary>
    /// 操作機器を手で固定するメニュー。
    ///
    /// 実機が無いと、指で触ったときの見た目をエディタで確かめられない。
    /// タッチ判定は『画面を触った』ことで決まるので、マウスでは切り替わらないため。
    ///
    /// 再生中に切り替えると、その場で表示が入れ替わる。
    /// 固定はエディタでの確認用で、ビルドには含まれない。
    /// </summary>
    public static class InputModeMenu
    {
        private const string ROOT = "ProjectKMP/操作機器を固定/";

        [MenuItem(ROOT + "自動(固定しない)")]
        private static void SetAuto() => InputModeTracker.Force(null);

        [MenuItem(ROOT + "タッチ(スマホ)")]
        private static void SetTouch() => InputModeTracker.Force(InputMode.Touch);

        [MenuItem(ROOT + "キーボードとマウス")]
        private static void SetKeyboard() => InputModeTracker.Force(InputMode.KeyboardMouse);

        [MenuItem(ROOT + "コントローラー")]
        private static void SetGamepad() => InputModeTracker.Force(InputMode.Gamepad);
    }
}
