using NUnit.Framework;
using ProjectKMP.Battle;

namespace ProjectKMP.Tests
{
    /// <summary>
    /// クリアタイムの見せ方のテスト。
    /// 記録の並びやリザルトの表示に使うので、桁の欠けは直接目に付く。
    /// </summary>
    public class ClearTimeTextTests
    {
        [Test]
        public void 秒だけなら分はゼロになる()
        {
            Assert.AreEqual("0:39.59", ClearTimeText.Format(39.59));
        }

        [Test]
        public void 分をまたぐと繰り上がる()
        {
            Assert.AreEqual("1:23.45", ClearTimeText.Format(83.45));
        }

        [Test]
        public void 一桁の秒は前をゼロで埋める()
        {
            // 1:5.00 のように詰まると、桁が揃わず読み違えられる
            Assert.AreEqual("1:05.00", ClearTimeText.Format(65.0));
        }

        [Test]
        public void 記録が無ければダッシュで表す()
        {
            Assert.AreEqual(ClearTimeText.EMPTY, ClearTimeText.Format(-1.0));
        }

        [Test]
        public void ちょうどゼロ秒も表せる()
        {
            Assert.AreEqual("0:00.00", ClearTimeText.Format(0.0));
        }
    }
}
