using System;
using System.Collections.Generic;
using System.Linq;
namespace RevitMCP.Core {
 public static class CadWallGeometry {
  public class Segment { public double X1,Y1,X2,Y2; public Segment(double x1,double y1,double x2,double y2){X1=x1;Y1=y1;X2=x2;Y2=y2;} public double Length=>Math.Sqrt((X2-X1)*(X2-X1)+(Y2-Y1)*(Y2-Y1)); }
  public class Run { public double U,V,N,A,B,Width; public int Bridges; public string Reason; public Segment Axis=>new Segment(U*A-V*N,V*A+U*N,U*B-V*N,V*B+U*N); }
  class Edge { public double U,V,N,A,B; }
  static bool Same(double u,double v,double x,double y)=>Math.Abs(u*y-v*x)<0.00001 && u*x+v*y>0;
  public static List<Run> Find(List<Segment> lines,List<Segment> openings,double tolerance,double gap,double minWidth,double maxWidth) {
   var edges=new List<Edge>();
   foreach(var l in lines.Where(l=>l.Length>tolerance)){double u=(l.X2-l.X1)/l.Length,v=(l.Y2-l.Y1)/l.Length;if(u< -0.000001 || Math.Abs(u)<0.000001&&v<0){u=-u;v=-v;}double a=u*l.X1+v*l.Y1,b=u*l.X2+v*l.Y2;edges.Add(new Edge{U=u,V=v,N=-v*l.X1+u*l.Y1,A=Math.Min(a,b),B=Math.Max(a,b)});}
   edges=edges.OrderBy(e=>Math.Atan2(e.V,e.U)).ThenBy(e=>e.N).ThenBy(e=>e.A).ToList();
   var merged=new List<Edge>();
   foreach(var e in edges){var m=merged.FirstOrDefault(x=>Same(x.U,x.V,e.U,e.V)&&Math.Abs(x.N-e.N)<=tolerance&&e.A<=x.B+tolerance&&e.B>=x.A-tolerance);if(m==null)merged.Add(e);else{m.A=Math.Min(m.A,e.A);m.B=Math.Max(m.B,e.B);}}
   var candidates=new List<Run>();
   for(int i=0;i<merged.Count;i++)for(int j=i+1;j<merged.Count;j++){
    var a=merged[i];var b=merged[j];if(!Same(a.U,a.V,b.U,b.V))continue;double width=Math.Abs(a.N-b.N),start=Math.Max(a.A,b.A),end=Math.Min(a.B,b.B);
    if(width<minWidth-tolerance||width>maxWidth+tolerance||end-start<Math.Max(100,width))continue;
    // An intervening parallel line indicates a multi-line/ambiguous wall; never pair across it.
    if(merged.Any(e=>e!=a&&e!=b&&Same(a.U,a.V,e.U,e.V)&&e.N>Math.Min(a.N,b.N)+tolerance&&e.N<Math.Max(a.N,b.N)-tolerance&&Math.Min(e.B,end)-Math.Max(e.A,start)>tolerance))continue;
    candidates.Add(new Run{U=a.U,V=a.V,N=(a.N+b.N)/2,A=start,B=end,Width=width});
   }
   candidates=candidates.OrderBy(r=>Math.Atan2(r.V,r.U)).ThenBy(r=>r.N).ThenBy(r=>r.A).ToList();
   var result=new List<Run>();
   foreach(var c in candidates){var m=result.FirstOrDefault(r=>Same(r.U,r.V,c.U,c.V)&&Math.Abs(r.N-c.N)<=tolerance&&Math.Abs(r.Width-c.Width)<=tolerance&&c.A<=r.B+tolerance&&c.B>=r.A-tolerance);if(m==null)result.Add(c);else{m.A=Math.Min(m.A,c.A);m.B=Math.Max(m.B,c.B);}}
   // Bridge only equal-width, collinear paired runs with opening geometry covering the entire gap.
   bool changed=true;while(changed){changed=false;for(int i=0;i<result.Count&&!changed;i++)for(int j=i+1;j<result.Count;j++){
    var a=result[i];var b=result[j];if(!Same(a.U,a.V,b.U,b.V)||Math.Abs(a.N-b.N)>tolerance||Math.Abs(a.Width-b.Width)>tolerance)continue;
    if(b.A<a.A){var temp=a;a=b;b=temp;}double d=b.A-a.B;if(d<=tolerance||d>gap)continue;
    var intervals=new List<Tuple<double,double>>();
    foreach(var s in openings){double n1=-a.V*s.X1+a.U*s.Y1,n2=-a.V*s.X2+a.U*s.Y2;if(Math.Max(n1,n2)<a.N-a.Width/2-tolerance||Math.Min(n1,n2)>a.N+a.Width/2+tolerance)continue;
     double x=a.U*s.X1+a.V*s.Y1,y=a.U*s.X2+a.V*s.Y2;intervals.Add(Tuple.Create(Math.Min(x,y),Math.Max(x,y)));}
    double covered=a.B;foreach(var t in intervals.OrderBy(t=>t.Item1)){if(t.Item2<covered)continue;if(t.Item1>covered+tolerance)break;covered=Math.Max(covered,t.Item2);}
    if(covered<b.A-tolerance)continue;
    // A perpendicular wall crossing the gap is not a door/window opening.
    if(result.Any(r=>r!=a&&r!=b&&!Same(a.U,a.V,r.U,r.V)&&Crosses(r.Axis,a.U,a.V,a.N,a.B,b.A,tolerance)))continue;
    a.B=b.B;a.Bridges+=b.Bridges+1;result.Remove(b);changed=true;break;
   }}
   // Competing candidates occupying the same strip are excluded, not chosen arbitrarily.
   foreach(var a in result)if(result.Any(b=>a!=b&&Same(a.U,a.V,b.U,b.V)&&Math.Abs(a.N-b.N)<(a.Width+b.Width)/2-tolerance&&Math.Min(a.B,b.B)-Math.Max(a.A,b.A)>tolerance))a.Reason="AmbiguousWallPair";
   return result;
  }
  static bool Crosses(Segment s,double u,double v,double n,double a,double b,double tol){double n1=-v*s.X1+u*s.Y1-n,n2=-v*s.X2+u*s.Y2-n;if(n1*n2>0||Math.Abs(n1-n2)<1e-9)return false;double t=n1/(n1-n2),x=u*(s.X1+t*(s.X2-s.X1))+v*(s.Y1+t*(s.Y2-s.Y1));return x>a+tol&&x<b-tol;}
  public static bool Overlaps(Run r,Segment s,double width,double tol){if(s.Length<tol)return false;double u=(s.X2-s.X1)/s.Length,v=(s.Y2-s.Y1)/s.Length;if(Math.Abs(r.U*v-r.V*u)>0.001)return false;double n=-r.V*s.X1+r.U*s.Y1,a=r.U*s.X1+r.V*s.Y1,b=r.U*s.X2+r.V*s.Y2;return Math.Abs(n-r.N)<(width+r.Width)/2-tol&&Math.Min(r.B,Math.Max(a,b))-Math.Max(r.A,Math.Min(a,b))>tol;}
 }
}
