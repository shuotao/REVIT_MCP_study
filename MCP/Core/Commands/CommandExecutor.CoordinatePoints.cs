using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object AlignCoordinatePointsToGrids(JObject input)
        {
            var doc = _uiApp.ActiveUIDocument.Document;
            if (doc.IsFamilyDocument || doc.IsReadOnly || doc.IsModifiable)
                throw new InvalidOperationException("Requires an editable project outside another transaction.");
            var names = new[] { input.Value<string>("gridName1"), input.Value<string>("gridName2") };
            if (names.Any(string.IsNullOrWhiteSpace) || names[0] == names[1])
                throw new ArgumentException("Two distinct exact grid names are required.");
            var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().ToList();
            var chosen = names.Select(n => {
                var matches = grids.Where(g => g.Name == n).ToList();
                if (matches.Count != 1 || !(matches[0].Curve is Line))
                    throw new ArgumentException("Requires one straight host grid: " + n);
                return matches[0];
            }).ToArray();
            var a = chosen[0].Curve.GetEndPoint(0); var b = chosen[0].Curve.GetEndPoint(1);
            var c = chosen[1].Curve.GetEndPoint(0); var d = chosen[1].Curve.GetEndPoint(1);
            var u = b - a; var v = d - c;
            double det = u.X * v.Y - u.Y * v.X;
            if (Math.Abs(det) <= 1e-10 * u.GetLength() * v.GetLength())
                throw new ArgumentException("Grid lines are parallel or coincident.");
            var pb = BasePoint.GetProjectBasePoint(doc); var sp = BasePoint.GetSurveyPoint(doc);
            if (pb == null || sp == null) throw new InvalidOperationException("Missing coordinate points.");
            double t = ((c.X - a.X) * v.Y - (c.Y - a.Y) * v.X) / det;
            var anchor = new XYZ(a.X + t * u.X, a.Y + t * u.Y, pb.Position.Z);
            if (!XYZ.IsWithinLengthLimits(anchor)) throw new ArgumentException("Intersection exceeds Revit design limits.");
            var location = doc.ActiveProjectLocation;
            var before = CoordinatePointState(pb, sp, location);
            var baseline = CoordinateGeometrySnapshot(doc);
            double angle = location.GetProjectPosition(XYZ.Zero).Angle;
            string token;
            using (var sha = SHA256.Create())
                token = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                    doc.ProjectInformation.UniqueId + "|" + location.UniqueId + "|" + before.ToString() + "|" +
                    CoordinateVector(anchor) + "|" + string.Join(";", baseline.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value)))))
                    .Replace("-", "");
            bool apply = input.Value<bool?>("apply") == true && input.Value<bool?>("dryRun") == false;
            if (apply && input.Value<string>("previewToken") != token)
                throw new InvalidOperationException("Missing or stale previewToken; run preview again.");
            bool pbPinned = pb.Pinned, spPinned = sp.Pinned, clipped = sp.Clipped;
            var allowed = new HashSet<ElementId> { pb.Id, sp.Id, location.Id, doc.SiteLocation.Id };
            // Revit updates these internal bookkeeping records when shared coordinates change.
            // Require both the observed name and absence of any model identity/geometry.
            var bookkeeping = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .Where(IsCoordinateBookkeeping).Select(e => e.Id).ToHashSet();
            var unexpected = new HashSet<ElementId>();
            var bookkeepingUpdates = new List<object>();
            bool membershipChanged = false;
            EventHandler<DocumentChangedEventArgs> guard = (sender, e) => {
                if (!e.GetDocument().Equals(doc)) return;
                if (e.GetAddedElementIds().Count > 0 || e.GetDeletedElementIds().Count > 0) membershipChanged = true;
                foreach (var id in e.GetModifiedElementIds()) if (!allowed.Contains(id)) unexpected.Add(id);
            };
            JObject after;
            using (var group = new TransactionGroup(doc, "Abby coordinate points at grid intersection"))
            {
                if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Cannot start coordinate transaction group.");
                _uiApp.Application.DocumentChanged += guard;
                try
                {
                    using (var tx = new Transaction(doc, "Align coordinate points and zero shared coordinates"))
                    {
                        tx.Start();
                        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
                            .SetFailuresPreprocessor(new ColumnBatchFailures()).SetClearAfterRollback(true));
                        if (pbPinned) pb.Pinned = false;
                        if (spPinned) sp.Pinned = false;
                        sp.Clipped = false;
                        // Only these two point IDs are ever passed to MoveElement.
                        ElementTransformUtils.MoveElement(doc, pb.Id, anchor - pb.Position);
                        ElementTransformUtils.MoveElement(doc, sp.Id, anchor - sp.Position);
                        location.SetProjectPosition(anchor, new ProjectPosition(0, 0, 0, angle));
                        doc.Regenerate();
                        sp.Clipped = clipped;
                        pb.Pinned = pbPinned; sp.Pinned = spPinned;
                        if (tx.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Revit rejected coordinate changes.");
                    }
                    const double tolerance = 0.01 / 304.8;
                    if (pb.Position.DistanceTo(anchor) > tolerance || sp.Position.DistanceTo(anchor) > tolerance ||
                        pb.SharedPosition.GetLength() > tolerance || sp.SharedPosition.GetLength() > tolerance ||
                        Math.Abs(location.GetProjectPosition(XYZ.Zero).Angle - angle) > 1e-10 ||
                        pb.Pinned != pbPinned || sp.Pinned != spPinned || sp.Clipped != clipped)
                        throw new InvalidOperationException("Coordinate point/angle/state verification failed.");
                    var verified = CoordinateGeometrySnapshot(doc);
                    foreach (var id in unexpected.ToList())
                    {
                        var element = doc.GetElement(id);
                        if (!bookkeeping.Contains(id) || !IsCoordinateBookkeeping(element)) continue;
                        bookkeepingUpdates.Add(new { Id = id.ToString(), element.Name,
                            RuntimeType = element.GetType().FullName });
                        unexpected.Remove(id);
                    }
                    if (membershipChanged || unexpected.Count > 0 || baseline.Count != verified.Count ||
                        baseline.Any(p => !verified.TryGetValue(p.Key, out var value) || value != p.Value))
                        throw new InvalidOperationException("Non-coordinate elements changed; rolling back. IDs: " +
                            string.Join(",", unexpected.Select(id => id.ToString()).Take(30)));
                    after = CoordinatePointState(pb, sp, location);
                    _uiApp.Application.DocumentChanged -= guard;
                    var status = apply ? group.Assimilate() : group.RollBack();
                    if (status != (apply ? TransactionStatus.Committed : TransactionStatus.RolledBack))
                        throw new InvalidOperationException("Unexpected coordinate group status: " + status);
                }
                catch
                {
                    _uiApp.Application.DocumentChanged -= guard;
                    if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                    throw;
                }
                finally { _uiApp.Application.DocumentChanged -= guard; }
            }
            return new { Applied = apply, TrialSucceeded = true, PreviewToken = token,
                GridNames = names, AnchorInternalMm = new[] { anchor.X * 304.8, anchor.Y * 304.8, anchor.Z * 304.8 },
                Before = before, VerifiedResult = after, ModelUnchanged = true,
                GeometryRecordsVerified = baseline.Count, UnexpectedModifiedElements = 0,
                InternalBookkeepingUpdates = bookkeepingUpdates,
                SharedCoordinatesChanged = apply, DocumentSaved = false };
        }

        private static string CoordinateVector(XYZ p) => string.Join(",", new[] { p.X, p.Y, p.Z }
            .Select(x => x.ToString("R", CultureInfo.InvariantCulture)));

        private static bool IsCoordinateBookkeeping(Element element)
        {
            if (element == null || element.GetType() != typeof(Element) || element.Category != null ||
                element.Location != null || element.get_BoundingBox(null) != null) return false;
            if (element.Name != "最新反作用力" && element.Name != "作用中的位置追蹤元素") return false;
            using (var options = new Options())
            {
                var geometry = element.get_Geometry(options);
                return geometry == null || !geometry.Cast<GeometryObject>().Any();
            }
        }

        private static JObject CoordinatePointState(BasePoint pb, BasePoint sp, ProjectLocation location)
        {
            Func<BasePoint, JObject> point = p => new JObject {
                ["Id"] = p.Id.ToString(), ["InternalFeet"] = CoordinateVector(p.Position),
                ["SharedFeet"] = CoordinateVector(p.SharedPosition), ["Pinned"] = p.Pinned };
            return new JObject { ["ProjectBasePoint"] = point(pb), ["SurveyPoint"] = point(sp),
                ["SurveyClipped"] = sp.Clipped, ["TrueNorthRadians"] = location.GetProjectPosition(XYZ.Zero).Angle };
        }

        // Internal geometry only: displayed shared elevations are expected to change.
        // The DocumentChanged guard separately rejects any modified non-coordinate element,
        // including elements without a Location (e.g. sketch-based floors).
        private static Dictionary<string, string> CoordinateGeometrySnapshot(Document doc)
        {
            var result = new Dictionary<string, string>();
            foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (e is BasePoint || e is ProjectLocation || e is SiteLocation) continue;
                string data = "";
                if (e.Location is LocationPoint p)
                {
                    data = CoordinateVector(p.Point);
                    // Rotation is unsupported for several LocationPoint owners,
                    // including non-Instance elements. Never call that optional API.
                    // Instance orientation is captured by GetTransform below;
                    // the DocumentChanged guard rejects changes to every other owner.
                }
                else if (e.Location is LocationCurve lc)
                    data = string.Join("/", lc.Curve.Tessellate().Select(CoordinateVector));
                if (e is Grid g) data += "G:" + string.Join("/", g.Curve.Tessellate().Select(CoordinateVector));
                if (e is Level l) data += "L:" + l.ProjectElevation.ToString("R", CultureInfo.InvariantCulture);
                if (e is Instance instance)
                {
                    var tr = instance.GetTransform();
                    data += "T:" + string.Join("/", new[] { tr.Origin, tr.BasisX, tr.BasisY, tr.BasisZ }.Select(CoordinateVector));
                }
                result.Add(e.Id.ToString(), data);
            }
            return result;
        }
    }
}
