using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Core
{
    // Units are arbitrary but must be consistent. Used with millimetres by the Revit adapter.
    internal static class WallTopExtremes
    {
        internal sealed class Segment
        {
            public double U0, U1, Z0, Z1;
            public long Source;
            public double Z(double u) => Z0 + (Z1-Z0)*(u-U0)/(U1-U0);
        }
        internal sealed class Point { public double U, Z; public long Source; }
        internal sealed class Result
        {
            public Point Low, High;
            public double At(double u) => Math.Abs(High.Z-Low.Z)<1e-8 ? Low.Z : Low.Z+(High.Z-Low.Z)*(u-Low.U)/(High.U-Low.U);
        }
        internal static Result Solve(IList<Segment> segments, bool bottom = false)
        {
            if(segments.Count==0 || segments.Any(s=>!Finite(s.U0)||!Finite(s.U1)||!Finite(s.Z0)||!Finite(s.Z1)||s.U1-s.U0<1e-6))
                throw new ArgumentException("No valid beam top segments.");
            var stations=segments.SelectMany(s=>new[]{s.U0,s.U1}).ToList();
            for(int i=0;i<segments.Count;i++)for(int j=i+1;j<segments.Count;j++)
            {
                var a=segments[i];var b=segments[j];double lo=Math.Max(a.U0,b.U0),hi=Math.Min(a.U1,b.U1);
                if(hi-lo<1e-6)continue;
                double f0=a.Z(lo)-b.Z(lo),f1=a.Z(hi)-b.Z(hi);
                if(f0*f1<0)stations.Add(lo+(hi-lo)*f0/(f0-f1));
            }
            stations=stations.Distinct().OrderBy(u=>u).ToList();
            stations.AddRange(stations.Zip(stations.Skip(1),(a,b)=>(a+b)/2).ToArray());
            var points=new List<Point>();
            foreach(double u in stations)
            {
                var covering=segments.Where(s=>u>=s.U0-1e-7&&u<=s.U1+1e-7).OrderBy(s=>bottom?s.Z(u):-s.Z(u)).ThenBy(s=>s.Source).ToList();
                if(covering.Count>0)points.Add(new Point{U=u,Z=covering[0].Z(u),Source=covering[0].Source});
            }
            Func<double,Point> pick=z=>{
                var tied=points.Where(p=>Math.Abs(p.Z-z)<1e-6).ToList();double mid=(tied.Min(p=>p.U)+tied.Max(p=>p.U))/2;
                return tied.OrderBy(p=>Math.Abs(p.U-mid)).ThenBy(p=>p.U).First();
            };
            var result=new Result{Low=pick(points.Min(p=>p.Z)),High=pick(points.Max(p=>p.Z))};
            if(Math.Abs(result.High.Z-result.Low.Z)>1e-8&&Math.Abs(result.High.U-result.Low.U)<1)
                throw new ArgumentException("Highest and lowest points are less than 1 mm apart along the wall.");
            return result;
        }
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
    }
}
