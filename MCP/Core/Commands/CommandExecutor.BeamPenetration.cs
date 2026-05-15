using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCP.Models;

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
        /// 1. 掃描目前視圖中所有被套管穿過的梁 (V3.0)
        /// </summary>
        private object ScanPenetratedBeamsInView(JObject parameters)
        {
            Document mainDoc = _uiApp.ActiveUIDocument.Document;
            View activeView = mainDoc.ActiveView;
            
            // 獲取視圖中所有套管
            var sleeves = new FilteredElementCollector(mainDoc, activeView.Id)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> { 
                    BuiltInCategory.OST_PipeAccessory, 
                    BuiltInCategory.OST_GenericModel 
                })).ToList();

            var results = new Dictionary<string, dynamic>();

            // 獲取視圖中所有連結模型
            var linkInstances = new FilteredElementCollector(mainDoc, activeView.Id)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            foreach (var sleeve in sleeves)
            {
                BoundingBoxXYZ sleeveBBox = sleeve.get_BoundingBox(null);
                if (sleeveBBox == null) continue;

                foreach (var li in linkInstances)
                {
                    Document linkDoc = li.GetLinkDocument();
                    if (linkDoc == null) continue;

                    Transform tr = li.GetTotalTransform();
                    Outline sleeveOutline = new Outline(tr.Inverse.OfPoint(sleeveBBox.Min), tr.Inverse.OfPoint(sleeveBBox.Max));

                    // 只針對視圖中「看得到」的梁進行幾何碰撞
                    var linkBeams = new FilteredElementCollector(linkDoc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WherePasses(new BoundingBoxIntersectsFilter(sleeveOutline))
                        .WhereElementIsNotElementType();

                    foreach (var b in linkBeams)
                    {
                        string key = $"{li.Id.GetIdValue()}_{b.Id.GetIdValue()}";
                        if (!results.ContainsKey(key))
                        {
                            results[key] = new { 
                                BeamId = b.Id.GetIdValue(), 
                                LinkId = li.Id.GetIdValue(), 
                                Name = b.Name, 
                                Level = GetReferenceLevelName(b), 
                                SleeveIds = new List<IdType>() 
                            };
                        }
                        results[key].SleeveIds.Add(sleeve.Id.GetIdValue());
                    }
                }
            }
            return new { Count = results.Count, Beams = results.Values.ToList() };
        }

        /// <summary>
        /// 2. 深度分析穿梁幾何
        /// </summary>
        private object AnalyzeBeamPenetration(JObject parameters)
        {
            // TaskDialog.Show("MCP Debug", "Entering AnalyzeBeamPenetration");
            try {
                IdType beamId = parameters["beamId"]?.Value<IdType>() ?? 0;
                IdType linkId = parameters["linkInstanceId"]?.Value<IdType>() ?? 0;
                // TaskDialog.Show("MCP Debug", $"Params: beam={beamId}, link={linkId}");

                Document mainDoc = _uiApp.ActiveUIDocument.Document;
                Document doc = mainDoc;
                
                if (linkId != 0) {
                    var link = mainDoc.GetElement(new ElementId(linkId)) as RevitLinkInstance;
                    if (link == null) throw new Exception($"找不到連結模型實體 (ID: {linkId})");
                    doc = link.GetLinkDocument();
                    if (doc == null) throw new Exception("無法取得連結模型的文件 (Document 為 null)");
                }

                Element beamElem = doc.GetElement(new ElementId(beamId));
                if (beamElem == null) throw new Exception($"在目標文件中找不到梁 (ID: {beamId})");
                
                FamilyInstance beam = beamElem as FamilyInstance;
                if (beam == null) throw new Exception($"元素 (ID: {beamId}) 不是 FamilyInstance (目前為 {beamElem.GetType().Name})");

                string beamLevel = GetReferenceLevelName(beam);

                double beamDepth = 0.6 / 0.3048;
                Parameter hParam = beam.LookupParameter("h") ?? beam.LookupParameter("梁高") ?? beam.LookupParameter("Height");
                if (hParam != null && hParam.HasValue) beamDepth = hParam.AsDouble();

                return "DEBUG_SUCCESS_STING";
            } catch (Exception ex) {
                return new { Success = false, Error = "分析失敗: " + ex.Message + "\n" + ex.StackTrace };
            }
        }

        private object GetSrcBeamMapping(JObject parameters) { return new { Success = true }; }

        /// <summary>
        /// 3. 自動化標註與視覺化
        /// </summary>
        private object VisualizePenetration(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            JArray results = parameters["results"] as JArray;

            using (Transaction trans = new Transaction(doc, "自動化穿梁標註"))
            {
                trans.Start();
                foreach (var res in results)
                {
                    IdType id = res["SleeveId"].Value<IdType>();
                    bool isOk = res["IsOk"].Value<bool>();
                    string msg = res["Message"].Value<string>();

                    Element sleeve = doc.GetElement(new ElementId(id));
                    if (sleeve == null) continue;

                    Color color = isOk ? new Color(0, 200, 0) : new Color(255, 0, 0);
                    OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                    
                    // 同時設定線條與填充顏色，確保清晰可見
                    ogs.SetProjectionLineColor(color);
                    ogs.SetSurfaceForegroundPatternColor(color);
                    // 嘗試抓取固體填充樣式
                    FillPatternElement solidFill = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>().FirstOrDefault(x => x.GetFillPattern().IsSolidFill);
                    if (solidFill != null) ogs.SetSurfaceForegroundPatternId(solidFill.Id);

                    doc.ActiveView.SetElementOverrides(sleeve.Id, ogs);

                    // 文字標註強制放在視圖平面高度
                    XYZ pos = XYZ.Zero;
                    BoundingBoxXYZ bb = sleeve.get_BoundingBox(null);
                    if (bb != null) pos = (bb.Min + bb.Max) * 0.5;
                    
                    // 取得視圖切割平面高度
                    PlanViewRange vr = (doc.ActiveView as ViewPlan)?.GetViewRange();
                    if (vr != null) {
                        double cutPlaneH = (doc.ActiveView as ViewPlan).GenLevel.Elevation + vr.GetOffset(PlanViewPlane.CutPlane);
                        pos = new XYZ(pos.X, pos.Y, cutPlaneH);
                    }

                    TextNote.Create(doc, doc.ActiveView.Id, pos, isOk ? "● PASS" : "● FAIL: " + msg, new ElementId(-1));
                }
                trans.Commit();
            }
            return new { Success = true };
        }

        private double GetBeamWidth(Element beam) {
            Parameter p = beam.LookupParameter("b") ?? beam.LookupParameter("梁寬") ?? beam.LookupParameter("Width");
            return (p != null && p.HasValue) ? p.AsDouble() : 0.4 / 0.3048;
        }
        private double GetSleeveLength(Element sleeve) {
            Parameter p = sleeve.LookupParameter("長度") ?? sleeve.LookupParameter("Length");
            return (p != null && p.HasValue) ? p.AsDouble() : 0.6 / 0.3048;
        }
        private string GetReferenceLevelName(Element elem) {
            // 嘗試多種可能的樓層參數名稱
            Parameter p = elem.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM) 
                       ?? elem.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                       ?? elem.LookupParameter("Reference Level")
                       ?? elem.LookupParameter("樓層")
                       ?? elem.LookupParameter("參考樓層");

            if (p != null && p.HasValue) {
                ElementId id = p.AsElementId();
                if (id != ElementId.InvalidElementId) return elem.Document.GetElement(id)?.Name;
            }
            return "Unknown";
        }
        private Outline TransformOutline(Outline outline, Transform tr) {
            XYZ min = tr.OfPoint(outline.MinimumPoint);
            XYZ max = tr.OfPoint(outline.MaximumPoint);
            return new Outline(new XYZ(Math.Min(min.X, max.X), Math.Min(min.Y, max.Y), Math.Min(min.Z, max.Z)),
                               new XYZ(Math.Max(min.X, max.X), Math.Max(min.Y, max.Y), Math.Max(min.Z, max.Z)));
        }
    }
}
