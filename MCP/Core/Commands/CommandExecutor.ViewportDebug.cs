using System;
using System.Collections.Generic;
using System.Linq;
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
        #region Viewport 診斷工具

        /// <summary>
        /// 診斷工具: dump 一個 viewport 的所有座標資料 (boxOutline, cropbox, view origin, scale, etc.)
        /// 用於反推 Revit cropbox <-> viewport 座標映射規則
        /// </summary>
        private object DebugViewportGeometry(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            IdType viewportId = parameters["viewportId"]?.Value<IdType>() ?? 0;
            IdType viewId = parameters["viewId"]?.Value<IdType>() ?? 0;

            Viewport vp = null;
            if (viewportId != 0)
            {
                vp = doc.GetElement(viewportId.ToElementId()) as Viewport;
            }
            else if (viewId != 0)
            {
                // 找對應 view 的 viewport
                vp = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .FirstOrDefault(v => v.ViewId.GetIdValue() == viewId);
            }
            else
            {
                throw new Exception("必須指定 viewportId 或 viewId");
            }

            if (vp == null)
                throw new Exception("找不到指定的 viewport");

            View view = doc.GetElement(vp.ViewId) as View;
            ViewSheet sheet = doc.GetElement(vp.SheetId) as ViewSheet;

            // Viewport 在 sheet 上的位置 (feet → mm)
            XYZ boxCenter = vp.GetBoxCenter();
            Outline boxOutline = vp.GetBoxOutline();
            XYZ boxMin = boxOutline.MinimumPoint;
            XYZ boxMax = boxOutline.MaximumPoint;

            object labelOutlineInfo = null;
            try
            {
                Outline labelOutline = vp.GetLabelOutline();
                if (labelOutline != null)
                {
                    labelOutlineInfo = new
                    {
                        MinMm = new { X = labelOutline.MinimumPoint.X * 304.8, Y = labelOutline.MinimumPoint.Y * 304.8 },
                        MaxMm = new { X = labelOutline.MaximumPoint.X * 304.8, Y = labelOutline.MaximumPoint.Y * 304.8 }
                    };
                }
            }
            catch (Exception ex)
            {
                labelOutlineInfo = new { Error = ex.Message };
            }

            // View 資訊
            XYZ viewOrigin = view.Origin;
            int viewScale = view.Scale;

            // CropBox (model 座標 / cropbox local 座標, 需要 Transform 才知道哪個)
            BoundingBoxXYZ cb = view.CropBox;
            object cropBoxInfo = null;
            object cropBoxTransformInfo = null;
            object cropBoxCenterInModel = null;
            object cropBoxSizeOnSheet = null;
            if (cb != null)
            {
                cropBoxInfo = new
                {
                    Min = new { cb.Min.X, cb.Min.Y, cb.Min.Z },
                    Max = new { cb.Max.X, cb.Max.Y, cb.Max.Z },
                    SizeFt = new
                    {
                        Width = cb.Max.X - cb.Min.X,
                        Height = cb.Max.Y - cb.Min.Y,
                        Depth = cb.Max.Z - cb.Min.Z
                    }
                };

                if (cb.Transform != null)
                {
                    cropBoxTransformInfo = new
                    {
                        Origin = new { cb.Transform.Origin.X, cb.Transform.Origin.Y, cb.Transform.Origin.Z },
                        BasisX = new { cb.Transform.BasisX.X, cb.Transform.BasisX.Y, cb.Transform.BasisX.Z },
                        BasisY = new { cb.Transform.BasisY.X, cb.Transform.BasisY.Y, cb.Transform.BasisY.Z },
                        BasisZ = new { cb.Transform.BasisZ.X, cb.Transform.BasisZ.Y, cb.Transform.BasisZ.Z },
                        IsIdentity = cb.Transform.IsIdentity
                    };

                    XYZ cropCenterLocal = new XYZ((cb.Min.X + cb.Max.X) / 2, (cb.Min.Y + cb.Max.Y) / 2, (cb.Min.Z + cb.Max.Z) / 2);
                    XYZ cropCenterModel = cb.Transform.OfPoint(cropCenterLocal);
                    cropBoxCenterInModel = new
                    {
                        Local = new { cropCenterLocal.X, cropCenterLocal.Y, cropCenterLocal.Z },
                        Model = new { cropCenterModel.X, cropCenterModel.Y, cropCenterModel.Z }
                    };
                }

                double s = viewScale > 0 ? viewScale : 1;
                cropBoxSizeOnSheet = new
                {
                    WidthMm = (cb.Max.X - cb.Min.X) / s * 304.8,
                    HeightMm = (cb.Max.Y - cb.Min.Y) / s * 304.8
                };
            }

            // View Outline (BoundingBoxUV in view's UV space)
            object viewOutlineInfo = null;
            try
            {
                BoundingBoxUV vo = view.Outline;
                if (vo != null)
                {
                    viewOutlineInfo = new
                    {
                        Min = new { U = vo.Min.U, V = vo.Min.V },
                        Max = new { U = vo.Max.U, V = vo.Max.V }
                    };
                }
            }
            catch (Exception ex)
            {
                viewOutlineInfo = new { Error = ex.Message };
            }

            return new
            {
                ViewportId = vp.Id.GetIdValue(),
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                ViewType = view.ViewType.ToString(),
                SheetNumber = sheet?.SheetNumber,
                SheetName = sheet?.Name,

                // Viewport on sheet (mm)
                ViewportBoxCenterMm = new { X = boxCenter.X * 304.8, Y = boxCenter.Y * 304.8 },
                ViewportBoxMinMm = new { X = boxMin.X * 304.8, Y = boxMin.Y * 304.8 },
                ViewportBoxMaxMm = new { X = boxMax.X * 304.8, Y = boxMax.Y * 304.8 },
                ViewportBoxWidthMm = (boxMax.X - boxMin.X) * 304.8,
                ViewportBoxHeightMm = (boxMax.Y - boxMin.Y) * 304.8,
                ViewportLabelOutline = labelOutlineInfo,

                // View properties
                ViewOriginFt = new { viewOrigin.X, viewOrigin.Y, viewOrigin.Z },
                ViewOriginMm = new { X = viewOrigin.X * 304.8, Y = viewOrigin.Y * 304.8, Z = viewOrigin.Z * 304.8 },
                ViewScale = viewScale,
                ViewRightDirection = new { view.RightDirection.X, view.RightDirection.Y, view.RightDirection.Z },
                ViewUpDirection = new { view.UpDirection.X, view.UpDirection.Y, view.UpDirection.Z },
                ViewDirection = new { view.ViewDirection.X, view.ViewDirection.Y, view.ViewDirection.Z },
                CropBoxActive = view.CropBoxActive,
                CropBoxVisible = view.CropBoxVisible,

                // CropBox details
                CropBox = cropBoxInfo,
                CropBoxTransform = cropBoxTransformInfo,
                CropBoxCenterInModel = cropBoxCenterInModel,
                CropBoxSizeOnSheet = cropBoxSizeOnSheet,

                // View outline (UV)
                ViewOutlineUV = viewOutlineInfo,

                Notes = new[]
                {
                    "ViewportBoxCenterMm: viewport.GetBoxCenter() — sheet 座標, mm",
                    "ViewportBoxMin/Max: viewport.GetBoxOutline() — bounding box of all visible elements on sheet",
                    "ViewOrigin: view.Origin — model 座標, view 的原點",
                    "CropBox.Min/Max: 在 cropbox local coords (用 Transform 換算到 model)",
                    "CropBoxCenterInModel.Local: cropbox 在 local 座標的中心",
                    "CropBoxCenterInModel.Model: cropbox 在 model 座標的中心 (Transform.OfPoint(local))",
                    "CropBoxSizeOnSheet: cropbox 大小換算到 sheet 上 (mm)",
                    "ViewOutlineUV: view 的 outline 在 view UV 座標"
                }
            };
        }

        #endregion
    }
}
