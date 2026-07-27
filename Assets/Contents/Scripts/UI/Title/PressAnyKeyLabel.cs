using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectKMP.UI.Title
{
    /// <summary>
    /// 入力デバイスに応じて「PRESS ANY KEY」と「TAP TO START」を出し分けるラベル。
    /// </summary>
    public class PressAnyKeyLabel : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("文言を書き換える対象。未設定なら同じオブジェクトの TMP_Text を使う")]
        private TMP_Text _text;

        [SerializeField, Tooltip("キーボード・コントローラー環境で表示する文言")]
        private string _keyboardMessage = "PRESS ANY KEY";

        [SerializeField, Tooltip("タッチ環境で表示する文言")]
        private string _touchMessage = "TAP TO START";

        // ---- 公開API -------------------------------------

        /// <summary>いまの環境に合う文言を反映する</summary>
        public void Apply()
        {
            if (_text == null) _text = GetComponent<TMP_Text>();
            if (_text == null) return;

            _text.text = IsTouchDevice() ? _touchMessage : _keyboardMessage;
        }

        // ---- Unityイベント -------------------------------

        private void Reset()
        {
            _text = GetComponent<TMP_Text>();
        }

        private void Start()
        {
            Apply();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>タッチ操作が主になる環境かどうか</summary>
        private static bool IsTouchDevice()
        {
            if (Application.isMobilePlatform) return true;

            // タッチパネル付きPCではキーボードも使えるため、キーボードが無いときだけタッチ扱いにする
            return Touchscreen.current != null && Keyboard.current == null;
        }
    }
}
