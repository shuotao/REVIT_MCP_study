using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Core
{
    /// <summary>
    /// 外牆開口檢討核心類別
    /// 依據建築技術規則第45條、第110條進行檢查
    /// </summary>
    public class ExteriorWallOpeningChecker
    {
        private readonly Document _doc;
        private const double FEET_TO_METER = 0.3048;

        public ExteriorWallOpeningChecker(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        #region 階段 1：識別外牆與開口

        /// <summary>
        /// 取得所有外牆
        /// </summary>
        public List<Wall> GetExteriorWalls()
        {
            var collector = new FilteredElementCollector(_doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w => w.WallType.Function == WallFunction.Exterior)
                .ToList();

            return collector;
        }

        /// <summary>
        /// 取得牆壁上的所有開口（門窗）
        /// </summary>
        public List<FamilyInstance> GetWallOpenings(Wall wall)
        {
            var openings = new List<FamilyInstance>();

            // 使用 FindInserts 取得牆壁上的所有嵌入元素
            var insertIds = wall.FindInserts(true, true, true, true);
            
            foreach (ElementId id in insertIds)
            {
                var element = _doc.GetElement(id);
                if (element is FamilyInstance fi)
                {
                    // 篩選門窗元素
                    if (fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows ||
                        fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors)
                    {
                        openings.Add(fi);
                    }
                }
            }

            return openings;
        }

        /// <summary>
        /// 取得開口資訊
        /// </summary>
        public OpeningInfo GetOpeningInfo(FamilyInstance opening)
        {
            var location = (opening.Location as LocationPoint)?.Point;
            if (location == null)
                return null;

            // 取得開口尺寸
            double width = 0, height = 0, area = 0;
            
            var widthParam = opening.Symbol.get_Parameter(BuiltInParameter.WINDOW_WIDTH) ??
                           opening.Symbol.get_Parameter(BuiltInParameter.DOOR_WIDTH);
            var heightParam = opening.Symbol.get_Parameter(BuiltInParameter.WINDOW_HEIGHT) ??
                            opening.Symbol.get_Parameter(BuiltInParameter.DOOR_HEIGHT);

            if (widthParam != null) width = widthParam.AsDouble();
            if (heightParam != null) height = heightParam.AsDouble();
            area = width * height;

            return new OpeningInfo
            {
                OpeningId = opening.Id,
                WallId = opening.Host?.Id,
                OpeningType = opening.Category.Name,
                Location = location,
                Width = width,
                Height = height,
                Area = area,
                HostWall = opening.Host as Wall
            };
        }

        #endregion

        #region 階段 2：計算距離

        /// <summary>
        /// 取得基地邊界線（PropertyLine）
        /// </summary>
        public List<Curve> GetPropertyLines()
        {
            var propertyLineCurves = new List<Curve>();

            // 策略 A: 嘗試取得 PropertyLine 物件 (通常是繪製或表格建立的)
            var plCollector = new FilteredElementCollector(_doc)
                .OfClass(typeof(PropertyLine))
                .WhereElementIsNotElementType();

            foreach (var elem in plCollector)
            {
                var opt = new Options { DetailLevel = ViewDetailLevel.Fine };
                var geomElem = elem.get_Geometry(opt);

                if (geomElem != null)
                {
                    foreach (var geomObj in geomElem)
                    {
                        if (geomObj is Line line)
                        {
                            propertyLineCurves.Add(line);
                        }
                        else if (geomObj is Arc arc)
                        {
                            propertyLineCurves.Add(arc);
                        }
                    }
                }
            }

            // 策略 B: 如果還是空的，嘗試取得 OST_SitePropertyLineSegment (舊版或特定資料結構)
            if (propertyLineCurves.Count == 0)
            {
                var segmentCollector = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_SitePropertyLineSegment)
                    .WhereElementIsNotElementType();

                foreach (Element elem in segmentCollector)
                {
                    if (elem.Location is LocationCurve locCurve)
                    {
                        propertyLineCurves.Add(locCurve.Curve);
                    }
                }
            }

            return propertyLineCurves;
        }

        /// <summary>
        /// 計算點到邊界線的最短距離
        /// </summary>
        public class BoundaryDistanceResult
        {
            public double MinDistance { get; set; }
            public XYZ ClosestPoint { get; set; }
        }

        /// <summary>
        /// 計算點到邊界線的最短距離 (傳回詳細資訊)
        /// </summary>
        public BoundaryDistanceResult CalculateDistanceToBoundary(OpeningInfo opening, List<Curve> boundaryLines)
        {
            if (boundaryLines == null || boundaryLines.Count == 0)
                throw new InvalidOperationException("找不到基地邊界線，請確認專案中已建立 Property Line");

            double minDistance = double.MaxValue;
            XYZ closestPoint = null;

            // 計算外牆外皮點: 沿牆壁外法線偏移半個牆厚
            XYZ point = opening.Location;
            if (opening.HostWall != null)
            {
                XYZ wallNormal = opening.HostWall.Orientation; // 牆壁的外部法線向量
                double wallThickness = opening.HostWall.WallType.Width;
                point = point + wallNormal * (wallThickness / 2.0);
            }

            foreach (var curve in boundaryLines)
            {
                // Property lines are usually projected to Z=0. 
                // Project the test point to Z=0 for accurate XY distance measurement.
                XYZ testPoint = new XYZ(point.X, point.Y, 0); 
                
                IntersectionResult result = curve.Project(testPoint);
                if (result != null)
                {
                    double distance = result.Distance;
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestPoint = result.XYZPoint;
                    }
                }
                else
                {
                    // Fallback to evaluating points if direct projection fails
                    for (double val = 0; val <= 1; val += 0.05)
                    {
                        XYZ pCrv = curve.Evaluate(val, true);
                        XYZ pCrvFlat = new XYZ(pCrv.X, pCrv.Y, 0);
                        double dist = pCrvFlat.DistanceTo(testPoint);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            closestPoint = pCrvFlat;
                        }
                    }
                }
            }

            return new BoundaryDistanceResult { MinDistance = minDistance, ClosestPoint = closestPoint };
        }

        /// <summary>
        /// 計算到同基地內其他建築物的距離
        /// </summary>
        public double CalculateDistanceToAdjacentBuildings(OpeningInfo opening, Wall currentWall)
        {
            double minDistance = double.MaxValue;

            // 取得所有外牆
            var allWalls = GetExteriorWalls();

            // 計算外牆外皮點
            XYZ point = opening.Location;
            if (opening.HostWall != null)
            {
                XYZ wallNormal = opening.HostWall.Orientation;
                double wallThickness = opening.HostWall.WallType.Width;
                point = point + wallNormal * (wallThickness / 2.0);
            }
            XYZ testPoint = new XYZ(point.X, point.Y, 0);

            foreach (var wall in allWalls)
            {
                // 排除自身所屬的牆壁
                if (wall.Id == currentWall.Id)
                    continue;

                // 取得牆壁的位置曲線
                if (wall.Location is LocationCurve locCurve)
                {
                    var curve = locCurve.Curve;
                    IntersectionResult result = curve.Project(testPoint);
                    if (result != null)
                    {
                        // 扣除對方牆壁厚度的一半（因為 curve 是牆心線）
                        double otherWallHalfThick = wall.WallType.Width / 2.0;
                        double distance = Math.Max(0, result.Distance - otherWallHalfThick);
                        
                        if (distance < minDistance)
                            minDistance = distance;
                    }
                }
            }

            return minDistance;
        }

        #endregion

        #region 階段 3：法規判定

        /// <summary>
        /// 執行第45條檢查（開口距離限制）
        /// </summary>
        public Article45Result CheckArticle45(OpeningInfo opening, double distanceToBoundary, double distanceToBuilding)
        {
            var result = new Article45Result
            {
                OpeningId = opening.OpeningId,
                DistanceToBoundary = distanceToBoundary * FEET_TO_METER,
                DistanceToBuilding = distanceToBuilding * FEET_TO_METER
            };

            // 檢查距離境界線（第45條：緊鄰鄰地不得向鄰地方向開設門窗，除非距離 ≥ 1.0m）
            if (result.DistanceToBoundary < 1.0)
            {
                result.BoundaryStatus = CheckStatus.Fail;
                result.BoundaryMessage = $"開口距境界線僅 {result.DistanceToBoundary:F2}m，不符合第45條規定（需 ≥ 1.0m）";
            }
            else
            {
                result.BoundaryStatus = CheckStatus.Pass;
                result.BoundaryMessage = $"開口距境界線 {result.DistanceToBoundary:F2}m，符合規定";
            }

            // 檢查同基地建築物間距
            // TODO: 需要判斷是雙面開口（≥2.0m）還是單面開口（≥1.0m）
            // 目前暫時使用較嚴格的標準：2.0m
            if (result.DistanceToBuilding < 2.0)
            {
                result.BuildingStatus = CheckStatus.Warning;
                result.BuildingMessage = $"開口距其他建築物 {result.DistanceToBuilding:F2}m，建議檢查是否為單面開口（需 ≥ 1.0m）或雙面開口（需 ≥ 2.0m）";
            }
            else
            {
                result.BuildingStatus = CheckStatus.Pass;
                result.BuildingMessage = $"開口距其他建築物 {result.DistanceToBuilding:F2}m，符合規定";
            }

            // 總體狀態
            result.OverallStatus = result.BoundaryStatus == CheckStatus.Fail || result.BuildingStatus == CheckStatus.Fail
                ? CheckStatus.Fail
                : (result.BoundaryStatus == CheckStatus.Warning || result.BuildingStatus == CheckStatus.Warning
                    ? CheckStatus.Warning
                    : CheckStatus.Pass);

            return result;
        }

        /// <summary>
        /// 執行第110條檢查（防火間隔）
        /// </summary>
        public Article110Result CheckArticle110(OpeningInfo opening, double distanceToBoundary, double distanceToBuilding)
        {
            var result = new Article110Result
            {
                OpeningId = opening.OpeningId,
                DistanceToBoundary = distanceToBoundary * FEET_TO_METER,
                DistanceToBuilding = distanceToBuilding * FEET_TO_METER
            };

            // 基地境界線防火間隔檢查
            if (result.DistanceToBoundary >= 6.0)
            {
                result.BoundaryFireStatus = CheckStatus.NotApplicable;
                result.BoundaryFireMessage = "鄰接6m以上道路或空地，不適用防火間隔規定";
            }
            else if (result.DistanceToBoundary < 1.5)
            {
                result.RequiredFireRating = 1.0;
                result.BoundaryFireStatus = CheckStatus.Warning;
                result.BoundaryFireMessage = $"退縮距離 {result.DistanceToBoundary:F2}m < 1.5m，外牆需1小時防火時效，開口需1小時防火門窗";
            }
            else if (result.DistanceToBoundary < 3.0)
            {
                result.RequiredFireRating = 0.5;
                result.BoundaryFireStatus = CheckStatus.Warning;
                result.BoundaryFireMessage = $"退縮距離 {result.DistanceToBoundary:F2}m (1.5~3.0m)，外牆需半小時防火時效，開口需半小時防火門窗";
            }
            else
            {
                result.BoundaryFireStatus = CheckStatus.Pass;
                result.BoundaryFireMessage = $"退縮距離 {result.DistanceToBoundary:F2}m ≥ 3.0m，不需防火時效";
            }

            // 同基地建築間防火間隔檢查
            if (result.DistanceToBuilding >= 6.0)
            {
                result.BuildingFireStatus = CheckStatus.Pass;
                result.BuildingFireMessage = "建築物間距 ≥ 6.0m，不需防火時效";
            }
            else if (result.DistanceToBuilding < 3.0)
            {
                result.RequiredFireRating = Math.Max(result.RequiredFireRating, 1.0);
                result.BuildingFireStatus = CheckStatus.Warning;
                result.BuildingFireMessage = $"建築物間距 {result.DistanceToBuilding:F2}m < 3.0m，外牆需1小時防火時效，開口需1小時防火門窗";
            }
            else if (result.DistanceToBuilding < 6.0)
            {
                result.RequiredFireRating = Math.Max(result.RequiredFireRating, 0.5);
                result.BuildingFireStatus = CheckStatus.Warning;
                result.BuildingFireMessage = $"建築物間距 {result.DistanceToBuilding:F2}m (3.0~6.0m)，外牆需半小時防火時效，開口需半小時防火門窗";
            }

            // 總體狀態
            if (result.BoundaryFireStatus == CheckStatus.Fail || result.BuildingFireStatus == CheckStatus.Fail)
                result.OverallStatus = CheckStatus.Fail;
            else if (result.BoundaryFireStatus == CheckStatus.Warning || result.BuildingFireStatus == CheckStatus.Warning)
                result.OverallStatus = CheckStatus.Warning;
            else if (result.BoundaryFireStatus == CheckStatus.NotApplicable && result.BuildingFireStatus == CheckStatus.Pass)
                result.OverallStatus = CheckStatus.Pass;
            else if (result.BoundaryFireStatus == CheckStatus.Pass && result.BuildingFireStatus == CheckStatus.Pass)
                result.OverallStatus = CheckStatus.Pass;
            else
                result.OverallStatus = CheckStatus.NotApplicable;

            return result;
        }

        #endregion

        #region 資料結構

        public class OpeningInfo
        {
            public ElementId OpeningId { get; set; }
            public ElementId WallId { get; set; }
            public string OpeningType { get; set; }
            public XYZ Location { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double Area { get; set; }
            public Wall HostWall { get; set; }
        }

        public class Article45Result
        {
            public ElementId OpeningId { get; set; }
            public double DistanceToBoundary { get; set; }
            public double DistanceToBuilding { get; set; }
            public CheckStatus BoundaryStatus { get; set; }
            public CheckStatus BuildingStatus { get; set; }
            public CheckStatus OverallStatus { get; set; }
            public string BoundaryMessage { get; set; }
            public string BuildingMessage { get; set; }
        }

        public class Article110Result
        {
            public ElementId OpeningId { get; set; }
            public double DistanceToBoundary { get; set; }
            public double DistanceToBuilding { get; set; }
            public double RequiredFireRating { get; set; }
            public CheckStatus BoundaryFireStatus { get; set; }
            public CheckStatus BuildingFireStatus { get; set; }
            public CheckStatus OverallStatus { get; set; }
            public string BoundaryFireMessage { get; set; }
            public string BuildingFireMessage { get; set; }
        }

        public enum CheckStatus
        {
            Pass,
            Warning,
            Fail,
            NotChecked,
            NotApplicable
        }

        #endregion
    }
}
