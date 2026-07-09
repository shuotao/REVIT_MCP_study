using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 依牆面開孔位置建立平行剖面視圖
    /// 套管品類：管附件（圓形套管）、風管附件（矩形開口）
    /// 支援：連結模型牆、跨連結模型套管搜尋、長牆分割、自動裁剪、方向判定
    /// </summary>
    public partial class CommandExecutor
    {
        #region 牆面平行剖面

        private const double _MM2FT = 1.0 / 304.8;

        private object CreateParallelSectionView(JObject parameters)
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return new { Success = false, Error = "無法取得文件" };

            // ── 參數 ───────────────────────────────────────────────────────
            IdType wallIdVal     = parameters["wallId"]?.Value<IdType>()
                ?? throw new ArgumentException("缺少 wallId");
            IdType wallLinkId    = parameters["wallLinkId"]?.Value<IdType>() ?? 0;
            double offsetMm      = parameters["offset"]?.Value<double>() ?? 1.0;
            bool   splitLong     = parameters["splitLongWalls"]?.Value<bool>() ?? true;
            string namePrefix    = parameters["viewNamePrefix"]?.Value<string>() ?? "牆面套管剖面";
            bool   autoCrop      = parameters["autoCrop"]?.Value<bool>() ?? true;
            string dirLogic      = parameters["directionLogic"]?.Value<string>() ?? "auto";
            int    scale         = parameters["scale"]?.Value<int>() ?? 50;
            double marginHorizMm = parameters["marginHorizontal"]?.Value<double>() ?? 500.0;
            double marginVertMm  = parameters["marginVertical"]?.Value<double>() ?? 200.0;

            // ── 取得牆（支援連結模型）────────────────────────────────────
            Wall   wall    = null;
            Document wallDoc = doc;
            Transform wallTr = Transform.Identity;

            if (wallLinkId != 0)
            {
                var linkInst = doc.GetElement(new ElementId(wallLinkId)) as RevitLinkInstance;
                if (linkInst == null)
                    return new { Success = false, Error = $"找不到連結模型 ID: {wallLinkId}" };
                wallDoc = linkInst.GetLinkDocument();
                wallTr  = linkInst.GetTotalTransform();
            }
            wall = wallDoc?.GetElement(new ElementId(wallIdVal)) as Wall;
            if (wall == null)
                return new { Success = false, Error = $"找不到牆 ID: {wallIdVal}（連結: {wallLinkId}）" };

            var locCurve = wall.Location as LocationCurve;
            if (locCurve == null)
                return new { Success = false, Error = "牆沒有位置曲線" };

            // ── 將牆曲線轉換至世界座標 ────────────────────────────────────
            Curve  wallCurveLocal = locCurve.Curve;
            XYZ    localStart = wallCurveLocal.GetEndPoint(0);
            XYZ    localEnd   = wallCurveLocal.GetEndPoint(1);
            XYZ    worldStart = wallTr.OfPoint(localStart);
            XYZ    worldEnd   = wallTr.OfPoint(localEnd);
            XYZ    wallDir    = (worldEnd - worldStart).Normalize();
            XYZ    wallNormal = wallDir.CrossProduct(XYZ.BasisZ).Normalize();
            double wallLength = worldStart.DistanceTo(worldEnd);
            double wallH      = GetWallHeight(wall);
            double wallW      = wall.Width; // ft

            // ── 收集所有連結模型中的套管和矩形開口 ───────────────────────
            var allLinks = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(li => li.GetLinkDocument() != null)
                .ToList();

            var openings = CollectWallOpeningsFromLinks(allLinks, worldStart, worldEnd, wallDir, wallW);

            // ── 計算剖面群組 ──────────────────────────────────────────────
            var groups = splitLong && openings.Count > 0
                ? SplitOpeningsIntoGroups(openings)
                : new List<List<PsOpeningInfo>> { openings.Count > 0 ? openings : null };

            // ── 取得剖面 ViewFamilyType ───────────────────────────────────
            var sectionVFT = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Section);
            if (sectionVFT == null)
                return new { Success = false, Error = "找不到剖面視圖類型" };

            // ── 觀察方向 ──────────────────────────────────────────────────
            XYZ viewDir = DetermineViewDir(doc, wall, wallTr, wallNormal, dirLogic);

            // ── 建立剖面視圖 ──────────────────────────────────────────────
            var createdViews = new List<object>();
            int groupIdx = 0;

            using (var tx = new Transaction(doc, "建立牆面套管剖面"))
            {
                tx.Start();
                foreach (var group in groups)
                {
                    groupIdx++;
                    string viewName = groups.Count == 1
                        ? $"{namePrefix}-{wallIdVal}"
                        : $"{namePrefix}-{wallIdVal}-{groupIdx}";
                    viewName = EnsureUniqueViewName(doc, viewName);

                    BoundingBoxXYZ sectionBox = (autoCrop && group != null && group.Count > 0)
                        ? BuildCroppedBox(wallDir, viewDir, group, marginHorizMm, marginVertMm, offsetMm, wallW, worldStart)
                        : BuildFullWallBox(wallDir, viewDir, worldStart, worldEnd, wallH, wallW, offsetMm, marginHorizMm, marginVertMm);

                    var sv   = ViewSection.CreateSection(doc, sectionVFT.Id, sectionBox);
                    sv.Name  = viewName;
                    sv.Scale = scale;

                    createdViews.Add(new
                    {
                        ViewId       = sv.Id.GetIdValue(),
                        ViewName     = viewName,
                        OpeningCount = group?.Count ?? 0,
                    });
                }
                tx.Commit();
            }

            return new
            {
                Success      = true,
                WallId       = wallIdVal,
                WallLinkId   = wallLinkId,
                ViewCount    = createdViews.Count,
                OpeningCount = openings.Count,
                Views        = createdViews,
                Summary      = $"牆 {wallIdVal}：建立 {createdViews.Count} 個剖面視圖（偵測到 {openings.Count} 個開孔）。",
            };
        }

        // ── 資料類別 ──────────────────────────────────────────────────────

        private class PsOpeningInfo
        {
            public IdType  Id;
            public string  Shape;       // "circular" | "rectangular"
            public XYZ     Center;      // 世界座標
            public double  SizeAlongWall;  // ft，沿牆方向的尺寸
            public double  SizeVert;       // ft，垂直方向（高度）
            public double  ProjDist;    // 從牆 worldStart 算的距離 ft
        }

        /// <summary>診斷用：被略過的牆資訊</summary>
        private class PsSkipInfo
        {
            public IdType Id;
            public string Reason;
            public double LenMm;
            public double SX, SY, EX, EY;
        }

        /// <summary>
        /// 預篩後的套管原始資料：含世界座標 Solid（供實體相交判定）與 AABB（供快速粗篩）
        /// </summary>
        private class PsOpeningRaw
        {
            public List<Solid> WorldSolids;  // 已轉至世界座標的實體
            public XYZ     BbMin;            // 世界 AABB 最小角
            public XYZ     BbMax;            // 世界 AABB 最大角
            public XYZ     Center;           // 世界座標中心
            public double  Sx, Sy, Sz;       // 世界 AABB 三軸尺寸 ft
            public bool    IsCircular;
        }

        // ── 實體 / 幾何輔助 ──────────────────────────────────────────────

        /// <summary>取出元素的所有 Solid 並轉至世界座標</summary>
        private List<Solid> GetWorldSolids(Element elem, Transform tr)
        {
            var solids = new List<Solid>();
            var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Medium };
            GeometryElement geo = elem.get_Geometry(opt);
            if (geo != null) CollectSolids(geo, tr, solids);
            return solids;
        }

        private void CollectSolids(GeometryElement geo, Transform tr, List<Solid> outList)
        {
            foreach (GeometryObject g in geo)
            {
                if (g is Solid s)
                {
                    if (s.Volume > 1e-6)
                        outList.Add(tr.IsIdentity ? s : SolidUtils.CreateTransformed(s, tr));
                }
                else if (g is GeometryInstance gi)
                {
                    // GetInstanceGeometry 已套用族群實例變換（連結本地座標），再套 tr 轉世界
                    GeometryElement instGeo = gi.GetInstanceGeometry();
                    if (instGeo != null) CollectSolids(instGeo, tr, outList);
                }
            }
        }

        /// <summary>軸對齊包圍盒是否重疊（快速粗篩）</summary>
        private bool AabbOverlap(XYZ aMin, XYZ aMax, XYZ bMin, XYZ bMax)
        {
            return aMin.X <= bMax.X && aMax.X >= bMin.X
                && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y
                && aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;
        }

        /// <summary>兩組實體是否相交（布林交集有體積）。布林失敗視為相交（多為共面貼合＝實際穿越）</summary>
        private bool SolidsIntersect(List<Solid> a, List<Solid> b)
        {
            foreach (var sa in a)
            {
                if (sa == null || sa.Volume < 1e-9) continue;
                foreach (var sb in b)
                {
                    if (sb == null || sb.Volume < 1e-9) continue;
                    try
                    {
                        Solid r = BooleanOperationsUtils.ExecuteBooleanOperation(
                            sa, sb, BooleanOperationsType.Intersect);
                        if (r != null && r.Volume > 1e-6) return true;
                    }
                    catch
                    {
                        // 布林運算失敗通常是共面/貼合，代表確實接觸 → 視為相交（偏向不漏牆）
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>取元素本地 BoundingBox 八個角，轉世界座標後求 AABB</summary>
        private void WorldAabb(BoundingBoxXYZ localBb, Transform tr, out XYZ min, out XYZ max)
        {
            var corners = new[]
            {
                new XYZ(localBb.Min.X, localBb.Min.Y, localBb.Min.Z),
                new XYZ(localBb.Max.X, localBb.Min.Y, localBb.Min.Z),
                new XYZ(localBb.Min.X, localBb.Max.Y, localBb.Min.Z),
                new XYZ(localBb.Min.X, localBb.Min.Y, localBb.Max.Z),
                new XYZ(localBb.Max.X, localBb.Max.Y, localBb.Min.Z),
                new XYZ(localBb.Max.X, localBb.Min.Y, localBb.Max.Z),
                new XYZ(localBb.Min.X, localBb.Max.Y, localBb.Max.Z),
                new XYZ(localBb.Max.X, localBb.Max.Y, localBb.Max.Z),
            };
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var c in corners)
            {
                XYZ w = tr.OfPoint(c);
                minX = Math.Min(minX, w.X); maxX = Math.Max(maxX, w.X);
                minY = Math.Min(minY, w.Y); maxY = Math.Max(maxY, w.Y);
                minZ = Math.Min(minZ, w.Z); maxZ = Math.Max(maxZ, w.Z);
            }
            min = new XYZ(minX, minY, minZ);
            max = new XYZ(maxX, maxY, maxZ);
        }

        // ── 套管搜尋（跨所有連結模型）────────────────────────────────────

        private List<PsOpeningInfo> CollectWallOpeningsFromLinks(
            List<RevitLinkInstance> links,
            XYZ worldStart, XYZ worldEnd,
            XYZ wallDir, double wallWidthFt)
        {
            var result = new List<PsOpeningInfo>();

            // 牆中心線向量（世界座標）
            XYZ wallVec = worldEnd - worldStart;
            double wallLen = wallVec.GetLength();

            // 搜尋寬度 = 牆厚 / 2 + 500mm 容差
            double perpThreshold = wallWidthFt / 2.0 + 500 * _MM2FT;

            foreach (var link in links)
            {
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null) continue;
                Transform tr = link.GetTotalTransform();

                // 管附件（圓形套管）
                var pipeAccs = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_PipeAccessory)
                    .WhereElementIsNotElementType()
                    .Cast<Element>();

                // 風管附件（矩形開口）
                var ductAccs = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_DuctAccessory)
                    .WhereElementIsNotElementType()
                    .Cast<Element>();

                foreach (var elem in pipeAccs.Concat(ductAccs))
                {
                    bool isCircular = elem.Category.Id.GetIdValue()
                        == (IdType)(int)BuiltInCategory.OST_PipeAccessory;

                    var bb = elem.get_BoundingBox(null);
                    if (bb == null) continue;

                    // 元素中心（連結本地座標 → 世界座標）
                    XYZ localCenter = (bb.Min + bb.Max) / 2.0;
                    XYZ worldCenter = tr.OfPoint(localCenter);

                    // 投影到牆中心線
                    XYZ toCenter = worldCenter - worldStart;
                    double projParam = toCenter.DotProduct(wallDir.Normalize());

                    // 超出牆端點範圍則跳過
                    if (projParam < -perpThreshold || projParam > wallLen + perpThreshold) continue;

                    // 計算垂直於牆中心線的距離（XY 平面）
                    XYZ projPt = worldStart + wallDir.Normalize() * projParam;
                    double perpDist = new XYZ(worldCenter.X - projPt.X, worldCenter.Y - projPt.Y, 0).GetLength();

                    if (perpDist > perpThreshold) continue;

                    // 計算元素沿牆方向的尺寸（轉換後的 bounding box）
                    XYZ wMin = tr.OfPoint(bb.Min);
                    XYZ wMax = tr.OfPoint(bb.Max);
                    double sizeX = Math.Abs(wMax.X - wMin.X);
                    double sizeY = Math.Abs(wMax.Y - wMin.Y);
                    double sizeZ = Math.Abs(wMax.Z - wMin.Z);

                    // 沿牆方向的尺寸
                    double sizeAlong = Math.Abs(wallDir.X) > Math.Abs(wallDir.Y) ? sizeX : sizeY;
                    double sizeVert  = sizeZ;

                    result.Add(new PsOpeningInfo
                    {
                        Id            = elem.Id.GetIdValue(),
                        Shape         = isCircular ? "circular" : "rectangular",
                        Center        = worldCenter,
                        SizeAlongWall = sizeAlong,
                        SizeVert      = sizeVert,
                        ProjDist      = projParam,
                    });
                }
            }

            return result.OrderBy(o => o.ProjDist).ToList();
        }

        // ── 長牆分割（相鄰開孔間距 > 3m 切割）────────────────────────────

        private List<List<PsOpeningInfo>> SplitOpeningsIntoGroups(List<PsOpeningInfo> openings)
        {
            double splitFt = 3000 * _MM2FT;
            var groups = new List<List<PsOpeningInfo>>();
            var cur = new List<PsOpeningInfo> { openings[0] };
            for (int i = 1; i < openings.Count; i++)
            {
                if (openings[i].ProjDist - openings[i - 1].ProjDist > splitFt)
                {
                    groups.Add(cur);
                    cur = new List<PsOpeningInfo>();
                }
                cur.Add(openings[i]);
            }
            groups.Add(cur);
            return groups;
        }

        // ── 觀察方向判定 ─────────────────────────────────────────────────

        private XYZ DetermineViewDir(Document doc, Wall wall, Transform wallTr, XYZ defaultNormal, string logic)
        {
            if (logic == "inside_out") return defaultNormal;
            if (logic == "outside_in") return defaultNormal.Negate();

            // auto：找牆中點兩側 500mm 有無房間
            try
            {
                var locCurve = wall.Location as LocationCurve;
                XYZ localMid = locCurve.Curve.Evaluate(0.5, true);
                XYZ worldMid = wallTr.OfPoint(localMid);

                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();

                foreach (XYZ testDir in new[] { defaultNormal, defaultNormal.Negate() })
                {
                    XYZ testPt = worldMid + testDir * (500 * _MM2FT);
                    if (rooms.Any(r => r.IsPointInRoom(testPt)))
                        return testDir.Negate(); // 從房間側朝外看
                }
            }
            catch { }

            return defaultNormal;
        }

        // ── 剖面框建立 ───────────────────────────────────────────────────

        private BoundingBoxXYZ BuildCroppedBox(
            XYZ wallDir, XYZ viewDir,
            List<PsOpeningInfo> group,
            double marginHorizMm, double marginVertMm,
            double offsetMm, double wallWidthFt,
            XYZ wallStart = null,
            double cropZMin = double.NaN, double cropZMax = double.NaN,
            double segMinProj = double.NaN, double segMaxProj = double.NaN)
        {
            double hMgn      = marginHorizMm * _MM2FT;
            double off       = offsetMm      * _MM2FT;
            double farMargin = 30 * _MM2FT;  // 視景深度 = 牆厚 + 30mm（文件規定）

            // ── 左右範圍 ──
            // 優先用傳入的牆段邊界（貼齊牆段）；否則退回到開孔分佈範圍 + 留白
            double minProj, maxProj;
            if (!double.IsNaN(segMinProj) && !double.IsNaN(segMaxProj))
            {
                minProj = segMinProj;
                maxProj = segMaxProj;
            }
            else
            {
                minProj = group.Min(o => o.ProjDist - o.SizeAlongWall / 2.0) - hMgn;
                maxProj = group.Max(o => o.ProjDist + o.SizeAlongWall / 2.0) + hMgn;
            }

            // ── 上下範圍 ──
            // 優先用樓層線基準 Z（±150mm 已由呼叫端計算）；否則退回開孔範圍 ± marginVertical
            double minZ, maxZ;
            if (!double.IsNaN(cropZMin) && !double.IsNaN(cropZMax))
            {
                minZ = cropZMin;
                maxZ = cropZMax;
            }
            else
            {
                double vMgn = marginVertMm * _MM2FT;
                minZ = group.Min(o => o.Center.Z - o.SizeVert / 2.0) - vMgn;
                maxZ = group.Max(o => o.Center.Z + o.SizeVert / 2.0) + vMgn;
            }

            double midProj = (minProj + maxProj) / 2.0;
            double midZ    = (minZ + maxZ)       / 2.0;

            // 原點：牆中心線上 midProj 點，沿 viewDir 推到「觀察側牆面 + offset(1mm)」
            // 使剖面線緊貼牆面，深度單向往牆內延伸（非中心對稱凸出）
            XYZ onWall = (wallStart != null)
                ? wallStart + wallDir * midProj
                : group[group.Count / 2].Center;
            XYZ originBase = new XYZ(onWall.X, onWall.Y, midZ);
            XYZ origin = originBase + viewDir * (wallWidthFt / 2.0 + off);

            var bb = new BoundingBoxXYZ();
            var tr = Transform.Identity;
            tr.Origin = origin;
            tr.BasisX = wallDir;
            tr.BasisY = XYZ.BasisZ;
            tr.BasisZ = viewDir;
            bb.Transform = tr;

            double halfW = (maxProj - minProj) / 2.0;

            // BasisZ(=viewDir)：近裁切面在剖面線外 1mm，遠裁切面 = 牆面 + 牆厚 + 30mm
            bb.Min = new XYZ(-halfW, minZ - midZ, -(off + wallWidthFt + farMargin));
            bb.Max = new XYZ( halfW, maxZ - midZ,  off);
            return bb;
        }

        private BoundingBoxXYZ BuildFullWallBox(
            XYZ wallDir, XYZ viewDir,
            XYZ worldStart, XYZ worldEnd,
            double wallH, double wallW,
            double offsetMm, double marginHorizMm, double marginVertMm)
        {
            double hMgn  = marginHorizMm * _MM2FT;
            double vMgn  = marginVertMm  * _MM2FT;
            double off   = offsetMm      * _MM2FT;
            double depth = wallW + 1.0;
            double halfL = worldStart.DistanceTo(worldEnd) / 2.0 + hMgn;

            XYZ midPt = (worldStart + worldEnd) / 2.0;

            var bb = new BoundingBoxXYZ();
            var tr = Transform.Identity;
            tr.Origin = midPt;
            tr.BasisX = wallDir;
            tr.BasisY = XYZ.BasisZ;
            tr.BasisZ = viewDir;
            bb.Transform = tr;

            bb.Min = new XYZ(-halfL, -vMgn,      -off - depth);
            bb.Max = new XYZ( halfL,  wallH + vMgn, off + depth);
            return bb;
        }

        // ── 工具方法 ─────────────────────────────────────────────────────

        private double GetWallHeight(Wall wall)
        {
            var p = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (p != null && p.HasValue && p.AsDouble() > 0) return p.AsDouble();
            return 4000 * _MM2FT;
        }

        private string EnsureUniqueViewName(Document doc, string baseName)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(v => v.Name)
                .ToHashSet();

            if (!existing.Contains(baseName)) return baseName;
            for (int i = 2; i < 200; i++)
            {
                string c = $"{baseName}-{i}";
                if (!existing.Contains(c)) return c;
            }
            return baseName + "-" + DateTime.Now.ToString("HHmmss");
        }

        // ── 批次建立（依當前視圖範圍過濾牆，只對有開孔的牆建剖面）────────

        private object BatchCreateWallSections(JObject parameters)
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return new { Success = false, Error = "無法取得文件" };

            var activeView = _uiApp.ActiveUIDocument.ActiveView;
            if (activeView == null) return new { Success = false, Error = "無法取得當前視圖" };

            IdType wallLinkId      = parameters["wallLinkId"]?.Value<IdType>()
                ?? throw new ArgumentException("缺少 wallLinkId");
            bool   splitLong       = parameters["splitLongWalls"]?.Value<bool>() ?? true;
            string namePrefix      = parameters["viewNamePrefix"]?.Value<string>() ?? "牆面套管剖面";
            bool   autoCrop        = parameters["autoCrop"]?.Value<bool>() ?? true;
            string dirLogic        = parameters["directionLogic"]?.Value<string>() ?? "auto";
            int    scale           = parameters["scale"]?.Value<int>() ?? 50;
            double marginHorizMm   = parameters["marginHorizontal"]?.Value<double>() ?? 500.0;
            double marginVertMm    = parameters["marginVertical"]?.Value<double>() ?? 200.0;
            double minWallLenMm    = parameters["minWallLength"]?.Value<double>() ?? 500.0;
            bool   seqNaming       = parameters["sequentialNaming"]?.Value<bool>() ?? false;
            string sortOrder       = parameters["sortOrder"]?.Value<string>() ?? "x_then_y";

            // ── 1. 取得連結模型 ────────────────────────────────────────
            var linkInst = doc.GetElement(new ElementId(wallLinkId)) as RevitLinkInstance;
            if (linkInst == null)
                return new { Success = false, Error = $"找不到連結模型 ID: {wallLinkId}" };

            Document linkDoc = linkInst.GetLinkDocument();
            Transform linkTr = linkInst.GetTotalTransform();
            if (linkDoc == null)
                return new { Success = false, Error = "連結模型未載入" };

            // ── 2. 取得當前視圖 BoundingBox → 定義搜尋範圍 ───────────
            BoundingBoxXYZ viewBb = activeView.get_BoundingBox(null);
            if (viewBb == null)
                return new { Success = false, Error = "無法取得視圖範圍" };

            // 以當前視圖的關聯樓層高程決定 Z 範圍，避免收到相鄰樓層的牆
            // 平面視圖的 viewBb.Z 只是切面位置，不代表樓層高度範圍
            Level activeLevel = activeView.GenLevel;
            double levelElev = activeLevel?.Elevation ?? viewBb.Min.Z;
            // 往下 500mm（地板厚度緩衝）、往上 6000mm（涵蓋樓層高度）
            double zFloorMin = levelElev - 500 * _MM2FT;
            double zFloorMax = levelElev + 6000 * _MM2FT;

            Transform linkInv = linkTr.Inverse;
            XYZ localMin = linkInv.OfPoint(new XYZ(viewBb.Min.X, viewBb.Min.Y, zFloorMin));
            XYZ localMax = linkInv.OfPoint(new XYZ(viewBb.Max.X, viewBb.Max.Y, zFloorMax));

            var searchOutline = new Outline(
                new XYZ(Math.Min(localMin.X, localMax.X), Math.Min(localMin.Y, localMax.Y), Math.Min(localMin.Z, localMax.Z)),
                new XYZ(Math.Max(localMin.X, localMax.X), Math.Max(localMin.Y, localMax.Y), Math.Max(localMin.Z, localMax.Z))
            );

            // ── 3. 過濾視圖範圍內的牆 ─────────────────────────────────
            var wallsInView = new FilteredElementCollector(linkDoc)
                .OfClass(typeof(Wall))
                .WherePasses(new BoundingBoxIntersectsFilter(searchOutline))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(w =>
                {
                    var lc = w.Location as LocationCurve;
                    if (lc == null) return false;
                    double lenMm = lc.Curve.Length * 304.8;
                    return lenMm >= minWallLenMm;
                })
                .ToList();

            // ── 4. 預先從所有連結模型篩出「當樓層範圍」的管附件 + 風管附件 ──
            //      以視圖 BoundingBox（XY + Z）轉至各連結模型本地座標後直接用
            //      BoundingBoxIntersectsFilter 過濾，讓 Revit 的空間索引加速篩選，
            //      不需在記憶體裡逐一比對整棟所有元素。
            var allLinks = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(li => li.GetLinkDocument() != null)
                .ToList();

            // 以樓層高程決定套管預篩 Z 範圍（與牆篩選範圍一致）
            double viewZMin = zFloorMin;
            double viewZMax = zFloorMax;

            var allOpenings = new List<PsOpeningRaw>();

            foreach (var li in allLinks)
            {
                Document lDoc = li.GetLinkDocument();
                Transform lTr = li.GetTotalTransform();
                Transform lInv = lTr.Inverse;

                // 將視圖 BoundingBox（含 Z 緩衝）轉至此連結模型本地座標
                XYZ lMin = lInv.OfPoint(new XYZ(viewBb.Min.X, viewBb.Min.Y, viewZMin));
                XYZ lMax = lInv.OfPoint(new XYZ(viewBb.Max.X, viewBb.Max.Y, viewZMax));
                var levelOutline = new Outline(
                    new XYZ(Math.Min(lMin.X, lMax.X), Math.Min(lMin.Y, lMax.Y), Math.Min(lMin.Z, lMax.Z)),
                    new XYZ(Math.Max(lMin.X, lMax.X), Math.Max(lMin.Y, lMax.Y), Math.Max(lMin.Z, lMax.Z))
                );
                var bbFilter = new BoundingBoxIntersectsFilter(levelOutline);

                // 管附件（圓形套管）
                var pipeAccs = new FilteredElementCollector(lDoc)
                    .OfCategory(BuiltInCategory.OST_PipeAccessory)
                    .WherePasses(bbFilter)
                    .WhereElementIsNotElementType()
                    .Cast<Element>();

                // 風管附件（矩形開口）
                var ductAccs = new FilteredElementCollector(lDoc)
                    .OfCategory(BuiltInCategory.OST_DuctAccessory)
                    .WherePasses(bbFilter)
                    .WhereElementIsNotElementType()
                    .Cast<Element>();

                foreach (var elem in pipeAccs.Concat(ductAccs))
                {
                    var bb = elem.get_BoundingBox(null);
                    if (bb == null) continue;

                    // 世界 AABB（粗篩用）
                    WorldAabb(bb, lTr, out XYZ wbMin, out XYZ wbMax);
                    XYZ wCenter = (wbMin + wbMax) / 2.0;

                    // 世界座標實體（實體相交判定用）；無實體則略過
                    var worldSolids = GetWorldSolids(elem, lTr);
                    if (worldSolids.Count == 0) continue;

                    bool isCirc = elem.Category.Id.GetIdValue()
                        == (IdType)(int)BuiltInCategory.OST_PipeAccessory;

                    allOpenings.Add(new PsOpeningRaw
                    {
                        WorldSolids = worldSolids,
                        BbMin       = wbMin,
                        BbMax       = wbMax,
                        Center      = wCenter,
                        Sx          = wbMax.X - wbMin.X,
                        Sy          = wbMax.Y - wbMin.Y,
                        Sz          = wbMax.Z - wbMin.Z,
                        IsCircular  = isCirc,
                    });
                }
            }

            // ── 5. 決定剖面裁剪框上下 Z：以當樓層與上方樓層線為基準 ±150mm ──
            //      若找不到上方樓層，fallback 到當樓層 + 4m
            double levelOffMm  = 150 * _MM2FT;
            double cropZBottom = levelElev - levelOffMm;

            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(lv => lv.Elevation)
                .ToList();

            Level nextLevel = allLevels.FirstOrDefault(lv => lv.Elevation > levelElev + 100 * _MM2FT);
            double cropZTop = nextLevel != null
                ? nextLevel.Elevation + levelOffMm
                : levelElev + 4000 * _MM2FT + levelOffMm;

            // ── 7. 取得剖面 ViewFamilyType ────────────────────────────
            var sectionVFT = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Section);
            if (sectionVFT == null)
                return new { Success = false, Error = "找不到剖面視圖類型" };

            // ── 6. 逐牆判斷是否有開孔，有則建剖面 ────────────────────
            // 若啟用流水號命名，先依 sortOrder 對牆排序
            if (seqNaming)
            {
                switch (sortOrder)
                {
                    case "y_then_x":
                        wallsInView = wallsInView.OrderBy(w =>
                        {
                            var lc = w.Location as LocationCurve;
                            XYZ mid = linkTr.OfPoint(lc.Curve.Evaluate(0.5, true));
                            return mid.Y;
                        }).ThenBy(w =>
                        {
                            var lc = w.Location as LocationCurve;
                            XYZ mid = linkTr.OfPoint(lc.Curve.Evaluate(0.5, true));
                            return mid.X;
                        }).ToList();
                        break;
                    case "creation":
                        // 保持原始順序
                        break;
                    default: // x_then_y
                        wallsInView = wallsInView.OrderBy(w =>
                        {
                            var lc = w.Location as LocationCurve;
                            XYZ mid = linkTr.OfPoint(lc.Curve.Evaluate(0.5, true));
                            return mid.X;
                        }).ThenBy(w =>
                        {
                            var lc = w.Location as LocationCurve;
                            XYZ mid = linkTr.OfPoint(lc.Curve.Evaluate(0.5, true));
                            return mid.Y;
                        }).ToList();
                        break;
                }
            }

            var createdViews = new List<object>();
            var skippedWalls = new List<PsSkipInfo>();
            int seqCounter   = 1; // 流水號計數器

            using (var tx = new Transaction(doc, "批次建立牆面套管剖面"))
            {
                tx.Start();

                foreach (var wall in wallsInView)
                {
                    var lc = wall.Location as LocationCurve;
                    if (lc == null) continue;

                    XYZ worldStart = linkTr.OfPoint(lc.Curve.GetEndPoint(0));
                    XYZ worldEnd   = linkTr.OfPoint(lc.Curve.GetEndPoint(1));
                    XYZ wallDir    = (worldEnd - worldStart).Normalize();
                    double wallLen = worldStart.DistanceTo(worldEnd);
                    double wallW   = wall.Width;

                    // 牆的世界座標實體與 AABB（方法 C：實體相交 + bbox 粗篩）
                    var wallSolids = GetWorldSolids(wall, linkTr);
                    if (wallSolids.Count == 0)
                    {
                        skippedWalls.Add(new PsSkipInfo {
                            Id = wall.Id.GetIdValue(), Reason = "無實體", LenMm = wallLen * 304.8,
                            SX = worldStart.X, SY = worldStart.Y, EX = worldEnd.X, EY = worldEnd.Y });
                        continue;
                    }
                    BoundingBoxXYZ wallLocalBb = wall.get_BoundingBox(null);
                    XYZ wallBbMin, wallBbMax;
                    if (wallLocalBb != null)
                        WorldAabb(wallLocalBb, linkTr, out wallBbMin, out wallBbMax);
                    else { wallBbMin = worldStart; wallBbMax = worldEnd; }

                    // 篩出此牆的開孔：先 AABB 粗篩，再實體相交確認
                    var wallOpenings = new List<PsOpeningInfo>();
                    foreach (var op in allOpenings)
                    {
                        // 快速粗篩：套管 AABB 與牆 AABB 不重疊 → 直接跳過
                        if (!AabbOverlap(wallBbMin, wallBbMax, op.BbMin, op.BbMax)) continue;

                        // 實體相交確認：套管實體有沒有真的穿過牆實體
                        if (!SolidsIntersect(wallSolids, op.WorldSolids)) continue;

                        // 通過 → 計算剖面裁剪所需的沿牆位置與尺寸
                        XYZ toCenter = op.Center - worldStart;
                        double projDist  = toCenter.DotProduct(wallDir);
                        double sizeAlong = op.Sx * Math.Abs(wallDir.X) + op.Sy * Math.Abs(wallDir.Y);

                        wallOpenings.Add(new PsOpeningInfo
                        {
                            Id            = wall.Id.GetIdValue(),
                            Shape         = op.IsCircular ? "circular" : "rectangular",
                            Center        = op.Center,
                            SizeAlongWall = sizeAlong,
                            SizeVert      = op.Sz,
                            ProjDist      = projDist,
                        });
                    }

                    if (wallOpenings.Count == 0)
                    {
                        skippedWalls.Add(new PsSkipInfo {
                            Id = wall.Id.GetIdValue(), Reason = "無開孔", LenMm = wallLen * 304.8,
                            SX = worldStart.X, SY = worldStart.Y, EX = worldEnd.X, EY = worldEnd.Y });
                        continue;
                    }

                    wallOpenings = wallOpenings.OrderBy(o => o.ProjDist).ToList();

                    // 長牆分割
                    var groups = splitLong
                        ? SplitOpeningsIntoGroups(wallOpenings)
                        : new List<List<PsOpeningInfo>> { wallOpenings };

                    // 計算每個群組的左右牆段邊界（貼齊牆段）：
                    // 單一群組 → 整面牆 [0, wallLen]；
                    // 多群組 → 以相鄰群組間隙中點切分，頭尾貼齊牆端。
                    var segBounds = new List<(double Min, double Max)>();
                    for (int gi = 0; gi < groups.Count; gi++)
                    {
                        double segMin = (gi == 0)
                            ? 0.0
                            : (groups[gi - 1].Max(o => o.ProjDist) + groups[gi].Min(o => o.ProjDist)) / 2.0;
                        double segMax = (gi == groups.Count - 1)
                            ? wallLen
                            : (groups[gi].Max(o => o.ProjDist) + groups[gi + 1].Min(o => o.ProjDist)) / 2.0;
                        segBounds.Add((segMin, segMax));
                    }

                    XYZ wallNormal = wallDir.CrossProduct(XYZ.BasisZ).Normalize();
                    XYZ viewDir    = DetermineViewDir(doc, wall, linkTr, wallNormal, dirLogic);
                    double wallH   = GetWallHeight(wall);

                    int groupIdx = 0;
                    foreach (var group in groups)
                    {
                        groupIdx++;
                        string viewName;
                        if (seqNaming)
                        {
                            // 流水號：前綴-001，長牆分割時加 -1/-2 後綴
                            string seq = seqCounter.ToString("D3");
                            viewName = groups.Count == 1
                                ? $"{namePrefix}-{seq}"
                                : $"{namePrefix}-{seq}-{groupIdx}";
                            if (groupIdx == groups.Count) seqCounter++; // 同一牆的最後一組才遞增
                        }
                        else
                        {
                            viewName = groups.Count == 1
                                ? $"{namePrefix}-{wall.Id.GetIdValue()}"
                                : $"{namePrefix}-{wall.Id.GetIdValue()}-{groupIdx}";
                        }
                        viewName = EnsureUniqueViewName(doc, viewName);

                        var (segMin, segMax) = segBounds[groupIdx - 1];
                        BoundingBoxXYZ sectionBox = autoCrop
                            ? BuildCroppedBox(wallDir, viewDir, group, marginHorizMm, marginVertMm, 1.0, wallW, worldStart, cropZBottom, cropZTop, segMin, segMax)
                            : BuildFullWallBox(wallDir, viewDir, worldStart, worldEnd, wallH, wallW, 1.0, marginHorizMm, marginVertMm);

                        var sv   = ViewSection.CreateSection(doc, sectionVFT.Id, sectionBox);
                        sv.Name  = viewName;
                        sv.Scale = scale;

                        createdViews.Add(new
                        {
                            ViewId       = sv.Id.GetIdValue(),
                            ViewName     = viewName,
                            WallId       = wall.Id.GetIdValue(),
                            OpeningCount = group.Count,
                        });
                    }
                }

                tx.Commit();
            }

            // 診斷：列出長度 > 4m 卻被略過的牆（協助定位漏抓）
            var longSkipped = skippedWalls
                .Where(s => s.LenMm > 4000)
                .OrderByDescending(s => s.LenMm)
                .Select(s => new {
                    WallId = s.Id, s.Reason, LenMm = Math.Round(s.LenMm),
                    SX = Math.Round(s.SX), SY = Math.Round(s.SY),
                    EX = Math.Round(s.EX), EY = Math.Round(s.EY) })
                .ToList();

            return new
            {
                Success       = true,
                WallsChecked  = wallsInView.Count,
                WallsWithOpenings = createdViews.Select(v => ((dynamic)v).WallId).Distinct().Count(),
                WallsSkipped  = skippedWalls.Count,
                ViewsCreated  = createdViews.Count,
                OpeningsCollected = allOpenings.Count,
                LongSkippedWalls  = longSkipped,
                Views         = createdViews,
                Summary       = $"視圖範圍內 {wallsInView.Count} 面牆，{createdViews.Select(v => ((dynamic)v).WallId).Distinct().Count()} 面有開孔，建立了 {createdViews.Count} 個剖面視圖。",
            };
        }

        #endregion
    }
}
