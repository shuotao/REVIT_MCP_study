using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Newtonsoft.Json.Linq;

#nullable disable

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    // =====================================================================================
    // 泛用風管系統建立 —— Stage 1: create_duct_system
    // -------------------------------------------------------------------------------------
    // repo 內在本檔新增前完全沒有任何程式碼碰過 MEP 系統（MechanicalSystem / DuctSystemType /
    // NewMechanicalSystem / MEPSystem 皆 0 命中）。本檔把一組風口（Air Terminal）與一台設備
    // （Mechanical Equipment）組成一個 Revit 風管系統。
    //
    // 2026-08-15：Opus 稽核（以 Revit 2026 官方 API XML 為證據來源）開出 16 項 findings，
    // 使用者裁決全部修復，本檔為修復後版本。以下設計要點已反映修復內容。
    //
    // 設計要點：
    //   - equipmentId 可省略是刻意設計，不是偷懶：現場常見「已決定要獨立成一個系統，但設備還沒
    //     選定」的中間狀態（需求量太小、通用族群沒有對應機型、正在等廠商資料），這種狀態必須能
    //     被記錄，且要與「還沒決定歸屬」區分開——建了系統但設備欄空白，代表已決定獨立、設備待補。
    //     Revit API 的 NewMechanicalSystem(Connector baseConnector, ConnectorSet connectors,
    //     DuctSystemType systemType) 的 baseConnector 參數，依 API 文件明載「The base equipment
    //     is optional for the system, so this argument may be null.」，本檔依此契約直接支援這條
    //     路徑；執行期尚未實測，若實測發現與文件契約不符，例外訊息會原樣往上拋，由呼叫端／後續
    //     驗收據實回報，本檔不吞掉也不假造成功。
    //   - equipmentId / equipmentConnectorIndex / equipmentConnectorId 顯式傳 JSON null 時，
    //     `JToken?.Value<T>()` 對值型別 T 會拋 InvalidCastException（`?.` 只擋 C# null、擋不住
    //     JTokenType.Null），一律經 ReadOptional<T> 讀取：null／省略皆視為「不指定」，型別不符
    //     時拋出附參數名與收到型別的可理解例外。
    //   - 設備 connector 的選擇必須明確且可稽核：一台設備常有多個接頭（送風、回風、排風、水路、
    //     電源），選錯不會報錯、只會接成錯的系統類型。因此依 systemType 篩選 Domain.DomainHvac
    //     且 DuctSystemType 相符（或 Direction 相符）的 connector；找到 0 個或多於 1 個一律
    //     拋例外並在訊息中列出全部候選（index/ConnectorId/DuctSystemType/Direction/Shape/Origin，
    //     各欄位個別 try/catch 包裝——Direction 依 API 文件對不支援流量計算的 connector 會拋
    //     InvalidOperationException，Origin 依 API 文件在 NonEndConn 型別上會拋例外，兩者皆有
    //     文件依據；Shape 文件未載明任何例外，一併包裝純為防禦、不宣稱有文件依據。讀不到一律回
    //     "N/A" 而不是讓診斷訊息自己拋例外），多於 1 個時要求呼叫端改用 equipmentConnectorIndex
    //     （清單位置索引，本工具已先依 Connector.Id 排序才建立索引，故同一族群在接頭數不變時
    //     穩定；但族群重載後若接頭增減，索引會位移且不會報錯，需跨次呼叫穩定指定同一接頭時請用
    //     equipmentConnectorId）或 equipmentConnectorId（Connector.Id，API 文件載明自 2016 起
    //     提供的穩定識別）明確指定，不靜默取第一個。equipmentConnectorIndex／
    //     equipmentConnectorId 指定時仍驗證 DuctSystemType/Direction 是否與 systemType 相符
    //     （不只驗 Domain），避免關掉本工具原本要防的「選錯不報錯」保護。成功採用的 connector
    //     完整資訊回傳在 EquipmentConnector。
    //   - 風口的風管接頭同樣要篩選：驗證 Domain.DomainHvac，並依 systemType 檢查方向是否合理
    //     （送風系統的風口理論上是 In——空氣從風管進風口；回/排風系統的風口理論上是 Out——
    //     空氣從風口出、進風管；Bidirectional 一律放行不擋）。
    //   - 設備／風口的 Connector 若已隸屬其他系統（Connector.MEPSystem 非 null），Revit 的
    //     NewMechanicalSystem 會對整批連接拋 ArgumentException 且不指名是哪一顆——本檔在
    //     ResolveEquipmentConnector／ResolveTerminalConnector 內主動用 Connector.MEPSystem
    //     （依 API 文件本身可能拋 InvalidOperationException，已 try/catch）預先擋下並指名該
    //     connector 已屬於哪個系統，設備端為硬性中止、風口端記為該筆的 Error（不中止其餘風口）。
    //     同時檢查 terminalIds 是否與 equipmentId 自我參照（設備不可同時是 baseConnector 與
    //     系統成員），以及 terminalIds 內部是否重複（Revit 對 ConnectorSet 插入重複 connector
    //     的行為未定義，故在解析階段就擋下，不放行到 ConnectorSet.Insert）。
    //   - 單一 Transaction（TransactionHelper.Begin，非裸 new Transaction）。逐一風口 try/catch，
    //     單一風口的 connector 解析失敗記進該筆 TerminalResults 的 Error，不中止其餘風口；
    //     但整批風口全部解析失敗（ConnectorSet 空）時，沒有任何成員可組系統，直接拋例外中止整個
    //     建立動作（不留下零成員的系統垃圾在模型裡）。
    //   - trans.Commit() 的回傳值（TransactionStatus）不再被丟棄：Success 同時要求
    //     CommitStatus == Committed 且所有風口都成功；非 Committed 時額外回 CommitError，且
    //     完全略過流量／系統成員讀回（避免對已回滾的元素讀值）。systemName 撞名（Revit 要求
    //     系統名稱在專案內唯一）不再讓整個系統連同已建立的成員一起 rollback——改為 try/catch，
    //     失敗放進 SystemNameError，系統保留 Revit 自動名稱繼續存在。
    //   - SystemFlowReadBack 是本 Stage 最重要的欄位之一：建立系統後（trans.Commit() 之後）
    //     從系統元素本身讀回流量，不是呼叫端寫進去的，是 Revit 依系統實際成員算出來的——但依
    //     API 文件「若系統未良好連接（IsWellConnected=false，例如風口已放置但風管尚未繪製，
    //     正是本專案現況），需要被計算的參數就是無效的」，因此一併回報 IsWellConnected /
    //     SystemIsEmpty / ActualMemberCount（與 ExpectedMemberCount=SuccessCount 交叉比對，
    //     不一致時回 MemberCountMismatch——API 文件對 MEPSystem.Elements 明載「不含 base
    //     equipment/panel」，故預期成員數只算風口成功筆數，不把設備算進去，否則帶 equipmentId
    //     的正常成功路徑會恆為 MemberCountMismatch=true；設備是否已掛上系統改用獨立的
    //     BaseEquipmentAttached 欄位回報，不混進成員數比對）與 CalculationValid，並在
    //     IsWellConnected=false 時於 Note
    //     明確標註「本數值不可作為驗算依據」，不讓呼叫端誤把無效值當成有效檢核點。顯示值
    //     （AsValueString，依專案顯示單位格式化）與內部值（依 StorageType 取的原始值）分兩個
    //     獨立欄位回報，互不補值，各自可能為 null（domain/tool-capability-boundary.md 的
    //     lesson L16 記錄過帶單位參數寫入/讀回不對稱的落差，這裡雖然是純讀回、非呼叫端寫入，
    //     仍然沿用「兩欄互不補值」的誠實回報慣例；L17 記錄的是另一件事——作用視圖為含計算欄位
    //     的明細表時批次寫入會顯著提高逾時機率，本檔對應的因應是回傳 PerformanceWarning，見下）。
    //     若設備存在，一併讀回其 Air Flow 參數放進 EquipmentAirFlowReadBack——此參數是否唯讀、
    //     是否由系統驅動，Revit 文件未明載，執行期亦尚未實測（部署後驗證為 open item，本輪
    //     稽核僅到 build 與靜態檢查）；本檔不預設，只回傳 param.IsReadOnly 供呼叫端自行核對，
    //     改用 Element.GetParameters
    //     (string) 偵測同名參數歧義（API 文件警告 LookupParameter 對多個同名參數的比對結果為
    //     「隨機決定且未來可能改變」），命中多筆時仍讀第一筆但回 Ambiguous=true 誠實揭露。
    //   - 效能：本檔只做一次 doc.Regenerate()，但 NewMechanicalSystem 自身依 API 文件本就會
    //     regenerate。依 lesson L17 實測，作用視圖為含計算欄位的明細表時單次模型變更成本會大幅
    //     上升，故取得作用視圖後（不anchor 到之前的呼叫，本次呼叫內重新讀取），若為 ViewSchedule
    //     則回傳 PerformanceWarning 提醒切換作用視圖（純警示、不擋執行）；另回傳 ElapsedMs
    //     （Stopwatch 全程量測）供呼叫端核對耗時，並在 TS 描述明載本工具的 MCP timeout 為 30 秒
    //     且逾時不會回滾。
    // =====================================================================================

    public partial class CommandExecutor
    {
        /// <summary>
        /// 把一組風口與一台設備（可省略）組成一個 Revit 風管系統。單一 Transaction。
        /// </summary>
        private object CreateDuctSystem(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string systemTypeToken = parameters["systemType"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(systemTypeToken))
            {
                throw new Exception("systemType 為必填（SupplyAir / ReturnAir / ExhaustAir，大小寫不敏感）。");
            }

            DuctSystemType systemType = ParseDuctSystemType(systemTypeToken);

            JArray terminalIdsArray = parameters["terminalIds"] as JArray;
            if (terminalIdsArray == null || terminalIdsArray.Count == 0)
            {
                throw new Exception("terminalIds 為必填，且至少需要一筆風口 ElementId。");
            }

            IdType? equipmentId = ReadOptional<IdType>(parameters, "equipmentId");
            int? equipmentConnectorIndex = ReadOptional<int>(parameters, "equipmentConnectorIndex");
            int? equipmentConnectorId = ReadOptional<int>(parameters, "equipmentConnectorId");
            string systemName = parameters["systemName"]?.Value<string>();

            if (!equipmentId.HasValue && (equipmentConnectorIndex.HasValue || equipmentConnectorId.HasValue))
            {
                throw new Exception("給了 equipmentConnectorIndex 或 equipmentConnectorId 但沒有 equipmentId——沒有設備就沒有設備接頭可指定。請一併給 equipmentId，或移除這兩個參數。");
            }

            // 作用視圖效能提醒（domain/tool-capability-boundary.md L17）：純警示，不擋執行。
            View activeView = doc.ActiveView;
            string performanceWarning = activeView is ViewSchedule
                ? "作用視圖為明細表，Revit 會在每次模型變更後重算整張表，依 domain/tool-capability-boundary.md L17 實測這會顯著提高逾時機率。建議先用 set_active_view 切到一般平面圖再執行。"
                : null;

            var stopwatch = Stopwatch.StartNew();

            using (Transaction trans = TransactionHelper.Begin(doc, "建立風管系統"))
            {
                trans.Start();

                // ---- 1. 設備 connector（可省略：equipmentId 省略時 baseConnector 保持 null）----
                FamilyInstance equipmentInstance = null;
                Connector baseConnector = null;
                object equipmentConnectorInfo = null;

                if (equipmentId.HasValue)
                {
                    Element equipmentElement = doc.GetElement(equipmentId.Value.ToElementId());
                    equipmentInstance = equipmentElement as FamilyInstance;
                    if (equipmentInstance == null)
                    {
                        throw new Exception($"找不到設備 ElementId: {equipmentId.Value}，或該元素不是 FamilyInstance。");
                    }

                    int chosenIndex;
                    baseConnector = ResolveEquipmentConnector(equipmentInstance, systemType, equipmentConnectorIndex, equipmentConnectorId, out chosenIndex);
                    equipmentConnectorInfo = BuildConnectorInfo(baseConnector, chosenIndex);
                }

                // ---- 2. 逐一風口：解析各自的 HVAC connector，部分失敗不中止整批 ----
                var terminalRows = new List<TerminalResultRow>();
                var terminalConnectorSet = new ConnectorSet();
                var seenTerminalIds = new HashSet<IdType>();
                int successCount = 0;
                int failureCount = 0;

                for (int i = 0; i < terminalIdsArray.Count; i++)
                {
                    JToken idToken = terminalIdsArray[i];
                    IdType? terminalId = null;

                    try
                    {
                        if (idToken.Type != JTokenType.Integer)
                        {
                            throw new Exception($"terminalIds[{i}] 必須是整數 ElementId，收到 {idToken.Type}: {idToken}");
                        }

                        IdType parsedId = idToken.Value<IdType>();
                        terminalId = parsedId;

                        if (equipmentId.HasValue && parsedId == equipmentId.Value)
                        {
                            throw new Exception("設備不可同時作為 baseConnector 與系統成員（Revit API 明文限制：baseEquipmentConnector 不得包含在 connectors 中）。");
                        }

                        if (!seenTerminalIds.Add(parsedId))
                        {
                            throw new Exception($"此 ElementId 在 terminalIds 中重複出現，已跳過重複項（Revit 對 ConnectorSet 插入重複 connector 的行為未定義）。");
                        }

                        Element terminalElement = doc.GetElement(parsedId.ToElementId());
                        if (terminalElement == null)
                        {
                            throw new Exception($"找不到風口 ElementId: {parsedId}。");
                        }

                        FamilyInstance terminalInstance = terminalElement as FamilyInstance;
                        if (terminalInstance == null)
                        {
                            throw new Exception($"ElementId {parsedId} 不是 FamilyInstance，無法取得 Connector。");
                        }

                        int connectorIndex;
                        Connector terminalConnector = ResolveTerminalConnector(terminalInstance, systemType, out connectorIndex);

                        terminalConnectorSet.Insert(terminalConnector);

                        terminalRows.Add(new TerminalResultRow
                        {
                            Index = i,
                            ElementId = parsedId,
                            Success = true,
                            Error = null,
                            ConnectorIndex = connectorIndex
                        });
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        terminalRows.Add(new TerminalResultRow
                        {
                            Index = i,
                            ElementId = terminalId,
                            Success = false,
                            Error = ex.Message,
                            ConnectorIndex = null
                        });
                        failureCount++;
                    }
                }

                if (terminalConnectorSet.IsEmpty)
                {
                    throw new Exception("所有風口的 Connector 都解析失敗，沒有任何成員可組成系統，未建立風管系統。詳見各筆 Error。");
                }

                // ---- 3. 建立系統。baseConnector 可能為 null（equipmentId 省略時），
                //         依 API 文件此為合法用法；若實測發現與文件不符，例外會直接往上拋、
                //         原樣回報，不做替代方案 ----
                MechanicalSystem system = doc.Create.NewMechanicalSystem(baseConnector, terminalConnectorSet, systemType);

                string systemNameError = null;
                if (!string.IsNullOrWhiteSpace(systemName))
                {
                    try
                    {
                        system.Name = systemName;
                    }
                    catch (Exception ex)
                    {
                        systemNameError = $"系統已建立，但命名為「{systemName}」失敗（Revit 要求系統名稱唯一，此名稱可能已被使用）：{ex.Message}。系統保留 Revit 自動名稱，請改用其他名稱後以 modify_element_parameter 重新命名。";
                    }
                }

                doc.Regenerate();

                TransactionStatus commitStatus = trans.Commit();

                // ---- 4. 讀回段落：只在交易確實 Committed 時才讀，避免對已回滾的元素讀值。
                //         SystemId 也移進 guard 內讀——若無條件讀取，交易回滾時仍可能回出非 null
                //         的 SystemId，與 Success=false／CommitError 並列會誤導呼叫端以為模型裡
                //         有那個系統，與 SystemName／流量欄位「非 Committed 一律 null」的行為不一致。----
                IdType? systemIdSafe = null;
                bool? isWellConnected = null;
                bool? systemIsEmpty = null;
                int? actualMemberCount = null;
                // MEPSystem.Elements 依 API 文件明載「不含 base equipment/panel」（The return value
                // is a read only collection and doesn't include the base equipment or panel.），
                // 故預期成員數只等於風口成功筆數，不把設備算進去——否則帶 equipmentId 的正常成功
                // 路徑會恆為 MemberCountMismatch=true（一個永遠在響的警報比沒有這個欄位更糟）。
                // 設備是否已掛上系統改用獨立的 BaseEquipmentAttached 欄位回報，不混進成員數比對。
                int expectedMemberCount = successCount;
                bool? memberCountMismatch = null;
                bool? baseEquipmentAttached = null;
                string systemNameAfterCommit = null;
                object systemFlowReadBack = null;
                object equipmentAirFlowReadBack = null;

                if (commitStatus == TransactionStatus.Committed)
                {
                    systemIdSafe = TryGetSystemId(system);
                    isWellConnected = TryGetIsWellConnected(system);
                    systemIsEmpty = TryGetIsEmpty(system);
                    actualMemberCount = TryGetMemberCount(system);
                    baseEquipmentAttached = TryGetBaseEquipmentAttached(system);
                    memberCountMismatch = actualMemberCount.HasValue
                        ? (bool?)(actualMemberCount.Value != expectedMemberCount)
                        : null;

                    try { systemNameAfterCommit = system.Name; } catch { systemNameAfterCommit = null; }
                    foreach (TerminalResultRow row in terminalRows)
                    {
                        if (row.Success)
                        {
                            row.SystemNameAfter = systemNameAfterCommit;
                        }
                    }

                    Parameter systemFlowParam;
                    try
                    {
                        systemFlowParam = system.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM)
                            ?? system.LookupParameter("Flow");
                    }
                    catch
                    {
                        systemFlowParam = null;
                    }
                    systemFlowReadBack = BuildFlowReadback(systemFlowParam, isWellConnected);

                    if (equipmentInstance != null)
                    {
                        IList<Parameter> airFlowCandidates;
                        try { airFlowCandidates = equipmentInstance.GetParameters("Air Flow"); }
                        catch { airFlowCandidates = null; }

                        if (airFlowCandidates == null || airFlowCandidates.Count == 0)
                        {
                            equipmentAirFlowReadBack = new
                            {
                                Found = false,
                                DisplayValue = (object)null,
                                RawValue = (object)null,
                                CalculationValid = isWellConnected.HasValue ? (object)isWellConnected.Value : null,
                                IsReadOnly = (object)null,
                                Ambiguous = false,
                                Note = (object)"此設備族群沒有名為 Air Flow 的參數（可能是非英文 UI 或族群使用其他參數名）"
                            };
                        }
                        else
                        {
                            equipmentAirFlowReadBack = BuildFlowReadback(airFlowCandidates[0], isWellConnected, airFlowCandidates.Count);
                        }
                    }
                }

                stopwatch.Stop();

                string commitError = commitStatus != TransactionStatus.Committed
                    ? $"交易未成功提交（狀態 {commitStatus}），模型中可能沒有任何系統被建立，詳見 RevitMCP 記錄檔"
                    : null;

                return new
                {
                    Success = failureCount == 0 && commitStatus == TransactionStatus.Committed,
                    CommitStatus = commitStatus.ToString(),
                    CommitError = (object)commitError,
                    PerformanceWarning = (object)performanceWarning,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    SystemId = systemIdSafe.HasValue ? (object)systemIdSafe.Value : null,
                    SystemName = (object)systemNameAfterCommit,
                    SystemNameRequested = (object)systemName,
                    SystemNameError = (object)systemNameError,
                    SystemType = systemType.ToString(),
                    EquipmentId = equipmentInstance != null ? (object)equipmentInstance.Id.GetIdValue() : null,
                    EquipmentConnector = equipmentConnectorInfo,
                    TerminalResults = terminalRows.Select(r => (object)new
                    {
                        Index = r.Index,
                        ElementId = r.ElementId.HasValue ? (object)r.ElementId.Value : null,
                        Success = r.Success,
                        Error = (object)r.Error,
                        ConnectorIndex = r.ConnectorIndex.HasValue ? (object)r.ConnectorIndex.Value : null,
                        SystemNameAfter = (object)r.SystemNameAfter
                    }).ToList(),
                    TerminalCount = terminalIdsArray.Count,
                    SuccessCount = successCount,
                    FailureCount = failureCount,
                    ExpectedMemberCount = expectedMemberCount,
                    ActualMemberCount = actualMemberCount.HasValue ? (object)actualMemberCount.Value : null,
                    MemberCountMismatch = memberCountMismatch.HasValue ? (object)memberCountMismatch.Value : null,
                    BaseEquipmentAttached = baseEquipmentAttached.HasValue ? (object)baseEquipmentAttached.Value : null,
                    IsWellConnected = isWellConnected.HasValue ? (object)isWellConnected.Value : null,
                    SystemIsEmpty = systemIsEmpty.HasValue ? (object)systemIsEmpty.Value : null,
                    SystemFlowReadBack = systemFlowReadBack,
                    EquipmentAirFlowReadBack = equipmentAirFlowReadBack
                };
            }
        }

        /// <summary>
        /// 逐筆風口/設備連線結果的可變暫存列——先在迴圈內填 Success/Error/ConnectorIndex，
        /// 系統建立成功後才回填 SystemNameAfter，所以不能用一次成形的匿名型別。
        /// ElementId 為 IdType?（解析失敗時維持 null，不用 0 這種看起來像真值的假造數字——
        /// Revit 的無效 ElementId 是 -1，0 是合法的 ElementId）。Index 對齊輸入的 terminalIds
        /// 陣列位置，供呼叫端在多筆失敗或含重複值時把結果對回輸入。
        /// </summary>
        private class TerminalResultRow
        {
            public int Index;
            public IdType? ElementId;
            public bool Success;
            public string Error;
            public int? ConnectorIndex;
            public string SystemNameAfter;
        }

        /// <summary>
        /// 從 JObject 讀取一個可省略的值型別參數。顯式 JSON null 與省略該 key 都視為「不指定」，
        /// 回傳 null——`JToken?.Value&lt;T&gt;()` 只擋得住 C# null，擋不住 JTokenType.Null，
        /// 對值型別 T 直接呼叫會拋 InvalidCastException 且訊息不含參數名，不可行動。
        /// 型別確實不符（例如給字串卻要數值）時包裝成含參數名與收到型別的可理解例外。
        /// </summary>
        private static T? ReadOptional<T>(JObject p, string name) where T : struct
        {
            JToken t = p[name];
            if (t == null || t.Type == JTokenType.Null) return null;
            try { return t.Value<T>(); }
            catch (Exception ex)
            {
                throw new Exception($"參數 {name} 的值無法解析為數值（收到 {t.Type}: {t}）：{ex.Message}。若要表示「不指定」，請省略此參數或傳 null。");
            }
        }

        /// <summary>
        /// 解析 systemType 字串為 DuctSystemType，限制在 SupplyAir / ReturnAir / ExhaustAir
        /// 三種本工具支援的範圍內。改用 switch 而非 Enum.TryParse——TryParse 會接受純數字字串
        /// （映射底層值）與逗號分隔名稱清單（即使非 [Flags] enum 也會 OR），對「systemType 只能是
        /// 這三個名稱之一」的白名單語意而言過度寬鬆，可能靜默接受呼叫端沒打算傳的值。
        /// </summary>
        private static DuctSystemType ParseDuctSystemType(string token)
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "supplyair":
                    return DuctSystemType.SupplyAir;
                case "returnair":
                    return DuctSystemType.ReturnAir;
                case "exhaustair":
                    return DuctSystemType.ExhaustAir;
                default:
                    throw new Exception($"systemType 只接受名稱字串 SupplyAir / ReturnAir / ExhaustAir（大小寫不敏感），不接受數字或逗號清單，收到: {token}");
            }
        }

        /// <summary>
        /// 確認 connector 尚未隸屬任何系統。Connector.MEPSystem 依 API 文件在「取得系統失敗」時
        /// 會拋 InvalidOperationException，故包 try/catch、失敗視為「沒有既有系統」而非中止。
        /// 已隸屬系統時拋例外並指名既有系統的 ElementId 與名稱，讓錯誤訊息可行動，而不是讓
        /// Revit 在 NewMechanicalSystem 內對整批 connectors 拋一句不指名的 ArgumentException。
        /// </summary>
        private static void EnsureConnectorNotInSystem(Connector connector, string subject, string suggestion)
        {
            MEPSystem existing = null;
            try { existing = connector.MEPSystem; } catch { }
            if (existing != null)
            {
                throw new Exception($"{subject}已隸屬系統 ElementId={existing.Id.GetIdValue()}（{existing.Name}），Revit 不允許同一個 connector 加入第二個系統。{suggestion}");
            }
        }

        /// <summary>
        /// 依 systemType 從設備上挑選要當 baseConnector 的 HVAC Connector。三種指定方式，優先序：
        ///   1) equipmentConnectorId（Connector.Id，穩定識別，自 Revit 2016 起提供，不受列舉順序影響）
        ///   2) equipmentConnectorIndex（清單位置索引，本工具已先依 Connector.Id 排序才建立索引，
        ///      故同一族群在接頭數不變時穩定；但族群重載後若接頭增減，索引會位移且不會報錯，
        ///      需跨次呼叫穩定指定同一接頭時請用 equipmentConnectorId——僅為相容既有呼叫端保留）
        ///   3) 自動篩選：Domain.DomainHvac 且（DuctSystemType 相符 或 Direction 相符 systemType
        ///      預期方向），0 個或多於 1 個都拋例外，不靜默取第一個。
        /// 三種方式最終選出的 connector 都會驗證 DuctSystemType/Direction 是否與 systemType 相符
        /// （不只驗 Domain），並確認尚未隸屬其他系統。
        /// </summary>
        private static Connector ResolveEquipmentConnector(
            FamilyInstance equipment,
            DuctSystemType systemType,
            int? equipmentConnectorIndex,
            int? equipmentConnectorId,
            out int chosenIndex)
        {
            List<Connector> allConnectors = CollectConnectors(equipment);
            if (allConnectors.Count == 0)
            {
                throw new Exception($"設備 ElementId: {equipment.Id.GetIdValue()} 沒有任何 Connector。");
            }

            if (equipmentConnectorId.HasValue)
            {
                var idMatches = new List<int>();
                for (int i = 0; i < allConnectors.Count; i++)
                {
                    int? cid = TryGetConnectorId(allConnectors[i]);
                    if (cid.HasValue && cid.Value == equipmentConnectorId.Value)
                    {
                        idMatches.Add(i);
                    }
                }

                if (idMatches.Count == 0)
                {
                    throw new Exception(
                        $"equipmentConnectorId {equipmentConnectorId.Value} 找不到對應的 Connector。" +
                        $"該設備共有 {allConnectors.Count} 個 Connector: {BuildConnectorSummaryText(allConnectors, Enumerable.Range(0, allConnectors.Count))}");
                }
                if (idMatches.Count > 1)
                {
                    throw new Exception(
                        $"equipmentConnectorId {equipmentConnectorId.Value} 命中多個 Connector（理論上應唯一，代表本工具的假設在此模型不成立）。候選: " +
                        BuildConnectorSummaryText(allConnectors, idMatches));
                }

                int idx = idMatches[0];
                Connector byId = allConnectors[idx];
                // Domain 檢查必須先於任何 Direction 讀取：抵達這裡代表 equipmentConnectorId 命中
                // 唯一一個 connector，但完全可能是非 HVAC domain（水路/電源接頭也有自己的 Id）。
                // Connector.Direction 依 API 文件對不支援流量計算的 connector 會拋
                // InvalidOperationException，若不先擋 Domain，「指到非 HVAC connector」這個抵達
                // 此處的主要途徑就會讓下面的錯誤訊息自己拋例外——equipmentConnectorIndex 路徑
                // （下方 if (equipmentConnectorIndex.HasValue) 區塊）同樣在讀 Direction 前先驗
                // Domain，兩條路徑保持對稱。
                if (byId.Domain != Domain.DomainHvac)
                {
                    throw new Exception($"equipmentConnectorId {equipmentConnectorId.Value} 指定的 Connector 不是 HVAC Domain（實際為 {byId.Domain}），無法用於建立風管系統。");
                }

                bool okById = byId.DuctSystemType == systemType || EquipmentDirectionMatchesSystemType(byId.Direction, systemType);
                if (!okById)
                {
                    // Domain 已確認是 DomainHvac，Direction 讀取本已安全；仍加 Safe() 作雙重保險。
                    throw new Exception($"equipmentConnectorId {equipmentConnectorId.Value} 指定的 Connector 的 DuctSystemType={byId.DuctSystemType}／Direction={Safe(() => byId.Direction.ToString())} 與 systemType={systemType} 不符，可能接成錯誤的系統類型。");
                }

                EnsureConnectorNotInSystem(byId, "equipmentConnectorId 指定的 Connector", "請先從原系統移除，或改用其他設備/接頭。");
                chosenIndex = idx;
                return byId;
            }

            if (equipmentConnectorIndex.HasValue)
            {
                int idx = equipmentConnectorIndex.Value;
                if (idx < 0 || idx >= allConnectors.Count)
                {
                    throw new Exception($"equipmentConnectorIndex {idx} 超出範圍（設備共有 {allConnectors.Count} 個 Connector，索引需介於 0 到 {allConnectors.Count - 1}）。");
                }

                Connector specified = allConnectors[idx];
                if (specified.Domain != Domain.DomainHvac)
                {
                    throw new Exception($"equipmentConnectorIndex {idx} 指定的 Connector 不是 HVAC Domain（實際為 {specified.Domain}），無法用於建立風管系統。");
                }

                bool ok = specified.DuctSystemType == systemType || EquipmentDirectionMatchesSystemType(specified.Direction, systemType);
                if (!ok)
                {
                    throw new Exception($"equipmentConnectorIndex {idx} 指定的 Connector 的 DuctSystemType={specified.DuctSystemType}／Direction={specified.Direction} 與 systemType={systemType} 不符，可能接成錯誤的系統類型。若確認要用此接頭，請改用 equipmentConnectorId 明確指定。");
                }

                EnsureConnectorNotInSystem(specified, "equipmentConnectorIndex 指定的 Connector", "請先從原系統移除，或改用其他設備/接頭。");
                chosenIndex = idx;
                return specified;
            }

            var matchIndices = new List<int>();
            for (int i = 0; i < allConnectors.Count; i++)
            {
                Connector c = allConnectors[i];
                if (c.Domain != Domain.DomainHvac) continue;

                bool systemTypeMatches = c.DuctSystemType == systemType;
                bool directionMatches = EquipmentDirectionMatchesSystemType(c.Direction, systemType);
                if (systemTypeMatches || directionMatches)
                {
                    matchIndices.Add(i);
                }
            }

            if (matchIndices.Count == 0)
            {
                throw new Exception(
                    $"設備 ElementId: {equipment.Id.GetIdValue()} 找不到符合 systemType={systemType} 的 HVAC Connector。" +
                    $"該設備共有 {allConnectors.Count} 個 Connector: {BuildConnectorSummaryText(allConnectors, Enumerable.Range(0, allConnectors.Count))}");
            }

            if (matchIndices.Count > 1)
            {
                throw new Exception(
                    $"設備 ElementId: {equipment.Id.GetIdValue()} 找到 {matchIndices.Count} 個符合 systemType={systemType} 的 HVAC Connector，" +
                    "無法自動判定，請用 equipmentConnectorIndex 或 equipmentConnectorId 明確指定其中之一（不會靜默取第一個）。候選: " +
                    BuildConnectorSummaryText(allConnectors, matchIndices));
            }

            chosenIndex = matchIndices[0];
            Connector chosen = allConnectors[chosenIndex];
            EnsureConnectorNotInSystem(chosen, "設備的 Connector", "請先從原系統移除，或改用其他設備。");
            return chosen;
        }

        /// <summary>
        /// 設備 connector 的方向與 systemType 是否吻合：送風設備把空氣「送出」到風管（Out），
        /// 回風/排風設備把空氣從風管「吸入」（In）。
        /// </summary>
        private static bool EquipmentDirectionMatchesSystemType(FlowDirectionType direction, DuctSystemType systemType)
        {
            switch (systemType)
            {
                case DuctSystemType.SupplyAir:
                    return direction == FlowDirectionType.Out;
                case DuctSystemType.ReturnAir:
                case DuctSystemType.ExhaustAir:
                    return direction == FlowDirectionType.In;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 風口的方向與 systemType 是否吻合，與設備端相反：送風系統的風口從風管「進氣」（In），
        /// 回風/排風系統的風口把室內空氣「排出」到風管（Out）。Bidirectional 一律放行不擋。
        /// </summary>
        private static bool TerminalDirectionMatchesSystemType(FlowDirectionType direction, DuctSystemType systemType)
        {
            switch (systemType)
            {
                case DuctSystemType.SupplyAir:
                    return direction == FlowDirectionType.In;
                case DuctSystemType.ReturnAir:
                case DuctSystemType.ExhaustAir:
                    return direction == FlowDirectionType.Out;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 解析單一風口要用哪個 Connector：只接受 Domain.DomainHvac，且風口通常只有一個風管接頭
        /// ——找到 0 個或多於 1 個都拋例外（不靜默取第一個）。方向與 systemType 預期不符時同樣拋例外，
        /// 避免把接錯方向的風口悶聲接進系統。最後確認尚未隸屬其他系統。
        /// </summary>
        private static Connector ResolveTerminalConnector(FamilyInstance terminal, DuctSystemType systemType, out int connectorIndex)
        {
            List<Connector> allConnectors = CollectConnectors(terminal);
            if (allConnectors.Count == 0)
            {
                throw new Exception("此風口沒有任何 Connector。");
            }

            var hvacIndices = new List<int>();
            for (int i = 0; i < allConnectors.Count; i++)
            {
                if (allConnectors[i].Domain == Domain.DomainHvac)
                {
                    hvacIndices.Add(i);
                }
            }

            if (hvacIndices.Count == 0)
            {
                throw new Exception($"找不到 Domain.DomainHvac 的 Connector（此風口共有 {allConnectors.Count} 個 Connector）。");
            }

            if (hvacIndices.Count > 1)
            {
                throw new Exception($"找到 {hvacIndices.Count} 個 HVAC Connector，本工具假設風口只有單一風管接頭，無法自動判定使用哪一個。");
            }

            connectorIndex = hvacIndices[0];
            Connector chosen = allConnectors[connectorIndex];

            if (chosen.Direction != FlowDirectionType.Bidirectional &&
                !TerminalDirectionMatchesSystemType(chosen.Direction, systemType))
            {
                throw new Exception($"Connector 方向為 {chosen.Direction}，與 systemType={systemType} 預期方向不符（可能接錯系統類型或風口種類）。");
            }

            EnsureConnectorNotInSystem(chosen, "此風口的 Connector", "請先從原系統移除，或改用其他風口。");

            return chosen;
        }

        /// <summary>
        /// 蒐集 FamilyInstance 上所有的 MEP Connector，並依 Connector.Id（API 文件載明自 2016
        /// 起提供的穩定識別，而非清單位置）排序後才建立索引清單——Revit 對 ConnectorSet 的
        /// foreach 列舉順序無任何穩定性承諾，若不排序，同一個 Index 在跨 session/族群重載/
        /// 文件重開後可能指到另一顆接頭且不會報錯。非 MEP 族群（MEPModel 為 null）回傳空清單。
        /// </summary>
        private static List<Connector> CollectConnectors(FamilyInstance instance)
        {
            var list = new List<Connector>();
            ConnectorManager cm = instance?.MEPModel?.ConnectorManager;
            if (cm == null) return list;

            foreach (Connector c in cm.Connectors)
            {
                list.Add(c);
            }

            list.Sort((a, b) => (TryGetConnectorId(a) ?? int.MaxValue).CompareTo(TryGetConnectorId(b) ?? int.MaxValue));
            return list;
        }

        /// <summary>
        /// 安全讀取 Connector.Id（穩定識別）。讀不到（理論上不應發生，防禦性寫法）回 null。
        /// </summary>
        private static int? TryGetConnectorId(Connector c)
        {
            try { return c.Id; }
            catch { return null; }
        }

        /// <summary>
        /// 對非流體 domain 或 NonEndConn 型別讀取會拋例外的 connector 屬性，統一包裝成失敗回
        /// "N/A" 而不是讓呼叫端拋出例外——用於組出人類可讀字串的場合（ToString() 系列）。
        /// </summary>
        private static string Safe(Func<string> f)
        {
            try { return f(); }
            catch { return "N/A"; }
        }

        /// <summary>
        /// 安全讀取 connector 的世界座標（mm）。Connector.Origin 依 API 文件在 NonEndConn 型別
        /// 上會拋例外，讀不到回字串 "N/A" 而不是讓呼叫端拋出例外。
        /// </summary>
        private static object SafeOrigin(Connector c)
        {
            try
            {
                XYZ o = c.Origin;
                return new
                {
                    X = Math.Round(o.X * 304.8, 2),
                    Y = Math.Round(o.Y * 304.8, 2),
                    Z = Math.Round(o.Z * 304.8, 2)
                };
            }
            catch
            {
                return "N/A";
            }
        }

        /// <summary>
        /// 組出單一 connector 的完整可稽核資訊：Index（清單位置，不保證跨呼叫穩定）、
        /// ConnectorId（Connector.Id，穩定識別）、DuctSystemType（僅 HVAC domain 才有意義）、
        /// Direction、Shape、Origin（世界座標，mm）。Direction 依 API 文件對不支援流量計算的
        /// connector 會拋 InvalidOperationException，Origin 依 API 文件在 NonEndConn 型別上會
        /// 拋例外，兩者皆各自 Safe 包裝；Shape 文件未載明任何例外，同樣包裝純為防禦、不宣稱有
        /// 文件依據——即使本方法目前只餵 HVAC connector，仍套用同一包裝以免日後放寬篩選時重現
        /// 「診斷訊息自己拋例外」的問題。
        /// </summary>
        private static object BuildConnectorInfo(Connector c, int index)
        {
            int? connectorId = TryGetConnectorId(c);
            return new
            {
                Index = index,
                ConnectorId = connectorId.HasValue ? (object)connectorId.Value : null,
                DuctSystemType = c.Domain == Domain.DomainHvac ? Safe(() => c.DuctSystemType.ToString()) : "N/A",
                Direction = Safe(() => c.Direction.ToString()),
                Shape = Safe(() => c.Shape.ToString()),
                Origin = SafeOrigin(c)
            };
        }

        /// <summary>
        /// 把指定索引的 connector 清單組成一段人類可讀的摘要文字，用於例外訊息內嵌「全部候選」
        /// （這層 dispatcher 的例外只有 Message 字串一個通道，沒有結構化附加資料的管道）。
        /// 每個欄位個別 Safe 包裝，避免對含電源/水路等非 HVAC domain connector 的清單（例如
        /// 「找不到符合 systemType 的 HVAC Connector」訊息會列出全部候選）產生二次例外。
        /// </summary>
        private static string BuildConnectorSummaryText(List<Connector> allConnectors, IEnumerable<int> indices)
        {
            var lines = indices.Select(i =>
            {
                Connector c = allConnectors[i];
                string ductType = c.Domain == Domain.DomainHvac ? Safe(() => c.DuctSystemType.ToString()) : "N/A";
                int? connectorId = TryGetConnectorId(c);
                string idText = connectorId.HasValue ? connectorId.Value.ToString() : "N/A";
                return $"[{i}] Id={idText} Domain={c.Domain} DuctSystemType={ductType} " +
                       $"Direction={Safe(() => c.Direction.ToString())} Shape={Safe(() => c.Shape.ToString())} " +
                       $"Origin={Safe(() => $"({Math.Round(c.Origin.X * 304.8, 1)},{Math.Round(c.Origin.Y * 304.8, 1)},{Math.Round(c.Origin.Z * 304.8, 1)})mm")}";
            });
            return string.Join("; ", lines);
        }

        /// <summary>安全讀取 MechanicalSystem.IsWellConnected；讀不到回 null（不假造成功或失敗）。</summary>
        private static bool? TryGetIsWellConnected(MechanicalSystem system)
        {
            try { return system.IsWellConnected; }
            catch { return null; }
        }

        /// <summary>安全讀取 MEPSystem.IsEmpty；讀不到回 null。</summary>
        private static bool? TryGetIsEmpty(MechanicalSystem system)
        {
            try { return system.IsEmpty; }
            catch { return null; }
        }

        /// <summary>安全讀取 MEPSystem.Elements.Size（系統實際成員數）；讀不到回 null。</summary>
        private static int? TryGetMemberCount(MechanicalSystem system)
        {
            try { return system.Elements?.Size; }
            catch { return null; }
        }

        /// <summary>安全讀取剛建立系統的 ElementId 數值；讀不到回 null（例如交易已回滾）。</summary>
        private static IdType? TryGetSystemId(MechanicalSystem system)
        {
            try { return system.Id.GetIdValue(); }
            catch { return null; }
        }

        /// <summary>
        /// 安全讀取 MEPSystem.BaseEquipment 是否非 null（設備是否已掛上系統）；讀不到回 null。
        /// 與 ActualMemberCount／MemberCountMismatch 刻意分開回報——API 文件明載
        /// MEPSystem.Elements「不含 base equipment/panel」，把設備算進成員數比對會讓帶
        /// equipmentId 的正常成功路徑恆為 MemberCountMismatch=true。
        /// </summary>
        private static bool? TryGetBaseEquipmentAttached(MechanicalSystem system)
        {
            try { return system.BaseEquipment != null; }
            catch { return null; }
        }

        /// <summary>
        /// 讀回一個流量參數的顯示值（AsValueString，依專案顯示單位格式化）與內部原始值
        /// （依 StorageType 取值，一般為 Double／內部單位），兩者互不補值、各自可能為 null。
        /// 找不到參數（param 為 null）時 Found=false，其餘欄位皆為 null，不假造成功。
        /// CalculationValid 反映呼叫端傳入的 IsWellConnected：為 false 時 Revit 文件明載計算
        /// 參數無效，於 Note 明確標註不可作為驗算依據。IsReadOnly 直接回報 param.IsReadOnly，
        /// 不由本檔片面宣稱「唯讀」。candidateCount 大於 1 時代表呼叫端用同名參數比對命中多筆
        /// （Element.LookupParameter/GetParameters 依 API 文件對同名參數的比對結果為「隨機決定
        /// 且未來可能改變」），標記 Ambiguous=true 並在 Note 註明，仍讀取第一筆但誠實揭露風險。
        /// </summary>
        private static object BuildFlowReadback(Parameter param, bool? isWellConnected, int candidateCount = 1)
        {
            bool ambiguous = candidateCount > 1;

            if (param == null)
            {
                return new
                {
                    Found = false,
                    DisplayValue = (object)null,
                    RawValue = (object)null,
                    CalculationValid = isWellConnected.HasValue ? (object)isWellConnected.Value : null,
                    IsReadOnly = (object)null,
                    Ambiguous = ambiguous,
                    Note = (object)null
                };
            }

            object displayValue = param.HasValue ? param.AsValueString() : null;
            object rawValue = param.HasValue && param.StorageType == StorageType.Double
                ? (object)param.AsDouble()
                : null;

            var notes = new List<string>();
            if (isWellConnected == false)
            {
                notes.Add("系統未良好連接，Revit 文件明載此時 calculated parameter 無效，本數值不可作為驗算依據");
            }
            if (ambiguous)
            {
                notes.Add($"此元素同名參數有 {candidateCount} 個，Revit 文件警告比對結果為隨機且未來可能改變，本欄位讀取的是 GetParameters() 回傳的第一筆");
            }

            return new
            {
                Found = true,
                DisplayValue = displayValue,
                RawValue = rawValue,
                CalculationValid = isWellConnected.HasValue ? (object)isWellConnected.Value : null,
                IsReadOnly = param.IsReadOnly,
                Ambiguous = ambiguous,
                Note = notes.Count > 0 ? (object)string.Join("; ", notes) : null
            };
        }
    }
}
