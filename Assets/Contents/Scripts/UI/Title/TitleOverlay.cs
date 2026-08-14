namespace ProjectKMP.UI
{
    /// <summary>
    /// タイトルで何かの画面が前に出ているかを、まとめて持つ。
    ///
    /// 前に出ている間は、後ろのメニューが上下の送りや選択を受け取ってはいけない。
    /// 画面ごとに『自分が開いているか』を配って回ると、数が増えるほど繋ぎ忘れが出る。
    /// ここ1か所を見るようにしておけば、画面が増えても直す場所は増えない。
    /// </summary>
    public static class TitleOverlay
    {
        private static int _openCount;

        /// <summary>何かが前に出ているか</summary>
        public static bool IsOpen => _openCount > 0;

        /// <summary>開いたと伝える</summary>
        public static void Push()
        {
            _openCount++;
        }

        /// <summary>閉じたと伝える</summary>
        public static void Pop()
        {
            if (_openCount > 0) _openCount--;
        }

        /// <summary>シーンを抜けるときなどに、数え間違いを戻す</summary>
        public static void Reset()
        {
            _openCount = 0;
        }
    }
}
