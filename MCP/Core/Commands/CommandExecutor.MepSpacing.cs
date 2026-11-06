using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json.Linq;
using RevitMCP.Core.Review;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// check_mep_spacing — 管線間預留淨距檢討（方法見 domain/mep-spacing-clearance-check.md）
    ///
    /// A. 管線對管線（管、風管、電纜架、導線管；主模型 + 連結）
    ///    每段直管視為「中心線段 + 外輪廓（含保溫）」；以分隔軸定理（SAT）在數個候選軸上
    ///    計算兩者投影的最大分離量，作為淨距的保守估計（平行管、交叉管為精確值；
    ///    斜向錯位的矩形管可能略為低估 → 寧可多報不漏報）。
    ///    經由管件 / 附件在 3 跳以內相連的管段視為同一路由，不互相比較。
    /// B. 管線對結構 / 其他類別（checkAgainst）
    ///    以「外輪廓外擴所需淨距」的擠出實體做 ElementIntersectsSolidFilter；
    ///    命中者再以未外擴實體判斷是「碰撞」還是「淨距不足」。
    /// </summary>
    public partial class CommandExecutor
    {
        private object CheckMepSpacing(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument?.Document ?? throw new Exception("目前沒有開啟的專案");

            double defaultClearMm = parameters["clearanceMm"]?.Value<double>()
                ?? throw new Exception("請提供 clearanceMm（要求的外緣淨距 mm）。本工具不代入預設值——各專案預留規範差異大，請由使用者明確指定。");
            if (defaultClearMm < 0) throw new Exception("clearanceMm 不可為負值");
            bool includeLinks = parameters["includeLinks"]?.Value<bool>() ?? true;
            bool includeInsulation = parameters["includeInsulation"]?.Value<bool>() ?? true;
            int maxResults = Math.Max(1, Math.Min(2000, parameters["maxResults"]?.Value<int>() ?? 300));
            bool createMarkers = parameters["createMarkers"]?.Value<bool>() ?? false;
            bool highlight = parameters["highlightInView"]?.Value<bool>() ?? false;
            var levelNames = (parameters["levels"] as JArray)?.Select(t => t.Value<string>()).ToList();
            var catNames = (parameters["categories"] as JArray)?.Select(t => t.Value<string>()).ToList()
                           ?? new List<string> { "pipes", "ducts", "cable_trays", "conduits" };
            var cats = catNames.Select(ReviewUtil.ParseCategory).Distinct().ToList();
            var againstNames = (parameters["checkAgainst"] as JArray)?.Select(t => t.Value<string>()).ToList() ?? new List<string>();
            var againstCats = againstNames.Select(ReviewUtil.ParseCategory).Distinct().ToList();
            double againstClearMm = parameters["againstClearanceMm"]?.Value<double>() ?? defaultClearMm;

            var rules = new List<(string a, string b, double mm)>();
            if (parameters["rules"] is JArray ra)
            {
                foreach (var r in ra.OfType<JObject>())
                {
                    string a = ReviewUtil.CategoryKey(ReviewUtil.ParseCategory(r["a"]?.Value<string>()));
                    string b = ReviewUtil.CategoryKey(ReviewUtil.ParseCategory(r["b"]?.Value<string>()));
                    double mm = r["clearanceMm"]?.Value<double>() ?? throw new Exception("rules 每一筆都需要 clearanceMm");
                    rules.Add((a, b, mm));
                }
            }
            double Required(string ka, string kb)
            {
                foreach (var r in rules)
                    if ((r.a == ka && r.b == kb) || (r.a == kb && r.b == ka)) return r.mm;
                return defaultClearMm;
            }
            double maxRequiredFt = ReviewUtil.Ft(Math.Max(defaultClearMm, rules.Count > 0 ? rules.Max(r => r.mm) : 0));

            var bands = ReviewUtil.LevelBands(doc, levelNames);
            var sources = ReviewUtil.GetSourceDocs(doc, includeLinks);

            // ── 收集管段 ──
            var items = new List<MepItem>();
            int skippedNonLinear = 0;
            for (int si = 0; si < sources.Count; si++)
            {
                var src = sources[si];
                foreach (Element e in new FilteredElementCollector(src.Doc).WherePasses(new ElementMulticategoryFilter(cats)).WhereElementIsNotElementType())
                {
                    if (!(e is MEPCurve mc)) continue;
                    var it = BuildMepItem(mc, src, si, includeInsulation);
                    if (it == null) { skippedNonLinear++; continue; }
                    if (!ReviewUtil.InBands(bands, (it.P0.Z + it.P1.Z) / 2)) continue;
                    items.Add(it);
                }
            }

            // ── A. 管線對管線：網格加速 ──
            var violations = new List<SpacingViolation>();
            double cell = ReviewUtil.Ft(2000);
            var grid = new Dictionary<(long, long, long), List<int>>();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                double pad = it.MaxExtent + maxRequiredFt;
                XYZ mn = new XYZ(Math.Min(it.P0.X, it.P1.X) - pad, Math.Min(it.P0.Y, it.P1.Y) - pad, Math.Min(it.P0.Z, it.P1.Z) - pad);
                XYZ mx = new XYZ(Math.Max(it.P0.X, it.P1.X) + pad, Math.Max(it.P0.Y, it.P1.Y) + pad, Math.Max(it.P0.Z, it.P1.Z) + pad);
                it.BoxMin = mn; it.BoxMax = mx;
                long x0 = (long)Math.Floor(mn.X / cell), x1 = (long)Math.Floor(mx.X / cell);
                long y0 = (long)Math.Floor(mn.Y / cell), y1 = (long)Math.Floor(mx.Y / cell);
                long z0 = (long)Math.Floor(mn.Z / cell), z1 = (long)Math.Floor(mx.Z / cell);
                if ((x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 20000) continue; // 異常巨大的元素略過網格
                for (long x = x0; x <= x1; x++)
                    for (long y = y0; y <= y1; y++)
                        for (long z = z0; z <= z1; z++)
                        {
                            var k = (x, y, z);
                            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                            l.Add(i);
                        }
            }

            var seen = new HashSet<long>();
            int pairsChecked = 0;
            foreach (var bucket in grid.Values)
            {
                for (int a = 0; a < bucket.Count; a++)
                    for (int b = a + 1; b < bucket.Count; b++)
                    {
                        int i = Math.Min(bucket[a], bucket[b]), j = Math.Max(bucket[a], bucket[b]);
                        if (!seen.Add((long)i * 1000003L + j)) continue;
                        var A = items[i]; var B = items[j];
                        if (A.DocIndex == B.DocIndex && (A.Connected.Contains(B.Id) || B.Connected.Contains(A.Id))) continue;
                        if (A.BoxMax.X < B.BoxMin.X || B.BoxMax.X < A.BoxMin.X ||
                            A.BoxMax.Y < B.BoxMin.Y || B.BoxMax.Y < A.BoxMin.Y ||
                            A.BoxMax.Z < B.BoxMin.Z || B.BoxMax.Z < A.BoxMin.Z) continue;

                        pairsChecked++;
                        double req = ReviewUtil.Ft(Required(A.CatKey, B.CatKey));
                        double gap = EstimateGap(A, B, out XYZ where);
                        if (gap + 1e-6 < req)
                            violations.Add(new SpacingViolation { A = A, B = B, Gap = gap, Required = req, Point = where, Kind = "MEP-MEP" });
                    }
            }

            // ── B. 管線對結構 / 其他類別 ──
            int againstQueries = 0;
            if (againstCats.Count > 0)
            {
                double c = ReviewUtil.Ft(againstClearMm);
                var againstFilter = new ElementMulticategoryFilter(againstCats);
                foreach (var it in items)
                {
                    Solid env = ReviewUtil.SweepBox(it.P0, it.P1, it.Round, it.R + c, it.Ax, it.Ay, it.HW + c, it.HH + c);
                    if (env == null) continue;
                    Solid body = null; bool bodyBuilt = false;
                    for (int ti = 0; ti < sources.Count; ti++)
                    {
                        var tsrc = sources[ti];
                        againstQueries++;
                        Transform toLocal = tsrc.ToHost.Inverse;
                        Solid envLocal = tsrc.IsHost ? env : SafeTransform(env, toLocal);
                        if (envLocal == null) continue;
                        Outline ol = LocalOutline(it, c, toLocal);
                        List<Element> hits;
                        try
                        {
                            hits = new FilteredElementCollector(tsrc.Doc).WherePasses(againstFilter).WhereElementIsNotElementType()
                                .WherePasses(new BoundingBoxIntersectsFilter(ol))
                                .WherePasses(new ElementIntersectsSolidFilter(envLocal)).ToElements().ToList();
                        }
                        catch { continue; }
                        foreach (var h in hits)
                        {
                            if (ti == it.DocIndex && h.Id.GetIdValue() == it.Id) continue;
                            if (!bodyBuilt) { body = ReviewUtil.SweepBox(it.P0, it.P1, it.Round, it.R, it.Ax, it.Ay, it.HW, it.HH); bodyBuilt = true; }
                            bool clash = false;
                            if (body != null)
                            {
                                Solid bodyLocal = tsrc.IsHost ? body : SafeTransform(body, toLocal);
                                if (bodyLocal != null)
                                {
                                    try
                                    {
                                        clash = new FilteredElementCollector(tsrc.Doc, new List<ElementId> { h.Id })
                                            .WherePasses(new ElementIntersectsSolidFilter(bodyLocal)).GetElementCount() > 0;
                                    }
                                    catch { }
                                }
                            }
                            var hb = ReviewUtil.HostBox(h, tsrc.ToHost);
                            XYZ where = hb != null ? ClosestOnSegment(it.P0, it.P1, (hb.Value.min + hb.Value.max) / 2) : (it.P0 + it.P1) / 2;
                            violations.Add(new SpacingViolation
                            {
                                A = it,
                                Other = new OtherRef
                                {
                                    Id = h.Id.GetIdValue(),
                                    Source = tsrc.Label,
                                    IsHost = tsrc.IsHost,
                                    Category = h.Category?.Name,
                                    Type = ReviewUtil.TypeName(h),
                                    Level = ReviewUtil.LevelName(h)
                                },
                                Gap = clash ? -1 : (double?)null,
                                Required = c,
                                Point = where,
                                Kind = "MEP-" + (h.Category?.Name ?? "其他")
                            });
                        }
                    }
                }
            }

            var ordered = violations.OrderBy(v => v.Gap ?? 0).ToList();

            int markers = 0, highlighted = 0;
            if (createMarkers && ordered.Count > 0)
                markers = ReviewUtil.PlaceMarkers(doc, "mep_spacing",
                    ordered.Take(maxResults).Select(v => (v.Point, v.Label())).ToList(), 200);
            if (highlight && ordered.Count > 0)
            {
                var hostIds = new List<ElementId>();
                foreach (var v in ordered)
                {
                    if (v.A.DocIndex == 0) hostIds.Add(new ElementId(v.A.Id));
                    if (v.B != null && v.B.DocIndex == 0) hostIds.Add(new ElementId(v.B.Id));
                    if (v.Other != null && v.Other.IsHost) hostIds.Add(new ElementId(v.Other.Id));
                }
                highlighted = ReviewUtil.HighlightInResultView(doc, hostIds, new Color(255, 140, 0));
            }

            var byPair = ordered.GroupBy(v => v.B != null ? MepPairKey(v.A.CatName, v.B.CatName) : $"{v.A.CatName} ↔ {v.Other.Category}")
                .Select(g => new { pair = g.Key, count = g.Count(), clashes = g.Count(v => v.Gap != null && v.Gap < 0) })
                .OrderByDescending(x => x.count).ToList();

            return new
            {
                method = "domain/mep-spacing-clearance-check.md",
                defaultClearanceMm = defaultClearMm,
                rules = rules.Select(r => new { a = r.a, b = r.b, clearanceMm = r.mm }).ToList(),
                checkAgainst = againstNames,
                againstClearanceMm = againstCats.Count > 0 ? againstClearMm : (double?)null,
                segmentsChecked = items.Count,
                skippedNonLinear,
                pairsEvaluated = pairsChecked,
                againstQueries,
                violationCount = ordered.Count,
                clashCount = ordered.Count(v => v.Gap != null && v.Gap < 0),
                summaryByPair = byPair,
                markersPlaced = markers,
                highlightedInView = highlighted,
                resultView = highlighted > 0 || markers > 0 ? ReviewUtil.ResultViewName : null,
                accuracyNote = "管對管淨距為分隔軸法保守估計（平行/交叉精確，斜向錯位矩形管可能低估）；管對結構只判定碰撞或小於要求，不給精確淨距；彎頭/三通等管件本身未納入管對管比較",
                items = ordered.Take(maxResults).Select(v => v.ToJson()).ToList()
            };
        }

        private static string MepPairKey(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? $"{a} ↔ {b}" : $"{b} ↔ {a}";

        private static Solid SafeTransform(Solid s, Transform t)
        {
            try { return SolidUtils.CreateTransformed(s, t); } catch { return null; }
        }

        private static Outline LocalOutline(MepItem it, double c, Transform toLocal)
        {
            double pad = it.MaxExtent + c;
            var pts = new List<XYZ>();
            foreach (var p in new[] { it.P0, it.P1 })
                foreach (double dx in new[] { -pad, pad })
                    foreach (double dy in new[] { -pad, pad })
                        foreach (double dz in new[] { -pad, pad })
                            pts.Add(toLocal.OfPoint(p + new XYZ(dx, dy, dz)));
            return new Outline(new XYZ(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)),
                               new XYZ(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z)));
        }

        private static XYZ ClosestOnSegment(XYZ a, XYZ b, XYZ p)
        {
            XYZ ab = b - a;
            double l2 = ab.DotProduct(ab);
            if (l2 < 1e-12) return a;
            double t = Math.Max(0, Math.Min(1, (p - a).DotProduct(ab) / l2));
            return a + ab * t;
        }

        // ───────────────────────── 管段模型 ─────────────────────────

        private MepItem BuildMepItem(MEPCurve mc, SourceDoc src, int docIndex, bool includeInsulation)
        {
            if (!(mc.Location is LocationCurve lc) || !(lc.Curve is Line line)) return null;
            XYZ p0 = src.ToHost.OfPoint(line.GetEndPoint(0));
            XYZ p1 = src.ToHost.OfPoint(line.GetEndPoint(1));
            XYZ d = p1 - p0;
            if (d.GetLength() < 1e-4) return null;
            d = d.Normalize();

            var bic = ReviewUtil.GetBic(mc) ?? BuiltInCategory.INVALID;
            var it = new MepItem
            {
                Id = mc.Id.GetIdValue(),
                DocIndex = docIndex,
                Source = src.Label,
                P0 = p0, P1 = p1, D = d,
                CatKey = ReviewUtil.CategoryKey(bic),
                CatName = mc.Category?.Name ?? bic.ToString(),
                System = SafeStr(mc, BuiltInParameter.RBS_SYSTEM_NAME_PARAM),
                Size = SafeStr(mc, BuiltInParameter.RBS_CALCULATED_SIZE),
                Level = mc.ReferenceLevel?.Name ?? ""
            };

            // 斷面：先找端點接頭
            Connector end = null;
            try
            {
                foreach (Connector c in mc.ConnectorManager.Connectors)
                    if (c.ConnectorType == ConnectorType.End) { end = c; break; }
            }
            catch { }

            double outer = 0;
            if (mc is Pipe) outer = SafeDbl(mc, BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
            else if (mc is Conduit) outer = SafeDbl(mc, BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM);

            bool rect = end != null && end.Shape != ConnectorProfileType.Round && !(mc is Pipe) && !(mc is Conduit);
            if (rect)
            {
                it.Round = false;
                it.HW = SafeConn(() => end.Width) / 2;
                it.HH = SafeConn(() => end.Height) / 2;
                XYZ ax = null, ay = null;
                try
                {
                    ax = src.ToHost.OfVector(end.CoordinateSystem.BasisX).Normalize();
                    ay = src.ToHost.OfVector(end.CoordinateSystem.BasisY).Normalize();
                }
                catch { }
                if (ax == null || ay == null || Math.Abs(ax.DotProduct(d)) > 0.1)
                {
                    ax = ReviewUtil.AnyPerpendicular(d);
                }
                // 一律正交化，確保 (ax, ay, d) 為正交基底
                ay = d.CrossProduct(ax).Normalize();
                ax = ay.CrossProduct(d).Normalize();
                // 保險：若接頭寬高抓不到，改用參數
                if (it.HW <= 0) it.HW = Math.Max(SafeDbl(mc, BuiltInParameter.RBS_CURVE_WIDTH_PARAM), SafeDbl(mc, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)) / 2;
                if (it.HH <= 0) it.HH = Math.Max(SafeDbl(mc, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM), SafeDbl(mc, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)) / 2;
                it.Ax = ax; it.Ay = ay;
            }
            else
            {
                it.Round = true;
                if (outer <= 0) outer = SafeDbl(mc, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                if (outer <= 0 && end != null) outer = SafeConn(() => end.Radius) * 2;
                if (outer <= 0) outer = SafeConn(() => mc.Diameter);
                it.R = outer / 2;
                it.Ax = ReviewUtil.AnyPerpendicular(d);
                it.Ay = d.CrossProduct(it.Ax).Normalize();
            }

            if (includeInsulation)
            {
                double ins = SafeDbl(mc, BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS);
                if (ins > 0) { it.R += ins; it.HW += ins; it.HH += ins; it.Insulation = ins; }
            }
            if (it.Round && it.R <= 0) return null;
            if (!it.Round && (it.HW <= 0 || it.HH <= 0)) return null;
            it.MaxExtent = it.Round ? it.R : Math.Sqrt(it.HW * it.HW + it.HH * it.HH);

            it.Connected = ConnectedCurves(mc, 3);
            return it;
        }

        private static string SafeStr(Element e, BuiltInParameter bip)
        {
            try { var p = e.get_Parameter(bip); return p?.AsString() ?? p?.AsValueString() ?? ""; } catch { return ""; }
        }

        private static double SafeDbl(Element e, BuiltInParameter bip)
        {
            try { var p = e.get_Parameter(bip); return p != null && p.HasValue && p.StorageType == StorageType.Double ? p.AsDouble() : 0; } catch { return 0; }
        }

        private static double SafeConn(Func<double> f)
        {
            try { return f(); } catch { return 0; }
        }

        /// <summary>經由管件 / 附件 / 設備，在 maxFittingHops 個族群實例以內可達的其他管段（同一文件）。</summary>
        private static HashSet<IdType> ConnectedCurves(MEPCurve start, int maxFittingHops)
        {
            var result = new HashSet<IdType>();
            var visited = new HashSet<IdType> { start.Id.GetIdValue() };
            var queue = new Queue<(Element el, int depth)>();
            queue.Enqueue((start, 0));
            while (queue.Count > 0)
            {
                var (el, depth) = queue.Dequeue();
                foreach (var owner in PhysicalNeighbors(el))
                {
                    IdType oid = owner.Id.GetIdValue();
                    if (!visited.Add(oid)) continue;
                    if (owner is MEPCurve) result.Add(oid);
                    else if (owner is FamilyInstance && depth < maxFittingHops) queue.Enqueue((owner, depth + 1));
                }
            }
            return result;
        }

        private static IEnumerable<Element> PhysicalNeighbors(Element el)
        {
            ConnectorSet set = null;
            try
            {
                if (el is MEPCurve mc) set = mc.ConnectorManager?.Connectors;
                else if (el is FamilyInstance fi) set = fi.MEPModel?.ConnectorManager?.Connectors;
            }
            catch { }
            if (set == null) yield break;
            foreach (Connector c in set)
            {
                ConnectorSet refs = null;
                try { if (c.IsConnected) refs = c.AllRefs; } catch { }
                if (refs == null) continue;
                foreach (Connector r in refs)
                {
                    Element o = null;
                    try
                    {
                        if (r.ConnectorType == ConnectorType.Logical) continue;
                        o = r.Owner;
                    }
                    catch { }
                    if (o != null && o.Id != el.Id) yield return o;
                }
            }
        }

        // ───────────────────────── 淨距估算（SAT） ─────────────────────────

        private static double EstimateGap(MepItem A, MepItem B, out XYZ where)
        {
            ClosestSegSeg(A.P0, A.P1, B.P0, B.P1, out XYZ pa, out XYZ pb);
            where = (pa + pb) / 2;

            var axes = new List<XYZ>();
            XYZ n = pb - pa;
            if (n.GetLength() > 1e-6) axes.Add(n.Normalize());
            XYZ cr = A.D.CrossProduct(B.D);
            if (cr.GetLength() > 1e-6) axes.Add(cr.Normalize());
            if (!A.Round) { axes.Add(A.Ax); axes.Add(A.Ay); }
            if (!B.Round) { axes.Add(B.Ax); axes.Add(B.Ay); }
            // 平行圓管：最近點連線方向即精確軸；若中心線重合則補一條垂直軸
            if (axes.Count == 0) axes.Add(ReviewUtil.AnyPerpendicular(A.D));

            double best = double.NegativeInfinity;
            foreach (var ax in axes)
            {
                SatProject(A, ax, out double aLo, out double aHi);
                SatProject(B, ax, out double bLo, out double bHi);
                double sep = Math.Max(bLo - aHi, aLo - bHi);
                if (sep > best) best = sep;
            }
            return best;
        }

        private static void SatProject(MepItem it, XYZ ax, out double lo, out double hi)
        {
            double t0 = it.P0.DotProduct(ax), t1 = it.P1.DotProduct(ax);
            double h = it.Round
                ? it.R * Math.Sqrt(Math.Max(0, 1 - Math.Pow(ax.DotProduct(it.D), 2)))
                : it.HW * Math.Abs(ax.DotProduct(it.Ax)) + it.HH * Math.Abs(ax.DotProduct(it.Ay));
            lo = Math.Min(t0, t1) - h;
            hi = Math.Max(t0, t1) + h;
        }

        /// <summary>兩線段最近點（Ericson, Real-Time Collision Detection 5.1.9）。</summary>
        private static void ClosestSegSeg(XYZ p1, XYZ q1, XYZ p2, XYZ q2, out XYZ c1, out XYZ c2)
        {
            XYZ d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            double a = d1.DotProduct(d1), e = d2.DotProduct(d2), f = d2.DotProduct(r);
            double s, t;
            const double Eps = 1e-12;
            if (a <= Eps && e <= Eps) { c1 = p1; c2 = p2; return; }
            if (a <= Eps) { s = 0; t = Clamp01(f / e); }
            else
            {
                double c = d1.DotProduct(r);
                if (e <= Eps) { t = 0; s = Clamp01(-c / a); }
                else
                {
                    double b = d1.DotProduct(d2), denom = a * e - b * b;
                    s = denom > Eps ? Clamp01((b * f - c * e) / denom) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Clamp01(-c / a); }
                    else if (t > 1) { t = 1; s = Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        private sealed class MepItem
        {
            public IdType Id;
            public int DocIndex;
            public string Source, CatKey, CatName, System, Size, Level;
            public XYZ P0, P1, D, Ax, Ay, BoxMin, BoxMax;
            public bool Round;
            public double R, HW, HH, MaxExtent, Insulation;
            public HashSet<IdType> Connected = new HashSet<IdType>();

            public object ToJson() => new
            {
                elementId = Id,
                source = Source,
                category = CatName,
                system = System,
                size = Size,
                level = Level,
                insulationMm = Insulation > 0 ? ReviewUtil.Mm(Insulation) : (double?)null
            };
        }

        private sealed class OtherRef
        {
            public IdType Id;
            public string Source, Category, Type, Level;
            public bool IsHost;
        }

        private sealed class SpacingViolation
        {
            public MepItem A, B;
            public OtherRef Other;
            public double? Gap;      // null = 小於要求但未精算；<0 = 碰撞
            public double Required;
            public XYZ Point;
            public string Kind;

            public string Status => Gap != null && Gap < 0 ? "CLASH" : "INSUFFICIENT_CLEARANCE";

            public string Label()
            {
                string other = B != null ? $"{B.CatName}#{B.Id}" : $"{Other.Category}#{Other.Id}";
                string g = Gap == null ? $"< {ReviewUtil.Mm(Required)}" : Gap < 0 ? "碰撞" : $"{ReviewUtil.Mm(Gap.Value)}";
                return $"管線淨距 {g}mm（要求 {ReviewUtil.Mm(Required)}）｜{A.CatName}#{A.Id} ↔ {other}";
            }

            public object ToJson() => new
            {
                kind = Kind,
                status = Status,
                gapMm = Gap == null ? (double?)null : Gap < 0 ? 0 : ReviewUtil.Mm(Gap.Value),
                requiredMm = ReviewUtil.Mm(Required),
                a = A.ToJson(),
                b = B?.ToJson(),
                other = Other == null ? null : new { elementId = Other.Id, source = Other.Source, category = Other.Category, type = Other.Type, level = Other.Level },
                pointMm = ReviewUtil.PointMm(Point)
            };
        }
    }
}
