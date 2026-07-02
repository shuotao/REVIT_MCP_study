using System.Collections.Generic;
using NUnit.Framework;
using RevitMCP.Core.Grading;

namespace RevitMCP.Tests.Grading
{
    [TestFixture]
    public class Polygon2DTests
    {
        private const double Tolerance = 0.001;

        [Test]
        public void Contains_點位於多邊形內部_回傳真()
        {
            Assert.That(Polygon2D.Contains(Rect(0, 0, 10, 10), new Point2D(5, 5), Tolerance), Is.True);
        }

        [Test]
        public void Contains_點位於多邊形外部_回傳假()
        {
            Assert.That(Polygon2D.Contains(Rect(0, 0, 10, 10), new Point2D(15, 5), Tolerance), Is.False);
        }

        [Test]
        public void Contains_點位於多邊形邊界_回傳真()
        {
            Assert.That(Polygon2D.Contains(Rect(0, 0, 10, 10), new Point2D(10, 5), Tolerance), Is.True);
        }

        [Test]
        public void Overlaps_兩個矩形有實際面積交集_回傳真()
        {
            var a = Rect(0, 0, 10, 10);
            var b = Rect(5, 5, 15, 15);

            Assert.That(Polygon2D.Overlaps(a, b, Tolerance), Is.True);
        }

        [Test]
        public void Overlaps_兩個矩形互不相交_回傳假()
        {
            var a = Rect(0, 0, 10, 10);
            var b = Rect(20, 0, 30, 10);

            Assert.That(Polygon2D.Overlaps(a, b, Tolerance), Is.False);
        }

        [Test]
        public void Overlaps_矩形只有共邊_回傳假()
        {
            var a = Rect(0, 0, 10, 10);
            var b = Rect(10, 0, 20, 10);

            Assert.That(Polygon2D.Overlaps(a, b, Tolerance), Is.False);
        }

        private static IReadOnlyList<Point2D> Rect(double minX, double minY, double maxX, double maxY)
        {
            return new[]
            {
                new Point2D(minX, minY),
                new Point2D(maxX, minY),
                new Point2D(maxX, maxY),
                new Point2D(minX, maxY)
            };
        }
    }
}
