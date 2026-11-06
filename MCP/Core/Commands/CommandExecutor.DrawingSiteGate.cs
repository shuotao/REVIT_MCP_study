using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
namespace RevitMCP.Core
{
 public partial class CommandExecutor
 {
  private object ExportDrawingSiteGate(JObject p,JObject spec)
  {
   string template=spec.Value<string>("templatePath"),folder=p.Value<string>("outputFolder");
   if(_uiApp.Application.VersionNumber!="2024"||!File.Exists(template)||!Path.IsPathRooted(folder))throw new ArgumentException("Revit2024, model template and absolute output folder required.");
   double width=FenceDimension(spec,"widthMm",10000,6000,16000),height=FenceDimension(spec,"heightMm",6000,3000,6000);string stem="施工大門_詳圖樣式_寬高連動",output=Path.Combine(folder,stem+".rfa");if(File.Exists(output))throw new IOException("Output exists; use another folder.");
   if(p.Value<bool?>("apply")!=true||p.Value<bool?>("dryRun")!=false)return new{DryRun=true,OutputPath=output,WidthMm=width,HeightMm=height,Panels=16,Diagonals=32,UserParameters=9,Assumptions="H250x125 uses schematic6mm web/9mm flanges. Grille/ribs/truss rods schematic; no foundation or folding animation."};
   var stage=Path.Combine(Path.GetTempPath(),"AbbyDrawingGate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);Document host=null,child=null;string operation="framework";
   try
   {
    host=_uiApp.Application.NewFamilyDocument(template);SharedGateGeometry g;ReferencePlane left,right,low,down,topChord,bottomChord,track,y10;ReferencePlane[] panels,braces;
    using(var tx=GateTransaction(host,operation))
    {
     host.FamilyManager.NewType("1000x600cm");g=new SharedGateGeometry(host);g.Input("整體寬度",width);g.Input("整體高度",height);g.Input("桁架高度",500);g.Input("門洞淨寬",0,"整體寬度 - 250 mm");g.Input("下門片高",0,"整體高度 * 0.55");g.Input("門片寬",0,"門洞淨寬 / 16");g.Input("斜材跨距",0,"門洞淨寬 / 32");g.Input("斜材高差",0,"桁架高度 - 250 mm");
     var note=host.FamilyManager.AddParameter("設計說明",GroupTypeId.IdentityData,SpecTypeId.String.Text,false);host.FamilyManager.Set(note,"依施工大門詳圖：H250x125門柱、C125x50x20x2.3上弦/下弦、50x50x5角鋁吊軌、75x75x4槽鋼軌道。H板厚6/9mm及下門片比例55%為未標尺寸示意；隔柵/企口板/5分圓鐵採外觀示意，無基礎及折疊動畫。");
     var end=g.Offset(0,g.X0,width,host.FamilyManager.get_Parameter("整體寬度"));var high=g.Offset(2,g.Z0,height,host.FamilyManager.get_Parameter("整體高度"));left=g.Offset(0,g.X0,125);right=g.Offset(0,end,-125);var material=Material.Create(host,"大門_鋼材");((Material)host.GetElement(material)).Color=new Color(140,150,160);
     foreach(var start in new[]{g.X0,right})
     {var finish=g.Offset(0,start,125);var web0=g.Offset(0,start,59.5);var web1=g.Offset(0,web0,6);var ya=g.Offset(1,g.Y0,-125);var yb=g.Offset(1,ya,9);var yc=g.Offset(1,g.Y0,116);var yd=g.Offset(1,yc,9);g.Box(start,finish,ya,yb,g.Z0,high,material);g.Box(start,finish,yc,yd,g.Z0,high,material);g.Box(web0,web1,yb,yc,g.Z0,high,material);}
     bottomChord=g.Offset(2,high,-500,host.FamilyManager.get_Parameter("桁架高度"));topChord=g.Offset(2,high,-125);low=g.Offset(2,bottomChord,125);down=g.Offset(2,low,15);track=g.Offset(2,bottomChord,-50);y10=g.Offset(1,g.Y0,10);
     panels=g.Grid(left,right,16);braces=g.Grid(left,right,32);
     var panelTop=g.Offset(2,g.Z0,height*.55,host.FamilyManager.get_Parameter("下門片高"));
     for(int i=0;i<=16;i+=2)
     {var a=g.Offset(0,panels[i],-25);var b=g.Offset(0,a,5);var c=g.Offset(0,panels[i],20);var e=g.Offset(0,c,5);var ya=g.Offset(1,g.Y0,-25);var yb=g.Offset(1,ya,5);var yc=g.Offset(1,g.Y0,20);var yd=g.Offset(1,yc,5);g.Box(a,b,ya,yd,panelTop,track,material);g.Box(c,e,ya,yd,panelTop,track,material);g.Box(b,c,ya,yb,panelTop,track,material);g.Box(b,c,yc,yd,panelTop,track,material);}
     GateCommit(tx);
    }
    var families=new List<Family>();
    foreach(var kind in new[]{"門片","斜材","C弦材","角鋁吊軌","槽鋼軌道"})
    {
     operation="shared unit "+kind;child=_uiApp.Application.NewFamilyDocument(template);
     using(var tx=GateTransaction(child,operation))
     {
      child.FamilyManager.NewType("標準");var u=new SharedGateGeometry(child);u.X0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Left);u.Y0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Front);u.Z0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Bottom);var material=Material.Create(child,"大門_金屬");((Material)child.GetElement(material)).Color=new Color(160,170,180);
      if(kind=="門片")BuildDrawingGatePanel(u,(width-250)/16,height*.55,material);
      else if(kind=="斜材"){var w=u.Input("單元寬",(width-250)/32);var h=u.Input("單元高",250);u.Diagonal(u.Offset(0,u.X0,(width-250)/32,w),u.Offset(2,u.Z0,250,h),material);}
      else
      {
       var length=u.Input("單元寬",width-250);UV[] outline;
       if(kind=="C弦材")outline=new[]{new UV(0,0),new UV(50,0),new UV(50,20),new UV(47.7,20),new UV(47.7,2.3),new UV(2.3,2.3),new UV(2.3,122.7),new UV(47.7,122.7),new UV(47.7,105),new UV(50,105),new UV(50,125),new UV(0,125)}.Select(v=>new UV(v.U-25,v.V)).ToArray();
       else if(kind=="角鋁吊軌")outline=new[]{new UV(-25,0),new UV(25,0),new UV(25,5),new UV(-20,5),new UV(-20,50),new UV(-25,50)};
       else outline=new[]{new UV(-37.5,0),new UV(37.5,0),new UV(37.5,75),new UV(33.5,75),new UV(33.5,4),new UV(-33.5,4),new UV(-33.5,75),new UV(-37.5,75)};
       FenceExtrusion(child,XYZ.Zero,XYZ.BasisY,XYZ.BasisZ,XYZ.BasisX,outline,width-250,material);var ex=new FilteredElementCollector(child).OfClass(typeof(Extrusion)).Cast<Extrusion>().Single();child.FamilyManager.AssociateElementParameterToFamilyParameter(ex.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM),length);
      }
      GateCommit(tx);
     }
     child.SaveAs(Path.Combine(stage,kind+".rfa"),new SaveAsOptions{OverwriteExistingFile=false});families.Add(child.LoadFamily(host,new ConstraintFamilyLoad()));child.Close(false);child=null;
    }
    operation="place shared components";using(var tx=GateTransaction(host,operation))
    {
     var symbols=families.Select(f=>(FamilySymbol)host.GetElement(f.GetFamilySymbolIds().Single())).ToArray();foreach(var s in symbols)s.Activate();host.Regenerate();
     foreach(var item in new[]{Tuple.Create(0,"單元寬","門片寬"),Tuple.Create(0,"單元高","下門片高"),Tuple.Create(1,"單元寬","斜材跨距"),Tuple.Create(1,"單元高","斜材高差"),Tuple.Create(2,"單元寬","門洞淨寬"),Tuple.Create(3,"單元寬","門洞淨寬"),Tuple.Create(4,"單元寬","門洞淨寬")})host.FamilyManager.AssociateElementParameterToFamilyParameter(symbols[item.Item1].LookupParameter(item.Item2),host.FamilyManager.get_Parameter(item.Item3));
     for(int i=0;i<16;i++)PlaceGateUnit(host,g,symbols[0],panels[i],g.Y0,g.Z0,false);
     for(int i=0;i<32;i++)PlaceGateUnit(host,g,symbols[1],braces[i%2==0?i:i+1],i%2==0?g.Y0:y10,i%2==0?low:down,i%2!=0);
     PlaceGateUnit(host,g,symbols[2],left,g.Y0,topChord,false);PlaceGateUnit(host,g,symbols[2],left,g.Y0,bottomChord,false);PlaceGateUnit(host,g,symbols[3],left,g.Y0,track,false);PlaceGateUnit(host,g,symbols[4],left,g.Y0,g.Z0,false);GateCommit(tx);
    }
    operation="width/height flex";var cases=new[]{new[]{8000.0,4500.0},new[]{12000.0,5500.0},new[]{width,height}};
    foreach(var size in cases)using(var tx=GateTransaction(host,operation)){host.FamilyManager.Set(host.FamilyManager.get_Parameter("整體寬度"),size[0]/304.8);host.FamilyManager.Set(host.FamilyManager.get_Parameter("整體高度"),size[1]/304.8);host.Regenerate();VerifyDrawingGate(host,size[0],size[1]);GateCommit(tx);}
    string candidate=Path.Combine(stage,stem+".rfa");host.SaveAs(candidate,new SaveAsOptions{OverwriteExistingFile=false});host.Close(false);host=null;host=_uiApp.Application.OpenDocumentFile(candidate);operation="reopened flex";
    foreach(var size in cases)using(var tx=GateTransaction(host,operation)){host.FamilyManager.Set(host.FamilyManager.get_Parameter("整體寬度"),size[0]/304.8);host.FamilyManager.Set(host.FamilyManager.get_Parameter("整體高度"),size[1]/304.8);host.Regenerate();VerifyDrawingGate(host,size[0],size[1]);tx.RollBack();}
    int count=host.FamilyManager.Parameters.Cast<FamilyParameter>().Count(fp=>fp.Id.GetIdValue()>0);host.Close(false);host=null;Directory.CreateDirectory(folder);File.Copy(candidate,output,false);return new{Success=true,OutputPath=output,WidthMm=width,HeightMm=height,UserParameters=count,SharedPanelCount=16,SharedDiagonalCount=32,ComponentCount=462,FlexTests=3,ReopenedFlexTests=3,HostProjectModified=false,ClosedAppearanceOnly=true};
   }
   catch(Exception ex){var diag=Path.Combine(folder,"diagnostics",Guid.NewGuid().ToString("N"));try{Directory.CreateDirectory(diag);foreach(var f in Directory.EnumerateFiles(stage,"*.rfa"))File.Copy(f,Path.Combine(diag,Path.GetFileName(f)),false);if(host!=null&&host.IsValidObject&&!host.IsModifiable)host.SaveAs(Path.Combine(diag,"未驗證主體.rfa"),new SaveAsOptions{OverwriteExistingFile=false});File.WriteAllText(Path.Combine(diag,"error.txt"),operation+"\n"+ex);}catch{}throw new InvalidOperationException(operation+": "+ex.Message+" | "+diag,ex);}
   finally{if(child!=null&&child.IsValidObject)child.Close(false);if(host!=null&&host.IsValidObject)host.Close(false);foreach(var f in Directory.EnumerateFiles(stage))File.Delete(f);if(!Directory.EnumerateFileSystemEntries(stage).Any())Directory.Delete(stage);}
  }
  private static void BuildDrawingGatePanel(SharedGateGeometry u,double width,double height,ElementId material)
  {
   var w=u.Input("單元寬",width);var h=u.Input("單元高",height);var xe=u.Offset(0,u.X0,width,w);var zt=u.Offset(2,u.Z0,height,h);var a=u.Offset(0,u.X0,3);var b=u.Offset(0,a,25);var e=u.Offset(0,xe,-3);var c=u.Offset(0,e,-25);var ya=u.Offset(1,u.Y0,-25);var yb=u.Offset(1,u.Y0,25);var zs=u.Offset(2,u.Z0,250);var sheetBase=u.Offset(2,zs,25);var mid=u.Offset(2,zt,-500);var grilleBase=u.Offset(2,mid,25);var grilleTop=u.Offset(2,zt,-25);var sy=u.Offset(1,u.Y0,-1.2);var gy=u.Offset(1,u.Y0,-3);var ry=u.Offset(1,u.Y0,-5);
   u.Box(a,b,ya,yb,zs,zt,material);u.Box(c,e,ya,yb,zs,zt,material);u.Box(b,c,ya,yb,zs,sheetBase,material);u.Box(b,c,ya,yb,grilleTop,zt,material);u.Box(b,c,ya,yb,mid,grilleBase,material);u.Box(b,c,sy,u.Y0,sheetBase,mid,material);
   var ribs=u.Grid(b,c,7);for(int i=1;i<7;i++)u.Box(ribs[i],u.Offset(0,ribs[i],2),ry,gy,sheetBase,mid,material);
   var vertical=u.Grid(b,c,8);for(int i=1;i<8;i++)u.Box(vertical[i],u.Offset(0,vertical[i],3),gy,u.Y0,grilleBase,grilleTop,material);
   var horizontal=u.VerticalGrid(grilleBase,grilleTop,6);for(int i=1;i<6;i++)u.Box(b,c,gy,u.Y0,horizontal[i],u.Offset(2,horizontal[i],3),material);
  }
  private static void VerifyDrawingGate(Document d,double w,double h)
  {
   var actual=new List<double[]>();foreach(var el in new FilteredElementCollector(d).WhereElementIsNotElementType().Where(e=>e is GenericForm||e is FamilyInstance))CollectGateSolids(el.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}),actual);var expected=new List<double[]>();Action<double,double,double,double,double,double> add=(a,b,c,e,f,g)=>expected.Add(new[]{a,b,c,e,f,g});
   foreach(double x in new[]{0.0,w-125}){add(x,x+125,-125,-116,0,h);add(x,x+125,116,125,0,h);add(x+59.5,x+65.5,-116,116,0,h);}
   double net=w-250,ph=h*.55;add(125,w-125,-25,25,h-125,h);add(125,w-125,-25,25,h-500,h-375);add(125,w-125,-25,25,h-550,h-500);add(125,w-125,-37.5,37.5,0,75);
   for(int i=0;i<=16;i+=2){double x=125+net*i/16;add(x-25,x-20,-25,25,ph,h-550);add(x+20,x+25,-25,25,ph,h-550);add(x-20,x+20,-25,-20,ph,h-550);add(x-20,x+20,20,25,ph,h-550);}
   for(int i=0;i<16;i++){double a=125+net*i/16+3,b=a+25,e=125+net*(i+1)/16-3,c=e-25;add(a,b,-25,25,250,ph);add(c,e,-25,25,250,ph);add(b,c,-25,25,250,275);add(b,c,-25,25,ph-25,ph);add(b,c,-25,25,ph-500,ph-475);add(b,c,-1.2,0,275,ph-500);for(int j=1;j<7;j++){double x=b+(c-b)*j/7;add(x,x+2,-5,-3,275,ph-500);}for(int j=1;j<8;j++){double x=b+(c-b)*j/8;add(x,x+3,-3,0,ph-475,ph-25);}for(int j=1;j<6;j++){double z=ph-475+450.0*j/6;add(b,c,-3,0,z,z+3);}}
   for(int i=0;i<32;i++)add(125+net*i/32,125+net*(i+1)/32,0,10,i%2==0?h-390:h-375,i%2==0?h-125:h-110);
   if(actual.Count!=462||expected.Count!=462)throw new InvalidOperationException("Detailed gate expected462 solids, got "+actual.Count);
   foreach(var bounds in expected){int n=actual.FindIndex(b=>b.Zip(bounds,(x,y)=>Math.Abs(x-y)).Max()<.5);if(n<0)throw new InvalidOperationException("Detailed gate component mismatch: "+string.Join(",",bounds));actual.RemoveAt(n);}
  }
 }
}
