using UnityEngine;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// ボスを倒した瞬間のスクリーンショットをシーンをまたいで持ち運ぶ入れ物。
    /// インゲーム(GameClearDirector)が撮って入れ、リザルトの背景(ResultBackground)が表示する。
    /// 使い終わったら Clear() でテクスチャを破棄してメモリを返す。
    /// </summary>
    public static class GameClearSnapshot
    {
        /// <summary>撃破の瞬間のスクリーンショット。無ければ null</summary>
        public static Texture2D Texture { get; private set; }

        /// <summary>スクリーンショットを差し替える。前のものは破棄する</summary>
        public static void Set(Texture2D texture)
        {
            Clear();
            Texture = texture;
        }

        /// <summary>テクスチャを破棄して空にする</summary>
        public static void Clear()
        {
            if (Texture != null) Object.Destroy(Texture);
            Texture = null;
        }
    }
}
