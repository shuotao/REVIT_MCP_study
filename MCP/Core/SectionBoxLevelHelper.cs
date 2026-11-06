using System;
using Autodesk.Revit.DB;

namespace RevitMCP.Core
{
    /// <summary>
    /// 共用的「Section Box 依樓層／結構延伸」幾何邏輯。
    ///
    /// 抽出理由（規格：Camera Manager Change 2）：既有 `set_3d_section_box`
    /// （<c>CommandExecutor.ViewCropBox.cs</c>）已經有一套驗證過的「往下找支撐樑、往上找
    /// 最近上層樓板」邏輯，相機管理面板的等角 Key Plan 視圖需要同一套規則（同一個
    /// `includeSupportingBeams` / `upperStructureMode=include_upper_structure_without_slab`
    /// 概念），但呼叫端的「target」不是一個既有元素的 BoundingBox（相機沒有元素本體），
    /// 而是「相機視野扇形＋遠裁剪」算出來的一個 XY 範圍。把三段迴圈抽成這裡的靜態方法、
    /// 用明確的 targetMinX/Y/Z 等參數取代原本內嵌的 `element`／`targetMinX` 區域變數，
    /// 兩邊呼叫端都可以重用，且 `CommandExecutor.ViewCropBox.cs` 那邊的行為（JSON 輸出、
    /// 篩選條件）完全不變——只是把迴圈本體搬過來，呼叫時傳的參數與原本區域變數逐一對應。
    ///
    /// 這裡只做純幾何／collector 查詢，不開 Transaction、不寫模型。
    /// </summary>
    internal static class SectionBoxLevelHelper
    {
        /// <summary>
        /// 取得元素在模型座標中的軸向 BoundingBox（套用 BoundingBox 自身 Transform 後重算 8 角點）。
        /// 從 <c>CommandExecutor.ViewCropBox.cs</c> 原樣搬過來，行為完全不變。
        /// </summary>
        internal static bool TryGetWorldBounds(Element element,
            out double minX, out double minY, out double minZ,
            out double maxX, out double maxY, out double maxZ)
        {
            minX = minY = minZ = double.PositiveInfinity;
            maxX = maxY = maxZ = double.NegativeInfinity;

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null) return false;

            Transform transform = box.Transform ?? Transform.Identity;
            for (int ix = 0; ix <= 1; ix++)
            for (int iy = 0; iy <= 1; iy++)
            for (int iz = 0; iz <= 1; iz++)
            {
                XYZ p = transform.OfPoint(new XYZ(
                    ix == 0 ? box.Min.X : box.Max.X,
                    iy == 0 ? box.Min.Y : box.Max.Y,
                    iz == 0 ? box.Min.Z : box.Max.Z));
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
                maxZ = Math.Max(maxZ, p.Z);
            }
            return true;
        }

        /// <summary>
        /// 掃描 <see cref="BuiltInCategory.OST_StructuralFraming"/>，把「XY 與目標範圍重疊（含容差）
        /// 且樑頂大約落在 targetMinZ（容差內，代表撐著目標底面）」的樑，聯集進 min/max（maxZ 刻意
        /// 不動——比照原邏輯「Deliberately do not raise maxZ above the target element top」）。
        /// targetMinX/Y/MaxX/Y 與 targetMinZ 必須是呼叫端在聯集開始前就凍結的值（不可傳同一個之後
        /// 會被 ref 修改的變數），否則會遞迴把下一跨的樑也拉進來——原始程式碼的既有教訓，這裡沿用。
        /// </summary>
        internal static int IncludeSupportingBeams(Document doc,
            double targetMinX, double targetMinY, double targetMaxX, double targetMaxY, double targetMinZ,
            double toleranceFeet,
            ref double minX, ref double minY, ref double minZ, ref double maxX, ref double maxY)
        {
            int count = 0;
            var framing = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType();

            foreach (Element beam in framing)
            {
                double beamMinX, beamMinY, beamMinZ, beamMaxX, beamMaxY, beamMaxZ;
                if (!TryGetWorldBounds(beam, out beamMinX, out beamMinY, out beamMinZ, out beamMaxX, out beamMaxY, out beamMaxZ))
                    continue;

                bool overlapsTargetXY =
                    beamMaxX >= targetMinX - toleranceFeet && beamMinX <= targetMaxX + toleranceFeet &&
                    beamMaxY >= targetMinY - toleranceFeet && beamMinY <= targetMaxY + toleranceFeet;
                bool supportsTargetBase = Math.Abs(beamMaxZ - targetMinZ) <= toleranceFeet;
                if (!overlapsTargetXY || !supportsTargetBase) continue;

                minX = Math.Min(minX, beamMinX);
                minY = Math.Min(minY, beamMinY);
                minZ = Math.Min(minZ, beamMinZ);
                maxX = Math.Max(maxX, beamMaxX);
                maxY = Math.Max(maxY, beamMaxY);
                count++;
            }
            return count;
        }

