using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 結構構架端點對齊的純幾何決策層。
    ///
    /// 刻意與 Revit 的 Document/Element 解耦：只吃 bbox 八角世界座標、端點、外向向量、
    /// 是否為柱、型號名稱。理由是這段（候選過濾 → T/L 判別 → 貼合面位置）是整個工具唯一
    /// 算得出「錯值」的地方，而它一旦綁上 Element 就只能開著 Revit 才驗得動。拆開之後
    /// 可以直接餵使用者手動量出來的地真值回歸。
    ///
    /// public 而非 internal，單純是為了讓外部測試組件能引用；add-in 本身沒有對外 API 契約。
    ///
    /// 演算法出處：pyRevit 版 AlignFraming.pushbutton/script.py，經 2026-07-22/23 兩輪
    /// 真模型校準（三組地真值：T 接大梁 -100、T 接且排除共線續接 -75、L 轉角 +75）。
    /// </summary>
    public static class FramingAlignGeometry
    {
        public const double MM_PER_FOOT = 304.8;

        /// <summary>橫向(Y/Z)相接容差：端點要落在目標 bbox 範圍內±此值才算「接在一起」。</summary>
        public const double CONTAINMENT_TOL_MM = 150.0;

        /// <summary>T/L 判別容差：垂直本樑方向要跨過端點兩側超過此值才算「貫穿」。</summary>
        public const double PERP_TOL_MM = 150.0;

        /// <summary>樑類目標的沿軸 footprint 上限；超過視為「共線端對端續接」而非垂直相接。柱豁免。</summary>
        public const double MAX_TARGET_FOOTPRINT_MM = 600.0;

        public struct CandidateResult
        {
            /// <summary>是否通過全部過濾、可作為對齊候選。</summary>
            public bool Accepted;

            /// <summary>端點沿外向到目標貼合面的帶號距離（英尺）。負=retract，正=extend。</summary>
            public double FaceAFeet;

            /// <summary>true = T 接（貫穿或接柱，retract 到近端面）；false = L 轉角（extend 到遠端面）。</summary>
            public bool IsThroughJoint;

            /// <summary>未通過時的原因，供診斷輸出。通過時為 null。</summary>
            public string RejectReason;
        }

        /// <summary>
        /// 從型號名稱解析真實斷面半寬（英尺）。
        ///
        /// 存在的理由：相接構材在繁忙節點會被 Revit 的 join 膨脹 bbox（實測真鋼 150 寬卻讀到
        /// bbox 433），直接拿 bbox 面定位會讓本樑停在真實鋼面外側而留縫。型號名沒有這個污染。
        /// 解析不出來（例如柱）才退回 bbox 半寬。
        /// </summary>
        public static double? ParseSectionHalfWidthFeet(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            Match m = Regex.Match(typeName, @"[Hh](\d+)[xX](\d+)");
            if (!m.Success) return null;
            double widthMm;
            if (!double.TryParse(m.Groups[2].Value, out widthMm)) return null;
            return (widthMm * 0.5) / MM_PER_FOOT;
        }

        /// <summary>
        /// 判斷單一候選目標能否作為對齊面，並算出貼合面的帶號距離。
        /// </summary>
        /// <param name="worldCorners">目標 bbox 的 8 個角點，已轉成世界座標（連結檔須先套 transform）。</param>
        /// <param name="endPoint">本樑端點，Z 已改用本樑實體 bbox 中點（見呼叫端說明）。</param>
        /// <param name="rayDir">外向單位向量：起點為 -v、終點為 +v（v 為 p0→p1）。</param>
        /// <param name="isColumn">目標是否為柱類別。</param>
        /// <param name="targetTypeName">目標型號名稱，用來解析真實半寬。</param>
        /// <param name="maxFaceFeet">沿軸窗口上限，超出視為不相接。</param>
        public static CandidateResult EvaluateCandidate(
            IList<XYZ> worldCorners,
            XYZ endPoint,
            XYZ rayDir,
            bool isColumn,
            string targetTypeName,
            double maxFaceFeet)
        {
            var result = new CandidateResult { Accepted = false };

            if (worldCorners == null || worldCorners.Count == 0)
            {
                result.RejectReason = "no corners";
                return result;
            }

            double cm = CONTAINMENT_TOL_MM / MM_PER_FOOT;

            // 橫向對齊：目標必須「跨在樑的軸線上」= 端點落在目標的「垂直本樑水平方向」與 Z 範圍內。
            // 沿軸刻意不設限 —— 轉角情形本樑較短、與對接構材之間留有間隙，設限會讓轉角案例找不到候選。
            // 橫向偏開的平行樑、或偏在一旁並不相接的柱，在此被排除。
            //
            // 橫向要在本樑自己的座標系裡量（perp 投影），不能拿世界 Y：三組校準樑剛好都沿 X，
            // 世界 Y 與 perp 重合所以當時看不出差別；換成沿 Y 的樑，比世界 Y 等於在比「沿軸」，
            // 側邊 2 公尺外的柱會因為 Y 範圍很寬而通過，再被柱豁免放行成對齊目標。
            XYZ perp = new XYZ(-rayDir.Y, rayDir.X, 0.0);
            var pprojs = worldCorners.Select(p => (p - endPoint).DotProduct(perp)).ToList();
            double minPerp = pprojs.Min(), maxPerp = pprojs.Max();
            double minZ = worldCorners.Min(p => p.Z), maxZ = worldCorners.Max(p => p.Z);

            if (!(minPerp - cm <= 0.0 && 0.0 <= maxPerp + cm &&
                  minZ - cm <= endPoint.Z && endPoint.Z <= maxZ + cm))
            {
                result.RejectReason = "endpoint outside target lateral/Z range";
                return result;
            }

            // 八角沿外向投影：a=近端(body 側)、b=遠端。用 bbox 全寬而不是打薄腹板面 ——
            // H 型鋼射線只會命中 web，量到 ±8~12mm 的無意義微小值。
            var projs = worldCorners.Select(p => (p - endPoint).DotProduct(rayDir)).ToList();
            double a = projs.Min();
            double b = projs.Max();

            // 沿軸窗口：近端面須在搜尋半徑內（可 retract 也可 extend）。這條擋掉的是
            // 「軸線外約 900mm、實際上並不相接」的柱。
            if (a < -maxFaceFeet || a > maxFaceFeet)
            {
                result.RejectReason = "near face outside axial window";
                return result;
            }

            // 樑類目標須「垂直穿越或轉角」= 沿軸 footprint 小。排除共線端對端續接的樑。
            // 柱不受此限：樑常直接接大柱面，柱的沿軸 footprint 本來就大。
            if (!isColumn && (b - a) > MAX_TARGET_FOOTPRINT_MM / MM_PER_FOOT)
            {
                result.RejectReason = "collinear continuation beam (axial footprint too large)";
                return result;
            }

            // T 接 vs L 轉角：看目標在「垂直本樑」的水平方向有沒有跨到端點兩側（同上面的 perp 投影）。
            //   兩側都有料 = 貫穿 = T 接 → retract 到近端面。
            //   只單側 = 目標也在此終止 = L 轉角 → extend 到遠端面。
            double tperp = PERP_TOL_MM / MM_PER_FOOT;
            bool through = minPerp < -tperp && maxPerp > tperp;

            // 用「沿軸中心 c + 真實半寬」定面，而不是直接取 bbox 的 a/b —— 見
            // ParseSectionHalfWidthFeet 的說明（join 膨脹 bbox 會讓貼合面外移而留縫）。
            //
            // 註：pyRevit 版的註解說 L 轉角要用「本樑半寬」，但它實際傳進來的 our_half_w
            // 在函式內從未被引用，跑出正確結果的是這裡的 mem_half（對接構材半寬）。
            // 校準案例中兩者剛好都是 75mm 所以地真值分不出差異；此處照實際跑通的程式碼走。
            double c = (a + b) * 0.5;
            double memHalf = ParseSectionHalfWidthFeet(targetTypeName) ?? ((b - a) * 0.5);

            bool isT = isColumn || through;

            result.Accepted = true;
            result.IsThroughJoint = isT;
            result.FaceAFeet = isT ? (c - memHalf) : (c + memHalf);
            return result;
        }
    }

    public partial class CommandExecutor
    {
        /// <summary>相接候選超過此數視為繁忙節點：選擇本身就模糊，寧可不動並標記需人工確認。</summary>
        private const int FRAMING_ALIGN_MAX_CANDIDATES = 4;

        private sealed class FramingAlignTarget
        {
            public Element Element;
            public Transform Transform;   // 連結檔才有；主模型為 null
            public bool IsLink;
        }

        /// <summary>
        /// 自動將結構構架（梁）端點的實體延伸切齊到相接的樑/柱面。
        ///
        /// 重要：只寫 START_EXTENSION / END_EXTENSION，**不動 LocationCurve**。
        /// 需求是「軸線（藍點）完全不動，只設三角形延伸」；本函式先前的版本直接改寫
        /// LocationCurve 端點（等於搬動樑本體），同時又把延伸參數歸零，方向性錯誤。
        ///
        /// 演算法與參數承接 pyRevit 版（見 FramingAlignGeometry 的出處說明）。
        /// </summary>
        private object AlignStructuralFraming(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var framingIdsArray = parameters["framingIds"] as JArray;
            List<IdType> framingIds = framingIdsArray != null
                ? framingIdsArray.Select(x => x.Value<IdType>()).ToList()
                : new List<IdType>();

            // 預設只對「結構構架 + 柱」。樓板/牆刻意不在預設內：樓板 bbox 中心離樑端可達十幾公尺，
            // 是早期版本把樑對到離譜位置的元兇。呼叫端仍可顯式指定。
            var targetCategoryTypesArray = parameters["targetCategoryTypes"] as JArray;
            List<string> targetCategories = targetCategoryTypesArray != null
                ? targetCategoryTypesArray.Select(x => x.Value<string>().ToLower()).ToList()
                : new List<string> { "framing", "column" };

            // 700mm 是實測校準值：容得下相接大梁半寬(~216)、轉角間隙、600 柱的半寬(300)，
            // 同時擋掉軸線外約 900mm 那根並不相接的柱。放大到 2000 會把它放進來。
            double maxDistanceMm = parameters["maxDistanceMm"] != null ? parameters["maxDistanceMm"].Value<double>() : 700.0;
            double maxFaceFeet = maxDistanceMm / FramingAlignGeometry.MM_PER_FOOT;

            bool disallowJoinAtEnds = parameters["disallowJoinAtEnds"] == null || parameters["disallowJoinAtEnds"].Value<bool>();

            IdType? viewId = parameters["viewId"]?.Value<IdType>();
            View targetView = viewId.HasValue
                ? doc.GetElement(viewId.Value.ToElementId()) as View
                : _uiApp.ActiveUIDocument.ActiveView;

            if (targetView == null)
            {
                return new { Success = false, Message = "無法取得有效的目標視圖" };
            }

            List<FamilyInstance> framingElements = CollectFramingElements(doc, targetView, framingIds);
            if (framingElements.Count == 0)
            {
                return new { Success = true, TotalFramingProcessed = 0, AlignedEndCount = 0, Message = "當前範圍未找到任何結構構架 (Structural Framing)" };
            }

            List<BuiltInCategory> catsToCollect = ResolveTargetCategories(targetCategories);
            List<FramingAlignTarget> targetItems = CollectAlignTargets(doc, catsToCollect, framingElements, maxFaceFeet);

            int alignedEndCount = 0;
            int manualCount = 0;
            int noCandidateCount = 0;
            var details = new List<object>();

            using (Transaction trans = TransactionHelper.Begin(doc, "Align Structural Framing Endpoints"))
            {
                trans.Start();

                // 第 1 段：先把所有待處理樑的延伸歸零再 Regenerate。
                // 這是 idempotent 的前提，也是先前反覆對不準的隱藏元兇 —— 上一輪殘留的延伸會
                // 撐大自身與相鄰構材的 bbox，讓這一輪讀到被污染的幾何。
                foreach (var framing in framingElements)
                {
                    foreach (var bip in new[] { BuiltInParameter.START_EXTENSION, BuiltInParameter.END_EXTENSION })
                    {
                        Parameter pr = framing.get_Parameter(bip);
                        if (pr != null && !pr.IsReadOnly)
                        {
                            try { pr.Set(0.0); } catch { }
                        }
                    }
                }
                doc.Regenerate();

                // 第 2 段：逐根計算並套用。
                foreach (var framing in framingElements)
                {
                    LocationCurve locCurve = framing.Location as LocationCurve;
                    Line beamLine = locCurve?.Curve as Line;
                    if (beamLine == null) continue;

                    XYZ p0 = beamLine.GetEndPoint(0);
                    XYZ p1 = beamLine.GetEndPoint(1);
                    XYZ v = (p1 - p0).Normalize();

                    // 端點 Z 改用本樑實體 bbox 中點：鋼樑的 LocationCurve 常對正實體「頂部」，
                    // 從中心線 Z 出發的水平比較會掠過相接樑的頂面而只對得到貫穿全高的柱。
                    // v 是水平向量，改 Z 不影響沿軸投影值。
                    BoundingBoxXYZ bbSelf = framing.get_BoundingBox(null);
                    if (bbSelf != null)
                    {
                        double rayZ = (bbSelf.Min.Z + bbSelf.Max.Z) * 0.5;
                        p0 = new XYZ(p0.X, p0.Y, rayZ);
                        p1 = new XYZ(p1.X, p1.Y, rayZ);
                    }

                    ApplyEndAlignment(framing, p0, -v, 0, targetItems, maxFaceFeet, disallowJoinAtEnds,
                        details, ref alignedEndCount, ref manualCount, ref noCandidateCount);
                    ApplyEndAlignment(framing, p1, v, 1, targetItems, maxFaceFeet, disallowJoinAtEnds,
                        details, ref alignedEndCount, ref manualCount, ref noCandidateCount);
                }

                if (trans.Commit() != TransactionStatus.Committed)
                    throw new Exception("交易未提交（Revit 回報錯誤，見 RevitMCP log），構架端點延伸未寫入。");
            }

            return new
            {
                Success = true,
                TotalFramingProcessed = framingElements.Count,
                AlignedEndCount = alignedEndCount,
                ManualReviewEndCount = manualCount,
                NoCandidateEndCount = noCandidateCount,
                Details = details,
                Message = $"完成 {framingElements.Count} 支結構構架處理：切齊 {alignedEndCount} 個端點，"
                        + $"{manualCount} 個端點因節點過於繁忙而跳過（需人工確認），"
                        + $"{noCandidateCount} 個端點找不到相接構材（延伸已歸零）。軸線位置未變動。"
            };
        }

        private List<FamilyInstance> CollectFramingElements(Document doc, View targetView, List<IdType> framingIds)
        {
            if (framingIds.Count > 0)
            {
                var picked = new List<FamilyInstance>();
                foreach (var fid in framingIds)
                {
                    if (doc.GetElement(fid.ToElementId()) is FamilyInstance fi &&
                        fi.Category?.Id.GetIdValue() == (IdType)(int)BuiltInCategory.OST_StructuralFraming)
                    {
                        picked.Add(fi);
                    }
                }
                return picked;
            }

            return new FilteredElementCollector(doc, targetView.Id)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();
        }

        private static List<BuiltInCategory> ResolveTargetCategories(List<string> targetCategories)
        {
            var cats = new List<BuiltInCategory>();
            if (targetCategories.Contains("framing")) cats.Add(BuiltInCategory.OST_StructuralFraming);
            if (targetCategories.Contains("column"))
            {
                cats.Add(BuiltInCategory.OST_StructuralColumns);
                cats.Add(BuiltInCategory.OST_Columns);
            }
            if (targetCategories.Contains("floor")) cats.Add(BuiltInCategory.OST_Floors);
            if (targetCategories.Contains("wall")) cats.Add(BuiltInCategory.OST_Walls);
            return cats;
        }

        /// <summary>
        /// 收集對齊目標，含連結檔 (RVT Links)。
        /// 連結檔不是可選項：支撐用的柱/大梁多半住在連結檔裡，只掃主模型會直接回 0 個候選。
        ///
        /// 主模型目標**不以視圖為範圍**。2026-09-17 真模型回歸：呼叫端帶 framingIds 時當前視圖是
        /// 13FL，樑在 3FL，視圖範圍的 collector 把主模型的相接樑全數漏掉，四根樑的終點都變成
        /// 「零候選」而延伸留 0；連結檔那一側本來就是整份文件掃，所以只有起點對得到。
        /// 改為整份文件加 bbox 預濾：以待處理樑 bbox 的聯集外擴 (maxFaceFeet + 1 m) 當 Outline，
        /// 全視圖模式下也不會退化成 O(n×m)。
        /// </summary>
        private List<FramingAlignTarget> CollectAlignTargets(
            Document doc, List<BuiltInCategory> cats, List<FamilyInstance> framingElements, double maxFaceFeet)
        {
            var items = new List<FramingAlignTarget>();

            ElementFilter bboxFilter = BuildFramingOutlineFilter(framingElements, maxFaceFeet);

            foreach (var cat in cats)
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(cat).WhereElementIsNotElementType();
                if (bboxFilter != null) collector = collector.WherePasses(bboxFilter);

                foreach (var e in collector.ToElements())
                {
                    items.Add(new FramingAlignTarget { Element = e, Transform = null, IsLink = false });
                }
            }

            var linkInstances = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_RvtLinks)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var li in linkInstances)
            {
                RevitLinkInstance link = li as RevitLinkInstance;
                if (link == null) continue;
                try
                {
                    Document linkDoc = link.GetLinkDocument();
                    if (linkDoc == null) continue;
                    Transform tf = link.GetTotalTransform();
                    foreach (var cat in cats)
                    {
                        foreach (var le in new FilteredElementCollector(linkDoc)
                            .OfCategory(cat).WhereElementIsNotElementType().ToElements())
                        {
                            items.Add(new FramingAlignTarget { Element = le, Transform = tf, IsLink = true });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[AlignFraming] 讀取連結檔失敗 (LinkInstance {link.Id.GetIdValue()}): {ex.Message}");
                }
            }

            return items;
        }

        private void ApplyEndAlignment(
            FamilyInstance framing,
            XYZ endPoint,
            XYZ outward,
            int endIndex,
            List<FramingAlignTarget> targetItems,
            double maxFaceFeet,
            bool disallowJoinAtEnds,
            List<object> details,
            ref int alignedEndCount,
            ref int manualCount,
            ref int noCandidateCount)
        {
            int candidateCount = 0;
            double? bestFaceA = null;
            bool bestIsT = false;
            Element bestTarget = null;
            bool bestIsLink = false;

            foreach (var item in targetItems)
            {
                Element target = item.Element;
                if (!item.IsLink && target.Id.GetIdValue() == framing.Id.GetIdValue()) continue;

                BoundingBoxXYZ bbox = target.get_BoundingBox(null);
                if (bbox == null) continue;

                List<XYZ> corners = BuildWorldCorners(bbox, item.Transform);

                string catName = target.Category?.Name ?? "?";
                bool isColumn = catName.Contains("柱") || catName.Contains("Column");

                var res = FramingAlignGeometry.EvaluateCandidate(
                    corners, endPoint, outward, isColumn, SafeElementName(target), maxFaceFeet);

                if (!res.Accepted) continue;

                candidateCount++;

                // 選「最靠端點」者 = 本樑沿外向最先碰到的相接面。
                if (bestFaceA == null || Math.Abs(res.FaceAFeet) < Math.Abs(bestFaceA.Value))
                {
                    bestFaceA = res.FaceAFeet;
                    bestIsT = res.IsThroughJoint;
                    bestTarget = target;
                    bestIsLink = item.IsLink;
                }
            }

            if (bestFaceA == null)
            {
                // 零候選也要回報：第 1 段已把延伸歸零，靜默 return 會讓「沒對到」跟「對到 0」看起來一樣。
                noCandidateCount++;
                details.Add(new
                {
                    FramingId = framing.Id.GetIdValue(),
                    End = endIndex,
                    Skipped = true,
                    Reason = "no connected candidate within search window",
                    CandidateCount = 0
                });
                return;
            }

            // 繁忙節點保險：候選過多時「哪個面才是對的」本身就沒有唯一答案，且這種節點的
            // bbox 最容易被 join 污染。寧可不動並回報，不要賭一個看起來合理的值。
            if (candidateCount > FRAMING_ALIGN_MAX_CANDIDATES)
            {
                manualCount++;
                details.Add(new
                {
                    FramingId = framing.Id.GetIdValue(),
                    End = endIndex,
                    Skipped = true,
                    Reason = $"busy joint: {candidateCount} connected candidates (> {FRAMING_ALIGN_MAX_CANDIDATES})",
                    CandidateCount = candidateCount
                });
                return;
            }

            try
            {
                if (disallowJoinAtEnds)
                {
                    try { StructuralFramingUtils.DisallowJoinAtEnd(framing, endIndex); } catch { }
                }

                BuiltInParameter bip = endIndex == 0 ? BuiltInParameter.START_EXTENSION : BuiltInParameter.END_EXTENSION;
                Parameter p = framing.get_Parameter(bip)
                    ?? framing.LookupParameter(endIndex == 0 ? "起點延伸" : "終點延伸");

                if (p == null || p.IsReadOnly) return;

                p.Set(bestFaceA.Value);
                alignedEndCount++;

                details.Add(new
                {
                    FramingId = framing.Id.GetIdValue(),
                    End = endIndex,
                    Skipped = false,
                    TargetId = bestTarget?.Id.GetIdValue(),
                    TargetCategory = bestTarget?.Category?.Name ?? "Target",
                    TargetIsLink = bestIsLink,
                    JointType = bestIsT ? "T" : "L",
                    ExtensionMm = Math.Round(bestFaceA.Value * FramingAlignGeometry.MM_PER_FOOT, 1),
                    CandidateCount = candidateCount
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"[AlignFraming] 設定延伸失敗 (Framing {framing.Id.GetIdValue()} End {endIndex}): {ex.Message}");
            }
        }

        /// <summary>
        /// 待處理樑 bbox 聯集外擴後的 BoundingBoxIntersectsFilter；沒有任何 bbox 時回 null（不過濾）。
        /// 外擴量 = 沿軸搜尋窗口 + 1 m：橫向 containment 容差只有 ±150 mm，1 m 已經很寬鬆，
        /// 寧可多撈幾根再讓 EvaluateCandidate 篩，也不要在這層漏掉。
        /// </summary>
        private static ElementFilter BuildFramingOutlineFilter(List<FamilyInstance> framingElements, double maxFaceFeet)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool any = false;

            foreach (var framing in framingElements)
            {
                BoundingBoxXYZ bb = framing.get_BoundingBox(null);
                if (bb == null) continue;
                any = true;
                minX = Math.Min(minX, bb.Min.X); minY = Math.Min(minY, bb.Min.Y); minZ = Math.Min(minZ, bb.Min.Z);
                maxX = Math.Max(maxX, bb.Max.X); maxY = Math.Max(maxY, bb.Max.Y); maxZ = Math.Max(maxZ, bb.Max.Z);
            }

            if (!any) return null;

            double pad = maxFaceFeet + 1000.0 / FramingAlignGeometry.MM_PER_FOOT;
            var outline = new Outline(
                new XYZ(minX - pad, minY - pad, minZ - pad),
                new XYZ(maxX + pad, maxY + pad, maxZ + pad));
            return new BoundingBoxIntersectsFilter(outline);
        }

        /// <summary>bbox 八角轉世界座標；連結檔套上 link transform。</summary>
        private static List<XYZ> BuildWorldCorners(BoundingBoxXYZ bbox, Transform transform)
        {
            var corners = new List<XYZ>(8);
            foreach (double cx in new[] { bbox.Min.X, bbox.Max.X })
                foreach (double cy in new[] { bbox.Min.Y, bbox.Max.Y })
                    foreach (double cz in new[] { bbox.Min.Z, bbox.Max.Z })
                    {
                        XYZ p = new XYZ(cx, cy, cz);
                        if (transform != null && !transform.IsIdentity) p = transform.OfPoint(p);
                        corners.Add(p);
                    }
            return corners;
        }

        private static string SafeElementName(Element e)
        {
            try { return e.Name; } catch { return null; }
        }
    }
}
