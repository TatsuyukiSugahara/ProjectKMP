using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 指で触るときだけ出す表示。移動スティックやスワイプ領域に付ける。
    ///
    /// キーボードやパッドで遊んでいる人には、画面のスティックは押す場所ではなく
    /// ただ視界を塞ぐだけになるので消す。
    ///
    /// 機器が切り替わったら出し入れも切り替わる。
    /// パッドを挿したPCでは、触った機器によって行き来する。
    /// </summary>
    public class TouchOnlyVisual : MonoBehaviour
    {
        [SerializeField, Tooltip("出し入れするまとまり。未設定なら自分自身")]
        private GameObject _target;

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
            GameObject target = _target != null ? _target : gameObject;

            // 自分自身を消すと切り替えを受け取れなくなるので、そのときは見た目だけ畳む
            if (target == gameObject)
            {
                var group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();

                bool touch = mode == InputMode.Touch;
                group.alpha = touch ? 1.0f : 0.0f;
                group.blocksRaycasts = touch;
                group.interactable = touch;
                return;
            }

            target.SetActive(mode == InputMode.Touch);
        }
    }
}
