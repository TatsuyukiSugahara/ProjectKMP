using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>
    /// ボタンの音を、名前による自動判定ではなく明示的に決めたいときに付ける。
    /// 付いていれば UiSoundPlayer の名前による振り分けより優先される。
    /// </summary>
    [DisallowMultipleComponent]
    public class UiButtonSoundKind : MonoBehaviour
    {
        [SerializeField, Tooltip("このボタンを押したときに鳴らす音")]
        private UiSoundPlayer.SoundKind _kind = UiSoundPlayer.SoundKind.Decide;

        /// <summary>指定された音の種類</summary>
        public UiSoundPlayer.SoundKind Kind => _kind;
    }
}
