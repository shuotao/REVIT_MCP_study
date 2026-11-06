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
        /// 將指定 2D 視圖的 CropBox 對齊到目標元素的 BoundingBox。
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

            // Backward-compatible bridge for MCP clients whose tool registry was
            // initialized before set_3d_section_box existed. The legacy command
            // name must still use the real View3D Section Box path in 3D; it must
            // never fall through to the CropBox implementation below.
            if (view is View3D)
                return Set3DSectionBox(parameters);

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
                ViewType = view.ViewType.ToString(),
                Mode = "CropBox",
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
        /// 將 3D 視圖的 Section Box 對齊到元素的完整模型座標 BoundingBox。
        /// 這是 3D 定位的唯一合法路徑；同時關閉 CropBox，避免雙重裁切。
        /// </summary>
        private object Set3DSectionBox(JObject parameters)
        {
            var uiDoc = _uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            IdType viewIdParam = parameters["viewId"]?.Value<IdType>() ?? 0;
            View3D view3D = (viewIdParam == 0 ? doc.ActiveView : doc.GetElement(viewIdParam.ToElementId())) as View3D;
            if (view3D == null || view3D.IsTemplate)
                throw new Exception("目標必須是非樣板 3D 視圖");

            bool clearCropOnly = parameters["clearCropOnly"]?.Value<bool>() ?? false;
            bool setActive = parameters["setActive"]?.Value<bool>() ?? true;
            bool selectElement = parameters["selectElement"]?.Value<bool>() ?? true;
            bool includeSupportingBeams = parameters["includeSupportingBeams"]?.Value<bool>() ?? false;
            double supportingBeamToleranceMm = parameters["supportingBeamTolerance_mm"]?.Value<double>() ?? 300;
            // padding_mm is retained as the backward-compatible all-axis fallback.
            // Smoke-review views normally need asymmetric Z extents: enough depth
            // below the room to show the full supporting beams, but no extra height
            // above the room that would reveal the next floor structure.
            double paddingMm = parameters["padding_mm"]?.Value<double>() ?? 1000;
            double paddingXYMm = parameters["padding_xy_mm"]?.Value<double>() ?? paddingMm;
            double paddingBottomMm = parameters["padding_bottom_mm"]?.Value<double>() ?? paddingMm;
            double paddingTopMm = parameters["padding_top_mm"]?.Value<double>() ?? paddingMm;
            if (paddingMm < 0 || paddingXYMm < 0 || paddingBottomMm < 0 || paddingTopMm < 0 || supportingBeamToleranceMm < 0)
                throw new Exception("Section Box padding 不可小於 0");
            string upperStructureMode = parameters["upperStructureMode"]?.Value<string>() ?? "current_level_only";
            if (upperStructureMode != "current_level_only" && upperStructureMode != "include_upper_structure_without_slab")
                throw new Exception($"upperStructureMode 只接受 current_level_only 或 include_upper_structure_without_slab，收到 '{upperStructureMode}'");
            double upperSlabClearanceMm = parameters["upperSlabClearance_mm"]?.Value<double>() ?? 10;
            if (upperSlabClearanceMm < 0)
                throw new Exception("upperSlabClearance_mm 不可小於 0");

            if (clearCropOnly)
            {
                using (Transaction trans = TransactionHelper.Begin(doc, "關閉 3D CropBox"))
                {
                    trans.Start();
                    view3D.CropBoxActive = false;
                    view3D.CropBoxVisible = false;
                    trans.Commit();
                }

                if (setActive)
                    uiDoc.ActiveView = view3D;

                return new
                {
                    Success = true,
                    Mode = "ClearCropOnly",
                    ViewId = view3D.Id.GetIdValue(),
                    ViewName = view3D.Name,
                    CropBoxActive = view3D.CropBoxActive,
                    SectionBoxActive = view3D.IsSectionBoxActive
                };
            }

            IdType elementId = parameters["elementId"]?.Value<IdType>() ?? 0;
            if (elementId == 0)
                throw new Exception("必須指定 elementId；若只要清除錯誤的 CropBox，請設 clearCropOnly=true");

            Element element = doc.GetElement(elementId.ToElementId());
            if (element == null)
                throw new Exception($"找不到元素 ID={elementId}");

            BoundingBoxXYZ elementBox = element.get_BoundingBox(null);
            if (elementBox == null)
                throw new Exception($"元素 ID={elementId} 沒有有效的模型 BoundingBox");

            Transform transform = elementBox.Transform ?? Transform.Identity;
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            double maxZ = double.NegativeInfinity;

            for (int ix = 0; ix <= 1; ix++)
            for (int iy = 0; iy <= 1; iy++)
            for (int iz = 0; iz <= 1; iz++)
            {
                XYZ localPoint = new XYZ(
                    ix == 0 ? elementBox.Min.X : elementBox.Max.X,
                    iy == 0 ? elementBox.Min.Y : elementBox.Max.Y,
                    iz == 0 ? elementBox.Min.Z : elementBox.Max.Z);
                XYZ worldPoint = transform.OfPoint(localPoint);
                minX = Math.Min(minX, worldPoint.X);
                minY = Math.Min(minY, worldPoint.Y);
                minZ = Math.Min(minZ, worldPoint.Z);
                maxX = Math.Max(maxX, worldPoint.X);
                maxY = Math.Max(maxY, worldPoint.Y);
                maxZ = Math.Max(maxZ, worldPoint.Z);
            }

            // Capture the target element bounds before unioning, otherwise each
            // newly included beam could recursively pull in the next beam bay.
            double targetMinX = minX;
            double targetMinY = minY;
            double targetMinZ = minZ;
            double targetMaxX = maxX;
            double targetMaxY = maxY;
            double targetMaxZ = maxZ;
            double toleranceFeet = supportingBeamToleranceMm / 304.8;

            int supportingBeamCount = 0;
            if (includeSupportingBeams)
            {
                // Refactored into SectionBoxLevelHelper (Camera Manager Change 2 shares this
                // loop with the camera iso Key Plan view). Behaviour/output unchanged: same
                // XY-overlap + base-support filter, maxZ still deliberately left untouched.
                supportingBeamCount = SectionBoxLevelHelper.IncludeSupportingBeams(doc,
                    targetMinX, targetMinY, targetMaxX, targetMaxY, targetMinZ, toleranceFeet,
                    ref minX, ref minY, ref minZ, ref maxX, ref maxY);
            }

            double paddingXYFeet = paddingXYMm / 304.8;
            double paddingBottomFeet = paddingBottomMm / 304.8;
            double paddingTopFeet = paddingTopMm / 304.8;
            double sectionMinZ = minZ - paddingBottomFeet;
            double sectionMaxZ = maxZ + paddingTopFeet;

            // Upper-structure routing. current_level_only keeps the validated
            // behaviour (top = target top + padding_top). The other route raises
            // the top to just under the nearest upper slab soffit, so beams hanging
            // below that slab become visible while the slab body stays cut away.
            // No slab found is a hard error: never guess a height.
            object upperStructure = null;
            if (upperStructureMode == "include_upper_structure_without_slab")
            {
                // Refactored into SectionBoxLevelHelper (shared with the camera iso Key Plan
                // view, Camera Manager Change 2). Same filter/priority, output unchanged.
                Element upperSlab = SectionBoxLevelHelper.FindNearestUpperSlab(doc,
                    targetMinX, targetMinY, targetMaxX, targetMaxY, targetMinZ, targetMaxZ, toleranceFeet,
                    out double slabBottomZ, out double slabTopZ, out int slabCandidateCount);

                if (upperSlab == null)
                    throw new Exception(
                        "upperStructureMode=include_upper_structure_without_slab：在目標元素上方找不到與其 XY 重疊的樓板，" +
                        "無法決定上層樓板底高度。上層樓板可能尚未建模、位於連結模型（目前不支援），或 XY 未覆蓋目標。" +
                        "Section Box 未修改；請先建模樓板，或改用 current_level_only。");

                double clearanceFeet = upperSlabClearanceMm / 304.8;
                sectionMaxZ = slabBottomZ - clearanceFeet;
                if (sectionMaxZ <= sectionMinZ)
                    throw new Exception($"上層樓板底 ({slabBottomZ * 304.8:F0} mm) 扣除 upperSlabClearance_mm 後低於 Section Box 底面，Section Box 未修改");

                int upperBeamCount = SectionBoxLevelHelper.CountUpperStructureBeams(doc,
                    targetMinX, targetMinY, targetMaxX, targetMaxY, targetMaxZ, paddingXYFeet, sectionMaxZ, toleranceFeet);

                upperStructure = new
                {
                    UpperSlabId = upperSlab.Id.GetIdValue(),
                    UpperSlabName = upperSlab.Name,
                    UpperSlabBottom_mm = slabBottomZ * 304.8,
                    UpperSlabTop_mm = slabTopZ * 304.8,
                    UpperSlabCandidateCount = slabCandidateCount,
                    UpperSlabClearance_mm = upperSlabClearanceMm,
                    TargetTop_mm = targetMaxZ * 304.8,
                    TopBelowTargetTop = sectionMaxZ < targetMaxZ,
                    UpperStructureBeamCount = upperBeamCount,
                    Note = upperBeamCount == 0
                        ? "Section Box 已延伸至上層樓板底，但範圍內未偵測到上層結構構架；請確認上層樑是否已建模"
                        : "Section Box 頂面位於上層樓板底下方，上層樑可見、樓板本體已排除"
                };
            }

            XYZ sectionMin = new XYZ(minX - paddingXYFeet, minY - paddingXYFeet, sectionMinZ);
            XYZ sectionMax = new XYZ(maxX + paddingXYFeet, maxY + paddingXYFeet, sectionMaxZ);
            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
            {
                Min = sectionMin,
                Max = sectionMax,
                Transform = Transform.Identity,
                Enabled = true
            };

            using (Transaction trans = TransactionHelper.Begin(doc, "設定 3D Section Box"))
            {
                trans.Start();
                view3D.SetSectionBox(sectionBox);
                view3D.IsSectionBoxActive = true;
                view3D.CropBoxActive = false;
                view3D.CropBoxVisible = false;
                trans.Commit();
            }

            if (setActive)
                uiDoc.ActiveView = view3D;
            if (selectElement)
                uiDoc.Selection.SetElementIds(new[] { element.Id });

            return new
            {
                Success = true,
                Mode = "SectionBox",
                ViewId = view3D.Id.GetIdValue(),
                ViewName = view3D.Name,
                ElementId = elementId,
                ElementName = element.Name,
                ElementCategory = element.Category?.Name ?? "Unknown",
                Padding_mm = paddingMm,
                PaddingXY_mm = paddingXYMm,
                PaddingBottom_mm = paddingBottomMm,
                PaddingTop_mm = paddingTopMm,
                UpperStructureMode = upperStructureMode,
                PaddingTopApplied = upperStructureMode == "current_level_only",
                UpperStructure = upperStructure,
                IncludeSupportingBeams = includeSupportingBeams,
                SupportingBeamTolerance_mm = supportingBeamToleranceMm,
                SupportingBeamCount = supportingBeamCount,
                CropBoxActive = view3D.CropBoxActive,
                SectionBoxActive = view3D.IsSectionBoxActive,
                SectionBox_mm = new
                {
                    Min = new { x = sectionMin.X * 304.8, y = sectionMin.Y * 304.8, z = sectionMin.Z * 304.8 },
                    Max = new { x = sectionMax.X * 304.8, y = sectionMax.Y * 304.8, z = sectionMax.Z * 304.8 },
                    Width = (sectionMax.X - sectionMin.X) * 304.8,
                    Depth = (sectionMax.Y - sectionMin.Y) * 304.8,
                    Height = (sectionMax.Z - sectionMin.Z) * 304.8
                }
            };
        }

        // TryGetWorldBounds 已抽到 SectionBoxLevelHelper.TryGetWorldBounds（Camera Manager
        // Change 2 與相機管理面板的等角 Key Plan 視圖共用同一份），這裡不再保留本地版本。

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
