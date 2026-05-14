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
        #region 結構穿梁檢核 (Branch B Wave 1)

        /// <summary>
        /// 掃描目前視圖中所有被套管穿過的梁（支援連結模型）
        /// 來源 fork: seven777/main:MCP/Core/Commands/CommandExecutor.BeamPenetration.cs (ScanPenetratedBeamsInView)
        /// </summary>
        private object ScanPenetratedBeamsInView(JObject parameters)
        {
            Document mainDoc = _uiApp.ActiveUIDocument.Document;
            View activeView = mainDoc.ActiveView;
            string targetLevelName = parameters["targetLevel"]?.Value<string>();

            var sleeves = new FilteredElementCollector(mainDoc, activeView.Id)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> {
                    BuiltInCategory.OST_PipeAccessory,
                    BuiltInCategory.OST_GenericModel
                })).ToList();

            var linkInstances = new FilteredElementCollector(mainDoc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();
            var results = new Dictionary<string, dynamic>();

            foreach (var sleeve in sleeves)
            {
                BoundingBoxXYZ sleeveBBox = sleeve.get_BoundingBox(null);
                if (sleeveBBox == null) continue;

                double sleeveL = GetSleeveLength(sleeve);
                double buffer = 50.0 / 304.8;
                Outline mainOutline = new Outline(sleeveBBox.Min - new XYZ(buffer, buffer, buffer), sleeveBBox.Max + new XYZ(buffer, buffer, buffer));

                foreach (var li in linkInstances.Concat(new List<RevitLinkInstance> { null }))
                {
                    Document doc = (li == null) ? mainDoc : li.GetLinkDocument();
                    if (doc == null) continue;

                    Transform tr = (li == null) ? Transform.Identity : li.GetTotalTransform();
                    Transform invTr = tr.Inverse;
                    Outline searchOutline = (li == null) ? mainOutline : TransformOutline(mainOutline, invTr);

                    var beams = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WherePasses(new BoundingBoxIntersectsFilter(searchOutline))
                        .Cast<FamilyInstance>();

                    foreach (var b in beams)
                    {
                        string beamLevel = GetReferenceLevelName(b);
                        if (!string.IsNullOrEmpty(targetLevelName) && beamLevel != targetLevelName) continue;

                        double beamWidth = GetBeamWidth(b);
                        if (sleeveL < beamWidth * 0.9) continue;

                        string key = $"{(li?.Id.GetIdValue() ?? 0)}_{b.Id.GetIdValue()}";
                        if (!results.ContainsKey(key))
                            results[key] = new { BeamId = b.Id.GetIdValue(), LinkId = li?.Id.GetIdValue() ?? 0, Name = b.Name, Level = beamLevel, Sleeves = new List<IdType>() };
                        results[key].Sleeves.Add(sleeve.Id.GetIdValue());
                    }
                }
            }
            return new { Count = results.Count, Beams = results.Values.ToList() };
        }

        /// <summary>
        /// 深度分析穿梁幾何 [WIP - Wave 2]
        /// Wave 1: 取得梁基本資訊（地位、深度、樓層）
        /// Wave 2: 完整 Zone A/B/C 分區判讀 + 套管位置/直徑/邊距計算（參考 domain/beam-penetration-rc.md §2-§4）
        /// 來源 fork: seven777/main:MCP/Core/Commands/CommandExecutor.BeamPenetration.cs (AnalyzeBeamPenetration)
        /// 改動：去除原始的 TaskDialog.Show 除錯彈窗 + 替換 "DEBUG_SUCCESS_STING" return 為結構化 WIP 回應
        /// </summary>
        private object AnalyzeBeamPenetration(JObject parameters)
        {
            try
            {
                IdType beamId = parameters["beamId"]?.Value<IdType>() ?? 0;
                IdType linkId = parameters["linkInstanceId"]?.Value<IdType>() ?? 0;

                Document mainDoc = _uiApp.ActiveUIDocument.Document;
                Document doc = mainDoc;

                if (linkId != 0)
                {
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

                // [WIP - Wave 2] 完整套管位置 / 直徑 / 邊距 / Zone A/B/C 分區判讀邏輯待補
                // 完成參考：domain/beam-penetration-rc.md §2-§4 (Zone A/B/C 規則樹)
                return new
                {
                    Status = "WIP",
                    BeamId = beamId,
                    BeamName = beam.Name,
                    BeamLevel = beamLevel,
                    BeamDepthMm = Math.Round(beamDepth * 304.8, 2),
                    Message = "[WIP - Wave 2] 完整穿孔分析邏輯尚未實作，目前僅回傳梁基本資訊。完整邏輯（Zone A/B/C 判讀、套管位置/直徑/邊距）參考 domain/beam-penetration-rc.md。"
                };
            }
            catch (Exception ex)
            {
                return new { Success = false, Error = "分析失敗: " + ex.Message };
            }
        }

        /// <summary>
        /// 偵測 RC 梁 + 鋼梁重疊建立 SRC 映射 [WIP - Wave 2]
        /// 設計：query 主文件 + 連結模型的 OST_StructuralFraming，依 material/family pattern 篩 RC vs 鋼梁，BoundingBox 相交判定
        /// 來源 fork: seven777/main:MCP/Core/Commands/CommandExecutor.BeamPenetration.cs (GetSrcBeamMapping，對方為空殼)
        /// </summary>
        private object GetSrcBeamMapping(JObject parameters)
        {
            // [WIP - Wave 2] 完整實作待補
            return new
            {
                Status = "WIP",
                Message = "[WIP - Wave 2] SRC 梁映射偵測尚未實作。完整邏輯應 query 主文件 + 連結模型的 OST_StructuralFraming，依 material/family pattern 篩 RC vs 鋼梁，再以 BoundingBox 相交判定。",
                Mappings = new List<object>()
            };
        }

        /// <summary>
        /// 視覺化穿梁檢核結果（上色 + TextNote 標籤）
        /// 來源 fork: seven777/main:MCP/Core/Commands/CommandExecutor.BeamPenetration.cs (VisualizePenetration)
        /// </summary>
        private object VisualizePenetration(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            JArray results = parameters["results"] as JArray;
            if (results == null) throw new Exception("缺少 results 參數");

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
                    ogs.SetProjectionLineColor(color);
                    doc.ActiveView.SetElementOverrides(sleeve.Id, ogs);

                    BoundingBoxXYZ bb = sleeve.get_BoundingBox(doc.ActiveView);
                    XYZ pos = (bb != null) ? bb.Max : XYZ.Zero;
                    ElementId textTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                    TextNote.Create(doc, doc.ActiveView.Id, pos, "[檢核] " + msg, textTypeId);
                }
                trans.Commit();
            }
            return new { Success = true };
        }

        #endregion

        #region BeamPenetration 私有 helper

        private double GetBeamWidth(FamilyInstance beam)
        {
            Parameter p = beam.LookupParameter("b") ?? beam.LookupParameter("梁寬") ?? beam.LookupParameter("Width");
            return (p != null && p.HasValue) ? p.AsDouble() : 0.4 / 0.3048;
        }

        private double GetSleeveLength(Element sleeve)
        {
            Parameter p = sleeve.LookupParameter("長度") ?? sleeve.LookupParameter("Length");
            return (p != null && p.HasValue) ? p.AsDouble() : 0.6 / 0.3048;
        }

        private string GetReferenceLevelName(Element elem)
        {
            Parameter p = elem.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM) ?? elem.get_Parameter(BuiltInParameter.LEVEL_PARAM);
            if (p != null && p.HasValue) return elem.Document.GetElement(p.AsElementId())?.Name;
            return "Unknown";
        }

        private Outline TransformOutline(Outline outline, Transform tr)
        {
            XYZ min = tr.OfPoint(outline.MinimumPoint);
            XYZ max = tr.OfPoint(outline.MaximumPoint);
            return new Outline(
                new XYZ(Math.Min(min.X, max.X), Math.Min(min.Y, max.Y), Math.Min(min.Z, max.Z)),
                new XYZ(Math.Max(min.X, max.X), Math.Max(min.Y, max.Y), Math.Max(min.Z, max.Z)));
        }

        #endregion
    }
}