        /// <summary>
        /// 掃描 <see cref="BuiltInCategory.OST_Floors"/>，找出「XY 與目標範圍重疊、底面高於目標底面
        /// （容差外，排除目標自己站的那片樓板）、且頂面達到目標頂面高度（容差內，排除搭不到全高的
        /// 平台）」的樓板裡，底面最低的那一片——即「最靠近目標正上方的樓板」。找不到回傳 null。
        /// </summary>
        internal static Element FindNearestUpperSlab(Document doc,
            double targetMinX, double targetMinY, double targetMaxX, double targetMaxY,
            double targetMinZ, double targetMaxZ, double toleranceFeet,
            out double slabBottomZ, out double slabTopZ, out int candidateCount)
        {
            Element upperSlab = null;
            slabBottomZ = double.PositiveInfinity;
            slabTopZ = double.NaN;
            candidateCount = 0;

            var floors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType();

            foreach (Element floor in floors)
            {
                double fMinX, fMinY, fMinZ, fMaxX, fMaxY, fMaxZ;
                if (!TryGetWorldBounds(floor, out fMinX, out fMinY, out fMinZ, out fMaxX, out fMaxY, out fMaxZ))
                    continue;

                bool overlapsTargetXY =
                    fMaxX > targetMinX && fMinX < targetMaxX &&
                    fMaxY > targetMinY && fMinY < targetMaxY;
                bool isAboveTargetBase = fMinZ > targetMinZ + toleranceFeet;
                bool reachesTargetTop = fMaxZ >= targetMaxZ - toleranceFeet;
                if (!overlapsTargetXY || !isAboveTargetBase || !reachesTargetTop) continue;

                candidateCount++;
                if (fMinZ < slabBottomZ)
                {
                    slabBottomZ = fMinZ;
                    slabTopZ = fMaxZ;
                    upperSlab = floor;
                }
            }
            return upperSlab;
        }

