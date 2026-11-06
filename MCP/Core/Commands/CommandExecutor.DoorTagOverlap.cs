using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object AvoidDoorTagOverlap(JObject args)
        {
            var doc = _uiApp.ActiveUIDocument.Document;
            var view = doc.ActiveView;
            if (!(view is ViewPlan) || args["viewId"]?.Value<long>() != view.Id.GetIdValue()) throw new ArgumentException("Current plan view required.");
            var input = args["elementIds"] as JArray;
            if (input == null || input.Count == 0 || input.Count > 10000 || input.Any(x => x.Type != JTokenType.Integer || x.Value<long>() <= 0)) throw new ArgumentException("Explicit tag IDs required.");
            double gapMm = args["gapPaperMm"]?.Value<double>() ?? 0.5, maxMm = args["maxMovePaperMm"]?.Value<double>() ?? 8;
            if (double.IsNaN(gapMm) || double.IsInfinity(gapMm) || double.IsNaN(maxMm) || double.IsInfinity(maxMm) || gapMm < 0 || gapMm > 3 || maxMm < 0.5 || maxMm > 20) throw new ArgumentException("Invalid limits.");
            double gap = gapMm * view.Scale / 304.8, max = maxMm * view.Scale / 304.8;
            var tags = new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().Where(t => t.OwnerViewId == view.Id).ToDictionary(t => (long)t.Id.GetIdValue());
            var boxes = tags.Select(t => new { t.Key, Box = ReadTagRectangle(t.Value, view) }).Where(x => x.Box != null).ToDictionary(x => x.Key, x => x.Box);
            var doors = new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().ToDictionary(e => (long)e.Id.GetIdValue());
            var doorBoxes = doors.Select(d => new { d.Key, Box = ReadTagRectangle(d.Value, view) }).Where(x => x.Box != null).ToDictionary(x => x.Key, x => x.Box);
            var rows = new JArray(); var targets = new Dictionary<long, XYZ>();
            foreach (long id in input.Values<long>().Distinct().OrderBy(x => x))
            {
                var row = new JObject { ["ElementId"] = id, ["Status"] = "Skipped" }; rows.Add(row);
                if (!tags.TryGetValue(id, out var tag) || tag.Category.Id.GetIdValue() != (long)BuiltInCategory.OST_DoorTags) { row["Reason"] = "NotVisibleDoorTag"; continue; }
                if (!boxes.TryGetValue(id, out var box)) { row["Reason"] = "MissingTagBox"; continue; }
                var refs = tag.GetTaggedElementIds().ToList();
                if (refs.Count != 1 || refs[0].HostElementId == ElementId.InvalidElementId || !doorBoxes.ContainsKey(refs[0].HostElementId.GetIdValue())) { row["Reason"] = "MissingSingleLocalDoorBox"; continue; }
                row["DoorId"] = refs[0].HostElementId.GetIdValue();
                if (!doorBoxes.Values.Any(b => box.Intersects(b, gap))) { row["Status"] = "Unchanged"; continue; }
                if (tag.Pinned || tag.IsOrphaned || tag.HasLeader || tag.GroupId != ElementId.InvalidElementId) { row["Reason"] = "PinnedOrOrphanedOrLeaderOrGrouped"; continue; }
                var candidates = new List<(double X, double Y)>();
                double step = 0.5 * view.Scale / 304.8;
                for (double x = -max; x <= max + 1e-8; x += step)
                    for (double y = -max; y <= max + 1e-8; y += step)
                        if (x*x+y*y <= max*max+1e-8) candidates.Add((x,y));
                foreach (var d in candidates.OrderBy(d => d.X*d.X+d.Y*d.Y))
                {
                    var b = box.Shift(d.X,d.Y);
                    if (doorBoxes.Values.Any(o => b.Intersects(o,gap)) || boxes.Any(o => o.Key != id && b.Intersects(o.Value,gap))) continue;
                    var old = tag.TagHeadPosition;
                    var target = old + view.RightDirection*d.X + view.UpDirection*d.Y;
                    targets[id] = target; boxes[id] = b; row["Status"] = "Planned";
                    row["OldPositionMm"] = new JArray(old.X*304.8,old.Y*304.8,old.Z*304.8);
                    row["NewPositionMm"] = new JArray(target.X*304.8,target.Y*304.8,target.Z*304.8); break;
                }
                if (!targets.ContainsKey(id)) row["Reason"] = "NoFreePositionWithinLimit";
            }
            bool dry = args["dryRun"]?.Value<bool>() ?? true, apply = args["apply"]?.Value<bool>() ?? false, committed = false; string error = null;
            if (!dry && apply && targets.Count > 0) using (var group = new TransactionGroup(doc,"Door tag clearance"))
            {
                group.Start();
                try
                {
                    using (var tx = new Transaction(doc,"Move door tags beside doors"))
                    {
                        tx.Start(); tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new ParameterCopyFailures()).SetClearAfterRollback(true));
                        foreach (var t in targets) tags[t.Key].TagHeadPosition = t.Value;
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Commit failed.");
                    }
                    var actual = tags.Select(t => new {t.Key, Box=ReadTagRectangle(t.Value,view)}).Where(x=>x.Box!=null).ToDictionary(x=>x.Key,x=>x.Box);
                    foreach (var t in targets)
                    {
                        if (!actual.ContainsKey(t.Key) || tags[t.Key].TagHeadPosition.DistanceTo(t.Value) > 0.1/304.8) throw new InvalidOperationException("Position verification failed.");
                        var b=actual[t.Key]; double toleranceGap=Math.Max(0,gap-0.1/304.8);
                        if (doorBoxes.Values.Any(o=>b.Intersects(o,toleranceGap)) || actual.Any(o=>o.Key!=t.Key && b.Intersects(o.Value,toleranceGap))) throw new InvalidOperationException("Overlap after regeneration.");
                    }
                    if (group.Assimilate()!=TransactionStatus.Committed) throw new InvalidOperationException("Group commit failed.");
                    committed=true; foreach (var r in rows.Where(r=>(string)r["Status"]=="Planned")) r["Status"]="Updated";
                }
                catch(Exception ex) { if(group.GetStatus()==TransactionStatus.Started)group.RollBack(); error=ex.Message; foreach(var r in rows.Where(r=>(string)r["Status"]=="Planned")){r["Status"]="Failed";r["Reason"]="BatchRolledBack";} }
            }
            return new { Success=error==null, DryRun=dry, Apply=apply, Committed=committed, Error=error, Scanned=rows.Count, Planned=targets.Count,
                Updated=rows.Count(r=>(string)r["Status"]=="Updated"), Unchanged=rows.Count(r=>(string)r["Status"]=="Unchanged"), Skipped=rows.Count(r=>(string)r["Status"]=="Skipped"), Failed=rows.Count(r=>(string)r["Status"]=="Failed"),
                UnmeasurableDoorIds=doors.Keys.Where(id=>!doorBoxes.ContainsKey(id)).ToArray(), UnmeasurableTagIds=tags.Keys.Where(id=>!boxes.ContainsKey(id)).ToArray(), Results=rows };
        }
    }
}
