using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
namespace RevitMCP.Core
{
 public partial class CommandExecutor
 {
  private object ExportSharedSiteGate(JObject p,JObject spec)
  {
   string template=spec.Value<string>("templatePath"),folder=p.Value<string>("outputFolder");
   if(_uiApp.Application.VersionNumber!="2024"||!File.Exists(template)||!Path.IsPathRooted(template)||!Path.IsPathRooted(folder))throw new ArgumentException("Revit2024 and absolute paths required.");
   double width=FenceDimension(spec,"widthMm",12000,6000,16000),height=FenceDimension(spec,"heightMm",5000,3000,6000);
   string output=Path.Combine(folder,"工地大門_共用單元連動版.rfa");if(File.Exists(output))throw new IOException("Output exists; choose a new folder.");
   if(p.Value<bool?>("apply")!=true||p.Value<bool?>("dryRun")!=false)return new{DryRun=true,OutputPath=output,SharedPanelCount=29,SharedDiagonalCount=14,ControlParameters=10,CalculatedParameters=5};
   string stage=Path.Combine(Path.GetTempPath(),"AbbySharedGate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);Document host=null,child=null;
   try
   {
    host=_uiApp.Application.NewFamilyDocument(template);SharedGateGeometry g;ReferencePlane left,right,high,diagonalLow,diagonalDown,y10;ReferencePlane[] bays,braces;
    using(var tx=GateTransaction(host,"Shared gate reference framework"))
    {
     host.OwnerFamily.FamilyCategory=Category.GetCategory(host,BuiltInCategory.OST_GenericModel);host.FamilyManager.NewType("共用尺寸");g=new SharedGateGeometry(host);
     foreach(var item in new[]{Tuple.Create("整體寬度",width),Tuple.Create("整體高度",height),Tuple.Create("柱寬",200.0),Tuple.Create("柱深",150.0),Tuple.Create("桁架高度",300.0),Tuple.Create("桁架弦高",35.0),Tuple.Create("框料寬",20.0),Tuple.Create("框料深",40.0),Tuple.Create("鐵皮厚度",1.2),Tuple.Create("鐵皮頂高",2000.0)})g.Input(item.Item1,item.Item2);
     g.Input("門洞淨寬",0,"整體寬度 - 2 * 柱寬");g.Input("門片寬",0,"門洞淨寬 / 29");g.Input("門片高",0,"整體高度 - 桁架高度 - 20 mm");g.Input("斜材跨距",0,"門洞淨寬 / 14");g.Input("斜材高差",0,"桁架高度 - 2 * 桁架弦高");
     var end=g.Offset(0,g.X0,width,host.FamilyManager.get_Parameter("整體寬度"));high=g.Offset(2,g.Z0,height,host.FamilyManager.get_Parameter("整體高度"));
     left=g.Offset(0,g.X0,200,host.FamilyManager.get_Parameter("柱寬"));right=g.Offset(0,end,-200,host.FamilyManager.get_Parameter("柱寬"));
     var y0=g.Offset(1,g.Y0,-75);var y1=g.Offset(1,y0,150,host.FamilyManager.get_Parameter("柱深"));
     var m=Material.Create(host,"大門_鍍鋅鋼材");((Material)host.GetElement(m)).Color=new Color(170,180,185);
     g.Box(g.X0,left,y0,y1,g.Z0,high,m);g.Box(right,end,y0,y1,g.Z0,high,m);
     var chordY0=g.Offset(1,g.Y0,-35);var chordY1=g.Offset(1,chordY0,70);
     var topBottom=g.Offset(2,high,-35,host.FamilyManager.get_Parameter("桁架弦高"));var bottom=g.Offset(2,high,-300,host.FamilyManager.get_Parameter("桁架高度"));diagonalLow=g.Offset(2,bottom,35,host.FamilyManager.get_Parameter("桁架弦高"));diagonalDown=g.Offset(2,diagonalLow,15);y10=g.Offset(1,g.Y0,10);
     g.Box(left,right,chordY0,chordY1,topBottom,high,m);g.Box(left,right,chordY0,chordY1,bottom,diagonalLow,m);
     bays=g.Grid(left,right,29);braces=g.Grid(left,right,14);
     GateCommit(tx);
    }
    var families=new List<Family>();
    foreach(bool diagonal in new[]{false,true})
    {
     child=_uiApp.Application.NewFamilyDocument(template);
     using(var tx=GateTransaction(child,"Build reusable gate unit"))
     {
      child.OwnerFamily.FamilyCategory=Category.GetCategory(child,BuiltInCategory.OST_GenericModel);child.FamilyManager.NewType("標準");var u=new SharedGateGeometry(child);
      u.X0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Left);
      u.Y0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Front);
      u.Z0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Bottom);
      var uw=u.Input("單元寬",diagonal?(width-400)/14:(width-400)/29);var uh=u.Input("單元高",diagonal?230:height-320);
      var xe=u.Offset(0,u.X0,u.Value("單元寬"),uw);var zt=u.Offset(2,u.Z0,u.Value("單元高"),uh);var metal=Material.Create(child,"大門_鍍鋅鋼材");((Material)child.GetElement(metal)).Color=new Color(170,180,185);
      if(diagonal)u.Diagonal(xe,zt,metal);
      else
      {
       var fw=u.Input("框料寬",20);var fd=u.Input("框料深",40);var st=u.Input("鐵皮厚度",1.2);var sh=u.Input("鐵皮頂高",2000);
       var a=u.Offset(0,u.X0,3);var b=u.Offset(0,a,20,fw);var c=u.Offset(0,xe,-3);var e=u.Offset(0,c,-20,fw);
       var ya=u.Offset(1,u.Y0,-20);var yb=u.Offset(1,ya,40,fd);var z0=u.Offset(2,u.Z0,20);var z1=u.Offset(2,z0,30);var z2=u.Offset(2,zt,-30);var zs=u.Offset(2,u.Z0,2000,sh);var sy=u.Offset(1,u.Y0,-1.2,st);
       u.Box(a,b,ya,yb,z0,zt,metal);u.Box(e,c,ya,yb,z0,zt,metal);u.Box(b,e,ya,yb,z0,z1,metal);u.Box(b,e,ya,yb,z2,zt,metal);u.Box(b,e,sy,u.Y0,z1,zs,metal);
      }
      GateCommit(tx);
     }
     string path=Path.Combine(stage,diagonal?"共用桁架斜材.rfa":"共用門片.rfa");child.SaveAs(path,new SaveAsOptions{OverwriteExistingFile=false});var loaded=child.LoadFamily(host,new ConstraintFamilyLoad());families.Add(loaded??throw new InvalidOperationException("Shared unit load failed."));child.Close(false);child=null;
    }
    using(var tx=GateTransaction(host,"Place shared units with equal spacing"))
    {
     var panel=(FamilySymbol)host.GetElement(families[0].GetFamilySymbolIds().Single());var diagonal=(FamilySymbol)host.GetElement(families[1].GetFamilySymbolIds().Single());panel.Activate();diagonal.Activate();
     foreach(var pair in new[]{Tuple.Create(panel,"單元寬","門片寬"),Tuple.Create(panel,"單元高","門片高"),Tuple.Create(panel,"框料寬","框料寬"),Tuple.Create(panel,"框料深","框料深"),Tuple.Create(panel,"鐵皮厚度","鐵皮厚度"),Tuple.Create(panel,"鐵皮頂高","鐵皮頂高"),Tuple.Create(diagonal,"單元寬","斜材跨距"),Tuple.Create(diagonal,"單元高","斜材高差")})host.FamilyManager.AssociateElementParameterToFamilyParameter(pair.Item1.LookupParameter(pair.Item2),host.FamilyManager.get_Parameter(pair.Item3));
     for(int i=0;i<29;i++)PlaceGateUnit(host,g,panel,bays[i],g.Y0,g.Z0,false);
     for(int i=0;i<14;i++)PlaceGateUnit(host,g,diagonal,braces[i%2==0?i:i+1],i%2==0?g.Y0:y10,i%2==0?diagonalLow:diagonalDown,i%2!=0);
     GateCommit(tx);
    }
    var tests=new[]{new[]{10000.0,4000.0},new[]{14000.0,5500.0},new[]{width,height}};
    foreach(var size in tests)using(var tx=GateTransaction(host,"Flex all shared gate components")){host.FamilyManager.Set(host.FamilyManager.get_Parameter("整體寬度"),size[0]/304.8);host.FamilyManager.Set(host.FamilyManager.get_Parameter("整體高度"),size[1]/304.8);host.Regenerate();VerifySharedGate(host,size[0],size[1]);GateCommit(tx);}
    if(host.FamilyManager.Parameters.Cast<FamilyParameter>().Count(fp=>fp.Id.GetIdValue()>0)!=15)throw new InvalidOperationException("Unexpected user parameter count.");
    var candidate=Path.Combine(stage,"工地大門_共用單元連動版.rfa");host.SaveAs(candidate,new SaveAsOptions{OverwriteExistingFile=false});host.Close(false);host=null;host=_uiApp.Application.OpenDocumentFile(candidate);
    foreach(var size in tests)using(var tx=GateTransaction(host,"Flex reopened shared gate")){host.FamilyManager.Set(host.FamilyManager.get_Parameter("整體寬度"),size[0]/304.8);host.FamilyManager.Set(host.FamilyManager.get_Parameter("整體高度"),size[1]/304.8);host.Regenerate();VerifySharedGate(host,size[0],size[1]);tx.RollBack();}
    host.Close(false);host=null;Directory.CreateDirectory(folder);File.Copy(candidate,output,false);
    return new{Success=true,OutputPath=output,ControlParameters=10,CalculatedParameters=5,SharedPanelCount=29,SharedDiagonalCount=14,ComponentCount=163,FlexTests=3,ReopenedFlexTests=3,OriginalUnchanged=true,HostProjectModified=false,ClosedAppearanceOnly=true};
   }
   catch(Exception ex)
   {
    string diagnostic=Path.Combine(folder,"diagnostics",Guid.NewGuid().ToString("N"));
    try{Directory.CreateDirectory(diagnostic);foreach(var file in Directory.EnumerateFiles(stage,"*.rfa"))File.Copy(file,Path.Combine(diagnostic,Path.GetFileName(file)),false);if(host!=null&&host.IsValidObject&&!host.IsModifiable)host.SaveAs(Path.Combine(diagnostic,"未驗證主體.rfa"),new SaveAsOptions{OverwriteExistingFile=false});File.WriteAllText(Path.Combine(diagnostic,"error.txt"),ex.ToString());}catch{}
    throw new InvalidOperationException(ex.Message+" | Diagnostic folder: "+diagnostic,ex);
   }
   finally{if(child!=null&&child.IsValidObject)child.Close(false);if(host!=null&&host.IsValidObject)host.Close(false);foreach(var file in Directory.EnumerateFiles(stage))File.Delete(file);if(!Directory.EnumerateFileSystemEntries(stage).Any())Directory.Delete(stage);}
  }
  private static Transaction GateTransaction(Document d,string name){var tx=new Transaction(d,name);tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FenceParametricBuilder.FenceFailures()).SetClearAfterRollback(true));return tx;}
  private static void GateCommit(Transaction tx){if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Gate native constraints rolled back: "+tx.GetName());}
  private static void PlaceGateUnit(Document d,SharedGateGeometry g,FamilySymbol s,ReferencePlane x,ReferencePlane y,ReferencePlane z,bool reversed)
  {
   var fi=d.FamilyCreate.NewFamilyInstance(new XYZ(g.Pos(x),g.Pos(y),g.Pos(z))/304.8,s,StructuralType.NonStructural);
   if(reversed)ElementTransformUtils.RotateElement(d,fi.Id,Line.CreateBound(((LocationPoint)fi.Location).Point,((LocationPoint)fi.Location).Point+XYZ.BasisZ),Math.PI);
   d.Regenerate();
   var desired=new XYZ(g.Pos(x),g.Pos(y),g.Pos(z))/304.8;
   var delta=desired-fi.GetTransform().Origin;
   if(delta.GetLength()>1e-9){ElementTransformUtils.MoveElement(d,fi.Id,delta);d.Regenerate();}
   var axes=new[]{Tuple.Create(g.Front,x,FamilyInstanceReferenceType.Left),Tuple.Create(g.Plan,y,FamilyInstanceReferenceType.Front),Tuple.Create(g.Front,z,FamilyInstanceReferenceType.Bottom)};
   foreach(var item in axes)
   {
    var refs=fi.GetReferences(item.Item3);
    if(refs==null||refs.Count!=1)throw new InvalidOperationException("Shared unit "+s.Family.Name+" reference "+item.Item3+" count="+(refs==null?0:refs.Count));
    try{d.FamilyCreate.NewAlignment(item.Item1,item.Item2.GetReference(),refs[0]);d.Regenerate();}
    catch(Exception ex){throw new InvalidOperationException("Shared unit "+s.Family.Name+" alignment "+item.Item3+", target="+g.Pos(item.Item2)+"mm, origin="+string.Join(",",PointMm(fi.GetTransform().Origin))+": "+ex.Message,ex);}
   }
  }
  private static void VerifySharedGate(Document d,double w,double h)
  {
   var actual=new List<double[]>();foreach(var e in new FilteredElementCollector(d).WhereElementIsNotElementType().Where(e=>e is GenericForm||e is FamilyInstance))CollectGateSolids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}),actual);
   var expected=new List<double[]>();Action<double,double,double,double,double,double> add=(a,b,c,e,f,g)=>expected.Add(new[]{a,b,c,e,f,g});
   add(0,200,-75,75,0,h);add(w-200,w,-75,75,0,h);add(200,w-200,-35,35,h-35,h);add(200,w-200,-35,35,h-300,h-265);
   for(int i=0;i<29;i++){double a=200+(w-400)*i/29,b=200+(w-400)*(i+1)/29;add(a+3,a+23,-20,20,20,h-320);add(b-23,b-3,-20,20,20,h-320);add(a+23,b-23,-20,20,20,50);add(a+23,b-23,-20,20,h-350,h-320);add(a+23,b-23,-1.2,0,50,2000);}
   for(int i=0;i<14;i++)add(200+(w-400)*i/14,200+(w-400)*(i+1)/14,0,10,i%2==0?h-280:h-265,i%2==0?h-35:h-20);
   if(actual.Count!=163)throw new InvalidOperationException("Expected 163 solids, got "+actual.Count);
   foreach(var bounds in expected){int match=actual.FindIndex(b=>b.Zip(bounds,(x,y)=>Math.Abs(x-y)).Max()<.5);if(match<0)throw new InvalidOperationException("Shared gate component flex mismatch: "+string.Join(",",bounds));actual.RemoveAt(match);}
  }
  private static void CollectGateSolids(GeometryElement geo,List<double[]> result)
  {if(geo==null)return;foreach(var item in geo){if(item is GeometryInstance gi)CollectGateSolids(gi.GetInstanceGeometry(),result);else if(item is Solid solid&&solid.Volume>1e-12){var pts=solid.Faces.Cast<Face>().SelectMany(f=>f.Triangulate().Vertices).ToArray();result.Add(new[]{pts.Min(p=>p.X)*304.8,pts.Max(p=>p.X)*304.8,pts.Min(p=>p.Y)*304.8,pts.Max(p=>p.Y)*304.8,pts.Min(p=>p.Z)*304.8,pts.Max(p=>p.Z)*304.8});}}}
 }
}
