using ProjectKMP.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.Result
{
    /// <summary>
    /// リザルトの背景。ボスを倒した瞬間のスクリーンショット(GameClearSnapshot)を画面いっぱいに表示する。
    /// スクリーンショットが無いとき(リザルトを直接再生した場合など)は暗い単色にする。
    /// </summary>
    public class ResultBackground : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("背景を表示する RawImage")]
        private RawImage _image;

        [SerializeField, Tooltip("スクリーンショットが無いときの背景色")]
        private Color _fallbackColor = new Color(0.13f, 0.16f, 0.20f, 1.0f);

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            if (_image == null) return;

            if (GameClearSnapshot.Texture != null)
            {
                _image.texture = GameClearSnapshot.Texture;
                _image.color = Color.white;
            }
            else
            {
                _image.texture = null;
                _image.color = _fallbackColor;
            }
        }

        private void OnDestroy()
        {
            // リザルトを抜けたらもう使わないので、テクスチャを破棄してメモリを返す
            if (_image != null) _image.texture = null;
            GameClearSnapshot.Clear();
        }
    }
}
