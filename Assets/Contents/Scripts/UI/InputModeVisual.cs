using System.Collections.Generic;
using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 指定した機器で遊んでいるときだけ表示を出す。
    ///
    /// 移動スティック(指のときだけ)や、Aボタンの絵(パッドのときだけ)に使う。
    ///
    /// 消し方はオブジェクトごと無効化する。透明にするだけだと、
    /// 自前で表示を出し入れしている部品(VirtualStick など)と取り合いになって、
    /// 消えたり出たりを繰り返してしまうため。
    ///
    /// この部品自身は消える側に置かない。無効になると切り替えを受け取れなくなるので、
    /// 消えない親(Canvas など)に置いて、対象を指定して使う。
    /// </summary>
    public class InputModeVisual : MonoBehaviour
    {
        [SerializeField, Tooltip("出し入れするもの。複数まとめて指定できる")]
        private List<GameObject> _targets = new List<GameObject>();

        [Header("出す機器")]
        [SerializeField, Tooltip("指で触っているとき")]
        private bool _showOnTouch = true;

        [SerializeField, Tooltip("キーボードとマウスのとき")]
        private bool _showOnKeyboard;

        [SerializeField, Tooltip("ゲームパッドのとき")]
        private bool _showOnGamepad;

        private void OnEnable()
        {
            InputModeTracker.Ensure();
            InputModeTracker.Changed += Apply;

            Apply(InputModeTracker.Current);
        }

        private void OnDisable()
        {
            InputModeTracker.Changed -= Apply;
        }

        private void Apply(InputMode mode)
        {
            bool show = ShouldShow(mode);

            foreach (GameObject target in _targets)
            {
                if (target == null || target == gameObject) continue;

                target.SetActive(show);
            }
        }

        private bool ShouldShow(InputMode mode)
        {
            switch (mode)
            {
                case InputMode.Touch: return _showOnTouch;
                case InputMode.KeyboardMouse: return _showOnKeyboard;
                case InputMode.Gamepad: return _showOnGamepad;
                default: return true;
            }
        }
    }
}
