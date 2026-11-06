using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        #region get_door_window_coordinates

        // 一次撈全部門窗的座標清單：插入點 (LocationPoint) + boundingbox，
        // 支援主模型與連結模型。座標一律轉為 mm。
        private object GetDoorWindowCoordinates(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string category = parameters["category"]?.Value<string>()?.Trim() ?? "全部";
            string levelFilter = parameters["level"]?.Value<string>()?.Trim();
            bool includeLinks = parameters["includeLinks"]?.Value<bool>() ?? false;
            int maxCount = parameters["maxCount"]?.Value<int>() ?? 1000;

            // 決定要撈哪些品類
            var cats = new List<BuiltInCategory>();
            string lower = category.ToLowerInvariant();
            if (lower == "門" || lower == "doors" || lower == "door")
                cats.Add(BuiltInCategory.OST_Doors);
            else if (lower == "窗" || lower == "windows" || lower == "window")
                cats.Add(BuiltInCategory.OST_Windows);
            else
            {
                cats.Add(BuiltInCategory.OST_Doors);
                cats.Add(BuiltInCategory.OST_Windows);
            }

            var results = new List<object>();

            // 主模型
            foreach (var bic in cats)
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType();

                foreach (var el in collector)
                {
                    if (results.Count >= maxCount) break;
                    if (el is FamilyInstance fi)
                    {
                        var data = ExtractDoorWindowData(fi, doc, Transform.Identity, "主模型", levelFilter);
                        if (data != null) results.Add(data);
                    }
                }
            }

            // 連結模型
            if (includeLinks)
            {
                var links = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>();

                foreach (var link in links)
                {
                    var linkDoc = link.GetLinkDocument();
                    if (linkDoc == null) continue; // 未載入
                    var transform = link.GetTotalTransform();
                    string srcLabel = "連結:" + linkDoc.Title;

                    foreach (var bic in cats)
                    {
                        var lc = new FilteredElementCollector(linkDoc)
                            .OfCategory(bic)
                            .WhereElementIsNotElementType();

                        foreach (var el in lc)
                        {
                            if (results.Count >= maxCount) break;
                            if (el is FamilyInstance fi)
                            {
                                var data = ExtractDoorWindowData(fi, linkDoc, transform, srcLabel, levelFilter);
                                if (data != null) results.Add(data);
                            }
                        }
                    }
                }
            }

            return new
            {
                Success = true,
                Count = results.Count,
                Category = category,
                IncludeLinks = includeLinks,
                Elements = results
            };
        }

        // 擷取單一門窗的座標資料。ownerDoc = 元素所屬文件（主或連結）；
        // transform = 連結轉換（主模型傳 Identity）。回 null 表示被 levelFilter 濾掉。
        private object ExtractDoorWindowData(FamilyInstance fi, Document ownerDoc, Transform transform, string source, string levelFilter)
        {
            // 樓層名稱
            string levelName = "";
            var lvl = ownerDoc.GetElement(fi.LevelId) as Level;
            if (lvl != null) levelName = lvl.Name;

            if (!string.IsNullOrEmpty(levelFilter) && levelName != levelFilter)
                return null;

            // 插入點 (LocationPoint)，套用連結 transform
            double? locX = null, locY = null, locZ = null;
            bool hasLoc = false;
            if (fi.Location is LocationPoint lp && lp.Point != null)
            {
                var p = transform.OfPoint(lp.Point);
                locX = Math.Round(p.X * 304.8, 2);
                locY = Math.Round(p.Y * 304.8, 2);
                locZ = Math.Round(p.Z * 304.8, 2);
                hasLoc = true;
            }

            // BoundingBox（+ 中心點）
            object bboxObj;
            var bbox = fi.get_BoundingBox(null);
            if (bbox != null)
            {
                var min = transform.OfPoint(bbox.Min);
                var max = transform.OfPoint(bbox.Max);
                double cx = Math.Round((min.X + max.X) / 2 * 304.8, 2);
                double cy = Math.Round((min.Y + max.Y) / 2 * 304.8, 2);
                double cz = Math.Round((min.Z + max.Z) / 2 * 304.8, 2);
                bboxObj = new
                {
                    HasBoundingBox = true,
                    MinX = Math.Round(min.X * 304.8, 2),
                    MinY = Math.Round(min.Y * 304.8, 2),
                    MinZ = Math.Round(min.Z * 304.8, 2),
                    MaxX = Math.Round(max.X * 304.8, 2),
                    MaxY = Math.Round(max.Y * 304.8, 2),
                    MaxZ = Math.Round(max.Z * 304.8, 2),
                    CenterX = cx,
                    CenterY = cy,
                    CenterZ = cz
                };

                // 沒有 LocationPoint 時（例如某些幕牆嵌板類窗），用 bbox 中心當插入點 fallback
                if (!hasLoc)
                {
                    locX = cx; locY = cy; locZ = cz;
                }
            }
            else
            {
                bboxObj = new { HasBoundingBox = false };
            }

            // 朝向（面向向量），套用連結旋轉
            var facing = transform.OfVector(fi.FacingOrientation);

            // 宿主牆
            object hostId = null;
            if (fi.Host != null) hostId = fi.Host.Id.GetIdValue();

            // 標記
            string mark = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";

            // 族群 / 類型
            string family = fi.Symbol?.FamilyName ?? "";
            string typeName = fi.Symbol?.Name ?? "";

            return new
            {
                ElementId = fi.Id.GetIdValue(),
                Category = fi.Category?.Name ?? "",
                Family = family,
                Type = typeName,
                Mark = mark,
                Level = levelName,
                HostId = hostId,
                Source = source,
                LocationFromPoint = hasLoc,
                LocX = locX,
                LocY = locY,
                LocZ = locZ,
                FacingX = Math.Round(facing.X, 4),
                FacingY = Math.Round(facing.Y, 4),
                BoundingBox = bboxObj
            };
        }

        #endregion
    }
}
