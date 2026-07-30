using R3;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// プレイヤーが操作できるかどうかを1か所で持つ。カットシーン中は false になり、
    /// 移動・攻撃・射撃の各スクリプトがここを見て入力を捨てる。
    /// 入力を読む側を1行変えるだけで済むので、操作を止めたい場面が増えても散らからない。
    /// </summary>
    public static class BattlePlayGate
    {
        // ---- 内部状態 ------------------------------------

        private static readonly ReactiveProperty<bool> PLAYABLE = new ReactiveProperty<bool>(true);

        // ---- 公開API -------------------------------------

        /// <summary>いま操作できるか</summary>
        public static bool IsPlayable => PLAYABLE.Value;

        /// <summary>操作できるかどうかの変化。UIの出し入れに使える</summary>
        public static Observable<bool> OnChanged => PLAYABLE;

        /// <summary>操作できるかどうかを切り替える。カットシーンの開始・終了で呼ぶ</summary>
        public static void SetPlayable(bool value)
        {
            PLAYABLE.Value = value;
        }
    }
}
