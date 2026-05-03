using System;
using Autodesk.Revit.DB;
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
        #region 視圖 CropBox 操作

        /// <summary>
        /// 將指定視圖的 CropBox 對齊到目標元素的 BoundingBox（保留 Z 軸深度與 Transform）
        /// </summary>
        private object AlignViewCropBoxToElement(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            IdType elementId = parameters["elementId"]?.Value<IdType>() ?? 0;
            if (elementId == 0)
                throw new Exception("必須指定 elementId");

            IdType viewIdParam = parameters["viewId"]?.Value<IdType>() ?? 0;
            double paddingMm = parameters["padding_mm"]?.Value<double>() ?? 0;
            double paddingFeet = paddingMm / 304.8;

            View view = viewIdParam == 0
                ? doc.ActiveView
                : doc.GetElement(viewIdParam.ToElementId()) as View;
            if (view == null)
                throw new Exception($"找不到視圖 ID={viewIdParam}");

            Element element = doc.GetElement(elementId.ToElementId());
            if (element == null)
                throw new Exception($"找不到元素 ID={elementId}");

            BoundingBoxXYZ elemBbox = element.get_BoundingBox(view);
            if (elemBbox == null)
                throw new Exception($"元素 ID={elementId} 在視圖 '{view.Name}' 中沒有有效的 BoundingBox");

            BoundingBoxXYZ oldCropBox = view.CropBox;
            Transform cropTransform = oldCropBox.Transform;

            XYZ elemMinLocal = cropTransform.Inverse.OfPoint(elemBbox.Min);
            XYZ elemMaxLocal = cropTransform.Inverse.OfPoint(elemBbox.Max);

            double minX = Math.Min(elemMinLocal.X, elemMaxLocal.X) - paddingFeet;
            double maxX = Math.Max(elemMinLocal.X, elemMaxLocal.X) + paddingFeet;
            double minY = Math.Min(elemMinLocal.Y, elemMaxLocal.Y) - paddingFeet;
            double maxY = Math.Max(elemMinLocal.Y, elemMaxLocal.Y) + paddingFeet;

            XYZ newMin = new XYZ(minX, minY, oldCropBox.Min.Z);
            XYZ newMax = new XYZ(maxX, maxY, oldCropBox.Max.Z);

            BoundingBoxXYZ newCropBox = new BoundingBoxXYZ
            {
                Min = newMin,
                Max = newMax,
                Transform = cropTransform
            };

            using (Transaction trans = TransactionHelper.Begin(doc, "對齊 CropBox 到元素"))
            {
                trans.Start();
                view.CropBoxActive = true;
                view.CropBoxVisible = true;
                view.CropBox = newCropBox;
                trans.Commit();
            }

            return new
            {
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                ElementId = elementId,
                ElementCategory = element.Category?.Name ?? "Unknown",
                Padding_mm = paddingMm,
                OldCropBox_mm = new
                {
                    Min = new { x = oldCropBox.Min.X * 304.8, y = oldCropBox.Min.Y * 304.8, z = oldCropBox.Min.Z * 304.8 },
                    Max = new { x = oldCropBox.Max.X * 304.8, y = oldCropBox.Max.Y * 304.8, z = oldCropBox.Max.Z * 304.8 },
                    Width = (oldCropBox.Max.X - oldCropBox.Min.X) * 304.8,
                    Height = (oldCropBox.Max.Y - oldCropBox.Min.Y) * 304.8
                },
                NewCropBox_mm = new
                {
                    Min = new { x = newMin.X * 304.8, y = newMin.Y * 304.8, z = newMin.Z * 304.8 },
                    Max = new { x = newMax.X * 304.8, y = newMax.Y * 304.8, z = newMax.Z * 304.8 },
                    Width = (newMax.X - newMin.X) * 304.8,
                    Height = (newMax.Y - newMin.Y) * 304.8
                }
            };
        }

        /// <summary>
        /// 平移指定視圖的 CropBox（在 CropBox 自身座標系中位移 dx, dy）
        /// </summary>
        private object ShiftViewCropBox(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            IdType viewIdParam = parameters["viewId"]?.Value<IdType>() ?? 0;
            double dxMm = parameters["dx_mm"]?.Value<double>() ?? 0;
            double dyMm = parameters["dy_mm"]?.Value<double>() ?? 0;

            View view = viewIdParam == 0
                ? doc.ActiveView
                : doc.GetElement(viewIdParam.ToElementId()) as View;
            if (view == null)
                throw new Exception($"找不到視圖 ID={viewIdParam}");

            double dxFeet = dxMm / 304.8;
            double dyFeet = dyMm / 304.8;

            BoundingBoxXYZ oldCropBox = view.CropBox;
            XYZ newMin = new XYZ(oldCropBox.Min.X + dxFeet, oldCropBox.Min.Y + dyFeet, oldCropBox.Min.Z);
            XYZ newMax = new XYZ(oldCropBox.Max.X + dxFeet, oldCropBox.Max.Y + dyFeet, oldCropBox.Max.Z);

            BoundingBoxXYZ newCropBox = new BoundingBoxXYZ
            {
                Min = newMin,
                Max = newMax,
                Transform = oldCropBox.Transform
            };

            using (Transaction trans = TransactionHelper.Begin(doc, "平移 CropBox"))
            {
                trans.Start();
                view.CropBoxActive = true;
                view.CropBoxVisible = true;
                view.CropBox = newCropBox;
                trans.Commit();
            }

            return new
            {
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                Dx_mm = dxMm,
                Dy_mm = dyMm,
                NewCropBox_mm = new
                {
                    Min = new { x = newMin.X * 304.8, y = newMin.Y * 304.8 },
                    Max = new { x = newMax.X * 304.8, y = newMax.Y * 304.8 }
                }
            };
        }

        #endregion
    }
}
