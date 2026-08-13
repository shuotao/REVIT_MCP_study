using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// MEP 尺寸的「用量盤點」與「增減」。對應 domain/mep-mechanical-settings.md。
    ///
    /// get_mep_size_usage — 掃模型裡真的有元件在用哪些尺寸（目錄 ≠ 用量，這是刪除守門的地基）。
    /// curate_mep_sizes   — 增／減。增無限；減只能刪沒有元件在用的，
    ///                      且走「列表 → 執行 → QC → 誤刪復原」四步。
    /// </summary>
    public partial class CommandExecutor
    {
        #region 共用：用量掃描

        /// <summary>尺寸比對容差（mm）。元件尺寸與目錄值可能有浮點誤差，不能用相等比。</summary>
        private const double MepSizeToleranceMm = 0.05;

        /// <summary>
        /// 單一尺寸值的用量累計。
        /// 注意 Total 是「命中次數」不是「元件數」——一個配件的寬與高都可能命中同一個值，
        /// 多個接頭也各算一次。要回答「幾個元件在用」請看 ElementCount。
        /// </summary>
        private class MepSizeUsage
        {
            public double Mm;
            public int CurveCount;      // 直管 / 直風管
            public int FittingCount;    // 配件 + 附件（由 Connector 取得）
            public HashSet<IdType> Elements = new HashSet<IdType>();
            public List<IdType> SampleIds = new List<IdType>();
            public int Total { get { return CurveCount + FittingCount; } }
            public int ElementCount { get { return Elements.Count; } }
        }

        /// <summary>把一筆用量累進 bucket；同一值以容差併攏</summary>
        private static void MepAccumulate(List<MepSizeUsage> bucket, double valueMm, bool fromFitting, IdType elementId, int maxSamples)
        {
            if (valueMm <= 0) return;

            MepSizeUsage hit = bucket.FirstOrDefault(u => Math.Abs(u.Mm - valueMm) <= MepSizeToleranceMm);
            if (hit == null)
            {
                hit = new MepSizeUsage { Mm = Math.Round(valueMm, 4) };
                bucket.Add(hit);
            }

            if (fromFitting) hit.FittingCount++; else hit.CurveCount++;

            // 只有第一次見到這個元件才收樣本，避免同一個 ID 佔滿樣本欄位
            if (hit.Elements.Add(elementId) && hit.SampleIds.Count < maxSamples) hit.SampleIds.Add(elementId);
        }

        /// <summary>由 Connector 判斷斷面形狀；取第一個有效形狀的接頭</summary>
        private static ConnectorProfileType MepGetProfile(ConnectorManager manager)
        {
            if (manager == null) return ConnectorProfileType.Invalid;
            foreach (Connector c in manager.Connectors)
            {
                if (c != null && c.Shape != ConnectorProfileType.Invalid) return c.Shape;
            }
            return ConnectorProfileType.Invalid;
        }

        /// <summary>掃風管用量：直風管的寬/高/直徑 + 配件與附件的 Connector 尺寸</summary>
        private static Dictionary<DuctShape, List<MepSizeUsage>> MepScanDuctUsage(Document doc, int maxSamples)
        {
            var result = new Dictionary<DuctShape, List<MepSizeUsage>>
            {
                { DuctShape.Round, new List<MepSizeUsage>() },
                { DuctShape.Rectangular, new List<MepSizeUsage>() },
                { DuctShape.Oval, new List<MepSizeUsage>() },
            };

            // 1) 直風管
            foreach (Duct duct in new FilteredElementCollector(doc).OfClass(typeof(Duct)).Cast<Duct>())
            {
                IdType id = duct.Id.GetIdValue();
                ConnectorProfileType profile;
                try { profile = MepGetProfile(duct.ConnectorManager); }
                catch { continue; }

                try
                {
                    if (profile == ConnectorProfileType.Round)
                    {
                        MepAccumulate(result[DuctShape.Round], ToMm(duct.Diameter), false, id, maxSamples);
                    }
                    else if (profile == ConnectorProfileType.Rectangular || profile == ConnectorProfileType.Oval)
                    {
                        // Rect / Oval 的尺寸表是單一維度清單，同時餵寬與高兩個下拉
                        DuctShape shape = profile == ConnectorProfileType.Oval ? DuctShape.Oval : DuctShape.Rectangular;
                        MepAccumulate(result[shape], ToMm(duct.Width), false, id, maxSamples);
                        MepAccumulate(result[shape], ToMm(duct.Height), false, id, maxSamples);
                    }
                }
                catch { /* 個別元件讀不到尺寸不該中斷整份掃描 */ }
            }

            // 2) 配件與附件（漏掃這段會把「只有變徑頭在用」的尺寸誤判成可刪）
            MepScanFittingConnectors(doc,
                new[] { BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory },
                (shape, mm, id) => MepAccumulate(result[shape], mm, true, id, maxSamples));

            return result;
        }

        /// <summary>走訪配件／附件的 End 接頭，回呼每一個尺寸值</summary>
        private static void MepScanFittingConnectors(
            Document doc, BuiltInCategory[] categories, Action<DuctShape, double, IdType> onSize)
        {
            foreach (BuiltInCategory category in categories)
            {
                var instances = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>();

                foreach (FamilyInstance fi in instances)
                {
                    ConnectorManager manager;
                    try { manager = fi.MEPModel?.ConnectorManager; }
                    catch { continue; }
                    if (manager == null) continue;

                    IdType id = fi.Id.GetIdValue();

                    foreach (Connector c in manager.Connectors)
                    {
                        if (c == null || c.ConnectorType != ConnectorType.End) continue;

                        try
                        {
                            if (c.Shape == ConnectorProfileType.Round)
                            {
                                onSize(DuctShape.Round, ToMm(c.Radius * 2.0), id);
                            }
                            else if (c.Shape == ConnectorProfileType.Rectangular || c.Shape == ConnectorProfileType.Oval)
                            {
                                DuctShape shape = c.Shape == ConnectorProfileType.Oval ? DuctShape.Oval : DuctShape.Rectangular;
                                onSize(shape, ToMm(c.Width), id);
                                onSize(shape, ToMm(c.Height), id);
                            }
                        }
                        catch { /* 單一接頭讀不到不中斷 */ }
                    }
                }
            }
        }

        /// <summary>
        /// 掃管用量。Pipe.PipeSegment 是直接屬性，所以「哪個 segment 的哪個管徑」可以精確歸戶。
        /// 管配件／附件沒有 PipeSegment，只能取到直徑 → 歸到 unattributed，刪除時保守處理。
        /// </summary>
        private static void MepScanPipeUsage(
            Document doc, int maxSamples,
            out Dictionary<IdType, List<MepSizeUsage>> bySegment,
            out List<MepSizeUsage> unattributed)
        {
            bySegment = new Dictionary<IdType, List<MepSizeUsage>>();
            unattributed = new List<MepSizeUsage>();

            foreach (Pipe pipe in new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>())
            {
                IdType id = pipe.Id.GetIdValue();

                PipeSegment segment = null;
                try { segment = pipe.PipeSegment; }
                catch { /* placeholder 之類可能取不到 */ }

                double diameterMm;
                try
                {
                    // 優先取「公稱直徑」參數；取不到才退回 MEPCurve.Diameter
                    Parameter p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    diameterMm = (p != null && p.StorageType == StorageType.Double)
                        ? ToMm(p.AsDouble())
                        : ToMm(pipe.Diameter);
                }
                catch { continue; }

                if (segment == null)
                {
                    MepAccumulate(unattributed, diameterMm, false, id, maxSamples);
                    continue;
                }

                IdType segId = segment.Id.GetIdValue();
                if (!bySegment.ContainsKey(segId)) bySegment[segId] = new List<MepSizeUsage>();
                MepAccumulate(bySegment[segId], diameterMm, false, id, maxSamples);
            }

            // 管配件／附件：只有直徑，無法歸到特定 segment
            var localUnattributed = unattributed;
            int samples = maxSamples;
            MepScanFittingConnectors(doc,
                new[] { BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory },
                (shape, mm, id) =>
                {
                    if (shape == DuctShape.Round) MepAccumulate(localUnattributed, mm, true, id, samples);
                });
        }

        /// <summary>在用量清單中找出與某個目錄尺寸相符的那一筆（容差比對）</summary>
        private static MepSizeUsage MepFindUsage(List<MepSizeUsage> usages, double catalogMm)
        {
            if (usages == null) return null;
            return usages.FirstOrDefault(u => Math.Abs(u.Mm - catalogMm) <= MepSizeToleranceMm);
        }

        /// <summary>mm 轉內部單位（feet）</summary>
        private static double ToFeet(double mm)
        {
            return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        }

        #endregion

        #region get_mep_size_usage

        private object GetMepSizeUsage(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string scope = (parameters["scope"]?.Value<string>() ?? "both").Trim().ToLowerInvariant();
            bool wantDuct = scope == "both" || scope == "duct";
            bool wantPipe = scope == "both" || scope == "pipe";
            bool includeUnused = parameters["includeUnused"]?.Value<bool?>() ?? true;
            bool includeElementIds = parameters["includeElementIds"]?.Value<bool?>() ?? false;
            int maxSamples = parameters["maxElementIdsPerSize"]?.Value<int?>() ?? 5;
            string shapeFilter = parameters["shape"]?.Value<string>()?.Trim();
            string segmentFilter = parameters["segmentName"]?.Value<string>()?.Trim();

            object duct = null;
            object pipe = null;

            if (wantDuct)
            {
                var usage = MepScanDuctUsage(doc, maxSamples);
                var shapes = new List<object>();

                DuctSizeSettings settings = DuctSizeSettings.GetDuctSizeSettings(doc);

                foreach (DuctShape shape in new[] { DuctShape.Round, DuctShape.Rectangular, DuctShape.Oval })
                {
                    if (!string.IsNullOrWhiteSpace(shapeFilter) &&
                        !string.Equals(shapeFilter, shape.ToString(), StringComparison.OrdinalIgnoreCase)) continue;

                    var catalog = new List<double>();
                    if (settings != null && settings[shape] != null)
                    {
                        foreach (MEPSize s in EnumerateDuctSizes(settings[shape]))
                            catalog.Add(ToMm(s.NominalDiameter));
                    }

                    shapes.Add(MepBuildUsageReport(
                        shape.ToString(), catalog, usage[shape], includeUnused, includeElementIds));
                }

                duct = new { shapes };
            }

            if (wantPipe)
            {
                MepScanPipeUsage(doc, maxSamples, out var bySegment, out var unattributed);

                var segments = new List<object>();
                var pipeSegments = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipeSegment))
                    .Cast<PipeSegment>()
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

                foreach (PipeSegment segment in pipeSegments)
                {
                    if (!string.IsNullOrWhiteSpace(segmentFilter) &&
                        (segment.Name == null ||
                         segment.Name.IndexOf(segmentFilter, StringComparison.OrdinalIgnoreCase) < 0)) continue;

                    IdType segId = segment.Id.GetIdValue();
                    List<MepSizeUsage> usages = bySegment.ContainsKey(segId) ? bySegment[segId] : new List<MepSizeUsage>();
                    var catalog = segment.GetSizes().Select(s => ToMm(s.NominalDiameter)).ToList();

                    var report = MepBuildUsageReport(segment.Name, catalog, usages, includeUnused, includeElementIds);
                    segments.Add(new { id = segId, name = segment.Name, report });
                }

                pipe = new
                {
                    segments,
                    // 配件沒有 PipeSegment，只知道直徑。刪除時要保守：任何 segment 中相符的尺寸都可能是它在用。
                    unattributedFittingSizes = unattributed
                        .OrderBy(u => u.Mm)
                        .Select(u => new { nominal_mm = u.Mm, count = u.Total, sampleElementIds = includeElementIds ? u.SampleIds : null })
                        .ToList(),
                    unattributedNote = "管配件／附件沒有 PipeSegment 屬性，只能取到直徑，無法歸到特定 segment。curate_mep_sizes 刪除時預設會把相符的尺寸一併擋下。",
                };
            }

            return new
            {
                Success = true,
                Scope = scope,
                Duct = duct,
                Pipe = pipe,
                Tolerance_mm = MepSizeToleranceMm,
                Message = "用量盤點完成（唯讀）。usageCount=0 才是可刪候選；orphans 是模型有用但目錄沒有的尺寸，屬「該增」的候選。"
            };
        }

        /// <summary>把「目錄」與「用量」對起來，產出可刪候選與 orphan 清單</summary>
        private static object MepBuildUsageReport(
            string label, List<double> catalogMm, List<MepSizeUsage> usages, bool includeUnused, bool includeElementIds)
        {
            var rows = new List<object>();
            var matched = new HashSet<MepSizeUsage>();
            int usedCount = 0;

            foreach (double catalog in catalogMm.OrderBy(v => v))
            {
                MepSizeUsage hit = MepFindUsage(usages, catalog);
                if (hit != null) { matched.Add(hit); usedCount++; }
                if (hit == null && !includeUnused) continue;

                rows.Add(new
                {
                    nominal_mm = Math.Round(catalog, 4),
                    usageCount = hit?.Total ?? 0,          // 命中次數（寬/高/多接頭各算一次）
                    elementCount = hit?.ElementCount ?? 0, // 不重複的元件數
                    fromCurves = hit?.CurveCount ?? 0,
                    fromFittings = hit?.FittingCount ?? 0,
                    removable = hit == null,
                    sampleElementIds = (includeElementIds && hit != null) ? hit.SampleIds : null,
                });
            }

            // 模型有用、目錄沒有 → 該增的候選，不是錯誤
            var orphans = usages
                .Where(u => !matched.Contains(u))
                .OrderBy(u => u.Mm)
                .Select(u => new
                {
                    nominal_mm = u.Mm,
                    usageCount = u.Total,
                    elementCount = u.ElementCount,
                    fromCurves = u.CurveCount,
                    fromFittings = u.FittingCount,
                    sampleElementIds = includeElementIds ? u.SampleIds : null,
                })
                .ToList();

            return new
            {
                name = label,
                catalogCount = catalogMm.Count,
                usedCount,
                removableCount = catalogMm.Count - usedCount,
                countsNote = "usageCount 是命中次數（一個配件的寬與高、多個接頭各算一次）；要看幾個元件在用請用 elementCount。",
                sizes = rows,
                orphans,
            };
        }

        #endregion

        #region curate_mep_sizes

        /// <summary>目錄裡的一筆尺寸快照（含復原所需的完整定義）</summary>
        private class MepSizeSnapshot
        {
            public double NominalFeet;
            public double InnerFeet;
            public double OuterFeet;
            public bool UsedInSizeLists;
            public bool UsedInSizing;
            public double NominalMm { get { return Math.Round(UnitUtils.ConvertFromInternalUnits(NominalFeet, UnitTypeId.Millimeters), 4); } }

            public object ToRestorePayload()
            {
                return new
                {
                    nominal_mm = NominalMm,
                    inner_mm = Math.Round(UnitUtils.ConvertFromInternalUnits(InnerFeet, UnitTypeId.Millimeters), 4),
                    outer_mm = Math.Round(UnitUtils.ConvertFromInternalUnits(OuterFeet, UnitTypeId.Millimeters), 4),
                    usedInSizeLists = UsedInSizeLists,
                    usedInSizing = UsedInSizing,
                };
            }

            public MEPSize ToMepSize()
            {
                return new MEPSize(NominalFeet, InnerFeet, OuterFeet, UsedInSizeLists, UsedInSizing);
            }
        }

        private object CurateMepSizes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string target = parameters["target"]?.Value<string>()?.Trim().ToLowerInvariant();
            if (target != "pipe" && target != "duct")
                throw new Exception("target 必須是 'pipe' 或 'duct'。");

            bool dryRun = parameters["dryRun"]?.Value<bool?>() ?? true;   // 預設不執行，先看清單
            bool ignoreUnattributedFittings = parameters["ignoreUnattributedFittings"]?.Value<bool?>() ?? false;
            int maxSamples = 5;

            // ── 解析目標目錄 ────────────────────────────────────────────
            PipeSegment pipeSegment = null;
            DuctShape ductShape = DuctShape.Round;
            DuctSizeSettings ductSettings = null;
            string targetLabel;

            if (target == "pipe")
            {
                string segmentName = parameters["segmentName"]?.Value<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(segmentName))
                    throw new Exception("target='pipe' 時必須指定 segmentName（例如 'Copper - K'）。");

                var candidates = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipeSegment))
                    .Cast<PipeSegment>()
                    .Where(s => s.Name != null && s.Name.IndexOf(segmentName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                if (candidates.Count == 0)
                    throw new Exception($"找不到名稱含 '{segmentName}' 的管段。");
                if (candidates.Count > 1)
                    throw new Exception($"'{segmentName}' 對應到 {candidates.Count} 個管段（{string.Join(" / ", candidates.Select(c => c.Name))}），請給更精確的名稱。");

                pipeSegment = candidates[0];
                targetLabel = pipeSegment.Name;
            }
            else
            {
                string shapeText = parameters["shape"]?.Value<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(shapeText) || !Enum.TryParse(shapeText, true, out ductShape))
                    throw new Exception("target='duct' 時必須指定 shape：Round / Rectangular / Oval。");

                ductSettings = DuctSizeSettings.GetDuctSizeSettings(doc);
                if (ductSettings == null) throw new Exception("此文件沒有 DuctSizeSettings（可能不是 MEP 樣板）。");
                targetLabel = $"Duct {ductShape}";
            }

            // ── ① 列表：現況快照 + 用量盤點 ──────────────────────────────
            List<MepSizeSnapshot> before = MepSnapshotCatalog(pipeSegment, ductSettings, ductShape);
            List<MepSizeUsage> usages = MepUsageForTarget(doc, target, pipeSegment, ductShape, maxSamples,
                                                          out List<MepSizeUsage> unattributed);

            // 刪除計畫
            var toRemove = new List<MepSizeSnapshot>();
            var blocked = new List<object>();
            var removeNotFound = new List<double>();

            var removeRequests = (parameters["remove"] as JArray)?.Select(t => t.Value<double>()).ToList() ?? new List<double>();
            foreach (double requestedMm in removeRequests)
            {
                MepSizeSnapshot match = before.FirstOrDefault(s => Math.Abs(s.NominalMm - requestedMm) <= MepSizeToleranceMm);
                if (match == null) { removeNotFound.Add(requestedMm); continue; }

                MepSizeUsage inUse = MepFindUsage(usages, match.NominalMm);
                MepSizeUsage fittingHit = (target == "pipe" && !ignoreUnattributedFittings)
                    ? MepFindUsage(unattributed, match.NominalMm) : null;

                if (inUse != null || fittingHit != null)
                {
                    blocked.Add(new
                    {
                        nominal_mm = match.NominalMm,
                        reason = inUse != null ? "模型中有元件正在使用" : "有管配件／附件的接頭是這個直徑（無法歸戶到 segment，保守擋下）",
                        usageCount = inUse?.Total ?? 0,
                        elementCount = (inUse ?? fittingHit)?.ElementCount ?? 0,
                        fromCurves = inUse?.CurveCount ?? 0,
                        fromFittings = (inUse?.FittingCount ?? 0) + (fittingHit?.Total ?? 0),
                        sampleElementIds = (inUse ?? fittingHit)?.SampleIds,
                    });
                    continue;
                }

                toRemove.Add(match);
            }

            // 新增計畫
            var toAdd = new List<MepSizeSnapshot>();
            var addAlreadyExists = new List<double>();

            var addRequests = (parameters["add"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            foreach (JObject request in addRequests)
            {
                double? nominalMm = request["nominal_mm"]?.Value<double?>();
                if (nominalMm == null || nominalMm <= 0)
                    throw new Exception("add 的每一筆都必須有正的 nominal_mm。");

                if (before.Any(s => Math.Abs(s.NominalMm - nominalMm.Value) <= MepSizeToleranceMm))
                {
                    addAlreadyExists.Add(nominalMm.Value);
                    continue;
                }

                double innerMm, outerMm;
                if (target == "pipe")
                {
                    // 管的內外徑是實際物理量（水力計算要用），不能由工具代為臆造
                    double? inner = request["inner_mm"]?.Value<double?>();
                    double? outer = request["outer_mm"]?.Value<double?>();
                    if (inner == null || outer == null)
                        throw new Exception($"新增管尺寸 {nominalMm} mm 必須同時給 inner_mm 與 outer_mm（內外徑是水力計算依據，不可省略由工具臆造）。");
                    innerMm = inner.Value;
                    outerMm = outer.Value;
                }
                else
                {
                    // 風管尺寸表只有 nominal 一個維度，inner/outer 是 Revit 忽略的佔位值。
                    // 有給就照用（讓「原樣復原」拿得回原本的佔位值），沒給才填 nominal。
                    innerMm = request["inner_mm"]?.Value<double?>() ?? nominalMm.Value;
                    outerMm = request["outer_mm"]?.Value<double?>() ?? nominalMm.Value;
                }

                toAdd.Add(new MepSizeSnapshot
                {
                    NominalFeet = ToFeet(nominalMm.Value),
                    InnerFeet = ToFeet(innerMm),
                    OuterFeet = ToFeet(outerMm),
                    UsedInSizeLists = request["usedInSizeLists"]?.Value<bool?>() ?? true,
                    UsedInSizing = request["usedInSizing"]?.Value<bool?>() ?? true,
                });
            }

            var plan = new
            {
                target = targetLabel,
                willAdd = toAdd.Select(s => s.ToRestorePayload()).ToList(),
                willRemove = toRemove.Select(s => s.ToRestorePayload()).ToList(),
                blocked,
                addAlreadyExists,
                removeNotFound,
                catalogCountBefore = before.Count,
            };

            if (dryRun)
            {
                return new
                {
                    Success = true,
                    DryRun = true,
                    Plan = plan,
                    RestorePayload = toRemove.Select(s => s.ToRestorePayload()).ToList(),
                    Message = $"這是預覽，未變更任何東西。預計新增 {toAdd.Count} 筆、移除 {toRemove.Count} 筆，擋下 {blocked.Count} 筆（模型仍在使用）。確認無誤後帶 dryRun=false 再呼叫一次。"
                };
            }

            // ── ② 執行：單一 Transaction（先減後增，讓「換尺寸」語意正確）────
            using (Transaction t = new Transaction(doc, "Curate MEP Sizes"))
            {
                t.Start();
                foreach (MepSizeSnapshot s in toRemove) MepRemoveSize(pipeSegment, ductSettings, ductShape, s.NominalFeet);
                foreach (MepSizeSnapshot s in toAdd) MepAddSize(pipeSegment, ductSettings, ductShape, s.ToMepSize());
                t.Commit();
            }

            // ── ③ QC：重讀目錄，與「執行前 − 預期移除 + 預期新增」逐筆比對 ──
            List<MepSizeSnapshot> after = MepSnapshotCatalog(pipeSegment, ductSettings, ductShape);

            var expected = before
                .Where(b => !toRemove.Any(r => Math.Abs(r.NominalMm - b.NominalMm) <= MepSizeToleranceMm))
                .Select(b => b.NominalMm)
                .Concat(toAdd.Select(a => a.NominalMm))
                .OrderBy(v => v)
                .ToList();

            var actual = after.Select(a => a.NominalMm).OrderBy(v => v).ToList();

            var missing = expected.Where(e => !actual.Any(a => Math.Abs(a - e) <= MepSizeToleranceMm)).ToList();
            var unexpected = actual.Where(a => !expected.Any(e => Math.Abs(a - e) <= MepSizeToleranceMm)).ToList();

            // ── ④ 誤刪復原：expected 有但 actual 沒有＝被多刪，用快照原樣加回 ──
            var restored = new List<object>();
            var restoreFailed = new List<object>();

            if (missing.Count > 0)
            {
                using (Transaction t = new Transaction(doc, "Restore Accidentally Removed MEP Sizes"))
                {
                    t.Start();
                    foreach (double lost in missing)
                    {
                        MepSizeSnapshot original = before.FirstOrDefault(b => Math.Abs(b.NominalMm - lost) <= MepSizeToleranceMm);
                        if (original == null)
                        {
                            restoreFailed.Add(new { nominal_mm = lost, reason = "這是本次新增失敗的尺寸，執行前的快照裡沒有可復原的定義" });
                            continue;
                        }

                        try
                        {
                            MepAddSize(pipeSegment, ductSettings, ductShape, original.ToMepSize());
                            restored.Add(original.ToRestorePayload());
                        }
                        catch (Exception ex)
                        {
                            restoreFailed.Add(new { nominal_mm = lost, reason = ex.Message });
                        }
                    }
                    t.Commit();
                }
            }

            // 復原後再驗一次
            List<double> finalActual = MepSnapshotCatalog(pipeSegment, ductSettings, ductShape)
                .Select(s => s.NominalMm).OrderBy(v => v).ToList();
            var stillMissing = expected.Where(e => !finalActual.Any(a => Math.Abs(a - e) <= MepSizeToleranceMm)).ToList();
            bool qcPassed = stillMissing.Count == 0 && unexpected.Count == 0;

            return new
            {
                Success = true,
                DryRun = false,
                Plan = plan,
                Qc = new
                {
                    passed = qcPassed,
                    catalogCountBefore = before.Count,
                    catalogCountAfter = finalActual.Count,
                    expectedCount = expected.Count,
                    accidentallyRemoved = missing,      // ③ 抓到的誤刪
                    restored,                           // ④ 已復原
                    restoreFailed,
                    stillMissing,                       // 復原後仍缺（要人工處理）
                    unexpectedExtra = unexpected,       // 目錄裡多出預期外的尺寸
                },
                RestorePayload = toRemove.Select(s => s.ToRestorePayload()).ToList(),
                Message = qcPassed
                    ? $"已套用並通過 QC：新增 {toAdd.Count} 筆、移除 {toRemove.Count} 筆、擋下 {blocked.Count} 筆。可 Ctrl+Z 還原；RestorePayload 保留了被移除尺寸的完整定義。"
                    : $"已套用但 QC 未通過：仍缺 {stillMissing.Count} 筆、預期外多出 {unexpected.Count} 筆。已自動復原 {restored.Count} 筆。請人工檢查後再處理。"
            };
        }

        /// <summary>讀出目標目錄的完整快照（復原要用，所以連 inner/outer 與兩個旗標都留著）</summary>
        private static List<MepSizeSnapshot> MepSnapshotCatalog(
            PipeSegment pipeSegment, DuctSizeSettings ductSettings, DuctShape ductShape)
        {
            IEnumerable<MEPSize> sizes = pipeSegment != null
                ? pipeSegment.GetSizes()
                : EnumerateDuctSizes(ductSettings[ductShape]);

            return sizes
                .Select(s => new MepSizeSnapshot
                {
                    NominalFeet = s.NominalDiameter,
                    InnerFeet = s.InnerDiameter,
                    OuterFeet = s.OuterDiameter,
                    UsedInSizeLists = s.UsedInSizeLists,
                    UsedInSizing = s.UsedInSizing,
                })
                .OrderBy(s => s.NominalFeet)
                .ToList();
        }

        /// <summary>取目標的用量清單；pipe 另外回傳無法歸戶的配件尺寸</summary>
        private static List<MepSizeUsage> MepUsageForTarget(
            Document doc, string target, PipeSegment pipeSegment, DuctShape ductShape, int maxSamples,
            out List<MepSizeUsage> unattributed)
        {
            unattributed = new List<MepSizeUsage>();

            if (target == "duct")
            {
                return MepScanDuctUsage(doc, maxSamples)[ductShape];
            }

            MepScanPipeUsage(doc, maxSamples, out var bySegment, out unattributed);
            IdType segId = pipeSegment.Id.GetIdValue();
            return bySegment.ContainsKey(segId) ? bySegment[segId] : new List<MepSizeUsage>();
        }

        private static void MepAddSize(PipeSegment pipeSegment, DuctSizeSettings ductSettings, DuctShape ductShape, MEPSize size)
        {
            if (pipeSegment != null) pipeSegment.AddSize(size);
            else ductSettings.AddSize(ductShape, size);
        }

        private static void MepRemoveSize(PipeSegment pipeSegment, DuctSizeSettings ductSettings, DuctShape ductShape, double nominalFeet)
        {
            // 傳目錄裡存的原值，不要用重建的浮點數，否則可能刪錯對象或刪不掉
            if (pipeSegment != null) pipeSegment.RemoveSize(nominalFeet);
            else ductSettings.RemoveSize(ductShape, nominalFeet);
        }

        #endregion
    }
}
