using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        #region 網格線建立

        const double GridMmPerFt = 304.8;
        const double GridDupTolFt = 10.0 / 304.8;

        /// <summary>
        /// 依座標（公釐）批次建立直線網格線，可指定名稱。
        /// 與既有網格線共線重疊者略過；名稱已被使用者不建立，逐筆回報。
        /// </summary>
        private object CreateGrids(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var items = parameters["grids"] as JArray;
            if (items == null || items.Count == 0)
                throw new Exception("請提供 grids 陣列（每筆含 startX/startY/endX/endY，公釐）");

            var plans = new List<GridPlan>();
            int gridIndex = 0;
            foreach (var it in items)
            {
                double? sx = it["startX"]?.Value<double?>();
                double? sy = it["startY"]?.Value<double?>();
                double? ex = it["endX"]?.Value<double?>();
                double? ey = it["endY"]?.Value<double?>();
                if (sx == null || sy == null || ex == null || ey == null)
                    throw new Exception($"grids[{gridIndex}] 缺少 startX/startY/endX/endY");
                plans.Add(new GridPlan
                {
                    Name = it["name"]?.Value<string>(),
                    Start = new XYZ(sx.Value / GridMmPerFt, sy.Value / GridMmPerFt, 0),
                    End = new XYZ(ex.Value / GridMmPerFt, ey.Value / GridMmPerFt, 0)
                });
                gridIndex++;
            }

            var results = ApplyGridPlans(doc, plans, false, "建立網格線");
            return new
            {
                Created = results.Count(r => r.Status == "created"),
                Skipped = results.Count(r => r.Status != "created"),
                Results = results
            };
        }

        internal class GridPlan
        {
            public string Name;
            public XYZ Start;
            public XYZ End;
        }

        internal class GridResult
        {
            public string Name { get; set; }
            public string Status { get; set; }   // created | planned | duplicate | name_conflict | invalid | failed
            public string Reason { get; set; }
            public long? ElementId { get; set; }
            public double StartX { get; set; }
            public double StartY { get; set; }
            public double EndX { get; set; }
            public double EndY { get; set; }
            public double LengthMm { get; set; }
        }

        /// <summary>
        /// 檢查並（非 dryRun 時）建立網格線。dryRun 只回報 planned / duplicate / name_conflict，不開 Transaction。
        /// </summary>
        private static List<GridResult> ApplyGridPlans(Document doc, List<GridPlan> plans, bool dryRun, string transactionName)
        {
            var existing = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                .Where(g => !g.IsCurved).ToList();
            var usedNames = new HashSet<string>(existing.Select(g => g.Name));
            var accepted = new List<(XYZ a, XYZ b)>();
            var results = new List<GridResult>();

            Transaction tr = null;
            if (!dryRun)
            {
                tr = new Transaction(doc, transactionName);
                tr.Start();
            }

            try
            {
                foreach (var p in plans)
                {
                    var r = new GridResult
                    {
                        Name = p.Name,
                        StartX = Math.Round(p.Start.X * GridMmPerFt, 1),
                        StartY = Math.Round(p.Start.Y * GridMmPerFt, 1),
                        EndX = Math.Round(p.End.X * GridMmPerFt, 1),
                        EndY = Math.Round(p.End.Y * GridMmPerFt, 1),
                        LengthMm = Math.Round(p.Start.DistanceTo(p.End) * GridMmPerFt, 0)
                    };
                    results.Add(r);

                    if (p.Start.DistanceTo(p.End) < 100.0 / GridMmPerFt)
                    {
                        r.Status = "invalid"; r.Reason = "線長小於 100mm";
                        continue;
                    }

                    var dup = existing.FirstOrDefault(g =>
                        IsSameGridLine(p.Start, p.End, g.Curve.GetEndPoint(0), g.Curve.GetEndPoint(1)));
                    if (dup != null)
                    {
                        r.Status = "duplicate"; r.Reason = $"與既有網格線「{dup.Name}」共線重疊";
                        continue;
                    }
                    if (accepted.Any(a => IsSameGridLine(p.Start, p.End, a.a, a.b)))
                    {
                        r.Status = "duplicate"; r.Reason = "與本批次另一條共線重疊";
                        continue;
                    }
                    if (!string.IsNullOrEmpty(p.Name) && usedNames.Contains(p.Name))
                    {
                        r.Status = "name_conflict"; r.Reason = $"名稱「{p.Name}」已被其他網格線使用";
                        continue;
                    }

                    accepted.Add((p.Start, p.End));
                    if (!string.IsNullOrEmpty(p.Name)) usedNames.Add(p.Name);

                    if (dryRun)
                    {
                        r.Status = "planned";
                        continue;
                    }

                    try
                    {
                        var grid = Grid.Create(doc, Line.CreateBound(p.Start, p.End));
                        if (!string.IsNullOrEmpty(p.Name)) grid.Name = p.Name;
                        r.Name = grid.Name;
                        r.ElementId = grid.Id.GetIdValue();
                        r.Status = "created";
                    }
                    catch (Exception ex)
                    {
                        r.Status = "failed"; r.Reason = ex.Message;
                    }
                }

                if (tr != null)
                {
                    var status = tr.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        foreach (var r in results.Where(x => x.Status == "created"))
                        {
                            r.Status = "failed"; r.ElementId = null; r.Reason = $"Transaction 未提交（{status}）";
                        }
                    }
                }
            }
            finally
            {
                if (tr != null)
                {
                    if (tr.GetStatus() == TransactionStatus.Started) tr.RollBack();
                    tr.Dispose();
                }
            }

            return results;
        }

        /// <summary>兩線段平行、垂直距離 &lt; 10mm 且投影有重疊 → 視為同一條網格線。</summary>
        private static bool IsSameGridLine(XYZ a1, XYZ a2, XYZ b1, XYZ b2)
        {
            var fa = new XYZ(a1.X, a1.Y, 0); var ta = new XYZ(a2.X, a2.Y, 0);
            var fb = new XYZ(b1.X, b1.Y, 0); var tb = new XYZ(b2.X, b2.Y, 0);
            var d = (ta - fa).Normalize();
            var db = (tb - fb).Normalize();
            if (Math.Abs(d.CrossProduct(db).Z) > 0.001) return false;

            var perp = new XYZ(-d.Y, d.X, 0);
            if (Math.Abs((fb - fa).DotProduct(perp)) > GridDupTolFt) return false;

            double aMin = 0, aMax = (ta - fa).DotProduct(d);
            double s1 = (fb - fa).DotProduct(d), s2 = (tb - fa).DotProduct(d);
            return Math.Max(s1, s2) > aMin - GridDupTolFt && Math.Min(s1, s2) < aMax + GridDupTolFt;
        }

        #endregion
    }
}