        // ══════════════════════════════════════════════════════════
        // 2026-09-24 新增（相機管理「等角 Key Plan」專用）：以下三個方法是「新增」，不修改上面
        // 被 set_3d_section_box 共用的既有方法，該工具的行為與 JSON 輸出完全不受影響。
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 同 <see cref="FindNearestUpperSlab"/> 的判準，但候選同時包含 <see cref="BuiltInCategory.OST_Floors"/>
        /// 與 <see cref="BuiltInCategory.OST_Roofs"/>（正上方的屋頂等同上層樓板）；天花板刻意不納入
        /// （天花板屬於本層）。條件：XY 與目標範圍重疊、底面高於 baseZ（容差外）、頂面達到
        /// reachZ（通常＝相機眼睛高度，容差內）。回傳底面最低者；找不到回傳 null。
        /// </summary>
        internal static Element FindNearestUpperSlabOrRoof(Document doc,
            double targetMinX, double targetMinY, double targetMaxX, double targetMaxY,
            double baseZ, double reachZ, double toleranceFeet, out double slabBottomZ)
        {
            Element best = null;
            slabBottomZ = double.PositiveInfinity;
            foreach (BuiltInCategory bic in new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs })
            {
                var collector = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType();
                foreach (Element e in collector)
                {
                    if (!TryGetWorldBounds(e, out double mnX, out double mnY, out double mnZ, out double mxX, out double mxY, out double mxZ))
                        continue;
                    bool overlapsXY = mxX > targetMinX && mnX < targetMaxX && mxY > targetMinY && mnY < targetMaxY;
                    if (!overlapsXY || !(mnZ > baseZ + toleranceFeet) || !(mxZ >= reachZ - toleranceFeet)) continue;
                    if (mnZ < slabBottomZ) { slabBottomZ = mnZ; best = e; }
                }
            }
            return best;
        }

        /// <summary>
        /// 2026-09-25 新增（相機 Key Plan：挑高房間／夾層）：以「房間頂」為基準找上層樓板／屋頂。
        /// 候選：XY 與目標範圍重疊的 Floors／Roofs，且（底面 ≥ roomTopZ − topToleranceFeet，或頂面 ≥
        /// roomTopZ − topToleranceFeet＝板身跨過房間頂面）。房間內、整片低於房間頂的夾層樓板
        /// （頂面 &lt; 房間頂 − 容差）不算。回傳底面最低者；找不到回傳 null。不修改任何既有方法。
        /// </summary>
        internal static Element FindUpperSlabOrRoofAboveRoomTop(Document doc,
            double targetMinX, double targetMinY, double targetMaxX, double targetMaxY,
            double roomTopZ, double topToleranceFeet, out double slabBottomZ, out int skippedMezzanines)
        {
            Element best = null;
            slabBottomZ = double.PositiveInfinity;
            skippedMezzanines = 0;
            double limit = roomTopZ - topToleranceFeet;
            foreach (BuiltInCategory bic in new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs })
            {
                var collector = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType();
                foreach (Element e in collector)
                {
                    if (!TryGetWorldBounds(e, out double mnX, out double mnY, out double mnZ, out double mxX, out double mxY, out double mxZ))
                        continue;
                    bool overlapsXY = mxX > targetMinX && mnX < targetMaxX && mxY > targetMinY && mnY < targetMaxY;
                    if (!overlapsXY) continue;
                    bool counts = mnZ >= limit || mxZ >= limit;
                    if (!counts)
                    {
                        // 低於房間頂的板：房間樓層以上者視為夾層（只計數供 log）
                        skippedMezzanines++;
                        continue;
                    }
                    if (mnZ < slabBottomZ) { slabBottomZ = mnZ; best = e; }
                }
            }
            return best;
        }

        /// <summary>
        /// 上層樑（從上一層樓板往下吊、會遮住本層的樑）中底面最低者：XY 與目標範圍重疊、
        /// 底面高於 baseZ（容差外，排除支撐本層樓板的樑）、頂面高於 reachZ（眼睛高度）。
        /// 找不到回傳 false。
        /// </summary>
        internal static bool FindLowestUpperBeamBottom(Document doc,
            double targetMinX, double targetMinY, double targetMaxX, double targetMaxY,
            double baseZ, double reachZ, double toleranceFeet, out double beamBottomZ, out int count)
        {
            beamBottomZ = double.PositiveInfinity;
            count = 0;
            var framing = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType();
            foreach (Element beam in framing)
            {
                if (!TryGetWorldBounds(beam, out double mnX, out double mnY, out double mnZ, out double mxX, out double mxY, out double mxZ))
                    continue;
                bool overlapsXY = mxX > targetMinX && mnX < targetMaxX && mxY > targetMinY && mnY < targetMaxY;
                if (!overlapsXY || !(mnZ > baseZ + toleranceFeet) || !(mxZ > reachZ)) continue;
                count++;
                if (mnZ < beamBottomZ) beamBottomZ = mnZ;
            }
            return count > 0;
        }

        /// <summary>
        /// 「代表性樓板厚度」：以 LevelId＝指定樓層的所有樓板，讀 FLOOR_ATTR_THICKNESS_PARAM
        /// （讀不到改用 BoundingBox 高度），取中位數。沒有任何樓板時回傳 null。
        /// </summary>
        internal static double? EstimateTypicalSlabThickness(Document doc, ElementId levelId)
        {
            if (levelId == null || levelId == ElementId.InvalidElementId) return null;
            var values = new System.Collections.Generic.List<double>();
            var floors = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType();
            foreach (Element f in floors)
            {
                if (f.LevelId != levelId) continue;
                double t = 0;
                try
                {
                    Parameter p = f.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                    if (p != null && p.HasValue) t = p.AsDouble();
                }
                catch { t = 0; }
                if (!(t > 1e-6) && TryGetWorldBounds(f, out _, out _, out double mnZ, out _, out _, out double mxZ))
                    t = mxZ - mnZ;
                if (t > 1e-6) values.Add(t);
            }
            if (values.Count == 0) return null;
            values.Sort();
            return values[values.Count / 2];
        }

        /// <summary>
        /// 掃描 <see cref="BuiltInCategory.OST_StructuralFraming"/>，計算「XY 與目標範圍重疊（含
        /// paddingXY 容差）且垫在 sectionMaxZ 之下、又高於目標頂面（容差外）」的樑數量——僅供
        /// 診斷／回報用途，不影響任何範圍計算。
        /// </summary>
        internal static int CountUpperStructureBeams(Document doc,
            double targetMinX, double targetMinY, double targetMaxX, double targetMaxY, double targetMaxZ,
            double paddingXYFeet, double sectionMaxZ, double toleranceFeet)
        {
            int count = 0;
            var upperFraming = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType();
            foreach (Element beam in upperFraming)
            {
                double bMinX, bMinY, bMinZ, bMaxX, bMaxY, bMaxZ;
                if (!TryGetWorldBounds(beam, out bMinX, out bMinY, out bMinZ, out bMaxX, out bMaxY, out bMaxZ))
                    continue;
                bool overlapsXY =
                    bMaxX >= targetMinX - paddingXYFeet && bMinX <= targetMaxX + paddingXYFeet &&
                    bMaxY >= targetMinY - paddingXYFeet && bMinY <= targetMaxY + paddingXYFeet;
                bool hangsIntoBox = bMinZ < sectionMaxZ && bMaxZ > targetMaxZ + toleranceFeet;
                if (overlapsXY && hangsIntoBox) count++;
            }
            return count;
        }

        /// <summary>
        /// 2026-09-25（相機管理「室外」規則，新增方法，不影響 set_3d_section_box）：XY 範圍內（Z 不限，
        /// BoundingBoxIntersectsFilter 以 ±zRangeFeet 的超高 Outline 篩）主要模型類別（牆、樓板、屋頂、結構構架／柱、
        /// 柱、一般模型、樓梯、欄杆、帷幕嵌板、地形／地形實體、基地、植栽）BoundingBox 頂面的最大 Z。
        /// 地形實體（OST_Toposolid）只有 R24+ 有，用名稱動態解析，舊版略過。找不到任何元素回傳 false。
        /// </summary>
        internal static bool FindMaxModelTopInXY(Document doc,
            double minX, double minY, double maxX, double maxY, double zRangeFeet,
            out double maxTopZ, out int count, out string topElementInfo)
        {
            maxTopZ = double.NegativeInfinity;
            count = 0;
            topElementInfo = null;
            var cats = new System.Collections.Generic.List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_StairsRailing,
                BuiltInCategory.OST_CurtainWallPanels, BuiltInCategory.OST_Topography, BuiltInCategory.OST_Site,
                BuiltInCategory.OST_Planting
            };
            if (Enum.TryParse("OST_Toposolid", out BuiltInCategory topo)) cats.Add(topo);

            Outline outline = new Outline(new XYZ(minX, minY, -zRangeFeet), new XYZ(maxX, maxY, zRangeFeet));
            var collector = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(cats))
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .WhereElementIsNotElementType();
            foreach (Element e in collector)
            {
                if (!TryGetWorldBounds(e, out double x0, out double y0, out double z0, out double x1, out double y1, out double z1))
                    continue;
                if (double.IsNaN(z1) || double.IsInfinity(z1)) continue;
                count++;
                if (z1 > maxTopZ)
                {
                    maxTopZ = z1;
                    topElementInfo = e.Id.GetIdValue() + " (" + (e.Category?.Name ?? "?") + ")";
                }
            }
            return count > 0 && !double.IsInfinity(maxTopZ);
        }
    }
}
