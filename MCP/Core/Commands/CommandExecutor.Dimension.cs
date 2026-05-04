using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
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
        #region 尺寸標註

        /// <summary>
        /// 使用射線偵測建立尺寸標註
        /// </summary>
        private object CreateDimensionByRay(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            IdType viewId = parameters["viewId"]?.Value<IdType>() ?? 0;
            double originX = parameters["origin"]?["x"]?.Value<double>() ?? 0;
            double originY = parameters["origin"]?["y"]?.Value<double>() ?? 0;
            double originZ = parameters["origin"]?["z"]?.Value<double>() ?? 0;
            double dirX = parameters["direction"]?["x"]?.Value<double>() ?? 0;
            double dirY = parameters["direction"]?["y"]?.Value<double>() ?? 0;
            double dirZ = parameters["direction"]?["z"]?.Value<double>() ?? 0;
            double counterDirX = parameters["counterDirection"]?["x"]?.Value<double>() ?? -dirX;
            double counterDirY = parameters["counterDirection"]?["y"]?.Value<double>() ?? -dirY;
            double counterDirZ = parameters["counterDirection"]?["z"]?.Value<double>() ?? -dirZ;

            View view = doc.GetElement(viewId.ToElementId()) as View;
            if (view == null)
                throw new Exception($"找不到視圖 ID: {viewId}");

            List<View3D> available3DViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v => !v.IsTemplate)
                .OrderBy(v => v.IsSectionBoxActive ? 1 : 0)
                .ToList();

            if (available3DViews.Count == 0)
                throw new Exception("專案中沒有可用的 3D 視圖");

            using (Transaction trans = TransactionHelper.Begin(doc, "建立射線標註"))
            {
                trans.Start();

                XYZ origin = new XYZ(originX / 304.8, originY / 304.8, originZ / 304.8);
                XYZ direction = new XYZ(dirX, dirY, dirZ).Normalize();
                XYZ counterDirection = new XYZ(counterDirX, counterDirY, counterDirZ).Normalize();

                Reference ref1 = null;
                Reference ref2 = null;

                foreach (View3D view3D in available3DViews)
                {
                    ElementFilter filter = new ElementMulticategoryFilter(new List<BuiltInCategory>
                    {
                        BuiltInCategory.OST_Walls,
                        BuiltInCategory.OST_StructuralColumns,
                        BuiltInCategory.OST_Columns
                    });
                    ReferenceIntersector iterator = new ReferenceIntersector(filter, FindReferenceTarget.Face, view3D);

                    ReferenceWithContext ref1Context = iterator.FindNearest(origin, direction);
                    ReferenceWithContext ref2Context = iterator.FindNearest(origin, counterDirection);

                    if (ref1Context != null && ref2Context != null)
                    {
                        ref1 = ref1Context.GetReference();
                        ref2 = ref2Context.GetReference();
                        break;
                    }
                }

                if (ref1 == null || ref2 == null)
                    throw new Exception("所有3D視圖都無法偵測到足夠的邊界，請確認房間周圍是否有完整的牆面");

                XYZ point1 = ref1.GlobalPoint;
                XYZ point2 = ref2.GlobalPoint;

                XYZ dimDir = direction.CrossProduct(XYZ.BasisZ);
                if (dimDir.IsZeroLength()) dimDir = XYZ.BasisX;

                double offset = 500 / 304.8;
                XYZ dimLineStart = point1.Add(dimDir.Multiply(offset));
                XYZ dimLineEnd = point2.Add(dimDir.Multiply(offset));
                Line dimLine = Line.CreateBound(dimLineStart, dimLineEnd);

                ReferenceArray refArray = new ReferenceArray();
                refArray.Append(ref1);
                refArray.Append(ref2);

                Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);

                trans.Commit();

                double dimValue = dim.Value.HasValue ? dim.Value.Value * 304.8 : 0;

                return new
                {
                    DimensionId = dim.Id.GetIdValue(),
                    Value = Math.Round(dimValue, 2),
                    Unit = "mm"
                };
            }
        }

        /// <summary>
        /// 使用房間邊界框標註
        /// </summary>
        private object CreateDimensionByBoundingBox(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            IdType viewId = parameters["viewId"]?.Value<IdType>() ?? 0;
            IdType roomId = parameters["roomId"]?.Value<IdType>() ?? 0;
            string axis = parameters["axis"]?.Value<string>() ?? "X";
            double offset = parameters["offset"]?.Value<double>() ?? 500;

            View view = doc.GetElement(viewId.ToElementId()) as View;
            if (view == null)
                throw new Exception($"找不到視圖 ID: {viewId}");

            Room room = doc.GetElement(roomId.ToElementId()) as Room;
            if (room == null)
                throw new Exception($"找不到房間 ID: {roomId}");

            BoundingBoxXYZ bbox = room.get_BoundingBox(view);
            if (bbox == null)
                throw new Exception($"房間 {room.Name} 沒有邊界框");

            using (Transaction trans = TransactionHelper.Begin(doc, "建立邊界框標註"))
            {
                trans.Start();

                XYZ min = bbox.Min;
                XYZ max = bbox.Max;
                double offsetFeet = offset / 304.8;

                XYZ point1, point2, dimLineStart, dimLineEnd;

                if (axis.ToUpper() == "X")
                {
                    double centerY = (min.Y + max.Y) / 2;
                    point1 = new XYZ(min.X, centerY, min.Z);
                    point2 = new XYZ(max.X, centerY, min.Z);
                    dimLineStart = new XYZ(min.X, centerY + offsetFeet, min.Z);
                    dimLineEnd = new XYZ(max.X, centerY + offsetFeet, min.Z);
                }
                else
                {
                    double centerX = (min.X + max.X) / 2;
                    point1 = new XYZ(centerX, min.Y, min.Z);
                    point2 = new XYZ(centerX, max.Y, min.Z);
                    dimLineStart = new XYZ(centerX + offsetFeet, min.Y, min.Z);
                    dimLineEnd = new XYZ(centerX + offsetFeet, max.Y, min.Z);
                }

                Line dimLine = Line.CreateBound(dimLineStart, dimLineEnd);

                double lineLength = 1.0;
                XYZ perpDir = (axis.ToUpper() == "X") ? XYZ.BasisY : XYZ.BasisX;

                DetailCurve dc1 = doc.Create.NewDetailCurve(view, Line.CreateBound(
                    point1.Subtract(perpDir.Multiply(lineLength / 2)),
                    point1.Add(perpDir.Multiply(lineLength / 2))));
                DetailCurve dc2 = doc.Create.NewDetailCurve(view, Line.CreateBound(
                    point2.Subtract(perpDir.Multiply(lineLength / 2)),
                    point2.Add(perpDir.Multiply(lineLength / 2))));

                ReferenceArray refArray = new ReferenceArray();
                refArray.Append(dc1.GeometryCurve.Reference);
                refArray.Append(dc2.GeometryCurve.Reference);

                Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);

                trans.Commit();

                double dimValue = dim.Value.HasValue ? dim.Value.Value * 304.8 : 0;

                return new
                {
                    DimensionId = dim.Id.GetIdValue(),
                    Value = Math.Round(dimValue, 2),
                    Unit = "mm",
                    Axis = axis,
                    RoomName = room.Name
                };
            }
        }

        /// <summary>
        /// 批次自動標註牆段尺寸。
        /// 三種模式：
        ///   per_wall      = 每道牆一個長度標註（垂直牆方向偏移）
        ///   chained       = 同列／同排共線牆串接成一條 string dimension
        ///   overall_bbox  = 兩條外圍標註（top 邊沿 X 軸串、right 邊沿 Y 軸串）
        /// 直接抓牆面 reference（exterior/interior side face 或 end cap face），
        /// 牆若被刪除 / 移動 Dimension 會自動更新或消失（與一般 Revit 標註行為一致）。
        /// </summary>
        private object AutoDimensionWalls(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType viewId = parameters["viewId"]?.Value<IdType>() ?? 0;
            string mode = (parameters["mode"]?.Value<string>() ?? "overall_bbox").ToLowerInvariant();
            double offsetMm = parameters["offsetMm"]?.Value<double>() ?? 1500;

            View view = doc.GetElement(viewId.ToElementId()) as View;
            if (view == null)
                throw new Exception($"找不到視圖 ID: {viewId}");
            if (!(view is ViewPlan))
                throw new Exception($"視圖 {view.Name} 不是平面圖（auto_dimension_walls 只支援 ViewPlan）");

            // 1) 收集牆
            var walls = new List<Wall>();
            var idsParam = parameters["wallIds"] as JArray;
            if (idsParam != null && idsParam.Count > 0)
            {
                foreach (var idTok in idsParam)
                {
                    IdType wid = idTok.Value<IdType>();
                    if (doc.GetElement(wid.ToElementId()) is Wall w)
                        walls.Add(w);
                }
            }
            else
            {
                walls = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>().ToList();
            }
            if (walls.Count == 0)
                throw new Exception("找不到任何牆（請確認 wallIds 或 view 範圍）");

            var wallLines = new List<(Wall wall, Line line)>();
            int nonLinearSkipped = 0;
            foreach (var w in walls)
            {
                if (w.Location is LocationCurve lc && lc.Curve is Line ln)
                    wallLines.Add((w, ln));
                else
                    nonLinearSkipped++;
            }
            if (wallLines.Count == 0)
                throw new Exception("沒有直線牆可標註（曲線牆暫不支援）");

            double offsetFt = offsetMm / 304.8;
            var created = new List<object>();

            using (Transaction trans = TransactionHelper.Begin(doc, "auto dimension walls"))
            {
                trans.Start();

                switch (mode)
                {
                    case "per_wall":
                        foreach (var (wall, line) in wallLines)
                        {
                            var info = CreatePerWallDim(doc, view, wall, line, offsetFt);
                            if (info != null) created.Add(info);
                        }
                        break;
                    case "chained":
                        created.AddRange(CreateChainedWallDims(doc, view, wallLines, offsetFt));
                        break;
                    case "overall_bbox":
                    default:
                        created.AddRange(CreateOverallBboxDims(doc, view, wallLines, offsetFt));
                        if (mode != "overall_bbox" && mode != "per_wall" && mode != "chained")
                            mode = "overall_bbox(fallback)";
                        break;
                }

                trans.Commit();
            }

            return new
            {
                Mode = mode,
                ViewId = viewId,
                ViewName = view.Name,
                WallCount = wallLines.Count,
                NonLinearSkipped = nonLinearSkipped,
                DimensionCount = created.Count,
                Dimensions = created
            };
        }

        private object CreatePerWallDim(Document doc, View view, Wall wall, Line wallLine, double offsetFt)
        {
            XYZ p0 = wallLine.GetEndPoint(0);
            XYZ p1 = wallLine.GetEndPoint(1);
            XYZ dir = wallLine.Direction.Normalize();
            XYZ perp = new XYZ(-dir.Y, dir.X, 0);
            if (perp.IsZeroLength()) perp = XYZ.BasisY;
            else perp = perp.Normalize();

            // 抓牆兩端的 end-cap face references（normal 平行於牆方向）。
            // 牆移動時 dimension 會跟著走；牆刪掉 dimension 自動消失。
            Reference ref0 = GetWallEndFaceReference(wall, dir, p0);
            Reference ref1 = GetWallEndFaceReference(wall, dir, p1);
            if (ref0 == null || ref1 == null)
                return null; // 拿不到 face ref（罕見，可能牆 geometry 異常），略過此牆

            var refArr = new ReferenceArray();
            refArr.Append(ref0);
            refArr.Append(ref1);

            Line dimLine = Line.CreateBound(
                p0.Add(perp.Multiply(offsetFt)),
                p1.Add(perp.Multiply(offsetFt)));

            Dimension dim = doc.Create.NewDimension(view, dimLine, refArr);
            double mm = dim.Value.HasValue ? Math.Round(dim.Value.Value * 304.8, 0) : 0;

            return new
            {
                WallId = wall.Id.GetIdValue(),
                DimensionId = dim.Id.GetIdValue(),
                ValueMm = mm
            };
        }

        /// <summary>
        /// 取得牆某一端的 end-cap PlanarFace Reference（normal 平行於 wallDir）。
        /// 找不到時回傳 null（呼叫端負責處理）。
        /// </summary>
        private Reference GetWallEndFaceReference(Wall wall, XYZ wallDir, XYZ endPoint)
        {
            Options opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };
            GeometryElement geomElem = wall.get_Geometry(opt);
            if (geomElem == null) return null;

            Reference best = null;
            double bestDist = double.MaxValue;

            foreach (GeometryObject go in geomElem)
            {
                Solid solid = go as Solid;
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face f in solid.Faces)
                {
                    PlanarFace pf = f as PlanarFace;
                    if (pf == null) continue;
                    XYZ n = pf.FaceNormal;
                    // 端面 normal 應平行於牆方向（dot product 接近 ±1）
                    double dot = Math.Abs(n.DotProduct(wallDir));
                    if (dot < 0.99) continue;
                    // 取面中心離 endPoint 最近者
                    XYZ c = pf.Origin;
                    double d = c.DistanceTo(endPoint);
                    if (d < bestDist && pf.Reference != null)
                    {
                        bestDist = d;
                        best = pf.Reference;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// 取得牆的 side face Reference（exterior 優先，沒有則 interior）。
        /// 用於 chained / overall_bbox 模式 — side face 的 normal 垂直於牆方向，
        /// 對沿座標軸串接的 dimension 是正確的 reference。
        /// </summary>
        private Reference GetWallSideFaceReference(Wall wall)
        {
            try
            {
                IList<Reference> ext = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
                if (ext != null && ext.Count > 0) return ext.First();
            }
            catch { }
            try
            {
                IList<Reference> intr = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);
                if (intr != null && intr.Count > 0) return intr.First();
            }
            catch { }
            return null;
        }

        private List<object> CreateOverallBboxDims(Document doc, View view, List<(Wall wall, Line line)> walls, double offsetFt)
        {
            var result = new List<object>();
            var allPts = new List<XYZ>();
            foreach (var (_, ln) in walls)
            {
                allPts.Add(ln.GetEndPoint(0));
                allPts.Add(ln.GetEndPoint(1));
            }
            double maxX = allPts.Max(p => p.X);
            double maxY = allPts.Max(p => p.Y);

            // X 軸總尺寸：把所有 wall face 投影到 X，去重後串接
            var xRefs = CollectAxisReferences(walls, axisIsX: true);
            if (xRefs.Count >= 2)
            {
                double dimY = maxY + offsetFt;
                Dimension dim = CreateDimFromRefs(doc, view, xRefs, dimY, axisIsX: true);
                if (dim != null)
                {
                    double totalMm = Math.Round((xRefs.Last().coord - xRefs.First().coord) * 304.8, 0);
                    result.Add(new
                    {
                        Axis = "X",
                        Side = "top",
                        Segments = xRefs.Count - 1,
                        DimensionId = dim.Id.GetIdValue(),
                        TotalMm = totalMm
                    });
                }
            }

            // Y 軸總尺寸
            var yRefs = CollectAxisReferences(walls, axisIsX: false);
            if (yRefs.Count >= 2)
            {
                double dimX = maxX + offsetFt;
                Dimension dim = CreateDimFromRefs(doc, view, yRefs, dimX, axisIsX: false);
                if (dim != null)
                {
                    double totalMm = Math.Round((yRefs.Last().coord - yRefs.First().coord) * 304.8, 0);
                    result.Add(new
                    {
                        Axis = "Y",
                        Side = "right",
                        Segments = yRefs.Count - 1,
                        DimensionId = dim.Id.GetIdValue(),
                        TotalMm = totalMm
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// 收集所有 wall face references 投影到指定軸（X 或 Y），依 coord 排序去重（1mm 容差）。
        /// - 與 axis 平行的牆（沿軸延伸）：用兩端 end-cap face
        /// - 與 axis 垂直的牆：用 side face（exterior 優先）
        /// </summary>
        private List<(double coord, Reference reference)> CollectAxisReferences(
            List<(Wall wall, Line line)> walls, bool axisIsX)
        {
            var bag = new List<(double coord, Reference reference)>();

            foreach (var (wall, line) in walls)
            {
                XYZ dir = line.Direction.Normalize();
                bool wallAlongAxis = axisIsX
                    ? Math.Abs(dir.Y) < 0.01
                    : Math.Abs(dir.X) < 0.01;

                if (wallAlongAxis)
                {
                    XYZ p0 = line.GetEndPoint(0);
                    XYZ p1 = line.GetEndPoint(1);
                    Reference r0 = GetWallEndFaceReference(wall, dir, p0);
                    Reference r1 = GetWallEndFaceReference(wall, dir, p1);
                    if (r0 != null) bag.Add((axisIsX ? p0.X : p0.Y, r0));
                    if (r1 != null) bag.Add((axisIsX ? p1.X : p1.Y, r1));
                }
                else
                {
                    Reference sideRef = GetWallSideFaceReference(wall);
                    if (sideRef == null) continue;
                    double coord = axisIsX
                        ? (line.GetEndPoint(0).X + line.GetEndPoint(1).X) / 2
                        : (line.GetEndPoint(0).Y + line.GetEndPoint(1).Y) / 2;
                    bag.Add((coord, sideRef));
                }
            }

            var deduped = new List<(double coord, Reference reference)>();
            foreach (var item in bag.OrderBy(b => b.coord))
            {
                if (deduped.Any(d => Math.Abs(d.coord - item.coord) < 1.0 / 304.8)) continue;
                deduped.Add(item);
            }
            return deduped;
        }

        /// <summary>
        /// 用 reference 串建立 Dimension。dimLine 沿指定軸，offsetCoord 是垂直軸的位置。
        /// </summary>
        private Dimension CreateDimFromRefs(
            Document doc, View view,
            List<(double coord, Reference reference)> refs,
            double offsetCoord, bool axisIsX)
        {
            if (refs == null || refs.Count < 2) return null;
            var refArr = new ReferenceArray();
            foreach (var (_, r) in refs) refArr.Append(r);

            XYZ start = axisIsX ? new XYZ(refs.First().coord, offsetCoord, 0)
                                : new XYZ(offsetCoord, refs.First().coord, 0);
            XYZ end = axisIsX ? new XYZ(refs.Last().coord, offsetCoord, 0)
                              : new XYZ(offsetCoord, refs.Last().coord, 0);
            Line dimLine = Line.CreateBound(start, end);

            try { return doc.Create.NewDimension(view, dimLine, refArr); }
            catch { return null; }
        }

        private List<object> CreateChainedWallDims(Document doc, View view, List<(Wall wall, Line line)> walls, double offsetFt)
        {
            var result = new List<object>();

            // 分類：水平牆（dir.Y≈0）→ 沿 X 串接；垂直牆（dir.X≈0）→ 沿 Y 串接
            var horizontal = walls.Where(w => Math.Abs(w.line.Direction.Y) < 0.01).ToList();
            var vertical = walls.Where(w => Math.Abs(w.line.Direction.X) < 0.01).ToList();

            // 水平牆：依 Y 分組（同列）→ 每組用 end-cap face 串成 X 軸 dimension
            var hGroups = GroupWallsByCoord(horizontal, w => (w.line.GetEndPoint(0).Y + w.line.GetEndPoint(1).Y) / 2);
            foreach (var g in hGroups)
            {
                var refs = CollectAxisReferences(g, axisIsX: true);
                if (refs.Count < 2) continue;

                double avgY = g.Average(w => (w.line.GetEndPoint(0).Y + w.line.GetEndPoint(1).Y) / 2);
                double dimY = avgY + offsetFt;
                Dimension dim = CreateDimFromRefs(doc, view, refs, dimY, axisIsX: true);
                if (dim == null) continue;
                result.Add(new
                {
                    Axis = "X",
                    BaseYMm = Math.Round(avgY * 304.8, 0),
                    WallCount = g.Count,
                    Segments = refs.Count - 1,
                    DimensionId = dim.Id.GetIdValue()
                });
            }

            // 垂直牆：依 X 分組（同排）→ 每組用 end-cap face 串成 Y 軸 dimension
            var vGroups = GroupWallsByCoord(vertical, w => (w.line.GetEndPoint(0).X + w.line.GetEndPoint(1).X) / 2);
            foreach (var g in vGroups)
            {
                var refs = CollectAxisReferences(g, axisIsX: false);
                if (refs.Count < 2) continue;

                double avgX = g.Average(w => (w.line.GetEndPoint(0).X + w.line.GetEndPoint(1).X) / 2);
                double dimX = avgX + offsetFt;
                Dimension dim = CreateDimFromRefs(doc, view, refs, dimX, axisIsX: false);
                if (dim == null) continue;
                result.Add(new
                {
                    Axis = "Y",
                    BaseXMm = Math.Round(avgX * 304.8, 0),
                    WallCount = g.Count,
                    Segments = refs.Count - 1,
                    DimensionId = dim.Id.GetIdValue()
                });
            }

            return result;
        }

        private List<List<(Wall wall, Line line)>> GroupWallsByCoord(
            List<(Wall wall, Line line)> walls,
            Func<(Wall wall, Line line), double> coord)
        {
            const double tolFt = 100.0 / 304.8; // 100mm
            var groups = new List<(double Coord, List<(Wall wall, Line line)> Items)>();
            foreach (var w in walls)
            {
                double c = coord(w);
                int idx = groups.FindIndex(g => Math.Abs(g.Coord - c) < tolFt);
                if (idx >= 0)
                    groups[idx].Items.Add(w);
                else
                    groups.Add((c, new List<(Wall, Line)> { w }));
            }
            return groups.Select(g => g.Items).ToList();
        }

        #endregion
    }
}
