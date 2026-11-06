using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using BeamIdType = System.Int64;
#else
using BeamIdType = System.Int32;
#endif

namespace RevitMCP.Core {
 public static partial class DwgBeamExecutor {
  private static object CreateBeamsByNumericSchedule(Document doc,JObject p) {
   string Required(string key) {
    if(p[key]?.Type!=JTokenType.String || string.IsNullOrWhiteSpace((string)p[key]))throw new ArgumentException(key+" is required.");
    return (string)p[key];
   }
   bool Flag(string key,bool fallback) {
    if(p[key]==null)return fallback;
    if(p[key].Type!=JTokenType.Boolean)throw new ArgumentException(key+" must be boolean.");
    return (bool)p[key];
   }
   var vp=doc.ActiveView as ViewPlan ?? throw new ArgumentException("A plan view is required.");
   var level=vp.GenLevel ?? throw new ArgumentException("Plan level required.");
   Level ResolveLevel(string key,Level fallback) {
    if(p[key]==null)return fallback;
    if(p[key].Type!=JTokenType.Integer)throw new ArgumentException(key+" must be a level ID.");
    return doc.GetElement(new ElementId(p[key].Value<BeamIdType>())) as Level ?? throw new ArgumentException(key+" is not a level.");
   }
   var defaultLevel=ResolveLevel("defaultLevelId",level);
   var uncertainLevel=ResolveLevel("uncertainElevationLevelId",null);
   var offsetLevel=ResolveLevel("parenthesizedLevelId",null);
   string offsetUnits=(string)p["parenthesizedOffsetUnits"];
   if((offsetLevel==null)!=(offsetUnits==null))throw new ArgumentException("parenthesizedLevelId and parenthesizedOffsetUnits must be supplied together.");
   if(offsetUnits!=null && offsetUnits!="mm" && offsetUnits!="cm")throw new ArgumentException("parenthesizedOffsetUnits must be mm or cm.");
   bool writeNumber=Flag("writeTextNumber",false);
   string layer=Required("layerName"), family=Required("familyName"), units=Required("cadUnits");
   bool dryRun=Flag("dryRun",true),apply=Flag("apply",false);
   if(p["typeName"]!=null)throw new ArgumentException("numericSchedule cannot be combined with typeName.");
   var factors=new Dictionary<string,double>{{"mm",1},{"cm",10},{"m",1000}};
   if(!factors.ContainsKey(units))throw new ArgumentException("cadUnits must be mm, cm or m.");
   var cads=new FilteredElementCollector(doc,vp.Id).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
   if(cads.Count!=1 || !cads[0].IsLinked)throw new ArgumentException("This mode requires exactly one linked CAD in the active view.");
   var cad=cads[0];
   var beams=ExtractBeamCenterLines(CollectLayerLines(doc,vp,layer));
   var baseSym=FindFramingFamily(doc,family) ?? throw new ArgumentException("Structural framing family not found.");
   var symbols=baseSym.Family.GetFamilySymbolIds().Select(id=>doc.GetElement(id) as FamilySymbol).ToList();
   List<BeamScheduleRule> customRules=null;
   if(p["scheduleRules"]!=null){
    if(!(p["scheduleRules"] is JArray rulesArray)||rulesArray.Count==0)throw new ArgumentException("scheduleRules must be a nonempty array.");
    customRules=rulesArray.ToObject<List<BeamScheduleRule>>();foreach(var rule in customRules)rule.Validate();
   }
   string[] combinedLayers=null;
   if(p["textLayerNames"]!=null){if(!(p["textLayerNames"] is JArray layers)||layers.Count==0||layers.Any(v=>v.Type!=JTokenType.String||string.IsNullOrWhiteSpace((string)v)))throw new ArgumentException("textLayerNames must contain names.");combinedLayers=layers.Values<string>().Distinct().ToArray();}
   var existingBeams=customRules==null?new List<FamilyInstance>():new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType().OfType<FamilyInstance>().Where(e=>e.StructuralType==Autodesk.Revit.DB.Structure.StructuralType.Beam).ToList();
   var xName=p["textLayerNameX"]==null?null:Required("textLayerNameX");
   var yName=p["textLayerNameY"]==null?null:Required("textLayerNameY");
   if(combinedLayers==null && xName==null && yName==null)throw new ArgumentException("A text layer is required.");
   var labelCache=new Dictionary<string,List<LabelData>>();
   foreach(string name in (combinedLayers??new[]{xName,yName}).Where(n=>n!=null).Distinct())
    labelCache[name]=ReadScheduleLabels(doc,cad,name,factors[units],p["pythonExecutable"]==null?"python":Required("pythonExecutable"));
   var rows=new List<JObject>();
   var elevationSuffixes=new Dictionary<LabelData,string>();var elevationErrors=new Dictionary<LabelData,string>();var elevationDiagnostics=new JArray();
   if(p["elevationLayerPairs"]!=null && offsetLevel==null)throw new ArgumentException("Separate elevation layers require parenthesizedLevelId and parenthesizedOffsetUnits.");
   MatchSeparateElevations(doc,cad,p,labelCache,factors[units],elevationSuffixes,elevationErrors,elevationDiagnostics);
   var plans=new List<(BeamData Beam,FamilySymbol Symbol,JObject Row,string Key,Level Level,double OffsetMm)>();
   foreach(var b in beams) {
    var row=new JObject{["Status"]="Skipped",["StartXmm"]=b.StartPoint.X*FtMm,["StartYmm"]=b.StartPoint.Y*FtMm,
     ["EndXmm"]=b.EndPoint.X*FtMm,["EndYmm"]=b.EndPoint.Y*FtMm,["CadWidthMm"]=b.WidthMm};
    rows.Add(row);
    var names=combinedLayers??(b.IsXAligned?new[]{xName}:b.IsYAligned?new[]{yName}:new[]{xName,yName});
    var labels=names.Where(n=>n!=null).Distinct().SelectMany(n=>labelCache[n])
      .GroupBy(l=>LabelKey(l)+"|"+l.Text).Select(g=>g.First()).ToList();
    var ranked=labels.Select(l=>new {Label=l,Distance=(b.IsXAligned || b.IsYAligned)
     ? b.Center.DistanceTo(new XYZ(l.X,l.Y,b.Center.Z))*FtMm
     : PointToLineDistanceMm(new XYZ(l.X,l.Y,b.Center.Z),b.StartPoint,b.EndPoint)})
     .OrderBy(l=>l.Distance).ToList();
    double limit=(b.IsXAligned || b.IsYAligned)?2500:b.WidthMm/2+500;
    int choice=ColumnMarkMatching.Select(ranked.Select(l=>l.Distance).ToList(),limit,100);
    if(choice<0){row["Reason"]=choice==-2?"AmbiguousLabels":"NoNearbyLabel";continue;}
    var nearest=ranked[choice];
    row["Label"]=nearest.Label.Text;row["DistanceMm"]=nearest.Distance;
    bool uncertain=elevationErrors.TryGetValue(nearest.Label,out string elevationError);
    if(uncertain && uncertainLevel==null){row["Reason"]=elevationError;continue;}
    string parseText=nearest.Label.Text;
    if(!uncertain && elevationSuffixes.TryGetValue(nearest.Label,out string suffix)){parseText+=suffix;row["ElevationLabel"]=suffix;}
    BeamElevationLabel parsed;
    try{parsed=BeamElevationLabel.Parse(parseText);}catch(ArgumentException ex){row["Reason"]="InvalidBeamLabel";row["Detail"]=ex.Message;continue;}
    if(parsed.Offset.HasValue && offsetLevel==null){row["Reason"]="MissingElevationRule";continue;}
    var beamLevel=uncertain?uncertainLevel:parsed.Offset.HasValue?offsetLevel:defaultLevel;
    double offsetMm=uncertain?0:(parsed.Offset??0)*(offsetUnits=="cm"?10:1);
    if(uncertain){row["ElevationFallback"]=true;row["ElevationFallbackReason"]=elevationError;}
    if(double.IsInfinity(offsetMm)||Math.Abs(offsetMm)>1000000){row["Reason"]="ElevationOutOfRange";continue;}
    row["Number"]=parsed.Number;row["ReferenceLevelId"]=beamLevel.Id.GetIdValue();row["ReferenceLevel"]=beamLevel.Name;
    row["ZOffsetMm"]=offsetMm;row["TopElevationMm"]=beamLevel.Elevation*FtMm+offsetMm;
    row["WriteTextNumber"]=writeNumber;
    BeamScheduleRule rule=null;
    try{if(customRules!=null)rule=BeamScheduleRule.Resolve(customRules,parsed.Number);}catch(ArgumentException ex){row["Reason"]="AmbiguousNumberRule";row["Detail"]=ex.Message;continue;}
    string type=customRules==null?BeamNumberRules.TypeName(parsed.Number):rule?.TypeName;
    double width=customRules==null?BeamNumberRules.WidthMm(type):rule?.WidthMm??0;
    double height=customRules==null?2700:rule?.HeightMm??0;
    if(type==null){row["Reason"]="UnmatchedNumberRule";continue;}
    row["TypeName"]=type;row["WidthMm"]=width;row["HeightMm"]=height;row["BeamRole"]=rule?.BeamRole??(string)p["beamRole"];
    var matches=symbols.Where(s=>NormalizeX(s.Name)==type).ToList();
    if(matches.Count!=1){row["Reason"]=matches.Count==0?"MissingType":"AmbiguousType";continue;}
    var sym=matches[0];string wp=null,dp=null;DetectParams(sym,ref wp,ref dp);
    var w=wp==null?null:sym.LookupParameter(wp);var h=dp==null?null:sym.LookupParameter(dp);
    if(w==null || h==null || w.StorageType!=StorageType.Double || h.StorageType!=StorageType.Double ||
       !w.Definition.GetDataType().Equals(SpecTypeId.Length) || !h.Definition.GetDataType().Equals(SpecTypeId.Length) ||
       Math.Abs(w.AsDouble()*FtMm-width)>1 ||
       Math.Abs(h.AsDouble()*FtMm-height)>1) {row["Reason"]="TypeDimensionMismatch";continue;}
    if(customRules!=null && Math.Abs(b.WidthMm-width)>10){row["Reason"]="CadWidthMismatch";continue;}
    if(customRules!=null && existingBeams.Any(e=>ScheduleBeamOverlap(e,b,beamLevel,offsetMm))){row["Reason"]="ExistingBeamOverlap";continue;}
    plans.Add((b,sym,row,LabelKey(nearest.Label),beamLevel,offsetMm));
   }
   foreach(var group in plans.GroupBy(v=>v.Key).Where(g=>g.Count()>1))
    foreach(var plan in group)plan.Row["Reason"]="LabelAssignedToMultipleBeams";
   plans=plans.Where(v=>v.Row["Reason"]==null).ToList();
   foreach(var plan in plans)plan.Row["Status"]="Planned";
   if(customRules!=null){
    foreach(var plan in plans.Where(a=>plans.Any(b=>a.Row!=b.Row&&Math.Abs((a.Level.Elevation-b.Level.Elevation)*FtMm+a.OffsetMm-b.OffsetMm)<1&&ScheduleSegmentOverlap(a.Beam.StartPoint,a.Beam.EndPoint,b.Beam.StartPoint,b.Beam.EndPoint))))plan.Row["Reason"]="OverlappingCandidates";
    plans=plans.Where(v=>v.Row["Reason"]==null).ToList();foreach(var row in rows.Where(r=>r["Reason"]!=null))row["Status"]="Skipped";
   }
   string previewToken=PlanarFloorRegions.Hash(new JObject{["UncertainLevel"]=uncertainLevel?.UniqueId,["UncertainLevelElevation"]=uncertainLevel?.Elevation,["ElevationLayerPairs"]=p["elevationLayerPairs"],["ElevationDiagnostics"]=elevationDiagnostics,["DefaultLevel"]=defaultLevel.UniqueId,["DefaultElevation"]=defaultLevel.Elevation,["OffsetLevel"]=offsetLevel?.UniqueId,["OffsetLevelElevation"]=offsetLevel?.Elevation,["OffsetUnits"]=offsetUnits,["WriteTextNumber"]=writeNumber,["Document"]=doc.ProjectInformation.UniqueId,["View"]=vp.UniqueId,["Level"]=level.UniqueId,["Elevation"]=level.Elevation,["Cad"]=cad.UniqueId,["Family"]=family,["Rules"]=p["scheduleRules"],["Rows"]=JArray.FromObject(rows),["Units"]=units,["Layer"]=layer,["TextLayers"]=JArray.FromObject((combinedLayers??new[]{xName,yName}).Where(n=>n!=null)),["Symbols"]=JArray.FromObject(symbols.Select(s=>new{Id=s.UniqueId}))}.ToString(Newtonsoft.Json.Formatting.None));
   if((customRules!=null||offsetLevel!=null||writeNumber||p["defaultLevelId"]!=null) && apply&&!dryRun && (string)p["previewToken"]!=previewToken)throw new ArgumentException("Fresh matching previewToken required for schedule/elevation/number changes.");
   bool committed=false,rolledBack=false;string error=null;int created=0;
   if(apply && !dryRun && plans.Count>0) {
    using(var tx=new Transaction(doc,"Create CAD beams by numeric schedule")) {
     try {
      if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction start failed.");
      tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new ScheduleFailures()).SetClearAfterRollback(true).SetForcedModalHandling(true));
      var types=new List<string>();
      foreach(var plan in plans) {
       int before=created;
       var instance=PlaceBeamInstance(doc,plan.Beam,plan.Level,plan.Symbol,types,ref created,(string)plan.Row["BeamRole"]);
       if(instance!=null)plan.Row["ElementId"]=instance.Id.GetIdValue();
       if(created!=before+1)throw new InvalidOperationException("Beam was not created.");
       var z=instance.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE);
       if(z==null||z.IsReadOnly||z.StorageType!=StorageType.Double)throw new InvalidOperationException("Cannot set beam Z offset.");
       z.Set(plan.OffsetMm/FtMm);
       foreach(var parameterId in new[]{BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION,BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION}){
        var end=instance.get_Parameter(parameterId);if(end==null||end.IsReadOnly)throw new InvalidOperationException("Cannot set beam endpoint elevation.");end.Set(0.0);
       }
       if(writeNumber){var number=ScheduleTextNumber(instance);if(number.IsReadOnly||!number.Set((string)plan.Row["Number"]))throw new InvalidOperationException("Cannot write Text/編號.");}
      }
      doc.Regenerate();
      foreach(var plan in plans){
       var instance=doc.GetElement(new ElementId(plan.Row["ElementId"].Value<BeamIdType>())) as FamilyInstance;
       var refId=instance.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId();
       if(refId!=plan.Level.Id||Math.Abs(instance.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE).AsDouble()*FtMm-plan.OffsetMm)>0.01||instance.get_Parameter(BuiltInParameter.Z_JUSTIFICATION)?.AsInteger()!=0)throw new InvalidOperationException("Beam reference level or top/Z offset readback failed.");
       if(writeNumber && ScheduleTextNumber(instance).AsString()!=(string)plan.Row["Number"])throw new InvalidOperationException("Text/編號 readback failed.");
       foreach(var parameterId in new[]{BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION,BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION})if(Math.Abs(instance.get_Parameter(parameterId).AsDouble()*FtMm)>0.01)throw new InvalidOperationException("Beam endpoint elevation readback failed.");
      }
      if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Commit failed.");
      committed=true;foreach(var plan in plans)plan.Row["Status"]="Created";
     }catch(Exception ex){
      error=ex.Message;if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();
      rolledBack=tx.GetStatus()==TransactionStatus.RolledBack;created=0;
      foreach(var plan in plans){plan.Row.Remove("ElementId");plan.Row["Status"]="Failed";plan.Row["Reason"]="BatchNotCommitted";}
     }
    }
   }
   return new {Success=error==null,Mode="numericSchedule",PreviewToken=previewToken,CustomSchedule=customRules!=null,DryRun=dryRun,Apply=apply,Committed=committed,RolledBack=rolledBack,Error=error,
    ViewId=vp.Id.GetIdValue(),Level=level.Name,FamilyName=family,CadUnits=units,Scanned=rows.Count,Planned=plans.Count,Created=created,
    ImportInstanceUniqueId=cad.UniqueId,SourceFile=GetCadFilePath(doc,cad),
    ElevationDiagnostics=elevationDiagnostics,
    ElevationFallbacks=rows.Where(r=>(bool?)r["ElevationFallback"]==true).Select(r=>new{Number=(string)r["Number"],Status=(string)r["Status"],ElementId=r["ElementId"],ReferenceLevel=(string)r["ReferenceLevel"],ZOffsetMm=(double)r["ZOffsetMm"],Reason=(string)r["ElevationFallbackReason"]}).ToList(),
    LabelInventory=labelCache.Select(pair=>new{Layer=pair.Key,Count=pair.Value.Count,Labels=pair.Value.Select(l=>l.Text).Distinct().ToList()}).ToList(),
    ByElevation=plans.GroupBy(v=>new{ReferenceLevel=v.Level.Name,ZOffsetMm=v.OffsetMm}).Select(g=>new{g.Key.ReferenceLevel,g.Key.ZOffsetMm,Count=g.Count()}).ToList(),
    Skipped=rows.Count(r=>(string)r["Status"]=="Skipped"),Failed=rows.Count(r=>(string)r["Status"]=="Failed"),
    UnmatchedLabels=rows.Where(r=>(string)r["Reason"]=="UnmatchedNumberRule").Select(r=>(string)r["Label"]).Distinct().ToList(),
    ByReason=rows.Where(r=>r["Reason"]!=null).GroupBy(r=>(string)r["Reason"]).Select(g=>new{Reason=g.Key,Count=g.Count()}).ToList(),Results=rows};
  }
  private static bool ScheduleSegmentOverlap(XYZ a,XYZ b,XYZ c,XYZ d){
   var v=new XYZ(b.X-a.X,b.Y-a.Y,0);double length=v.GetLength();if(length<0.001)return false;v=v/length;
   var u=new XYZ(d.X-c.X,d.Y-c.Y,0);if(u.GetLength()<0.001||Math.Abs(v.CrossProduct(u.Normalize()).Z)>0.001)return false;
   if(Math.Abs((c.X-a.X)*v.Y-(c.Y-a.Y)*v.X)*FtMm>10)return false;
   double s=(c-a).DotProduct(v),t=(d-a).DotProduct(v);return (Math.Min(length,Math.Max(s,t))-Math.Max(0,Math.Min(s,t)))*FtMm>10;
  }
  private static Parameter ScheduleTextNumber(FamilyInstance e){
   var matches=e.Parameters.Cast<Parameter>().Where(x=>x.Definition.Name=="編號"&&x.Definition.GetGroupTypeId().Equals(GroupTypeId.Text)).ToList();
   if(matches.Count!=1||matches[0].StorageType!=StorageType.String)throw new InvalidOperationException("Missing, ambiguous or incompatible Text/編號 on beam.");
   return matches[0];
  }
  private static bool ScheduleBeamOverlap(FamilyInstance e,BeamData b,Level level,double offsetMm){
   if(!(e.Location is LocationCurve location))return false;var refLevel=e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId();
   var existingLevel=e.Document.GetElement(refLevel??e.LevelId) as Level;
   if(existingLevel==null)return false;
   double z=e.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE)?.AsDouble()??0;
   double start=e.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION)?.AsDouble()??0,end=e.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION)?.AsDouble()??0;
   if(Math.Abs((existingLevel.Elevation+z+start-level.Elevation)*FtMm-offsetMm)>1||Math.Abs((existingLevel.Elevation+z+end-level.Elevation)*FtMm-offsetMm)>1)return false;
   return ScheduleSegmentOverlap(b.StartPoint,b.EndPoint,location.Curve.GetEndPoint(0),location.Curve.GetEndPoint(1));
  }
  internal static JArray ReadFloorScheduleLabels(Document doc, ImportInstance cad, string layer, double mmPerUnit, string python) =>
   new JArray(ReadScheduleLabels(doc,cad,layer,mmPerUnit,python).Select(l=>new JObject {["Text"]=l.Text,["Xmm"]=l.X*304.8,["Ymm"]=l.Y*304.8}));
  private static List<LabelData> ReadScheduleLabels(Document doc,ImportInstance cad,string layer,double mmPerUnit,string python,
    View elevationView=null,List<Line> modelLines=null,string controlLayer=null) {
   string file=GetCadFilePath(doc,cad)??throw new InvalidOperationException("Linked CAD file unavailable.");
   string worker=FindWorkerScript()??throw new InvalidOperationException("CAD reader unavailable.");
   if(new[]{file,worker,layer}.Any(s=>s.Contains("\"")||s.EndsWith("\\")))throw new ArgumentException("Unsupported CAD argument.");
   if(controlLayer!=null&&(controlLayer.Contains("\"")||controlLayer.EndsWith("\\")))throw new ArgumentException("Unsupported control layer argument.");
   var psi=new ProcessStartInfo(python,"\""+worker+"\" \""+file+"\" \""+layer+"\""+(controlLayer==null?"":" \""+controlLayer+"\"")){
    UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   string odaRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ODA");
   if(Directory.Exists(odaRoot))psi.EnvironmentVariables["PATH"]=string.Join(";",Directory.GetDirectories(odaRoot)
     .Where(d=>File.Exists(Path.Combine(d,"ODAFileConverter.exe"))))+";"+Environment.GetEnvironmentVariable("PATH");
   string output;
   using(var process=Process.Start(psi)) {
    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    if(!process.WaitForExit(60000)){process.Kill();throw new TimeoutException("CAD reader timeout.");}
    output=stdout.GetAwaiter().GetResult();string err=stderr.GetAwaiter().GetResult();
    if(process.ExitCode!=0)throw new InvalidOperationException(output+err);
   }
   var json=JObject.Parse(output);if(json["error"]!=null)throw new InvalidOperationException((string)json["error"]);
   if(mmPerUnit==0){
    int ins=(int?)json["insunits"]??0;
    if(!new[]{1,2,4,5,6,14}.Contains(ins))throw new ArgumentException("CAD has no supported INSUNITS; specify cadUnits.");
    mmPerUnit=(double)json["insunits_to_mm"];
   }
   var transform=cad.GetTotalTransform();var labels=new List<LabelData>();
   if(elevationView!=null)transform=ResolveElevationTextTransform(transform,elevationView,modelLines,(JArray)json["control_lines"],mmPerUnit/FtMm);
   foreach(JObject item in json["data"] as JArray??new JArray()){
    string text=((string)item["text"]??"").Trim();if(text.Length==0)continue;
    var pt=transform.OfPoint(new XYZ((double)item["x"]*mmPerUnit/FtMm,(double)item["y"]*mmPerUnit/FtMm,(double)item["z"]*mmPerUnit/FtMm));
    labels.Add(new LabelData{Text=text,X=pt.X,Y=pt.Y,Z=pt.Z});
   }
   return labels;
  }
  private sealed class ScheduleFailures:IFailuresPreprocessor {
   public FailureProcessingResult PreprocessFailures(FailuresAccessor a)=>a.GetFailureMessages().Any(m=>m.GetSeverity()==FailureSeverity.Error)
    ?FailureProcessingResult.ProceedWithRollBack:FailureProcessingResult.Continue;
  }
 }
}
