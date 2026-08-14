using UnityEngine;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 確認のときだけ効かせる切り替え。
    ///
    /// クールタイムを毎回インスペクタで書き換えると、戻し忘れがそのまま展示へ出る。
    /// 実際、必殺技のクールタイムは確認用の1秒のまま長く残っていた。
    ///
    /// ここに集めておけば、切り替えは1か所で済み、
    /// 『いま何を無効にしているか』も一目で分かる。
    ///
    /// ビルドには含めない。切り替えごと消えるので、
    /// 展示用のROMへ確認用の設定が紛れ込むことがない。
    /// </summary>
    public static class DebugFlags
    {
        // ---- 定数 ----------------------------------------

        private const string KEY_NO_COOLDOWN = "ProjectKMP.Debug.NoCooldown";

        // ---- 公開API -------------------------------------

        /// <summary>
        /// クールタイムを無くすか。
        /// エディタで遊んでいるときだけ効く。
        /// </summary>
        public static bool NoCooldown
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(KEY_NO_COOLDOWN, false);
#else
                return false;
#endif
            }

            set
            {
#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetBool(KEY_NO_COOLDOWN, value);
#endif
            }
        }

        /// <summary>
        /// クールタイムの秒数を通す。無効にしていれば0を返す。
        /// 各スキルはこれを通してから待ち時間を決める。
        /// </summary>
        public static float ApplyCooldown(float seconds)
        {
            return NoCooldown ? 0.0f : seconds;
        }
    }
}
