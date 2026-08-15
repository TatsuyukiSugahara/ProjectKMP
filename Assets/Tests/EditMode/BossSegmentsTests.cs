using NUnit.Framework;
using ProjectKMP.Battle;

namespace ProjectKMP.Tests
{
    /// <summary>
    /// ボスのHPを何本かに分けて見せる計算のテスト。
    ///
    /// 本数の切り替わりは、演出の合図(ブレイク・最終フェーズ)の起点にもなっている。
    /// ここがずれると、演出が出るタイミングごと狂う。
    /// </summary>
    public class BossSegmentsTests
    {
        [Test]
        public void 満タンなら全部残っている()
        {
            Assert.AreEqual(4, BossSegments.Remaining(1.0f, 4));
        }

        [Test]
        public void 削り切ったらゼロになる()
        {
            Assert.AreEqual(0, BossSegments.Remaining(0.0f, 4));
        }

        [Test]
        public void 半分なら半分の本数が残る()
        {
            Assert.AreEqual(2, BossSegments.Remaining(0.5f, 4));
        }

        [Test]
        public void わずかでも残っていれば最後の1本として数える()
        {
            // 1本ぶんを削り切る手前は、まだその本が残っている扱いにする。
            // ここを切り上げないと、最後の一撃の前に撃破の演出が出てしまう
            Assert.AreEqual(1, BossSegments.Remaining(0.001f, 4));
        }

        [Test]
        public void 切れ目のちょうど上は次の本へ進んでいない()
        {
            // 4本のうち3本目を削り切った瞬間。まだ3本目が残っている扱いにする
            Assert.AreEqual(3, BossSegments.Remaining(0.75f, 4));
        }

        [Test]
        public void 本数が1なら常に1本として扱う()
        {
            Assert.AreEqual(1, BossSegments.Remaining(0.3f, 1));
            Assert.AreEqual(0, BossSegments.Remaining(0.0f, 1));
        }

        [Test]
        public void 満タンの見た目は満タンになる()
        {
            Assert.AreEqual(1.0f, BossSegments.Ratio(1.0f, 4), 0.0001f);
        }

        [Test]
        public void 切れ目をまたぐと見た目が満タンへ戻る()
        {
            // 3本目を削り切った瞬間は、次の本が満タンで現れる
            Assert.AreEqual(1.0f, BossSegments.Ratio(0.75f, 4), 0.0001f);
        }

        [Test]
        public void 本の途中は割合で表される()
        {
            // 全体の 0.625 は、4本中3本目の半分まで削った状態
            Assert.AreEqual(0.5f, BossSegments.Ratio(0.625f, 4), 0.0001f);
        }

        [Test]
        public void 削り切ったら見た目もゼロになる()
        {
            Assert.AreEqual(0.0f, BossSegments.Ratio(0.0f, 4), 0.0001f);
        }
    }
}
