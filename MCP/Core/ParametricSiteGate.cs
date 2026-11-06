using System;
using System.Linq;
using Autodesk.Revit.DB;
namespace RevitMCP.Core
{
 internal sealed class ParametricSiteGate
 {
  private readonly Document doc;private readonly FenceParametricBuilder b;private readonly View front;private readonly ReferencePlane xBase,zBase;
  private readonly System.Collections.Generic.List<Tuple<Extrusion,string[],string[]>> diagonals=new System.Collections.Generic.List<Tuple<Extrusion,string[],string[]>>();
  internal ParametricSiteGate(Document d,double width,double height)
  {
   doc=d;b=new FenceParametricBuilder(d,width,height,1.2,false,1000,1200);b.Input("整體寬度",width);b.Input("整體高度",height);
   d.FamilyManager.SetFormula(d.FamilyManager.get_Parameter(FenceParametricBuilder.W),"整體寬度");d.FamilyManager.SetFormula(d.FamilyManager.get_Parameter(FenceParametricBuilder.H),"整體高度");
   front=new FilteredElementCollector(d).OfClass(typeof(View)).Cast<View>().First(v=>!v.IsTemplate&&v.ViewType==ViewType.Elevation&&Math.Abs(v.ViewDirection.Y)>.99);
   xBase=d.FamilyCreate.NewReferencePlane(new XYZ(-1000/304.8,0,-10),new XYZ(-1000/304.8,0,30),XYZ.BasisY,front);xBase.Pinned=true;
   zBase=d.FamilyCreate.NewReferencePlane(new XYZ(-10,0,-1000/304.8),new XYZ(50,0,-1000/304.8),XYZ.BasisY,front);zBase.Pinned=true;
  }
  internal void Build(ElementId metal)
  {
   string w="整體寬度",h="整體高度",pitch="(整體寬度 - 400 mm) / 29";
   b.Box("0 mm","200 mm","-75 mm","75 mm","0 mm",h,metal,"左門柱");b.Box(w+" - 200 mm",w,"-75 mm","75 mm","0 mm",h,metal,"右門柱");
   b.Box("200 mm",w+" - 200 mm","-35 mm","35 mm",h+" - 35 mm",h,metal,"桁架上弦");b.Box("200 mm",w+" - 200 mm","-35 mm","35 mm",h+" - 300 mm",h+" - 265 mm",metal,"桁架下弦");
   for(int i=0;i<29;i++)
   {
    string a="200 mm + ("+pitch+") * "+i,c="200 mm + ("+pitch+") * "+(i+1);
    b.Box(a+" + 3 mm",a+" + 23 mm","-20 mm","20 mm","20 mm",h+" - 320 mm",metal,"門片立框"+i);
    b.Box(c+" - 23 mm",c+" - 3 mm","-20 mm","20 mm","20 mm",h+" - 320 mm",metal,"門片立框右"+i);
    b.Box(a+" + 23 mm",c+" - 23 mm","-20 mm","20 mm","20 mm","50 mm",metal,"門片下框"+i);
    b.Box(a+" + 23 mm",c+" - 23 mm","-20 mm","20 mm",h+" - 350 mm",h+" - 320 mm",metal,"門片上框"+i);
    b.Box(a+" + 23 mm",c+" - 23 mm","-1.2 mm","0 mm","50 mm","if(整體高度 - 400 mm < 2000 mm, 整體高度 - 400 mm, 2000 mm)",metal,"門片鐵皮"+i);
   }
   for(int i=0;i<14;i++)Diagonal("200 mm + (整體寬度 - 400 mm) * "+i+" / 14","200 mm + (整體寬度 - 400 mm) * "+(i+1)+" / 14",i%2==0,metal);
   b.Verify();
  }
  private void Diagonal(string a,string c,bool up,ElementId material)
  {
   string lo="整體高度 - 265 mm",hi="整體高度 - 35 mm";string[] xs={a,c,c,a};string[] zs=up?new[]{lo,hi,hi+" - 15 mm",lo+" - 15 mm"}:new[]{hi,lo,lo+" + 15 mm",hi+" + 15 mm"};
   var points=Enumerable.Range(0,4).Select(i=>new XYZ(b.Value(xs[i]),0,b.Value(zs[i]))).ToArray();var profile=new CurveArray();for(int i=0;i<4;i++)profile.Append(Line.CreateBound(points[i],points[(i+1)%4]));var loops=new CurveArrArray();loops.Append(profile);
   var extrusion=doc.FamilyCreate.NewExtrusion(true,loops,SketchPlane.Create(doc,Plane.CreateByNormalAndOrigin(XYZ.BasisY,XYZ.Zero)),10/304.8);extrusion.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(material);doc.Regenerate();
   var edges=extrusion.Sketch.GetAllElements().Select(id=>doc.GetElement(id)).OfType<ModelCurve>().ToList();
   for(int i=0;i<4;i++)
   {
    var edge=edges.First(e=>e.GeometryCurve.GetEndPoint(0).DistanceTo(points[i])<1e-6);var reference=edge.GeometryCurve.GetEndPointReference(0);
    foreach(bool x in new[]{true,false}){var refs=new ReferenceArray();refs.Append((x?xBase:zBase).GetReference());refs.Append(reference);var p=points[i];var start=x?new XYZ(-1000/304.8,0,p.Z):new XYZ(p.X,0,-1000/304.8);var dim=doc.FamilyCreate.NewLinearDimension(front,Line.CreateBound(start,p),refs);dim.FamilyLabel=b.Formula("("+(x?xs[i]:zs[i])+") + 1000 mm");}
   }
   diagonals.Add(Tuple.Create(extrusion,xs,zs));
  }
  internal void Verify(){b.Verify();foreach(var item in diagonals){var box=item.Item1.get_BoundingBox(null);var xs=item.Item2.Select(b.Value).ToArray();var zs=item.Item3.Select(b.Value).ToArray();if(new[]{Math.Abs(box.Min.X-xs.Min()),Math.Abs(box.Max.X-xs.Max()),Math.Abs(box.Min.Z-zs.Min()),Math.Abs(box.Max.Z-zs.Max())}.Max()*304.8>.2)throw new InvalidOperationException("Diagonal flex failed.");}}
 }
}
