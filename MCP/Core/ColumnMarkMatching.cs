using System;
using System.Collections.Generic;
using System.Linq;
namespace RevitMCP.Core {
    public static class ColumnMarkMatching {
        public static int Select(IReadOnlyList<double> distancesMm, double maximum, double ambiguityGap) {
            var ranked = distancesMm.Select((distance,index)=>new {distance,index}).OrderBy(x=>x.distance).ToList();
            if (ranked.Count==0 || ranked[0].distance>maximum) return -1;
            if (ranked.Count>1 && ranked[1].distance-ranked[0].distance<ambiguityGap) return -2;
            return ranked[0].index;
        }
        public static HashSet<int> ReusedLabels(IEnumerable<int> labels) =>
            new HashSet<int>(labels.Where(i=>i>=0).GroupBy(i=>i).Where(g=>g.Count()>1).Select(g=>g.Key));
    }
}
