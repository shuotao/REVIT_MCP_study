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
        /// <summary>
        /// 調整剖面視圖的網格線 (Grids) 與樓層線 (Levels) 2D 範圍與顯示
        /// </summary>
        private object AdjustSectionDatums(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            
            // 讀取傳入的視圖 ID 陣列
            JArray viewIdsArray = parameters["viewIds"] as JArray;
            if (viewIdsArray == null || viewIdsArray.Count == 0)
            {
                return new { Success = false, Message = "未提供有效的 viewIds 參數。" };
            }

            var processedViews = new List<string>();
            var errors = new List<string>();

            using (Transaction trans = new Transaction(doc, "自動調整剖面基準線"))
            {
                trans.Start();

                foreach (var token in viewIdsArray)
                {
                    try
                    {
                        IdType val = token.Value<IdType>();
                        ElementId id = new ElementId(val);
                        Element elem = doc.GetElement(id);
                        if (elem == null) continue;

                        // 嘗試尋找/轉為視圖
                        View view = elem as View;
                        if (view == null)
                        {
                            // 容錯：若傳入的是剖面標記，利用名稱尋找同名視圖
                            string name = elem.Name;
                            view = new FilteredElementCollector(doc)
                                .OfClass(typeof(View))
                                .Cast<View>()
                                .FirstOrDefault(v => v.Name == name && v.ViewType == ViewType.Section);
                        }

                        if (view == null)
                        {
                            errors.Add($"Element ID {val} 無法對應至剖面視圖。");
                            continue;
                        }

                        // 里程碑 2：自動判定並啟用裁剪框，取得邊界幾何
                        BoundingBoxXYZ cropBox = EnsureAndGetCropBox(view);
                        if (cropBox == null)
                        {
                            errors.Add($"視圖 {view.Name} 無法取得 CropBox 邊界資訊。");
                            continue;
                        }

                        double minX = cropBox.Min.X * 304.8;
                        double maxX = cropBox.Max.X * 304.8;
                        double minY = cropBox.Min.Y * 304.8;
                        double maxY = cropBox.Max.Y * 304.8;

                        // 里程碑 3：調整網格線 (Grids) 的 2D 範圍與氣泡
                        AdjustGridsInView(doc, view, cropBox, GetViewTransform(view));

                        // 里程碑 4：調整樓層線 (Levels) 的 2D 範圍（動態長度適應）與氣泡
                        AdjustLevelsInView(doc, view, cropBox, GetViewTransform(view));

                        processedViews.Add($"{view.Name} (Crop Box X: {minX:F0} ~ {maxX:F0}, Y: {minY:F0} ~ {maxY:F0} mm)");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"處理視圖時發生錯誤: {ex.Message}");
                    }
                }

                trans.Commit();
            }

            return new
            {
                Success = errors.Count == 0,
                ProcessedCount = processedViews.Count,
                ProcessedViews = processedViews,
                Errors = errors
            };
        }

        /// <summary>
        /// 取得視圖在世界座標中的 Transform (適用各版本 Revit)
        /// </summary>
        private Transform GetViewTransform(View view)
        {
            Transform transform = Transform.Identity;
            transform.BasisX = view.RightDirection;
            transform.BasisY = view.UpDirection;
            transform.BasisZ = view.ViewDirection;
            transform.Origin = view.Origin;
            return transform;
        }

        /// <summary>
        /// 確保視圖啟用裁剪框，並回傳 CropBox XYZ
        /// </summary>
        private BoundingBoxXYZ EnsureAndGetCropBox(View view)
        {
            if (view == null) return null;

            if (!view.CropBoxActive)
            {
                view.CropBoxActive = true;
                view.CropBoxVisible = true; // 同意自動啟用，並設為可見以方便除錯與出圖確認
            }

            return view.CropBox;
        }

        /// <summary>
        /// 調整剖面視圖內 Grids 的 2D 範圍與氣泡顯示
        /// </summary>
        private void AdjustGridsInView(Document doc, View view, BoundingBoxXYZ cropBox, Transform transform)
        {
            var grids = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();

            double offsetFeet = 150.0 / 304.8; // 150 mm 轉為英尺
            Transform invTrans = transform.Inverse;

            foreach (var grid in grids)
            {
                try
                {
                    // 確保將端點切換成視圖特有的 2D 範圍
                    grid.SetDatumExtentType(DatumEnds.End0, view, DatumExtentType.ViewSpecific);
                    grid.SetDatumExtentType(DatumEnds.End1, view, DatumExtentType.ViewSpecific);

                    IList<Curve> curves = grid.GetCurvesInView(DatumExtentType.ViewSpecific, view);
                    if (curves == null || curves.Count == 0) continue;

                    Curve curve = curves[0];
                    XYZ gP0_world = curve.GetEndPoint(0);
                    XYZ gP1_world = curve.GetEndPoint(1);

                    // 轉為視圖局部座標進行上下判定
                    XYZ gP0_local = invTrans.OfPoint(gP0_world);
                    XYZ gP1_local = invTrans.OfPoint(gP1_world);

                    bool p0IsTop = gP0_local.Y > gP1_local.Y;
                    XYZ top_local = p0IsTop ? gP0_local : gP1_local;
                    XYZ bottom_local = p0IsTop ? gP1_local : gP0_local;

                    // 上下端點垂直對齊 Crop Box 並延伸 150mm
                    XYZ newTop_local = new XYZ(top_local.X, cropBox.Max.Y + offsetFeet, top_local.Z);
                    XYZ newBottom_local = new XYZ(bottom_local.X, cropBox.Min.Y - offsetFeet, bottom_local.Z);

                    XYZ newTop_world = transform.OfPoint(newTop_local);
                    XYZ newBottom_world = transform.OfPoint(newBottom_local);

                    // 重新指派 2D 線段，起點設為下方，終點設為上方
                    Line newCurve = Line.CreateBound(newBottom_world, newTop_world);
                    grid.SetCurveInView(DatumExtentType.ViewSpecific, view, newCurve);

                    // 設定氣泡顯示：下方隱藏，上方顯示
                    grid.HideBubbleInView(DatumEnds.End0, view);
                    grid.ShowBubbleInView(DatumEnds.End1, view);
                }
                catch (Exception)
                {
                    // 容錯，不干擾其他 Grid 的處理
                }
            }
        }

        /// <summary>
        /// 調整剖面視圖內 Levels 的 2D 範圍（基於字串長度動態適應）與氣泡顯示
        /// </summary>
        private void AdjustLevelsInView(Document doc, View view, BoundingBoxXYZ cropBox, Transform transform)
        {
            var levels = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();

            Transform invTrans = transform.Inverse;
            double viewScale = view.Scale;

            // 基礎字元長度估算參數（公釐）
            double basePaperWidth = 10.0; // 氣泡本身的基本物理寬度
            double charPaperWidth = 2.2;  // 每個字元的平均估估物理寬度

            foreach (var level in levels)
            {
                try
                {
                    // 確保將端點切換成視圖特有的 2D 範圍
                    level.SetDatumExtentType(DatumEnds.End0, view, DatumExtentType.ViewSpecific);
                    level.SetDatumExtentType(DatumEnds.End1, view, DatumExtentType.ViewSpecific);

                    IList<Curve> curves = level.GetCurvesInView(DatumExtentType.ViewSpecific, view);
                    if (curves == null || curves.Count == 0) continue;

                    Curve curve = curves[0];
                    XYZ lP0_world = curve.GetEndPoint(0);
                    XYZ lP1_world = curve.GetEndPoint(1);

                    // 轉為視圖局部座標進行左右判定
                    XYZ lP0_local = invTrans.OfPoint(lP0_world);
                    XYZ lP1_local = invTrans.OfPoint(lP1_world);

                    bool p0IsLeft = lP0_local.X < lP1_local.X;
                    XYZ left_local = p0IsLeft ? lP0_local : lP1_local;
                    XYZ right_local = p0IsLeft ? lP1_local : lP0_local;

                    // 依樓層名稱長度動態計算模型偏移量（英尺）
                    string levelName = level.Name ?? "";
                    // 估算此樓層名稱與高度文字加總長度（Revit 預設可能顯示: 名稱 + 高度值）
                    // 這裡取 levelName 長度，並多給予安全緩衝
                    double paperWidth = basePaperWidth + (levelName.Length * charPaperWidth);
                    double offsetFeet = (paperWidth * viewScale) / 304.8;

                    // 左右端點水平對齊 Crop Box 並向外延伸動態計算後的 offset
                    XYZ newLeft_local = new XYZ(cropBox.Min.X - offsetFeet, left_local.Y, left_local.Z);
                    XYZ newRight_local = new XYZ(cropBox.Max.X + offsetFeet, right_local.Y, right_local.Z);

                    XYZ newLeft_world = transform.OfPoint(newLeft_local);
                    XYZ newRight_world = transform.OfPoint(newRight_local);

                    // 重新指派 2D 線段，起點設為左側，終點設為右側
                    Line newCurve = Line.CreateBound(newLeft_world, newRight_world);
                    level.SetCurveInView(DatumExtentType.ViewSpecific, view, newCurve);

                    // 雙側均顯示氣泡
                    level.ShowBubbleInView(DatumEnds.End0, view);
                    level.ShowBubbleInView(DatumEnds.End1, view);
                }
                catch (Exception)
                {
                    // 容錯，不干擾其他 Level 的處理
                }
            }
        }
    }
}
