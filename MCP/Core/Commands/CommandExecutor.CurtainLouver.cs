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
  private object ExportCurtainLouver(JObject p,JObject spec)
  {
   string template=spec.Value<string>("templatePath"),ct=spec.Value<string>("curtainTemplatePath"),folder=p.Value<string>("outputFolder");
   if(_uiApp.Application.VersionNumber!="2024"||!Path.IsPathRooted(folder)||!File.Exists(template)||!File.Exists(ct))throw new ArgumentException("Revit2024 and existing generic-model/curtain-window templates required.");
   double width=FenceDimension(spec,"widthMm",1125,500,3000),height=FenceDimension(spec,"heightMm",3650,1200,6000);
   string coreName="百葉窗_可調寬高",adapterName="帷幕百葉窗_進退可調";string outputCore=Path.Combine(folder,coreName+".rfa"),outputAdapter=Path.Combine(folder,adapterName+".rfa");
   if(File.Exists(outputCore)||File.Exists(outputAdapter))throw new IOException("Output exists; choose another output folder.");
   if(p.Value<bool?>("apply")!=true||p.Value<bool?>("dryRun")!=false)return new{DryRun=true,Core=outputCore,CurtainWindow=outputAdapter,WidthMm=width,HeightMm=height,BladeCount=24,BladeAngleDegrees=45,BladeDepthMm=120,BladeThicknessMm=3,DefaultPitchMm=(height-250)/23};
   string staging=Path.Combine(Path.GetTempPath(),"AbbyLouver-"+Guid.NewGuid().ToString("N")),stage="begin";Directory.CreateDirectory(staging);Document core=null,blade=null,adapter=null,project=null;
   var cases=new[]{new[]{900.0,2500.0},new[]{1500.0,4200.0},new[]{width,height}};
   try
   {
    stage="blade unit";blade=_uiApp.Application.NewFamilyDocument(template);
    using(var tx=GateTransaction(blade,stage))
    {
     blade.FamilyManager.NewType("45度_120x3");var u=new SharedGateGeometry(blade);u.X0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Left);u.Y0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Front);u.Z0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Bottom);
     var span=u.Input("葉片長",width-100,null,true);var material=Material.Create(blade,"百葉_鋁材");((Material)blade.GetElement(material)).Color=new Color(130,140,150);
     double c=Math.Sqrt(.5);FenceExtrusion(blade,XYZ.Zero,new XYZ(0,c,c),new XYZ(0,-c,c),XYZ.BasisX,new[]{new UV(-60,-1.5),new UV(60,-1.5),new UV(60,1.5),new UV(-60,1.5)},width-100,material);
     var e=new FilteredElementCollector(blade).OfClass(typeof(Extrusion)).Cast<Extrusion>().Single();blade.FamilyManager.AssociateElementParameterToFamilyParameter(e.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM),span);GateCommit(tx);
    }
    blade.SaveAs(Path.Combine(staging,"共用百葉片.rfa"),new SaveAsOptions{OverwriteExistingFile=false});
    stage="core framework";core=_uiApp.Application.NewFamilyDocument(template);SharedGateGeometry g;ReferencePlane left,right;ReferencePlane[] rows;
    using(var tx=GateTransaction(core,stage))
    {
     core.FamilyManager.NewType("1125x3650");g=new SharedGateGeometry(core);g.X0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Left);g.Y0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Front);g.Z0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Bottom);
     var w=g.Input("窗寬",width,null,true);var h=g.Input("窗高",height,null,true);var fw=g.Input("框寬",50);var fd=g.Input("框深",100);g.Input("葉片淨長",0,"窗寬 - 2 * 框寬",true);
     var note=core.FamilyManager.AddParameter("設計說明",GroupTypeId.IdentityData,SpecTypeId.String.Text,false);core.FamilyManager.Set(note,"24片等距百葉；45°、葉片120x3mm。預設1125x3650mm，窗台安裝高300mm由專案定位設定。窗高改變時葉片間距等分調整。");
     var xe=g.Offset(0,g.X0,width,w);left=g.Offset(0,g.X0,50,fw);right=g.Offset(0,xe,-50,fw);var zt=g.Offset(2,g.Z0,height,h);var zb=g.Offset(2,g.Z0,50,fw);var zu=g.Offset(2,zt,-50,fw);var ya=g.Offset(1,g.Y0,-50);var yb=g.Offset(1,ya,100,fd);var material=Material.Create(core,"百葉框_鋁材");((Material)core.GetElement(material)).Color=new Color(100,110,120);
     g.Box(g.X0,left,ya,yb,g.Z0,zt,material);g.Box(right,xe,ya,yb,g.Z0,zt,material);g.Box(left,right,ya,yb,g.Z0,zb,material);g.Box(left,right,ya,yb,zu,zt,material);
     rows=g.VerticalGrid(g.Offset(2,g.Z0,125),g.Offset(2,zt,-125),23);GateCommit(tx);
    }
    var loaded=blade.LoadFamily(core,new ConstraintFamilyLoad());blade.Close(false);blade=null;
    stage="shared blade placement";using(var tx=GateTransaction(core,stage))
    {
     var symbol=(FamilySymbol)core.GetElement(loaded.GetFamilySymbolIds().Single());symbol.Activate();core.Regenerate();
     foreach(var row in rows){var fi=PlaceLouverBlade(core,g,symbol,left,row);core.FamilyManager.AssociateElementParameterToFamilyParameter(fi.LookupParameter("葉片長"),core.FamilyManager.get_Parameter("葉片淨長"));}
     GateCommit(tx);
    }
    stage="core flex";foreach(var size in cases)using(var tx=GateTransaction(core,stage)){core.FamilyManager.Set(core.FamilyManager.get_Parameter("窗寬"),size[0]/304.8);core.FamilyManager.Set(core.FamilyManager.get_Parameter("窗高"),size[1]/304.8);core.Regenerate();VerifyLouverGeometry(core,null,size[0],size[1],true);GateCommit(tx);}
    string corePath=Path.Combine(staging,coreName+".rfa");core.SaveAs(corePath,new SaveAsOptions{OverwriteExistingFile=false});core.Close(false);core=null;core=_uiApp.Application.OpenDocumentFile(corePath);
    stage="reopened core flex";foreach(var size in cases)using(var tx=GateTransaction(core,stage)){core.FamilyManager.Set(core.FamilyManager.get_Parameter("窗寬"),size[0]/304.8);core.FamilyManager.Set(core.FamilyManager.get_Parameter("窗高"),size[1]/304.8);core.Regenerate();VerifyLouverGeometry(core,null,size[0],size[1],true);tx.RollBack();}
    stage="curtain template";adapter=_uiApp.Application.NewFamilyDocument(ct);ReferencePlane xl,xr,zbnd,ztop,yzero;View front;
    using(var tx=GateTransaction(adapter,stage))
    {
     if(adapter.FamilyManager.CurrentType==null)adapter.FamilyManager.NewType("百葉窗");
     var planes=new FilteredElementCollector(adapter).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().ToList();var xs=planes.Where(r=>Math.Abs(r.Normal.X)>.99).OrderBy(r=>r.BubbleEnd.X).ToList();var zs=planes.Where(r=>Math.Abs(r.Normal.Z)>.99).OrderBy(r=>r.BubbleEnd.Z).ToList();
     if(xs.Count<2||zs.Count<2)throw new InvalidOperationException("Curtain window boundary reference planes missing.");xl=xs.First();xr=xs.Last();zbnd=zs.First();ztop=zs.Last();
     front=new FilteredElementCollector(adapter).OfClass(typeof(View)).Cast<View>().First(v=>!v.IsTemplate&&v.ViewType==ViewType.Elevation&&Math.Abs(v.ViewDirection.Y)>.99);
     AddLouverReporting(adapter,front,xl,xr,"帷幕淨寬",true);AddLouverReporting(adapter,front,zbnd,ztop,"帷幕淨高",false);GateCommit(tx);
    }
    var coreFamily=core.LoadFamily(adapter,new ConstraintFamilyLoad());core.Close(false);core=null;
    stage="curtain core association";using(var tx=GateTransaction(adapter,stage))
    {
     var guide=new SharedGateGeometry(adapter);
     guide.Input("進退偏移",0,null,true);
     var offsetDistance=guide.Input("進退定位距離",0,"1000 mm + 進退偏移",true);
     var offsetBase=guide.Offset(1,guide.Y0,-1000);
     yzero=guide.Offset(1,offsetBase,1000,offsetDistance);
     var symbol=(FamilySymbol)adapter.GetElement(coreFamily.GetFamilySymbolIds().Single());symbol.Activate();adapter.Regenerate();
     var fi=adapter.FamilyCreate.NewFamilyInstance(new XYZ(xl.BubbleEnd.X,0,zbnd.BubbleEnd.Z),symbol,StructuralType.NonStructural);adapter.Regenerate();
     adapter.FamilyManager.AssociateElementParameterToFamilyParameter(fi.LookupParameter("窗寬"),adapter.FamilyManager.get_Parameter("帷幕淨寬"));adapter.FamilyManager.AssociateElementParameterToFamilyParameter(fi.LookupParameter("窗高"),adapter.FamilyManager.get_Parameter("帷幕淨高"));adapter.Regenerate();
     var delta=new XYZ(xl.BubbleEnd.X,0,zbnd.BubbleEnd.Z)-fi.GetTransform().Origin;if(delta.GetLength()>1e-9)ElementTransformUtils.MoveElement(adapter,fi.Id,delta);adapter.Regenerate();
     foreach(var item in new[]{Tuple.Create(guide.Front,xl,FamilyInstanceReferenceType.Left),Tuple.Create(guide.Plan,yzero,FamilyInstanceReferenceType.Front),Tuple.Create(guide.Front,zbnd,FamilyInstanceReferenceType.Bottom)}){var reference=fi.GetReferences(item.Item3);if(reference==null||reference.Count!=1)throw new InvalidOperationException("Louver core origin reference missing.");adapter.FamilyCreate.NewAlignment(item.Item1,item.Item2.GetReference(),reference[0]);adapter.Regenerate();}
     GateCommit(tx);
    }
    stage="curtain host resizing";project=_uiApp.Application.NewProjectDocument(UnitSystem.Metric);var family=adapter.LoadFamily(project,new ConstraintFamilyLoad());var adapterSymbol=(FamilySymbol)project.GetElement(family.GetFamilySymbolIds().Single());
    TestLouverCurtainHost(project,adapterSymbol,cases);project.Close(false);project=null;
    string adapterPath=Path.Combine(staging,adapterName+".rfa");adapter.SaveAs(adapterPath,new SaveAsOptions{OverwriteExistingFile=false});adapter.Close(false);adapter=null;adapter=_uiApp.Application.OpenDocumentFile(adapterPath);
    stage="reopened curtain host resizing";project=_uiApp.Application.NewProjectDocument(UnitSystem.Metric);family=adapter.LoadFamily(project,new ConstraintFamilyLoad());adapterSymbol=(FamilySymbol)project.GetElement(family.GetFamilySymbolIds().Single());TestLouverCurtainHost(project,adapterSymbol,cases);project.Close(false);project=null;adapter.Close(false);adapter=null;
    Directory.CreateDirectory(folder);File.Copy(corePath,outputCore,false);try{File.Copy(adapterPath,outputAdapter,false);}catch{File.Delete(outputCore);throw;}
    return new{Success=true,CorePath=outputCore,CurtainWindowPath=outputAdapter,WidthMm=width,HeightMm=height,BladeCount=24,AngleDegrees=45,DefaultPitchMm=(height-250)/23,OffsetParameter="進退偏移",DefaultOffsetMm=0,OffsetTestsMm=new[]{-200,0,200},CoreFlexTests=3,ReopenedCoreTests=3,CurtainHostTests=9,ReopenedCurtainHostTests=9,HostProjectModified=false};
   }
   catch(Exception ex)
   {string diag=Path.Combine(folder,"diagnostics",Guid.NewGuid().ToString("N"));try{Directory.CreateDirectory(diag);foreach(var f in Directory.EnumerateFiles(staging,"*.rfa"))File.Copy(f,Path.Combine(diag,Path.GetFileName(f)),false);foreach(var pair in new[]{Tuple.Create(core,"未驗證主體"),Tuple.Create(adapter,"未驗證帷幕")})if(pair.Item1!=null&&pair.Item1.IsValidObject&&!pair.Item1.IsModifiable)pair.Item1.SaveAs(Path.Combine(diag,pair.Item2+".rfa"),new SaveAsOptions{OverwriteExistingFile=false});File.WriteAllText(Path.Combine(diag,"error.txt"),stage+"\n"+ex);}catch{}throw new InvalidOperationException("Louver stage "+stage+": "+ex.Message+" | "+diag,ex);}
   finally{foreach(var d in new[]{project,blade,core,adapter})if(d!=null&&d.IsValidObject)d.Close(false);foreach(var f in Directory.EnumerateFiles(staging))File.Delete(f);if(!Directory.EnumerateFileSystemEntries(staging).Any())Directory.Delete(staging);}
  }
  private static FamilyInstance PlaceLouverBlade(Document d,SharedGateGeometry g,FamilySymbol symbol,ReferencePlane left,ReferencePlane row)
  {
   var fi=d.FamilyCreate.NewFamilyInstance(new XYZ(g.Pos(left),0,g.Pos(row))/304.8,symbol,StructuralType.NonStructural);d.Regenerate();var delta=new XYZ(g.Pos(left),0,g.Pos(row))/304.8-fi.GetTransform().Origin;if(delta.GetLength()>1e-9){ElementTransformUtils.MoveElement(d,fi.Id,delta);d.Regenerate();}
   foreach(var item in new[]{Tuple.Create(g.Front,left,FamilyInstanceReferenceType.Left),Tuple.Create(g.Plan,g.Y0,FamilyInstanceReferenceType.Front),Tuple.Create(g.Front,row,FamilyInstanceReferenceType.Bottom)}){var refs=fi.GetReferences(item.Item3);if(refs==null||refs.Count!=1)throw new InvalidOperationException("Blade origin reference missing.");d.FamilyCreate.NewAlignment(item.Item1,item.Item2.GetReference(),refs[0]);d.Regenerate();}return fi;
  }
  private static void AddLouverReporting(Document d,View front,ReferencePlane a,ReferencePlane b,string name,bool x)
  {
   double ca=x?a.BubbleEnd.X:a.BubbleEnd.Z,cb=x?b.BubbleEnd.X:b.BubbleEnd.Z;var fm=d.FamilyManager;var p=fm.AddParameter(name,GroupTypeId.Geometry,SpecTypeId.Length,true);fm.Set(p,Math.Abs(cb-ca));var refs=new ReferenceArray();refs.Append(a.GetReference());refs.Append(b.GetReference());d.Regenerate();
   var dim=d.FamilyCreate.NewLinearDimension(front,Line.CreateBound(x?new XYZ(ca,0,-1):new XYZ(-1,0,ca),x?new XYZ(cb,0,-1):new XYZ(-1,0,cb)),refs);dim.FamilyLabel=p;fm.MakeReporting(p);d.Regenerate();
  }
  private static void TestLouverCurtainHost(Document d,FamilySymbol symbol,double[][] cases)
  {
   foreach(var size in cases)using(var tx=GateTransaction(d,"Test louver curtain host"))
   {
    symbol.Activate();var level=new FilteredElementCollector(d).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault()??Level.Create(d,0);var types=new FilteredElementCollector(d).OfClass(typeof(WallType)).Cast<WallType>().Where(t=>t.Kind==WallKind.Curtain).ToList();Wall wall=null;
    foreach(var type in types){var candidate=Wall.Create(d,Line.CreateBound(XYZ.Zero,new XYZ(size[0]/304.8,0,0)),type.Id,level.Id,size[1]/304.8,-level.Elevation,false,false);d.Regenerate();if(candidate.CurtainGrid.GetPanelIds().Count==1){wall=candidate;break;}d.Delete(candidate.Id);}
    if(wall==null)throw new InvalidOperationException("No single-panel curtain wall type in temporary project.");var old=wall.CurtainGrid.GetPanelIds().Single();var newId=d.GetElement(old).ChangeTypeId(symbol.Id);d.Regenerate();var panel=d.GetElement(newId==ElementId.InvalidElementId?old:newId);
    var instance=panel as FamilyInstance??throw new InvalidOperationException("Expected curtain window instance.");
    var offset=instance.LookupParameter("進退偏移");if(offset==null||offset.IsReadOnly)throw new InvalidOperationException("Editable instance offset parameter required.");
    double baseline=LouverCenterY(instance);var direction=instance.GetTransform().BasisY;
    foreach(double mm in new[]{-200.0,0.0,200.0})
    {
     offset.Set(mm/304.8);d.Regenerate();VerifyLouverGeometry(d,panel,size[0],size[1],false);
     if(Math.Abs(LouverCenterY(instance)-baseline-direction.Y*mm)>.5)throw new InvalidOperationException("Louver offset did not move all geometry with the instance parameter.");
    }
    tx.RollBack();
   }
  }
  private static double LouverCenterY(Element instance)
  {var bounds=new List<double[]>();CollectGateSolids(instance.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}),bounds);if(bounds.Count!=28)throw new InvalidOperationException("Offset louver geometry missing.");return(bounds.Min(b=>b[2])+bounds.Max(b=>b[3]))/2;}
  private static void VerifyLouverGeometry(Document d,Element panel,double w,double h,bool originZero)
  {
   var actual=new List<double[]>();if(panel!=null)CollectGateSolids(panel.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}),actual);else foreach(var e in new FilteredElementCollector(d).WhereElementIsNotElementType().Where(e=>e is GenericForm||e is FamilyInstance))CollectGateSolids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}),actual);
   if(actual.Count!=28)throw new InvalidOperationException("Expected28 louver solids, got"+actual.Count);double minX=actual.Min(b=>b[0]),minY=(actual.Min(b=>b[2])+actual.Max(b=>b[3]))/2,minZ=actual.Min(b=>b[4]);if(Math.Abs(actual.Max(b=>b[1])-minX-w)>.5||Math.Abs(actual.Max(b=>b[5])-minZ-h)>.5)throw new InvalidOperationException("Louver envelope mismatch.");
   if(originZero&&(Math.Abs(minX)>.5||Math.Abs(minZ)>.5))throw new InvalidOperationException("Louver origin changed.");
   var expected=new List<double[]> {new[]{0.0,50.0,-50.0,50.0,0.0,h},new[]{w-50,w,-50.0,50.0,0.0,h},new[]{50.0,w-50,-50.0,50.0,0.0,50.0},new[]{50.0,w-50,-50.0,50.0,h-50,h}};double c=61.5/Math.Sqrt(2);for(int i=0;i<24;i++){double z=125+(h-250)*i/23;expected.Add(new[]{50.0,w-50,-c,c,z-c,z+c});}
   foreach(var e in expected){var world=new[]{e[0]+minX,e[1]+minX,e[2]+minY,e[3]+minY,e[4]+minZ,e[5]+minZ};int n=actual.FindIndex(b=>b.Zip(world,(x,y)=>Math.Abs(x-y)).Max()<.5);if(n<0)throw new InvalidOperationException("Louver frame/blade flex mismatch.");actual.RemoveAt(n);}
  }
 }
}
