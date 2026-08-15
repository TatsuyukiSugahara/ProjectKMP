using System.Collections.Generic;
using NUnit.Framework;
using ProjectKMP.Battle;

namespace ProjectKMP.Tests
{
    /// <summary>
    /// その日の記録のテスト。
    ///
    /// この処理は端末へ書き込むので、前のテストの結果が次へ残る。
    /// テストは順番に関係なく通らなければならないので、毎回まっさらから始める。
    /// </summary>
    public class BestTimeBoardTests
    {
        [SetUp]
        public void 前の記録を消す()
        {
            BestTimeBoard.Clear();
        }

        [TearDown]
        public void 後始末する()
        {
            // 遊んでいる端末に、テストで入れた記録を残さない
            BestTimeBoard.Clear();
        }

        [Test]
        public void 最初は空っぽ()
        {
            Assert.AreEqual(0, BestTimeBoard.Load().Count);
        }

        [Test]
        public void 入れた記録が読み出せる()
        {
            BestTimeBoard.Submit("ポチ", 39.59);

            List<BestTimeBoard.Entry> entries = BestTimeBoard.Load();

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("ポチ", entries[0].Name);
            Assert.AreEqual(39.59, entries[0].Seconds, 0.01);
        }

        [Test]
        public void 速い順に並ぶ()
        {
            BestTimeBoard.Submit("おそい", 90.0);
            BestTimeBoard.Submit("はやい", 30.0);
            BestTimeBoard.Submit("ふつう", 60.0);

            List<BestTimeBoard.Entry> entries = BestTimeBoard.Load();

            Assert.AreEqual("はやい", entries[0].Name);
            Assert.AreEqual("ふつう", entries[1].Name);
            Assert.AreEqual("おそい", entries[2].Name);
        }

        [Test]
        public void 上位だけ残って残りは捨てられる()
        {
            // 上限より多く入れる。展示では何十回も遊ばれるので、必ず起きる状況
            for (int i = 0; i < BestTimeBoard.MAX_ENTRIES + 3; i++) BestTimeBoard.Submit("いぬ" + i, 10.0 + i);

            List<BestTimeBoard.Entry> entries = BestTimeBoard.Load();

            Assert.AreEqual(BestTimeBoard.MAX_ENTRIES, entries.Count);

            // 捨てられるのは遅いほう。一番速い記録は必ず残る
            Assert.AreEqual(10.0, entries[0].Seconds, 0.01);
        }

        [Test]
        public void 一番になれば1位が返る()
        {
            BestTimeBoard.Submit("せんぱい", 50.0);

            Assert.AreEqual(1, BestTimeBoard.Submit("こうはい", 20.0));
        }

        [Test]
        public void 遅ければ下の順位が返る()
        {
            BestTimeBoard.Submit("はやい", 20.0);

            Assert.AreEqual(2, BestTimeBoard.Submit("おそい", 50.0));
        }

        [Test]
        public void 上位に入らなければゼロが返る()
        {
            for (int i = 0; i < BestTimeBoard.MAX_ENTRIES; i++) BestTimeBoard.Submit("いぬ" + i, 10.0 + i);

            // 既に埋まっている記録より遅いので、どこにも入らない
            Assert.AreEqual(0, BestTimeBoard.Submit("おそすぎ", 999.0));
        }

        [Test]
        public void 名前が空なら仮の名前になる()
        {
            BestTimeBoard.Submit(string.Empty, 30.0);

            Assert.AreEqual("ななしさん", BestTimeBoard.Load()[0].Name);
        }

        [Test]
        public void 区切りに使う文字は名前から取り除かれる()
        {
            // 名前に区切り文字が混ざると、読み出すときに壊れてしまう
            BestTimeBoard.Submit("ポ,チ|タロー", 30.0);

            Assert.AreEqual("ポチタロー", BestTimeBoard.Load()[0].Name);
        }

        [Test]
        public void 記録にならない時間は受け付けない()
        {
            Assert.AreEqual(0, BestTimeBoard.Submit("ポチ", 0.0));
            Assert.AreEqual(0, BestTimeBoard.Submit("ポチ", -5.0));

            Assert.AreEqual(0, BestTimeBoard.Load().Count);
        }
    }
}
