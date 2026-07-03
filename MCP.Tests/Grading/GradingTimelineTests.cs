using System.Linq;
using NUnit.Framework;
using RevitMCP.Core.Grading;

namespace RevitMCP.Tests.Grading
{
    [TestFixture]
    public class GradingTimelineTests
    {
        [Test]
        public void Measure_記錄階段名稱與非負耗時()
        {
            var timeline = new GradingTimeline();
            using (timeline.Measure("測試階段"))
            {
            }

            Assert.That(timeline.Stages, Has.Count.EqualTo(1));
            Assert.That(timeline.Stages[0].Name, Is.EqualTo("測試階段"));
            Assert.That(timeline.Stages[0].ElapsedMilliseconds, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void Record_依呼叫順序保留階段()
        {
            var timeline = new GradingTimeline();
            timeline.Record("甲", 5);
            timeline.Record("乙", 7);

            Assert.That(
                timeline.Stages.Select(stage => stage.Name),
                Is.EqualTo(new[] { "甲", "乙" }));
        }

        [Test]
        public void Record_空白名稱_回報繁體中文錯誤()
        {
            var timeline = new GradingTimeline();
            var error = Assert.Throws<System.ArgumentException>(() => timeline.Record(" ", 1));
            StringAssert.Contains("階段名稱", error.Message);
        }

        [Test]
        public void Record_負值耗時_以零記錄()
        {
            var timeline = new GradingTimeline();
            timeline.Record("階段", -3);

            Assert.That(timeline.Stages[0].ElapsedMilliseconds, Is.EqualTo(0));
        }
    }
}
