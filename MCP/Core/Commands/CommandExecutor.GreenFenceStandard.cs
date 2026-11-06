using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
namespace RevitMCP.Core
{
 public partial class CommandExecutor
 {
  private static int GreenPotColumns(double width)=>Math.Max(2,1+(int)Math.Floor((width-210)/125));
  private Family LoadAdaptiveGreenRows(Document host,string template,string staging,double width)
  {
   Document row=null,plant=null;
   try
   {
    row=_uiApp.Application.NewFamilyDocument(template);plant=_uiApp.Application.OpenDocumentFile(Path.Combine(staging,"掛盆植栽.rfa"));var family=plant.LoadFamily(row,new ConstraintFamilyLoad());plant.Close(false);plant=null;
    using(var tx=GateTransaction(row,"Adaptive planter row"))
    {
     row.FamilyManager.NewType("武竹");var g=new SharedGateGeometry(row);
     g.X0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Left);g.Y0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Front);g.Z0.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME).Set((int)FamilyInstanceReferenceType.Bottom);
     g.Input("排寬",width);var count=row.FamilyManager.AddParameter("盆數",GroupTypeId.Geometry,SpecTypeId.Int.Integer,false);row.FamilyManager.SetFormula(count,"if(排寬 < 335 mm, 2, rounddown((排寬 - 210 mm) / 125 mm) + 1)");
     var material=row.FamilyManager.AddParameter("葉片材質",GroupTypeId.Materials,SpecTypeId.Reference.Material,false);var green=GreenMaterial(row,"排_綠葉",65,145,65);var purple=GreenMaterial(row,"排_紫葉",65,35,75);row.FamilyManager.Set(material,green);
     var symbol=family.GetFamilySymbolIds().Select(id=>(FamilySymbol)row.GetElement(id)).Single(s=>s.Name=="武竹");symbol.Activate();row.FamilyManager.AssociateElementParameterToFamilyParameter(symbol.LookupParameter("葉片材質"),material);row.Regenerate();
     var first=row.FamilyCreate.NewFamilyInstance(new XYZ(50/304.8,0,0),symbol,StructuralType.NonStructural);row.Regenerate();var array=LinearArray.Create(row,g.Front,first.Id,GreenPotColumns(width),XYZ.BasisX*(125/304.8),ArrayAnchorMember.Second);array.Label=count;
     row.FamilyManager.NewType("紫背鴨跖草");row.FamilyManager.Set(material,purple);row.FamilyManager.CurrentType=row.FamilyManager.Types.Cast<FamilyType>().Single(t=>t.Name=="武竹");GateCommit(tx);
    }
    row.SaveAs(Path.Combine(staging,"共用植栽排.rfa"),new SaveAsOptions());return row.LoadFamily(host,new ConstraintFamilyLoad());
   }
   finally{if(plant!=null&&plant.IsValidObject)plant.Close(false);if(row!=null&&row.IsValidObject)row.Close(false);}
  }
  private object ExportGreenFiller(JObject root,JObject spec)
  {
   string template=spec.Value<string>("templatePath"),folder=root.Value<string>("outputFolder");double width=FenceDimension(spec,"widthMm",60,10,299),height=FenceDimension(spec,"heightMm",2400,1800,3600);string output=Path.Combine(folder,"施工綠籬_窄收邊條.rfa");
   if(_uiApp.Application.VersionNumber!="2024"||!File.Exists(template)||!Path.IsPathRooted(folder)||File.Exists(output))throw new ArgumentException("2024 template and unused absolute output required.");
   if(root.Value<bool?>("apply")!=true||root.Value<bool?>("dryRun")!=false)return new{DryRun=true,OutputPath=output,WidthMm=width,HeightMm=height,Planters=0};
   Document d=null;try
   {
    d=_uiApp.Application.NewFamilyDocument(template);
    using(var tx=GateTransaction(d,"Narrow green fence closure"))
    {
     d.FamilyManager.NewType("窄收邊");var g=new SharedGateGeometry(d);var wp=g.Input("整體寬度",width);var hp=g.Input("地上高度",height);var end=g.Offset(0,g.X0,width,wp);var top=g.Offset(2,g.Z0,height,hp);
     foreach(string name in new[]{"左端斜撐","右端斜撐"}){var fp=d.FamilyManager.AddParameter(name,GroupTypeId.Visibility,SpecTypeId.Boolean.YesNo,true);d.FamilyManager.Set(fp,0);}
     var concrete=GreenMaterial(d,"收邊混凝土",165,165,160);var steel=GreenMaterial(d,"收邊鋼板",140,150,155);var z200=g.Offset(2,g.Z0,200);var z500=g.Offset(2,g.Z0,500);
     g.Box(g.X0,end,g.Offset(1,g.Y0,-250),g.Offset(1,g.Y0,900),g.Offset(2,g.Z0,-600),g.Z0,concrete);var front=g.Offset(1,g.Y0,-200);g.Box(g.X0,end,front,g.Y0,g.Z0,z200,concrete);g.Box(g.X0,end,front,g.Y0,z200,z500,steel);g.Box(g.X0,end,g.Offset(1,g.Y0,-25),g.Offset(1,g.Y0,25),z500,top,steel);GateCommit(tx);
    }
    var cases=new[]{new[]{39.452959859,2400.0},new[]{59.879217953,2400.0},new[]{150.0,2100.0},new[]{width,height}};
    foreach(var size in cases)using(var tx=GateTransaction(d,"Flex narrow closure")){d.FamilyManager.Set(d.FamilyManager.get_Parameter("整體寬度"),size[0]/304.8);d.FamilyManager.Set(d.FamilyManager.get_Parameter("地上高度"),size[1]/304.8);d.Regenerate();VerifyGreenFiller(d,size[0],size[1]);GateCommit(tx);}
    Directory.CreateDirectory(folder);d.SaveAs(output,new SaveAsOptions());d.Close(false);d=_uiApp.Application.OpenDocumentFile(output);
    foreach(var size in cases)using(var tx=GateTransaction(d,"Reopen closure flex")){d.FamilyManager.Set(d.FamilyManager.get_Parameter("整體寬度"),size[0]/304.8);d.FamilyManager.Set(d.FamilyManager.get_Parameter("地上高度"),size[1]/304.8);d.Regenerate();VerifyGreenFiller(d,size[0],size[1]);tx.RollBack();}
    return new{Success=true,OutputPath=output,PlanterCount=0,FlexTests=cases.Length,ReopenedFlexTests=cases.Length,HostProjectModified=false};
   }
   finally{if(d!=null&&d.IsValidObject)d.Close(false);}
  }
  private static void VerifyGreenFiller(Document d,double w,double h)
  {
   var solids=new System.Collections.Generic.List<double[]>();foreach(var e in new FilteredElementCollector(d).OfClass(typeof(Extrusion)))CollectGateSolids(e.get_Geometry(new Options()),solids);
   if(solids.Count!=4||solids.Any(b=>Math.Abs(b[0])>.5||Math.Abs(b[1]-w)>.5)||Math.Abs(solids.Max(b=>b[5])-h)>.5)throw new InvalidOperationException("Narrow closure flex failed.");
  }
 }
}
