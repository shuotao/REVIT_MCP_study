using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
#if REVIT2025_OR_GREATER
using TagId = System.Int64;
#else
using TagId = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object CenterBeamTags(JObject args)
        {
            var doc = _uiApp.ActiveUIDocument.Document;
            var view = doc.ActiveView;
            if (args["viewId"]?.Value<long>() != view.Id.GetIdValue())
                throw new ArgumentException("Active view changed; refresh preview.");
            var ids = args["elementIds"] as JArray;
            if (ids == null || ids.Count == 0 || ids.Count > 10000)
                throw new ArgumentException("Explicit tag elementIds required (1..10000).");
            var visible = new HashSet<ElementId>(new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_StructuralFramingTags).WhereElementIsNotElementType().ToElementIds());
            var rows = new JArray();
            var planned = new List<Tuple<IndependentTag, XYZ>>();
            var normal = view.ViewDirection.Normalize();
            foreach (var id in ids.Values<long>().Distinct())
            {
                var tag = doc.GetElement(new ElementId((TagId)id)) as IndependentTag;
                var row = new JObject { ["ElementId"] = id, ["Status"] = "Skipped" };
                rows.Add(row);
                if (tag == null || !visible.Contains(tag.Id) || tag.OwnerViewId != view.Id)
                { row["Reason"] = "NotVisibleBeamTagInActiveView"; continue; }
                if (tag.IsOrphaned || tag.Pinned || tag.GroupId != ElementId.InvalidElementId)
                { row["Reason"] = "OrphanedPinnedOrGrouped"; continue; }
                var refs = tag.GetTaggedElementIds().ToList();
                if (refs.Count != 1 || refs[0].HostElementId == ElementId.InvalidElementId)
                { row["Reason"] = "MultipleTargetsOrLinkedElement"; continue; }
                var beam = doc.GetElement(refs[0].HostElementId) as FamilyInstance;
                if (beam == null || beam.StructuralType != StructuralType.Beam || !(beam.Location is LocationCurve location))
                { row["Reason"] = "TargetIsNotBeamWithCurve"; continue; }
                var old = tag.TagHeadPosition;
                var midpoint = location.Curve.Evaluate(0.5, true);
                // Keep annotation depth; move only within the view plane.
                var delta = midpoint - old;
                var target = old + delta - normal.Multiply(delta.DotProduct(normal));
                row["BeamId"] = beam.Id.GetIdValue();
                // Do not read TagText: some framing tags crash Revit 2025 in native
                // IndependentTag.getTagText (AccessViolationException, not recoverable).
                // Positioning depends only on the actual referenced beam and tag head.
                row["OldPositionMm"] = new JArray(old.X * 304.8, old.Y * 304.8, old.Z * 304.8);
                row["NewPositionMm"] = new JArray(target.X * 304.8, target.Y * 304.8, target.Z * 304.8);
                row["DistanceMm"] = old.DistanceTo(target) * 304.8;
                row["Status"] = old.DistanceTo(target) <= 0.1 / 304.8 ? "Unchanged" : "Planned";
                if ((string)row["Status"] == "Planned") planned.Add(Tuple.Create(tag, target));
            }
            bool dry = args["dryRun"]?.Value<bool>() ?? true;
            bool apply = args["apply"]?.Value<bool>() ?? false;
            bool committed = false;
            string error = null;
            if (!dry && apply && planned.Count > 0)
            using (var group = new TransactionGroup(doc, "Center beam tags"))
            {
                group.Start();
                try
                {
                    using (var tx = new Transaction(doc, "Move tags to referenced beams"))
                    {
                        tx.Start();
                        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
                            .SetFailuresPreprocessor(new FramingFloorFailures()).SetClearAfterRollback(true));
                        foreach (var item in planned) item.Item1.TagHeadPosition = item.Item2;
                        if (tx.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Transaction not committed.");
                    }
                    foreach (var item in planned)
                        if (item.Item1.TagHeadPosition.DistanceTo(item.Item2) > 0.1 / 304.8)
                            throw new InvalidOperationException("Position verification failed: " + item.Item1.Id.GetIdValue());
                    if (group.Assimilate() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Group commit failed.");
                    committed = true;
                    foreach (var row in rows.Where(r => (string)r["Status"] == "Planned")) row["Status"] = "Updated";
                }
                catch (Exception ex)
                {
                    if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                    error = ex.Message;
                    foreach (var row in rows.Where(r => (string)r["Status"] == "Planned"))
                    { row["Status"] = "Failed"; row["Reason"] = "BatchRolledBack"; }
                }
            }
            return new { Success = error == null, ViewId = view.Id.GetIdValue(), DryRun = dry, Apply = apply,
                Committed = committed, Error = error, Scanned = rows.Count, Planned = planned.Count,
                Updated = rows.Count(r => (string)r["Status"] == "Updated"),
                Unchanged = rows.Count(r => (string)r["Status"] == "Unchanged"),
                Skipped = rows.Count(r => (string)r["Status"] == "Skipped"),
                Failed = rows.Count(r => (string)r["Status"] == "Failed"), Results = rows };
        }
    }
}
