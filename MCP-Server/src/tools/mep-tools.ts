/**
 * MEP 管線工具 — mep Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const mepTools: Tool[] = [
    {
        name: "get_connector_info",
        description: "取得 MEP 元素（管、風管、線管等）的接頭（Connector）資訊，包含座標、連接狀態、形狀等。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要查詢的 MEP 元素 ID" },
            },
            required: ["elementId"],
        },
    },
    {
        name: "get_mep_segments_and_sizes",
        description: "一次盤點整個專案的 MEP Segment 與 Size 目錄（唯讀）。對應 Manage → MEP Settings 裡 Segments and Sizes 對話框的內容：每個管段（PipeSegment＝材質 × Schedule，如 Copper - K、PVC - Sch 40）各一份尺寸表，含 nominal / inner / outer 直徑與 Used in Size Lists、Used in Sizing 兩個勾選狀態；可一併回傳風管（Round / Rectangular / Oval）尺寸表（風管只有 nominal，Revit 的 duct inner/outer 是佔位值故不輸出）。這些資訊 Schedule 與 System Browser 都撈不到。所有尺寸以 mm 回傳，供台灣 CNS 尺寸對帳使用。全量 dump 可達數百筆，先用 summaryOnly=true 看全貌，再用 segmentName 鑽單一管段。",
        inputSchema: {
            type: "object",
            properties: {
                summaryOnly: { type: "boolean", description: "只回傳每個管段/風管形狀的統計（尺寸筆數、勾選筆數），不逐筆列出尺寸，預設 false。全案盤點建議先用這個。" },
                segmentName: { type: "string", description: "只回傳名稱含此字串的管段（不分大小寫），例如 'Copper'、'PVC'、'Copper - K'。給了這個參數時 includeDuct 預設變 false。" },
                includeDuct: { type: "boolean", description: "是否一併回傳風管尺寸表（Round / Rectangular / Oval）。預設 true；但有給 segmentName 時預設 false。" },
                usedOnly: { type: "boolean", description: "只列出有勾選 Used in Size Lists 或 Used in Sizing 的尺寸，預設 false（全列）。" },
            },
        },
    },
    {
        name: "get_mep_settings",
        description: "讀取 Manage → MEP Settings 裡「尺寸目錄以外」的所有設定頁（唯讀）：Duct/Pipe 的 Angles（fitting 角度用法與各角度勾選狀態）、Pipe 的 Slopes（坡度清單）與 Fluids（流體類型，可選溫度/黏度/密度表）、兩邊的 Calculation（空氣密度與黏度、network-based 計算、接頭容差）、尺寸命名與註記字串、標高文字（Centerline / Set Up / Flat on Top …）、以及 Hidden Line。角度以度回傳、長度以 mm 回傳，物理量另附以專案顯示單位格式化的字串。管段與尺寸目錄不在這裡，請用 get_mep_segments_and_sizes。",
        inputSchema: {
            type: "object",
            properties: {
                includeFluids: { type: "boolean", description: "是否回傳流體類型清單（含是否被使用、溫度筆數），預設 true。" },
                includeFluidTemperatures: { type: "boolean", description: "是否逐筆列出每個流體的溫度/黏度/密度表，預設 false（表可能很長，預設只給筆數）。" },
            },
        },
    },
    {
        name: "get_mep_size_usage",
        description: "盤點模型裡「真的有元件在用」哪些 MEP 尺寸（唯讀）。這與 get_mep_segments_and_sizes 讀的「目錄裡列了哪些尺寸」是兩件事——目錄不知道自己有沒有被用，只看目錄就刪除等於盲刪。掃描來源包含直管/直風管的寬高與直徑，以及配件與附件的 Connector 尺寸（漏掃配件會把「只有變徑頭在用」的尺寸誤判成可刪）。管的用量以 Pipe.PipeSegment 精確歸戶到各 segment。回傳每個目錄尺寸的 usageCount 與 removable 旗標，另列 orphans（模型有用但目錄沒有的尺寸，屬「該增」的候選）。刪除任何尺寸前一定要先跑這支。方法見 domain/mep-mechanical-settings.md。",
        inputSchema: {
            type: "object",
            properties: {
                scope: { type: "string", description: "'both'（預設）、'duct' 或 'pipe'。" },
                shape: { type: "string", description: "只看某個風管形狀：Round / Rectangular / Oval（選填）。" },
                segmentName: { type: "string", description: "只看名稱含此字串的管段，例如 'Copper'（選填）。" },
                includeUnused: { type: "boolean", description: "是否一併列出用量為 0 的目錄尺寸（可刪候選），預設 true。" },
                includeElementIds: { type: "boolean", description: "是否附上使用該尺寸的元件 ID 樣本，方便追查是誰擋住刪除，預設 false。" },
                maxElementIdsPerSize: { type: "number", description: "每個尺寸最多附幾個元件 ID 樣本，預設 5。" },
            },
        },
    },
    {
        name: "curate_mep_sizes",
        description: "增減 MEP 尺寸目錄（會修改模型設定）。規則:增無限制;減只能刪「模型中沒有任何元件在用」的尺寸,工具會自行跑用量盤點把在用的擋下並說明是誰在用。執行採四步:①列表(dryRun,預設 true)→②單一 Transaction 執行(可 Ctrl+Z)→③QC 重讀目錄逐筆比對→④偵測到誤刪自動以快照原樣加回。回傳一律附 RestorePayload(被移除尺寸的完整定義),供事後人工復原。新增管尺寸必須同時給 inner_mm 與 outer_mm(內外徑是水力計算依據,不由工具臆造);風管只需 nominal_mm。務必先用 get_mep_size_usage 確認用量。協定見 domain/mep-mechanical-settings.md。",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string", description: "'pipe' 或 'duct'（必填）。" },
                segmentName: { type: "string", description: "target='pipe' 時必填,要精確到單一管段,例如 'Copper - K'。比對到多個會直接報錯要求更精確。" },
                shape: { type: "string", description: "target='duct' 時必填:Round / Rectangular / Oval。" },
                add: {
                    type: "array",
                    description: "要新增的尺寸。pipe 需 nominal_mm + inner_mm + outer_mm;duct 只需 nominal_mm。usedInSizeLists / usedInSizing 省略時預設 true。",
                    items: {
                        type: "object",
                        properties: {
                            nominal_mm: { type: "number", description: "公稱尺寸(mm)" },
                            inner_mm: { type: "number", description: "內徑(mm)。target='pipe' 時必填。" },
                            outer_mm: { type: "number", description: "外徑(mm)。target='pipe' 時必填。" },
                            usedInSizeLists: { type: "boolean", description: "是否出現在尺寸下拉選單,預設 true。" },
                            usedInSizing: { type: "boolean", description: "是否參與自動定尺寸,預設 true。" },
                        },
                        required: ["nominal_mm"],
                    },
                },
                remove: {
                    type: "array",
                    description: "要移除的公稱尺寸(mm)清單。模型中仍在使用的會被擋下並回報使用者。",
                    items: { type: "number" },
                },
                dryRun: { type: "boolean", description: "預設 true,只回傳計畫不改任何東西。確認清單無誤後才帶 false 執行。" },
                ignoreUnattributedFittings: { type: "boolean", description: "管配件沒有 PipeSegment 屬性,只能比對直徑,預設會保守擋下相符的尺寸。設 true 可略過這層保護（僅在你確認過那些配件不屬於此 segment 時使用）。預設 false。" },
            },
            required: ["target"],
        },
    },
    {
        name: "get_space_centroid",
        description: "回傳每個 Revit Space（OST_MEPSpaces）的代表點座標（mm）。這是本專案第一個支援 Space 品類的工具——get_room_info 等既有工具只吃 OST_Rooms，傳 Space 進去會 cast 失敗。供後續自動放置元件（如風口）使用。代表點依序嘗試 LocationPoint → BoundingBox 中心 → BoundingBox 內網格取樣，每一步都用 Space.IsPointInSpace 驗證是否真的落在空間內（凹形空間的 BoundingBox 中心可能落在空間外），全部失敗則回傳 null 並標示 PointSource='None'，不做靜默 fallback。也回傳 BoundedHeight（= Volume / Area，可用來估算前期天花高度）。未放置或未封閉的 Space 仍會列出，但標示 IsPlaced=false 且幾何欄位為 null；IsPlaced=false 時可再看 HasLocation 與 IsEnclosed 區分「根本沒放置」與「已放置但未封閉（unenclosed）」這兩種處置方式完全不同的狀況。",
        inputSchema: {
            type: "object",
            properties: {
                spaceIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "只處理這些 Space 的 ElementId。省略則處理全部 Space。",
                },
                levelName: { type: "string", description: "只處理指定樓層名稱（或名稱包含此字串）的 Space。省略則不依樓層過濾。" },
                gridSamples: { type: "number", description: "當 LocationPoint 與 BoundingBox 中心都驗證失敗時，退回網格取樣的每邊取樣點數（gridSamples × gridSamples）。預設 5，上限 25，超過會被夾制（夾制後的實際值會如實反映在回傳結果的 GridSamples 欄位）。" },
            },
        },
    },
    {
        name: "place_family_instances",
        description: "批次把族群實例放置到指定世界座標點，可放置後設定實例參數。這是本專案第一個不綁死品類的泛用放置工具——place_furniture 寫死 OST_Furniture、create_door/create_window 需要 hosting 牆，都無法放風口（Air Terminal）等 MEP 族群。placements 每筆優先用 typeId（FamilySymbol 的 ElementId）直接取用，不做名稱比對；未給 typeId 才走 familyName/typeName 名稱比對（支援「FamilyName: TypeName」複合寫法）。名稱比對採兩段式：給了 familyName 就只在該族群底下比對 typeName，命中多個會直接報錯要求改用 typeId，不會悶聲選第一個；familyName 為空才允許 typeName 全域比對，同樣命中多個會報錯。x/y/z 皆為世界座標，單位 mm，get_space_centroid 回傳的座標可直接餵入（歷史註記：2026-08-14 曾實測 z 是「相對所在樓層標高的偏移」而非世界座標，直接餵 get_space_centroid 的 Z 會整批高出一個樓層標高且無錯誤訊息；2026-08-15 已將 C# 端 z 語意改為世界座標，換算改在工具內部用 Level.ProjectElevation 完成，見下方 z 欄位的部署時滯警告；此偏移語意僅在單一模型的特定設備族群上實測過，可能為族群／放置型式相依，Revit API 文件並未記載此語意）。levelName 省略時的自動選層邏輯是取「ProjectElevation 最接近 z 且不高於 z」的樓層，z 改為世界座標後這段邏輯才自洽（此分支同樣未經 Revit 實測，見下方 levelName 欄位說明）。回傳結果的 LevelName 是實際採用的樓層，請與你給的 levelName 核對是否一致；並務必用回傳的 PlacedPoint（與 PlacedBBoxCenter，見下方）核對實際落點。rotation 為繞放置點鉛直軸旋轉的角度（度），預設 0。tag 是呼叫端自訂識別字串，原樣回傳供比對。整批只開一個 Transaction，單筆失敗不影響其他筆；若實例已建立但後續步驟（旋轉/設參數）才失敗，該筆結果仍會附上 ElementId 並在 Error 註明「實例已建立但後續步驟失敗」，避免孤兒元件追蹤不到。放置後、讀取旋轉軸與最終落點之前，工具會補一次 Regenerate（2026-08-14 實測某些族群如 Exhauster with Cabinet，放置後未 Regenerate 直接讀 LocationPoint 會拿到 (0,0,0)，即使 BoundingBox 證實位置正確；同批 AHU 沒有這個問題，屬族群相依）；加上參數設定完後的既有一次性 Regenerate，每筆 placement 在這兩個時點各至多一次、上限共兩次；另外每個尚未啟用的族群類型（FamilySymbol）在批次中第一次用到時會多觸發一次 Activate 後的 Regenerate，但只發生在該 symbol 第一次出現時，不隨 placement 筆數累加。批次規模較大時請沿用 domain/space-centroid-placement.md 的分批估算，避免撞破 MCP-Server 的 30 秒 timeout。回傳結果每筆附 PlacedPointSource：'LocationPoint' 代表 PlacedPoint 是讀到的實際落點（可用來偵測 Revit 放置時是否對點做了吸附/調整）；'RequestedFallback' 代表讀不到 LocationPoint、PlacedPoint 只是回聲你給的 x/y/z，此時不可用它判斷是否發生吸附。另附 PlacedBBoxCenter：取元件 BoundingBox 中心（mm），與 PlacedPoint 是兩個獨立訊號、互不補值——LocationPoint 因族群相依而失真時（如上述 Exhauster 案例），可用 PlacedBBoxCenter 交叉核對；BoundingBox 讀不到時為 null，不會用 PlacedPoint 頂替。PlacedBBoxCenter 是元件整體外接盒的中心，對非對稱族群本來就不等於插入點，且依 Revit API 文件該盒會納入 flip controls 等不明顯的幾何；交叉核對的用途是判斷量級是否合理（例如 PlacedPoint 回 (0,0,0) 而 BBox 中心落在預期位置附近），不是逐位比對相等。回傳逐筆 Success/Error 與整體成功/失敗計數。parameters 是放置後要設定的實例參數 {參數名: 值}；每個參數設定後會讀回兩個獨立欄位：WrittenBackDisplay（Parameter.AsValueString()，Revit 依專案顯示單位格式化的值）與 WrittenBackRaw（依 StorageType 取的內部單位原始值，ElementId 類參數若讀到有效 ID 就回該 ID、讀到「已設定但無效」則回 -1、真正讀不出來才回 null），兩者互不補值、各自可能為 null，務必併看才能判斷是數字寫錯還是單位換算錯——modify_element_parameter 走 Revit 內部單位，Set() 成功不代表寫進去的是你要的量（domain/tool-capability-boundary.md 的 lesson L16，實測表在該檔「寫入值／讀回值／比值」表格，記錄過「寫入 89.2 讀回 9093」的落差），這裡不做任何單位換算或猜測，只誠實回報寫進去之後實際是什麼，請自行比對 Requested 與這兩個 WrittenBack 欄位判斷是否正確。⚠️ 部署時滯警告（2026-08-15）：z 的世界座標語意是 C# 端的改動，需重新編譯部署 DLL 並重啟 Revit 才會生效；若你連到的 add-in 尚未更新，行為仍是舊版的「相對樓層偏移」語意，且不會有任何錯誤提示你版本不符。PlacedBBoxCenter 亦為本次新增欄位；若回傳結果中沒有這個欄位，即代表你連到的 add-in 尚未更新，z 仍為舊版的相對樓層偏移語意——這是唯一可用的版本判別訊號。首次使用本工具時請先只放 1 件，用回傳的 PlacedPoint 與 PlacedBBoxCenter 核對落點是否符合世界座標預期，確認無誤後再批次放置。",
        inputSchema: {
            type: "object",
            properties: {
                category: { type: "string", description: "品類名稱或 BuiltInCategory 名（如 'Air Terminals'、'OST_DuctTerminal'），用於縮小 FamilySymbol 搜尋範圍。省略則不限品類搜尋全部 FamilySymbol。僅影響 familyName/typeName 名稱比對；有給 typeId 的筆會直接用 ID 取用，不受此篩選限制。給了但無法解析（拼錯或不是合法品類名）會直接報錯，不會靜默改成搜尋全部品類。" },
                placements: {
                    type: "array",
                    description: "逐筆放置指令，至少需要一筆。",
                    items: {
                        type: "object",
                        properties: {
                            typeId: { type: "number", description: "FamilySymbol 的 ElementId。優先使用；給了就直接取用，不做名稱比對。批次放置時用 ID 比用名稱比對可靠，能避免重名/大小寫/全形半形造成的誤放。" },
                            familyName: { type: "string", description: "族群名稱。typeId 未給時，搭配 typeName 用於名稱比對。" },
                            typeName: { type: "string", description: "類型名稱。typeId 未給時必填；可為單純類型名，或 'FamilyName: TypeName' 複合寫法（此時可省略 familyName）。" },
                            x: { type: "number", description: "世界座標 X，單位 mm。" },
                            y: { type: "number", description: "世界座標 Y，單位 mm。" },
                            z: { type: "number", description: "世界座標 Z，單位 mm。get_space_centroid 回傳的 Z 可直接餵入。⚠️ BREAKING（2026-08-15）：舊版語意為「相對所在樓層標高的偏移」，現已改為世界座標，換算改在工具內部完成。⚠️ 部署時滯警告：C# 端需重新部署 DLL 並重啟 Revit 才會採用新語意，若對接的 add-in 尚未更新，行為仍是舊版的相對偏移且不會報錯提示——首次使用請先放 1 件，用回傳的 PlacedPoint / PlacedBBoxCenter 核對落點是否符合世界座標預期。" },
                            levelName: { type: "string", description: "放置的樓層名稱。會先精確比對（區分大小寫的完全相同），沒有精確命中才退回既有的模糊比對（可能把 '1F' 誤配到 'B1F'，因為模糊比對是雙向 Contains）。省略時自動取「ProjectElevation 最接近 z 且不高於 z」的樓層（z 為世界座標）；仍找不到則該筆失敗並回報原因。結果的 LevelName 是實際採用的樓層，請核對是否等於你給的值。此自動選層分支至今未經 Revit 實測（僅靜態驗證），需要確定樓層歸屬時仍建議明確指定 levelName。" },
                            rotation: { type: "number", description: "繞通過放置點的鉛直軸旋轉，單位度，預設 0。旋轉軸使用實例實際落點（而非你給的 x/y/z），因為 Revit 放置時可能對點做吸附/調整。" },
                            tag: { type: "string", description: "呼叫端自訂識別字串（例如 Space 編號），原樣回傳，方便把結果對回你自己的清單。" },
                            parameters: {
                                type: "object",
                                description: "放置後要設定的實例參數，格式 {\"參數名\": 值}。每個參數設定後都會讀回 WrittenBackDisplay（顯示值）與 WrittenBackRaw（內部單位原始值）兩個獨立欄位，兩者互不補值、各自可能為 null，務必併看才能確認寫入的是你要的量——本工具不做任何單位換算或猜測。",
                            },
                        },
                        required: ["x", "y", "z"],
                    },
                },
            },
            required: ["placements"],
        },
    },
    {
        name: "create_duct_system",
        description: "把一組風口與一台設備組成 Revit 風管系統（MechanicalSystem）。equipmentId 可省略，用於建立「已決定獨立成系統但設備尚未選定」的中間狀態（端點未歸屬任何系統代表還沒決定，已建系統但設備欄空白代表已決定獨立、設備待補）——省略時會把 baseConnector 傳 null 呼叫 Revit API；Revit API 文件明載 baseEquipmentConnector 為選填、允許傳 null（\"The base equipment is optional for the system, so this argument may be null.\"），但此路徑目前僅為文件契約推導，執行期尚未實測，若實測發現與文件不符，例外訊息會原樣往上拋，不做替代方案。equipmentId、equipmentConnectorIndex、equipmentConnectorId 顯式傳 null 等同省略該參數。terminalIds 每筆若解析失敗（找不到元素、非 FamilyInstance、非整數 ElementId、該風口沒有任何 Connector、與 equipmentId 重複、在 terminalIds 內重複、Connector 已隸屬其他系統、方向或 Domain 不符、該風口有多於一個 HVAC Connector（本工具假設風口只有單一風管接頭，不自動挑選））都只記入該筆 TerminalResults 的 Error，不中止其餘風口；但全部風口都失敗時不建立系統，直接整體報錯。TerminalResults 陣列每筆對齊輸入 terminalIds 的同一位置，附 Index（輸入陣列位置，從 0 起算）、ElementId（terminalIds 該筆解析出的整數值；只有當這筆輸入本身不是合法整數時才為 null，一旦成功解析出整數，即使後續驗證失敗——與 equipmentId 重複、terminalIds 內重複、找不到元素、非 FamilyInstance、Connector 已隸屬其他系統等——ElementId 仍會回填該值方便對應是哪一筆，不會因為失敗就抹成 null，也不會用 0 這種本身合法的 ElementId 當失敗標記）、Success、Error、ConnectorIndex（該風口採用的 HVAC connector 在排序後清單中的位置索引，只有 Success=true 時才有值，其餘為 null）；另有 SystemNameAfter，只在該筆 Success=true 且整體 CommitStatus='Committed' 時才有值，其餘情況為 null；其值即頂層 SystemName 的逐筆複本。設備上常有多個 Connector（送風/回風/排風/水路/電源），選錯不會報錯、只會接成錯的系統類型：本工具依 systemType 篩選 Domain.DomainHvac 且 DuctSystemType 或 Direction 相符者，剛好 1 個才自動採用並在 EquipmentConnector 完整回報其 Index/ConnectorId/DuctSystemType/Direction/Shape/Origin（部分族群的非流體 Connector 讀取 Direction/Shape/Origin 會拋例外，已包 try/catch，讀不到回 \"N/A\"）；未帶 equipmentId 時 EquipmentConnector 為 null；找到 0 個或多於 1 個一律拋例外並在訊息列出全部候選，多於 1 個時請改用 equipmentConnectorIndex（清單位置索引，本工具已先依 Connector.Id 排序才建立索引，故同一族群在接頭數不變時穩定；但族群重載後若接頭增減，索引會位移且不會報錯，需跨次呼叫穩定指定同一接頭時請用 equipmentConnectorId）或 equipmentConnectorId（Connector.Id，穩定識別）明確指定，不會靜默取第一個；equipmentConnectorIndex/equipmentConnectorId 指定的 Connector 仍會驗證 DuctSystemType/Direction 是否與 systemType 相符，不符會拋例外（不會只驗 Domain 就放行）。設備或風口的 Connector 若已隸屬其他系統，Revit 不允許加入第二個系統，本工具會在對應的錯誤訊息中列出既有系統的 ElementId 與名稱。回傳的 SystemId 是新建立的 MechanicalSystem 的 ElementId，讀取時機在 trans.Commit() 之後——CommitStatus 非 'Committed'（交易未成功提交）或讀取本身失敗時為 null，此時沒有系統可供後續工具引用。SystemType 是實際採用的系統類型（即 systemType 參數解析後的值），恆有值、不受 CommitStatus 影響。EquipmentId 是實際採用的設備 ElementId，在設備 Connector 解析成功時（早於交易 Commit）就已決定；未帶 equipmentId 時為 null。SystemName 是實際生效的系統名稱，於 Commit 之後讀回；CommitStatus 非 Committed 或讀取失敗時為 null、不補值。SystemFlowReadBack 是建立後從系統本身讀回的總流量，不是呼叫端寫進去的，是 Revit 依系統成員實際計算的結果——但只有在回傳的 IsWellConnected=true 時才是有效計算值；IsWellConnected=false（例如風口已放置但風管尚未繪製，系統未良好連接）時 Revit 文件明載該計算參數無效，請勿用於驗算，回傳的 Note 會註明原因。IsWellConnected 讀 MechanicalSystem.IsWellConnected，CommitStatus 非 Committed 或讀取失敗時為 null。回傳同時附 SystemIsEmpty（讀 MEPSystem.IsEmpty），CommitStatus 非 Committed 或讀取失敗時同樣為 null、不補值。若有設備，一併讀回其 Air Flow 參數放進 EquipmentAirFlowReadBack（附 IsReadOnly 供核對是否唯讀；若該族群同名參數有多個，Ambiguous=true 且讀取的是 Revit GetParameters() 回傳的第一筆，比對結果依 Revit 文件為隨機且可能變動）。兩者都拆成 Found/DisplayValue（顯示值）/RawValue（內部值）/CalculationValid/IsReadOnly/Ambiguous/Note 欄位，互不補值。回傳另附 ActualMemberCount（系統實際成員數，讀不到為 null）與 ExpectedMemberCount（= SuccessCount；Revit API 文件明載 MEPSystem.Elements 不含 base equipment 或 panel，故預期成員數只算風口成功筆數、不含設備）供交叉比對，不一致時 MemberCountMismatch=true。設備是否已掛上系統由獨立欄位 BaseEquipmentAttached 回報（true/false；交易非 Committed 或讀取失敗時為 null，不補值），不混進成員數比對。整批在單一 Transaction 內完成；systemName 有給時嘗試命名，但 Revit 要求系統名稱在專案內唯一，撞名時系統仍會建立並保留 Revit 自動產生的名稱，命名失敗的原因放在 SystemNameError（不會整批 rollback），你要求的原始名稱回報在 SystemNameRequested。回傳的 CommitStatus 是 Transaction.Commit() 的實際回傳狀態，Success 欄位同時要求 CommitStatus='Committed' 且所有風口都成功；CommitStatus 非 Committed 時 CommitError 會說明、且不會讀回任何流量或系統成員資訊（避免對已回滾的元素讀值）。TerminalCount 是輸入 terminalIds 的總筆數（不論成敗）；SuccessCount／FailureCount 分別是成功／失敗筆數，兩者相加等於 TerminalCount。FailureCount=0 不等於整體 Success=true——即使所有風口都解析成功，只要 CommitStatus 不是 'Committed'，Success 仍為 false，請兩個欄位一起看。本工具的 MCP timeout 為 30 秒且逾時不會回滾——若逾時，Revit 端交易仍可能已提交，請務必用 query_elements_with_filter 或 get_element_info 核對模型實際狀態後再重試。直接重試會失敗——第一次已提交時，設備與風口的 Connector 已隸屬該系統，本工具會在設備端直接中止、或全部風口都記為失敗後因無可用成員而整體報錯（與 systemName 是否重複無關；名稱撞名本身不會讓呼叫失敗，只會填 SystemNameError）。作用視圖為明細表時，Revit 會在每次模型變更後重算整張表，依 domain/tool-capability-boundary.md L17 實測這會顯著提高逾時機率，回傳的 PerformanceWarning 會提醒（不擋執行），建議先用 set_active_view 切到一般平面圖再執行；回傳附 ElapsedMs 供核對耗時。",
        inputSchema: {
            type: "object",
            properties: {
                systemType: { type: "string", description: "風管系統類型：SupplyAir（送風）/ ReturnAir（回風）/ ExhaustAir（排風），大小寫不敏感，對應 Revit DuctSystemType。只接受這三個名稱字串，不接受數字或逗號清單。" },
                terminalIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "要納入系統的風口（Air Terminal）ElementId 清單，至少一筆，須為整數。逐筆驗證是否有 Connector、其 HVAC Connector 的 Domain 與方向（依 systemType 推定應有的流向）、是否恰好只有一個 HVAC Connector（本工具假設風口只有單一風管接頭，不自動挑選）、是否已隸屬其他系統、是否與 equipmentId 重複、是否在本清單內重複，任一項不符會記入該筆 TerminalResults 的 Error，不中止其餘。",
                },
                equipmentId: { type: "number", description: "設備（Mechanical Equipment）ElementId，可省略——省略時建立「已決定獨立成系統但設備尚未選定」的系統（baseConnector 傳 null，依 Revit API 文件此參數為選填）。若給定，會依 systemType 從設備的 Connector 篩選 Domain.DomainHvac 且 DuctSystemType 或 Direction 相符者；剛好 1 個才自動採用，0 個或多於 1 個一律拋例外（多於 1 個時請改用 equipmentConnectorIndex 或 equipmentConnectorId 明確指定，不會靜默取第一個）。傳 null 等同省略。" },
                equipmentConnectorIndex: { type: "number", description: "當 equipmentId 的設備有多個符合 systemType 的候選 Connector 時，用這個索引明確指定要用哪一個（對應例外訊息或 EquipmentConnector.Index 的位置索引，從 0 起算）。注意：這是清單位置索引，本工具已先依 Connector.Id 排序才建立索引，故同一族群在接頭數不變時穩定；但族群重載後若接頭增減，索引會位移且不會報錯，需跨次呼叫穩定指定同一接頭時請改用 equipmentConnectorId。指定的 Connector 仍會驗證 DuctSystemType/Direction 是否與 systemType 相符。候選只有 1 個時可省略。傳 null 等同省略。沒有 equipmentId 時不可給此參數。" },
                equipmentConnectorId: { type: "number", description: "以 Revit 的 Connector.Id（穩定識別，自 Revit 2016 起提供，不受列舉順序影響）明確指定要用哪個設備 Connector，優先於 equipmentConnectorIndex。命中 0 個或多於 1 個都拋例外並列出候選。指定的 Connector 仍會驗證 DuctSystemType/Direction 是否與 systemType 相符。沒有 equipmentId 時不可給此參數。傳 null 等同省略。" },
                systemName: { type: "string", description: "建立後要嘗試設定的系統名稱，省略則沿用 Revit 自動產生的名稱。Revit 要求系統名稱在專案內唯一，撞名時系統仍會建立並保留自動名稱，不會整批 rollback，失敗原因會放在回傳的 SystemNameError；你給的原始值回報在 SystemNameRequested，實際生效的名稱在 SystemName。" },
            },
            required: ["systemType", "terminalIds"],
        },
    },
    {
        name: "add_pipe_cap",
        description: "在管件的未連線端安裝管帽或法蘭。自動尋找開放的接頭並連接。",
        inputSchema: {
            type: "object",
            properties: {
                pipeId: { type: "number", description: "管件的元素 ID" },
                familyName: { type: "string", description: "要安裝的管帽/法蘭族群名稱" },
            },
            required: ["pipeId", "familyName"],
        },
    },
    {
        name: "export_families",
        description: "把專案中已載入的可編輯族群另存為 .rfa 檔到指定資料夾,建立可重用元件庫。預設匯出管配件(OST_PipeFitting)與管附件(OST_PipeAccessory)。自動依類別建立子資料夾;subFolderBySeries=true 時再依族群名稱系列(CIP/DWV/碳鋼.../)細分。略過系統族群、現地(in-place)與不可編輯族群。",
        inputSchema: {
            type: "object",
            properties: {
                replaceGreenFence: {
                    type: "object", description: "Batch replace explicit selected fence IDs along measured collinear runs with tight-joint green-fence modules. Preserve explicit gate IDs/transforms; preview/project token required for apply. Duplicate width/height types, never edit existing types. Selected nested warning lights follow source deletion. Single reversible transaction group with post-commit gate/placement verification.",
                    properties: {
                        sourceIds: { type: "array", items: { type: "integer" } },
                        lightIds: { type: "array", items: { type: "integer" } },
                        gateIds: { type: "array", items: { type: "integer" }, minItems: 2, maxItems: 2 },
                        familyFile: { type: "string" },
                        fillerFile: { type: "string", description: "Verified narrow closure RFA without planters." },
                        standardWidth: { type: "boolean", default: false, description: "1100mm modules; only the last module uses the remainder. Remainders below300mm use planter-free filler." },
                        reflowExisting: { type: "boolean", default: false, description: "Reflow only Abby-owned green fence instances, preserving measured endpoints and gates." },
                        expectedProjectId: { type: "string" }, previewToken: { type: "string" },
                        allowedDependentIds: { type: "array", items: { type: "integer" }, description: "Only explicitly reviewed additional IDs from DeletionPreview.Unexpected; never protected gates. Automatic owned sketch/hidden constraints are classified separately." },
                    }, required: ["sourceIds", "gateIds", "familyFile"], additionalProperties: false,
                },
                familyConstraints: {
                    type: "object",
                    description: "Inspect or repair native RFA constraints on an isolated copy; never edits the active project/source RFA. Inspect without sourcePath reads the active family. Repair requires explicit declarative steps plus at least two different expected-width flex tests. apply=true/dryRun=false exports only after all tests pass before and after reopening. Existing output is never overwritten. Coordinates mm; raw inspected double parameter values are Revit internal feet.",
                    properties: {
                        mode: { type: "string", enum: ["inspect", "repair"], default: "inspect" },
                        sourcePath: { type: "string", description: "Existing absolute RFA path. Required for repair; omitted inspect reads the active family." },
                        fileName: { type: "string", description: "New output RFA filename only, no directory. Required for repair." },
                        familyPath: { type: "array", items: { type: "string" }, description: "For inspect: exact nested family names from source root." },
                        steps: {
                            type: "array", items: {
                                type: "object", properties: {
                                    familyPath: { type: "array", items: { type: "string" } },
                                    typeName: { type: "string" },
                                    actions: { type: "array", items: {
                                        type: "object", properties: {
                                            kind: { type: "string", enum: ["parameter", "associate", "dimension", "align", "plane", "move", "curve", "removeDimension", "compactSiteGateParameters"] },
                                            parameter: { type: "string" }, create: { type: "boolean" }, instance: { type: "boolean" },
                                            dataType: { type: "string", enum: ["length", "yesNo"] },
                                            formula: { type: ["string", "null"] }, valueMm: { type: "number" }, valueBool: { type: "boolean" },
                                            elementId: { type: "integer" }, elementParameter: { type: "string" }, viewId: { type: "integer" },
                                            references: { type: "array", description: "Two stable-reference strings or objects {elementId,kind,end}. kind=end for curve endpoint; FamilyInstanceReferenceType names for instance; reference planes need only elementId.", items: { type: ["string", "object"] } },
                                            first: { type: ["string", "object"] }, second: { type: ["string", "object"] },
                                            startMm: { type: "array", items: { type: "number" }, minItems: 3, maxItems: 3 },
                                            endMm: { type: "array", items: { type: "number" }, minItems: 3, maxItems: 3 },
                                            offsetMm: { type: "array", items: { type: "number" }, minItems: 3, maxItems: 3 },
                                            normalMm: { type: "array", items: { type: "number" }, minItems: 3, maxItems: 3 }, name: { type: "string" },
                                        }, required: ["kind"], additionalProperties: false,
                                    } },
                                    flexCases: { type: "array", items: { type: "object" }, description: "Optional local-family tests, same shape as root flexCases." },
                                }, required: ["actions"], additionalProperties: false,
                            },
                        },
                        flexCases: { type: "array", items: {
                            type: "object", properties: {
                                name: { type: "string" },
                                parameters: { type: "array", items: { type: "object", properties: {
                                    parameter: { type: "string" }, valueMm: { type: "number" }, valueBool: { type: "boolean" },
                                }, required: ["parameter"], additionalProperties: false } },
                                expectedWidthMm: { type: "number", exclusiveMinimum: 0 }, expectedHeightMm: { type: "number", exclusiveMinimum: 0 },
                                toleranceMm: { type: "number", exclusiveMinimum: 0, maximum: 2, default: 0.5 },
                                elementChecks: { type: "array", items: { type: "object", properties: {
                                    elementId: { type: "integer" },
                                    minMm: { type: "array", items: { type: ["number", "null"] }, minItems: 3, maxItems: 3 },
                                    maxMm: { type: "array", items: { type: ["number", "null"] }, minItems: 3, maxItems: 3 },
                                }, required: ["elementId"], additionalProperties: false } },
                            }, required: ["parameters"], additionalProperties: false,
                        } },
                    }, additionalProperties: false,
                },
                constructionFence: {
                    type: "object",
                    description: "Create a parametric Taichung fixed sheet-metal fence RFA in Revit 2024. Type parameters drive width, height and sheet thickness; optional centered pedestrian door. Flexes native geometry before saving and after reopening; no host-project placement. Frame sizes are modeling assumptions, not structural design. Never overwrites an existing file.",
                    properties: {
                        templatePath: { type: "string", description: "Absolute Revit 2024 unhosted metric Generic Model .rft path." },
                        curtainLouver: { type: "boolean", default: false, description: "Generate parametric louver core plus curtain-window adapter in Revit 2024. Width/height mm; default1125x3650,24 equal-spaced blades at45 degrees. Tests core flex and curtain-host resizing in an isolated temporary project before exporting." },
                        greenFence: { type: "boolean", default: false, description: "Generate parametric construction green fence in Revit2024, default1100x2400mm above ground. Six rows/eight columns of shared pots, true150mm mesh arrays, catch trough, concrete base/foundation, irrigation and rear braces. Width-height flex verified before/after save; leaves/unmarked details schematic." },
                        tightJoints: { type: "boolean", default: false, description: "greenFence=true: move brace centers to module end planes so adjacent modules share overlapping brace geometry; creates a separate family." },
                        adaptivePlanters: { type: "boolean", default: false, description: "Green fence with planter count adapting to width300..2200mm, 125mm pitch." },
                        fillerOnly: { type: "boolean", default: false, description: "Narrow closure10..299mm without planters or braces." },
                        curtainTemplatePath: { type: "string", description: "curtainLouver: absolute Revit curtain-window .rft template path." },
                        sharedUnits: { type: "boolean", default: false, description: "siteGate=true: rebuild with shared nested panel/diagonal units, equal spacing and 15 host size/formula parameters; component flex and reopened tests required before publishing." },
                        drawingStyle: { type: "boolean", default: false, description: "siteGate=true: drawing-style gate with H250x125 posts, C125x50x20x2.3 chords, 16 ribbed/grille panels, tracks and shared truss units; default10000x6000mm. Unspecified details schematic; width-height flex verified." },
                        siteGate: { type: "boolean", default: false, description: "重建工地大門關閉外觀；原生寬高連動門柱、29格門片、頂部桁架，不含折疊動作。" },
                        widthMm: { type: "number", minimum: 10, maximum: 16000, description: "單位mm；各模式由後端檢核範圍。綠籬收邊10..299、可調盆數綠籬300..2200；百葉窗500..3000。" },
                        heightMm: { type: "number", minimum: 1200, maximum: 6000, description: "圍籬2400..4500；大門3000..6000；百葉窗1200..6000，預設3650。單位mm。" },
                        sheetThicknessMm: { type: "number", minimum: 1.2, maximum: 3, default: 1.2 },
                        door: {
                            type: "object", description: "Optional centered door opening from ground, closed leaf with 5mm perimeter gap; plinth and low rails stop at door jambs.",
                            properties: {
                                widthMm: { type: "number", minimum: 600, default: 1000 },
                                heightMm: { type: "number", minimum: 800, default: 1200 },
                            }, additionalProperties: false,
                        },
                    },
                    required: ["templatePath"],
                    additionalProperties: false,
                },
                dryRun: { type: "boolean", default: true, description: "constructionFence/familyConstraints: preview without publishing an output RFA. Constraint repair uses a temporary isolated copy and real flex tests." },
                apply: { type: "boolean", default: false, description: "constructionFence/familyConstraints requires apply=true and dryRun=false to create output RFA." },
                outputFolder: { type: "string", description: "輸出根資料夾絕對路徑,例如 C:\\Users\\xxx\\Desktop\\MEP管元件庫。不存在會自動建立。" },
                categories: {
                    type: "array",
                    items: { type: "string" },
                    description: "要匯出的 BuiltInCategory 名稱清單(如 OST_PipeFitting、OST_PipeAccessory)。省略則預設這兩類。",
                },
                subFolderBySeries: { type: "boolean", description: "是否在類別資料夾下再依族群名稱系列建立子資料夾(預設 false,只依類別分層)。" },
                overwrite: { type: "boolean", description: "目標 .rfa 已存在時是否覆寫(預設 true)。" },
            },
            required: ["outputFolder"],
        },
    },
];
