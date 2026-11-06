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
  private object ExportGreenFence(JObject p,JObject spec)
  {
   if(spec.Value<bool?>("fillerOnly")==true)return ExportGreenFiller(p,spec);
   string template=spec.Value<string>("templatePath"),folder=p.Value<string>("outputFolder");
   if(_uiApp.Application.VersionNumber!="2024"||!File.Exists(template)||!Path.IsPathRooted(folder))throw new ArgumentException("Revit2024, model template and absolute output folder required.");
   bool adaptive=spec.Value<bool?>("adaptivePlanters")==true,tight=adaptive||spec.Value<bool?>("tightJoints")==true;
   double width=FenceDimension(spec,"widthMm",1100,adaptive?300:1000,2200),height=FenceDimension(spec,"heightMm",2400,1800,3600);string stem=adaptive?"施工綠籬_標準收尾":tight?"施工綠籬_緊密排列":"施工綠籬_掛盆式_寬高連動",output=Path.Combine(folder,stem+".rfa");if(File.Exists(output))throw new IOException("Output exists; choose another folder.");
   if(p.Value<bool?>("apply")!=true||p.Value<bool?>("dryRun")!=false)return new{DryRun=true,OutputPath=output,WidthMm=width,AboveGroundHeightMm=height,Rows=6,Columns=8,PotMm=new[]{110,100,120},MeshPitchMm=150,FoundationDepthMm=600};
   string staging=Path.Combine(Path.GetTempPath(),"AbbyGreenFence-"+Guid.NewGuid().ToString("N")),operation="framework";Directory.CreateDirectory(staging);Document host=null,child=null;
   try
   {
    host=_uiApp.Application.NewFamilyDocument(template);SharedGateGeometry g;ReferencePlane left,right,meshBase,pipeZ,braceLeft,braceRight,braceY;ReferencePlane[] columns,rows;
    using(var tx=GateTransaction(host,operation))
    {
     host.FamilyManager.NewType("110x240cm");g=new SharedGateGeometry(host);g.Input("整體寬度",width);g.Input("地上高度",height);g.Input("網片淨寬",0,"整體寬度 - 100 mm");g.Input("網架高度",0,"地上高度 - 600 mm");g.Input("斜撐接高",0,"地上高度 * 0.55");
     if(tight)foreach(string name in new[]{"左端斜撐","右端斜撐"}){var visible=host.FamilyManager.AddParameter(name,GroupTypeId.Visibility,SpecTypeId.Boolean.YesNo,true);host.FamilyManager.Set(visible,1);}
     foreach(var item in new[]{Tuple.Create("直線網數量","rounddown(網片淨寬 / 150 mm) + 1"),Tuple.Create("橫線網數量","rounddown(網架高度 / 150 mm) + 1")}){var fp=host.FamilyManager.AddParameter(item.Item1,GroupTypeId.Geometry,SpecTypeId.Int.Integer,false);host.FamilyManager.SetFormula(fp,item.Item2);}
     var note=host.FamilyManager.AddParameter("設計說明",GroupTypeId.IdentityData,SpecTypeId.String.Text,false);host.FamilyManager.Set(note,"圖示公分：寬110、地上高240；防溢座20、集水槽區30、網架180、頂部10。6排x8盆，盆寬11深10高12。網格150mm等距陣列；4#網線採6mm示意。葉片、50mm支架、斜撐及地下60cm基礎為外觀示意；無結構/灌溉性能認證。");
     var end=g.Offset(0,g.X0,width,host.FamilyManager.get_Parameter("整體寬度"));left=g.Offset(0,g.X0,50);right=g.Offset(0,end,-50);var high=g.Offset(2,g.Z0,height,host.FamilyManager.get_Parameter("地上高度"));var frameTop=g.Offset(2,high,-100);meshBase=g.Offset(2,g.Z0,500);pipeZ=g.Offset(2,high,-70);braceLeft=g.Offset(0,g.X0,tight?-5:20);braceRight=g.Offset(0,end,tight?-5:-30);braceY=g.Offset(1,g.Y0,800);
     var steel=GreenMaterial(host,"鍍鋅鋼架",150,160,165);var concrete=GreenMaterial(host,"混凝土",165,165,160);var trough=GreenMaterial(host,"集水槽",100,110,115);
     var ya=g.Offset(1,g.Y0,-25);var yb=g.Offset(1,g.Y0,25);var embedded=g.Offset(2,g.Z0,-400);g.Box(g.X0,left,ya,yb,embedded,frameTop,steel);g.Box(right,end,ya,yb,embedded,frameTop,steel);g.Box(left,right,ya,yb,g.Offset(2,frameTop,-50),frameTop,steel);g.Box(left,right,ya,yb,g.Offset(2,meshBase,-50),meshBase,steel);
     var back=g.Offset(1,g.Y0,900);var front=g.Offset(1,g.Y0,-250);g.Box(g.X0,end,front,back,g.Offset(2,g.Z0,-600),g.Z0,concrete);
     var ty0=g.Offset(1,g.Y0,-200);var ty1=g.Offset(1,g.Y0,-180);var ty2=g.Offset(1,g.Y0,-20);var z200=g.Offset(2,g.Z0,200);var z220=g.Offset(2,g.Z0,220);
     g.Box(g.X0,end,ty0,g.Y0,g.Z0,z200,concrete);g.Box(g.X0,end,ty0,g.Y0,z200,z220,trough);g.Box(g.X0,end,ty0,ty1,z220,meshBase,trough);g.Box(g.X0,end,ty2,g.Y0,z220,meshBase,trough);g.Box(g.X0,g.Offset(0,g.X0,20),ty1,ty2,z220,meshBase,trough);g.Box(g.Offset(0,end,-20),end,ty1,ty2,z220,meshBase,trough);
     columns=adaptive?null:g.Grid(left,g.Offset(0,end,-160),7);rows=g.VerticalGrid(g.Offset(2,g.Z0,600),g.Offset(2,high,-210),5);GateCommit(tx);
    }
    var families=new List<Family>();
    foreach(var kind in new[]{"掛盆植栽","直線網","橫線網","灌溉管","斜撐"})
    {
     operation="unit "+kind;child=_uiApp.Application.NewFamilyDocument(template);
     using(var tx=GateTransaction(child,operation))
     {
      child.FamilyManager.NewType(kind=="掛盆植栽"?"武竹":"標準");var u=new SharedGateGeometry(child);u.X0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Left);u.Y0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Front);u.Z0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Bottom);
      if(kind=="掛盆植栽")BuildGreenPlanter(child);
      else if(kind=="斜撐"){var fp=u.Input("單元高",height*.55);u.Diagonal(u.Offset(0,u.X0,800),u.Offset(2,u.Z0,height*.55,fp),GreenMaterial(child,"斜撐鋼材",140,150,155));}
      else{double length=kind=="直線網"?height-600:width-100;var fp=u.Input("單元長",length);var metal=GreenMaterial(child,kind,90,100,100);var ex=GreenCylinder(child,kind=="直線網"?XYZ.BasisZ:XYZ.BasisX,kind=="灌溉管"?8:3,length,metal);child.FamilyManager.AssociateElementParameterToFamilyParameter(ex.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM),fp);}
      GateCommit(tx);
     }
     child.SaveAs(Path.Combine(staging,kind+".rfa"),new SaveAsOptions{OverwriteExistingFile=false});families.Add(adaptive&&kind=="掛盆植栽"?null:child.LoadFamily(host,new ConstraintFamilyLoad()));child.Close(false);child=null;
    }
    if(adaptive)families[0]=LoadAdaptiveGreenRows(host,template,staging,width);
    operation="shared arrangement";using(var tx=GateTransaction(host,operation))
    {
     var plantSymbols=families[0].GetFamilySymbolIds().Select(id=>(FamilySymbol)host.GetElement(id)).ToList();var green=plantSymbols.Single(s=>s.Name=="武竹");var purple=plantSymbols.Single(s=>s.Name=="紫背鴨跖草");var wires=families.Skip(1).Select(f=>(FamilySymbol)host.GetElement(f.GetFamilySymbolIds().Single())).ToArray();foreach(var s in plantSymbols.Concat(wires))s.Activate();host.Regenerate();
     foreach(var item in new[]{Tuple.Create(0,"單元長","網架高度"),Tuple.Create(1,"單元長","網片淨寬"),Tuple.Create(2,"單元長","網片淨寬"),Tuple.Create(3,"單元高","斜撐接高")})host.FamilyManager.AssociateElementParameterToFamilyParameter(wires[item.Item1].LookupParameter(item.Item2),host.FamilyManager.get_Parameter(item.Item3));
     var plantY=g.Offset(1,g.Y0,-30);
     if(adaptive){foreach(var s in plantSymbols)host.FamilyManager.AssociateElementParameterToFamilyParameter(s.LookupParameter("排寬"),host.FamilyManager.get_Parameter("整體寬度"));for(int row=0;row<6;row++)PlaceGateUnit(host,g,row==3?purple:green,g.X0,plantY,rows[row],false);}
     else for(int row=0;row<6;row++)for(int column=0;column<8;column++)PlaceGateUnit(host,g,row==3?purple:green,columns[column],plantY,rows[row],false);
     foreach(bool vertical in new[]{true,false})
     {
      var symbol=wires[vertical?0:1];var instance=host.FamilyCreate.NewFamilyInstance(new XYZ(50,0,500)/304.8,symbol,StructuralType.NonStructural);host.Regenerate();int count=1+(int)Math.Floor((vertical?width-100:height-600)/150);var array=LinearArray.Create(host,g.Front,instance.Id,count,(vertical?XYZ.BasisX:XYZ.BasisZ)*(150/304.8),ArrayAnchorMember.Second);array.Label=host.FamilyManager.get_Parameter(vertical?"直線網數量":"橫線網數量");host.Regenerate();
     }
     PlaceGateUnit(host,g,wires[2],left,g.Offset(1,g.Y0,-25),pipeZ,false);
     foreach(var x in new[]{braceLeft,braceRight})
     {
      var point=new XYZ(g.Pos(x),800,0)/304.8;var fi=host.FamilyCreate.NewFamilyInstance(point,wires[3],StructuralType.NonStructural);ElementTransformUtils.RotateElement(host,fi.Id,Line.CreateBound(point,point+XYZ.BasisZ),-Math.PI/2);host.Regenerate();var delta=point-fi.GetTransform().Origin;if(delta.GetLength()>1e-9){ElementTransformUtils.MoveElement(host,fi.Id,delta);host.Regenerate();}
      foreach(var item in new[]{Tuple.Create(g.Front,x,FamilyInstanceReferenceType.Front),Tuple.Create(g.Plan,braceY,FamilyInstanceReferenceType.Left),Tuple.Create(g.Front,g.Z0,FamilyInstanceReferenceType.Bottom)}){var refs=fi.GetReferences(item.Item3);if(refs==null||refs.Count!=1)throw new InvalidOperationException("Brace reference missing.");host.FamilyCreate.NewAlignment(item.Item1,item.Item2.GetReference(),refs[0]);host.Regenerate();}
      if(tight){var visible=fi.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM)??throw new InvalidOperationException("Nested brace visibility parameter missing.");host.FamilyManager.AssociateElementParameterToFamilyParameter(visible,host.FamilyManager.get_Parameter(x.Id==braceLeft.Id?"左端斜撐":"右端斜撐"));}
     }
     GateCommit(tx);
    }
    var cases=new[]{new[]{1000.0,2100.0},new[]{1600.0,3000.0}}.Concat(adaptive?new[]{new[]{445.683872,2400.0},new[]{479.993533,2400.0},new[]{620.171180,2400.0},new[]{903.039769,2400.0}}:Array.Empty<double[]>()).Concat(new[]{new[]{width,height}}).ToArray();operation="flex";
    foreach(var size in cases)using(var tx=GateTransaction(host,operation)){host.FamilyManager.Set(host.FamilyManager.get_Parameter("整體寬度"),size[0]/304.8);host.FamilyManager.Set(host.FamilyManager.get_Parameter("地上高度"),size[1]/304.8);host.Regenerate();VerifyGreenFence(host,size[0],size[1],tight,adaptive);GateCommit(tx);}
    var candidate=Path.Combine(staging,stem+".rfa");host.SaveAs(candidate,new SaveAsOptions{OverwriteExistingFile=false});host.Close(false);host=null;host=_uiApp.Application.OpenDocumentFile(candidate);operation="reopened flex";
    foreach(var size in cases)using(var tx=GateTransaction(host,operation)){host.FamilyManager.Set(host.FamilyManager.get_Parameter("整體寬度"),size[0]/304.8);host.FamilyManager.Set(host.FamilyManager.get_Parameter("地上高度"),size[1]/304.8);host.Regenerate();VerifyGreenFence(host,size[0],size[1],tight,adaptive);tx.RollBack();}
    if(tight)foreach(string name in new[]{"左端斜撐","右端斜撐"})using(var tx=GateTransaction(host,"Verify individual end brace visibility")){host.FamilyManager.Set(host.FamilyManager.get_Parameter(name),0);host.Regenerate();VerifyGreenFence(host,width,height,tight,adaptive);tx.RollBack();}
    var countParameters=host.FamilyManager.Parameters.Cast<FamilyParameter>().Count(fp=>fp.Id.GetIdValue()>0);host.Close(false);host=null;Directory.CreateDirectory(folder);File.Copy(candidate,output,false);return new{Success=true,OutputPath=output,WidthMm=width,AboveGroundHeightMm=height,UserParameters=countParameters,PlanterCount=6*(adaptive?GreenPotColumns(width):8),MeshPitchMm=150,FoundationDepthMm=600,FlexTests=cases.Length,ReopenedFlexTests=cases.Length,BraceVisibilityTests=tight?2:0,HostProjectModified=false,PlantGeometrySchematic=true};
   }
   catch(Exception ex){var diag=Path.Combine(folder,"diagnostics",Guid.NewGuid().ToString("N"));try{Directory.CreateDirectory(diag);foreach(var f in Directory.EnumerateFiles(staging,"*.rfa"))File.Copy(f,Path.Combine(diag,Path.GetFileName(f)),false);if(host!=null&&host.IsValidObject&&!host.IsModifiable)host.SaveAs(Path.Combine(diag,"未驗證主體.rfa"),new SaveAsOptions{OverwriteExistingFile=false});File.WriteAllText(Path.Combine(diag,"error.txt"),operation+"\n"+ex);}catch{}throw new InvalidOperationException(operation+": "+ex.Message+" | "+diag,ex);}
   finally{if(child!=null&&child.IsValidObject)child.Close(false);if(host!=null&&host.IsValidObject)host.Close(false);foreach(var f in Directory.EnumerateFiles(staging))File.Delete(f);if(!Directory.EnumerateFileSystemEntries(staging).Any())Directory.Delete(staging);}
  }
  private static ElementId GreenMaterial(Document d,string name,byte r,byte g,byte b){var id=Material.Create(d,name);((Material)d.GetElement(id)).Color=new Color(r,g,b);return id;}
  private static Extrusion GreenCylinder(Document d,XYZ axis,double radius,double length,ElementId material)
  {
   var u=axis.IsAlmostEqualTo(XYZ.BasisZ)?XYZ.BasisX:XYZ.BasisY;var v=axis.CrossProduct(u);var curves=new CurveArray();curves.Append(Arc.Create(XYZ.Zero,radius/304.8,0,Math.PI,u,v));curves.Append(Arc.Create(XYZ.Zero,radius/304.8,Math.PI,2*Math.PI,u,v));var loops=new CurveArrArray();loops.Append(curves);var ex=d.FamilyCreate.NewExtrusion(true,loops,SketchPlane.Create(d,Plane.CreateByNormalAndOrigin(axis,XYZ.Zero)),length/304.8);ex.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(material);return ex;
  }
  private static double[] GreenTipsX={-65,-45,-25,0,25,45,65},GreenTipsZ={50,70,85,90,85,70,50};
  private static UV[] GreenLeaf(int index)
  {double x=GreenTipsX[index],z=GreenTipsZ[index],length=Math.Sqrt(x*x+z*z),nx=-z/length*5,nz=x/length*5;return new[]{new UV(0,0),new UV(x*.5+nx,z*.5+nz),new UV(x,z),new UV(x*.5-nx,z*.5-nz)};}
  private static void BuildGreenPlanter(Document d)
  {
   var plastic=GreenMaterial(d,"塑膠掛盆",100,100,90);var soil=GreenMaterial(d,"培養土",65,45,25);var hook=GreenMaterial(d,"掛勾",100,110,115);var green=GreenMaterial(d,"武竹_綠葉",65,145,65);var purple=GreenMaterial(d,"紫背鴨跖草_紫葉",65,35,75);var fp=d.FamilyManager.AddParameter("葉片材質",GroupTypeId.Materials,SpecTypeId.Reference.Material,false);d.FamilyManager.Set(fp,green);
   FenceExtrusion(d,new XYZ(0,-100,0),XYZ.BasisX,XYZ.BasisZ,XYZ.BasisY,new[]{new UV(10,0),new UV(100,0),new UV(110,120),new UV(0,120)},100,plastic);FenceBox(d,15,-85,120,95,-15,122,soil);
   foreach(double x in new[]{10.0,96.0})FenceExtrusion(d,new XYZ(x,0,0),XYZ.BasisY,XYZ.BasisZ,XYZ.BasisX,new[]{new UV(0,108),new UV(0,142),new UV(30,142),new UV(30,125),new UV(27,125),new UV(27,139),new UV(3,139),new UV(3,108)},4,hook);
   for(int i=0;i<7;i++){var before=new HashSet<ElementId>(new FilteredElementCollector(d).OfClass(typeof(Extrusion)).ToElementIds());FenceExtrusion(d,new XYZ(55,-55,120),XYZ.BasisX,XYZ.BasisZ,XYZ.BasisY,GreenLeaf(i),2,green);var ex=new FilteredElementCollector(d).OfClass(typeof(Extrusion)).Cast<Extrusion>().Single(e=>!before.Contains(e.Id));d.FamilyManager.AssociateElementParameterToFamilyParameter(ex.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM),fp);}
   d.FamilyManager.NewType("紫背鴨跖草");d.FamilyManager.Set(fp,purple);d.FamilyManager.CurrentType=d.FamilyManager.Types.Cast<FamilyType>().Single(t=>t.Name=="武竹");
  }
  private static void VerifyGreenFence(Document d,double w,double h,bool tight=false,bool adaptive=false)
  {
   var actual=new List<double[]>();foreach(var e in new FilteredElementCollector(d).WhereElementIsNotElementType().Where(e=>e is GenericForm||e is FamilyInstance)){var visible=e.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);if(visible!=null&&visible.AsInteger()==0)continue;CollectGateSolids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}),actual);}var expected=new List<double[]>();Action<double,double,double,double,double,double> add=(a,b,c,e,f,g)=>expected.Add(new[]{a,b,c,e,f,g});
   add(0,50,-25,25,-400,h-100);add(w-50,w,-25,25,-400,h-100);add(50,w-50,-25,25,h-150,h-100);add(50,w-50,-25,25,450,500);add(0,w,-250,900,-600,0);add(0,w,-200,0,0,200);add(0,w,-200,0,200,220);add(0,w,-200,-180,220,500);add(0,w,-20,0,220,500);add(0,20,-180,-20,220,500);add(w-20,w,-180,-20,220,500);add(50,w-50,-33,-17,h-78,h-62);
   var bracePositions=tight?new[]{-5.0,w-5}:new[]{20.0,w-30};for(int side=0;side<2;side++){var parameter=d.FamilyManager.get_Parameter(side==0?"左端斜撐":"右端斜撐");if(parameter!=null&&d.FamilyManager.CurrentType.AsInteger(parameter)==0)continue;double x=bracePositions[side];add(x,x+10,0,800,-15,h*.55);}
   int nv=1+(int)Math.Floor((w-100)/150),nh=1+(int)Math.Floor((h-600)/150);for(int i=0;i<nv;i++){double x=50+150*i;add(x-3,x+3,-3,3,500,h-100);}for(int i=0;i<nh;i++){double z=500+150*i;add(50,w-50,-3,3,z-3,z+3);}
   int potColumns=adaptive?GreenPotColumns(w):8;
   for(int row=0;row<6;row++)for(int column=0;column<potColumns;column++)
   {
    double x=50+(adaptive?125*column:(w-210)*column/7),z=600+(h-810)*row/5;add(x,x+110,-130,-30,z,z+120);add(x+15,x+95,-115,-45,z+120,z+122);foreach(double hook in new[]{10.0,96.0})add(x+hook,x+hook+4,-30,0,z+108,z+142);
    for(int i=0;i<7;i++){var leaf=GreenLeaf(i);add(x+55+leaf.Min(v=>v.U),x+55+leaf.Max(v=>v.U),-85,-83,z+120+leaf.Min(v=>v.V),z+120+leaf.Max(v=>v.V));}
   }
   if(actual.Count!=expected.Count)throw new InvalidOperationException("Green fence expected"+expected.Count+" solids, got"+actual.Count);
   foreach(var bounds in expected){int n=actual.FindIndex(b=>b.Zip(bounds,(x,y)=>Math.Abs(x-y)).Max()<.5);if(n<0)throw new InvalidOperationException("Green fence component mismatch: "+string.Join(",",bounds));actual.RemoveAt(n);}
   var arrays=new FilteredElementCollector(d).OfClass(typeof(LinearArray)).Cast<LinearArray>().ToList();if(arrays.Count!=2||!arrays.Select(a=>a.NumMembers).OrderBy(x=>x).SequenceEqual(new[]{nv,nh}.OrderBy(x=>x)))throw new InvalidOperationException("150mm mesh array counts failed.");
  }
 }
}
