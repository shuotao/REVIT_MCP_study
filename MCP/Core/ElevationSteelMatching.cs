using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Core
{
    // Coordinates here are millimeters along the center grid and vertical Z.
    internal static class ElevationSteelMatching
    {
        internal static int Select(double start, double end, double top, IReadOnlyList<(double Station, double Z)> labels, double maxDistance, double ambiguityGap = 100)
        {
            double lo = Math.Min(start, end), hi = Math.Max(start, end);
            var ranked = labels.Select((p, i) => new { p, i, Distance = Math.Abs(p.Z - top) })
                .Where(x => x.p.Station >= lo && x.p.Station <= hi).OrderBy(x => x.Distance).ToList();
            if (ranked.Count == 0 || ranked[0].Distance > maxDistance) return -1;
            if (ranked.Count > 1 && ranked[1].Distance - ranked[0].Distance < ambiguityGap) return -2;
            return ranked[0].i;
        }
    }
}
