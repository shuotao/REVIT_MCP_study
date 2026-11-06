using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
namespace RevitMCP.Core
{
 public partial class CommandExecutor
 {
  private sealed class GreenRun
  {internal XYZ Axis,Normal,Back;internal double Offset,Z,Height;internal ElementId Level,Phase;internal List<Tuple<double,double>> Pieces=new List<Tuple<double,double>>(),Merged=new List<Tuple<double,double>>();internal List<long> Sources=new List<long>();}
  private sealed class GreenPlacement
  {internal GreenRun Run;internal double From,Width;internal bool IsFiller,ShowLeft=true,ShowRight=true;internal XYZ Point=>Run.Axis*From+Run.Normal*Run.Offset+XYZ.BasisZ*Run.Z;internal bool Reverse=>Run.Normal.DotProduct(Run.Back)<0;internal XYZ RootPoint=>Reverse?Point+Run.Axis*Width:Point;internal XYZ RootAxis=>Reverse?-Run.Axis:Run.Axis;internal bool LocalLeft=>Reverse?ShowRight:ShowLeft;internal bool LocalRight=>Reverse?ShowLeft:ShowRight;}
  private object ReplaceSelectedGreenFence(JObject root,JObject spec)
  {
   var doc=_uiApp.ActiveUIDocument.Document;if(doc.IsFamilyDocument||_uiApp.Application.VersionNumber!="2024")throw new InvalidOperationException("Project document in Revit2024 required.");
   var sources=(spec["sourceIds"] as JArray)?.Values<long>().Distinct().OrderBy(x=>x).ToArray()??new long[0];var lights=(spec["lightIds"] as JArray)?.Values<long>().Distinct().ToArray()??new long[0];var gates=(spec["gateIds"] as JArray)?.Values<long>().Distinct().ToArray()??new long[0];if(sources.Length==0||gates.Length!=2||sources.Intersect(gates).Any())throw new ArgumentException("Explicit fences and two protected gates required.");
   bool standard=spec.Value<bool?>("standardWidth")==true,reflow=spec.Value<bool?>("reflowExisting")==true;
   string projectId=doc.ProjectInformation.UniqueId;bool apply=root.Value<bool?>("apply")==true&&root.Value<bool?>("dryRun")==false;
   if(apply&&spec.Value<string>("expectedProjectId")!=projectId)throw new InvalidOperationException("Project identity differs from preview.");
   var gateInstances=gates.Select(id=>GreenInstance(doc,id)).ToArray();if(gateInstances.Any(f=>!f.Symbol.Family.Name.Contains("大門")))throw new ArgumentException("Protected gate family names must contain 大門.");var gateBefore=gateInstances.Select(GreenTransformKey).ToArray();
   var allowedDelete=new HashSet<long>(sources.Concat(lights));var runs=new List<GreenRun>();var stamp=new StringBuilder(projectId);
   foreach(var id in sources)
   {
    var fi=GreenInstance(doc,id);bool ownGreen=reflow&&fi.Symbol.Family.Name.StartsWith("施工綠籬_")&&(fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()??"").StartsWith("AbbyGreenReplacement:");if((!ownGreen&&!fi.Symbol.Family.Name.Contains("固定式圍籬"))||fi.Pinned||fi.GroupId!=ElementId.InvalidElementId||fi.SuperComponent!=null)throw new InvalidOperationException("Source must be an unpinned, ungrouped fence owned by this operation: "+id);
    GreenDescendants(fi,allowedDelete);var t=fi.GetTransform();if(Math.Abs(t.BasisX.Z)>.0001||Math.Abs(t.BasisY.Z)>.0001||Math.Abs(t.BasisZ.Z-1)>.0001)throw new InvalidOperationException("Only upright plan fences supported: "+id);
    var bounds=GreenLocalBounds(fi);double nominal=GreenLength(fi.Symbol,"整體寬度");if(ownGreen){bounds[0]=0;bounds[1]=nominal;}else if(Math.Abs(bounds[1]-bounds[0]-nominal)>.002)throw new InvalidOperationException("Fence actual width differs from its width parameter: "+id);
    var a=t.OfPoint(new XYZ(bounds[0],0,0));var b=t.OfPoint(new XYZ(bounds[1],0,0));var axis=(b-a).Normalize();if(axis.X<-.000001||Math.Abs(axis.X)<.000001&&axis.Y<0)axis=-axis;var n=XYZ.BasisZ.CrossProduct(axis);double offset=a.DotProduct(n),z=a.Z;var back=t.BasisY*(bounds[2]+bounds[3]>=0?1:-1);double height=ownGreen?GreenLength(fi.Symbol,"地上高度"):GreenLength(fi,"整體高度");
    if(height*304.8<1800||height*304.8>3600)throw new InvalidOperationException("Fence height outside green-fence range: "+id);
    var phase=fi.get_Parameter(BuiltInParameter.PHASE_CREATED)?.AsElementId()??ElementId.InvalidElementId;
    var mid=(a+b)*.5;
    var run=runs.FirstOrDefault(r=>r.Axis.DotProduct(axis)>.999999&&r.Back.DotProduct(back)>.999999&&Math.Abs(mid.DotProduct(r.Normal)-r.Offset)<50/304.8&&Math.Abs(r.Z-z)<1/304.8&&Math.Abs(r.Height-height)<.1/304.8&&r.Level==fi.LevelId&&r.Phase==phase);
    if(run==null){run=new GreenRun{Axis=axis,Normal=n,Back=back,Offset=offset,Z=z,Height=height,Level=fi.LevelId,Phase=phase};runs.Add(run);}
    double from=Math.Min(a.DotProduct(run.Axis),b.DotProduct(run.Axis)),to=Math.Max(a.DotProduct(run.Axis),b.DotProduct(run.Axis));run.Pieces.Add(Tuple.Create(from,to));run.Sources.Add(id);stamp.Append("|").Append(id).Append(":").Append(GreenTransformKey(fi)).Append(":").Append(from.ToString("R",CultureInfo.InvariantCulture)).Append(":").Append(to.ToString("R",CultureInfo.InvariantCulture)).Append(":").Append(height.ToString("R",CultureInfo.InvariantCulture));
   }
   foreach(long id in lights){var light=GreenInstance(doc,id);if(light.Category.Id.GetIdValue()!=(long)BuiltInCategory.OST_LightingFixtures||!light.Symbol.Family.Name.Contains("圍籬警示燈"))throw new InvalidOperationException("Unexpected selected accessory: "+id);}
   var deletion=GreenProbeDeletion(doc,sources.Concat(lights).ToArray(),allowedDelete,gates);
   foreach(long id in deletion.InternalIds)allowedDelete.Add(id);
   var explicitDependents=(spec["allowedDependentIds"] as JArray)?.Values<long>().Distinct().ToArray()??new long[0];
   if(explicitDependents.Any(id=>gates.Contains(id)||!deletion.DeletedIds.Contains(id)))throw new InvalidOperationException("Additional dependent IDs must come from this deletion preview and exclude gates.");
   foreach(long id in explicitDependents)allowedDelete.Add(id);
   foreach(var item in gateBefore)stamp.Append("|").Append(item);
   foreach(long id in deletion.DeletedIds.OrderBy(id=>id))stamp.Append("|delete:").Append(id);
   foreach(var run in runs)foreach(var piece in run.Pieces.OrderBy(x=>x.Item1)){if(run.Merged.Count>0&&piece.Item1<=run.Merged.Last().Item2+50/304.8){var last=run.Merged.Last();run.Merged[run.Merged.Count-1]=Tuple.Create(last.Item1,Math.Max(last.Item2,piece.Item2));}else run.Merged.Add(piece);}
   int alignedCorners=GreenAlignRunCorners(runs);
   stamp.Append("|standard:").Append(standard).Append("|reflow:").Append(reflow);string token;using(var sha=SHA256.Create())token=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(stamp.ToString()))).Replace("-","");
   var placements=new List<GreenPlacement>();var segments=new List<object>();var blocked=new List<object>();int sharedJoints=0;
   foreach(var run in runs)
   {
    var merged=run.Merged.ToList();
    foreach(var gate in gateInstances)
    {
     var t=gate.GetTransform();if(Math.Abs(t.BasisX.DotProduct(run.Axis))<.9999||Math.Abs(t.Origin.DotProduct(run.Normal)-run.Offset)>250/304.8||Math.Abs(t.Origin.Z-run.Z)>500/304.8)continue;var bounds=GreenLocalBounds(gate);double x0=t.OfPoint(new XYZ(bounds[0],0,0)).DotProduct(run.Axis),x1=t.OfPoint(new XYZ(bounds[1],0,0)).DotProduct(run.Axis);double cut0=Math.Min(x0,x1),cut1=Math.Max(x0,x1);var remaining=new List<Tuple<double,double>>();foreach(var piece in merged){if(cut1<=piece.Item1||cut0>=piece.Item2){remaining.Add(piece);continue;}if(cut0-piece.Item1>.5/304.8)remaining.Add(Tuple.Create(piece.Item1,cut0));if(piece.Item2-cut1>.5/304.8)remaining.Add(Tuple.Create(cut1,piece.Item2));}merged=remaining;
    }
    foreach(var piece in merged)
    {
     if(standard)
     {
      double length=(piece.Item2-piece.Item1)*304.8;int full=(int)Math.Floor((length+.00001)/1100);double tail=length-full*1100;int unitCount=full+(tail>.05?1:0);
      for(int i=0;i<unitCount;i++){double size=i<full?1100:tail;bool filler=size<300;placements.Add(new GreenPlacement{Run=run,From=piece.Item1+i*1100/304.8,Width=size/304.8,IsFiller=filler,ShowLeft=!filler,ShowRight=!filler});}
      sharedJoints+=Math.Max(0,unitCount-1);segments.Add(new{StartMm=PointMm(run.Axis*piece.Item1+run.Normal*run.Offset+XYZ.BasisZ*run.Z),EndMm=PointMm(run.Axis*piece.Item2+run.Normal*run.Offset+XYZ.BasisZ*run.Z),Count=unitCount,StandardCount=full,StandardWidthMm=1100,LastWidthMm=tail,Filler=tail>.05&&tail<300,HeightMm=run.Height*304.8,SourceIds=run.Sources});continue;
     }
     double mm=(piece.Item2-piece.Item1)*304.8;int count=Math.Max(1,(int)Math.Round(mm/1100));if(mm/count<1000-.05)count=Math.Max(1,(int)Math.Floor(mm/1000));double width=mm/count;if(width<1000-.05||width>2200+.05){blocked.Add(new{LengthMm=mm,SourceIds=run.Sources});continue;}
     for(int i=0;i<count;i++)placements.Add(new GreenPlacement{Run=run,From=piece.Item1+(piece.Item2-piece.Item1)*i/count,Width=(piece.Item2-piece.Item1)/count});sharedJoints+=count-1;segments.Add(new{StartMm=PointMm(run.Axis*piece.Item1+run.Normal*run.Offset+XYZ.BasisZ*run.Z),EndMm=PointMm(run.Axis*piece.Item2+run.Normal*run.Offset+XYZ.BasisZ*run.Z),Count=count,WidthMm=width,HeightMm=run.Height*304.8,SourceIds=run.Sources});
    }
   }
   int cornerBraces=0;for(int i=0;i<placements.Count;i++)for(int j=i+1;j<placements.Count;j++)
   {var a=placements[i];var b=placements[j];if(Math.Abs(a.Run.Axis.DotProduct(b.Run.Axis))>.9999)continue;foreach(bool ae in new[]{false,true})foreach(bool be in new[]{false,true})if((a.Point+(ae?a.Run.Axis*a.Width:XYZ.Zero)).DistanceTo(b.Point+(be?b.Run.Axis*b.Width:XYZ.Zero))*304.8<.5){if(!a.IsFiller&&!b.IsFiller){if(be)b.ShowRight=false;else b.ShowLeft=false;}cornerBraces++;}}
   var unexpected=deletion.DeletedIds.Where(id=>!allowedDelete.Contains(id)).ToArray();
   var plan=new{ProjectId=projectId,PreviewToken=token,SourceCount=sources.Length,SelectedLights=lights.Length,PreservedGateIds=gates,Planned=placements.Count,SharedBraceJoints=sharedJoints,AlignedCorners=alignedCorners,SingleBraceCorners=cornerBraces,AlignmentToleranceMm=50,CornerTrimLimitMm=500,Segments=segments,Blocked=blocked,DeletionPreview=new{Total=deletion.DeletedIds.Count,AutomaticInternalIds=deletion.InternalIds,Unexpected=deletion.Metadata.Where(m=>unexpected.Contains((long)m["Id"])).ToArray()}};
   if(!apply)return new{Success=true,DryRun=true,Plan=plan};if(spec.Value<string>("previewToken")!=token)throw new InvalidOperationException("Sources, gates or dependencies changed since preview.");if(blocked.Count>0||placements.Count==0||unexpected.Length>0)throw new InvalidOperationException("Layout/dependencies require review; original fences preserved. Unexpected IDs: "+string.Join(",",unexpected));
   string familyFile=spec.Value<string>("familyFile"),fillerFile=spec.Value<string>("fillerFile");string familyName=standard?"施工綠籬_標準收尾":"施工綠籬_緊密排列";if(!File.Exists(familyFile)||!Path.IsPathRooted(familyFile)||Path.GetFileNameWithoutExtension(familyFile)!=familyName)throw new ArgumentException("Verified green family RFA required.");if(placements.Any(x=>x.IsFiller)&&(!File.Exists(fillerFile)||!Path.IsPathRooted(fillerFile)||Path.GetFileNameWithoutExtension(fillerFile)!="施工綠籬_窄收邊條"))throw new ArgumentException("Verified narrow closure RFA required.");
   var existingWarnings=new HashSet<string>(doc.GetWarnings().Select(GreenWarningKey));
   var placed=new List<Tuple<FamilyInstance,GreenPlacement>>();var createdTypes=new Dictionary<string,FamilySymbol>();Document familyDoc=null;var deletedIds=new List<long>();
   using(var group=new TransactionGroup(doc,"Replace selected fences with tightly arranged green fence"))
   {
    group.Start();try
    {
     var family=new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().SingleOrDefault(f=>f.Name==familyName);if(family==null){familyDoc=_uiApp.Application.OpenDocumentFile(familyFile);family=familyDoc.LoadFamily(doc,new ConstraintFamilyLoad());familyDoc.Close(false);familyDoc=null;}if(family==null)throw new InvalidOperationException("Cannot load green fence.");var baseType=(FamilySymbol)doc.GetElement(family.GetFamilySymbolIds().First());FamilySymbol fillerType=null;
     if(placements.Any(x=>x.IsFiller)){var filler=new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().SingleOrDefault(f=>f.Name=="施工綠籬_窄收邊條");if(filler==null){familyDoc=_uiApp.Application.OpenDocumentFile(fillerFile);filler=familyDoc.LoadFamily(doc,new ConstraintFamilyLoad());familyDoc.Close(false);familyDoc=null;}fillerType=(FamilySymbol)doc.GetElement(filler.GetFamilySymbolIds().First());}
     using(var tx=GateTransaction(doc,"Batch create green fences and replace selected sources"))
     {
      tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new GreenExistingWarnings(existingWarnings)));
      foreach(var item in placements)
      {
       string key=(item.Width*304.8).ToString("0.000000",CultureInfo.InvariantCulture)+"_"+(item.Run.Height*304.8).ToString("0.000000",CultureInfo.InvariantCulture);
       if(!createdTypes.TryGetValue(key,out var symbol)){symbol=(FamilySymbol)(item.IsFiller?fillerType:baseType).Duplicate("Abby_"+token.Substring(0,8)+"_"+key);symbol.LookupParameter("整體寬度").Set(item.Width);symbol.LookupParameter("地上高度").Set(item.Run.Height);symbol.Activate();createdTypes.Add(key,symbol);}
      }
      doc.Regenerate();
      foreach(var item in placements)
      {
       string key=(item.Width*304.8).ToString("0.000000",CultureInfo.InvariantCulture)+"_"+(item.Run.Height*304.8).ToString("0.000000",CultureInfo.InvariantCulture);var level=doc.GetElement(item.Run.Level) as Level??throw new InvalidOperationException("Source level missing.");var point=item.RootPoint;
       var fi=doc.Create.NewFamilyInstance(new XYZ(point.X,point.Y,point.Z-level.ProjectElevation),createdTypes[key],level,StructuralType.NonStructural);fi.LookupParameter("左端斜撐").Set(item.LocalLeft?1:0);fi.LookupParameter("右端斜撐").Set(item.LocalRight?1:0);fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("AbbyGreenReplacement:"+token);if(item.Run.Phase!=ElementId.InvalidElementId){var phase=fi.get_Parameter(BuiltInParameter.PHASE_CREATED);if(phase!=null&&!phase.IsReadOnly)phase.Set(item.Run.Phase);}placed.Add(Tuple.Create(fi,item));
      }
      doc.Regenerate();
      foreach(var pair in placed)
      {
       var fi=pair.Item1;var item=pair.Item2;var transform=fi.GetTransform();var delta=item.RootPoint-transform.Origin;if(delta.GetLength()>1e-8)ElementTransformUtils.MoveElement(doc,fi.Id,delta);double angle=Math.Atan2(transform.BasisX.CrossProduct(item.RootAxis).Z,transform.BasisX.DotProduct(item.RootAxis));if(Math.Abs(angle)>1e-9)ElementTransformUtils.RotateElement(doc,fi.Id,Line.CreateBound(item.RootPoint,item.RootPoint+XYZ.BasisZ),angle);
      }
      doc.Regenerate();GreenVerifyReplacement(doc,placed,gateInstances,gateBefore);
      var toDelete=sources.Concat(lights).Where(id=>doc.GetElement(GreenId(id))!=null).Select(GreenId).ToList();var deleted=doc.Delete(toDelete);deletedIds=deleted.Select(id=>(long)id.GetIdValue()).ToList();if(deletedIds.Any(id=>!allowedDelete.Contains(id)))throw new InvalidOperationException("Deleting selected sources would also delete unselected dependent elements; rolled back.");
      GateCommit(tx);
     }
     GreenVerifyReplacement(doc,placed,gateInstances,gateBefore);if(sources.Any(id=>doc.GetElement(GreenId(id))!=null))throw new InvalidOperationException("Some original sources remain.");group.Assimilate();
    }
    catch{if(familyDoc!=null&&familyDoc.IsValidObject)familyDoc.Close(false);group.RollBack();throw;}
   }
   _uiApp.ActiveUIDocument.Selection.SetElementIds(placed.Select(p=>p.Item1.Id).ToList());return new{Success=true,DryRun=false,Plan=plan,Updated=placed.Count,Deleted=deletedIds,NewElementIds=placed.Select(p=>p.Item1.Id.GetIdValue()).ToArray(),GatesUnchanged=true,JointGapToleranceMm=.5,SharedBracesOverlap=true};
  }
  private static ElementId GreenId(long id)
  {
#if REVIT2025_OR_GREATER
   return new ElementId(id);
#else
   return new ElementId(checked((int)id));
#endif
  }
  private static string GreenWarningKey(FailureMessage m)=>m.GetFailureDefinitionId().Guid+"|"+m.GetDescriptionText()+"|"+string.Join(",",m.GetFailingElements().Select(id=>id.GetIdValue()).OrderBy(id=>id));
  private sealed class GreenExistingWarnings:IFailuresPreprocessor
  {
   private readonly HashSet<string> baseline;internal GreenExistingWarnings(HashSet<string> values){baseline=values;}
   public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
   {
    foreach(var m in accessor.GetFailureMessages())
    {
     if(m.GetSeverity()==FailureSeverity.Error)return FailureProcessingResult.ProceedWithRollBack;
     string key=m.GetFailureDefinitionId().Guid+"|"+m.GetDescriptionText()+"|"+string.Join(",",m.GetFailingElementIds().Select(id=>id.GetIdValue()).OrderBy(id=>id));
     if(m.GetSeverity()==FailureSeverity.Warning&&baseline.Contains(key))accessor.DeleteWarning(m);
    }
    return FailureProcessingResult.Continue;
   }
  }
  private sealed class GreenDeletionProbe
  {internal List<long> DeletedIds=new List<long>(),InternalIds=new List<long>();internal List<JObject> Metadata=new List<JObject>();}
  private static GreenDeletionProbe GreenProbeDeletion(Document doc,long[] requested,HashSet<long> allowed,long[] protectedIds)
  {
   var result=new GreenDeletionProbe();
   using(var tx=GateTransaction(doc,"Read-only replacement dependency probe (rollback)"))
   {result.DeletedIds=doc.Delete(requested.Where(id=>doc.GetElement(GreenId(id))!=null).Select(GreenId).ToList()).Select(id=>(long)id.GetIdValue()).ToList();tx.RollBack();}
   if(result.DeletedIds.Intersect(protectedIds).Any())throw new InvalidOperationException("Source deletion would affect a protected gate.");
   var all=new HashSet<long>(result.DeletedIds);
   foreach(long id in result.DeletedIds.Where(id=>!allowed.Contains(id)))
   {
    var e=doc.GetElement(GreenId(id))??throw new InvalidOperationException("Dependency probe rollback failed.");bool automatic=false;var referenceIds=new List<long>();
    if(e is Dimension dimension && e.OwnerViewId==ElementId.InvalidElementId && e.Category!=null && e.Category.Id.GetIdValue()==(long)BuiltInCategory.OST_Constraints)
    {
     try{referenceIds=dimension.References.Cast<Reference>().Select(r=>(long)r.ElementId.GetIdValue()).Distinct().ToList();automatic=referenceIds.Any(allowed.Contains)&&referenceIds.All(r=>allowed.Contains(r)||doc.GetElement(GreenId(r)) is Level);}catch{automatic=false;}
    }
    if(e is Sketch||e is SketchPlane)
    {var dependents=e.GetDependentElements(null).Select(d=>(long)d.GetIdValue()).Where(d=>d!=id).ToList();automatic=dependents.Count>0&&dependents.All(all.Contains);referenceIds=dependents;}
    if(automatic)result.InternalIds.Add(id);
    result.Metadata.Add(new JObject{["Id"]=id,["Class"]=e.GetType().FullName,["Name"]=e.Name,["Category"]=e.Category?.Name,["OwnerViewId"]=e.OwnerViewId.GetIdValue(),["References"]=new JArray(referenceIds),["AutomaticOwnedInternal"]=automatic});
   }
   return result;
  }
  private static int GreenAlignRunCorners(List<GreenRun> runs)
  {
   int count=0;for(int i=0;i<runs.Count;i++)for(int j=i+1;j<runs.Count;j++)
   {
    var a=runs[i];var b=runs[j];if(Math.Abs(a.Z-b.Z)>1/304.8||a.Level!=b.Level||a.Phase!=b.Phase)continue;double den=a.Axis.DotProduct(b.Normal);if(Math.Abs(den)<.1)continue;
    double ta=(b.Offset-a.Offset*a.Normal.DotProduct(b.Normal))/den;var point=a.Axis*ta+a.Normal*a.Offset;double tb=point.DotProduct(b.Axis);int ia=-1,ib=-1,ea=-1,eb=-1;double da=501/304.8,db=501/304.8;
    for(int k=0;k<a.Merged.Count;k++)for(int e=0;e<2;e++){double distance=Math.Abs((e==0?a.Merged[k].Item1:a.Merged[k].Item2)-ta);if(distance<da){da=distance;ia=k;ea=e;}}
    for(int k=0;k<b.Merged.Count;k++)for(int e=0;e<2;e++){double distance=Math.Abs((e==0?b.Merged[k].Item1:b.Merged[k].Item2)-tb);if(distance<db){db=distance;ib=k;eb=e;}}
    if(da<=500/304.8&&db<=500/304.8){var pa=a.Merged[ia];var pb=b.Merged[ib];var na=ea==0?Tuple.Create(ta,pa.Item2):Tuple.Create(pa.Item1,ta);var nb=eb==0?Tuple.Create(tb,pb.Item2):Tuple.Create(pb.Item1,tb);if(na.Item2-na.Item1<1000/304.8||nb.Item2-nb.Item1<1000/304.8)continue;a.Merged[ia]=na;b.Merged[ib]=nb;count++;}
   }return count;
  }
  private static FamilyInstance GreenInstance(Document d,long id)=>d.GetElement(GreenId(id)) as FamilyInstance??throw new InvalidOperationException("Family instance missing: "+id);
  private static double GreenLength(Element e,string name)
  {var ps=e.GetParameters(name).Where(p=>p.StorageType==StorageType.Double).ToList();if(ps.Count!=1)throw new InvalidOperationException("Unique length parameter required: "+e.Id+" / "+name);return ps[0].AsDouble();}
  private static void GreenDescendants(FamilyInstance fi,HashSet<long> ids){foreach(var id in fi.GetSubComponentIds())if(ids.Add(id.GetIdValue())&&fi.Document.GetElement(id) is FamilyInstance child)GreenDescendants(child,ids);}
  private static string GreenTransformKey(FamilyInstance fi){var t=fi.GetTransform();return fi.Id.GetIdValue()+"/"+fi.Symbol.Id.GetIdValue()+"/"+string.Join("/",new[]{t.Origin,t.BasisX,t.BasisY,t.BasisZ}.SelectMany(p=>new[]{p.X,p.Y,p.Z}).Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));}
  private static double[] GreenLocalBounds(FamilyInstance fi)
  {var points=new List<XYZ>();GatherConstraintVertices(fi.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}),points);if(points.Count==0)throw new InvalidOperationException("Source solid geometry unavailable: "+fi.Id);var inverse=fi.GetTransform().Inverse;var local=points.Select(inverse.OfPoint).ToList();return new[]{local.Min(p=>p.X),local.Max(p=>p.X),local.Min(p=>p.Y),local.Max(p=>p.Y)};}
  private static void GreenVerifyReplacement(Document doc,List<Tuple<FamilyInstance,GreenPlacement>> placed,FamilyInstance[] gates,string[] before)
  {
   for(int i=0;i<gates.Length;i++)if(!gates[i].IsValidObject||GreenTransformKey(gates[i])!=before[i])throw new InvalidOperationException("Protected gate changed.");
   foreach(var pair in placed)
   {var fi=pair.Item1;var item=pair.Item2;var t=fi.GetTransform();double position=t.Origin.DistanceTo(item.RootPoint)*304.8,axis=t.BasisX.DotProduct(item.RootAxis),back=t.BasisY.DotProduct(item.Run.Back),width=Math.Abs(GreenLength(fi.Symbol,"整體寬度")-item.Width)*304.8,height=Math.Abs(GreenLength(fi.Symbol,"地上高度")-item.Run.Height)*304.8;int left=fi.LookupParameter("左端斜撐").AsInteger(),right=fi.LookupParameter("右端斜撐").AsInteger();
    if(position>.5||axis<.999999||back<.999999||width>.1||height>.1||left!=(item.LocalLeft?1:0)||right!=(item.LocalRight?1:0))throw new InvalidOperationException("Green fence verification "+fi.Id+": positionErrorMm="+position+", axisDot="+axis+", backDot="+back+", widthErrorMm="+width+", heightErrorMm="+height+", left="+left+"/"+item.LocalLeft+", right="+right+"/"+item.LocalRight+", originMm="+string.Join(",",PointMm(t.Origin))+", targetMm="+string.Join(",",PointMm(item.RootPoint)));}
   foreach(var group in placed.GroupBy(p=>p.Item2.Run))
   {var sorted=group.OrderBy(p=>p.Item2.From).ToList();for(int i=1;i<sorted.Count;i++){var a=sorted[i-1].Item2;var b=sorted[i].Item2;double gap=(b.From-a.From-a.Width)*304.8;if(Math.Abs(gap)<1){var ta=sorted[i-1].Item1.GetTransform();var tb=sorted[i].Item1.GetTransform();double ax=a.Reverse?0:a.Width,bx=b.Reverse?b.Width:0;var pa=ta.OfPoint(new XYZ(ax,0,0));var pb=tb.OfPoint(new XYZ(bx,0,0));if(pa.DistanceTo(pb)*304.8>.5)throw new InvalidOperationException("Joint does not close.");var ca=ta.OfPoint(new XYZ(ax,800/304.8,a.Run.Height*.55/2));var cb=tb.OfPoint(new XYZ(bx,800/304.8,b.Run.Height*.55/2));if(ca.DistanceTo(cb)*304.8>.5)throw new InvalidOperationException("Shared brace planes differ.");}}}
  }
 }
}
