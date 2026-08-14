using TMPro;
using UnityEngine;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 当たった場所に『ガブッ！』のような擬音を弾けさせる。
    ///
    /// 犬が主役のゲームなので、数字よりも擬音のほうが手応えが伝わる。
    /// 漫画のコマのように、大きく出して素早く引く。
    ///
    /// 文字は世界の中に置いてカメラの方を向かせる。
    /// 画面に貼り付けると、どこで起きたことなのか分からなくなるため。
    ///
    /// 出したら自分で消えるので、呼ぶ側は後始末を気にしなくてよい。
    /// </summary>
    public class Onomatopoeia : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>飛び出してから縮んで収まるまでの割合</summary>
        private const float POP_RATIO = 0.18f;

        /// <summary>飛び出す瞬間の大きさの倍率</summary>
        private const float POP_SCALE = 1.55f;

        /// <summary>消え始める割合。それまでは濃いまま見せる</summary>
        private const float FADE_START = 0.55f;

        // ---- 内部状態 ------------------------------------

        /// <summary>一度見つけた日本語フォント。作るたびに探し直さないための控え</summary>
        private static TMP_FontAsset _sharedFont;

        private TMP_Text _text;
        private float _elapsed;
        private float _duration = 0.6f;
        private float _baseScale = 1.0f;
        private Vector3 _riseDirection = Vector3.up;
        private float _riseDistance = 1.2f;
        private Vector3 _startPosition;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 擬音を1つ出す。
        /// scale は文字の大きさ。強い技ほど大きくすると、技の格の差が伝わる。
        /// </summary>
        public static void Play(Vector3 position, string label, Color color, float scale = 1.0f, float durationSec = 0.6f)
        {
            if (string.IsNullOrEmpty(label)) return;

            var go = new GameObject("Onomatopoeia");

            // 同じ場所に重ならないよう、少しだけ散らす
            Vector3 jitter = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.2f, 0.4f), Random.Range(-0.5f, 0.5f));
            go.transform.position = position + jitter;

            var popup = go.AddComponent<Onomatopoeia>();
            popup._baseScale = Mathf.Max(0.1f, scale);
            popup._duration = Mathf.Max(0.1f, durationSec);
            popup._startPosition = go.transform.position;

            // 上へ真っすぐだと単調なので、少し斜めへ逃がす
            popup._riseDirection = (Vector3.up + new Vector3(jitter.x, 0.0f, jitter.z) * 0.6f).normalized;

            popup.Setup(label, color);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>
        /// 日本語が出せるフォントを探す。
        ///
        /// 実行時に作る文字なので、手で割り当てる先が無い。
        /// プロジェクトの既定を先に見て、無ければ画面に出ている文字から借りる。
        /// 一度見つけたら覚えておき、毎回探し回らない。
        /// </summary>
        private static TMP_FontAsset ResolveJapaneseFont()
        {
            if (_sharedFont != null) return _sharedFont;

            // 画面に出ている文字を片っ端から見て、日本語を持っているものを選ぶ。
            // 『最初に見つかったもの』では英字だけのフォントを掴んで化ける
            TMP_FontAsset firstFound = null;

            foreach (TMP_Text text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (text == null || text.font == null) continue;

                if (firstFound == null) firstFound = text.font;
                if (!HasJapanese(text.font)) continue;

                _sharedFont = text.font;
                return _sharedFont;
            }

            // どれも確かめられなければ、最初に見つけたものを使う。
            // 既定のフォントは英字だけのことが多く、そちらへ落とすと必ず化ける
            _sharedFont = firstFound != null ? firstFound : TMP_Settings.defaultFontAsset;
            return _sharedFont;
        }

        /// <summary>
        /// 擬音に使う文字を出せるフォントか。
        ///
        /// 引数を付けずに調べると『いま焼き込まれている文字』しか見ないため、
        /// 日本語を出せるフォントでも、まだ使っていない文字は無いと判定されてしまう。
        /// 予備のフォントもたどり、必要なら焼き込ませたうえで確かめる。
        /// </summary>
        private static bool HasJapanese(TMP_FontAsset font)
        {
            return font.HasCharacter('ガ', true, true)
                && font.HasCharacter('ッ', true, true)
                && font.HasCharacter('ー', true, true);
        }

        private void Setup(string label, Color color)
        {
            var textObject = new GameObject("Label");
            textObject.transform.SetParent(transform, false);

            _text = textObject.AddComponent<TextMeshPro>();

            // 何も指定しないと英字だけのフォントが割り当てられ、日本語が化ける
            TMP_FontAsset font = ResolveJapaneseFont();
            if (font != null) _text.font = font;

            _text.text = label;
            _text.fontSize = 8.0f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.color = color;
            _text.fontStyle = FontStyles.Bold | FontStyles.Italic;

            // 太い縁を付ける。背景が明るくても暗くても読めるようにする
            _text.outlineWidth = 0.28f;
            _text.outlineColor = new Color32(30, 20, 10, 255);

            Apply(0.0f);
        }

        private void Update()
        {
            // ヒットストップ中に出すので、止まった時間ではなく実時間で進める
            _elapsed += Time.unscaledDeltaTime;

            float t = _elapsed / _duration;
            if (t >= 1.0f) { Destroy(gameObject); return; }

            Apply(t);
        }

        private void Apply(float t)
        {
            // 飛び出して、行きすぎてから収まる。まっすぐ大きくすると弾けた感じが出ない
            float scale = t < POP_RATIO
                ? Mathf.Lerp(0.2f, POP_SCALE, t / POP_RATIO)
                : Mathf.Lerp(POP_SCALE, 1.0f, (t - POP_RATIO) / (1.0f - POP_RATIO));

            transform.localScale = Vector3.one * (_baseScale * scale);

            // 最初は速く、だんだん緩やかに昇る
            float rise = 1.0f - (1.0f - t) * (1.0f - t);
            transform.position = _startPosition + _riseDirection * (_riseDistance * rise);

            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;

            if (_text == null) return;

            float alpha = t < FADE_START ? 1.0f : 1.0f - (t - FADE_START) / (1.0f - FADE_START);
            Color color = _text.color;
            _text.color = new Color(color.r, color.g, color.b, alpha);
        }
    }
}
