using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
namespace RevitMCP.Core
{
 // Geometry is constrained to reference planes. Constant offsets use locked dimensions, not parameters.
 internal sealed class SharedGateGeometry
 {
  internal readonly Document Doc;internal readonly View Front,Plan;
  internal readonly ReferencePlane X0,Y0,Z0;
  private readonly Dictionary<long,double> positions=new Dictionary<long,double>();
  internal SharedGateGeometry(Document d)
  {
   Doc=d;Front=new FilteredElementCollector(d).OfClass(typeof(View)).Cast<View>().First(v=>!v.IsTemplate&&v.ViewType==ViewType.Elevation&&Math.Abs(v.ViewDirection.Y)>.99);
   Plan=new FilteredElementCollector(d).OfClass(typeof(ViewPlan)).Cast<View>().First(v=>!v.IsTemplate&&Math.Abs(v.ViewDirection.Z)>.99);
   X0=Raw(0,0,"定位X");Y0=Raw(1,0,"定位Y");Z0=Raw(2,0,"定位Z");X0.Pinned=Y0.Pinned=Z0.Pinned=true;
  }
  internal FamilyParameter Input(string name,double mm,string formula=null,bool instance=false)
  {var p=Doc.FamilyManager.AddParameter(name,GroupTypeId.Geometry,SpecTypeId.Length,instance);if(formula==null)Doc.FamilyManager.Set(p,mm/304.8);else Doc.FamilyManager.SetFormula(p,formula);return p;}
  internal ReferencePlane[] VerticalGrid(ReferencePlane first,ReferencePlane last,int count)
  {
   var all=new List<ReferencePlane>{first};for(int i=1;i<count;i++)all.Add(Raw(2,Pos(first)+(Pos(last)-Pos(first))*i/count));all.Add(last);
   var refs=new ReferenceArray();foreach(var p in all)refs.Append(p.GetReference());Doc.Regenerate();
   var dim=Doc.FamilyCreate.NewLinearDimension(Front,Line.CreateBound(new XYZ(-400,0,Pos(first))/304.8,new XYZ(-400,0,Pos(last))/304.8),refs);dim.AreSegmentsEqual=true;Doc.Regenerate();return all.ToArray();
  }
  internal double Value(string name)=>Doc.FamilyManager.CurrentType.AsDouble(Doc.FamilyManager.get_Parameter(name)).Value*304.8;
  internal ReferencePlane Raw(int axis,double mm,string name=null)
  {
   double c=mm/304.8;var a=axis==0?new XYZ(c,0,-10):axis==1?new XYZ(-10,c,0):new XYZ(-10,0,c);
   var b=axis==0?new XYZ(c,0,30):axis==1?new XYZ(50,c,0):new XYZ(50,0,c);
   var p=Doc.FamilyCreate.NewReferencePlane(a,b,axis==1?XYZ.BasisZ:XYZ.BasisY,axis==1?Plan:Front);
   if(name!=null){p.Name=name;p.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.StrongReference);}
   positions[p.Id.GetIdValue()]=mm;Doc.Regenerate();return p;
  }
  internal double Pos(ReferencePlane p)
  {var n=p.Normal;var point=p.BubbleEnd;return (Math.Abs(n.X)>.99?point.X:Math.Abs(n.Y)>.99?point.Y:point.Z)*304.8;}
  internal ReferencePlane Offset(int axis,ReferencePlane from,double distance,FamilyParameter parameter=null)
  {
   if(Math.Abs(distance)<.00001 && parameter==null)return from;
   var to=Raw(axis,Pos(from)+distance);var refs=new ReferenceArray();refs.Append(from.GetReference());refs.Append(to.GetReference());
   var a=axis==0?new XYZ(Pos(from),0,-200):axis==1?new XYZ(-200,Pos(from),0):new XYZ(-200,0,Pos(from));
   var b=axis==0?new XYZ(Pos(to),0,-200):axis==1?new XYZ(-200,Pos(to),0):new XYZ(-200,0,Pos(to));
   Dimension dim;
   try{dim=Doc.FamilyCreate.NewLinearDimension(axis==1?Plan:Front,Line.CreateBound(a/304.8,b/304.8),refs);}
   catch(Exception ex){throw new InvalidOperationException("Shared gate offset axis="+axis+", from="+Pos(from)+"mm, distance="+distance+"mm: "+ex.Message,ex);}
   if(parameter!=null)dim.FamilyLabel=parameter;else dim.IsLocked=true;return to;
  }
  internal ReferencePlane[] Grid(ReferencePlane first,ReferencePlane last,int count)
  {
   var all=new List<ReferencePlane>{first};for(int i=1;i<count;i++)all.Add(Raw(0,Pos(first)+(Pos(last)-Pos(first))*i/count));all.Add(last);
   var refs=new ReferenceArray();foreach(var p in all)refs.Append(p.GetReference());
   var dim=Doc.FamilyCreate.NewLinearDimension(Front,Line.CreateBound(new XYZ(Pos(first),0,-400)/304.8,new XYZ(Pos(last),0,-400)/304.8),refs);dim.AreSegmentsEqual=true;return all.ToArray();
  }
  internal Extrusion Box(ReferencePlane x0,ReferencePlane x1,ReferencePlane y0,ReferencePlane y1,ReferencePlane z0,ReferencePlane z1,ElementId material)
  {
   double a=Pos(x0),b=Pos(x1),c=Pos(y0),e=Pos(y1);var pts=new[]{new XYZ(a,c,0),new XYZ(b,c,0),new XYZ(b,e,0),new XYZ(a,e,0)};
   var ca=new CurveArray();for(int i=0;i<4;i++)ca.Append(Line.CreateBound(pts[i]/304.8,pts[(i+1)%4]/304.8));var loops=new CurveArrArray();loops.Append(ca);
   double bottom=Pos(z0)/304.8,top=Pos(z1)/304.8;
   if(top-bottom<=1e-7)throw new InvalidOperationException("Box top must be above bottom.");
   // NewExtrusion requires a nonzero initial depth, including foundations ending at elevation zero.
   var ex=Doc.FamilyCreate.NewExtrusion(true,loops,SketchPlane.Create(Doc,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,XYZ.Zero)),Math.Max(top,top-bottom));ex.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM).Set(bottom);ex.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM).Set(top);ex.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(material);Doc.Regenerate();
   foreach(var curve in ex.Sketch.GetAllElements().Select(id=>Doc.GetElement(id)).OfType<ModelCurve>())
   {var line=(Line)curve.GeometryCurve;var mid=line.Evaluate(.5,true)*304.8;var target=Math.Abs(line.Direction.Y)>.99?(Math.Abs(mid.X-a)<.01?x0:x1):(Math.Abs(mid.Y-c)<.01?y0:y1);Doc.FamilyCreate.NewAlignment(Plan,target.GetReference(),line.Reference);}
   var solid=ex.get_Geometry(new Options{ComputeReferences=true}).OfType<Solid>().First(s=>s.Faces.Size>0);
   foreach(var face in solid.Faces.Cast<Face>().OfType<PlanarFace>().Where(f=>Math.Abs(f.FaceNormal.Z)>.99))Doc.FamilyCreate.NewAlignment(Front,(face.FaceNormal.Z>0?z1:z0).GetReference(),face.Reference);
   return ex;
  }
  internal Extrusion Diagonal(ReferencePlane end,ReferencePlane high,ElementId material)
  {
   var low15=Offset(2,Z0,-15);var high15=Offset(2,high,-15);
   var xs=new[]{X0,end,end,X0};var zs=new[]{Z0,high,high15,low15};var pts=Enumerable.Range(0,4).Select(i=>new XYZ(Pos(xs[i]),0,Pos(zs[i]))/304.8).ToArray();var ca=new CurveArray();for(int i=0;i<4;i++)ca.Append(Line.CreateBound(pts[i],pts[(i+1)%4]));var loops=new CurveArrArray();loops.Append(ca);
   var ex=Doc.FamilyCreate.NewExtrusion(true,loops,SketchPlane.Create(Doc,Plane.CreateByNormalAndOrigin(XYZ.BasisY,XYZ.Zero)),10/304.8);ex.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(material);Doc.Regenerate();
   var edges=ex.Sketch.GetAllElements().Select(id=>Doc.GetElement(id)).OfType<ModelCurve>().ToList();
   for(int i=0;i<4;i++){var edge=edges.First(e=>e.GeometryCurve.GetEndPoint(0).DistanceTo(pts[i])<1e-6);var point=edge.GeometryCurve.GetEndPointReference(0);
    foreach(bool x in new[]{true,false}){var anchor=Offset(x?0:2,x?xs[i]:zs[i],-1000);var refs=new ReferenceArray();refs.Append(anchor.GetReference());refs.Append(point);var p=pts[i];var start=p-(x?XYZ.BasisX:XYZ.BasisZ)*(1000/304.8);var dim=Doc.FamilyCreate.NewLinearDimension(Front,Line.CreateBound(start,p),refs);dim.IsLocked=true;}}
   return ex;
  }
 }
}
