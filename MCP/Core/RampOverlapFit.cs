using System;
using System.Collections.Generic;
using System.Linq;
namespace RevitMCP.Core {
 public static class RampOverlapFit {
  public static Tuple<double,double> Fit(IList<Tuple<double,double>> samples,double tolerance){
   if(samples.Count<6)throw new ArgumentException("Too few overlap samples.");
   double t=samples.Average(s=>s.Item1),z=samples.Average(s=>s.Item2),den=samples.Sum(s=>(s.Item1-t)*(s.Item1-t));
   if(den<1e-8||samples.Min(s=>s.Item1)>0.001||samples.Max(s=>s.Item1)<0.999)throw new ArgumentException("Overlap must cover both longitudinal ends.");
   double slope=samples.Sum(s=>(s.Item1-t)*(s.Item2-z))/den,start=z-slope*t;
   if(samples.Any(s=>Math.Abs(s.Item2-start-slope*s.Item1)>tolerance))throw new ArgumentException("Overlap is not a single longitudinal plane within tolerance.");
   return Tuple.Create(start,start+slope);
  }
 }
}
