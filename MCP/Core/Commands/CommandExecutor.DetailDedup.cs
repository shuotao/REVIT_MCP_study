using System;
using System.Collections.Generic;
using System.Globalization;
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
        #region Detail Element Dedup

        /// <summary>
        /// 找出當前視圖（或指定視圖）中重複的 detail element：
        /// - 重複定義：同 Type（TypeId/LineStyle 相同）+ 位置量化後相同
        /// - 處理規則：若副本有任一個在 Detail Group 內，保留 group 內的，刪除 group 外的
        /// - 邊界情形（不動，僅回報）：
        ///     * 全部副本都在 group 中（"all_in_groups"）
        ///     * 全部副本都不在 group 中（"ambiguous_no_group"）
        /// 預設 dryRun=true，只列清單不實際刪除。
        /// 涵蓋類別：DetailComponent / DetailCurve / FilledRegion / TextNote / Dimension。
        /// 只認 Detail Group（OST_IOSDetailGroups），不認 Model Group。
        /// </summary>
        private object DedupDetailElementsInView(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            // === 解析參數 ===
            IdType viewIdValue = parameters["viewId"]?.Value<IdType>() ?? 0;
            View targetView;
            if (viewIdValue == 0)
            {
                targetView = doc.ActiveView;
                if (targetView == null) throw new Exception("沒有 active view，請指定 viewId");
            }
            else
            {
                targetView = doc.GetElement(RevitCompatibility.ToElementId(viewIdValue)) as View;
                if (targetView == null) throw new Exception($"找不到視圖 ID: {viewIdValue}");
            }
            if (targetView.IsTemplate) throw new Exception($"視圖 '{targetView.Name}' 是 ViewTemplate，無法處理");

            var categoriesArr = parameters["categories"]?.ToObject<List<string>>() ?? new List<string> { "All" };
            var categories = new HashSet<string>(categoriesArr, StringComparer.OrdinalIgnoreCase);
            bool processAll = categories.Contains("All");

            double toleranceMm = parameters["tolerance"]?.Value<double>() ?? 1.0;
            if (toleranceMm <= 0) toleranceMm = 1.0;

            bool dryRun = parameters["dryRun"] == null ? true : parameters["dryRun"].Value<bool>();

            // === 蒐集 elements（限定於目標 view） ===
            var collected = new List<Element>();
            int detailComponentCount = 0, detailCurveCount = 0, filledRegionCount = 0, textNoteCount = 0, dimensionCount = 0;

            if (processAll || categories.Contains("DetailComponent"))
            {
                var items = new FilteredElementCollector(doc, targetView.Id)
                    .OfCategory(BuiltInCategory.OST_DetailComponents)
                    .WhereElementIsNotElementType()
                    .ToList();
                detailComponentCount = items.Count;
                collected.AddRange(items);
            }
            if (processAll || categories.Contains("DetailCurve"))
            {
                var items = new FilteredElementCollector(doc, targetView.Id)
                    .OfClass(typeof(CurveElement))
                    .Where(e => e is DetailCurve)
                    .ToList();
                detailCurveCount = items.Count;
                collected.AddRange(items);
            }
            if (processAll || categories.Contains("FilledRegion"))
            {
                var items = new FilteredElementCollector(doc, targetView.Id)
                    .OfClass(typeof(FilledRegion))
                    .ToList();
                filledRegionCount = items.Count;
                collected.AddRange(items);
            }
            if (processAll || categories.Contains("TextNote"))
            {
                var items = new FilteredElementCollector(doc, targetView.Id)
                    .OfClass(typeof(TextNote))
                    .ToList();
                textNoteCount = items.Count;
                collected.AddRange(items);
            }
            if (processAll || categories.Contains("Dimension"))
            {
                var items = new FilteredElementCollector(doc, targetView.Id)
                    .OfClass(typeof(Dimension))
                    .WhereElementIsNotElementType()
                    .ToList();
                dimensionCount = items.Count;
                collected.AddRange(items);
            }

            // === 建立 memberId → DetailGroup map（只認 Detail Group） ===
            var memberToDetailGroup = new Dictionary<ElementId, GroupRef>();
            var detailGroups = new FilteredElementCollector(doc, targetView.Id)
                .OfClass(typeof(Group))
                .Cast<Group>()
                .Where(g => g.Category != null
                            && g.Category.Id == new ElementId(BuiltInCategory.OST_IOSDetailGroups))
                .ToList();
            foreach (var g in detailGroups)
            {
                string gName = g.Name;
                ElementId gId = g.Id;
                foreach (var mid in g.GetMemberIds())
                {
                    memberToDetailGroup[mid] = new GroupRef { GroupId = gId, GroupName = gName };
                }
            }

            // === 為每個 element 算出 (TypeKey, PosKey) ===
            var keyed = new List<KeyedElem>();
            int unkeyedCount = 0;
            foreach (var el in collected)
            {
                string typeKey = ComputeTypeKey(el);
                string posKey = ComputePositionKey(el, toleranceMm, targetView);
                if (typeKey == null || posKey == null) { unkeyedCount++; continue; }
                keyed.Add(new KeyedElem { Elem = el, TypeKey = typeKey, PosKey = posKey });
            }

            // === 依 (TypeKey + PosKey) 分組，保留 count > 1 的 ===
            var dupGroups = keyed
                .GroupBy(k => k.TypeKey + "||" + k.PosKey)
                .Where(g => g.Count() > 1)
                .ToList();

            var duplicateGroups = new List<object>();
            var toDeleteIds = new List<IdType>();
            int ambiguousCount = 0;
            int allInGroupsCount = 0;
            int toDedupGroupCount = 0;

            foreach (var grp in dupGroups)
            {
                var members = grp.ToList();
                var inGrp = members.Where(m => memberToDetailGroup.ContainsKey(m.Elem.Id)).ToList();
                var outGrp = members.Where(m => !memberToDetailGroup.ContainsKey(m.Elem.Id)).ToList();

                string status;
                if (inGrp.Count == 0)
                {
                    status = "ambiguous_no_group";
                    ambiguousCount++;
                }
                else if (outGrp.Count == 0)
                {
                    status = "all_in_groups";
                    allInGroupsCount++;
                }
                else
                {
                    status = "to_dedup";
                    toDedupGroupCount++;
                    foreach (var m in outGrp)
                    {
                        toDeleteIds.Add(m.Elem.Id.GetIdValue());
                    }
                }

                duplicateGroups.Add(new
                {
                    TypeKey = members[0].TypeKey,
                    PositionKey = members[0].PosKey,
                    Status = status,
                    MemberCount = members.Count,
                    InGroupCount = inGrp.Count,
                    OutGroupCount = outGrp.Count,
                    Members = members.Select(m =>
                    {
                        bool isInGroup = memberToDetailGroup.TryGetValue(m.Elem.Id, out var gi);
                        return new
                        {
                            ElementId = m.Elem.Id.GetIdValue(),
                            Category = m.Elem.Category?.Name,
                            TypeName = doc.GetElement(m.Elem.GetTypeId())?.Name,
                            InDetailGroup = isInGroup,
                            GroupId = isInGroup ? (object)gi.GroupId.GetIdValue() : null,
                            GroupName = isInGroup ? gi.GroupName : null,
                            WillBeDeleted = !isInGroup && status == "to_dedup"
                        };
                    }).ToList()
                });
            }

            // === 執行刪除（非 dryRun） ===
            int deletedCount = 0;
            var deleteErrors = new List<object>();
            if (!dryRun && toDeleteIds.Count > 0)
            {
                using (Transaction trans = TransactionHelper.Begin(doc, "Dedup detail elements"))
                {
                    trans.Start();
                    foreach (var id in toDeleteIds)
                    {
                        try
                        {
                            var elId = RevitCompatibility.ToElementId(id);
                            var el = doc.GetElement(elId);
                            if (el != null)
                            {
                                doc.Delete(elId);
                                deletedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            deleteErrors.Add(new { ElementId = id, Error = ex.Message });
                        }
                    }
                    trans.Commit();
                }
            }

            return new
            {
                ViewId = targetView.Id.GetIdValue(),
                ViewName = targetView.Name,
                ViewType = targetView.ViewType.ToString(),
                DryRun = dryRun,
                ToleranceMm = toleranceMm,
                Scanned = new
                {
                    DetailComponent = detailComponentCount,
                    DetailCurve = detailCurveCount,
                    FilledRegion = filledRegionCount,
                    TextNote = textNoteCount,
                    Dimension = dimensionCount,
                    Total = collected.Count,
                    UnkeyedSkipped = unkeyedCount
                },
                DetailGroupsInView = detailGroups.Count,
                DuplicateGroupCount = duplicateGroups.Count,
                ToDedupGroupCount = toDedupGroupCount,
                AmbiguousNoGroupCount = ambiguousCount,
                AllInGroupsCount = allInGroupsCount,
                ToDeleteCount = toDeleteIds.Count,
                ToDeleteIds = toDeleteIds,
                DeletedCount = deletedCount,
                DeleteErrors = deleteErrors,
                DuplicateGroups = duplicateGroups,
                Notes = new[]
                {
                    "Status='to_dedup' = will delete out-of-group copies, keep in-group copies",
                    "Status='all_in_groups' = all duplicates inside detail groups, NOT touched",
                    "Status='ambiguous_no_group' = no copy is inside any detail group, NOT touched (no group reference)",
                    "Tolerance is in mm; coordinates are quantized to this granularity for matching",
                    "Only Detail Groups (OST_IOSDetailGroups) are recognized; Model Groups are ignored"
                }
            };
        }

        /// <summary>
        /// Type Key：用 Category + TypeId 為主；DetailCurve 沒 GetTypeId，改用 LineStyle.Id。
        /// 這樣「同 Type」即定義為 TypeId 相同（自動意涵同 Family + 同 Type）。
        /// </summary>
        private string ComputeTypeKey(Element el)
        {
            if (el is DetailCurve dc)
            {
                var lsId = dc.LineStyle?.Id;
                return $"DetailCurve:LS{(lsId != null ? lsId.GetIdValue().ToString() : "null")}";
            }
            var typeId = el.GetTypeId();
            string catId = el.Category?.Id.GetIdValue().ToString() ?? "null";
            if (typeId != ElementId.InvalidElementId)
            {
                return $"Cat{catId}:T{typeId.GetIdValue()}";
            }
            return $"Cat{catId}:NoType";
        }

        /// <summary>
        /// Position Key：依類別取不同幾何特徵 + 量化容差。
        /// 回 null 表示無法決定位置（會被計入 UnkeyedSkipped）。
        /// </summary>
        private string ComputePositionKey(Element el, double toleranceMm, View view)
        {
            string Q(double mm) => Math.Round(mm / toleranceMm).ToString("R", CultureInfo.InvariantCulture);
            string Pt(XYZ p) => p == null ? "null" : $"{Q(p.X * 304.8)},{Q(p.Y * 304.8)},{Q(p.Z * 304.8)}";

            // FamilyInstance（DetailComponent 等）→ LocationPoint
            if (el is FamilyInstance fi)
            {
                var lp = fi.Location as LocationPoint;
                if (lp != null) return $"pt:{Pt(lp.Point)}";
                var bbF = TryBBox(el, view);
                if (bbF != null) return $"bbox:{Pt(bbF.Min)}|{Pt(bbF.Max)}";
                return null;
            }

            // DetailCurve → 兩端點（排序後讓方向對調的同曲線視為相同）
            if (el is DetailCurve dc)
            {
                Curve crv = null;
                try { crv = dc.GeometryCurve; } catch { crv = null; }
                if (crv == null) return null;
                XYZ p0, p1;
                try { p0 = crv.GetEndPoint(0); p1 = crv.GetEndPoint(1); }
                catch { return null; }
                var s0 = Pt(p0);
                var s1 = Pt(p1);
                var sorted = string.Compare(s0, s1, StringComparison.Ordinal) <= 0 ? $"{s0}|{s1}" : $"{s1}|{s0}";
                return $"crv:{sorted}";
            }

            // FilledRegion → BoundingBox（沒有 LocationPoint）
            if (el is FilledRegion)
            {
                var bb = TryBBox(el, view);
                if (bb == null) return null;
                return $"fr:{Pt(bb.Min)}|{Pt(bb.Max)}";
            }

            // TextNote → Coord + 文字內容（用 hash 避免長文字當 key）
            if (el is TextNote tn)
            {
                XYZ coord = null;
                try { coord = tn.Coord; } catch { coord = null; }
                string text = tn.Text ?? "";
                int textHash = text.GetHashCode();
                return $"text:{Pt(coord)}|L{text.Length}|H{textHash}";
            }

            // Dimension → BoundingBox 中心 + ValueString
            if (el is Dimension dim)
            {
                XYZ center = null;
                var bb = TryBBox(el, view);
                if (bb != null) center = (bb.Min + bb.Max) * 0.5;
                else
                {
                    try { center = dim.Origin; } catch { center = null; }
                }
                string val = "";
                try { val = dim.ValueString ?? ""; } catch { }
                return $"dim:{Pt(center)}|V{val}";
            }

            // 其他 → BoundingBox
            var def = TryBBox(el, view);
            if (def == null) return null;
            return $"bbox:{Pt(def.Min)}|{Pt(def.Max)}";
        }

        /// <summary>
        /// 安全包裝：先試 view-specific bbox，失敗 fallback 到 model bbox。
        /// </summary>
        private static BoundingBoxXYZ TryBBox(Element el, View view)
        {
            try
            {
                var bb = el.get_BoundingBox(view);
                if (bb != null) return bb;
            }
            catch { }
            try { return el.get_BoundingBox(null); }
            catch { return null; }
        }

        private class GroupRef
        {
            public ElementId GroupId { get; set; }
            public string GroupName { get; set; }
        }

        private class KeyedElem
        {
            public Element Elem { get; set; }
            public string TypeKey { get; set; }
            public string PosKey { get; set; }
        }

        #endregion
    }
}
