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

            using (Transaction trans = new Transaction(doc, "建立射線標註"))
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

            using (Transaction trans = new Transaction(doc, "建立邊界框標註"))
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

        #endregion
    }
}
