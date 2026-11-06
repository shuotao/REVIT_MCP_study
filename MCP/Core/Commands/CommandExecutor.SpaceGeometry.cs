using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Newtonsoft.Json.Linq;

#nullable disable

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    // =====================================================================================
    // Space（機電空間）幾何工具 —— Stage 1: get_space_centroid
    // -------------------------------------------------------------------------------------
    // repo 內在此之前完全沒有任何程式碼碰過 OST_MEPSpaces：get_room_info 只吃 OST_Rooms，
    // 傳 Space 進去會 cast 成 null 拋錯（見 domain/mep-space-demand-matrix.md 限制 L-A）。
    // 本檔提供每個 Space 的代表點座標，供後續自動放置元件（風口等）使用。
    //
    // 代表點決策順序（不得簡化）：
    //   1. LocationPoint（若存在）→ 以 Space.IsPointInSpace 驗證
    //   2. BoundingBox 中心（XY 取中點、Z 取 bb.Min.Z）→ 同樣驗證
    //   3. BoundingBox XY 範圍內 gridSamples × gridSamples 網格取樣，取第一個通過者
    //   4. 全部失敗 → SafePoint = null，PointSource = "None"，不得靜默 fallback。
    //
    // 凹形空間的 BoundingBox 中心可能落在空間外，這正是每一步都要驗證的原因。
    // 注意：Room.IsPointInRoom 與 Space.IsPointInSpace 是不同方法，SpatialElement 基底
    // 沒有共同的點在內測試，因此本檔案直接處理 Autodesk.Revit.DB.Mechanical.Space。
    // =====================================================================================

    public partial class CommandExecutor
    {
        /// <summary>
        /// 唯讀命令：回傳每個 Space 的代表點座標（mm），供自動放置元件使用。不需要 Transaction。
        /// </summary>
        private object GetSpaceCentroid(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            HashSet<IdType> requestedIds = null;
            if (parameters["spaceIds"] is JArray spaceIdsArray && spaceIdsArray.Count > 0)
            {
                requestedIds = new HashSet<IdType>();
                foreach (JToken idToken in spaceIdsArray)
                {
                    requestedIds.Add((IdType)idToken.Value<long>());
                }
            }

            string levelNameFilter = parameters["levelName"]?.Value<string>();

            int gridSamples = parameters["gridSamples"]?.Value<int>() ?? 5;
            if (gridSamples < 1) gridSamples = 1;
            if (gridSamples > 25) gridSamples = 25; // 上界保護：25×25=625 點/Space 已足夠涵蓋凹形空間，避免呼叫端誤帶超大值把 IsPointInSpace 呼叫次數炸開（N×N 次/Space）進而撞上 MCP-Server 30 秒 timeout。夾制後的實際值誠實反映在回傳的 GridSamples 欄位，不靜默。

            List<Space> spaces = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .Cast<Space>()
                .ToList();

            if (requestedIds != null)
            {
                spaces = spaces.Where(s => requestedIds.Contains(s.Id.GetIdValue())).ToList();
            }

            if (!string.IsNullOrWhiteSpace(levelNameFilter))
            {
                spaces = spaces
                    .Where(s =>
                    {
                        string lvlName = GetLevelName(doc, s.LevelId);
                        return !string.IsNullOrEmpty(lvlName) &&
                               (lvlName.Equals(levelNameFilter, StringComparison.OrdinalIgnoreCase) ||
                                lvlName.IndexOf(levelNameFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                    })
                    .ToList();
            }

            List<object> results = spaces
                .Select(space => BuildSpaceCentroidResult(doc, space, gridSamples))
                .ToList();

            return new
            {
                Success = true,
                Count = results.Count,
                GridSamples = gridSamples,
                Spaces = results
            };
        }

        private object BuildSpaceCentroidResult(Document doc, Space space, int gridSamples)
        {
            string levelName = GetLevelName(doc, space.LevelId);

            double areaFeet2 = 0;
            double volumeFeet3 = 0;
            try { areaFeet2 = space.Area; } catch { /* unplaced/unenclosed Space can throw */ }
            try { volumeFeet3 = space.Volume; } catch { /* same as above */ }

            // 拆成兩個獨立判定，避免「根本沒放置」與「已放置但未封閉（unenclosed）」被併成同一種狀態——
            // 這兩者在 MEP 前期模型的處置完全不同，後者常見且可修（補邊界即可），前者要先放置 Space。
            // IsPlaced 維持既有語意與既有值（= hasLocation && isEnclosed）不變；HasLocation / IsEnclosed 為新增的細分欄位。
            bool hasLocation = space.Location != null;
            bool isEnclosed = areaFeet2 > 1e-9;
            bool isPlaced = hasLocation && isEnclosed;

            double? boundedHeightMm = (areaFeet2 > 1e-9)
                ? (double?)Math.Round((volumeFeet3 / areaFeet2) * 304.8, 2)
                : null;

            double areaM2 = Math.Round(areaFeet2 * 0.092903, 4);
            double volumeM3 = Math.Round(volumeFeet3 * 0.0283168, 4);

            if (!isPlaced)
            {
                // 未放置或未封閉的 Space：不嘗試任何幾何運算，僅標示 IsPlaced = false。
                // 用 HasLocation / IsEnclosed 區分「根本沒放置」與「已放置但未封閉」兩種不同狀況。
                return new
                {
                    ElementId = space.Id.GetIdValue(),
                    Number = space.Number,
                    Name = space.Name,
                    Level = levelName,
                    IsPlaced = false,
                    HasLocation = hasLocation,
                    IsEnclosed = isEnclosed,
                    LocationPoint = (object)null,
                    BBoxCenter = (object)null,
                    SafePoint = (object)null,
                    PointSource = "None",
                    BoundedHeight = (object)null,
                    Area = areaM2,
                    Volume = volumeM3
                };
            }

            object locationPointResult = null;
            object bboxCenterResult = null;
            XYZ safePoint = null;
            string pointSource = "None";

            // 1) LocationPoint
            LocationPoint locationPoint = space.Location as LocationPoint;
            if (locationPoint != null)
            {
                XYZ pt = locationPoint.Point;
                locationPointResult = SpacePointToMm(pt);
                if (IsPointInSpaceSafe(space, pt))
                {
                    safePoint = pt;
                    pointSource = "LocationPoint";
                }
            }

            // 2) BoundingBox 中心（XY 取中點，Z 取 bb.Min.Z）
            BoundingBoxXYZ bbox = space.get_BoundingBox(null);
            if (bbox != null)
            {
                XYZ bboxCenter = new XYZ(
                    (bbox.Min.X + bbox.Max.X) / 2.0,
                    (bbox.Min.Y + bbox.Max.Y) / 2.0,
                    bbox.Min.Z);
                bboxCenterResult = SpacePointToMm(bboxCenter);

                if (safePoint == null && IsPointInSpaceSafe(space, bboxCenter))
                {
                    safePoint = bboxCenter;
                    pointSource = "BBoxCenter";
                }

                // 3) Grid sampling：BoundingBox XY 範圍內 gridSamples × gridSamples 網格，
                //    Z 用 bb.Min.Z，逐點驗證，取第一個通過者。
                if (safePoint == null)
                {
                    XYZ gridHit = FindSpaceGridSamplePoint(space, bbox, gridSamples);
                    if (gridHit != null)
                    {
                        safePoint = gridHit;
                        pointSource = "GridSample";
                    }
                }
            }

            // 4) 全部失敗 → safePoint 維持 null，pointSource 維持 "None"。不做靜默 fallback。

            return new
            {
                ElementId = space.Id.GetIdValue(),
                Number = space.Number,
                Name = space.Name,
                Level = levelName,
                IsPlaced = true,
                HasLocation = hasLocation,
                IsEnclosed = isEnclosed,
                LocationPoint = locationPointResult,
                BBoxCenter = bboxCenterResult,
                SafePoint = safePoint != null ? SpacePointToMm(safePoint) : null,
                PointSource = pointSource,
                BoundedHeight = boundedHeightMm,
                Area = areaM2,
                Volume = volumeM3
            };
        }

        /// <summary>
        /// Space.IsPointInSpace 的安全包裝：Revit 對特定退化幾何可能拋例外，視為「不通過」而非讓整支命令中斷。
        /// </summary>
        private bool IsPointInSpaceSafe(Space space, XYZ point)
        {
            try
            {
                return space.IsPointInSpace(point);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 在 Space 的 BoundingBox XY 範圍內做 gridSamples × gridSamples 網格取樣（Z 用 bb.Min.Z），
        /// 逐點呼叫 IsPointInSpace，回傳第一個通過者；全部不通過回傳 null。
        /// 取樣 pattern 對齊既有的 BuildPartitionRoomSamplePoints/AddPartitionRoomSample
        /// （CommandExecutor.PartitionTakeoff.cs）。
        /// </summary>
        private XYZ FindSpaceGridSamplePoint(Space space, BoundingBoxXYZ bbox, int gridSamples)
        {
            int n = Math.Max(1, gridSamples);
            double zFeet = bbox.Min.Z;

            for (int ix = 0; ix < n; ix++)
            {
                double xRatio = (n == 1) ? 0.5 : (double)ix / (n - 1);
                double x = bbox.Min.X + (bbox.Max.X - bbox.Min.X) * xRatio;

                for (int iy = 0; iy < n; iy++)
                {
                    double yRatio = (n == 1) ? 0.5 : (double)iy / (n - 1);
                    double y = bbox.Min.Y + (bbox.Max.Y - bbox.Min.Y) * yRatio;

                    XYZ candidate = new XYZ(x, y, zFeet);
                    if (IsPointInSpaceSafe(space, candidate))
                        return candidate;
                }
            }

            return null;
        }

        private static object SpacePointToMm(XYZ pt)
        {
            return new
            {
                X = Math.Round(pt.X * 304.8, 2),
                Y = Math.Round(pt.Y * 304.8, 2),
                Z = Math.Round(pt.Z * 304.8, 2)
            };
        }
    }
}
