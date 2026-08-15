using UnityEngine;

namespace ProjectKMP.Presentation
{
    /// <summary>
    /// 締めの演出で使う絵を作る。
    ///
    /// 画像を持たずにコードで描く。大きさを変えても粗くならず、
    /// 形を直すのに絵の作り直しが要らない。
    ///
    /// どの絵にも太い輪郭線を入れる。線が無いと平たく見え、
    /// 漫画やアニメの締め絵にならない。
    /// </summary>
    internal static class FinishCutinArt
    {
        // ---- 定数 ----------------------------------------

        /// <summary>輪郭線の色。真っ黒より、少し緑を含んだほうが題字と揃う</summary>
        private static readonly Color OUTLINE = new Color(0.07f, 0.13f, 0.09f, 1.0f);

        // ---- 内部状態 ------------------------------------

        private static Sprite _fang;
        private static Sprite _bite;
        private static Sprite _speedLines;
        private static Sprite _band;

        // ---- 牙 ------------------------------------------

        /// <summary>攻撃ボタンで使っている牙の絵の置き場</summary>
        private const string FANG_PATH = "SPR_UI_BiteButton_Fangs";

        /// <summary>
        /// 並んだ牙。攻撃ボタンと同じ絵を使う。
        ///
        /// 締めだけ別の形にすると、遊んでいる間ずっと見ていた牙と繋がらない。
        /// 同じ絵を大きく使うことで、あのボタンの牙が画面いっぱいに来た、と分かる。
        /// </summary>
        public static Sprite Fang()
        {
            if (_fang != null) return _fang;

            _fang = Resources.Load<Sprite>(FANG_PATH);

            return _fang;
        }

        // ---- 噛み跡 --------------------------------------

        /// <summary>上下から牙が食い込んだ跡。輪郭を付けて判子のように見せる</summary>
        public static Sprite BiteMark()
        {
            if (_bite != null) return _bite;

            const int SIZE = 512;
            const int TEETH = 4;

            var inside = new bool[SIZE * SIZE];

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float u = x / (float)SIZE;
                    float v = y / (float)SIZE;

                    bool hit = false;

                    for (int row = 0; row < 2 && !hit; row++)
                    {
                        bool upper = row == 0;
                        float jaw = upper ? 0.62f : 0.38f;

                        float arc = 1.0f - Mathf.Abs(u - 0.5f) * 1.1f;
                        if (arc <= 0.0f) continue;

                        float tooth = u * TEETH;
                        int index = Mathf.FloorToInt(tooth);
                        float within = Mathf.Abs(tooth - index - 0.5f) * 2.0f;

                        float variation = 0.8f + Mathf.Abs(Mathf.Sin(index * 3.3f + row)) * 0.4f;
                        float depth = 0.20f * arc * variation * (1.0f - within * within * within);

                        hit = upper ? (v < jaw && v > jaw - depth) : (v > jaw && v < jaw + depth);
                    }

                    inside[y * SIZE + x] = hit;
                }
            }

            _bite = Build(SIZE, SIZE, inside, 7.0f, index => new Color(0.72f, 0.10f, 0.12f, 1.0f));

