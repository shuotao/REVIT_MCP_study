using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
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
        private const double CM_TO_FEET = 10.0 / 304.8;
        private const double MM_TO_FEET = 1.0 / 304.8;
        /// <summary>
        /// 建立樓梯檢核專用的剖面視圖
        /// </summary>
        private object CreateStairSectionView(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType stairId = parameters["stairId"]?.Value<IdType>() ?? 0;
            string viewNamePrefix = parameters["viewName"]?.Value<string>() ?? "樓梯檢核剖面";
            double offsetMm = parameters["offset"]?.Value<double>() ?? 1000.0;
            int scale = parameters["scale"]?.Value<int>() ?? 50;

            Stairs stairs = doc.GetElement(new ElementId(stairId)) as Stairs;
            if (stairs == null) throw new Exception("找不到目標樓梯");

            BoundingBoxXYZ bbox = stairs.get_BoundingBox(null);
            XYZ min = bbox.Min;
            XYZ max = bbox.Max;
            XYZ center = (min + max) / 2.0;

            double dx = max.X - min.X;
            double dy = max.Y - min.Y;

            XYZ viewUp = XYZ.BasisZ;
            XYZ viewDirection = dx > dy ? XYZ.BasisY : XYZ.BasisX;
            XYZ viewRight = dx > dy ? XYZ.BasisX : -XYZ.BasisY;

            double offsetFeet = offsetMm / 304.8;
            double sectionHeight = (max.Z - min.Z) + offsetFeet * 2;
            double sectionDepth = (dx > dy ? dy : dx) + offsetFeet * 2;
            double sectionWidth = (dx > dy ? dx : dy) + offsetFeet * 2;

            Transform transform = Transform.Identity;
            transform.Origin = center;
            transform.BasisX = viewRight;
            transform.BasisY = viewUp;
            transform.BasisZ = viewDirection;

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
            sectionBox.Transform = transform;
            sectionBox.Min = new XYZ(-sectionWidth / 2.0, -offsetFeet, -sectionDepth / 2.0);
            sectionBox.Max = new XYZ(sectionWidth / 2.0, sectionHeight - offsetFeet, sectionDepth / 2.0);

            ElementId viewfamilyId = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Section).Id;

            ViewSection sectionView = null;
            string finalViewName = $"{viewNamePrefix}_{stairId}_{DateTime.Now:HHmm}";

            using (Transaction trans = new Transaction(doc, "建立樓梯專用剖面"))
            {
                trans.Start();
                sectionView = ViewSection.CreateSection(doc, viewfamilyId, sectionBox);
                sectionView.Name = finalViewName;
                sectionView.Scale = scale;
                trans.Commit();
            }

            return new { ViewId = sectionView.Id.GetIdValue(), ViewName = finalViewName, Message = "已建立側向剖面" };
        }

        /// <summary>
        /// 取得樓梯的真實實測寬度
        /// </summary>
        private object GetStairActualWidth(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType stairId = parameters["stairId"]?.Value<IdType>() ?? 0;
            Stairs stairs = doc.GetElement(new ElementId(stairId)) as Stairs;
            if (stairs == null) throw new Exception("找不到目標樓梯");

            var runIds = stairs.GetStairsRuns();
            var runDetails = new List<object>();
            double minActualWidth = double.MaxValue;

            foreach (ElementId rid in runIds)
            {
                StairsRun run = doc.GetElement(rid) as StairsRun;
                if (run != null)
                {
                    double widthMm = Math.Round(run.ActualRunWidth * 304.8, 2);
                    if (widthMm < minActualWidth) minActualWidth = widthMm;
                    runDetails.Add(new { RunId = rid.GetIdValue(), ActualWidthMm = widthMm });
                }
            }
            return new { StairId = stairId, MinActualWidth = minActualWidth, RunDetails = runDetails };
        }

        /// <summary>
        /// 樓梯淨高檢查 - 2D 平面化與參數版
        /// </summary>
        private object CheckStairHeadroom(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            View activeView = doc.ActiveView;
            IdType stairId = parameters["stairId"]?.Value<IdType>() ?? 0;
            double finishThicknessCm = parameters["finishThicknessCm"]?.Value<double>() ?? 0;
            double headroomLimitCm = parameters["headroomLimitCm"]?.Value<double>() ?? 190.0;


            Stairs stairs = doc.GetElement(new ElementId(stairId)) as Stairs;
            if (stairs == null) throw new Exception("找不到樓梯");
            
            double checkHeightFeet = (headroomLimitCm + finishThicknessCm) * CM_TO_FEET;
            int failCount = 0;
            List<XYZ> failPoints = new List<XYZ>();

            Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true, ComputeReferences = true };
            List<Solid> stairSolids = new List<Solid>();
            AddSolidsFromElement(stairs, opt, stairSolids);
            foreach (ElementId rid in stairs.GetStairsRuns()) AddSolidsFromElement(doc.GetElement(rid), opt, stairSolids);
            foreach (ElementId lid in stairs.GetStairsLandings()) AddSolidsFromElement(doc.GetElement(lid), opt, stairSolids);

            View3D detectorView = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate);
            if (detectorView == null) throw new Exception("需有 3D 視圖進行射線偵測");

            var categories = new List<BuiltInCategory> { 
                BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Ceilings, 
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Stairs
            };

            using (Transaction trans = new Transaction(doc, "樓梯淨高 2D 檢核"))
            {
                trans.Start();
                ClearExistingMarkers(doc, stairId);
                ReferenceIntersector intersector = new ReferenceIntersector(new ElementMulticategoryFilter(categories), FindReferenceTarget.All, detectorView);
                
                ElementId filledRegionTypeId = GetOrCreateRedFilledRegionType(doc);
                var failures = new List<Tuple<XYZ, Reference, Reference, double>>();

                foreach (Solid solid in stairSolids)
                {
                    foreach (Face face in solid.Faces)
                    {
                        XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
                        if (normal.Z > 0.5 && face.Area >= 0.05)
                        {
                            XYZ center = face.Evaluate(new UV(0.5, 0.5));
                            XYZ startPoint = center + XYZ.BasisZ * (10.0 * MM_TO_FEET);
                            ReferenceWithContext hit = intersector.FindNearest(startPoint, XYZ.BasisZ);
                            
                            if (hit != null && hit.Proximity < (checkHeightFeet - (10.0 * MM_TO_FEET)))
                            {
                                    if (hit.GetReference().ElementId.GetIdValue() == stairId) continue;
                                    if (failures.Any(f => f.Item1.DistanceTo(center) < 0.3)) continue;
                                    
                                    failCount++;
                                    failPoints.Add(center);
                                    failures.Add(Tuple.Create(center, face.Reference, hit.GetReference(), hit.Proximity));
                                    
                                    if (activeView is ViewPlan planView)
                                    {
                                        CreateFailFilledRegion(doc, planView, face, filledRegionTypeId, stairId);
                                    }
                            }
                        }
                    }
                }

                // 更新參數
                Parameter commentPara = stairs.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentPara != null) 
                {
                    string status = failCount > 0 ? $"【淨高不合格】共 {failCount} 處點位不足 {headroomLimitCm}cm (已扣除裝修補償 {finishThicknessCm}cm)" : "【淨高合格】";
                    commentPara.Set(status);
                }

                trans.Commit();

                // 3. 自動產製帶標註的剖面
                if (failCount > 0)
                {
                    try {
                        using (Transaction transSection = new Transaction(doc, "建立標註剖面"))
                        {
                            transSection.Start();
                            CreateDetailedSection(doc, stairs, failures, headroomLimitCm);
                            transSection.Commit();
                        }
                    } catch { }
                }
            }
            return new { StairId = stairId, Failures = failCount, Message = $"檢測完成：發現 {failCount} 處淨高不足，已自動產生標註剖面。" };
        }

        private void CreateDetailedSection(Document doc, Stairs stairs, List<Tuple<XYZ, Reference, Reference, double>> failures, double limitCm)
        {
            XYZ failPos = failures[0].Item1;
            StairsRun targetRun = null;
            foreach (ElementId rid in stairs.GetStairsRuns()) {
                StairsRun run = doc.GetElement(rid) as StairsRun;
                if (run != null && run.get_BoundingBox(null).Min.Z <= failPos.Z + 1.0 && run.get_BoundingBox(null).Max.Z >= failPos.Z - 1.0) {
                    targetRun = run; break;
                }
            }

            XYZ viewDir = XYZ.BasisX; XYZ viewUp = XYZ.BasisZ; XYZ viewRight = XYZ.BasisY;
            if (targetRun != null) {
                BoundingBoxXYZ runBox = targetRun.get_BoundingBox(null);
                if ((runBox.Max.X - runBox.Min.X) > (runBox.Max.Y - runBox.Min.Y)) { viewDir = XYZ.BasisY; viewRight = XYZ.BasisX; }
                else { viewDir = XYZ.BasisX; viewRight = -XYZ.BasisY; }
            }

            double sizeFeet = 35.0;
            Transform tr = Transform.Identity; tr.Origin = failPos; tr.BasisX = viewRight; tr.BasisY = viewUp; tr.BasisZ = viewDir;
            BoundingBoxXYZ sBox = new BoundingBoxXYZ { Transform = tr, Min = new XYZ(-sizeFeet, -sizeFeet, -0.5), Max = new XYZ(sizeFeet, sizeFeet, 0.5) };
            
            ElementId vftId = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.Section).Id;
            ViewSection section = ViewSection.CreateSection(doc, vftId, sBox);
            section.Name = $"MCP_不合格剖面_{stairs.Id}_{DateTime.Now:HHmm}";

            // 批次建立標註 (包含真實尺寸線與文字註記)
            foreach (var fail in failures) {
                // A. 建立文字註記 (淨高不足: XXcm) 並增加引線
                XYZ textOffset = viewRight * (300.0 * MM_TO_FEET) + viewUp * (300.0 * MM_TO_FEET);
                XYZ textPos = fail.Item1 + textOffset;
                string text = $"淨高不足: {Math.Round((fail.Item4 + (10.0 * MM_TO_FEET)) * 304.8 / 10, 1)}cm";
                TextNote tn = TextNote.Create(doc, section.Id, textPos, text, doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType));
                
                // 增加引線指向違規點
                Leader leader = tn.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                leader.End = fail.Item1;

                // B. 嘗試建立真實尺寸線 (Dimension)
                try {
                    XYZ p1Raw = fail.Item1;
                    double distToPlane = (p1Raw - failPos).DotProduct(viewDir);
                    XYZ p1 = p1Raw - viewDir * distToPlane; // 投影到視圖平面
                    XYZ p2 = p1 + XYZ.BasisZ * fail.Item4;
                    Line line = Line.CreateBound(p1, p2);
                    
                    ReferenceArray ra = new ReferenceArray();
                    ra.Append(fail.Item2); ra.Append(fail.Item3);
                    doc.Create.NewDimension(section, line, ra);
                } catch {
                    // 只有在面體非平行(非水平)時 Dim 會失敗，則此點僅保留 A 步驟的文字註記
                }
            }
        }

        private void ClearExistingMarkers(Document doc, IdType stairId)
        {
            string failKey = $"MCP_FAIL_STAIR_{stairId}";

            // 精確清理此樓梯的 FilledRegion
            var frCollector = new FilteredElementCollector(doc).OfClass(typeof(FilledRegion)).Cast<FilledRegion>()
                .Where(fr => fr.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == failKey).ToList();
            foreach (var fr in frCollector) doc.Delete(fr.Id);
            
            // 精確清理此樓梯的標註文字
            var textCollector = new FilteredElementCollector(doc).OfClass(typeof(TextNote)).Cast<TextNote>()
                .Where(t => t.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == failKey).ToList();
            foreach (var t in textCollector) doc.Delete(t.Id);

            // 清理舊有的 DirectShape (回溯相容)
            var dsCollector = new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Cast<DirectShape>()
                .Where(ds => ds.Name == "MCP_淨高不合格" || ds.ApplicationId == "RevitMCP").ToList();
            foreach (var ds in dsCollector) doc.Delete(ds.Id);
        }


        private void AddSolidsFromElement(Element elem, Options opt, List<Solid> solids)
        {
            if (elem == null) return;
            GeometryElement geo = elem.get_Geometry(opt);
            if (geo == null) return;
            foreach (GeometryObject obj in geo)
            {
                if (obj is Solid s && s.Volume > 0) solids.Add(s);
                else if (obj is GeometryInstance gi)
                {
                    Transform t = gi.Transform;
                    foreach (GeometryObject symObj in gi.GetSymbolGeometry())
                    {
                        if (symObj is Solid symS && symS.Volume > 0)
                        {
                            solids.Add(SolidUtils.CreateTransformed(symS, t));
                        }
                    }
                }
            }
        }

        private ElementId GetOrCreateRedFilledRegionType(Document doc)
        {
            FilledRegionType type = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>()
                .FirstOrDefault(x => x.Name == "MCP_Stair_Fail_Red");
            if (type != null) return type.Id;

            FilledRegionType existing = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().FirstOrDefault();
            if (existing == null) return ElementId.InvalidElementId;

            FilledRegionType newType = existing.Duplicate("MCP_Stair_Fail_Red") as FilledRegionType;
            newType.ForegroundPatternColor = new Color(255, 0, 0);
            
            FillPatternElement solidFill = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern().IsSolidFill);
            if (solidFill != null) newType.ForegroundPatternId = solidFill.Id;
            
            return newType.Id;
        }

        private void CreateFailFilledRegion(Document doc, ViewPlan view, Face face, ElementId typeId, IdType stairId)
        {
            try
            {
                IList<CurveLoop> loops = face.GetEdgesAsCurveLoops();
                List<CurveLoop> projectedLoops = new List<CurveLoop>();
                double viewZ = view.GenLevel != null ? view.GenLevel.ProjectElevation : view.Origin.Z;
                string failKey = $"MCP_FAIL_STAIR_{stairId}";

                foreach (CurveLoop loop in loops)
                {
                    CurveLoop projected = new CurveLoop();
                    foreach (Curve curve in loop)
                    {
                        XYZ p1 = curve.GetEndPoint(0);
                        XYZ p2 = curve.GetEndPoint(1);
                        projected.Append(Line.CreateBound(new XYZ(p1.X, p1.Y, viewZ), new XYZ(p2.X, p2.Y, viewZ)));
                    }
                    projectedLoops.Add(projected);
                }

                FilledRegion fr = FilledRegion.Create(doc, typeId, view.Id, projectedLoops);
                Parameter commentPara = fr.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentPara != null) commentPara.Set(failKey);
                
                // 加註文字標籤並增加引線 (同樣鎖定 stairId)
                XYZ center = face.Evaluate(new UV(0.5, 0.5));
                XYZ textPos = new XYZ(center.X + (600.0 * MM_TO_FEET), center.Y + (600.0 * MM_TO_FEET), viewZ);
                ElementId textTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                TextNote tn = TextNote.Create(doc, view.Id, textPos, "【淨高不合格】", textTypeId);
                
                // 增加引線指向違規中心點
                Leader leader = tn.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                leader.End = new XYZ(center.X, center.Y, viewZ);

                Parameter tnComment = tn.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (tnComment != null) tnComment.Set(failKey);
            }
            catch { }
        }

        /// <summary>
        /// 建立帶有引線的文字標註 (樓梯專用)
        /// </summary>
        private object CreateTextNoteWithLeader(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType viewId = parameters["viewId"].Value<IdType>();
            double x = parameters["x"].Value<double>() / 304.8;
            double y = parameters["y"].Value<double>() / 304.8;
            double lx = parameters["leaderX"].Value<double>() / 304.8;
            double ly = parameters["leaderY"].Value<double>() / 304.8;
            string text = parameters["text"].Value<string>();

            View view = doc.GetElement(new ElementId(viewId)) as View;
            if (view == null) throw new Exception("找不到視圖");

            using (Transaction trans = new Transaction(doc, "建立帶引線標註"))
            {
                trans.Start();
                TextNote tn = TextNote.Create(doc, view.Id, new XYZ(x, y, 0), text, doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType));
                Leader leader = tn.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                leader.End = new XYZ(lx, ly, 0);
                trans.Commit();
            }

            return new { Success = true };
        }
    }
}
