using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RevitMCP.Core.Grading
{
    public struct Point2D
    {
        public Point2D(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }
    }

    public sealed class GradingRequest
    {
        public long ToposolidId { get; set; }
        public IReadOnlyList<long> FloorIds { get; set; }
        public string Mode { get; set; }
        public string TargetFace { get; set; }
        public bool AllowPhaseSetup { get; set; }
        public bool UpdateExisting { get; set; }

        public void Validate()
        {
            if (ToposolidId <= 0) throw new ArgumentException("地形 ID 必須大於 0。");
            if (FloorIds == null || FloorIds.Count == 0) throw new ArgumentException("至少一片樓板才能執行整地。");
            if (FloorIds.Any(id => id <= 0)) throw new ArgumentException("樓板 ID 必須大於 0。");
            if (!string.Equals(Mode, "footprint_only", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("本次試跑僅支援 footprint_only。");
            if (!string.Equals(TargetFace, "bottom", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("本次試跑僅支援樓板底面 bottom。");
            if (UpdateExisting)
                throw new ArgumentException("本次試跑尚未支援 updateExisting=true。");
        }
    }

    public sealed class FloorFootprint
    {
        public long FloorId { get; set; }
        public IReadOnlyList<Point2D> OuterLoop { get; set; }
        public Func<double, double, double> BottomElevationAt { get; set; }
    }

    /// <summary>整地執行的單一階段耗時。</summary>
    public sealed class GradingStage
    {
        public GradingStage(string name, long elapsedMilliseconds)
        {
            Name = name;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public string Name { get; }
        public long ElapsedMilliseconds { get; }
    }

    /// <summary>整地執行的分段計時，供效能記錄與逐步優化使用。</summary>
    public sealed class GradingTimeline
    {
        private readonly List<GradingStage> _stages = new List<GradingStage>();

        public IReadOnlyList<GradingStage> Stages => _stages;

        public IDisposable Measure(string name)
        {
            return new StageScope(this, name);
        }

        public void Record(string name, long elapsedMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("階段名稱不可空白。", nameof(name));
            }

            _stages.Add(new GradingStage(name, elapsedMilliseconds < 0 ? 0 : elapsedMilliseconds));
        }

        private sealed class StageScope : IDisposable
        {
            private readonly GradingTimeline _timeline;
            private readonly string _name;
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private bool _disposed;

            public StageScope(GradingTimeline timeline, string name)
            {
                _timeline = timeline;
                _name = name;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _timeline.Record(_name, _stopwatch.ElapsedMilliseconds);
            }
        }
    }

    public sealed class GradingResult
    {
        public long OriginalToposolidId { get; set; }
        public long DesignToposolidId { get; set; }
        public IReadOnlyList<long> FloorIds { get; set; }
        public double CutCubicMeters { get; set; }
        public double FillCubicMeters { get; set; }
        public double NetCubicMeters => FillCubicMeters - CutCubicMeters;
        public int ModifiedPointCount { get; set; }
        public string AssociationId { get; set; }
        public IReadOnlyList<string> Warnings { get; set; }
    }
}
