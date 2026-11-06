using System;
using System.Collections.Generic;
using System.Linq;
namespace RevitMCP.Core {
 public static class CadLiningPlacement {
  public class Reference { public long Id; public CadWallGeometry.Segment Axis; public double Width; }
  public class Result { public CadWallGeometry.Run Run; public int Side; public double InteriorPlane; public string Reason; public List<long> ReferenceIds=new List<long>(); }
  public static Result Resolve(CadWallGeometry.Run cad,double actualWidth,IEnumerable<Reference> references,double maxDistance,double tolerance) {
   var result=new Result();
   if(actualWidth<cad.Width-0.1){result.Reason="LiningNarrowerThanCad";return result;}
   var candidates=new List<Tuple<Reference,int,double,double>>();
   foreach(var reference in references){var s=reference.Axis;if(s.Length<1)continue;
    double u=(s.X2-s.X1)/s.Length,v=(s.Y2-s.Y1)/s.Length;if(Math.Abs(cad.U*v-cad.V*u)>0.001)continue;
    double a=cad.U*s.X1+cad.V*s.Y1,b=cad.U*s.X2+cad.V*s.Y2,n=-cad.V*s.X1+cad.U*s.Y1,d=n-cad.N;
    double lo=Math.Max(cad.A,Math.Min(a,b)),hi=Math.Min(cad.B,Math.Max(a,b)),clearance=Math.Abs(d)-(reference.Width+cad.Width)/2;
    if(hi-lo<=tolerance||clearance<0||clearance>maxDistance)continue;
    candidates.Add(Tuple.Create(reference,Math.Sign(d),lo,hi));
   }
   var sides=new List<int>();
   foreach(int side in new[]{-1,1}){double covered=0,end=double.NegativeInfinity;
    foreach(var c in candidates.Where(c=>c.Item2==side).OrderBy(c=>c.Item3)){covered+=Math.Max(0,c.Item4-Math.Max(end,c.Item3));end=Math.Max(end,c.Item4);}
    if(covered>=(cad.B-cad.A)*0.8)sides.Add(side);
   }
   if(sides.Count!=1){result.Reason=sides.Count==0?"NoAdjacentReferenceWall":"AmbiguousReferenceSide";return result;}
   result.Side=sides[0];result.InteriorPlane=cad.N-result.Side*cad.Width/2;
   result.ReferenceIds=candidates.Where(c=>c.Item2==result.Side).Select(c=>c.Item1.Id).Distinct().OrderBy(i=>i).ToList();
   result.Run=new CadWallGeometry.Run{U=cad.U,V=cad.V,N=cad.N+result.Side*(actualWidth-cad.Width)/2,A=cad.A,B=cad.B,Width=actualWidth,Bridges=cad.Bridges};return result;
  }
 }
}
