using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
namespace RevitMCP.Core
{
 public partial class CommandExecutor
 {
  private object ExportNewSiteGate(JObject p,JObject spec)
  {
   if(spec.Value<bool?>("drawingStyle")==true)return ExportDrawingSiteGate(p,spec);
   if(spec.Value<bool?>("sharedUnits")==true)return ExportSharedSiteGate(p,spec);
   string template=spec.Value<string>("templatePath"),folder=p.Value<string>("outputFolder");
   if(_uiApp.Application.VersionNumber!="2024"||!File.Exists(template)||!Path.IsPathRooted(template)||!Path.IsPathRooted(folder))throw new ArgumentException("Revit2024 and absolute template/output required.");
   double width=FenceDimension(spec,"widthMm",12000,6000,16000),height=FenceDimension(spec,"heightMm",5000,3000,6000);
   string stem="工地大門_寬高連動_重建版",output=Path.Combine(folder,stem+".rfa");if(File.Exists(output))throw new IOException("Output exists; use another outputFolder.");
   if(p.Value<bool?>("apply")!=true||p.Value<bool?>("dryRun")!=false)return new{DryRun=true,OutputPath=output,WidthMm=width,HeightMm=height,Appearance="Two200mm posts, top zigzag truss,29 framed sheet panels. Closed appearance only."};
   Document family=null;string staged=null;
   try{
    family=_uiApp.Application.NewFamilyDocument(template);ParametricSiteGate builder;
    using(var tx=new Transaction(family,"Create native parametric site gate")){tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FenceParametricBuilder.FenceFailures()).SetClearAfterRollback(true));family.OwnerFamily.FamilyCategory=Category.GetCategory(family,BuiltInCategory.OST_GenericModel);family.FamilyManager.NewType(stem);var steel=Material.Create(family,"大門_鍍鋅鋼材");((Material)family.GetElement(steel)).Color=new Color(170,180,185);builder=new ParametricSiteGate(family,width,height);builder.Build(steel);if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Gate creation failed.");}
    var cases=new[]{new[]{10000.0,4000.0},new[]{14000.0,5500.0},new[]{width,height}};
    foreach(var size in cases)using(var tx=new Transaction(family,"Verify gate width and height")){tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FenceParametricBuilder.FenceFailures()).SetClearAfterRollback(true));family.FamilyManager.Set(family.FamilyManager.get_Parameter("整體寬度"),size[0]/304.8);family.FamilyManager.Set(family.FamilyManager.get_Parameter("整體高度"),size[1]/304.8);family.Regenerate();builder.Verify();FenceVerify(family,163,size[0],size[1]);if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Gate flex failed.");}
    Directory.CreateDirectory(folder);staged=Path.Combine(folder,"gate-staging-"+Guid.NewGuid().ToString("N")+".rfa");family.SaveAs(staged,new SaveAsOptions{OverwriteExistingFile=false,MaximumBackups=1});family.Close(false);family=null;
    family=_uiApp.Application.OpenDocumentFile(staged);FenceVerify(family,163,width,height);
    using(var tx=new Transaction(family,"Verify reopened gate")){tx.Start();family.FamilyManager.Set(family.FamilyManager.get_Parameter("整體寬度"),11000/304.8);family.FamilyManager.Set(family.FamilyManager.get_Parameter("整體高度"),4500/304.8);family.Regenerate();FenceVerify(family,163,11000,4500);tx.RollBack();}
    family.Close(false);family=null;File.Move(staged,output);staged=null;return new{Success=true,OutputPath=output,WidthParameter="整體寬度",HeightParameter="整體高度",FlexCasesPassed=4,OriginalUnchanged=true,HostProjectModified=false,ClosedAppearanceOnly=true};
   }finally{if(family!=null&&family.IsValidObject)family.Close(false);if(staged!=null&&File.Exists(staged))File.Delete(staged);}
  }
 }
}
