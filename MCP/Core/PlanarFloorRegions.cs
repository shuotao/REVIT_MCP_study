using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Overlay;
namespace RevitMCP.Core {
 public static class PlanarFloorRegions {
  public static readonly GeometryFactory Factory = new GeometryFactory();
  public static readonly PrecisionModel Precision = new PrecisionModel(1000);
  // Resolve against all regions before selection/overlap filtering. Never steal a
  // label from an occupied or unselected region, or from an internal column hole.
  public static int LabelRegion(IList<Polygon> regions,Coordinate coordinate,double toleranceMm,double ambiguityGapMm) {
   var point=Factory.CreatePoint(coordinate);
   var inside=Enumerable.Range(0,regions.Count).Where(i=>regions[i].Covers(point)).ToList();
   if(inside.Count>0)return inside.Count==1?inside[0]:-1;
   if(toleranceMm<=0)return -1;
   if(regions.Any(r=>Enumerable.Range(0,r.NumInteriorRings).Any(i=>Factory.CreatePolygon((LinearRing)r.GetInteriorRingN(i)).Covers(point))))return -1;
   var nearest=Enumerable.Range(0,regions.Count).Select(i=>new{Index=i,Distance=regions[i].Distance(point)}).OrderBy(v=>v.Distance).ToList();
   if(nearest.Count==0||nearest[0].Distance>toleranceMm)return -1;
   if(nearest.Count>1&&nearest[1].Distance-nearest[0].Distance<ambiguityGapMm)return -1;
   return nearest[0].Index;
  }
  public static Geometry Difference(Geometry a,Geometry b) => OverlayNG.Overlay(a,b,SpatialFunction.Difference,Precision);
  public static double OverlapArea(Geometry a,Geometry b) => OverlayNG.Overlay(a,b,SpatialFunction.Intersection,Precision).Area;
  public static IEnumerable<Polygon> Polygons(Geometry geometry) {
   if (geometry is Polygon p) { if(!p.IsEmpty) yield return p; }
   else if(geometry is GeometryCollection c)
    for(int i=0;i<c.NumGeometries;i++) foreach(var q in Polygons(c.GetGeometryN(i))) yield return q;
  }
  public static Geometry Union(IEnumerable<Geometry> pieces) {
   var all=pieces.Where(g=>g!=null && !g.IsEmpty).ToList();
   return all.Count==0 ? Factory.CreatePolygon() : UnaryUnionNG.Union(all,Precision);
  }
  // Bounded voids of the union, not its exterior envelope. Subtraction preserves internal column islands.
  public static List<Polygon> Find(IEnumerable<Geometry> footprints) {
   var obstacles=Union(footprints);
   if(!obstacles.IsValid) throw new InvalidOperationException("Invalid structural footprint union.");
   var holes=new List<Geometry>();
   foreach(var p in Polygons(obstacles))
    for(int i=0;i<p.NumInteriorRings;i++)
     holes.Add(Factory.CreatePolygon(p.GetInteriorRingN(i).Coordinates));
   if(holes.Count==0)return new List<Polygon>();
   var spaces=Difference(Union(holes),obstacles);
   if(!spaces.IsValid)throw new InvalidOperationException("Invalid floor region topology.");
   return Polygons(spaces).OrderBy(p=>p.EnvelopeInternal.MinX).ThenBy(p=>p.EnvelopeInternal.MinY).ToList();
  }
  public static bool IsNearExcluded(Geometry region,Geometry envelope) => region.Distance(envelope)<=1.0;
  public static string Hash(string value) {
   using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();
  }
  public static string Key(Geometry geometry) {
   var copy=(Geometry)geometry.Copy();copy.Normalize();return Hash(copy.AsText());
  }
  // Remove exactly collinear intermediate mesh vertices only; never bridge a real gap.
  public static Coordinate[] Ring(Coordinate[] closed) {
   var pts=closed.Take(closed.Length-1).ToList();
   bool changed=true;
   while(changed && pts.Count>3) {
    changed=false;
    for(int i=0;i<pts.Count;i++) {
     var a=pts[(i+pts.Count-1)%pts.Count];var b=pts[i];var c=pts[(i+1)%pts.Count];
     double cross=(b.X-a.X)*(c.Y-b.Y)-(b.Y-a.Y)*(c.X-b.X);
     double dot=(b.X-a.X)*(c.X-b.X)+(b.Y-a.Y)*(c.Y-b.Y);
     if(Math.Abs(cross)<1e-7 && dot>=0) {pts.RemoveAt(i);changed=true;break;}
    }
   }
   return pts.Concat(new[]{pts[0]}).ToArray();
  }
 }
}
