using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using AdditionId = System.Int64;
#else
using AdditionId = System.Int32;
#endif
namespace RevitMCP.Core {
 public partial class CommandExecutor {
  private static Parameter AdditionDimension(FamilySymbol s,bool width){
   var names=width?new[]{"B","b","寬度","Width"}:new[]{"H","h","高度","Height"};
   var ps=names.SelectMany(n=>s.GetParameters(n)).Where(p=>p.StorageType==StorageType.Double&&p.Definition.GetDataType().Equals(SpecTypeId.Length)).Distinct().ToList();
   if(ps.Count!=1)throw new ArgumentException("Ambiguous or missing rectangular dimension: "+s.Name);
   return ps[0];
  }
  private static Parameter AdditionText(Element e,string name){
   var ps=e.GetParameters(name).Where(p=>p.Definition.GetGroupTypeId().Equals(GroupTypeId.Text)).ToList();
   if(ps.Count!=1||ps[0].StorageType!=StorageType.String||ps[0].IsReadOnly)throw new ArgumentException("Missing/ambiguous/read-only Text/"+name);
   return ps[0];
  }
  private static void AdditionSet(Element e,BuiltInParameter key,double value,bool integer=false){
   var p=e.get_Parameter(key);if(p==null||p.IsReadOnly)throw new ArgumentException("Cannot set "+key);
   if(integer)p.Set((int)value);else p.Set(value);
  }
  private static double AdditionVolume(GeometryElement geometry){
   double volume=0;if(geometry==null)return 0;
   foreach(var item in geometry){if(item is Solid s)volume+=s.Volume;else if(item is GeometryInstance g)volume+=AdditionVolume(g.GetInstanceGeometry());}return volume;
  }
  // Element bounds also include the reference axis. Only positive-volume solids
  // establish the actual top/bottom of an addition.
  private static BoundingBoxXYZ AdditionSolidBounds(Element element){
   var points=new List<XYZ>();
   void Walk(GeometryElement geometry){
    if(geometry==null)return;
    foreach(var item in geometry){
     if(item is GeometryInstance instance){Walk(instance.GetInstanceGeometry());continue;}
     if(!(item is Solid solid)||solid.Volume<=1e-9)continue;
     var box=solid.GetBoundingBox();
     foreach(double x in new[]{box.Min.X,box.Max.X})foreach(double y in new[]{box.Min.Y,box.Max.Y})foreach(double z in new[]{box.Min.Z,box.Max.Z})points.Add(box.Transform.OfPoint(new XYZ(x,y,z)));
    }
   }
   Walk(element.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}));
   if(points.Count==0)return null;
   return new BoundingBoxXYZ{Min=new XYZ(points.Min(v=>v.X),points.Min(v=>v.Y),points.Min(v=>v.Z)),Max=new XYZ(points.Max(v=>v.X),points.Max(v=>v.Y),points.Max(v=>v.Z))};
  }
  private object CreateBeamAdditions(JObject p){
   if((string)p["mode"]=="slopedSolid")return CreateSlopedBeamAddition(p);
   if(p["mode"]!=null&&(string)p["mode"]!="rectangular")throw new ArgumentException("Unknown addition mode.");
   var doc=_uiApp.ActiveUIDocument.Document;
   if(!(p["sourceBeamIds"] is JArray ids)||ids.Count==0||ids.Count>500||ids.Any(x=>x.Type!=JTokenType.Integer))throw new ArgumentException("sourceBeamIds required (1..500).");
   var level=doc.GetElement(new ElementId(p["referenceLevelId"].Value<AdditionId>())) as Level??throw new ArgumentException("Invalid referenceLevelId.");
   double depth=ReadBeamDimensionMm(p,"depthMm"),offset=p["zOffsetMm"].Value<double>();
   if(double.IsNaN(offset)||double.IsInfinity(offset)||Math.Abs(offset)>1000000||depth>10000)throw new ArgumentException("Invalid dimensions/elevation.");
   string number=(string)p["numberValue"]??"增築";if(string.IsNullOrWhiteSpace(number))throw new ArgumentException("numberValue is empty.");
   bool dry=p["dryRun"]?.Value<bool>()??true,apply=p["apply"]?.Value<bool>()??false;
   double top=level.Elevation+offset/304.8,bottom=top-depth/304.8;
   var all=new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).OfCategory(BuiltInCategory.OST_StructuralFraming).Cast<FamilyInstance>().ToList();
   var rows=new List<JObject>();var sources=new Dictionary<AdditionId,FamilyInstance>();
   foreach(var id in ids.Values<AdditionId>().Distinct()){
    var source=doc.GetElement(new ElementId(id)) as FamilyInstance??throw new ArgumentException("Source missing: "+id);
    if(source.StructuralType!=StructuralType.Beam||!(source.Location is LocationCurve lc)||!(lc.Curve is Line line))throw new ArgumentException("Only straight structural beams supported: "+id);
    var a=line.GetEndPoint(0);var b=line.GetEndPoint(1);
    if(Math.Abs(a.Z-b.Z)>0.001||Math.Abs(source.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE)?.AsDouble()??0)>1e-8)throw new ArgumentException("Sloped/rotated source unsupported: "+id);
    double width=AdditionDimension(source.Symbol,true).AsDouble()*304.8;
    int y=source.get_Parameter(BuiltInParameter.Y_JUSTIFICATION)?.AsInteger()??1;
    double yo=source.get_Parameter(BuiltInParameter.Y_OFFSET_VALUE)?.AsDouble()??0;
    if((source.get_Parameter(BuiltInParameter.YZ_JUSTIFICATION)?.AsInteger()??0)!=0)throw new ArgumentException("Independent endpoint alignment unsupported.");
    AdditionText(source,"編號");
    string type=(width/10).ToString("0.###",CultureInfo.InvariantCulture)+"x"+(depth/10).ToString("0.###",CultureInfo.InvariantCulture);
    var matches=source.Symbol.Family.GetFamilySymbolIds().Select(i=>(FamilySymbol)doc.GetElement(i)).Where(s=>s.Name==type).ToList();
    if(matches.Count>1)throw new ArgumentException("Ambiguous type "+type);
    var target=matches.SingleOrDefault();
    if(target!=null&&(Math.Abs(AdditionDimension(target,true).AsDouble()*304.8-width)>0.1||Math.Abs(AdditionDimension(target,false).AsDouble()*304.8-depth)>0.1))throw new ArgumentException("Type dimensions do not match name: "+type);
    bool duplicate=all.Any(e=>e.Id!=source.Id&&e.Location is LocationCurve el&&el.Curve is Line&&SameAdditionAxis(line,el.Curve)&&AdditionSolidBounds(e) is BoundingBoxXYZ eb&&Math.Abs(eb.Max.Z-top)*304.8<1&&Math.Abs(eb.Min.Z-bottom)*304.8<1);
    var row=new JObject{["SourceId"]=id,["SourceUniqueId"]=source.UniqueId,["SourceTypeId"]=source.GetTypeId().GetIdValue(),["Family"]=source.Symbol.FamilyName,["TypeName"]=type,["ExistingTypeId"]=target?.Id.GetIdValue(),["WidthMm"]=width,["DepthMm"]=depth,["Start"]=new JArray(a.X,a.Y,a.Z),["End"]=new JArray(b.X,b.Y,b.Z),["YJustification"]=y,["YOffset"]=yo,["Status"]=duplicate?"Skipped":"Planned",["Reason"]=duplicate?"ExistingAddition":null};rows.Add(row);sources[id]=source;
   }
   var plans=rows.Where(r=>(string)r["Status"]=="Planned").ToList();
   var token=PlanarFloorRegions.Hash(new JObject{["Document"]=doc.ProjectInformation.UniqueId,["View"]=doc.ActiveView.UniqueId,["Level"]=level.UniqueId,["Top"]=top,["Depth"]=depth,["Number"]=number,["Rows"]=JArray.FromObject(rows)}.ToString());
   if(apply&&!dry&&(string)p["previewToken"]!=token)throw new ArgumentException("Fresh previewToken required.");
   bool committed=false;string error=null;
   if(apply&&!dry&&plans.Count>0)using(var tx=new Transaction(doc,"Create beam additions")){
    try{
     tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new BeamTypeFailuresPreprocessor()).SetClearAfterRollback(true));
     foreach(var row in plans){
      var source=sources[row["SourceId"].Value<AdditionId>()];string type=(string)row["TypeName"];
      var symbol=source.Symbol.Family.GetFamilySymbolIds().Select(i=>(FamilySymbol)doc.GetElement(i)).SingleOrDefault(s=>s.Name==type);
      if(symbol==null){symbol=(FamilySymbol)source.Symbol.Duplicate(type);var h=AdditionDimension(symbol,false);if(h.IsReadOnly)throw new ArgumentException("Type height read-only.");h.Set(depth/304.8);}
      if(Math.Abs(AdditionDimension(symbol,true).AsDouble()*304.8-(double)row["WidthMm"])>0.1||Math.Abs(AdditionDimension(symbol,false).AsDouble()*304.8-depth)>0.1)throw new InvalidOperationException("Type dimension verification failed.");
      if(!symbol.IsActive){symbol.Activate();doc.Regenerate();}
      var line=((LocationCurve)source.Location).Curve;var a=line.GetEndPoint(0);var b=line.GetEndPoint(1);
      var beam=doc.Create.NewFamilyInstance(Line.CreateBound(new XYZ(a.X,a.Y,level.Elevation),new XYZ(b.X,b.Y,level.Elevation)),symbol,level,StructuralType.Beam);
      StructuralFramingUtils.DisallowJoinAtEnd(beam,0);StructuralFramingUtils.DisallowJoinAtEnd(beam,1);
      AdditionSet(beam,BuiltInParameter.Y_JUSTIFICATION,(int)row["YJustification"],true);AdditionSet(beam,BuiltInParameter.Y_OFFSET_VALUE,(double)row["YOffset"]);
      AdditionSet(beam,BuiltInParameter.Z_JUSTIFICATION,0,true);AdditionSet(beam,BuiltInParameter.Z_OFFSET_VALUE,offset/304.8);
      AdditionSet(beam,BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION,0);AdditionSet(beam,BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION,0);
      AdditionText(beam,"編號").Set(number);row["ElementId"]=beam.Id.GetIdValue();
     }
     doc.Regenerate();
     foreach(var row in plans){
      var beam=(FamilyInstance)doc.GetElement(new ElementId(row["ElementId"].Value<AdditionId>()));
      var bb=AdditionSolidBounds(beam);double volume=AdditionVolume(beam.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}));
      if(bb==null||volume<=1e-9||Math.Abs(bb.Max.Z-top)*304.8>1||Math.Abs(bb.Min.Z-bottom)*304.8>1||beam.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId()!=level.Id||Math.Abs(beam.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE).AsDouble()*304.8-offset)>0.01||AdditionText(beam,"編號").AsString()!=number)throw new InvalidOperationException("Addition geometry/parameter verification failed: "+row["SourceId"]);
      row["VolumeM3"]=volume*Math.Pow(0.3048,3);row["TopMm"]=bb.Max.Z*304.8;row["BottomMm"]=bb.Min.Z*304.8;
     }
     if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Commit failed.");committed=true;foreach(var row in plans)row["Status"]="Created";
    }catch(Exception ex){error=ex.Message;if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();foreach(var row in plans){row.Remove("ElementId");row["Status"]="Failed";}}
   }
   return new{Success=error==null,Committed=committed,Error=error,DryRun=dry,PreviewToken=token,ReferenceLevel=level.Name,ZOffsetMm=offset,Number=number,Scanned=rows.Count,Planned=plans.Count,Created=committed?plans.Count:0,Skipped=rows.Count-plans.Count,Failed=error==null?0:plans.Count,Results=rows};
  }
  private static bool SameAdditionAxis(Curve a,Curve b){
   double Distance(XYZ x,XYZ y)=>new XYZ(x.X-y.X,x.Y-y.Y,0).GetLength()*304.8;
   return (Distance(a.GetEndPoint(0),b.GetEndPoint(0))<1&&Distance(a.GetEndPoint(1),b.GetEndPoint(1))<1)||(Distance(a.GetEndPoint(0),b.GetEndPoint(1))<1&&Distance(a.GetEndPoint(1),b.GetEndPoint(0))<1);
  }
 }
}
