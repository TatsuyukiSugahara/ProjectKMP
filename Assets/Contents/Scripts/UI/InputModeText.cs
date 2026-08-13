using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 操作機器に合わせて文言を差し替える。
    ///
    /// 『なにかキーをおしてね』はキーボードにしか当てはまらない。
    /// 指で触る人には『タッチしてね』、パッドの人には『ボタンをおしてね』でないと、
    /// 何をすればよいか分からないまま止まってしまう。
    ///
    /// 絵ではなく文字なので、グリフ(InputGlyph)ではなくこちらで扱う。
    /// TextMeshPro と旧 Text のどちらでも使える。
    /// </summary>
    public class InputModeText : MonoBehaviour
    {
        [SerializeField, Tooltip("書き換える文字。未設定なら自分から探す")]
        private TMPro.TMP_Text _tmpText;

        [SerializeField, Tooltip("旧 Text を使っている場合はこちら")]
        private UnityEngine.UI.Text _legacyText;

        [Header("機器ごとの文言")]
        [SerializeField, Tooltip("指で触っているとき")]
        private string _touchText = "";

        [SerializeField, Tooltip("キーボードとマウスのとき")]
        private string _keyboardText = "";

        [SerializeField, Tooltip("ゲームパッドのとき")]
        private string _gamepadText = "";

        private void Awake()
        {
            if (_tmpText == null) _tmpText = GetComponent<TMPro.TMP_Text>();
            if (_legacyText == null) _legacyText = GetComponent<UnityEngine.UI.Text>();
        }

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
            string value = Resolve(mode);

            // 空のままにすると文言が消えてしまうので、指定が無ければ触らない
            if (string.IsNullOrEmpty(value)) return;

            if (_tmpText != null) _tmpText.text = value;
            if (_legacyText != null) _legacyText.text = value;
        }

        private string Resolve(InputMode mode)
        {
            switch (mode)
            {
                case InputMode.Touch: return _touchText;
                case InputMode.KeyboardMouse: return _keyboardText;
                case InputMode.Gamepad: return _gamepadText;
                default: return _keyboardText;
            }
        }
    }
}
