using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitMCP.Models;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object CreateColumnBatch(JObject p)
        {
            var doc = _uiApp.ActiveUIDocument.Document;
            var input = p["placements"] as JArray;
            if (input == null || input.Count < 1 || input.Count > 500) throw new ArgumentException("placements requires 1..500 items.");
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            var bottoms = levels.Where(x => x.Name == p.Value<string>("bottomLevel")).ToList();
            var tops = levels.Where(x => x.Name == p.Value<string>("topLevel")).ToList();
            if (bottoms.Count != 1 || tops.Count != 1 || tops[0].Elevation <= bottoms[0].Elevation)
                throw new ArgumentException("Exact bottom/top level names required, top above bottom.");
            var bottom = bottoms[0]; var top = tops[0];
            var existing = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            var plan = new List<Tuple<XYZ,FamilySymbol,JObject>>();
            var rows = new JArray();
            foreach (var item in input.OfType<JObject>())
            {
                if (item["x"] == null || item["y"] == null || item["typeId"]?.Type != JTokenType.Integer)
                    throw new ArgumentException("Each placement requires x, y and integer typeId.");
                double x = item.Value<double>("x"), y = item.Value<double>("y");
                if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
                    throw new ArgumentException("Invalid coordinates.");
                var symbol = doc.GetElement(new ElementId(item.Value<long>("typeId"))) as FamilySymbol;
                if (symbol?.Category?.Id != new ElementId(BuiltInCategory.OST_StructuralColumns))
                    throw new ArgumentException("typeId must identify a structural column type.");
                var point = new XYZ(x/304.8,y/304.8,bottom.Elevation);
                var old = existing.FirstOrDefault(e => e.Location is LocationPoint lp &&
                    new XYZ(lp.Point.X,lp.Point.Y,bottom.Elevation).DistanceTo(point)<1/304.8 &&
                    e.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId()==bottom.Id &&
                    e.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId()==top.Id);
                bool duplicate = plan.Any(q => q.Item1.DistanceTo(point)<1/304.8);
                var row = new JObject { ["Xmm"]=x,["Ymm"]=y,["TypeId"]=symbol.Id.Value,
                    ["Status"]=old!=null || duplicate ? "Skipped" : "Planned" };
                if (old!=null) { row["ExistingId"]=old.Id.Value; row["Reason"]="Existing column at same location and levels"; }
                else if (duplicate) row["Reason"]="Duplicate input location";
                else plan.Add(Tuple.Create(point,symbol,row));
                rows.Add(row);
            }
            if (rows.Count!=input.Count) throw new ArgumentException("All placements must be objects.");
            bool write=p.Value<bool?>("apply")==true && p.Value<bool?>("dryRun") == false;
            if (plan.Count>0)
            {
                using (var group = new TransactionGroup(doc,"Verify column batch"))
                using (var tx = new Transaction(doc,"Batch create structural columns"))
                {
                    group.Start();
                    tx.Start();
                    tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new ColumnBatchFailures()).SetClearAfterRollback(true));
                    foreach(var q in plan)
                    {
                        if (!q.Item2.IsActive) q.Item2.Activate();
                        var c=doc.Create.NewFamilyInstance(q.Item1,q.Item2,bottom,StructuralType.Column);
                        c.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM).Set(top.Id);
                        c.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM).Set(0.0);
                        c.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM).Set(0.0);
                        q.Item3["ElementId"]=c.Id.Value;
                    }
                    doc.Regenerate();
                    foreach(var q in plan)
                    {
                        var c=(FamilyInstance)doc.GetElement(new ElementId(q.Item3.Value<long>("ElementId")));
                        // For level-based columns LocationPoint.Z is not a reliable
                        // physical bottom. Verify plan position separately; vertical
                        // placement is defined by the two levels and their offsets.
                        if (!(c.Location is LocationPoint lp) ||
                            new XYZ(lp.Point.X,lp.Point.Y,q.Item1.Z).DistanceTo(q.Item1)>1/304.8 || c.Symbol.Id!=q.Item2.Id ||
                            c.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM).AsElementId()!=bottom.Id ||
                            c.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM).AsElementId()!=top.Id ||
                            Math.Abs(c.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM).AsDouble())>1e-8 ||
                            Math.Abs(c.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM).AsDouble())>1e-8)
                            throw new InvalidOperationException("Column position/type/level/offset verification failed for " + c.Id + "; batch rolled back.");
                        var box=c.get_BoundingBox(null);
                        if(box==null || Math.Abs(box.Min.Z-bottom.Elevation)>1/304.8 || Math.Abs(box.Max.Z-top.Elevation)>1/304.8)
                            throw new InvalidOperationException("Column physical bottom/top verification failed for " + c.Id + "; batch rolled back.");
                    }
                    if(tx.Commit()!=TransactionStatus.Committed) throw new InvalidOperationException("Column batch rolled back by Revit.");
                    if(write)
                    {
                        group.Assimilate();
                        foreach(var q in plan) q.Item3["Status"]="Created";
                    }
                    else
                    {
                        group.RollBack();
                        foreach(var q in plan) q.Item3.Remove("ElementId");
                    }
                }
            }
            return new { Scanned=input.Count, Planned=plan.Count, Created=write?plan.Count:0,
                Skipped=rows.Count-plan.Count, Applied=write, BottomLevel=bottom.Name,TopLevel=top.Name,Results=rows };
        }
        private sealed class ColumnBatchFailures : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                return accessor.GetFailureMessages().Count>0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}
