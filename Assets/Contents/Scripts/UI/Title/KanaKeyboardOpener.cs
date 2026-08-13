using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 入力欄で決定されたら、五十音パネルを開く。
    ///
    /// パッドでは文字を打てないので、入力欄を選んで決定した時点でパネルへ移る。
    /// 名前入力の画面に来ただけで全面が覆われると、他のボタンが押せず戸惑うため、
    /// 開くのは『入力欄を選んで決定した』ときだけにしている。
    ///
    /// 入力欄そのものにも決定は届くので、パッドのときは打ち込みを始めさせない。
    /// カーソルだけ出て何も入らない状態になり、直感に反するため。
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public class KanaKeyboardOpener : MonoBehaviour, ISubmitHandler
    {
        [SerializeField, Tooltip("開くパネル。未設定なら親から探す")]
        private KanaKeyboard _keyboard;

        private TMP_InputField _field;

        private void Awake()
        {
            _field = GetComponent<TMP_InputField>();
            if (_keyboard == null) _keyboard = GetComponentInParent<KanaKeyboard>();
        }

        /// <summary>決定(Aボタン・Enter)が届いたとき</summary>
        public void OnSubmit(BaseEventData eventData)
        {
            if (InputModeTracker.Current != InputMode.Gamepad) return;
            if (_keyboard == null) return;

            // 入力欄側の打ち込みは始めさせない。カーソルだけ出ても何も入らない
            if (_field != null) _field.DeactivateInputField();

            _keyboard.Open();
        }
    }
}
