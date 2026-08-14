using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 画面で使う簡単な形の絵を、その場で描いて配る。
    ///
    /// 丸や角の丸い四角のためだけに画像を用意すると、
    /// 置き場所と名前を決めるところから話が始まってしまう。
    /// 一度描いたら全員で使い回すので、数が増えても作り直しは起きない。
    /// </summary>
    public static class UiShapeSprites
    {
        private static Sprite _circle;
        private static Sprite _roundedBox;

        /// <summary>ふちを丸めた円</summary>
        public static Sprite Circle()
        {
            if (_circle != null) return _circle;

            const int SIZE = 128;
            var texture = NewTexture(SIZE);

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float dx = x + 0.5f - SIZE * 0.5f;
                    float dy = y + 0.5f - SIZE * 0.5f;

                    // ふちを1画素ぶんぼかす。そのままだと縁がぎざぎざに見える
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    texture.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, Mathf.Clamp01(SIZE * 0.5f - 1.0f - distance)));
                }
            }

            texture.Apply();
            _circle = Sprite.Create(texture, new Rect(0.0f, 0.0f, SIZE, SIZE), new Vector2(0.5f, 0.5f));

            return _circle;
        }

        /// <summary>
        /// 角の丸い四角。9分割で貼るので、どんな大きさに伸ばしても角の丸みは崩れない。
        /// 使う側は Image の type を Sliced にする。
        /// </summary>
        public static Sprite RoundedBox()
        {
            if (_roundedBox != null) return _roundedBox;

            const int SIZE = 96;
            const float RADIUS = 28.0f;

            var texture = NewTexture(SIZE);

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;

                    // 角の丸みの中心からどれだけ離れているかで、内か外かを決める
                    float dx = Mathf.Max(RADIUS - px, px - (SIZE - RADIUS), 0.0f);
                    float dy = Mathf.Max(RADIUS - py, py - (SIZE - RADIUS), 0.0f);

                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    texture.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, Mathf.Clamp01(RADIUS - distance)));
                }
            }

            texture.Apply();

            // 真ん中を伸ばし、四隅は伸ばさない
            var border = new Vector4(RADIUS + 2.0f, RADIUS + 2.0f, RADIUS + 2.0f, RADIUS + 2.0f);
            _roundedBox = Sprite.Create(texture, new Rect(0.0f, 0.0f, SIZE, SIZE), new Vector2(0.5f, 0.5f), 100.0f, 0, SpriteMeshType.FullRect, border);

            return _roundedBox;
        }

        private static Texture2D NewTexture(int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        }
    }
}
