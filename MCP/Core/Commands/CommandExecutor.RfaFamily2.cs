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
    /// RFA 族群編輯工具第二批：尺寸標註＋參數綁定、面對齊鎖定、掃掠、融合。
    /// 共用 CommandExecutor.RfaFamily.cs 的輔助函式（單位換算、輪廓、草圖平面）。
    /// </summary>
    public partial class CommandExecutor
    {
        #region 共用

        private static ReferencePlane RfaFindRefPlane(Document doc, string name)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>()
                       .FirstOrDefault(r => r.Name == name)
                   ?? throw new Exception($"找不到參考平面「{name}」");
        }

        /// <summary>找一個「看得到這組平行平面為線」的視圖：視線方向與平面法向量垂直</summary>
        private static View RfaPickViewForNormal(Document doc, XYZ normal, string viewName)
        {
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate &&
                            (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan ||
                             v.ViewType == ViewType.Elevation || v.ViewType == ViewType.Section))
                .ToList();

            if (!string.IsNullOrEmpty(viewName))
                return views.FirstOrDefault(v => v.Name == viewName) ?? throw new Exception($"找不到視圖「{viewName}」");

            // 平面圖優先（直立平面），其次立面（水平平面）
            var ordered = views.OrderBy(v => v.ViewType == ViewType.FloorPlan ? 0 : v.ViewType == ViewType.Elevation ? 1 : 2);
            foreach (var v in ordered)
            {
                try
                {
                    if (Math.Abs(v.ViewDirection.Normalize().DotProduct(normal)) < 1e-6) return v;
                }
                catch { }
            }
            throw new Exception("找不到能看到這些平面的平面圖或立面視圖");
        }

        private static readonly Dictionary<string, BuiltInParameter> RfaCommonAssoc =
            new Dictionary<string, BuiltInParameter>(StringComparer.OrdinalIgnoreCase)
            {
                { "material", BuiltInParameter.MATERIAL_ID_PARAM },
                { "visible", BuiltInParameter.IS_VISIBLE_PARAM },
            };

        /// <summary>指定材質並把形體參數綁到族群參數；回傳成功綁定的清單，問題寫進 warnings</summary>
        private static List<object> RfaApplyMaterialAndAssociations(
            Document doc, FamilyManager fm, Element form, bool isSolid, string materialName,
            JObject associate, Dictionary<string, BuiltInParameter> extraKeys, List<string> warnings)
        {
            if (!string.IsNullOrEmpty(materialName))
            {
                if (!isSolid) warnings.Add("空心形體沒有材質，已忽略 material");
                else
                {
                    var mat = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                        .FirstOrDefault(m => m.Name == materialName);
                    if (mat == null) warnings.Add($"族群檔內找不到材質「{materialName}」，未指定材質");
                    else form.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(mat.Id);
                }
            }

            var results = new List<object>();
            if (associate == null) return results;

            var map = new Dictionary<string, BuiltInParameter>(RfaCommonAssoc, StringComparer.OrdinalIgnoreCase);
            if (extraKeys != null) foreach (var kv in extraKeys) map[kv.Key] = kv.Value;

            foreach (var kv in associate)
            {
                string famParamName = kv.Value?.Value<string>();
                if (!map.TryGetValue(kv.Key, out var bip))
                {
                    warnings.Add($"associate 不支援的鍵「{kv.Key}」（可用: {string.Join(", ", map.Keys)}）");
                    continue;
                }
                Parameter elemParam = form.get_Parameter(bip);
                FamilyParameter fp = RfaFindParam(fm, famParamName);
                if (elemParam == null) { warnings.Add($"形體沒有「{kv.Key}」參數"); continue; }
                if (fp == null) { warnings.Add($"找不到族群參數「{famParamName}」，請先用 rfa_add_parameter 建立"); continue; }
                if (!fm.CanElementParameterBeAssociated(elemParam)) { warnings.Add($"「{kv.Key}」無法綁定族群參數"); continue; }
                try
                {
                    fm.AssociateElementParameterToFamilyParameter(elemParam, fp);
                    results.Add(new { element = kv.Key, familyParameter = famParamName });
                }
                catch (Exception ex)
                {
                    warnings.Add($"綁定「{kv.Key}」→「{famParamName}」失敗：{ex.Message}（兩邊型別需相同）");
                }
            }
            return results;
        }

        /// <summary>輪廓簽章：曲線種類與數量 + 總長（0.1 mm），用來辨認哪個輪廓落在頂部</summary>
        private static string RfaLoopSignature(CurveArray loop)
        {
            var curves = loop.Cast<Curve>().ToList();
            string kinds = string.Join(",", curves.GroupBy(c => c.GetType().Name).OrderBy(g => g.Key).Select(g => $"{g.Count()}{g.Key}"));
            return $"{kinds}|{Math.Round(RfaToMm(curves.Sum(c => c.Length)), 1)}";
        }

        private static string RfaLoopSignature(CurveArrArray profile)
        {
            if (profile == null || profile.Size == 0) return null;
            return RfaLoopSignature(profile.get_Item(0));
        }

        private static string RfaDescribeProfile(CurveArrArray profile)
        {
            if (profile == null) return null;
            var counts = new Dictionary<string, int>();
            foreach (CurveArray loop in profile)
                foreach (Curve c in loop)
                {
                    string k = c.GetType().Name;
                    counts[k] = counts.TryGetValue(k, out int n) ? n + 1 : 1;
                }
            return string.Join(", ", counts.Select(kv => $"{kv.Value} {kv.Key}"));
        }

        #endregion

        #region rfa_create_dimension

        private object RfaCreateDimension(JObject parameters)
        {
            Document doc = GetActiveFamilyDocument();
            FamilyManager fm = doc.FamilyManager;

            var names = (parameters["referencePlanes"] as JArray)?.Select(t => t.Value<string>()).ToList();
            if (names == null || names.Count < 2)
                throw new Exception("referencePlanes 至少要兩個參考平面名稱");

            var planes = names.Select(n => RfaFindRefPlane(doc, n)).ToList();
            XYZ normal = planes[0].Normal.Normalize();
            foreach (var rp in planes)
                if (!rp.Normal.Normalize().IsAlmostEqualTo(normal) && !rp.Normal.Normalize().IsAlmostEqualTo(normal.Negate()))
                    throw new Exception($"參考平面「{rp.Name}」與「{planes[0].Name}」不平行，無法標同一道尺寸");

            // 沿法向量排序，讓尺寸段落依序排列
            planes = planes.OrderBy(rp => rp.GetPlane().Origin.DotProduct(normal)).ToList();

            string label = parameters["label"]?.Value<string>();
            bool equalize = parameters["equalize"]?.Value<bool>() ?? false;
            if (!string.IsNullOrEmpty(label) && planes.Count != 2)
                throw new Exception("label 只能用在兩個參考平面的單段尺寸；三個以上請改用 equalize");
            if (equalize && planes.Count < 3)
                throw new Exception("equalize（EQ）至少需要三個參考平面");

            View view = RfaPickViewForNormal(doc, normal, parameters["viewName"]?.Value<string>());

            // 尺寸線：沿法向量方向，通過 throughPointMm（預設為第一個平面的端點，通常落在形體外）
            XYZ through = parameters["throughPointMm"] != null
                ? RfaReadXyzMm(parameters["throughPointMm"], "throughPointMm")
                : planes[0].BubbleEnd;
            // 把點投影回視圖所在高度／深度，避免尺寸線跑出視圖平面
            XYZ viewDir = view.ViewDirection.Normalize();
            XYZ viewOrigin = view.Origin;
            through = through - viewDir * (through - viewOrigin).DotProduct(viewDir);

            double minS = planes.Min(rp => rp.GetPlane().Origin.DotProduct(normal));
            double maxS = planes.Max(rp => rp.GetPlane().Origin.DotProduct(normal));
            double s0 = through.DotProduct(normal);
            XYZ a = through + normal * (minS - s0 - RfaMm(100));
            XYZ b = through + normal * (maxS - s0 + RfaMm(100));

            var refs = new ReferenceArray();
            foreach (var rp in planes) refs.Append(rp.GetReference());

            var warnings = new List<string>();
            Dimension dim;
            using (var t = new Transaction(doc, "MCP: rfa_create_dimension"))
            {
                t.Start();
                dim = doc.FamilyCreate.NewDimension(view, Line.CreateBound(a, b), refs);
                if (dim == null) throw new Exception("建立尺寸失敗（Revit 回傳 null）");

                if (!string.IsNullOrEmpty(label))
                {
                    FamilyParameter fp = RfaFindParam(fm, label)
                                         ?? throw new Exception($"找不到族群參數「{label}」，請先用 rfa_add_parameter 建立長度參數");
                    try { dim.FamilyLabel = fp; }
                    catch (Exception ex) { throw new Exception($"尺寸綁定參數「{label}」失敗：{ex.Message}（參數需為長度型別）"); }
                }
                if (equalize)
                {
                    try { dim.AreSegmentsEqual = true; }
                    catch (Exception ex) { warnings.Add($"設定 EQ 失敗：{ex.Message}"); }
                }
                t.Commit();
            }

            return new
            {
                id = dim.Id.GetIdValue(),
                view = view.Name,
                referencePlanes = planes.Select(p => p.Name).ToList(),
                valueMm = dim.NumberOfSegments == 0 && dim.Value.HasValue ? RfaToMm(dim.Value.Value) : (double?)null,
                segmentsMm = dim.NumberOfSegments > 0
                    ? dim.Segments.Cast<DimensionSegment>().Select(s => s.Value.HasValue ? RfaToMm(s.Value.Value) : (double?)null).ToList()
                    : null,
                label = string.IsNullOrEmpty(label) ? null : label,
                equalize,
                warnings,
            };
        }

        #endregion

        #region rfa_align_to_reference_plane

        private object RfaAlignToReferencePlane(JObject parameters)
        {
            Document doc = GetActiveFamilyDocument();

            IdType idVal = parameters["elementId"]?.Value<IdType>() ?? throw new Exception("請提供 elementId（形體的 ElementId）");
            Element form = doc.GetElement(idVal.ToElementId()) ?? throw new Exception($"找不到元素 {idVal}");
            var names = (parameters["referencePlanes"] as JArray)?.Select(t => t.Value<string>()).ToList();
            if (names == null || names.Count == 0) throw new Exception("請提供 referencePlanes（要對齊鎖定的參考平面名稱）");
            bool lockIt = parameters["lock"]?.Value<bool>() ?? true;

            var planarFaces = new List<PlanarFace>();
            var opt = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true };
            void Collect(GeometryElement ge)
            {
                if (ge == null) return;
                foreach (GeometryObject go in ge)
                {
                    if (go is Solid s && s.Faces.Size > 0)
                    {
                        foreach (Face f in s.Faces)
                        {
                            if (f is PlanarFace pf && pf.Reference != null) planarFaces.Add(pf);
                        }
                    }
                    else if (go is GeometryInstance gi)
                    {
                        Collect(gi.GetSymbolGeometry());
                    }
                }
            }
            Collect(form.get_Geometry(opt));
            if (planarFaces.Count == 0) throw new Exception("此元素沒有可對齊的平面");

            double tol = RfaMm(0.5);
            var aligned = new List<object>();
            var failed = new List<object>();

            using (var t = new Transaction(doc, "MCP: rfa_align_to_reference_plane"))
            {
                t.Start();
                foreach (string name in names)
                {
                    ReferencePlane rp;
                    try { rp = RfaFindRefPlane(doc, name); }
                    catch (Exception ex) { failed.Add(new { referencePlane = name, error = ex.Message }); continue; }

                    XYZ n = rp.Normal.Normalize();
                    XYZ o = rp.GetPlane().Origin;
                    PlanarFace face = planarFaces.FirstOrDefault(pf =>
                        Math.Abs(Math.Abs(pf.FaceNormal.Normalize().DotProduct(n)) - 1) < 1e-6 &&
                        Math.Abs((pf.Origin - o).DotProduct(n)) < tol);
                    if (face == null)
                    {
                        failed.Add(new { referencePlane = name, error = "形體沒有與此參考平面共面的面（請先讓形體的面落在平面上，再對齊）" });
                        continue;
                    }

                    try
                    {
                        View view = RfaPickViewForNormal(doc, n, parameters["viewName"]?.Value<string>());
                        Dimension al = doc.FamilyCreate.NewAlignment(view, rp.GetReference(), face.Reference);
                        if (lockIt) al.IsLocked = true;
                        aligned.Add(new { referencePlane = name, alignmentId = al.Id.GetIdValue(), view = view.Name, locked = al.IsLocked });
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new { referencePlane = name, error = $"對齊失敗：{ex.Message}" });
                    }
                }
                t.Commit();
            }

            return new { elementId = idVal, alignedCount = aligned.Count, aligned, failed };
        }

        #endregion

        #region rfa_create_sweep

        private static Curve RfaReadPathSegment(JObject seg, int index)
        {
            if (seg["line"] is JObject ln)
                return Line.CreateBound(RfaReadXyzMm(ln["start"], $"路徑段 #{index} line.start"), RfaReadXyzMm(ln["end"], $"路徑段 #{index} line.end"));
            if (seg["arc"] is JObject arc)
                return Arc.Create(RfaReadXyzMm(arc["start"], $"路徑段 #{index} arc.start"),
                                  RfaReadXyzMm(arc["end"], $"路徑段 #{index} arc.end"),
                                  RfaReadXyzMm(arc["mid"], $"路徑段 #{index} arc.mid"));
            throw new Exception($"路徑段 #{index} 需要 line {{start,end}} 或 arc {{start,mid,end}}");
        }

        private object RfaCreateSweep(JObject parameters)
        {
            Document doc = GetActiveFamilyDocument();
            if (parameters["pathReferenceLineIds"] is JArray)
                return RfaCreateSweepOnReferenceLines(doc, parameters);
            FamilyManager fm = doc.FamilyManager;

            var curves = new List<Curve>();
            if (parameters["pathPoints"] is JArray pp)
            {
                var pts = pp.Select((tk, i) => RfaReadXyzMm(tk, $"pathPoints[{i}]")).ToList();
                if (parameters["closed"]?.Value<bool>() == true && pts.Count > 2) pts.Add(pts[0]);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    if (pts[i].DistanceTo(pts[i + 1]) < RfaMm(0.5)) continue;
                    curves.Add(Line.CreateBound(pts[i], pts[i + 1]));
                }
            }
            else if (parameters["pathSegments"] is JArray ps)
            {
                int i = 0;
                foreach (JObject seg in ps.OfType<JObject>()) curves.Add(RfaReadPathSegment(seg, i++));
            }
            if (curves.Count == 0) throw new Exception("請提供 pathPoints（多段線）或 pathSegments（line/arc），路徑至少一段");

            // 路徑需在同一平面上
            var samplePts = curves.SelectMany(c => new[] { c.GetEndPoint(0), c.Evaluate(0.5, true), c.GetEndPoint(1) }).ToList();
            XYZ pn = null;
            for (int i = 1; i < samplePts.Count && pn == null; i++)
                for (int j = i + 1; j < samplePts.Count && pn == null; j++)
                {
                    XYZ c = (samplePts[i] - samplePts[0]).CrossProduct(samplePts[j] - samplePts[0]);
                    if (c.GetLength() > 1e-6) pn = c.Normalize();
                }
            if (pn != null && samplePts.Any(p => Math.Abs((p - samplePts[0]).DotProduct(pn)) > RfaMm(0.5)))
                throw new Exception("掃掠路徑必須位於同一個平面上（3D 路徑請拆成多個掃掠）");

            var profiles = parameters["profiles"] as JArray;
            if (profiles == null || profiles.Count == 0) throw new Exception("請提供 profiles（掃掠斷面，至少一個封閉輪廓）");

            // 斷面畫在 XY 平面，Revit 會自動搬到路徑起點並垂直於路徑
            var xyFrame = new RfaSketchFrame { Origin = XYZ.Zero, U = XYZ.BasisX, V = XYZ.BasisY, Normal = XYZ.BasisZ };
            var loops = new CurveArrArray();
            int li = 0;
            foreach (JObject loop in profiles.OfType<JObject>()) loops.Append(RfaBuildLoop(loop, xyFrame, li++));

            bool isSolid = parameters["isSolid"]?.Value<bool>() ?? true;
            var warnings = new List<string>();

            using (var t = new Transaction(doc, "MCP: rfa_create_sweep"))
            {
                t.Start();
                var path = new CurveArray();
                foreach (var c in curves) path.Append(c);

                // API 文件說 pathPlane 可為 null，但 Revit 2027 實測會丟 ArgumentNullException，所以一律自建。
                // 直線路徑沒有唯一平面：取包含路徑與 Z 軸的平面（垂直路徑則改用 X 軸）。
                XYZ planeNormal = pn;
                if (planeNormal == null)
                {
                    XYZ dir = (curves[0].GetEndPoint(1) - curves[0].GetEndPoint(0)).Normalize();
                    XYZ helper = Math.Abs(dir.DotProduct(XYZ.BasisZ)) > 0.999 ? XYZ.BasisX : XYZ.BasisZ;
                    planeNormal = dir.CrossProduct(helper).Normalize();
                }
                SketchPlane pathPlane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(planeNormal, samplePts[0]));

                SweepProfile profile = _uiApp.Application.Create.NewCurveLoopsProfile(loops);
                Sweep sweep;
                try
                {
                    sweep = doc.FamilyCreate.NewSweep(isSolid, path, pathPlane, profile, 0, ProfilePlaneLocation.Start);
                }
                catch (Exception ex)
                {
                    throw new Exception($"建立掃掠失敗：{ex.Message}（常見原因：斷面太大導致路徑轉角自我交錯、路徑不連續、斷面不封閉）");
                }
                if (sweep == null) throw new Exception("建立掃掠失敗（Revit 回傳 null）");

                var assoc = RfaApplyMaterialAndAssociations(doc, fm, sweep, isSolid,
                    parameters["material"]?.Value<string>(), parameters["associate"] as JObject, null, warnings);
                t.Commit();

                return new
                {
                    id = sweep.Id.GetIdValue(),
                    isSolid,
                    pathSegmentCount = curves.Count,
                    pathLengthMm = RfaToMm(curves.Sum(c => c.Length)),
                    profileCount = profiles.Count,
                    associated = assoc,
                    warnings,
                };
            }
        }

        #endregion

        #region rfa_create_sweep（參考線路徑）

        /// <summary>
        /// 沿「參考線」（rfa_create_reference_line 建的）掃掠：路徑以參考取用，參考線移動時掃掠跟著動。
        /// 可用 diameterParameter 自動產生圓形斷面並在斷面草圖標直徑綁參數（族群檔中此項可驅動，已實測）。
        /// </summary>
        private object RfaCreateSweepOnReferenceLines(Document doc, JObject parameters)
        {
            FamilyManager fm = doc.FamilyManager;
            var warnings = new List<string>();

            var ids = ((JArray)parameters["pathReferenceLineIds"]).Select(tk => tk.Value<IdType>()).ToList();
            if (ids.Count == 0) throw new Exception("pathReferenceLineIds 至少一條");
            var pathRefs = new ReferenceArray();
            var pathCurves = new List<Curve>();
            foreach (var idv in ids)
            {
                var ce = doc.GetElement(idv.ToElementId()) as CurveElement
                         ?? throw new Exception($"元素 {idv} 不是參考線／模型線");
                pathRefs.Append(ce.GeometryCurve.Reference ?? throw new Exception($"參考線 {idv} 取不到參考"));
                pathCurves.Add(ce.GeometryCurve);
            }

            // 斷面：給 profiles，或只給 diameterParameter 自動畫圓（圓心在原點，Revit 會放到路徑起點）
            string diaParam = parameters["diameterParameter"]?.Value<string>();
            FamilyParameter diaFp = null;
            if (!string.IsNullOrEmpty(diaParam))
            {
                diaFp = RfaFindParam(fm, diaParam) ?? throw new Exception($"找不到族群參數「{diaParam}」");
                if (diaFp.Definition.GetDataType() != SpecTypeId.Length) throw new Exception($"「{diaParam}」不是長度參數");
            }
            var xyFrame = new RfaSketchFrame { Origin = XYZ.Zero, U = XYZ.BasisX, V = XYZ.BasisY, Normal = XYZ.BasisZ };
            var loops = new CurveArrArray();
            if (parameters["profiles"] is JArray profiles && profiles.Count > 0)
            {
                int li = 0;
                foreach (JObject loop in profiles.OfType<JObject>()) loops.Append(RfaBuildLoop(loop, xyFrame, li++));
            }
            else if (diaFp != null)
            {
                double dia = fm.CurrentType?.AsDouble(diaFp) ?? 0;
                if (dia <= 0) throw new Exception($"「{diaParam}」目前類型沒有正值，無法自動畫圓形斷面");
                loops.Append(RfaBuildLoop(new JObject { ["circle"] = new JObject { ["center"] = new JArray(0, 0), ["radius"] = RfaToMm(dia / 2) } }, xyFrame, 0));
            }
            else throw new Exception("請提供 profiles，或給 diameterParameter 自動產生圓形斷面");

            bool isSolid = parameters["isSolid"]?.Value<bool>() ?? true;
            int profileCurveIndex = parameters["profileCurveIndex"]?.Value<int>() ?? 0;
            Sweep sweep;
            List<object> assoc;

            using (var t = new Transaction(doc, "MCP: rfa_create_sweep（參考線路徑）"))
            {
                var fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new SilentFailuresPreprocessor());
                t.SetFailureHandlingOptions(fho);
                t.Start();
                SweepProfile profile = _uiApp.Application.Create.NewCurveLoopsProfile(loops);
                try
                {
                    sweep = doc.FamilyCreate.NewSweep(isSolid, pathRefs, profile, profileCurveIndex, ProfilePlaneLocation.Start);
                }
                catch (Exception ex)
                {
                    throw new Exception($"建立掃掠失敗：{ex.Message}（參考線需首尾相接、同一平面；封閉路徑需為一圈）");
                }
                if (sweep == null) throw new Exception("建立掃掠失敗（Revit 回傳 null）");
                assoc = RfaApplyMaterialAndAssociations(doc, fm, sweep, isSolid,
                    parameters["material"]?.Value<string>(), parameters["associate"] as JObject, null, warnings);
                if (t.Commit() != TransactionStatus.Committed) throw new Exception("建立掃掠的交易被復原");
            }

            // 斷面直徑標籤：草圖弧在草圖模式外仍可標直徑並驅動（實測 Revit 2027）
            var dims = new List<object>();
            string flex = null;
            if (diaFp != null)
            {
                try
                {
                    using (var t = new Transaction(doc, "MCP: 掃掠斷面直徑標籤"))
                    {
                        var fho = t.GetFailureHandlingOptions();
                        fho.SetFailuresPreprocessor(new SilentFailuresPreprocessor());
                        t.SetFailureHandlingOptions(fho);
                        t.Start();
                        var arcs = RfaProfileArcs(doc, sweep);
                        if (arcs.Count == 0) throw new Exception("斷面草圖裡找不到圓弧");
                        foreach (var arcEl in arcs)
                        {
                            var arc = (Arc)arcEl.GeometryCurve;
                            View view = RfaPickConstraintView(doc, null, null, arc.Normal, parameters["viewName"]?.Value<string>());
                            XYZ at = arc.Center + (arc.XDirection + arc.YDirection).Normalize() * arc.Radius * 1.5;
                            Dimension dd = doc.FamilyCreate.NewDiameterDimension(view, arc.Reference ?? new Reference(arcEl), at);
                            dd.FamilyLabel = diaFp;
                            dims.Add(new { id = dd.Id.GetIdValue(), view = view.Name, label = diaParam });
                        }
                        if (t.Commit() != TransactionStatus.Committed) throw new Exception("交易被復原");
                    }
                    flex = RfaFlexCheckSweepDiameter(doc, sweep, diaFp);
                }
                catch (Exception ex)
                {
                    warnings.Add($"斷面直徑未綁參數：{ex.Message}（掃掠已建立，管徑固定）");
                }
            }

            return new
            {
                id = sweep.Id.GetIdValue(),
                isSolid,
                pathReferenceLineIds = ids,
                pathLengthMm = RfaToMm(pathCurves.Sum(c => c.Length)),
                diameterLabels = dims,
                flexCheck = flex,
                associated = assoc,
                warnings,
            };
        }

        private static List<CurveElement> RfaProfileArcs(Document doc, Sweep sweep)
        {
            Sketch sk = sweep.ProfileSketch;
            if (sk == null) return new List<CurveElement>();
            return sk.GetAllElements().Select(id => doc.GetElement(id)).OfType<CurveElement>()
                .Where(ce => ce.GeometryCurve is Arc).ToList();
        }

        /// <summary>暫時把管徑 +10 mm，檢查斷面每段圓弧半徑是否等於參數的一半，然後復原</summary>
        private static string RfaFlexCheckSweepDiameter(Document doc, Sweep sweep, FamilyParameter diaFp)
        {
            FamilyManager fm = doc.FamilyManager;
            if (!string.IsNullOrEmpty(diaFp.Formula)) return "skipped（管徑參數由公式驅動）";
            using (var t = new Transaction(doc, "MCP: 管徑撓曲自檢"))
            {
                var fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new SilentFailuresPreprocessor());
                t.SetFailureHandlingOptions(fho);
                t.Start();
                double v = (fm.CurrentType.AsDouble(diaFp) ?? 0) + RfaMm(10);
                fm.Set(diaFp, v);
                doc.Regenerate();
                var bad = RfaProfileArcs(doc, sweep).Select(ce => ((Arc)ce.GeometryCurve).Radius)
                    .Where(r => Math.Abs(r - v / 2) > RfaMm(0.1)).Select(r => RfaToMm(r * 2)).ToList();
                t.RollBack();
                return bad.Count == 0 ? "OK" : $"FAILED：斷面直徑 {string.Join(",", bad)} ≠ {RfaToMm(v)}";
            }
        }

        #endregion

        #region rfa_create_blend

        private object RfaCreateBlend(JObject parameters)
        {
            Document doc = GetActiveFamilyDocument();
            FamilyManager fm = doc.FamilyManager;

            var baseLoop = parameters["baseProfile"] as JObject ?? throw new Exception("請提供 baseProfile（底部輪廓）");
            var topLoop = parameters["topProfile"] as JObject ?? throw new Exception("請提供 topProfile（頂部輪廓）");
            double heightMm = parameters["heightMm"]?.Value<double>() ?? throw new Exception("請提供 heightMm（底到頂的距離，mm）");
            if (Math.Abs(heightMm) < 0.5) throw new Exception("heightMm 太小");
            bool isSolid = parameters["isSolid"]?.Value<bool>() ?? true;
            var warnings = new List<string>();

            using (var t = new Transaction(doc, "MCP: rfa_create_blend"))
            {
                t.Start();
                RfaSketchFrame frame = RfaResolveSketchFrame(doc, parameters);
                CurveArray baseCurves = RfaBuildLoop(baseLoop, frame, 0);

                // 頂部輪廓：同一套 2D 座標，沿法向量平移 heightMm
                var topFrame = frame;
                topFrame.Origin = frame.Origin + frame.Normal * RfaMm(heightMm);
                CurveArray topCurves = RfaBuildLoop(topLoop, topFrame, 1);

                Blend blend;
                try
                {
                    // NewBlend 哪個輪廓參數會成為 TopProfile，文件與實測不一致（Revit 2027：第一個參數成為頂部）。
                    // 不寫死版本：先建，再比對 Revit 認定的頂部輪廓；若與要求相反就刪掉、對調參數重建。
                    blend = doc.FamilyCreate.NewBlend(isSolid, topCurves, baseCurves, frame.Sketch);
                    string wantTop = RfaLoopSignature(topCurves), wantBase = RfaLoopSignature(baseCurves);
                    if (wantTop != wantBase && blend != null && RfaLoopSignature(blend.TopProfile) == wantBase)
                    {
                        doc.Delete(blend.Id);
                        blend = doc.FamilyCreate.NewBlend(isSolid, baseCurves, topCurves, frame.Sketch);
                        warnings.Add("此 Revit 版本的 NewBlend 輪廓順序與預設相反，已自動對調重建");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"建立融合失敗：{ex.Message}（底部與頂部各只能一個封閉輪廓）");
                }
                if (blend == null) throw new Exception("建立融合失敗（Revit 回傳 null）");

                // 明確設定底/頂偏移，不依賴 Revit 從輪廓位置推斷（頂底順序錯置時它曾往法向量反方向長）
                blend.BottomOffset = 0;
                blend.TopOffset = RfaMm(heightMm);

                var extra = new Dictionary<string, BuiltInParameter>(StringComparer.OrdinalIgnoreCase)
                {
                    { "blendTop", BuiltInParameter.BLEND_END_PARAM },
                    { "blendBase", BuiltInParameter.BLEND_START_PARAM },
                };
                var assoc = RfaApplyMaterialAndAssociations(doc, fm, blend, isSolid,
                    parameters["material"]?.Value<string>(), parameters["associate"] as JObject, extra, warnings);
                t.Commit();

                return new
                {
                    id = blend.Id.GetIdValue(),
                    isSolid,
                    sketchPlane = frame.Description,
                    bottomOffsetMm = RfaToMm(blend.BottomOffset),
                    topOffsetMm = RfaToMm(blend.TopOffset),
                    // 自我檢查：Revit 實際認定的頂/底輪廓由哪些曲線組成（例如圓 = 2 Arc，矩形 = 4 Line）
                    bottomProfileCurves = RfaDescribeProfile(blend.BottomProfile),
                    topProfileCurves = RfaDescribeProfile(blend.TopProfile),
                    associated = assoc,
                    warnings,
                };
            }
        }

        #endregion
    }
}
