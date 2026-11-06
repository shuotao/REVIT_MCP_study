#if REVIT2024_OR_GREATER
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
namespace RevitMCP.Core
{
 public partial class CommandExecutor
 {
  private Polygon ExcavationModelBoundary(Document doc,JArray ids,out double gapLimitMm)
  {
   var gf=new GeometryFactory();var projections=new List<NetTopologySuite.Geometries.Geometry>();
   foreach(long id in ids.Values<long>().Distinct())
   {
    var element=doc.GetElement(new ElementId(id))??throw new ArgumentException("Missing boundary element "+id);
    long category=element.Category?.Id.Value??0;
    if(category!=(long)BuiltInCategory.OST_Walls&&category!=(long)BuiltInCategory.OST_StructuralColumns)throw new ArgumentException("Boundary requires wall/structural-column category: "+id);
    int before=projections.Count;
    foreach(var solid in ExcavationSolids(element))foreach(Face face in solid.Faces)
    {var mesh=face.Triangulate();for(int i=0;i<mesh.NumTriangles;i++){var triangle=mesh.get_Triangle(i);var c=Enumerable.Range(0,3).Select(j=>triangle.get_Vertex(j)).Select(v=>new Coordinate(Math.Round(v.X*3048)/3048,Math.Round(v.Y*3048)/3048)).ToList();c.Add(c[0].Copy());var poly=gf.CreatePolygon(c.ToArray());if(poly.IsValid&&poly.Area>1e-8)projections.Add(poly);}}
    if(projections.Count==before)throw new InvalidOperationException("No projected solid geometry for "+id);
   }
   return TerrainRetainingOutline.Enclose(UnaryUnionOp.Union(projections),out gapLimitMm);
  }
 }
}
#endif
