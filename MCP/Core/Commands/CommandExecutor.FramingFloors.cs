using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using NetTopologySuite.Geometries;
using NGeometry=NetTopologySuite.Geometries.Geometry;
using NPolygon=NetTopologySuite.Geometries.Polygon;

namespace RevitMCP.Core {
 public partial class CommandExecutor {
  private object ListFloorTypes(JObject args) {
   var doc=_uiApp.ActiveUIDocument.Document;
   return new { Types=new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
    .OrderBy(t=>t.Name).Select(t=>new {TypeId=t.Id.GetIdValue(),Name=t.Name,
     ThicknessMm=(t.GetCompoundStructure()?.GetWidth() ?? 0)*304.8,
     IsFoundation=t.IsFoundationSlab,
     HasVariableLayer=(t.GetCompoundStructure()?.VariableLayerIndex ?? -1)>=0}).ToList() };
  }
  private static bool FramingFloorFlag(JObject p,string key,bool fallback) {
   if(p[key]==null)return fallback;
   if(p[key].Type!=JTokenType.Boolean)throw new ArgumentException(key+" must be boolean.");
   return (bool)p[key];
  }
  private static double FramingFloorNumber(JObject p,string key,double fallback,double min,double max) {
   var token=p[key];if(token==null)return fallback;
   if(token.Type!=JTokenType.Integer && token.Type!=JTokenType.Float)throw new ArgumentException(key+" must be numeric.");
   double value=(double)token;
   if(double.IsNaN(value)||double.IsInfinity(value)||value<min||value>max)throw new ArgumentException(key+" is out of range.");
   return value;
  }
  private static long FramingFloorId(JObject p,string key) {
   if(p[key]?.Type!=JTokenType.Integer || (long)p[key]<=0)throw new ArgumentException(key+" must be a positive ID.");
   return (long)p[key];
  }
  private object CreateFloorsFromFramingRegions(JObject args) {
   var doc=_uiApp.ActiveUIDocument.Document;
   if(doc.IsFamilyDocument)throw new ArgumentException("A project document is required.");
   var view=doc.ActiveView as ViewPlan ?? throw new ArgumentException("A plan view is required.");
   var level=doc.GetElement(new ElementId(FramingFloorId(args,"levelId"))) as Level ?? throw new ArgumentException("Level not found.");
   if(view.GenLevel==null || view.GenLevel.Id!=level.Id)throw new ArgumentException("Active plan must belong to the specified level.");
   var placementLevel=args["placementLevelId"]==null?level:doc.GetElement(new ElementId(FramingFloorId(args,"placementLevelId"))) as Level;
   if(placementLevel==null)throw new ArgumentException("Placement level not found.");
   var type=doc.GetElement(new ElementId(FramingFloorId(args,"floorTypeId"))) as FloorType ?? throw new ArgumentException("Floor type not found.");
   var compound=type.GetCompoundStructure();
   if(type.IsFoundationSlab || compound==null || compound.GetWidth()<=0 || compound.VariableLayerIndex>=0)
    throw new ArgumentException("Choose a uniform-thickness non-foundation floor type.");
   string requestedTypeName=(string)args["newFloorTypeName"];
   if(args["newThicknessMm"]!=null){
    double requestedThickness=FramingFloorNumber(args,"newThicknessMm",0,1,2000);
    if(string.IsNullOrWhiteSpace(requestedTypeName))throw new ArgumentException("newFloorTypeName required.");
    var matches=new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().Where(t=>t.Name==requestedTypeName).ToList();
    if(matches.Count>1)throw new ArgumentException("Ambiguous floor type name.");
    if(matches.Count==1){type=matches[0];compound=type.GetCompoundStructure();if(type.IsFoundationSlab||compound==null||compound.VariableLayerIndex>=0||Math.Abs(compound.GetWidth()*304.8-requestedThickness)>0.1)throw new ArgumentException("Existing named floor type thickness mismatch.");}
    else {if(compound.LayerCount!=1)throw new ArgumentException("New thickness requires a single-layer template.");compound.SetLayerWidth(0,requestedThickness/304.8);}
   }else if(requestedTypeName!=null)throw new ArgumentException("newThicknessMm required.");
   bool columns=FramingFloorFlag(args,"includeColumns",true), dryRun=FramingFloorFlag(args,"dryRun",true),
    apply=FramingFloorFlag(args,"apply",false), structural=FramingFloorFlag(args,"structural",true);
   double offset=FramingFloorNumber(args,"offsetMm",0,-10000,10000);
   double minimumArea=FramingFloorNumber(args,"minimumAreaM2",0.1,0.001,100000);
   double top=placementLevel.Elevation+offset/304.8,bottom=top-compound.GetWidth();
   double projectionBelow=FramingFloorNumber(args,"beamProjectionBelowSlabMm",0,0,3000);
   double beamBottom=bottom-projectionBelow/304.8;
   bool allReferenceLevels=FramingFloorFlag(args,"includeBeamsAcrossLevels",false);
   var phaseId=view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId() ?? ElementId.InvalidElementId;
   bool Current(Element e) {
    if(phaseId==ElementId.InvalidElementId)return true;
    var status=e.GetPhaseStatus(phaseId);
    return status==ElementOnPhaseStatus.New || status==ElementOnPhaseStatus.Existing;
   }
   var sources=new List<Element>();
   var beams=new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).OfCategory(BuiltInCategory.OST_StructuralFraming)
    .Cast<FamilyInstance>().Where(e=>e.StructuralType==Autodesk.Revit.DB.Structure.StructuralType.Beam
     && (allReferenceLevels||e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId()==level.Id) && Current(e)).ToList();
   if(args["beamIds"]!=null) {
    if(!(args["beamIds"] is JArray ids) || ids.Count==0 || ids.Any(i=>i.Type!=JTokenType.Integer || (long)i<=0))
     throw new ArgumentException("beamIds must be a nonempty array of positive IDs.");
    var wanted=ids.Values<long>().Distinct().ToList();
    beams=beams.Where(b=>wanted.Contains(b.Id.GetIdValue())).ToList();
    if(beams.Count!=wanted.Count)throw new ArgumentException("Some beamIds are not beams on this level/current phase.");
   }
   sources.AddRange(beams);
   if(columns) sources.AddRange(new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
    .WhereElementIsNotElementType().Cast<FamilyInstance>()
    .Where(e=>(e.Category?.Id.GetIdValue()==(long)BuiltInCategory.OST_StructuralColumns ||
      e.Category?.Id.GetIdValue()==(long)BuiltInCategory.OST_Columns) && Current(e) && FramingFloorInBand(e,bottom,top)));
   if(sources.Count==0 || sources.Count>2000)throw new ArgumentException("Need between 1 and 2000 source members.");
   if(sources.Any(e=>e.DesignOption!=null))throw new ArgumentException("Design-option members require a separate supported workflow.");
   var exclusions=new List<(long Id,NGeometry Shape)>();
   var excludedItems=new List<JObject>();
   var footprints=new List<NGeometry>();var sourceRows=new List<object>();
   foreach(var source in sources.OrderBy(e=>e.Id.GetIdValue())) {
    bool isBeam=source.Category?.Id.GetIdValue()==(long)BuiltInCategory.OST_StructuralFraming;
    double sourceBottom=isBeam?beamBottom:bottom;
    if(!FramingFloorInBand(source,sourceBottom,top)) {sourceRows.Add(new {ElementId=source.Id.GetIdValue(),Status="OutsideSlabBand"});continue;}
    try {
     var shape=FramingFloorFootprint(source,sourceBottom,top);
     if(shape.IsEmpty)throw new InvalidOperationException("No usable footprint.");
     footprints.Add(shape);sourceRows.Add(new {ElementId=source.Id.GetIdValue(),Status=isBeam&&projectionBelow>0?"UsedExtendedBeamBand":"Used"});
    } catch(Exception ex) when (FramingFloorRecoverable(ex)) {
     var envelope=FramingFloorEnvelope(source);
     footprints.Add(envelope);exclusions.Add((source.Id.GetIdValue(),envelope));
     excludedItems.Add(new JObject {["ElementId"]=source.Id.GetIdValue(),["Name"]=source.Name,
      ["Category"]=source.Category?.Name,["Reason"]="SourceGeometryUnavailable",["Detail"]=ex.Message,
      ["EnvelopeMm"]=FramingFloorCoordinates(((NPolygon)envelope).ExteriorRing.Coordinates)});
     sourceRows.Add(new {ElementId=source.Id.GetIdValue(),Status="ExcludedEnvelope"});
    }
   }
   var cadSchedule=args["cadSchedule"] as JObject;
   if(args["cadSchedule"]!=null && cadSchedule==null)throw new ArgumentException("cadSchedule must be an object.");
   JArray cadLabels=null; ImportInstance scheduleCad=null; JArray labelRules=null;
   JArray elevationLabels=null;List<FloorCadElevation.Match> resolvedLabels=null;
   if(cadSchedule!=null) {
    string uid=(string)cadSchedule["importInstanceUniqueId"],layer=(string)cadSchedule["textLayerName"],python=(string)cadSchedule["pythonExecutable"];
    if(string.IsNullOrWhiteSpace(uid)||string.IsNullOrWhiteSpace(layer)||string.IsNullOrWhiteSpace(python)||string.IsNullOrWhiteSpace((string)cadSchedule["floorValue"]))throw new ArgumentException("CAD ID, layer, Python and floorValue required.");
    scheduleCad=doc.GetElement(uid) as ImportInstance;
    if(scheduleCad==null||!scheduleCad.IsLinked||!new FilteredElementCollector(doc,view.Id).OfClass(typeof(ImportInstance)).Any(e=>e.Id==scheduleCad.Id))throw new ArgumentException("Linked CAD must be present in active plan.");
    var factors=new Dictionary<string,double>{{"mm",1},{"cm",10},{"m",1000}};
    if(!factors.TryGetValue((string)cadSchedule["cadUnits"]??"",out double factor))throw new ArgumentException("Invalid CAD units.");
    labelRules=cadSchedule["rules"] as JArray;
    if(labelRules==null||labelRules.Count==0)throw new ArgumentException("CAD label rules required.");
    foreach(JObject rule in labelRules) {
     string pattern=(string)rule["pattern"];
     if(string.IsNullOrWhiteSpace(pattern)||!System.Text.RegularExpressions.Regex.IsMatch(pattern,@"^[A-Za-z]+(?:[0-9]+|\*)?$"))throw new ArgumentException("Invalid label pattern.");
     FramingFloorNumber(rule,"thicknessMm",0,1,2000);
     if(rule["thicknessMm"]==null)throw new ArgumentException("Rule thickness required.");
    }
    cadLabels=DwgBeamExecutor.ReadFloorScheduleLabels(doc,scheduleCad,layer,factor,python);
    if(cadLabels.Count==0)throw new ArgumentException("No CAD text found in exact layer: "+layer);
    if(cadSchedule["elevationLayerName"]!=null){
     string elevationLayer=(string)cadSchedule["elevationLayerName"];
     if(string.IsNullOrWhiteSpace(elevationLayer))throw new ArgumentException("Elevation layer required.");
     long defaultId=FramingFloorId(cadSchedule,"defaultLevelId"),numericId=FramingFloorId(cadSchedule,"numericLevelId");
     if(!(doc.GetElement(new ElementId(defaultId)) is Level)||!(doc.GetElement(new ElementId(numericId)) is Level))throw new ArgumentException("Invalid schedule levels.");
     string units=(string)cadSchedule["elevationUnits"];if(!factors.TryGetValue(units??"",out double elevationFactor))throw new ArgumentException("Elevation units required.");
     elevationLabels=DwgBeamExecutor.ReadFloorScheduleLabels(doc,scheduleCad,elevationLayer,factor,python);
     if(elevationLabels.Count==0)throw new ArgumentException("No elevation annotations found in exact layer: "+elevationLayer);
     List<FloorCadElevation.Text> Texts(JArray a)=>a.OfType<JObject>().Select(l=>new FloorCadElevation.Text{Value=(string)l["Text"],X=(double)l["Xmm"],Y=(double)l["Ymm"]}).ToList();
     resolvedLabels=FloorCadElevation.Resolve(Texts(cadLabels),Texts(elevationLabels),defaultId,numericId,elevationFactor,
      FramingFloorNumber(cadSchedule,"elevationPairDistanceMm",1800,1,10000),FramingFloorNumber(cadSchedule,"elevationPairGapMm",100,1,10000),
      new HashSet<string>((cadSchedule["skipLabels"] as JArray??new JArray()).Values<string>(),StringComparer.Ordinal),FramingFloorFlag(cadSchedule,"foldedAsFlat",false));
    }
   }
   var regions=PlanarFloorRegions.Find(footprints);
   var labelRegions=new Dictionary<JObject,int>();
   if(cadSchedule!=null){
    double labelTolerance=FramingFloorNumber(cadSchedule,"labelBoundaryToleranceMm",0,0,300);
    double labelGap=FramingFloorNumber(cadSchedule,"labelBoundaryGapMm",100,1,1000);
    foreach(var label in cadLabels.OfType<JObject>())labelRegions[label]=PlanarFloorRegions.LabelRegion(regions,new Coordinate((double)label["Xmm"],(double)label["Ymm"]),labelTolerance,labelGap);
   }
   var existing=new List<NGeometry>();
   foreach(var floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>().Where(e=>Current(e)&&FramingFloorInBand(e,bottom,top))) {
    if(floor.DesignOption!=null)throw new ArgumentException("Design-option floor overlaps the analysis band.");
    try {
     var shape=FramingFloorFootprint(floor,bottom,top);
     if(shape.IsEmpty)throw new InvalidOperationException("No usable existing floor footprint.");
     existing.Add(shape);
    } catch(Exception ex) when (FramingFloorRecoverable(ex)) {
     var envelope=FramingFloorEnvelope(floor);existing.Add(envelope);
     exclusions.Add((floor.Id.GetIdValue(),envelope));
     excludedItems.Add(new JObject {["ElementId"]=floor.Id.GetIdValue(),["Name"]=floor.Name,
      ["Category"]=floor.Category?.Name,["Reason"]="ExistingFloorGeometryUnavailable",["Detail"]=ex.Message,
      ["EnvelopeMm"]=FramingFloorCoordinates(((NPolygon)envelope).ExteriorRing.Coordinates)});
    }
   }
   var occupied=PlanarFloorRegions.Union(existing);
   var rows=new List<JObject>();var plans=new List<(NPolygon Shape,JObject Row,IList<CurveLoop> Loops)>();
   var selection=args["regionKeys"] as JArray;
   if(args["regionKeys"]!=null && (selection==null || selection.Count==0 || selection.Any(t=>t.Type!=JTokenType.String || string.IsNullOrWhiteSpace((string)t))))
    throw new ArgumentException("regionKeys must contain nonempty preview keys.");
   var selected=selection==null?null:new HashSet<string>(selection.Values<string>());
   foreach(var region in regions) {
    var key=PlanarFloorRegions.Key(region);
    var row=new JObject {["RegionKey"]=key,["AreaM2"]=region.Area/1000000,["Status"]="Skipped",
     ["OuterBoundaryMm"]=FramingFloorCoordinates(region.ExteriorRing.Coordinates),
     ["HolesMm"]=new JArray(Enumerable.Range(0,region.NumInteriorRings).Select(i=>FramingFloorCoordinates(region.GetInteriorRingN(i).Coordinates)))};
    rows.Add(row);
    var affected=exclusions.Where(e=>PlanarFloorRegions.IsNearExcluded(region,e.Shape)).Select(e=>e.Id).ToList();
    if(affected.Count>0) {row["Reason"]="NearUnsupportedGeometry";row["ExcludedElementIds"]=JArray.FromObject(affected);continue;}
    if(region.Area<minimumArea*1000000) {row["Reason"]="BelowMinimumArea";continue;}
    try {
     if(!occupied.IsEmpty && PlanarFloorRegions.OverlapArea(region,occupied)>1) {row["Reason"]="ExistingFloorOverlap";continue;}
    } catch(Exception ex) when (FramingFloorRecoverable(ex)) {row["Reason"]="RegionGeometryUnavailable";row["Detail"]=ex.Message;continue;}
    if(selected!=null&&!selected.Contains(key)) {row["Reason"]="NotSelected";continue;}
    if(cadSchedule!=null) {
     var labels=cadLabels.OfType<JObject>().Where(l=>labelRegions[l]==regions.IndexOf(region)).ToList();
     row["BoundaryLabelMatches"]=JArray.FromObject(labels.Where(l=>!region.Covers(PlanarFloorRegions.Factory.CreatePoint(new Coordinate((double)l["Xmm"],(double)l["Ymm"])))).Select(l=>new{Text=(string)l["Text"],DistanceMm=region.Distance(PlanarFloorRegions.Factory.CreatePoint(new Coordinate((double)l["Xmm"],(double)l["Ymm"])))}));
     var mapped=resolvedLabels==null?null:labels.Select(l=>resolvedLabels[cadLabels.IndexOf(l)]).ToList();
     row["RawCadLabels"]=JArray.FromObject(labels.Select(l=>(string)l["Text"]));
     if(mapped!=null&&mapped.Any(m=>m.Reason!=null)){row["Reason"]="UnsupportedOrAmbiguousCadLabel";row["LabelIssues"]=JArray.FromObject(mapped.Where(m=>m.Reason!=null).Select(m=>new{m.Code,m.Reason}));continue;}
     var codes=(mapped==null?labels.Select(l=>((string)l["Text"]).Trim()):mapped.Select(m=>m.Code)).Distinct(StringComparer.Ordinal).ToList();
     row["CadLabels"]=JArray.FromObject(codes);
     if(codes.Count!=1){row["Reason"]=codes.Count==0?"NoSlabLabel":"AmbiguousSlabLabels";continue;}
     string code=codes[0];row["Number"]=code;row["FloorValue"]=(string)cadSchedule["floorValue"];
     var exact=labelRules.OfType<JObject>().Where(r=>(string)r["pattern"]==code).ToList();
     var matched=exact.Count>0?exact:labelRules.OfType<JObject>().Where(r=>((string)r["pattern"]).EndsWith("*") && System.Text.RegularExpressions.Regex.IsMatch(code,"^"+System.Text.RegularExpressions.Regex.Escape(((string)r["pattern"]).TrimEnd('*'))+"[0-9]+$")).ToList();
     if(matched.Count!=1){row["Reason"]=matched.Count==0?"UnknownSlabNumber":"AmbiguousSlabRule";continue;}
     double thickness=(double)matched[0]["thicknessMm"];row["ScheduledThicknessMm"]=thickness;
     if(Math.Abs(thickness-compound.GetWidth()*304.8)>0.1){row["Reason"]="OtherSlabThickness";continue;}
     if(mapped!=null){
      var heights=mapped.Select(m=>new{m.LevelId,m.OffsetMm}).Distinct().ToList();
      if(heights.Count!=1){row["Reason"]="AmbiguousSlabElevations";continue;}
      row["PlacementLevelId"]=heights[0].LevelId;row["OffsetMm"]=heights[0].OffsetMm;
      row["ElevationAnnotations"]=JArray.FromObject(mapped.Select(m=>m.Annotation).Where(s=>s!=null));
      if(heights[0].LevelId!=placementLevel.Id.GetIdValue()||Math.Abs(heights[0].OffsetMm-offset)>0.01){row["Reason"]="OtherSlabElevation";continue;}
     }
    }
    IList<CurveLoop> loops;
    try {
     loops=FramingFloorLoops(region,doc.Application.ShortCurveTolerance);
     if(!BoundaryValidation.IsValidHorizontalBoundary(loops))throw new ArgumentException("Invalid horizontal boundary.");
    } catch(Exception ex) {row["Reason"]="InvalidBoundary";row["Detail"]=ex.Message;continue;}
    row["Status"]="Planned";plans.Add((region,row,loops));
   }
   if(selected!=null && selected.Except(rows.Select(r=>(string)r["RegionKey"])).Any())
    throw new ArgumentException("Unknown region key; preview again.");
   string signature=string.Join("|",new[]{doc.ProjectInformation.UniqueId,view.UniqueId,level.UniqueId,type.UniqueId,
    offset.ToString("R",CultureInfo.InvariantCulture),compound.GetWidth().ToString("R",CultureInfo.InvariantCulture),columns.ToString(),structural.ToString(),
    minimumArea.ToString("R",CultureInfo.InvariantCulture),phaseId.GetIdValue().ToString(),
    string.Join(",",exclusions.OrderBy(e=>e.Id).Select(e=>e.Id+":"+PlanarFloorRegions.Key(e.Shape))),
    string.Join(",",sources.OrderBy(e=>e.Id.GetIdValue()).Select(e=>e.UniqueId)),
    PlanarFloorRegions.Key(PlanarFloorRegions.Union(footprints)),PlanarFloorRegions.Key(occupied),
    string.Join(",",rows.Select(r=>(string)r["RegionKey"]+":"+(string)r["Status"]+":"+(string)r["Reason"]))});
   if(cadSchedule!=null)signature+="|"+cadSchedule.ToString(Newtonsoft.Json.Formatting.None)+"|"+cadLabels.ToString(Newtonsoft.Json.Formatting.None)+"|"+rows.Aggregate("",(a,r)=>a+(string)r["Number"]+":"+(string)r["Reason"]+";");
   string token=PlanarFloorRegions.Hash(signature);
   token=PlanarFloorRegions.Hash(token+"|beamProjectionBelowSlabMm="+projectionBelow.ToString("R",CultureInfo.InvariantCulture));
   token=PlanarFloorRegions.Hash(token+"|"+placementLevel.UniqueId+"|"+placementLevel.Elevation.ToString("R",CultureInfo.InvariantCulture)+"|"+allReferenceLevels+"|"+requestedTypeName+"|"+elevationLabels?.ToString(Newtonsoft.Json.Formatting.None));
   bool committed=false,rolledBack=false;string error=null;
   if(apply&&!dryRun) {
    if(args["previewToken"]?.Type!=JTokenType.String || (string)args["previewToken"]!=token)
     throw new ArgumentException("Valid matching previewToken required; model or settings changed. Preview again.");
    if(plans.Count>0) using(var group=new TransactionGroup(doc,"Create floors in framing clear regions")) {
     try {
      if(group.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction group start failed.");
      foreach(var plan in plans) {
       using(var tx=new Transaction(doc,"Create one framing-region floor")) {
        try {
         if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction start failed.");
         tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FramingFloorFailures()).SetClearAfterRollback(true).SetForcedModalHandling(true));
         var actualType=type;
         if(requestedTypeName!=null){
          actualType=new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().SingleOrDefault(t=>t.Name==requestedTypeName);
          if(actualType==null){actualType=(FloorType)type.Duplicate(requestedTypeName);actualType.SetCompoundStructure(compound);}
          if(Math.Abs(actualType.GetCompoundStructure().GetWidth()-compound.GetWidth())*304.8>0.1)throw new InvalidOperationException("New type thickness verification failed.");
         }
         var floor=Floor.Create(doc,plan.Loops,actualType.Id,placementLevel.Id);
         var height=floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
         var structure=floor.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
         if(height==null || height.IsReadOnly || !height.Set(offset/304.8))throw new InvalidOperationException("Could not set floor elevation.");
         if(structure==null || structure.IsReadOnly || !structure.Set(structural?1:0))throw new InvalidOperationException("Could not set structural flag.");
         if(floor.GetTypeId()!=actualType.Id)throw new InvalidOperationException("Floor type verification failed.");
         if(cadSchedule!=null) {
          FramingFloorSetText(floor,"編號",(string)plan.Row["Number"]);
          FramingFloorSetText(floor,"Floor",(string)plan.Row["FloorValue"]);
         }
         long id=floor.Id.GetIdValue();
         doc.Regenerate();
         if(floor.LevelId!=placementLevel.Id||Math.Abs(height.AsDouble()*304.8-offset)>0.01)throw new InvalidOperationException("Floor level/offset verification failed.");
         var floorBounds=AdditionSolidBounds(floor);
         if(floorBounds==null||Math.Abs(floorBounds.Max.Z-top)*304.8>1||Math.Abs(floorBounds.Min.Z-bottom)*304.8>1)throw new InvalidOperationException("Floor solid elevation verification failed.");
         plan.Row["WallJoins"]=JoinNewFloorWalls(doc,floor);
         if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Region transaction not committed.");
         plan.Row["ElementId"]=id;plan.Row["Status"]="Created";
        } catch(Exception ex) {
         if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();
         if(!FramingFloorRecoverable(ex) || tx.GetStatus()!=TransactionStatus.RolledBack)throw;
         plan.Row.Remove("WallJoins");plan.Row.Remove("ElementId");plan.Row["Status"]="Failed";
         plan.Row["Reason"]="RegionNotCommitted";plan.Row["Detail"]=ex.Message;
        }
       }
      }
      if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Could not commit transaction group.");
      committed=true;
     } catch(Exception ex) {
      error=ex.Message;if(group.GetStatus()==TransactionStatus.Started)group.RollBack();
      rolledBack=group.GetStatus()==TransactionStatus.RolledBack;
      foreach(var plan in plans){plan.Row.Remove("WallJoins");plan.Row.Remove("ElementId");plan.Row["Status"]="Failed";plan.Row["Reason"]="BatchNotCommitted";}
     }
    }
   }
   return new {Success=error==null && !rows.Any(r=>(string)r["Status"]=="Failed"),PartialSuccess=committed && rows.Any(r=>(string)r["Status"]=="Created") && rows.Any(r=>(string)r["Status"]=="Failed"),Error=error,DryRun=dryRun,Apply=apply,Committed=committed,RolledBack=rolledBack,
    ViewId=view.Id.GetIdValue(),LevelId=level.Id.GetIdValue(),LevelName=level.Name,FloorTypeId=type.Id.GetIdValue(),FloorTypeName=type.Name,
    PlacementLevelId=placementLevel.Id.GetIdValue(),PlacementLevelName=placementLevel.Name,RequestedTypeName=requestedTypeName,
    LabelInventory=resolvedLabels==null?null:resolvedLabels.Select((m,i)=>new{RawText=(string)cadLabels[i]["Text"],m.Code,m.LevelId,m.OffsetMm,m.Annotation,m.Reason}).ToList(),
    BoundaryMode="innerClear",BeamProjectionBelowSlabMm=projectionBelow,BeamAnalysisBottomMm=beamBottom*304.8,SlabTopMm=top*304.8,SlabBottomMm=bottom*304.8,
    IncludeColumns=columns,OffsetMm=offset,ThicknessMm=compound.GetWidth()*304.8,PreviewToken=token,
    AutoJoinWalls=true,WallJoinFailed=rows.Where(r=>(string)r["Status"]=="Created").Sum(r=>(r["WallJoins"] as JArray)?.Count(j=>(string)j["Status"]=="Failed")??0),
    Scanned=sources.Count,ClosedRegions=regions.Count,Planned=plans.Count,Created=rows.Count(r=>(string)r["Status"]=="Created"),
    Skipped=rows.Count(r=>(string)r["Status"]=="Skipped"),Failed=rows.Count(r=>(string)r["Status"]=="Failed"),
    TotalPlannedAreaM2=plans.Sum(p=>p.Shape.Area)/1000000,
    ByReason=rows.Where(r=>r["Reason"]!=null).GroupBy(r=>(string)r["Reason"]).Select(g=>new {Reason=g.Key,Count=g.Count()}).ToList(),
    ExcludedItems=excludedItems,ExcludedItemCount=excludedItems.Count,Sources=sourceRows,Results=rows,
    Notes=new[]{"Only current-document members in active plan phase are considered; linked structures are not included.",
     "Footprints are projected from actual solids clipped to the slab thickness band; coordinates rounded to 0.001 mm.",
     "No gap bridging; no inference of stairs/shafts not modeled as structural obstacles. Review all regions before apply.",
     "Unsupported geometry is quarantined using conservative envelopes; touching regions are skipped. Per-region failures roll back locally; fatal document errors roll back the batch."}};
  }
  private static void FramingFloorSetText(Floor floor,string name,string value) {
   var matches=floor.GetParameters(name).Where(p=>p.Definition.GetGroupTypeId().Equals(GroupTypeId.Text)).ToList();
   if(matches.Count!=1||matches[0].IsReadOnly||matches[0].StorageType!=StorageType.String)throw new InvalidOperationException("Expected one writable Text-group string parameter: "+name);
   if(!matches[0].Set(value)||matches[0].AsString()!=value)throw new InvalidOperationException("Text parameter readback failed: "+name);
  }
  private static bool FramingFloorRecoverable(Exception ex) =>
   !(ex is Autodesk.Revit.Exceptions.RegenerationFailedException) &&
   !(ex is Autodesk.Revit.Exceptions.ModificationForbiddenException) &&
   !(ex is Autodesk.Revit.Exceptions.OperationCanceledException) &&
   !(ex is OperationCanceledException) && !(ex is OutOfMemoryException);
  private static NGeometry FramingFloorEnvelope(Element e) {
   var box=e.get_BoundingBox(null) ?? throw new InvalidOperationException("Cannot safely locate unsupported element "+e.Id.GetIdValue()+". Batch stopped.");
   var corners=new List<XYZ>();
   for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)
    corners.Add(box.Transform.OfPoint(new XYZ(x==0?box.Min.X:box.Max.X,y==0?box.Min.Y:box.Max.Y,z==0?box.Min.Z:box.Max.Z)));
   double x0=corners.Min(p=>p.X)*304.8,x1=corners.Max(p=>p.X)*304.8,y0=corners.Min(p=>p.Y)*304.8,y1=corners.Max(p=>p.Y)*304.8;
   if(new[]{x0,x1,y0,y1}.Any(v=>double.IsNaN(v)||double.IsInfinity(v)) || x1<=x0 || y1<=y0)
    throw new InvalidOperationException("Invalid unsupported-element envelope. Batch stopped.");
   return PlanarFloorRegions.Factory.CreatePolygon(new[]{new Coordinate(x0,y0),new Coordinate(x1,y0),new Coordinate(x1,y1),new Coordinate(x0,y1),new Coordinate(x0,y0)});
  }
  private static bool FramingFloorInBand(Element e,double bottom,double top) {
   var b=e.get_BoundingBox(null);
   return b!=null && b.Max.Z>bottom+1e-6 && b.Min.Z<top-1e-6;
  }
  private static IEnumerable<Solid> FramingFloorSolids(GeometryElement geometry) {
   if(geometry==null)yield break;
   foreach(var obj in geometry) {
    if(obj is Solid solid && solid.Volume>1e-9)yield return solid;
    else if(obj is GeometryInstance instance)
     foreach(var s in FramingFloorSolids(instance.GetInstanceGeometry()))yield return s;
   }
  }
  private static NGeometry FramingFloorFootprint(Element e,double bottom,double top) {
   var box=e.get_BoundingBox(null);
   if(box==null)throw new InvalidOperationException("No bounding box for "+e.Id.GetIdValue());
   double margin=1;
   var pts=new[]{new XYZ(box.Min.X-margin,box.Min.Y-margin,bottom),new XYZ(box.Max.X+margin,box.Min.Y-margin,bottom),
    new XYZ(box.Max.X+margin,box.Max.Y+margin,bottom),new XYZ(box.Min.X-margin,box.Max.Y+margin,bottom)};
   var ring=new CurveLoop();for(int i=0;i<4;i++)ring.Append(Line.CreateBound(pts[i],pts[(i+1)%4]));
   var band=GeometryCreationUtilities.CreateExtrusionGeometry(new[]{ring},XYZ.BasisZ,top-bottom);
   var pieces=new List<NGeometry>();int triangles=0;
   var geometry=e.get_Geometry(new Options {DetailLevel=ViewDetailLevel.Fine,IncludeNonVisibleObjects=false});
   foreach(var solid in FramingFloorSolids(geometry)) {
    // Restrict v1 to planar faces: approximating circular columns by inscribed chords could intrude into columns.
    
    var clip=BooleanOperationsUtils.ExecuteBooleanOperation(solid,band,BooleanOperationsType.Intersect);
    if(clip==null||clip.Volume<=1e-9)continue;
    foreach(Face face in clip.Faces) {
     if(!FramingFloorIsPlanar(face))throw new InvalidOperationException("Non-planar face within slab band: element "+e.Id.GetIdValue()+", face type "+face.GetType().Name+". No writes performed.");
     var mesh=face.Triangulate();
     for(int i=0;i<mesh.NumTriangles;i++) {
      if(++triangles>200000)throw new InvalidOperationException("Footprint complexity limit exceeded.");
      var tri=mesh.get_Triangle(i);var coords=new Coordinate[4];
      for(int j=0;j<3;j++){var v=tri.get_Vertex(j);coords[j]=new Coordinate(Math.Round(v.X*304.8,3),Math.Round(v.Y*304.8,3));}
      coords[3]=coords[0].Copy();
      var p=PlanarFloorRegions.Factory.CreatePolygon(coords);
      if(p.Area>0.000001)pieces.Add(p);
     }
    }
   }
   return PlanarFloorRegions.Union(pieces);
  }
  // Revit may encode geometrically planar joined faces as ruled or spline faces.
  // Verify mesh vertices AND sampled surface positions/normals; do not trust the CLR type alone.
  private static bool FramingFloorIsPlanar(Face face) {
   if(face is PlanarFace)return true;
   var mesh=face.Triangulate(1.0);
   XYZ origin=null,normal=null;
   for(int i=0;i<mesh.NumTriangles;i++) {
    var triangle=mesh.get_Triangle(i);
    var n=(triangle.get_Vertex(1)-triangle.get_Vertex(0)).CrossProduct(triangle.get_Vertex(2)-triangle.get_Vertex(0));
    if(n.GetLength()>1e-12){origin=triangle.get_Vertex(0);normal=n.Normalize();break;}
   }
   if(origin==null)return false;
   double tolerance=0.001/304.8;
   foreach(var vertex in mesh.Vertices)
    if(Math.Abs((vertex-origin).DotProduct(normal))>tolerance)return false;
   var uvBox=face.GetBoundingBox();int samples=0;
   for(int u=0;u<=8;u++)for(int v=0;v<=8;v++) {
    var uv=new UV(uvBox.Min.U+(uvBox.Max.U-uvBox.Min.U)*u/8.0,uvBox.Min.V+(uvBox.Max.V-uvBox.Min.V)*v/8.0);
    if(!face.IsInside(uv))continue;
    samples++;
    if(Math.Abs((face.Evaluate(uv)-origin).DotProduct(normal))>tolerance)return false;
    if(Math.Abs(face.ComputeNormal(uv).Normalize().DotProduct(normal))<1-1e-10)return false;
   }
   return samples>=3;
  }
  private static JArray FramingFloorCoordinates(Coordinate[] points) =>
   new JArray(PlanarFloorRegions.Ring(points).Take(PlanarFloorRegions.Ring(points).Length-1)
    .Select(c=>new JObject{["x"]=c.X,["y"]=c.Y}));
  private static IList<CurveLoop> FramingFloorLoops(NPolygon p,double tolerance) {
   var result=new List<CurveLoop>();
   var rings=new List<LineString>{p.ExteriorRing};
   for(int i=0;i<p.NumInteriorRings;i++)rings.Add(p.GetInteriorRingN(i));
   foreach(var r in rings) {
    var points=PlanarFloorRegions.Ring(r.Coordinates);var loop=new CurveLoop();
    for(int i=1;i<points.Length;i++) {
     var a=new XYZ(points[i-1].X/304.8,points[i-1].Y/304.8,0);
     var b=new XYZ(points[i].X/304.8,points[i].Y/304.8,0);
     if(a.DistanceTo(b)<=tolerance)throw new ArgumentException("Boundary contains a segment below Revit short-curve tolerance.");
     loop.Append(Line.CreateBound(a,b));
    } result.Add(loop);
   }return result;
  }
  private sealed class FramingFloorFailures:IFailuresPreprocessor {
   public FailureProcessingResult PreprocessFailures(FailuresAccessor a)=>
    a.GetFailureMessages().Any(f=>f.GetSeverity()==FailureSeverity.Error)?FailureProcessingResult.ProceedWithRollBack:FailureProcessingResult.Continue;
  }
 }
}

