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
    /// RFA 族群編輯工具（rfa_*）：直接操作「目前作用中的族群文件」。
    /// 使用者先在 Revit 以樣板開好 .rfa（族群編輯器），這些指令再於其中建參考平面、擠出、參數與類型。
    /// 只用 Revit 2022 起就存在的 API（ForgeTypeId 版 AddParameter、SpecTypeId、GroupTypeId），
    /// 所以 R22–R27 共用同一份程式碼，不需要版本分支。
    /// 所有長度以 mm 輸入／輸出；角度以度；面積 m²；體積 m³。
    /// </summary>
    public partial class CommandExecutor
    {
        #region 共用

        private Document GetActiveFamilyDocument()
        {
            Document doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null)
                throw new Exception("Revit 目前沒有開啟任何文件。請先以族群樣板開啟或新建一個 .rfa。");
            if (!doc.IsFamilyDocument)
                throw new Exception($"目前作用中的文件「{doc.Title}」是專案檔，不是族群檔。rfa_* 工具只能在族群編輯器中使用，請先切換到要編輯的 .rfa 視窗。");
            return doc;
        }

        private static double RfaMm(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        private static double RfaToMm(double ft) => Math.Round(UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters), 3);

        private static object RfaPt(XYZ p) => new[] { RfaToMm(p.X), RfaToMm(p.Y), RfaToMm(p.Z) };
        private static object RfaVec(XYZ v) => new[] { Math.Round(v.X, 6), Math.Round(v.Y, 6), Math.Round(v.Z, 6) };

        private static XYZ RfaReadXyzMm(JToken token, string label)
        {
            var arr = token as JArray;
            if (arr == null || arr.Count != 3)
                throw new Exception($"{label} 需要 [x, y, z]（mm）");
            return new XYZ(RfaMm(arr[0].Value<double>()), RfaMm(arr[1].Value<double>()), RfaMm(arr[2].Value<double>()));
        }

        private static XYZ RfaReadVector(JToken token, string label)
        {
            var arr = token as JArray;
            if (arr == null || arr.Count != 3)
                throw new Exception($"{label} 需要 [x, y, z] 方向向量");
            var v = new XYZ(arr[0].Value<double>(), arr[1].Value<double>(), arr[2].Value<double>());
            if (v.IsZeroLength()) throw new Exception($"{label} 不可為零向量");
            return v.Normalize();
        }

        // 參數型別：工具輸入用的短名稱 ↔ Revit SpecTypeId
        private static readonly (string Key, ForgeTypeId Spec)[] RfaSpecMap =
        {
            ("length", SpecTypeId.Length),
            ("number", SpecTypeId.Number),
            ("integer", SpecTypeId.Int.Integer),
            ("yes_no", SpecTypeId.Boolean.YesNo),
            ("text", SpecTypeId.String.Text),
            ("angle", SpecTypeId.Angle),
            ("area", SpecTypeId.Area),
            ("volume", SpecTypeId.Volume),
            ("material", SpecTypeId.Reference.Material),
        };

        private static string RfaSpecName(ForgeTypeId spec)
        {
            if (spec == null) return null;
            foreach (var (key, s) in RfaSpecMap)
                if (s == spec) return key;
            return spec.TypeId;
        }

        private static ForgeTypeId RfaGroupFromName(string name)
        {
            switch ((name ?? "dimensions").Trim().ToLowerInvariant())
            {
                case "dimensions":
                case "geometry": return GroupTypeId.Geometry;
                case "constraints": return GroupTypeId.Constraints;
                case "materials": return GroupTypeId.Materials;
                case "identity_data": return GroupTypeId.IdentityData;
                case "data": return GroupTypeId.Data;
                case "text": return GroupTypeId.Text;
                case "graphics": return GroupTypeId.Graphics;
                case "construction": return GroupTypeId.Construction;
                case "visibility": return GroupTypeId.Visibility;
                // 「其他」群組在 ForgeTypeId 版 API 以空 TypeId 表示
                case "other": return new ForgeTypeId(string.Empty);
                default:
                    throw new Exception($"不支援的參數群組: {name}（可用: dimensions, constraints, materials, identity_data, data, text, graphics, construction, visibility, other）");
            }
        }

        private static FamilyParameter RfaFindParam(FamilyManager fm, string name)
        {
            foreach (FamilyParameter p in fm.Parameters)
                if (p.Definition.Name == name) return p;
            return null;
        }

        /// <summary>讀出某類型的參數值（換成 mm / 度 / m² / m³ / 材質名稱）</summary>
        private static object RfaReadValue(Document doc, FamilyType ft, FamilyParameter fp)
        {
            if (ft == null || !ft.HasValue(fp)) return null;
            ForgeTypeId spec = fp.Definition.GetDataType();
            switch (fp.StorageType)
            {
                case StorageType.Double:
                    double d = ft.AsDouble(fp) ?? 0;
                    if (spec == SpecTypeId.Length) return RfaToMm(d);
                    if (spec == SpecTypeId.Angle) return Math.Round(UnitUtils.ConvertFromInternalUnits(d, UnitTypeId.Degrees), 6);
                    if (spec == SpecTypeId.Area) return Math.Round(UnitUtils.ConvertFromInternalUnits(d, UnitTypeId.SquareMeters), 6);
                    if (spec == SpecTypeId.Volume) return Math.Round(UnitUtils.ConvertFromInternalUnits(d, UnitTypeId.CubicMeters), 6);
                    return d;
                case StorageType.Integer:
                    int i = ft.AsInteger(fp) ?? 0;
                    if (spec == SpecTypeId.Boolean.YesNo) return i != 0;
                    return i;
                case StorageType.String:
                    return ft.AsString(fp);
                case StorageType.ElementId:
                    ElementId id = ft.AsElementId(fp);
                    if (id == null || id == ElementId.InvalidElementId) return null;
                    Element e = doc.GetElement(id);
                    return e != null ? (object)e.Name : id.GetIdValue();
                default:
                    return ft.AsValueString(fp);
            }
        }

        /// <summary>依參數型別把 JSON 值寫進目前類型（mm / 度 / m² / m³ / bool / 材質名稱）</summary>
        private static void RfaSetValue(Document doc, FamilyManager fm, FamilyParameter fp, JToken value)
        {
            ForgeTypeId spec = fp.Definition.GetDataType();
            switch (fp.StorageType)
            {
                case StorageType.Double:
                    double d = value.Value<double>();
                    if (spec == SpecTypeId.Length) d = RfaMm(d);
                    else if (spec == SpecTypeId.Angle) d = UnitUtils.ConvertToInternalUnits(d, UnitTypeId.Degrees);
                    else if (spec == SpecTypeId.Area) d = UnitUtils.ConvertToInternalUnits(d, UnitTypeId.SquareMeters);
                    else if (spec == SpecTypeId.Volume) d = UnitUtils.ConvertToInternalUnits(d, UnitTypeId.CubicMeters);
                    fm.Set(fp, d);
                    break;
                case StorageType.Integer:
                    if (value.Type == JTokenType.Boolean) fm.Set(fp, value.Value<bool>() ? 1 : 0);
                    else fm.Set(fp, value.Value<int>());
                    break;
                case StorageType.String:
                    fm.Set(fp, value.Value<string>() ?? string.Empty);
                    break;
                case StorageType.ElementId:
                    ElementId id;
                    if (value.Type == JTokenType.Integer)
                    {
                        id = ((IdType)value.Value<long>()).ToElementId();
                    }
                    else
                    {
                        string matName = value.Value<string>();
                        var mat = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                            .FirstOrDefault(m => m.Name == matName);
                        if (mat == null)
                            throw new Exception($"族群檔內找不到材質「{matName}」（族群只能用已存在於族群檔中的材質）");
                        id = mat.Id;
                    }
                    fm.Set(fp, id);
                    break;
                default:
                    throw new Exception($"不支援的參數儲存型別: {fp.StorageType}");
            }
        }

        private static object RfaDescribeParam(Document doc, FamilyManager fm, FamilyParameter fp)
        {
            string group;
            try { group = fp.Definition.GetGroupTypeId()?.TypeId; } catch { group = null; }
            return new
            {
                name = fp.Definition.Name,
                id = fp.Id.GetIdValue(),
                type = RfaSpecName(fp.Definition.GetDataType()),
                group,
                isInstance = fp.IsInstance,
                isShared = fp.IsShared,
                formula = string.IsNullOrEmpty(fp.Formula) ? null : fp.Formula,
                currentValue = RfaReadValue(doc, fm.CurrentType, fp),
            };
        }

        #endregion

        #region rfa_get_document_info

        private object RfaGetDocumentInfo(JObject parameters)
        {
            Document doc = GetActiveFamilyDocument();
            FamilyManager fm = doc.FamilyManager;
            Family fam = doc.OwnerFamily;

            var refPlanes = new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>()
                .Select(rp =>
                {
                    Plane pl = rp.GetPlane();
                    return new
                    {
                        id = rp.Id.GetIdValue(),
                        name = rp.Name,
                        normal = RfaVec(rp.Normal),
                        origin = RfaPt(pl.Origin),
                        bubbleEnd = RfaPt(rp.BubbleEnd),
                        freeEnd = RfaPt(rp.FreeEnd),
                    };
                }).ToList();

            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Select(l => new { id = l.Id.GetIdValue(), name = l.Name, elevation = RfaToMm(l.Elevation) }).ToList();

            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate)
                .Select(v =>
                {
                    object dir = null;
                    try { dir = RfaVec(v.ViewDirection); } catch { }
                    return new { id = v.Id.GetIdValue(), name = v.Name, viewType = v.ViewType.ToString(), viewDirection = dir };
                }).ToList();

            var parameters_ = fm.Parameters.Cast<FamilyParameter>()
                .OrderBy(p => p.Definition.Name)
                .Select(p => RfaDescribeParam(doc, fm, p)).ToList();

            var types = fm.Types.Cast<FamilyType>().Select(t => t.Name).ToList();

            var forms = new FilteredElementCollector(doc).OfClass(typeof(GenericForm)).Cast<GenericForm>()
                .Select(f => new { id = f.Id.GetIdValue(), kind = f.GetType().Name, isSolid = f.IsSolid, name = f.Name }).ToList();

            return new
            {
                title = doc.Title,
                path = string.IsNullOrEmpty(doc.PathName) ? null : doc.PathName,
                category = fam?.FamilyCategory?.Name,
                familyPlacementType = fam?.FamilyPlacementType.ToString(),
                currentType = fm.CurrentType?.Name,
                types,
                parameters = parameters_,
                referencePlanes = refPlanes,
                levels,
                views,
                forms,
                units = "長度 mm、角度 度、面積 m²、體積 m³",
            };
        }

        #endregion

        #region rfa_create_reference_plane

        private object RfaCreateReferencePlane(JObject parameters)
        {
            Document doc = GetActiveFamilyDocument();
            var planes = parameters["planes"] as JArray;
            if (planes == null || planes.Count == 0)
                throw new Exception("請提供 planes 陣列（每個元素描述一個參考平面）");

            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate &&
                            (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan ||
                             v.ViewType == ViewType.Elevation || v.ViewType == ViewType.Section))
                .ToList();

            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().Select(r => r.Name));

            var created = new List<object>();
            var skipped = new List<object>();

            using (var t = new Transaction(doc, "MCP: rfa_create_reference_plane"))
            {
                t.Start();
                foreach (JObject p in planes.OfType<JObject>())
                {
                    string name = p["name"]?.Value<string>();
                    if (!string.IsNullOrEmpty(name) && existingNames.Contains(name))
                    {
                        skipped.Add(new { name, reason = "已有同名參考平面" });
                        continue;
                    }

                    XYZ bubble, free, cut;
                    string axis = p["normal"]?.Value<string>()?.Trim().ToUpperInvariant();
                    if (!string.IsNullOrEmpty(axis))
                    {
                        double off = RfaMm(p["offsetMm"]?.Value<double>() ?? 0);
                        double ext = RfaMm(p["extentMm"]?.Value<double>() ?? 1000);
                        switch (axis)
                        {
                            case "X": // 平面 x = offset（垂直，平面圖可見）
                                bubble = new XYZ(off, ext, 0); free = new XYZ(off, -ext, 0); cut = XYZ.BasisZ; break;
                            case "Y": // 平面 y = offset（垂直，平面圖可見）
                                bubble = new XYZ(-ext, off, 0); free = new XYZ(ext, off, 0); cut = XYZ.BasisZ; break;
                            case "Z": // 平面 z = offset（水平，前/後立面可見）
                                bubble = new XYZ(-ext, 0, off); free = new XYZ(ext, 0, off); cut = XYZ.BasisY; break;
                            default:
                                throw new Exception($"normal 只接受 X / Y / Z，收到: {axis}");
                        }
                    }
                    else
                    {
                        if (p["bubbleEndMm"] == null || p["freeEndMm"] == null || p["cutVector"] == null)
                            throw new Exception("每個參考平面需提供 normal（+offsetMm），或 bubbleEndMm + freeEndMm + cutVector");
                        bubble = RfaReadXyzMm(p["bubbleEndMm"], "bubbleEndMm");
                        free = RfaReadXyzMm(p["freeEndMm"], "freeEndMm");
                        cut = RfaReadVector(p["cutVector"], "cutVector");
                    }

                    if (bubble.IsAlmostEqualTo(free))
                        throw new Exception($"參考平面「{name}」的兩端點重合");

                    // 參考平面要畫在「視線方向與 cutVector 平行」的視圖裡
                    View host = null;
                    string viewName = p["viewName"]?.Value<string>();
                    if (!string.IsNullOrEmpty(viewName))
                    {
                        host = views.FirstOrDefault(v => v.Name == viewName)
                               ?? throw new Exception($"找不到視圖「{viewName}」");
                    }
                    else
                    {
                        host = views.FirstOrDefault(v =>
                        {
                            try { return Math.Abs(v.ViewDirection.Normalize().DotProduct(cut)) > 0.999; }
                            catch { return false; }
                        });
                        if (host == null)
                            throw new Exception($"找不到可放置參考平面「{name}」的視圖（需要視線方向平行於 cutVector 的平面或立面視圖）");
                    }

                    ReferencePlane rp = doc.FamilyCreate.NewReferencePlane(bubble, free, cut, host);
                    if (!string.IsNullOrEmpty(name))
                    {
                        rp.Name = name;
                        existingNames.Add(name);
                    }

                    created.Add(new
                    {
                        id = rp.Id.GetIdValue(),
                        name = rp.Name,
                        normal = RfaVec(rp.Normal),
                        origin = RfaPt(rp.GetPlane().Origin),
                        hostView = host.Name,
                    });
                }
                t.Commit();
            }

            return new { createdCount = created.Count, created, skipped };
        }

        #endregion

        #region rfa_create_extrusion

        private struct RfaSketchFrame
        {
            public XYZ Origin, U, V, Normal;
            public SketchPlane Sketch;
            public string Description;
            public XYZ At(double uMm, double vMm) => Origin + U * RfaMm(uMm) + V * RfaMm(vMm);
        }

        private RfaSketchFrame RfaResolveSketchFrame(Document doc, JObject parameters)
        {
            string refPlaneName = parameters["referencePlane"]?.Value<string>();
            if (!string.IsNullOrEmpty(refPlaneName))
            {
                var rp = new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>()
                    .FirstOrDefault(r => r.Name == refPlaneName)
                    ?? throw new Exception($"找不到參考平面「{refPlaneName}」");
                Plane pl = rp.GetPlane();
                return new RfaSketchFrame
                {
                    Origin = pl.Origin, U = pl.XVec, V = pl.YVec, Normal = pl.Normal,
                    Sketch = SketchPlane.Create(doc, rp.Id),
                    Description = $"參考平面「{rp.Name}」",
                };
            }

            string plane = (parameters["plane"]?.Value<string>() ?? "XY").Trim().ToUpperInvariant();
            double off = RfaMm(parameters["planeOffsetMm"]?.Value<double>() ?? 0);
            XYZ origin, u, v;
            switch (plane)
            {
                case "XY": origin = new XYZ(0, 0, off); u = XYZ.BasisX; v = XYZ.BasisY; break;
                case "XZ": origin = new XYZ(0, off, 0); u = XYZ.BasisX; v = XYZ.BasisZ; break;
                case "YZ": origin = new XYZ(off, 0, 0); u = XYZ.BasisY; v = XYZ.BasisZ; break;
                default: throw new Exception($"plane 只接受 XY / XZ / YZ，收到: {plane}");
            }
            XYZ n = u.CrossProduct(v);
            Plane geo = Plane.CreateByOriginAndBasis(origin, u, v);
            return new RfaSketchFrame
            {
                Origin = origin, U = u, V = v, Normal = n,
                Sketch = SketchPlane.Create(doc, geo),
                Description = $"{plane} 平面（偏移 {RfaToMm(off)} mm，擠出方向 {n.X:0},{n.Y:0},{n.Z:0}）",
            };
        }

        private static CurveArray RfaBuildLoop(JObject loop, RfaSketchFrame f, int index)
        {
            var arr = new CurveArray();

            if (loop["circle"] is JObject c)
            {
                var ctr = c["center"] as JArray;
                double r = c["radius"]?.Value<double>() ?? 0;
                if (ctr == null || ctr.Count != 2 || r <= 0) throw new Exception($"輪廓 #{index} 的 circle 需要 center [u,v] 與 radius > 0");
                XYZ center = f.At(ctr[0].Value<double>(), ctr[1].Value<double>());
                Plane pl = Plane.CreateByOriginAndBasis(center, f.U, f.V);
                arr.Append(Arc.Create(pl, RfaMm(r), 0, Math.PI));
                arr.Append(Arc.Create(pl, RfaMm(r), Math.PI, 2 * Math.PI));
                return arr;
            }

            List<double[]> pts;
            if (loop["rectangle"] is JObject rect)
            {
                var ctr = rect["center"] as JArray;
                double cu = ctr != null && ctr.Count == 2 ? ctr[0].Value<double>() : 0;
                double cv = ctr != null && ctr.Count == 2 ? ctr[1].Value<double>() : 0;
                double w = rect["width"]?.Value<double>() ?? 0, h = rect["height"]?.Value<double>() ?? 0;
                if (w <= 0 || h <= 0) throw new Exception($"輪廓 #{index} 的 rectangle 需要 width、height > 0");
                pts = new List<double[]>
                {
                    new[] { cu - w / 2, cv - h / 2 }, new[] { cu + w / 2, cv - h / 2 },
                    new[] { cu + w / 2, cv + h / 2 }, new[] { cu - w / 2, cv + h / 2 },
                };
            }
            else if (loop["points"] is JArray pa)
            {
                pts = pa.OfType<JArray>().Select(a => new[] { a[0].Value<double>(), a[1].Value<double>() }).ToList();
            }
            else
            {
                throw new Exception($"輪廓 #{index} 需要 points、rectangle 或 circle 其中之一");
            }

            // 去掉重複的收尾點，自動閉合
            if (pts.Count > 1 && Math.Abs(pts[0][0] - pts[pts.Count - 1][0]) < 1e-6 && Math.Abs(pts[0][1] - pts[pts.Count - 1][1]) < 1e-6)
                pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) throw new Exception($"輪廓 #{index} 至少需要 3 個點");

            for (int i = 0; i < pts.Count; i++)
            {
                XYZ a = f.At(pts[i][0], pts[i][1]);
                XYZ b = f.At(pts[(i + 1) % pts.Count][0], pts[(i + 1) % pts.Count][1]);
                if (a.DistanceTo(b) < RfaMm(0.5))
                    throw new Exception($"輪廓 #{index} 第 {i} 段長度小於 0.5 mm，請合併重複點");
                arr.Append(Line.CreateBound(a, b));
            }
            return arr;
        }

        private object RfaCreateExtrusion(JObject parameters)
        {
            Document doc = GetActiveFamilyDocument();
            FamilyManager fm = doc.FamilyManager;

            var profiles = parameters["profiles"] as JArray;
            if (profiles == null || profiles.Count == 0)
                throw new Exception("請提供 profiles（至少一個封閉輪廓；多個輪廓時，內部輪廓會成為開孔）");

            bool isSolid = parameters["isSolid"]?.Value<bool>() ?? true;
            double startMm = parameters["startOffsetMm"]?.Value<double>() ?? 0;
            double endMm = parameters["endOffsetMm"]?.Value<double>()
                           ?? throw new Exception("請提供 endOffsetMm（擠出終點，沿草圖平面法向量，mm）");
            if (Math.Abs(endMm - startMm) < 0.5) throw new Exception("擠出深度（endOffsetMm - startOffsetMm）太小");

            string materialName = parameters["material"]?.Value<string>();
            var associate = parameters["associate"] as JObject;

            var warnings = new List<string>();
            Extrusion ext;
            RfaSketchFrame frame;

            using (var t = new Transaction(doc, "MCP: rfa_create_extrusion"))
            {
                t.Start();
                frame = RfaResolveSketchFrame(doc, parameters);

                var loops = new CurveArrArray();
                int idx = 0;
                foreach (JObject loop in profiles.OfType<JObject>())
                    loops.Append(RfaBuildLoop(loop, frame, idx++));

                try
                {
                    ext = doc.FamilyCreate.NewExtrusion(isSolid, loops, frame.Sketch, RfaMm(endMm));
                }
                catch (Exception ex)
                {
                    throw new Exception($"建立擠出失敗：{ex.Message}（常見原因：輪廓不封閉或自我交錯、族群樣板不支援擠出（例如概念量體／自適應族群））");
                }
                if (ext == null) throw new Exception("建立擠出失敗（Revit 回傳 null）");

                ext.StartOffset = RfaMm(startMm);
                ext.EndOffset = RfaMm(endMm);

                // 材質與參數綁定（例如擠出終點 ↔ 厚度），共用 RfaFamily2.cs 的輔助函式
                var assocResults = RfaApplyMaterialAndAssociations(doc, fm, ext, isSolid, materialName, associate,
                    new Dictionary<string, BuiltInParameter>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "extrusionEnd", BuiltInParameter.EXTRUSION_END_PARAM },
                        { "extrusionStart", BuiltInParameter.EXTRUSION_START_PARAM },
                    }, warnings);

                t.Commit();

                return new
                {
                    id = ext.Id.GetIdValue(),
                    isSolid,
                    sketchPlane = frame.Description,
                    startOffsetMm = RfaToMm(ext.StartOffset),
                    endOffsetMm = RfaToMm(ext.EndOffset),
                    profileCount = profiles.Count,
                    associated = assocResults,
                    warnings,
                };
            }
        }

        #endregion

        #region rfa_add_parameter

        private object RfaAddParameter(JObject parameters)
        {
            Document doc = GetActiveFamilyDocument();
            FamilyManager fm = doc.FamilyManager;

            var list = parameters["parameters"] as JArray;
            if (list == null || list.Count == 0)
                throw new Exception("請提供 parameters 陣列（每個元素含 name、type，可選 group、isInstance、formula、value）");

            var created = new List<object>();
            var skipped = new List<object>();
            var errors = new List<object>();

            using (var t = new Transaction(doc, "MCP: rfa_add_parameter"))
            {
                t.Start();
                foreach (JObject p in list.OfType<JObject>())
                {
                    string name = p["name"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(name)) { errors.Add(new { name, error = "缺少 name" }); continue; }

                    if (RfaFindParam(fm, name) != null)
                    {
                        skipped.Add(new { name, reason = "已有同名族群參數" });
                        continue;
                    }

                    string typeKey = (p["type"]?.Value<string>() ?? "length").Trim().ToLowerInvariant();
                    ForgeTypeId spec = RfaSpecMap.Where(m => m.Key == typeKey).Select(m => m.Spec).FirstOrDefault();
                    if (spec == null)
                    {
                        errors.Add(new { name, error = $"不支援的 type「{typeKey}」（可用: {string.Join(", ", RfaSpecMap.Select(m => m.Key))}）" });
                        continue;
                    }

                    bool isInstance = p["isInstance"]?.Value<bool>() ?? false;
                    string groupName = p["group"]?.Value<string>()
                                       ?? (typeKey == "material" ? "materials" : typeKey == "text" ? "identity_data" : "dimensions");

                    FamilyParameter fp;
                    try
                    {
                        fp = fm.AddParameter(name, RfaGroupFromName(groupName), spec, isInstance);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new { name, error = $"新增失敗：{ex.Message}" });
                        continue;
                    }

                    string formula = p["formula"]?.Value<string>();
                    string note = null;
                    if (!string.IsNullOrEmpty(formula))
                    {
                        try { fm.SetFormula(fp, formula); }
                        catch (Exception ex) { note = $"公式設定失敗：{ex.Message}"; }
                    }
                    else if (p["value"] != null && p["value"].Type != JTokenType.Null)
                    {
                        if (fm.CurrentType == null)
                            note = "族群目前沒有任何類型，未寫入 value（可用 rfa_set_family_type 建立類型）";
                        else
                        {
                            try { RfaSetValue(doc, fm, fp, p["value"]); }
                            catch (Exception ex) { note = $"值設定失敗：{ex.Message}"; }
                        }
                    }

                    created.Add(new
                    {
                        name,
                        type = typeKey,
                        group = groupName,
                        isInstance,
                        formula = string.IsNullOrEmpty(fp.Formula) ? null : fp.Formula,
                        currentValue = RfaReadValue(doc, fm.CurrentType, fp),
                        note,
                    });
                }
                t.Commit();
            }

            return new { createdCount = created.Count, created, skipped, errors };
        }

        #endregion

        #region rfa_set_family_type

        private object RfaSetFamilyType(JObject parameters)
        {
            Document doc = GetActiveFamilyDocument();
            FamilyManager fm = doc.FamilyManager;

            string typeName = parameters["typeName"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(typeName)) throw new Exception("請提供 typeName");
            string baseType = parameters["baseType"]?.Value<string>();
            string renameTo = parameters["renameTo"]?.Value<string>();
            var values = parameters["values"] as JObject;
            var formulas = parameters["formulas"] as JObject;

            bool createdNew = false;
            var applied = new List<object>();
            var errors = new List<object>();

            using (var t = new Transaction(doc, "MCP: rfa_set_family_type"))
            {
                t.Start();

                FamilyType target = fm.Types.Cast<FamilyType>().FirstOrDefault(ft => ft.Name == typeName);
                if (target == null)
                {
                    // 新類型會複製「目前類型」的值；指定 baseType 時先切到它
                    if (!string.IsNullOrEmpty(baseType))
                    {
                        FamilyType b = fm.Types.Cast<FamilyType>().FirstOrDefault(ft => ft.Name == baseType)
                                       ?? throw new Exception($"找不到 baseType「{baseType}」");
                        fm.CurrentType = b;
                    }
                    target = fm.NewType(typeName);
                    createdNew = true;
                }
                fm.CurrentType = target;

                if (!string.IsNullOrEmpty(renameTo) && renameTo != typeName)
                {
                    if (fm.Types.Cast<FamilyType>().Any(ft => ft.Name == renameTo))
                        throw new Exception($"已有名為「{renameTo}」的類型，無法改名");
                    fm.RenameCurrentType(renameTo);
                }

                if (formulas != null)
                {
                    foreach (var kv in formulas)
                    {
                        FamilyParameter fp = RfaFindParam(fm, kv.Key);
                        if (fp == null) { errors.Add(new { parameter = kv.Key, error = "找不到此族群參數" }); continue; }
                        try
                        {
                            string f = kv.Value?.Type == JTokenType.Null ? null : kv.Value?.Value<string>();
                            fm.SetFormula(fp, string.IsNullOrEmpty(f) ? null : f);
                            applied.Add(new { parameter = kv.Key, formula = f });
                        }
                        catch (Exception ex) { errors.Add(new { parameter = kv.Key, error = $"公式設定失敗：{ex.Message}" }); }
                    }
                }

                if (values != null)
                {
                    foreach (var kv in values)
                    {
                        FamilyParameter fp = RfaFindParam(fm, kv.Key);
                        if (fp == null) { errors.Add(new { parameter = kv.Key, error = "找不到此族群參數" }); continue; }
                        if (!string.IsNullOrEmpty(fp.Formula))
                        {
                            errors.Add(new { parameter = kv.Key, error = $"此參數由公式「{fp.Formula}」驅動，不能直接設值" });
                            continue;
                        }
                        try
                        {
                            RfaSetValue(doc, fm, fp, kv.Value);
                            applied.Add(new { parameter = kv.Key, value = RfaReadValue(doc, fm.CurrentType, fp) });
                        }
                        catch (Exception ex) { errors.Add(new { parameter = kv.Key, error = ex.Message }); }
                    }
                }

                t.Commit();
            }

            var finalValues = fm.Parameters.Cast<FamilyParameter>()
                .OrderBy(p => p.Definition.Name)
                .ToDictionary(p => p.Definition.Name, p => RfaReadValue(doc, fm.CurrentType, p));

            return new
            {
                type = fm.CurrentType?.Name,
                createdNew,
                allTypes = fm.Types.Cast<FamilyType>().Select(ft => ft.Name).ToList(),
                applied,
                errors,
                values = finalValues,
            };
        }

        #endregion
    }
}
