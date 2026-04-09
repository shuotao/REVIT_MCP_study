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
        #region 牆類型與元素類型變更

        /// <summary>
        /// 取得所有牆類型
        /// </summary>
        private object GetWallTypes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string search = parameters["search"]?.Value<string>();

            var wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Select(wt => new
                {
                    ElementId = wt.Id.GetIdValue(),
                    Name = wt.Name
                });

            if (!string.IsNullOrEmpty(search))
                wallTypes = wallTypes.Where(wt => wt.Name.Contains(search));

            var result = wallTypes.OrderBy(wt => wt.Name).ToList();

            return new
            {
                Count = result.Count,
                WallTypes = result
            };
        }

        /// <summary>
        /// 變更元素類型
        /// </summary>
        private object ChangeElementType(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType? singleElementId = parameters["elementId"]?.Value<IdType>();
            var elementIdsArray = parameters["elementIds"] as JArray;
            IdType targetTypeId = parameters["typeId"]?.Value<IdType>() ?? 0;

            if (targetTypeId == 0)
                throw new Exception("必須提供目標類型的 Element ID (typeId)");

            ElementId newTypeId = targetTypeId.ToElementId();
            Element targetType = doc.GetElement(newTypeId);
            if (targetType == null)
                throw new Exception($"找不到目標類型 ID: {targetTypeId}");

            List<IdType> elementIds = new List<IdType>();
            if (singleElementId.HasValue)
                elementIds.Add(singleElementId.Value);
            if (elementIdsArray != null)
                elementIds.AddRange(elementIdsArray.Select(id => id.Value<IdType>()));

            if (elementIds.Count == 0)
                throw new Exception("請提供至少一個元素 ID");

            using (Transaction trans = new Transaction(doc, "變更元素類型"))
            {
                trans.Start();

                int successCount = 0;
                var errors = new List<string>();
                foreach (IdType id in elementIds)
                {
                    Element elem = doc.GetElement(id.ToElementId());
                    if (elem == null) continue;

#if REVIT2023_OR_GREATER
                    if (!elem.CanHaveTypeAssigned())
                    {
                        errors.Add($"元素 {id} 不支援類型變更");
                        continue;
                    }
#endif

                    try
                    {
                        elem.ChangeTypeId(newTypeId);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"元素 {id}: {ex.Message}");
                    }
                }

                trans.Commit();

                return new
                {
                    Success = true,
                    ChangedCount = successCount,
                    Errors = errors.Count > 0 ? errors : null,
                    Message = $"已成功變更 {successCount} 個元素的類型"
                };
            }
        }

        /// <summary>
        /// 取得線型樣式
        /// </summary>
        private object GetLineStyles()
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            var result = new List<Dictionary<string, object>>();

            var allStyles = new FilteredElementCollector(doc)
                .OfClass(typeof(GraphicsStyle))
                .ToElements();

            foreach (Element elem in allStyles)
            {
                GraphicsStyle gs = elem as GraphicsStyle;
                if (gs == null) continue;

                try
                {
                    Category cat = gs.GraphicsStyleCategory;
                    if (cat == null) continue;
                    Category parent = cat.Parent;
                    if (parent == null) continue;

                    if (parent.Name == "Lines" || parent.Name == "線" || parent.Name == "線條")
                    {
                        result.Add(new Dictionary<string, object>
                        {
                            { "Id", gs.Id.GetIdValue() },
                            { "Name", gs.Name }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"GetLineStyles 跳過樣式: {ex.Message}");
                }
            }

            return result.OrderBy(r => r["Name"].ToString()).ToList();
        }

        /// <summary>
        /// 追蹤樓梯幾何並偵測被遮擋的邊緣
        /// </summary>
        private object TraceStairGeometry(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            View view = _uiApp.ActiveUIDocument.ActiveView;

            XYZ viewDir = view.ViewDirection;
            XYZ origin = view.Origin;

            var runs = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_StairsRuns)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Architecture.StairsRun>()
                .ToList();

            var result = new List<object>();

            foreach (var run in runs)
            {
                Autodesk.Revit.DB.Architecture.Stairs parentStair = run.GetStairs();
                if (parentStair != null)
                {
                    ElementType stairType = doc.GetElement(parentStair.GetTypeId()) as ElementType;
                    if (stairType != null)
                    {
                        string familyName = stairType.FamilyName;
                        if (!familyName.Contains("組合") && !familyName.Contains("Assembled"))
                            continue;
                    }
                }

                Options opt = new Options { DetailLevel = ViewDetailLevel.Fine };
                GeometryElement geom = run.get_Geometry(opt);

                var hiddenLines = new List<object>();
                var allEdges = new List<StairEdgeData>();

                CollectStairEdges(geom, null, origin, viewDir, allEdges);

                if (allEdges.Count > 0)
                {
                    double minDepth = allEdges.Min(e => e.Depth);

                    if (minDepth <= 0.05) continue;

                    double depthTolerance = 2.5;
                    var firstRunEdges = allEdges.Where(e => e.Depth <= minDepth + depthTolerance && e.IsStepProfile).ToList();

                    foreach (var edge in firstRunEdges)
                    {
                        hiddenLines.Add(new
                        {
                            startX = Math.Round(edge.P0.X * 304.8, 2),
                            startY = Math.Round(edge.P0.Y * 304.8, 2),
                            startZ = Math.Round(edge.P0.Z * 304.8, 2),
                            endX = Math.Round(edge.P1.X * 304.8, 2),
                            endY = Math.Round(edge.P1.Y * 304.8, 2),
                            endZ = Math.Round(edge.P1.Z * 304.8, 2)
                        });
                    }
                }

                if (hiddenLines.Count > 0)
                {
                    result.Add(new
                    {
                        StairId = run.Id.GetIdValue(),
                        HiddenLines = hiddenLines,
                        TotalEdges = allEdges.Count,
                        FirstRunEdgesCount = hiddenLines.Count
                    });
                }
            }

            return result;
        }

        private void CollectStairEdges(GeometryElement gelem, Transform transform, XYZ origin, XYZ viewDir, List<StairEdgeData> allEdges)
        {
            if (gelem == null) return;
            foreach (GeometryObject obj in gelem)
            {
                if (obj is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Edge edge in solid.Edges)
                    {
                        Curve c = edge.AsCurve();
                        if (transform != null && !transform.IsIdentity)
                            c = c.CreateTransformed(transform);

                        if (c.Length < 0.01) continue;

                        XYZ p0 = c.GetEndPoint(0);
                        XYZ p1 = c.GetEndPoint(1);
                        XYZ mid = c.Evaluate(0.5, true);

                        double depth = (origin - mid).DotProduct(viewDir);
                        double d0 = (origin - p0).DotProduct(viewDir);
                        double d1 = (origin - p1).DotProduct(viewDir);
                        XYZ proj0 = p0 + viewDir * d0;
                        XYZ proj1 = p1 + viewDir * d1;
                        double projLen = proj0.DistanceTo(proj1);

                        if (projLen < 0.01) continue;

                        XYZ dir = (proj1 - proj0).Normalize();
                        bool isHorizontal = Math.Abs(dir.Z) < 0.1;
                        bool isVertical = Math.Abs(Math.Abs(dir.Z) - 1.0) < 0.1;
                        bool isStepProfile = isHorizontal || isVertical || (projLen < 0.65);

                        allEdges.Add(new StairEdgeData
                        {
                            Depth = depth,
                            Length = projLen,
                            IsStepProfile = isStepProfile,
                            P0 = p0,
                            P1 = p1
                        });
                    }
                }
                else if (obj is GeometryInstance instance)
                {
                    Transform currentTransform = transform != null ? transform.Multiply(instance.Transform) : instance.Transform;
                    CollectStairEdges(instance.SymbolGeometry, currentTransform, origin, viewDir, allEdges);
                }
            }
        }

        private class StairEdgeData
        {
            public double Depth;
            public double Length;
            public bool IsStepProfile;
            public XYZ P0;
            public XYZ P1;
        }

        #endregion
    }
}
