using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

#nullable disable

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    // =====================================================================================
    // 重建的私有 helper —— salvage/reclaim-contributor-impl 補洞
    // -------------------------------------------------------------------------------------
    // 背景:貢獻者(Jacky820507, PR #81/#82)的 C# 實作只以本機檔案交付,fork 上僅推 domain
    // 文件;salvage commit 604cafe 帶入了 PartitionTakeoff.cs / IfcStructuralSync.cs,卻未
    // 帶入它們依賴的這些私有 helper。這些符號在任何 git ref 皆不存在(已窮舉查證)。
    //
    // 以下為「照呼叫點合約 + repo 既有同族 helper + 貢獻者 domain 規格」重建,非憑空捏造:
    //   - TrySetStringParameter / TrySetDoubleParameterMm:比照同檔既有
    //     TrySetDoubleParameterFeet / TrySetAllDoubleParametersMm。
    //   - DismissWarningsPreprocessor:比照 DetailCopy.cs 既有 SuppressWarningsPreprocessor。
    //   - GetDetector3DView:比照 StairCompliance.cs 取非樣板 View3D 的樣式。
    //
    // ⚠️ 待 Jacky 對其原始碼複核。幾何射線類(FloorHitInfo / CollectFloorBottomHitsAtPoint /
    //    CollectGeometryFloorBottomHitsAtPoint)另置於本檔下段,務必在 Revit 對真實模型實跑驗證。
    // =====================================================================================

    /// <summary>
    /// 於 Transaction 中直接吃掉(刪除)所有 warning,不阻塞 UI thread。
    /// 對應貢獻者原碼中的 new DismissWarningsPreprocessor()(無參數建構式)。
    /// 語意等同 DetailCopy.cs 的 SuppressWarningsPreprocessor,但不回收訊息清單。
    /// </summary>
    internal class DismissWarningsPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (var failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(failure);
            }
            return FailureProcessingResult.Continue;
        }
    }

    public partial class CommandExecutor
    {
        // ---- PartitionTakeoff (#81) 依賴 ----------------------------------------------

        /// <summary>依 LevelId 取樓層名稱;查無回傳 null。</summary>
        private static string GetLevelName(Document doc, ElementId levelId)
        {
            if (levelId == null || levelId == ElementId.InvalidElementId) return null;
            Level level = doc.GetElement(levelId) as Level;
            return level?.Name;
        }

        /// <summary>
        /// 取一個可供 ReferenceIntersector 射線偵測用的非樣板 3D 視圖。
        /// 呼叫端已包 try/catch,查無時丟例外由上層降級為警告即可(不需 Transaction)。
        /// </summary>
        private View3D GetDetector3DView(Document doc)
        {
            View3D view = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => v != null && !v.IsTemplate);

            if (view == null)
                throw new Exception("模型中找不到可用的非樣板 3D 視圖,無法執行房間至樓板底射線偵測");
            return view;
        }

        // ---- IfcStructuralSync (#82) 依賴 ---------------------------------------------

        /// <summary>
        /// 依序嘗試多個參數名,設定第一個命中的 String 參數後即返回。
        /// 比照同檔既有 TrySetDoubleParameterFeet 的 first-match-return 語意。
        /// </summary>
        private static void TrySetStringParameter(Element element, string value, params string[] names)
        {
            if (element == null || string.IsNullOrEmpty(value)) return;
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String) continue;
                parameter.Set(value);
                return;
            }
        }

        /// <summary>
        /// 依序嘗試多個參數名,把 mm 值轉為內部 feet 後設定第一個命中的 Double 參數。
        /// 比照同檔既有 TrySetAllDoubleParametersMm 的 mm→feet 換算(IfcSyncMmToFeet)。
        /// </summary>
        private static void TrySetDoubleParameterMm(Element element, double? valueMm, params string[] names)
        {
            if (element == null || !valueMm.HasValue) return;
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double) continue;
                parameter.Set(valueMm.Value * IfcSyncMmToFeet);
                return;
            }
        }

        /// <summary>
        /// 盡力設定柱頂固定旗標(整數參數)。柱頂實際對齊已由 TopLevel + TopOffset 完成,
        /// 此為附帶旗標;查無對應參數即略過。
        /// ⚠️ 原作者私有實作細節不明,待 Jacky 複核確認目標參數。
        /// </summary>
        private static void TrySetColumnTopAttachment(FamilyInstance column, int value)
        {
            if (column == null) return;
            string[] names = { "頂部附著", "柱頂固定", "頂部固定", "Top Attachment", "Column Top Attachment" };
            foreach (string name in names)
            {
                Parameter parameter = column.LookupParameter(name);
                if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer) continue;
                parameter.Set(value);
                return;
            }
        }

        // ---- 幾何射線類(PartitionTakeoff #81 + IfcStructuralSync #82 共用)-------------
        // ⚠️ 依「呼叫點合約 + 貢獻者 domain 規格(tall-partition-index-workflow.md 第 73 行:
        //    自房間底部向上打射線到樓板底)+ repo 既有 ReferenceIntersector 樣式」重建。
        //    必須在 Revit 對真實模型實跑驗證,並請 Jacky 對其原始碼複核。
        //    全程僅用 ElementId(無 .IntegerValue),R22–R26 皆安全。

        /// <summary>射線/幾何偵測到的樓板底命中資料。</summary>
        private class FloorHitInfo
        {
            public bool HasHit { get; set; }
            public double BottomZFeet { get; set; }
            public IdType FloorId { get; set; }
            public string FloorName { get; set; }
            public string LevelName { get; set; }
            public string Message { get; set; }
        }

        /// <summary>
        /// 從 sample 點(位於 baseZFeet 高度)沿 +Z 向上打射線,收集 maxSearchDistanceMm 範圍內
        /// 打中的樓板面,回傳每一命中的樓板底 Z。呼叫端再自行 filter/order 取最近者。
        /// 用 ReferenceIntersector(需一個非樣板 3D 視圖)。
        /// </summary>
        private List<FloorHitInfo> CollectFloorBottomHitsAtPoint(
            Document doc,
            View3D detectorView,
            XYZ sample,
            double baseZFeet,
            double maxSearchDistanceMm,
            ICollection<ElementId> excludeFloorIds)
        {
            var results = new List<FloorHitInfo>();
            if (detectorView == null || sample == null) return results;

            var intersector = new ReferenceIntersector(
                new ElementCategoryFilter(BuiltInCategory.OST_Floors),
                FindReferenceTarget.Face,
                detectorView)
            {
                FindReferencesInRevitLinks = false
            };

            XYZ origin = new XYZ(sample.X, sample.Y, baseZFeet + (1.0 / 304.8)); // 抬 1mm 避免自碰
            IList<ReferenceWithContext> hits = intersector.Find(origin, XYZ.BasisZ);
            if (hits == null) return results;

            var seen = new HashSet<string>();
            foreach (ReferenceWithContext rc in hits)
            {
                if (rc == null) continue;
                if (rc.Proximity * 304.8 > maxSearchDistanceMm) continue;

                Reference reference = rc.GetReference();
                if (reference == null) continue;
                ElementId floorId = reference.ElementId;
                if (floorId == null || floorId == ElementId.InvalidElementId) continue;
                if (excludeFloorIds != null && excludeFloorIds.Contains(floorId)) continue;

                double hitZ = reference.GlobalPoint != null ? reference.GlobalPoint.Z : origin.Z + rc.Proximity;
                if (!seen.Add(floorId.ToString() + ":" + Math.Round(hitZ, 4).ToString())) continue;

                Element floor = doc.GetElement(floorId);
                results.Add(new FloorHitInfo
                {
                    HasHit = true,
                    BottomZFeet = hitZ,
                    FloorId = floorId.GetIdValue(),
                    FloorName = floor != null ? floor.Name : null,
                    LevelName = floor != null ? GetLevelName(doc, floor.LevelId) : null,
                    Message = "ray-face-hit"
                });
            }
            return results;
        }

        /// <summary>
        /// 幾何版:不需 3D 視圖。於 (sample.X, sample.Y) 建一條自 baseZFeet 向上、長度
        /// maxSearchDistanceMm 的垂直線,對每片樓板 Solid 做 IntersectWithCurve,取進入點
        /// (較低端 Z)為該樓板底。用於柱頂對齊(IfcStructuralSync)。
        /// </summary>
        private List<FloorHitInfo> CollectGeometryFloorBottomHitsAtPoint(
            Document doc,
            XYZ sample,
            double baseZFeet,
            double maxSearchDistanceMm,
            ICollection<ElementId> excludeFloorIds)
        {
            var results = new List<FloorHitInfo>();
            if (sample == null) return results;

            double maxDistFeet = Math.Max(maxSearchDistanceMm / 304.8, 1.0 / 304.8);
            Line vertical = Line.CreateBound(
                new XYZ(sample.X, sample.Y, baseZFeet),
                new XYZ(sample.X, sample.Y, baseZFeet + maxDistFeet));

            var geomOptions = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Medium
            };
            var sciOptions = new SolidCurveIntersectionOptions();

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType();

            foreach (Element floor in collector)
            {
                if (excludeFloorIds != null && excludeFloorIds.Contains(floor.Id)) continue;

                GeometryElement geo = floor.get_Geometry(geomOptions);
                if (geo == null) continue;

                double? bottomZ = null;
                foreach (Solid solid in ExtractSolids(geo))
                {
                    SolidCurveIntersection sci;
                    try { sci = solid.IntersectWithCurve(vertical, sciOptions); }
                    catch { continue; }
                    if (sci == null) continue;

                    for (int i = 0; i < sci.SegmentCount; i++)
                    {
                        Curve seg = sci.GetCurveSegment(i);
                        if (seg == null) continue;
                        double z = Math.Min(seg.GetEndPoint(0).Z, seg.GetEndPoint(1).Z);
                        if (!bottomZ.HasValue || z < bottomZ.Value) bottomZ = z;
                    }
                }

                if (bottomZ.HasValue)
                {
                    results.Add(new FloorHitInfo
                    {
                        HasHit = true,
                        BottomZFeet = bottomZ.Value,
                        FloorId = floor.Id.GetIdValue(),
                        FloorName = floor.Name,
                        LevelName = GetLevelName(doc, floor.LevelId),
                        Message = "geometry-solid-hit"
                    });
                }
            }
            return results;
        }

        /// <summary>攤平 GeometryElement,取出直接 Solid 及 GeometryInstance 一層內的 Solid。</summary>
        private static IEnumerable<Solid> ExtractSolids(GeometryElement geo)
        {
            foreach (GeometryObject go in geo)
            {
                if (go is Solid solid)
                {
                    if (solid.Volume > 0) yield return solid;
                }
                else if (go is GeometryInstance gi)
                {
                    GeometryElement inner = gi.GetInstanceGeometry();
                    if (inner == null) continue;
                    foreach (GeometryObject innerGo in inner)
                    {
                        if (innerGo is Solid innerSolid && innerSolid.Volume > 0)
                            yield return innerSolid;
                    }
                }
            }
        }
    }
}
