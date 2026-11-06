using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCP.Core.Review;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// batch_modify_elements — 「調整現有模型」的安全批次編輯。
    ///
    /// 防錯設計：
    ///   1. 預設 dryRun=true：在 TransactionGroup 內實際執行並提交，讓 Revit 跑完接合 / 約束 / 警告檢查，
    ///      收集結果後整組 RollBack，模型不會被改動。確認無誤再以 dryRun=false 正式執行。
    ///   2. 原子性：任何一個操作失敗 → 整批回滾，不會留下改一半的模型。
    ///   3. 警告與錯誤由 CollectingFailuresPreprocessor 收集回傳，不跳對話框；
    ///      rollbackOnWarning=true 時連 Warning 都視為失敗。
    ///   4. 參數數值：數字或字串都以「專案顯示單位」解讀（SetValueString），回報修改前後值，
    ///      避免把 mm 當成英尺寫入的經典錯誤。
    ///   5. 正式執行的交易名稱以「AI: 」開頭，使用者可在 Revit 直接 Ctrl+Z 復原。
    ///   刻意不支援刪除（請用既有 delete_element），讓本工具維持非破壞性。
    /// </summary>
    public partial class CommandExecutor
    {
        private object BatchModifyElements(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument?.Document ?? throw new Exception("目前沒有開啟的專案");
            var ops = parameters["operations"] as JArray;
            if (ops == null || ops.Count == 0) throw new Exception("operations 不可為空");
            if (ops.Count > 200) throw new Exception("單次最多 200 個操作，請分批");
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? true;
            bool rollbackOnWarning = parameters["rollbackOnWarning"]?.Value<bool>() ?? false;
            string title = parameters["transactionName"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(title)) title = "批次調整模型";
            string txName = "AI: " + title;

            int warningsBefore = SafeWarningCount(doc);
            var opResults = new List<object>();
            var collector = new CollectingFailuresPreprocessor(rollbackOnWarning);
            bool committed = false;
            string abortReason = null;

            using (var tg = new TransactionGroup(doc, txName))
            {
                tg.Start();
                using (var t = new Transaction(doc, txName))
                {
                    var fho = t.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(collector);
                    fho.SetClearAfterRollback(true);
                    t.SetFailureHandlingOptions(fho);
                    t.Start();
                    try
                    {
                        for (int i = 0; i < ops.Count; i++)
                        {
                            var op = ops[i] as JObject ?? throw new Exception($"第 {i + 1} 個操作格式錯誤");
                            opResults.Add(RunBatchOp(doc, op, i + 1));
                        }
                    }
                    catch (Exception ex)
                    {
                        abortReason = ex.Message;
                        if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                    }

                    if (abortReason == null)
                    {
                        var status = t.Commit();
                        committed = status == TransactionStatus.Committed;
                        if (!committed && abortReason == null)
                            abortReason = collector.RolledBackByFailures ? "Revit 回報錯誤（或 rollbackOnWarning 擋下警告），已整批回滾" : $"交易未提交：{status}";
                    }
                }

                // 讀取正式提交後的警告數，再決定保留或回滾整組
                int warningsAfter = committed ? SafeWarningCount(doc) : warningsBefore;
                bool keep = committed && !dryRun;
                if (keep) tg.Assimilate(); else tg.RollBack();

                return new
                {
                    dryRun,
                    applied = keep,
                    success = committed,
                    message = !committed
                        ? $"未套用：{abortReason}"
                        : dryRun
                            ? "試跑成功，模型未變更。確認結果後以 dryRun=false 正式執行。"
                            : $"已套用。可在 Revit 以 Ctrl+Z 復原（交易：{txName}）。",
                    abortReason,
                    // Warning 由 preprocessor 收集後刪除（避免彈窗），不會留在 doc.GetWarnings()；此陣列是唯一紀錄
                    newWarningCount = collector.Warnings.Count,
                    modelWarningCountAfter = warningsAfter,
                    warnings = collector.Warnings,
                    errors = collector.Errors,
                    operations = opResults
                };
            }
        }

        private static int SafeWarningCount(Document doc)
        {
            try { return doc.GetWarnings().Count; } catch { return 0; }
        }

        private object RunBatchOp(Document doc, JObject op, int index)
        {
            string action = (op["action"]?.Value<string>() ?? "").Trim().ToLowerInvariant();
            var ids = (op["elementIds"] as JArray)?.Select(x => new ElementId(x.Value<IdType>())).ToList() ?? new List<ElementId>();
            if (ids.Count == 0) throw new Exception($"第 {index} 個操作（{action}）缺少 elementIds");
            var elements = ids.Select(id => doc.GetElement(id) ?? throw new Exception($"第 {index} 個操作：找不到元素 {id.GetIdValue()}")).ToList();

            switch (action)
            {
                case "set_parameter": return OpSetParameter(doc, op, elements, index);
                case "change_type": return OpChangeType(doc, op, elements, index);
                case "move":
                case "copy":
                    {
                        XYZ v = new XYZ(ReviewUtil.Ft(op["dx"]?.Value<double>() ?? 0), ReviewUtil.Ft(op["dy"]?.Value<double>() ?? 0), ReviewUtil.Ft(op["dz"]?.Value<double>() ?? 0));
                        if (v.IsZeroLength()) throw new Exception($"第 {index} 個操作：位移量為 0");
                        CheckPinned(elements, index, action);
                        if (action == "move")
                        {
                            ElementTransformUtils.MoveElements(doc, ids, v);
                            return new { index, action, count = ids.Count, offsetMm = ReviewUtil.PointMm(v) };
                        }
                        var created = ElementTransformUtils.CopyElements(doc, ids, v);
                        return new { index, action, count = ids.Count, offsetMm = ReviewUtil.PointMm(v), newElementIds = created.Select(x => x.GetIdValue()).ToList(), note = "試跑時的新 ID 僅供參考，正式執行會不同" };
                    }
                case "rotate":
                    {
                        double deg = op["angleDeg"]?.Value<double>() ?? throw new Exception($"第 {index} 個操作：rotate 需要 angleDeg");
                        CheckPinned(elements, index, action);
                        XYZ c;
                        if (op["centerX"] != null && op["centerY"] != null)
                            c = new XYZ(ReviewUtil.Ft(op["centerX"].Value<double>()), ReviewUtil.Ft(op["centerY"].Value<double>()), 0);
                        else
                        {
                            var boxes = elements.Select(e => ReviewUtil.HostBox(e, Transform.Identity)).Where(b => b != null).Select(b => b.Value).ToList();
                            if (boxes.Count == 0) throw new Exception($"第 {index} 個操作：無法計算旋轉中心，請提供 centerX/centerY");
                            c = new XYZ((boxes.Min(b => b.min.X) + boxes.Max(b => b.max.X)) / 2, (boxes.Min(b => b.min.Y) + boxes.Max(b => b.max.Y)) / 2, 0);
                        }
                        Line axis = Line.CreateBound(c, c + XYZ.BasisZ);
                        ElementTransformUtils.RotateElements(doc, ids, axis, deg * Math.PI / 180.0);
                        return new { index, action, count = ids.Count, angleDeg = deg, centerMm = new { x = ReviewUtil.Mm(c.X), y = ReviewUtil.Mm(c.Y) } };
                    }
                case "delete":
                    throw new Exception("batch_modify_elements 不支援刪除，請改用 delete_element（需使用者確認）");
                default:
                    throw new Exception($"第 {index} 個操作：不支援的 action「{action}」（可用：set_parameter, change_type, move, copy, rotate）");
            }
        }

        private static void CheckPinned(List<Element> elements, int index, string action)
        {
            var pinned = elements.Where(e => e.Pinned).Select(e => e.Id.GetIdValue()).ToList();
            if (pinned.Count > 0)
                throw new Exception($"第 {index} 個操作（{action}）：元素已鎖定（Pinned），為避免誤動不執行：{string.Join(",", pinned.Take(20))}");
        }

        private object OpSetParameter(Document doc, JObject op, List<Element> elements, int index)
        {
            string pname = op["parameterName"]?.Value<string>() ?? throw new Exception($"第 {index} 個操作：缺少 parameterName");
            JToken value = op["value"] ?? throw new Exception($"第 {index} 個操作：缺少 value");
            bool onType = string.Equals(op["target"]?.Value<string>(), "type", StringComparison.OrdinalIgnoreCase);

            // 套到類型時，同類型只改一次
            var targets = onType
                ? elements.Select(e => doc.GetElement(e.GetTypeId())).Where(t => t != null).GroupBy(t => t.Id.GetIdValue()).Select(g => g.First()).ToList()
                : elements;
            if (targets.Count == 0) throw new Exception($"第 {index} 個操作：找不到可修改的{(onType ? "類型" : "元素")}");

            var details = new List<object>();
            foreach (var el in targets)
            {
                Parameter p = FindParam(el, pname)
                    ?? throw new Exception($"第 {index} 個操作：元素 {el.Id.GetIdValue()} 找不到參數「{pname}」" + (onType ? "" : "（若是類型參數請加 target:\"type\"）"));
                if (p.IsReadOnly) throw new Exception($"第 {index} 個操作：參數「{pname}」是唯讀的（元素 {el.Id.GetIdValue()}）");
                string before = ParamDisplay(p);
                SetParamValue(doc, p, value, index);
                details.Add(new { id = el.Id.GetIdValue(), before, after = ParamDisplay(p) });
            }
            return new
            {
                index,
                action = "set_parameter",
                parameter = pname,
                target = onType ? "type" : "instance",
                affectedInstancesNote = onType ? "修改類型參數會影響該類型的所有實例" : null,
                changes = details.Take(50).ToList(),
                changedCount = details.Count
            };
        }

        private static Parameter FindParam(Element e, string name)
        {
            Parameter p = e.LookupParameter(name);
            if (p != null) return p;
            if (Enum.TryParse(name, true, out BuiltInParameter bip))
            {
                try { p = e.get_Parameter(bip); } catch { }
            }
            return p;
        }

        private static string ParamDisplay(Parameter p)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String: return p.AsString();
                    case StorageType.ElementId:
                        var id = p.AsElementId();
                        var el = id == null || id == ElementId.InvalidElementId ? null : p.Element.Document.GetElement(id);
                        return el != null ? el.Name : p.AsValueString();
                    default: return p.AsValueString() ?? (p.StorageType == StorageType.Integer ? p.AsInteger().ToString() : p.AsDouble().ToString(CultureInfo.InvariantCulture));
                }
            }
            catch { return null; }
        }

        private static void SetParamValue(Document doc, Parameter p, JToken value, int index)
        {
            bool ok;
            switch (p.StorageType)
            {
                case StorageType.String:
                    ok = p.Set(value.Type == JTokenType.Null ? "" : value.ToString());
                    break;

                case StorageType.Double:
                    {
                        // 以專案顯示單位解讀，例如專案為 mm 時 3000 = 3000 mm
                        string s = value.Type == JTokenType.Float || value.Type == JTokenType.Integer
                            ? value.Value<double>().ToString(CultureInfo.InvariantCulture)
                            : value.ToString();
                        ok = p.SetValueString(s);
                        break;
                    }

                case StorageType.Integer:
                    {
                        if (value.Type == JTokenType.Boolean) { ok = p.Set(value.Value<bool>() ? 1 : 0); break; }
                        string s = value.ToString().Trim();
                        string sl = s.ToLowerInvariant();
                        if (sl == "true" || sl == "yes" || s == "是") { ok = p.Set(1); break; }
                        if (sl == "false" || sl == "no" || s == "否") { ok = p.Set(0); break; }
                        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv)) { ok = p.Set(iv); break; }
                        ok = p.SetValueString(s);
                        break;
                    }

                case StorageType.ElementId:
                    {
                        ElementId target;
                        if (value.Type == JTokenType.Integer) target = new ElementId(value.Value<IdType>());
                        else
                        {
                            string name = value.ToString();
                            Element found = new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstOrDefault(l => l.Name == name)
                                ?? new FilteredElementCollector(doc).WhereElementIsElementType().FirstOrDefault(t => t.Name == name)
                                ?? new FilteredElementCollector(doc).OfClass(typeof(Material)).FirstOrDefault(m => m.Name == name);
                            target = found?.Id ?? throw new Exception($"第 {index} 個操作：找不到名稱為「{name}」的樓層 / 類型 / 材料");
                        }
                        ok = p.Set(target);
                        break;
                    }

                default:
                    throw new Exception($"第 {index} 個操作：不支援的參數儲存型別 {p.StorageType}");
            }
            if (!ok) throw new Exception($"第 {index} 個操作：參數「{p.Definition?.Name}」設定失敗（值：{value}）");
        }

        private object OpChangeType(Document doc, JObject op, List<Element> elements, int index)
        {
            IdType typeIdVal = op["typeId"]?.Value<IdType>() ?? 0;
            string typeName = op["typeName"]?.Value<string>();
            string familyName = op["familyName"]?.Value<string>();
            if (typeIdVal == 0 && string.IsNullOrWhiteSpace(typeName)) throw new Exception($"第 {index} 個操作：change_type 需要 typeId 或 typeName");

            var details = new List<object>();
            foreach (var e in elements)
            {
                ElementId newType = null;
                var valid = e.GetValidTypes();
                if (typeIdVal != 0)
                {
                    var cand = new ElementId(typeIdVal);
                    if (valid.Contains(cand)) newType = cand;
                }
                else
                {
                    var matches = valid.Select(id => doc.GetElement(id) as ElementType)
                        .Where(t => t != null && t.Name == typeName && (string.IsNullOrEmpty(familyName) || t.FamilyName == familyName)).ToList();
                    if (matches.Count > 1) throw new Exception($"第 {index} 個操作：類型名稱「{typeName}」對應多個族群（{string.Join(", ", matches.Select(m => m.FamilyName))}），請加 familyName");
                    newType = matches.FirstOrDefault()?.Id;
                }
                if (newType == null)
                    throw new Exception($"第 {index} 個操作：元素 {e.Id.GetIdValue()} 不能換成指定類型（可用類型請先用 get_types_by_category 確認）");
                string before = ReviewUtil.TypeName(e);
                e.ChangeTypeId(newType);
                details.Add(new { id = e.Id.GetIdValue(), before, after = ReviewUtil.TypeName(e) });
            }
            return new { index, action = "change_type", changedCount = details.Count, changes = details.Take(50).ToList() };
        }
    }
}
