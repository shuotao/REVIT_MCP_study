using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// RFA 族群編輯工具第三批：參考線（rfa_create_reference_line）。
    ///
    /// 為什麼需要它：族群檔不支援 SketchEditScope，而在草圖模式外對「草圖曲線」標尺寸／對齊，
    /// Revit 雖會接受但不會正確約束（實測：圓心尺寸建立後圓直接跑掉）。參考線不是草圖元素，
    /// 對齊、標註、綁參數都走一般路徑，所以用它當骨架：圓管＝沿參考線掃掠（rfa_create_sweep 的
    /// pathReferenceLineIds），位置交給參考線，管徑交給掃掠斷面的直徑標籤（這一項已實測可驅動）。
    /// </summary>
    public partial class CommandExecutor
    {
        #region rfa_create_reference_line

        private static readonly ViewType[] RfaConstraintViewTypes =
            { ViewType.Elevation, ViewType.Section, ViewType.FloorPlan, ViewType.CeilingPlan };

        /// <summary>
        /// 找一個可以建立此約束的視圖：參考平面要側看成線（視線 ⟂ 平面法向量），
        /// 並滿足額外條件（mustBePerp：視線需垂直的方向；mustBeParallel：視線需平行的方向）。立面優先，平面其次。
        /// </summary>
        private static View RfaPickConstraintView(Document doc, XYZ planeNormal, XYZ mustBePerp, XYZ mustBeParallel, string viewName)
        {
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && RfaConstraintViewTypes.Contains(v.ViewType)).ToList();
            if (!string.IsNullOrEmpty(viewName))
                return views.FirstOrDefault(v => v.Name == viewName) ?? throw new Exception($"找不到視圖「{viewName}」");

            foreach (var v in views.OrderBy(v => Array.IndexOf(RfaConstraintViewTypes, v.ViewType)))
            {
                XYZ d;
                try { d = v.ViewDirection.Normalize(); } catch { continue; }
                if (planeNormal != null && Math.Abs(d.DotProduct(planeNormal)) > 1e-6) continue;
                if (mustBePerp != null && Math.Abs(d.DotProduct(mustBePerp)) > 1e-6) continue;
                if (mustBeParallel != null && Math.Abs(Math.Abs(d.DotProduct(mustBeParallel)) - 1) > 1e-6) continue;
                return v;
            }
            throw new Exception("找不到合適的視圖（需要能把參考平面側看成線的平面圖或立面）");
        }

        /// <summary>平面圖的視圖範圍若切不到 zMax，就把頂部與剖切面往上拉（族群編輯器的平面圖預設範圍有限，看不到的元素無法標註）</summary>
        private static string RfaEnsurePlanShows(Document doc, View view, double zMaxFt)
        {
            if (!(view is ViewPlan vp) || vp.ViewType != ViewType.FloorPlan) return null;
            PlanViewRange range = vp.GetViewRange();
            double cut = range.GetOffset(PlanViewPlane.CutPlane);
            if (zMaxFt < cut - RfaMm(1)) return null;
            double newCut = zMaxFt + RfaMm(500);
            if (range.GetOffset(PlanViewPlane.TopClipPlane) < newCut + RfaMm(100))
                range.SetOffset(PlanViewPlane.TopClipPlane, newCut + RfaMm(500));
            range.SetOffset(PlanViewPlane.CutPlane, newCut);
            vp.SetViewRange(range);
            return $"平面圖「{vp.Name}」的剖切面調到 {RfaToMm(newCut)} mm，才能標註較高的參考線";
        }

        private object RfaCreateReferenceLine(JObject parameters)
        {
            Document doc = GetActiveFamilyDocument();
            FamilyManager fm = doc.FamilyManager;
            var warnings = new List<string>();
            double tol = RfaMm(0.5);

            string hostName = parameters["hostPlane"]?.Value<string>()
                              ?? throw new Exception("請提供 hostPlane（參考線所在的參考平面，平面移動時參考線跟著移動）");
            ReferencePlane hostRp = RfaFindRefPlane(doc, hostName);
            XYZ hn = hostRp.Normal.Normalize();
            XYZ ho = hostRp.GetPlane().Origin;

            // ── 幾何：直線或三點圓弧，點都要落在 hostPlane 上 ──
            Curve curve;
            bool isArc;
            if (parameters["line"] is JObject ln)
            {
                curve = Line.CreateBound(RfaReadXyzMm(ln["startMm"], "line.startMm"), RfaReadXyzMm(ln["endMm"], "line.endMm"));
                isArc = false;
            }
            else if (parameters["arc"] is JObject ar)
            {
                curve = Arc.Create(RfaReadXyzMm(ar["startMm"], "arc.startMm"), RfaReadXyzMm(ar["endMm"], "arc.endMm"), RfaReadXyzMm(ar["midMm"], "arc.midMm"));
                isArc = true;
            }
            else throw new Exception("請提供 line {startMm, endMm} 或 arc {startMm, midMm, endMm}");

            foreach (XYZ p in new[] { curve.GetEndPoint(0), curve.Evaluate(0.5, true), curve.GetEndPoint(1) })
                if (Math.Abs((p - ho).DotProduct(hn)) > tol)
                    throw new Exception($"參考線不在 hostPlane「{hostName}」上（點 {RfaToMm(p.X)},{RfaToMm(p.Y)},{RfaToMm(p.Z)} 偏離 {RfaToMm((p - ho).DotProduct(hn))} mm）");

            var results = new List<object>();
            ModelCurve mc;
            using (var t = new Transaction(doc, "MCP: rfa_create_reference_line"))
            {
                var fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new SilentFailuresPreprocessor());
                t.SetFailureHandlingOptions(fho);
                t.Start();

                SketchPlane sp = SketchPlane.Create(doc, hostRp.Id);
                mc = doc.FamilyCreate.NewModelCurve(curve, sp) ?? throw new Exception("建立參考線失敗（Revit 回傳 null）");
                mc.ChangeToReferenceLine();
                doc.Regenerate();
                Curve g = mc.GeometryCurve;
                double zMax = Math.Max(g.GetEndPoint(0).Z, Math.Max(g.GetEndPoint(1).Z, g.Evaluate(0.5, true).Z));

                // ── 約束：curve / start / end / center → 參考平面（對齊或距離參數）──
                if (parameters["constraints"] is JArray cs)
                {
                    foreach (JObject c in cs.OfType<JObject>())
                    {
                        string target = (c["target"]?.Value<string>() ?? "curve").Trim().ToLowerInvariant();
                        string pn = c["referencePlane"]?.Value<string>() ?? throw new Exception("constraints 每項需要 referencePlane");
                        string dp = c["distanceParameter"]?.Value<string>();
                        ReferencePlane rp = RfaFindRefPlane(doc, pn);
                        XYZ n = rp.Normal.Normalize();
                        XYZ o = rp.GetPlane().Origin;

                        Reference r;
                        XYZ at;
                        XYZ perp = null, parallel = null;
                        switch (target)
                        {
                            case "curve":
                                if (isArc) throw new Exception("圓弧不能以 curve 對齊參考平面；請改用 center 或 start / end");
                                XYZ dir = ((Line)g).Direction.Normalize();
                                if (Math.Abs(dir.DotProduct(n)) > 1e-6) throw new Exception($"參考線不平行於參考平面「{pn}」，無法以 curve 約束");
                                r = g.Reference; at = g.Evaluate(0.5, true); perp = dir;
                                break;
                            case "start":
                            case "end":
                                r = g.GetEndPointReference(target == "start" ? 0 : 1);
                                at = g.GetEndPoint(target == "start" ? 0 : 1);
                                if (isArc) parallel = ((Arc)g).Normal; else perp = ((Line)g).Direction;
                                break;
                            case "center":
                                if (!isArc) throw new Exception("只有圓弧有 center");
                                r = mc.CenterPointReference; at = ((Arc)g).Center; parallel = ((Arc)g).Normal;
                                break;
                            default:
                                throw new Exception($"不支援的 target「{target}」（可用: curve, start, end, center）");
                        }
                        if (r == null) throw new Exception($"取不到 {target} 的參考");

                        double dist = Math.Abs((at - o).DotProduct(n));
                        if (!string.IsNullOrEmpty(dp))
                        {
                            FamilyParameter fp = RfaFindParam(fm, dp) ?? throw new Exception($"找不到族群參數「{dp}」");
                            double want = fm.CurrentType?.AsDouble(fp) ?? -1;
                            if (Math.Abs(want - dist) > tol)
                                throw new Exception($"{target} 到「{pn}」的距離是 {RfaToMm(dist)} mm，但參數「{dp}」是 {RfaToMm(want)} mm");
                        }
                        else if (dist > tol)
                            throw new Exception($"{target} 不在參考平面「{pn}」上（相距 {RfaToMm(dist)} mm），要對齊請先讓它落在平面上，否則給 distanceParameter");

                        View view = RfaPickConstraintView(doc, n, perp, parallel, c["viewName"]?.Value<string>());
                        string note = RfaEnsurePlanShows(doc, view, zMax);
                        if (note != null && !warnings.Contains(note)) warnings.Add(note);

                        try
                        {
                            if (string.IsNullOrEmpty(dp))
                            {
                                Dimension al = doc.FamilyCreate.NewAlignment(view, rp.GetReference(), r);
                                al.IsLocked = true;
                                results.Add(new { target, kind = "align", referencePlane = pn, id = al.Id.GetIdValue(), view = view.Name });
                            }
                            else
                            {
                                XYZ side = view.ViewDirection.Normalize().CrossProduct(n).Normalize();
                                XYZ basePt = at + side * RfaMm(200);
                                double sP = o.DotProduct(n), sB = basePt.DotProduct(n);
                                XYZ a = basePt + n * (Math.Min(sP, sB) - sB - RfaMm(50));
                                XYZ b = basePt + n * (Math.Max(sP, sB) - sB + RfaMm(50));
                                var refs = new ReferenceArray();
                                refs.Append(rp.GetReference());
                                refs.Append(r);
                                Dimension d = doc.FamilyCreate.NewDimension(view, Line.CreateBound(a, b), refs);
                                d.FamilyLabel = RfaFindParam(fm, dp);
                                results.Add(new { target, kind = "distance", referencePlane = pn, label = dp, id = d.Id.GetIdValue(), view = view.Name });
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"約束 {target} →「{pn}」失敗（視圖「{view.Name}」）：{ex.Message}");
                        }
                    }
                }

                // ── 圓弧半徑標籤 ──
                string radParam = parameters["radiusParameter"]?.Value<string>();
                if (!string.IsNullOrEmpty(radParam))
                {
                    if (!isArc) throw new Exception("radiusParameter 只適用於圓弧");
                    var arc = (Arc)g;
                    FamilyParameter fp = RfaFindParam(fm, radParam) ?? throw new Exception($"找不到族群參數「{radParam}」");
                    double want = fm.CurrentType?.AsDouble(fp) ?? -1;
                    if (Math.Abs(want - arc.Radius) > tol)
                        throw new Exception($"圓弧半徑是 {RfaToMm(arc.Radius)} mm，但參數「{radParam}」是 {RfaToMm(want)} mm");
                    View view = RfaPickConstraintView(doc, null, null, arc.Normal, parameters["viewName"]?.Value<string>());
                    string note = RfaEnsurePlanShows(doc, view, zMax);
                    if (note != null && !warnings.Contains(note)) warnings.Add(note);
                    XYZ midDir = (arc.Evaluate(0.5, true) - arc.Center).Normalize();
                    Dimension rd = doc.FamilyCreate.NewRadialDimension(view, arc.Reference, arc.Center + midDir * arc.Radius * 0.6);
                    rd.FamilyLabel = fp;
                    results.Add(new { target = "radius", kind = "radius", label = radParam, id = rd.Id.GetIdValue(), view = view.Name });
                }

                if (t.Commit() != TransactionStatus.Committed)
                    throw new Exception("Revit 拒絕了參考線或其約束（交易被復原，可能是過度約束）");
            }

            Curve fin = mc.GeometryCurve;
            return new
            {
                id = mc.Id.GetIdValue(),
                kind = isArc ? "arc" : "line",
                hostPlane = hostName,
                startMm = RfaPt(fin.GetEndPoint(0)),
                endMm = RfaPt(fin.GetEndPoint(1)),
                lengthMm = RfaToMm(fin.Length),
                constraints = results,
                warnings,
            };
        }

        #endregion
    }
}
