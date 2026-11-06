using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
namespace RevitMCP.Core
{
 internal static class TerrainRetainingOutline
 {
  internal static Polygon Enclose(Geometry union,out double gapLimitMm)
  {
   var gf=union.Factory;var parameters=new BufferParameters{JoinStyle=JoinStyle.Mitre,MitreLimit=10,QuadrantSegments=1};
   foreach(double limit in new double[]{0,5,10,25,50,100,200,300,500,1000})
   {
    var joined=limit==0?union:BufferOp.Buffer(BufferOp.Buffer(union,(limit/2+.1)/304.8,parameters),-(limit/2+.1)/304.8,parameters);
    var candidates=new List<Polygon>();
    for(int i=0;i<joined.NumGeometries;i++)if(joined.GetGeometryN(i) is Polygon p && p.NumInteriorRings>0)candidates.Add(gf.CreatePolygon((LinearRing)p.ExteriorRing.Copy()));
    foreach(var outer in candidates.OrderByDescending(p=>p.Area))
    {
     if(!outer.IsValid||outer.Area<outer.EnvelopeInternal.Area*.5)continue;
     if(!outer.Buffer(.2/304.8).Covers(union))continue;
     gapLimitMm=limit;return outer;
    }
   }
   throw new InvalidOperationException("No enclosing retaining ring after straight-edge gap closure up to1000mm; no excavation applied.");
  }
 }
}