            return _bite;
        }

        // ---- 題字の下じき ---------------------------------

        /// <summary>
        /// 題字を載せる帯。両端が斜めに切り落とされた形。
        ///
        /// 対戦ものの決着画面でよく使われる形で、
        /// 傾けて重ねると速さと勢いが出る。
        /// 爆発の形は可愛くなりすぎて、決着の重さが消えてしまう。
        /// </summary>
        public static Sprite Band()
        {
            if (_band != null) return _band;

            const int WIDTH = 1024;
            const int HEIGHT = 256;

            // 端の斜めの深さ。大きいほど鋭く尖る
            const float SKEW = 0.14f;

            var inside = new bool[WIDTH * HEIGHT];

            for (int y = 0; y < HEIGHT; y++)
            {
                for (int x = 0; x < WIDTH; x++)
                {
                    float u = x / (float)WIDTH;
                    float v = y / (float)HEIGHT;

                    // 上下で切り口をずらす。平行四辺形になり、傾いて見える
                    float left = SKEW * v;
                    float right = 1.0f - SKEW * (1.0f - v);

                    inside[y * WIDTH + x] = u > left && u < right;
                }
            }

            _band = Build(WIDTH, HEIGHT, inside, 7.0f, index => Color.white);

            return _band;
        }

        // ---- 集中線 --------------------------------------

        /// <summary>中心から放射状に伸びる線。中心は抜いて、置いた文字が読めるようにする</summary>
        public static Sprite SpeedLines()
        {
            if (_speedLines != null) return _speedLines;

            const int SIZE = 512;
            const int LINES = 64;

            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float u = x / (float)SIZE - 0.5f;
                    float v = y / (float)SIZE - 0.5f;

                    float radius = Mathf.Sqrt(u * u + v * v) * 2.0f;

                    if (radius < 0.38f) { texture.SetPixel(x, y, Color.clear); continue; }

                    float angle = Mathf.Atan2(v, u) / (Mathf.PI * 2.0f) + 0.5f;
                    float line = angle * LINES;
                    int index = Mathf.FloorToInt(line);

                    float thickness = 0.12f + Mathf.Abs(Mathf.Sin(index * 12.9898f)) * 0.26f;
                    float within = Mathf.Abs(line - index - 0.5f) * 2.0f;

                    if (within > thickness) { texture.SetPixel(x, y, Color.clear); continue; }

                    float alpha = Mathf.Clamp01((radius - 0.38f) / 0.45f);
                    alpha *= 1.0f - within / thickness;

                    texture.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, alpha));
                }
            }

            texture.Apply();
            _speedLines = Sprite.Create(texture, new Rect(0.0f, 0.0f, SIZE, SIZE), new Vector2(0.5f, 0.5f));

            return _speedLines;
        }

        // ---- 共通 ----------------------------------------

        /// <summary>
        /// 形の内側を塗り、縁の内側に太い線を入れて絵にする。
        ///
        /// 縁は外へ広げずに内側へ描く。外へ広げると形が太っていき、
        /// 尖らせたはずの先が丸くなってしまう。
        /// </summary>
        private static Sprite Build(
            int width, int height, bool[] inside, float outlineWidth, System.Func<int, Color> fill)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            // 円周を回りながら外を探す。近所を全部見るより軽い
            const int SAMPLES = 16;

            var offsets = new Vector2Int[SAMPLES];
            for (int i = 0; i < SAMPLES; i++)
            {
                float angle = i / (float)SAMPLES * Mathf.PI * 2.0f;

                offsets[i] = new Vector2Int(
                    Mathf.RoundToInt(Mathf.Cos(angle) * outlineWidth),
                    Mathf.RoundToInt(Mathf.Sin(angle) * outlineWidth));
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;

                    if (!inside[index]) { texture.SetPixel(x, y, Color.clear); continue; }

                    bool nearEdge = IsNearEdge(inside, width, height, offsets, x, y);

                    texture.SetPixel(x, y, nearEdge ? OUTLINE : fill(index));
                }
            }

            texture.Apply();

            return Sprite.Create(texture, new Rect(0.0f, 0.0f, width, height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>まわりに外側があるか。あればそこは縁として塗る</summary>
        private static bool IsNearEdge(
            bool[] inside, int width, int height, Vector2Int[] offsets, int x, int y)
        {
            foreach (Vector2Int offset in offsets)
            {
                int sx = x + offset.x;
                int sy = y + offset.y;

                // 絵からはみ出した先も外として扱う
                if (sx < 0 || sx >= width || sy < 0 || sy >= height) return true;

                if (!inside[sy * width + sx]) return true;
            }

            return false;
        }
    }
}
