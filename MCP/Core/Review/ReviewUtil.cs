using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core.Review
{
    /// <summary>
    /// 一個「來源文件」：主模型或某個已載入的連結模型實例。
    /// ToHost 把來源文件座標轉成主模型座標（主模型為 Identity）。
    /// </summary>
    internal sealed class SourceDoc
    {
        public Document Doc;
        public Transform ToHost;
        public string Label;          // "主模型" 或 連結名稱
        public IdType LinkInstanceId;  // 主模型為 -1
        public bool IsHost => LinkInstanceId < 0;
    }

    /// <summary>
    /// 檢討 / 安全編輯共用工具：單位、類別別名、連結列舉、幾何、檢討視圖、標記。
    /// 全部 static、與 CommandExecutor 解耦，方便日後單獨測試。
    /// </summary>
    internal static class ReviewUtil
    {
        public const double FtToMm = 304.8;
        public static double Mm(double ft) => Math.Round(ft * FtToMm, 1);
        public static double Ft(double mm) => mm / FtToMm;

        public const string MarkerAppId = "RevitMCP.Review";
        public const string RayViewName = "AI檢討_射線用3D";
        public const string ResultViewName = "AI檢討_結果3D";

        // ───────────────────────── 類別別名 ─────────────────────────

        private static readonly Dictionary<string, BuiltInCategory> CategoryAliases =
            new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                ["walls"] = BuiltInCategory.OST_Walls, ["牆"] = BuiltInCategory.OST_Walls,
                ["floors"] = BuiltInCategory.OST_Floors, ["樓板"] = BuiltInCategory.OST_Floors,
                ["ceilings"] = BuiltInCategory.OST_Ceilings, ["天花板"] = BuiltInCategory.OST_Ceilings,
                ["roofs"] = BuiltInCategory.OST_Roofs, ["屋頂"] = BuiltInCategory.OST_Roofs,
                ["doors"] = BuiltInCategory.OST_Doors, ["門"] = BuiltInCategory.OST_Doors,
                ["windows"] = BuiltInCategory.OST_Windows, ["窗"] = BuiltInCategory.OST_Windows,
                ["rooms"] = BuiltInCategory.OST_Rooms, ["房間"] = BuiltInCategory.OST_Rooms,
                ["columns"] = BuiltInCategory.OST_Columns, ["建築柱"] = BuiltInCategory.OST_Columns,
                ["structural_columns"] = BuiltInCategory.OST_StructuralColumns, ["結構柱"] = BuiltInCategory.OST_StructuralColumns, ["柱"] = BuiltInCategory.OST_StructuralColumns,
                ["beams"] = BuiltInCategory.OST_StructuralFraming, ["structural_framing"] = BuiltInCategory.OST_StructuralFraming, ["梁"] = BuiltInCategory.OST_StructuralFraming, ["樑"] = BuiltInCategory.OST_StructuralFraming,
                ["foundations"] = BuiltInCategory.OST_StructuralFoundation, ["基礎"] = BuiltInCategory.OST_StructuralFoundation,
                ["stairs"] = BuiltInCategory.OST_Stairs, ["樓梯"] = BuiltInCategory.OST_Stairs,
                ["railings"] = BuiltInCategory.OST_StairsRailing, ["欄杆"] = BuiltInCategory.OST_StairsRailing,
                ["furniture"] = BuiltInCategory.OST_Furniture, ["家具"] = BuiltInCategory.OST_Furniture,
                ["generic_models"] = BuiltInCategory.OST_GenericModel, ["一般模型"] = BuiltInCategory.OST_GenericModel,
                ["pipes"] = BuiltInCategory.OST_PipeCurves, ["管"] = BuiltInCategory.OST_PipeCurves, ["管線"] = BuiltInCategory.OST_PipeCurves,
                ["ducts"] = BuiltInCategory.OST_DuctCurves, ["風管"] = BuiltInCategory.OST_DuctCurves,
                ["cable_trays"] = BuiltInCategory.OST_CableTray, ["電纜架"] = BuiltInCategory.OST_CableTray, ["線架"] = BuiltInCategory.OST_CableTray,
                ["conduits"] = BuiltInCategory.OST_Conduit, ["導線管"] = BuiltInCategory.OST_Conduit, ["電管"] = BuiltInCategory.OST_Conduit,
                ["pipe_fittings"] = BuiltInCategory.OST_PipeFitting, ["duct_fittings"] = BuiltInCategory.OST_DuctFitting,
                ["mechanical_equipment"] = BuiltInCategory.OST_MechanicalEquipment, ["機械設備"] = BuiltInCategory.OST_MechanicalEquipment,
            };

        public static BuiltInCategory ParseCategory(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new Exception("類別名稱不可為空");
            name = name.Trim();
            if (CategoryAliases.TryGetValue(name, out var bic)) return bic;
            string ost = name.StartsWith("OST_", StringComparison.OrdinalIgnoreCase) ? name : "OST_" + name;
            if (Enum.TryParse(ost, true, out BuiltInCategory parsed)) return parsed;
            throw new Exception($"無法辨識的類別: {name}（可用：{string.Join(", ", CategoryAliases.Keys.Where(k => k.All(c => c < 128)))} 或 OST_xxx）");
        }

        /// <summary>給規則比對用的正規化類別鍵（例如 pipes / ducts）。</summary>
        public static string CategoryKey(BuiltInCategory bic)
        {
            switch (bic)
            {
                case BuiltInCategory.OST_PipeCurves: return "pipes";
                case BuiltInCategory.OST_DuctCurves: return "ducts";
                case BuiltInCategory.OST_CableTray: return "cable_trays";
                case BuiltInCategory.OST_Conduit: return "conduits";
                case BuiltInCategory.OST_StructuralFraming: return "beams";
                case BuiltInCategory.OST_StructuralColumns: return "structural_columns";
                default: return bic.ToString();
            }
        }

        public static BuiltInCategory? GetBic(Element e)
        {
            var cat = e?.Category;
            if (cat == null) return null;
            long v = cat.Id.GetIdValue();
            if (Enum.IsDefined(typeof(BuiltInCategory), (int)v)) return (BuiltInCategory)(int)v;
            return null;
        }

        // ───────────────────────── 文件 / 連結 ─────────────────────────

        public static List<SourceDoc> GetSourceDocs(Document host, bool includeLinks)
        {
            var list = new List<SourceDoc>
            {
                new SourceDoc { Doc = host, ToHost = Transform.Identity, Label = "主模型", LinkInstanceId = -1 }
            };
            if (!includeLinks) return list;

            foreach (RevitLinkInstance li in new FilteredElementCollector(host).OfClass(typeof(RevitLinkInstance)))
            {
                Document ld = null;
                try { ld = li.GetLinkDocument(); } catch { }
                if (ld == null) continue; // 未載入
                list.Add(new SourceDoc
                {
                    Doc = ld,
                    ToHost = li.GetTotalTransform(),
                    Label = li.Name,
                    LinkInstanceId = li.Id.GetIdValue()
                });
            }
            return list;
        }

        // ───────────────────────── 元素資訊 ─────────────────────────

        public static string TypeName(Element e)
        {
            try
            {
                var t = e.Document.GetElement(e.GetTypeId()) as ElementType;
                if (t == null) return e.Name;
                return string.IsNullOrEmpty(t.FamilyName) ? t.Name : $"{t.FamilyName}: {t.Name}";
            }
            catch { return e.Name; }
        }

        public static string LevelName(Element e)
        {
            try
            {
                Document d = e.Document;
                ElementId lid = ElementId.InvalidElementId;
                var p = e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId) lid = p.AsElementId();
                if (lid == null || lid == ElementId.InvalidElementId)
                {
                    if (e is MEPCurve mc && mc.ReferenceLevel != null) return mc.ReferenceLevel.Name;
                    lid = e.LevelId;
                }
                if (lid != null && lid != ElementId.InvalidElementId) return d.GetElement(lid)?.Name ?? "";
            }
            catch { }
            return "";
        }

        public static string Mark(Element e)
        {
            try { return e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? ""; } catch { return ""; }
        }

        /// <summary>元素外框（已轉為主模型座標），無外框回傳 null。</summary>
        public static (XYZ min, XYZ max)? HostBox(Element e, Transform toHost)
        {
            BoundingBoxXYZ bb = null;
            try { bb = e.get_BoundingBox(null); } catch { }
            if (bb == null) return null;
            var corners = new List<XYZ>();
            foreach (double x in new[] { bb.Min.X, bb.Max.X })
                foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                    foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                    {
                        XYZ p = bb.Transform.OfPoint(new XYZ(x, y, z));
                        corners.Add(toHost.OfPoint(p));
                    }
            return (new XYZ(corners.Min(c => c.X), corners.Min(c => c.Y), corners.Min(c => c.Z)),
                    new XYZ(corners.Max(c => c.X), corners.Max(c => c.Y), corners.Max(c => c.Z)));
        }

        public static object PointMm(XYZ p) => new { x = Mm(p.X), y = Mm(p.Y), z = Mm(p.Z) };

        // ───────────────────────── 樓層 ─────────────────────────

        public static List<Level> SortedLevels(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).ToList();

        /// <summary>依 Z（主模型座標）找出所在樓層：最高且 elevation ≤ z + tol 的樓層。</summary>
        public static Level LevelAtZ(List<Level> sorted, double z, double tolFt = 0.1)
        {
            Level found = null;
            foreach (var l in sorted)
            {
                if (l.ProjectElevation <= z + tolFt) found = l; else break;
            }
            return found ?? sorted.FirstOrDefault();
        }

        /// <summary>
        /// 把使用者給的樓層名稱轉成 Z 區間 [本層, 上一層)，用來挑選「這層空間內」的元素。
        /// 回傳 null 表示不過濾。
        /// </summary>
        public static List<(string name, double zMin, double zMax)> LevelBands(Document doc, IEnumerable<string> names)
        {
            var wanted = names?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            if (wanted == null || wanted.Count == 0) return null;
            var levels = SortedLevels(doc);
            var bands = new List<(string, double, double)>();
            foreach (var n in wanted)
            {
                int idx = levels.FindIndex(l => l.Name == n);
                if (idx < 0) idx = levels.FindIndex(l => l.Name.Contains(n) || n.Contains(l.Name));
                if (idx < 0) throw new Exception($"找不到樓層: {n}（現有：{string.Join(", ", levels.Select(l => l.Name))}）");
                // ProjectElevation 與幾何同為內部座標；Elevation 會隨「高程基準」設定偏移
                double z0 = levels[idx].ProjectElevation;
                double z1 = idx + 1 < levels.Count ? levels[idx + 1].ProjectElevation : z0 + Ft(100000);
                bands.Add((levels[idx].Name, z0, z1));
            }
            return bands;
        }

        public static bool InBands(List<(string name, double zMin, double zMax)> bands, double z)
        {
            if (bands == null) return true;
            return bands.Any(b => z > b.zMin + 1e-6 && z <= b.zMax + Ft(100));
        }

        // ───────────────────────── 幾何 ─────────────────────────

        public static List<Solid> GetSolids(Element e, Transform toHost)
        {
            var result = new List<Solid>();
            GeometryElement ge = null;
            try
            {
                ge = e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false });
            }
            catch { }
            if (ge == null) return result;
            CollectSolids(ge, result);
            if (toHost != null && !toHost.IsIdentity)
            {
                for (int i = 0; i < result.Count; i++)
                {
                    try { result[i] = SolidUtils.CreateTransformed(result[i], toHost); } catch { result[i] = null; }
                }
                result.RemoveAll(s => s == null); // 轉換失敗的實體留在連結座標會產生錯誤 Z，寧可略過
            }
            return result;
        }

        private static void CollectSolids(GeometryElement ge, List<Solid> acc)
        {
            foreach (GeometryObject go in ge)
            {
                if (go is Solid s && s.Volume > 1e-9) acc.Add(s);
                else if (go is GeometryInstance gi)
                {
                    try { CollectSolids(gi.GetInstanceGeometry(), acc); } catch { }
                }
            }
        }

        /// <summary>垂直線穿過實體，回傳實體在此 XY 的最低點 Z；沒穿到回傳 null。</summary>
        public static double? BottomZAt(List<Solid> solids, double x, double y, double zLow, double zHigh)
        {
            Line line;
            try { line = Line.CreateBound(new XYZ(x, y, zHigh + 1.0), new XYZ(x, y, zLow - 1.0)); }
            catch { return null; }
            double? best = null;
            var opt = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
            foreach (var s in solids)
            {
                SolidCurveIntersection sci;
                try { sci = s.IntersectWithCurve(line, opt); } catch { continue; }
                if (sci == null) continue;
                for (int i = 0; i < sci.SegmentCount; i++)
                {
                    Curve seg = sci.GetCurveSegment(i);
                    double z = Math.Min(seg.GetEndPoint(0).Z, seg.GetEndPoint(1).Z);
                    if (best == null || z < best) best = z;
                }
            }
            return best;
        }

        public static XYZ AnyPerpendicular(XYZ d)
        {
            XYZ a = Math.Abs(d.Z) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;
            return d.CrossProduct(a).Normalize();
        }

        /// <summary>沿 p0→p1 擠出的圓形或矩形實體（主模型座標）。</summary>
        public static Solid SweepBox(XYZ p0, XYZ p1, bool round, double r, XYZ ax, XYZ ay, double hw, double hh)
        {
            XYZ d = (p1 - p0);
            double len = d.GetLength();
            if (len < 1e-4) return null;
            d = d.Normalize();
            CurveLoop loop = new CurveLoop();
            if (round)
            {
                XYZ x = AnyPerpendicular(d);
                XYZ y = d.CrossProduct(x).Normalize();
                loop.Append(Arc.Create(p0, r, 0, Math.PI, x, y));
                loop.Append(Arc.Create(p0, r, Math.PI, 2 * Math.PI, x, y));
            }
            else
            {
                XYZ x = ax, y = ay;
                XYZ c1 = p0 + x * hw + y * hh, c2 = p0 - x * hw + y * hh, c3 = p0 - x * hw - y * hh, c4 = p0 + x * hw - y * hh;
                loop.Append(Line.CreateBound(c1, c2));
                loop.Append(Line.CreateBound(c2, c3));
                loop.Append(Line.CreateBound(c3, c4));
                loop.Append(Line.CreateBound(c4, c1));
            }
            try { return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, d, len); }
            catch { return null; }
        }

        // ───────────────────────── 檢討視圖 ─────────────────────────

        /// <summary>
        /// 取得（必要時建立）專給射線檢測用的 3D 視圖：不套樣版、不開剖面框、樓板與天花可見。
        /// 呼叫端不可在 Transaction 中呼叫（本方法自行開交易）。
        /// </summary>
        public static View3D EnsureReviewView(Document doc, string name, IEnumerable<BuiltInCategory> mustShow)
        {
            View3D v = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(x => !x.IsTemplate && x.Name == name);

            bool needCreate = v == null;
            bool needFix = !needCreate && (v.ViewTemplateId != ElementId.InvalidElementId || v.IsSectionBoxActive
                           || mustShow.Any(c => SafeHidden(v, c)));
            if (!needCreate && !needFix) return v;

            using (var t = new Transaction(doc, "AI: 準備檢討視圖"))
            {
                var o = t.GetFailureHandlingOptions();
                o.SetFailuresPreprocessor(new WarningSwallower());
                t.SetFailureHandlingOptions(o);
                t.Start();
                if (needCreate)
                {
                    var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
                    v = View3D.CreateIsometric(doc, vft.Id);
                    // 先移除樣版：視圖類型可能帶預設樣版，樣版控制的屬性在移除前不可設定
                    if (v.ViewTemplateId != ElementId.InvalidElementId) v.ViewTemplateId = ElementId.InvalidElementId;
                    try { v.Name = name; }
                    catch { v.Name = name + "_" + v.Id.GetIdValue(); } // 名稱已被其他類型視圖使用
                    try { v.DetailLevel = ViewDetailLevel.Fine; } catch { }
                }
                if (v.ViewTemplateId != ElementId.InvalidElementId) v.ViewTemplateId = ElementId.InvalidElementId;
                if (v.IsSectionBoxActive) v.IsSectionBoxActive = false;
                foreach (var c in mustShow)
                {
                    var cid = new ElementId(c);
                    try { if (v.CanCategoryBeHidden(cid) && v.GetCategoryHidden(cid)) v.SetCategoryHidden(cid, false); } catch { }
                }
                t.Commit();
            }
            return v;
        }

        private static bool SafeHidden(View v, BuiltInCategory c)
        {
            try { return v.GetCategoryHidden(new ElementId(c)); } catch { return false; }
        }

        // ───────────────────────── 標色 / 標記 ─────────────────────────

        public static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return fallback;
                hex = hex.TrimStart('#');
                if (hex.Length != 6) return fallback;
                return new Color(byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                                 byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                                 byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
            }
            catch { return fallback; }
        }

        public static OverrideGraphicSettings ColorOverride(Document doc, Color c)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(c);
            ogs.SetCutLineColor(c);
            var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
            if (solid != null)
            {
                ogs.SetSurfaceForegroundPatternId(solid.Id);
                ogs.SetSurfaceForegroundPatternColor(c);
                ogs.SetCutForegroundPatternId(solid.Id);
                ogs.SetCutForegroundPatternColor(c);
            }
            return ogs;
        }

        /// <summary>
        /// 在主模型放置檢討標記（一般模型 DirectShape 方塊），可用 clear_review_markers 一鍵清除。
        /// 連結模型內的問題無法直接標色，所以用標記把位置帶回主模型。
        /// </summary>
        public static int PlaceMarkers(Document doc, string checkName, IList<(XYZ p, string label)> items, double sizeMm = 300)
        {
            if (items == null || items.Count == 0) return 0;
            double s = Ft(sizeMm) / 2;
            int n = 0;
            using (var t = new Transaction(doc, $"AI: 檢討標記（{checkName}）"))
            {
                var o = t.GetFailureHandlingOptions();
                o.SetFailuresPreprocessor(new WarningSwallower());
                t.SetFailureHandlingOptions(o);
                t.Start();
                foreach (var (p, label) in items)
                {
                    try
                    {
                        XYZ b = new XYZ(p.X, p.Y, p.Z - s);
                        var loop = CurveLoop.Create(new List<Curve>
                        {
                            Line.CreateBound(b + new XYZ(-s, -s, 0), b + new XYZ(s, -s, 0)),
                            Line.CreateBound(b + new XYZ(s, -s, 0), b + new XYZ(s, s, 0)),
                            Line.CreateBound(b + new XYZ(s, s, 0), b + new XYZ(-s, s, 0)),
                            Line.CreateBound(b + new XYZ(-s, s, 0), b + new XYZ(-s, -s, 0)),
                        });
                        Solid box = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, 2 * s);
                        var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        ds.ApplicationId = MarkerAppId;
                        ds.ApplicationDataId = checkName;
                        ds.SetShape(new List<GeometryObject> { box });
                        ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(label ?? "");
                        ds.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set("AI-REVIEW");
                        n++;
                    }
                    catch (Exception ex) { Logger.Error($"[ReviewUtil] 放置標記失敗: {ex.Message}"); }
                }
                t.Commit();
            }
            return n;
        }

        /// <summary>在結果 3D 視圖把主模型元素上色。</summary>
        public static int HighlightInResultView(Document doc, IEnumerable<ElementId> ids, Color c)
        {
            var list = ids?.Distinct().ToList() ?? new List<ElementId>();
            if (list.Count == 0) return 0;
            var view = EnsureReviewView(doc, ResultViewName, new[] { BuiltInCategory.OST_GenericModel });
            int n = 0;
            using (var t = new Transaction(doc, "AI: 檢討結果上色"))
            {
                var o = t.GetFailureHandlingOptions();
                o.SetFailuresPreprocessor(new WarningSwallower());
                t.SetFailureHandlingOptions(o);
                t.Start();
                var ogs = ColorOverride(doc, c);
                foreach (var id in list)
                {
                    try { view.SetElementOverrides(id, ogs); n++; } catch { }
                }
                t.Commit();
            }
            return n;
        }
    }

    /// <summary>
    /// 收集交易中的 Warning / Error，讓呼叫端回報給 AI。
    /// Warning 預設刪除（避免彈窗），rollbackOnWarning=true 時有任何 Warning 就整筆回滾。
    /// Error 一律回滾，不會跳出對話框。
    /// </summary>
    internal sealed class CollectingFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly bool _rollbackOnWarning;
        public readonly List<object> Warnings = new List<object>();
        public readonly List<object> Errors = new List<object>();
        public bool RolledBackByFailures { get; private set; }

        public CollectingFailuresPreprocessor(bool rollbackOnWarning) { _rollbackOnWarning = rollbackOnWarning; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            bool blocking = false;
            foreach (FailureMessageAccessor msg in fa.GetFailureMessages())
            {
                var sev = msg.GetSeverity();
                var entry = new
                {
                    severity = sev.ToString(),
                    message = SafeText(msg),
                    elementIds = msg.GetFailingElementIds().Select(i => i.GetIdValue()).Take(30).ToList()
                };
                if (sev == FailureSeverity.Warning)
                {
                    Warnings.Add(entry);
                    if (_rollbackOnWarning) blocking = true;
                    else fa.DeleteWarning(msg);
                }
                else
                {
                    Errors.Add(entry);
                    blocking = true;
                }
            }
            if (blocking)
            {
                RolledBackByFailures = true;
                return FailureProcessingResult.ProceedWithRollBack;
            }
            return FailureProcessingResult.Continue;
        }

        private static string SafeText(FailureMessageAccessor m)
        {
            try { return m.GetDescriptionText(); } catch { return "(無法讀取訊息)"; }
        }
    }
}
