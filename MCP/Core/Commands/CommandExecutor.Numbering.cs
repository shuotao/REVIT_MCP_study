using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

// Revit 2025+ ElementId: int → long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 順序編號命令，兩個產出物完全不同的工具：
    ///
    /// - RenumberParkingSpaces：寫入停車格參數（預設「備註」）。方法定義於 domain/parking-auto-numbering.md。
    /// - CreateSequenceNumbers：在圖面上放置編號 TextNote。方法定義於 domain/sequence-numbering.md。
    ///
    /// 排序沿用 CommandExecutor.RoomRenumber.cs 的 OrderRoomsTopDownLeftRight —— 那是同一套
    /// 「Y 分排、X 排序」規則，另外複製一份只會讓兩邊各自漂移。
    ///
    /// 編號字串生成（跳 4、字母序）抽成 static 純函式，不吃 Revit 型別。
    /// </summary>
    public partial class CommandExecutor
    {
        #region 共用編號邏輯

        /// <summary>編號含數字 4 與否。跳 4 是台灣圖面慣例，不是法規要求。</summary>
        internal static bool ContainsFour(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture).IndexOf('4') >= 0;
        }

        /// <summary>
        /// 從 start 開始取 count 個數字；skipFour=true 時跳過任何含 4 的數字。
        /// 純函式。
        /// </summary>
        internal static List<long> BuildNumberSequence(long start, int count, bool skipFour)
        {
            var result = new List<long>();
            long current = start;
            // 上限保護：跳 4 在極端起點下仍會前進，但不允許無限迴圈
            long guard = 0;
            long guardLimit = (long)count * 100 + 10000;

            while (result.Count < count)
            {
                if (++guard > guardLimit)
                    throw new Exception($"產生編號序列時超過安全上限（start={start}, count={count}, skipFour={skipFour}）。");

                if (!skipFour || !ContainsFour(current))
                    result.Add(current);
                current++;
            }
            return result;
        }

        /// <summary>
        /// 序數轉字母標籤：1→a、26→z、27→aa、28→ab。index 從 1 起算。
        /// 純函式。
        /// </summary>
        internal static string ToLetterLabel(long index, bool upperCase)
        {
            if (index < 1)
                throw new Exception($"字母序的起始序數必須 ≥ 1，收到 {index}。");

            var chars = new List<char>();
            long remaining = index;
            while (remaining > 0)
            {
                long rem = (remaining - 1) % 26;
                chars.Insert(0, (char)('a' + rem));
                remaining = (remaining - 1) / 26;
            }
            string label = new string(chars.ToArray());
            return upperCase ? label.ToUpperInvariant() : label;
        }

        /// <summary>取元件中心點（mm）：優先 LocationPoint，其次 BoundingBox 中心。</summary>
        private static bool TryGetElementCenterMm(Element element, out double xMm, out double yMm)
        {
            const double feetToMm = 304.8;

            LocationPoint locationPoint = element.Location as LocationPoint;
            if (locationPoint != null)
            {
                xMm = locationPoint.Point.X * feetToMm;
                yMm = locationPoint.Point.Y * feetToMm;
                return true;
            }

            BoundingBoxXYZ bbox = element.get_BoundingBox(null);
            if (bbox != null)
            {
                xMm = (bbox.Min.X + bbox.Max.X) / 2.0 * feetToMm;
                yMm = (bbox.Min.Y + bbox.Max.Y) / 2.0 * feetToMm;
                return true;
            }

            xMm = 0;
            yMm = 0;
            return false;
        }

        /// <summary>
        /// 把已排序的序列旋轉到指定元件開頭。找不到該元件時回傳 false，
        /// 呼叫端應停止並回報 —— 靜默沿用預設起點會讓整批編號從錯的地方開始。
        /// 純函式。
        /// </summary>
        internal static bool TryRotateToStart(List<RoomOrderPoint> ordered, string startId, out List<RoomOrderPoint> rotated)
        {
            rotated = ordered;
            if (string.IsNullOrEmpty(startId))
                return true;

            int index = ordered.FindIndex(p => p.Id == startId);
            if (index < 0)
                return false;

            rotated = ordered.Skip(index).Concat(ordered.Take(index)).ToList();
            return true;
        }

        /// <summary>
        /// 判斷某個車位是否要被排除，並回報是依哪一條規則。純函式，只吃字串。
        ///
        /// 兩條規則刻意分開：excludeIds 是一次性的手動指定，excludeValues 是依「參數現值」
        /// 保護既有註記（例如「變更」），下次再跑一樣有效，不必維護一份會過期的 ElementId 清單。
        /// </summary>
        internal static bool IsExcluded(
            string idString,
            string currentValue,
            HashSet<string> excludeIds,
            HashSet<string> excludeValues,
            out string reason)
        {
            reason = null;

            if (excludeIds != null && excludeIds.Contains(idString))
            {
                reason = "excludeElementIds 明確指定排除。";
                return true;
            }

            if (excludeValues != null && !string.IsNullOrEmpty(currentValue) && excludeValues.Contains(currentValue))
            {
                reason = "目前值 '" + currentValue + "' 命中 excludeValues，保留原值不覆寫。";
                return true;
            }

            return false;
        }

        /// <summary>
        /// 讀字串陣列參數。key 不存在或空陣列 → fallback；**存在但不是陣列 → 丟例外**。
        /// 靜默退回 fallback 曾是 2026-09-17 審查的 Critical：excludeValues 是防覆寫既有編號的
        /// 唯一防線，client 把它送成字串（或 Node server 跑的是沒有這個欄位的舊 schema）時，
        /// 退回空清單等於把防線拆掉，dry-run 看起來還是綠的。
        /// </summary>
        internal static List<string> ReadStringArray(JObject parameters, string key, string[] fallback)
        {
            JToken raw = parameters[key];
            if (raw == null || raw.Type == JTokenType.Null)
                return fallback.ToList();
            var token = raw as JArray;
            if (token == null)
                throw new Exception($"參數 {key} 必須是陣列，收到 {raw.Type}。若 MCP server 的 schema 沒有這個欄位，請重啟 session 讓新 schema 生效。");
            if (token.Count == 0)
                return fallback.ToList();
            return token.Select(t => t.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        /// <summary>
        /// 讀 ElementId 陣列參數。key 不存在或空陣列 → null（呼叫端走「自動收集」路徑）；
        /// 存在但不是陣列、或元素不是整數 → 丟例外，不得靜默退回自動收集。
        /// </summary>
        internal static List<IdType> ReadIdArray(JObject parameters, string key)
        {
            JToken raw = parameters[key];
            if (raw == null || raw.Type == JTokenType.Null)
                return null;
            var token = raw as JArray;
            if (token == null)
                throw new Exception($"參數 {key} 必須是陣列，收到 {raw.Type}。若 MCP server 的 schema 沒有這個欄位，請重啟 session 讓新 schema 生效。");
            if (token.Count == 0)
                return null;
            var ids = new List<IdType>(token.Count);
            foreach (var t in token)
            {
                if (t.Type != JTokenType.Integer)
                    throw new Exception($"參數 {key} 的元素必須是整數 ElementId，收到 {t.Type}：{t}");
                ids.Add(t.Value<IdType>());
            }
            return ids;
        }

        #endregion

        #region 停車格重新編號

        /// <summary>依族群／類型名稱關鍵字判定車位類別。比對不分大小寫。</summary>
        internal static string ClassifyParkingCategory(string familyAndTypeName, List<string> motorcycleKeywords, List<string> busKeywords)
        {
            string haystack = (familyAndTypeName ?? "").ToLowerInvariant();

            if (motorcycleKeywords != null && motorcycleKeywords.Any(k => haystack.Contains(k.ToLowerInvariant())))
                return "motorcycle";
            if (busKeywords != null && busKeywords.Any(k => haystack.Contains(k.ToLowerInvariant())))
                return "bus";
            return "car";
        }

        /// <summary>
        /// 批次重編停車格編號並寫入參數。單一 Transaction，任一格寫入失敗整批回滾。
        /// </summary>
        private object RenumberParkingSpaces(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string levelName = parameters["levelName"]?.Value<string>();
            string only = (parameters["only"]?.Value<string>() ?? "all").Trim().ToLowerInvariant();
            // 未指定參數名時走 BuiltInParameter，不用 LookupParameter("備註")：那是 zh-TW 的顯示名，
            // 非中文 Revit 回 null，且同名共用參數會被 LookupParameter 先撿到。
            string parameterName = parameters["parameterName"]?.Value<string>();
            // schema 預設值 "備註" / "Comments" 視同未指定，仍走 BuiltInParameter（不受 Revit 語系影響）。
            bool useBuiltInComments = string.IsNullOrWhiteSpace(parameterName)
                || string.Equals(parameterName.Trim(), "備註", StringComparison.Ordinal)
                || string.Equals(parameterName.Trim(), "Comments", StringComparison.OrdinalIgnoreCase);
            if (useBuiltInComments)
                parameterName = "備註";
            Func<Element, Parameter> resolveParam = e => useBuiltInComments
                ? e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                : e.LookupParameter(parameterName);
            long carStart = parameters["carStart"]?.Value<long?>() ?? 1;
            long motorcycleStart = parameters["motorcycleStart"]?.Value<long?>() ?? 1;
            long busStart = parameters["busStart"]?.Value<long?>() ?? 1;
            string prefix = parameters["prefix"]?.Value<string>() ?? "";
            IdType? startElementId = parameters["startElementId"]?.Value<IdType?>();
            double yToleranceMm = parameters["yToleranceMm"]?.Value<double?>() ?? 1500;
            bool skipFour = parameters["skipFour"]?.Value<bool?>() ?? false;
            bool dryRun = parameters["dryRun"]?.Value<bool?>() ?? true;
            string excludeMode = (parameters["excludeMode"]?.Value<string>() ?? "skip").Trim().ToLowerInvariant();
            var excludeIds = new HashSet<string>(ReadStringArray(parameters, "excludeElementIds", new string[0]), StringComparer.Ordinal);
            var excludeValues = new HashSet<string>(ReadStringArray(parameters, "excludeValues", new string[0]), StringComparer.Ordinal);

            var motorcycleKeywords = ReadStringArray(parameters, "motorcycleKeywords", new[] { "機車", "motor", "scooter" });
            var busKeywords = ReadStringArray(parameters, "busKeywords", new[] { "大客車", "巴士", "bus", "coach" });

            var validCategories = new[] { "all", "car", "motorcycle", "bus" };
            if (!validCategories.Contains(only))
                throw new Exception($"only 只接受 all / car / motorcycle / bus，收到 '{only}'。");

            if (excludeMode != "skip" && excludeMode != "reserve")
                throw new Exception($"excludeMode 只接受 skip / reserve，收到 '{excludeMode}'。");

            // 1. 解析目標樓層
            Level targetLevel;
            if (!string.IsNullOrWhiteSpace(levelName))
            {
                targetLevel = ResolveUnambiguousLevel(doc, levelName);
            }
            else
            {
                targetLevel = doc.ActiveView?.GenLevel;
                if (targetLevel == null)
                    throw new Exception("未指定 levelName，且作用中的視圖沒有關聯樓層。請明確指定 levelName。");
            }

            // 2. 收集該樓層停車格
            var parkingElements = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Parking)
                .WhereElementIsNotElementType()
                .Where(e => e.LevelId == targetLevel.Id)
                .ToList();

            var skipped = new List<object>();
            var excluded = new List<object>();
            var reservedIds = new HashSet<string>(StringComparer.Ordinal);
            var byCategory = new Dictionary<string, List<RoomOrderPoint>>
            {
                { "car", new List<RoomOrderPoint>() },
                { "motorcycle", new List<RoomOrderPoint>() },
                { "bus", new List<RoomOrderPoint>() }
            };
            var elementById = new Dictionary<string, Element>();
            var categoryById = new Dictionary<string, string>();

            foreach (Element element in parkingElements)
            {
                string idString = element.Id.GetIdValue().ToString(CultureInfo.InvariantCulture);
                FamilyInstance instance = element as FamilyInstance;
                string familyName = instance?.Symbol?.FamilyName ?? "";
                string typeName = instance?.Symbol?.Name ?? element.Name ?? "";
                string category = ClassifyParkingCategory(familyName + " " + typeName, motorcycleKeywords, busKeywords);

                if (only != "all" && category != only)
                    continue;

                double xMm, yMm;
                if (!TryGetElementCenterMm(element, out xMm, out yMm))
                {
                    skipped.Add(new
                    {
                        ElementId = element.Id.GetIdValue(),
                        FamilyName = familyName,
                        TypeName = typeName,
                        Category = category,
                        Reason = "取不到中心點（無 LocationPoint 亦無 BoundingBox）。"
                    });
                    continue;
                }

                string currentValue = resolveParam(element)?.AsString();
                string exclusionReason;
                if (IsExcluded(idString, currentValue, excludeIds, excludeValues, out exclusionReason))
                {
                    excluded.Add(new
                    {
                        ElementId = element.Id.GetIdValue(),
                        FamilyName = familyName,
                        TypeName = typeName,
                        Category = category,
                        CurrentValue = currentValue,
                        Mode = excludeMode,
                        Reason = exclusionReason
                    });

                    // skip：完全不進排序，後面的號碼補上來（總數變少）。
                    // reserve：仍進排序、仍佔一個號碼，只是不寫入 —— 圖面上那個位置留空號。
                    if (excludeMode == "skip")
                        continue;
                    reservedIds.Add(idString);
                }

                elementById[idString] = element;
                categoryById[idString] = category;
                byCategory[category].Add(new RoomOrderPoint { Id = idString, X = xMm, Y = yMm });
            }

            // 3. 各類別獨立排序、旋轉起點、產生編號
            var starts = new Dictionary<string, long>
            {
                { "car", carStart },
                { "motorcycle", motorcycleStart },
                { "bus", busStart }
            };

            // ParamIssue 在 dry-run 階段就算出來：參數不存在／唯讀／非文字型別。以前只在真寫入時檢查，
            // dry-run 讀到 null 參數會顯示 OldValue=null 看起來全綠，失敗要到 dryRun=false 那次才爆。
            var proposals = new List<(Element Element, string Category, string OldValue, string NewValue, double X, double Y, int RowIndex, bool Reserved, string ParamIssue)>();
            var startPoints = new List<object>();
            string startIdString = startElementId.HasValue
                ? startElementId.Value.ToString(CultureInfo.InvariantCulture)
                : null;
            bool startElementUsed = false;

            foreach (string category in new[] { "car", "motorcycle", "bus" })
            {
                var input = byCategory[category];
                if (input.Count == 0)
                    continue;

                var ordered = OrderRoomsTopDownLeftRight(input, yToleranceMm);

                string rotateId = null;
                if (startIdString != null && categoryById.TryGetValue(startIdString, out string startCategory) && startCategory == category)
                {
                    rotateId = startIdString;
                }

                List<RoomOrderPoint> finalOrder;
                if (!TryRotateToStart(ordered, rotateId, out finalOrder))
                {
                    throw new Exception($"startElementId {startElementId} 不在 {category} 類別的排序結果中，請確認該元件是停車格、在目標樓層、且未被 only 過濾掉。");
                }
                if (rotateId != null)
                    startElementUsed = true;

                var numbers = BuildNumberSequence(starts[category], finalOrder.Count, skipFour);

                for (int i = 0; i < finalOrder.Count; i++)
                {
                    RoomOrderPoint point = finalOrder[i];
                    Element element = elementById[point.Id];
                    Parameter param = resolveParam(element);
                    string oldValue = param?.AsString();
                    string newValue = prefix + numbers[i].ToString(CultureInfo.InvariantCulture);
                    string paramIssue = null;
                    if (param == null)
                        paramIssue = $"沒有名為 '{parameterName}' 的參數。";
                    else if (param.IsReadOnly)
                        paramIssue = $"參數 '{parameterName}' 唯讀，無法寫入。";
                    else if (param.StorageType != StorageType.String)
                        paramIssue = $"參數 '{parameterName}' 不是文字型別（實際：{param.StorageType}）。";
                    proposals.Add((element, category, oldValue, newValue, point.X, point.Y, point.RowIndex, reservedIds.Contains(point.Id), paramIssue));
                }

                startPoints.Add(new
                {
                    Category = category,
                    Count = finalOrder.Count,
                    StartElementId = elementById[finalOrder[0].Id].Id.GetIdValue(),
                    StartSource = rotateId != null ? "使用者指定" : "自動判定（排序後第一個）",
                    FirstNumber = prefix + numbers[0].ToString(CultureInfo.InvariantCulture),
                    LastNumber = prefix + numbers[numbers.Count - 1].ToString(CultureInfo.InvariantCulture)
                });
            }

            if (startIdString != null && !startElementUsed && proposals.Count > 0)
                throw new Exception($"startElementId {startElementId} 沒有被套用到任何類別，請確認該元件是本次處理範圍內的停車格。");

            int writableCount = proposals.Count(p => !p.Reserved);
            bool willWrite = !dryRun && writableCount > 0;
            var writeFailures = new List<object>();

            if (willWrite)
            {
                using (Transaction trans = TransactionHelper.Begin(doc, "重新編號停車格"))
                {
                    trans.Start();
                    try
                    {
                        foreach (var proposal in proposals)
                        {
                            // reserve 模式：這格佔了號碼但不寫入，原值原封不動。
                            if (proposal.Reserved)
                                continue;

                            if (proposal.ParamIssue != null)
                                throw new Exception($"停車格 {proposal.Element.Id.GetIdValue()}：{proposal.ParamIssue}");
                            Parameter param = resolveParam(proposal.Element);

                            // Parameter.Set 被拒絕時回傳 false 而不丟例外；不檢查就會 commit 出一批
                            // 靜默沒改到的車位，看起來全綠但實際比對了 0 個項目。
                            bool applied = param.Set(proposal.NewValue);
                            if (!applied)
                                throw new Exception($"停車格 {proposal.Element.Id.GetIdValue()} 寫入 '{proposal.NewValue}' 被 Revit 拒絕（可能受群組或公式約束）。");
                        }
                        // SilentFailuresPreprocessor 對 Error 級失敗回 Continue，Commit 會回 RolledBack 而不丟例外；
                        // 不檢查就會回報「已寫入 N 個」但模型裡一個都沒改。
                        if (trans.Commit() != TransactionStatus.Committed)
                            throw new Exception("交易未提交（Revit 回報錯誤，見 RevitMCP log）。");
                    }
                    catch (Exception ex)
                    {
                        if (trans.GetStatus() == TransactionStatus.Started)
                            trans.RollBack();
                        throw new Exception($"寫入停車格編號失敗，已整批回滾: {ex.Message}", ex);
                    }
                }
            }

            var spacesOutput = proposals.Select(p => new
            {
                ElementId = p.Element.Id.GetIdValue(),
                Category = p.Category,
                OldValue = p.OldValue,
                NewValue = p.NewValue,
                CenterXMm = Math.Round(p.X, 2),
                CenterYMm = Math.Round(p.Y, 2),
                RowIndex = p.RowIndex,
                Reserved = p.Reserved,
                ParamIssue = p.ParamIssue
            }).ToList();

            var categoryCounts = proposals
                .GroupBy(p => p.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            string excludeSuffix = excludeMode == "reserve" ? "，仍佔號但不寫入" : "，號碼已補上";
            string excludeNote = excluded.Count == 0
                ? ""
                : $"，{excluded.Count} 個被排除（excludeMode={excludeMode}{excludeSuffix}）";

            int paramIssueCount = proposals.Count(p => !p.Reserved && p.ParamIssue != null);
            string paramIssueNote = paramIssueCount == 0 ? "" : $"；**{paramIssueCount} 個停車格的參數不可寫（見 Spaces[].ParamIssue），dryRun=false 會整批回滾**";

            string message = dryRun
                ? $"Dry-run：{writableCount} 個停車格預計寫入（{string.Join("、", categoryCounts.Select(kv => kv.Key + " " + kv.Value))}），{skipped.Count} 個略過{excludeNote}{paramIssueNote}。尚未寫入，確認排序與起點後再以 dryRun=false 執行。"
                : $"已寫入 {writableCount} 個停車格的 '{parameterName}'（{string.Join("、", categoryCounts.Select(kv => kv.Key + " " + kv.Value))}），{skipped.Count} 個略過{excludeNote}。";

            return new
            {
                Level = targetLevel.Name,
                LevelId = targetLevel.Id.GetIdValue(),
                ParameterName = parameterName,
                Count = proposals.Count,
                WritableCount = writableCount,
                ParamIssueCount = paramIssueCount,
                ExcludeMode = excludeMode,
                CategoryCounts = categoryCounts,
                StartPoints = startPoints,
                SkipFour = skipFour,
                DryRun = dryRun,
                Written = willWrite,
                Spaces = spacesOutput,
                Skipped = skipped,
                Excluded = excluded,
                WriteFailures = writeFailures,
                Message = message
            };
        }

        #endregion

        #region 圖面順序編號標註

        /// <summary>
        /// 在視圖上依順序放置編號 TextNote。
        /// </summary>
        private object CreateSequenceNumbers(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            const double feetToMm = 304.8;

            IdType? viewIdParam = parameters["viewId"]?.Value<IdType?>();
            View view = viewIdParam.HasValue
                ? doc.GetElement(viewIdParam.Value.ToElementId()) as View
                : doc.ActiveView;
            if (view == null)
                throw new Exception(viewIdParam.HasValue ? $"找不到視圖 ID: {viewIdParam.Value}" : "取不到作用中的視圖。");

            List<IdType> explicitIds = ReadIdArray(parameters, "elementIds");
            string categoryName = parameters["category"]?.Value<string>();
            string order = parameters["order"]?.Value<string>();
            string format = (parameters["format"]?.Value<string>() ?? "number").Trim().ToLowerInvariant();
            long start = parameters["start"]?.Value<long?>() ?? 1;
            string prefix = parameters["prefix"]?.Value<string>() ?? "";
            bool upperCase = parameters["upperCase"]?.Value<bool?>() ?? false;
            bool skipFour = parameters["skipFour"]?.Value<bool?>() ?? false;
            double offsetXMm = parameters["offsetXMm"]?.Value<double?>() ?? 0;
            double offsetYMm = parameters["offsetYMm"]?.Value<double?>() ?? 0;
            double yToleranceMm = parameters["yToleranceMm"]?.Value<double?>() ?? 1500;
            string textTypeName = parameters["textTypeName"]?.Value<string>();
            // 預設 dry-run，與 renumber_parking_spaces 一致；domain/sequence-numbering.md 要求先看排序再放。
            bool dryRun = parameters["dryRun"]?.Value<bool?>() ?? true;

            if (format != "number" && format != "letter")
                throw new Exception($"format 只接受 number 或 letter，收到 '{format}'。");
            if (start < 1 && format == "letter")
                throw new Exception("format='letter' 時 start 必須 ≥ 1（1 代表 a）。");

            bool hasExplicitIds = explicitIds != null;
            if (string.IsNullOrWhiteSpace(order))
                order = hasExplicitIds ? "given" : "yx";
            if (order != "given" && order != "yx")
                throw new Exception($"order 只接受 given 或 yx，收到 '{order}'。");

            // 1. 收集目標元件
            var elements = new List<Element>();
            if (hasExplicitIds)
            {
                foreach (IdType rawId in explicitIds)
                {
                    Element element = doc.GetElement(rawId.ToElementId());
                    if (element == null)
                        throw new Exception($"找不到 ElementId {rawId}。");
                    elements.Add(element);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(categoryName))
                    throw new Exception("請提供 elementIds，或用 category 指定要收集的類別（例如 OST_Parking）。");

                BuiltInCategory builtInCategory;
                if (!Enum.TryParse(categoryName, true, out builtInCategory))
                    throw new Exception($"無法解析 BuiltInCategory '{categoryName}'，請使用 OST_ 開頭的名稱（例如 OST_Parking）。");

                elements = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(builtInCategory)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (elements.Count == 0)
                    throw new Exception($"視圖「{view.Name}」中找不到類別 {categoryName} 的元件。");
            }

            // 2. 取中心點，決定順序
            var skipped = new List<object>();
            var placeable = new List<(Element Element, double X, double Y)>();
            foreach (Element element in elements)
            {
                double xMm, yMm;
                if (!TryGetElementCenterMm(element, out xMm, out yMm))
                {
                    skipped.Add(new
                    {
                        ElementId = element.Id.GetIdValue(),
                        Name = element.Name,
                        Reason = "取不到中心點（無 LocationPoint 亦無 BoundingBox）。"
                    });
                    continue;
                }
                placeable.Add((element, xMm, yMm));
            }

            if (placeable.Count == 0)
                throw new Exception("沒有任何元件取得到中心點，無法放置編號。");

            List<(Element Element, double X, double Y, int RowIndex)> sequence;
            if (order == "yx")
            {
                var orderInput = placeable
                    .Select(p => new RoomOrderPoint
                    {
                        Id = p.Element.Id.GetIdValue().ToString(CultureInfo.InvariantCulture),
                        X = p.X,
                        Y = p.Y
                    })
                    .ToList();
                var byId = placeable.ToDictionary(
                    p => p.Element.Id.GetIdValue().ToString(CultureInfo.InvariantCulture),
                    p => p);
                sequence = OrderRoomsTopDownLeftRight(orderInput, yToleranceMm)
                    .Select(p =>
                    {
                        var item = byId[p.Id];
                        return (item.Element, item.X, item.Y, p.RowIndex);
                    })
                    .ToList();
            }
            else
            {
                sequence = placeable.Select(p => (p.Element, p.X, p.Y, 0)).ToList();
            }

            // 3. 產生標籤
            var labels = new List<string>();
            if (format == "letter")
            {
                for (int i = 0; i < sequence.Count; i++)
                    labels.Add(prefix + ToLetterLabel(start + i, upperCase));
            }
            else
            {
                var numbers = BuildNumberSequence(start, sequence.Count, skipFour);
                foreach (long n in numbers)
                    labels.Add(prefix + n.ToString(CultureInfo.InvariantCulture));
            }

            // 4. 放置
            var placements = new List<object>();
            var createdIds = new List<IdType>();
            bool written = false;
            string usedTextTypeName = null;

            if (!dryRun)
            {
                TextNoteType textType = ResolveTextNoteType(doc, textTypeName);
                usedTextTypeName = textType.Name;

                using (Transaction trans = TransactionHelper.Begin(doc, "放置順序編號"))
                {
                    trans.Start();
                    try
                    {
                        var options = new TextNoteOptions
                        {
                            TypeId = textType.Id,
                            HorizontalAlignment = HorizontalTextAlignment.Center
                        };

                        for (int i = 0; i < sequence.Count; i++)
                        {
                            double px = (sequence[i].X + offsetXMm) / feetToMm;
                            double py = (sequence[i].Y + offsetYMm) / feetToMm;
                            TextNote note = TextNote.Create(doc, view.Id, new XYZ(px, py, 0), labels[i], options);
                            createdIds.Add(note.Id.GetIdValue());
                        }
                        if (trans.Commit() != TransactionStatus.Committed)
                            throw new Exception("交易未提交（Revit 回報錯誤，見 RevitMCP log）。");
                        written = true;
                    }
                    catch (Exception ex)
                    {
                        if (trans.GetStatus() == TransactionStatus.Started)
                            trans.RollBack();
                        throw new Exception($"放置編號 TextNote 失敗，已整批回滾: {ex.Message}", ex);
                    }
                }
            }

            for (int i = 0; i < sequence.Count; i++)
            {
                placements.Add(new
                {
                    ElementId = sequence[i].Element.Id.GetIdValue(),
                    Label = labels[i],
                    TextNoteId = written && i < createdIds.Count ? (IdType?)createdIds[i] : null,
                    PlacementXMm = Math.Round(sequence[i].X + offsetXMm, 2),
                    PlacementYMm = Math.Round(sequence[i].Y + offsetYMm, 2),
                    RowIndex = sequence[i].RowIndex
                });
            }

            string message = dryRun
                ? $"Dry-run：{sequence.Count} 個元件預計取得編號 {labels[0]} 到 {labels[labels.Count - 1]}，{skipped.Count} 個略過。尚未建立 TextNote。"
                : $"已在視圖「{view.Name}」放置 {createdIds.Count} 個編號（{labels[0]} 到 {labels[labels.Count - 1]}），{skipped.Count} 個略過。";

            return new
            {
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                Order = order,
                Format = format,
                Count = sequence.Count,
                FirstLabel = labels[0],
                LastLabel = labels[labels.Count - 1],
                SkipFour = skipFour,
                DryRun = dryRun,
                Written = written,
                TextNoteTypeName = usedTextTypeName,
                Placements = placements,
                Skipped = skipped,
                Message = message
            };
        }

        #endregion
    }
}
