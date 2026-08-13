using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 戦っている間だけマウスカーソルを隠す。
    ///
    /// インゲームには押す場所が無い(技の枠は表示だけ)ので、
    /// カーソルが残っていても邪魔になるだけ。
    /// メニューではクリックで進むため、そちらでは出したままにする。
    ///
    /// 掴んで固定はしない。固定するとウィンドウから出られなくなり、
    /// 展示で操作に困る人が出るため。
    /// </summary>
    public class InGameCursor : MonoBehaviour
    {
        [SerializeField, Tooltip("この表示が出ている間はカーソルを隠す")]
        private bool _hideWhileEnabled = true;

        private void OnEnable()
        {
            if (!_hideWhileEnabled) return;

            Cursor.visible = false;
        }

        private void OnDisable()
        {
            // シーンを抜けたら必ず戻す。戻さないとメニューで押せなくなる
            Cursor.visible = true;
        }
    }
}
