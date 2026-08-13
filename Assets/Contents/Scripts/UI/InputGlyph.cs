using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 『何を押せばよいか』を1つ表示する部品。
    ///
    /// 動作を指定しておくと、いまの機器に合わせて中身を入れ替える。
    /// 押せる場所がある機器(タッチ、メニューでのマウス)では、表示そのものを消す。
    /// そこにボタンがあるなら、それが答えなので添える必要がない。
    ///
    /// 下地の形と文字は表(InputGlyphTable)が持つ。
    /// 表に絵が入っていればそちらを出すので、公式のボタン素材へ差し替えるときも
    /// この部品には手を入れずに済む。
    /// </summary>
    public class InputGlyph : MonoBehaviour
    {
        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("どの動作の入力を出すか")]
        private GameAction _action = GameAction.Attack;

        [SerializeField, Tooltip("割り当ての表。未設定なら何も出ない")]
        private InputGlyphTable _table;

        [SerializeField, Tooltip("下地を出す Image")]
        private Image _shapeImage;

        [SerializeField, Tooltip("グリフに載せる文字")]
        private Text _label;

        [SerializeField, Tooltip("『長押し』などの添え字。未設定なら出さない")]
        private Text _suffix;

        [SerializeField, Tooltip("長押しのときに添える文字")]
        private string _holdSuffix = "長押し";

        // ---- 公開API -------------------------------------

        /// <summary>出す動作を差し替える</summary>
        public void SetAction(GameAction action)
        {
            _action = action;
            Refresh(InputModeTracker.Current);
        }

        // ---- 内部処理 ------------------------------------

        private void OnEnable()
        {
            InputModeTracker.Ensure();
            InputModeTracker.Changed += Refresh;

            Refresh(InputModeTracker.Current);
        }

        private void OnDisable()
        {
            InputModeTracker.Changed -= Refresh;
        }

        /// <summary>いまの機器に合わせて中身を入れ替える</summary>
        private void Refresh(InputMode mode)
        {
            InputGlyphTable.Entry entry = _table != null ? _table.Find(_action, mode) : null;

            // 表に無い、または『出さない』指定なら、表示ごと消す
            bool visible = entry != null && entry.Shape != GlyphShape.None;
            if (_shapeImage != null) _shapeImage.transform.parent.gameObject.SetActive(visible);

            if (!visible) return;

            if (entry.Sprite != null)
            {
                // 差し替えの絵があるときは、文字を出さずに絵だけで見せる
                _shapeImage.sprite = entry.Sprite;
                _shapeImage.color = Color.white;

                if (_label != null) _label.gameObject.SetActive(false);
            }
            else
            {
                _shapeImage.sprite = _table.ResolveShapeSprite(entry.Shape);
                _shapeImage.color = entry.Tint;

                if (_label != null)
                {
                    _label.gameObject.SetActive(true);
                    _label.text = entry.Label;
                    _label.color = entry.LabelColor;
                }
            }

            if (_suffix == null) return;

            _suffix.gameObject.SetActive(entry.Hold);
            _suffix.text = _holdSuffix;
        }
    }
}
