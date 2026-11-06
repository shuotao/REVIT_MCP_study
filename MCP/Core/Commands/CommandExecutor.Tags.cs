using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        #region get_all_tags

        // 一次撈全專案所有標籤：IndependentTag（門/窗/梁/柱/牆/材料/多類別 等）
        // 以及空間標籤 RoomTag / AreaTag / SpaceTag。
        // 每筆重點回傳：tag 頭座標(mm) + 標註對象（哪根梁/柱/房間，含連結模型）+ 文字 + 所在視圖。
        private object GetAllTags(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string viewFilter = parameters["viewName"]?.Value<string>()?.Trim();
            string categoryFilter = parameters["category"]?.Value<string>()?.Trim();
            bool includeSpatial = parameters["includeSpatialTags"]?.Value<bool>() ?? true;
            int maxCount = parameters["maxCount"]?.Value<int>() ?? 5000;

            var results = new List<object>();

            // ---- IndependentTag ----
            foreach (var tag in new FilteredElementCollector(doc)
                        .OfClass(typeof(IndependentTag))
                        .Cast<IndependentTag>())
            {
                if (results.Count >= maxCount) break;
                var data = ExtractIndependentTag(tag, doc, viewFilter, categoryFilter);
                if (data != null) results.Add(data);
            }

            // ---- 空間標籤（房間 / 面積 / 空間）----
            if (includeSpatial)
            {
                foreach (var tag in new FilteredElementCollector(doc)
                            .OfClass(typeof(SpatialElementTag))
                            .Cast<SpatialElementTag>())
                {
                    if (results.Count >= maxCount) break;
                    var data = ExtractSpatialTag(tag, doc, viewFilter, categoryFilter);
                    if (data != null) results.Add(data);
                }
            }

            return new
            {
                Success = true,
                Count = results.Count,
                MaxCount = maxCount,
                IncludeSpatialTags = includeSpatial,
                Truncated = results.Count >= maxCount,
                Tags = results
            };
        }

        private object ExtractIndependentTag(IndependentTag tag, Document doc, string viewFilter, string categoryFilter)
        {
            string categoryName = tag.Category?.Name ?? "";
            if (!string.IsNullOrEmpty(categoryFilter) && !categoryName.Contains(categoryFilter))
                return null;

            string viewName = (doc.GetElement(tag.OwnerViewId) as View)?.Name ?? "";
            if (!string.IsNullOrEmpty(viewFilter) && viewName != viewFilter)
                return null;

            var tagType = doc.GetElement(tag.GetTypeId()) as ElementType;

            string tagText = "";
            try { tagText = tag.TagText ?? ""; } catch { }

            // 標註對象（哪根梁/柱…）：走 references 以同時支援本模型與連結模型
            var tagged = new List<object>();
            try
            {
                foreach (var refr in tag.GetTaggedReferences())
                {
                    var d = DescribeTaggedReference(doc, refr, tag);
                    if (d != null) tagged.Add(d);
                }
            }
            catch { }

            return new
            {
                TagId = tag.Id.GetIdValue(),
                TagKind = "IndependentTag",
                Category = categoryName,
                Family = tagType?.FamilyName ?? "",
                Type = tagType?.Name ?? "",
                TagText = tagText,
                HasLeader = SafeGet(() => tag.HasLeader, false),
                Orientation = SafeGet(() => tag.TagOrientation.ToString(), ""),
                ViewId = tag.OwnerViewId.GetIdValue(),
                ViewName = viewName,
                TagHeadPosition = ToMm(SafeGet<XYZ>(() => tag.TagHeadPosition, null)),
                TaggedElements = tagged
            };
        }

        private object ExtractSpatialTag(SpatialElementTag tag, Document doc, string viewFilter, string categoryFilter)
        {
            string categoryName = tag.Category?.Name ?? "";
            if (!string.IsNullOrEmpty(categoryFilter) && !categoryName.Contains(categoryFilter))
                return null;

            string viewName = (doc.GetElement(tag.OwnerViewId) as View)?.Name ?? "";
            if (!string.IsNullOrEmpty(viewFilter) && viewName != viewFilter)
                return null;

            string kind = "SpatialElementTag";
            SpatialElement se = null;
            if (tag is RoomTag rt) { kind = "RoomTag"; se = SafeGet<SpatialElement>(() => rt.Room, null); }
            else if (tag is SpaceTag sp) { kind = "SpaceTag"; se = SafeGet<SpatialElement>(() => sp.Space, null); }
            else if (tag is AreaTag at) { kind = "AreaTag"; se = SafeGet<SpatialElement>(() => at.Area, null); }

            var tagged = new List<object>();
            string number = "";
            string spaceName = "";
            if (se != null)
            {
                try { number = se.Number ?? ""; } catch { }
                spaceName = se.Name ?? "";
                tagged.Add(new
                {
                    ElementId = se.Id.GetIdValue(),
                    Source = "主模型",
                    Category = se.Category?.Name ?? "",
                    Name = spaceName,
                    Number = number
                });
            }

            var tagType = doc.GetElement(tag.GetTypeId()) as ElementType;

            return new
            {
                TagId = tag.Id.GetIdValue(),
                TagKind = kind,
                Category = categoryName,
                Family = tagType?.FamilyName ?? "",
                Type = tagType?.Name ?? "",
                TagText = string.IsNullOrEmpty(number) ? spaceName : (spaceName + " " + number).Trim(),
                HasLeader = SafeGet(() => tag.HasLeader, false),
                ViewId = tag.OwnerViewId.GetIdValue(),
                ViewName = viewName,
                TagHeadPosition = ToMm(SafeGet<XYZ>(() => tag.TagHeadPosition, null)),
                TaggedElements = tagged
            };
        }

        // 描述單一被標註的元素（本模型或連結模型），並附引線末端座標(若有)
        private object DescribeTaggedReference(Document doc, Reference refr, IndependentTag tag)
        {
            Element hostEl = null;
            try { hostEl = doc.GetElement(refr.ElementId); } catch { }

            Element target = hostEl;
            string source = "主模型";

            if (refr.LinkedElementId != ElementId.InvalidElementId && hostEl is RevitLinkInstance rli)
            {
                var linkDoc = rli.GetLinkDocument();
                if (linkDoc != null)
                {
                    var le = linkDoc.GetElement(refr.LinkedElementId);
                    if (le != null) { target = le; source = "連結:" + linkDoc.Title; }
                }
            }

            if (target == null) return null;

            // 引線末端（best-effort：附引線且可見才取）
            object leaderEnd = null;
            try
            {
#if REVIT2023_OR_GREATER
                if (tag.HasLeader && tag.IsLeaderVisible(refr))
#else
                if (tag.HasLeader) // R22 API 無 IsLeaderVisible；GetLeaderEnd 失敗由 catch 吞掉
#endif
                    leaderEnd = ToMm(tag.GetLeaderEnd(refr));
            }
            catch { }

            var et = target.Document.GetElement(target.GetTypeId()) as ElementType;

            return new
            {
                ElementId = target.Id.GetIdValue(),
                Source = source,
                Category = target.Category?.Name ?? "",
                Name = target.Name,
                TypeName = et?.Name ?? "",
                FamilyName = et?.FamilyName ?? "",
                Mark = target.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "",
                LeaderEnd = leaderEnd
            };
        }

        // 內部單位(feet) XYZ → mm 物件；null 安全
        private object ToMm(XYZ p)
        {
            if (p == null) return null;
            return new
            {
                X = Math.Round(p.X * 304.8, 2),
                Y = Math.Round(p.Y * 304.8, 2),
                Z = Math.Round(p.Z * 304.8, 2)
            };
        }

        private T SafeGet<T>(Func<T> getter, T fallback)
        {
            try { return getter(); } catch { return fallback; }
        }

        #endregion
    }
}
