import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * 相機管理（Camera Manager）MCP 測試工具（2026-09-25 Wave 1）。
 *
 * 這些工具呼叫的是 Revit 相機管理停靠面板背後「同一套」服務程式碼
 * （CameraManagerService / CameraKeyPlanService），用途是讓 AI 自動測試面板的行為，
 * 不必每次都靠人工點面板。C# 實作：MCP/Core/Commands/CommandExecutor.CameraManager.cs。
 * 規格：docs/camera-manager/spec.zh-TW.md。
 */
export const cameraManagerTools: Tool[] = [
    {
        name: "list_cameras",
        title: "列出透視相機（相機管理）",
        description: "列出專案中所有透視相機，逐台回報相機管理面板看到的同一份資料：所在樓層（室內＝眼睛所在房間的樓層、室外＝眼睛正下方的建築樓層，附判定來源）、離樓板高度（cm 與 mm）、水平角（0°=+Y 順時針）、俯仰角、視野角（左／右／下／上邊界與水平／垂直總角）、遠裁剪、室內／室外與眼睛所在房間、相機放置時記錄的平面、③ 位置圖底圖挑選器會選哪張平面與步驟、已連結的 Key Plan 平面／剖面／等角視圖 id（KeyPlan.PlanLevelMismatch＝平面的樓層與相機目前樓層不符，重跑一鍵 4 視圖會重建）、剖面方向與剖面位置設定、這台相機是否啟用「剖面轉 90°」（SectionOrientation.Rotate90）、剖面是否被手動移過。唯讀。觸發條件：相機清單、相機管理、Key Plan、人視高檢查、camera list。",
        inputSchema: {
            type: "object",
            properties: {
                nameContains: {
                    type: "string",
                    description: "只列名稱包含此字串的相機（不分大小寫；省略＝全部）"
                }
            },
            required: []
        }
    },
    {
        name: "run_camera_keyplan",
        title: "執行相機一鍵 4 視圖（Key Plan）",
        description: "對一台透視相機執行相機管理面板的「一鍵 4 視圖」核心（建立或更新 Key Plan 平面／剖面／等角三張視圖＋相機標記），與面板同一段程式碼，不經面板 UI。回傳三張視圖的 id、名稱、比例、範圍（平面裁剪框、剖面裁剪框、等角 Section Box，mm）、各視圖內本相機標記元素數（依角色分組），以及關鍵判定（室內／室外、使用的房間、本層頂、扇形半徑、實際剖面方向、是否啟用剖面轉 90°、等角範圍模式、等角近側牆切掉哪兩面）、實際生效的相機標記型式，與裁剪框讀回不符預期（> 1 %）的警告。複製來的平面會先解除 Scope Box、改專案北、圖紙上的標題改為「Key Plan 平面」；平面／剖面隱藏其他剖面／立面／詳圖索引標記（平面保留本相機剖面）。寫入模型（一個交易「建立相機 Key Plan」），不刪除任何使用者元素。室內相機的等角預設切掉觀看者側兩道牆（近側兩面＝房間邊界往內 100 mm，遠側維持房間 +0.6 m）。觸發條件：一鍵 4 視圖、Key Plan、相機平面剖面等角、run keyplan。",
        inputSchema: {
            type: "object",
            properties: {
                cameraId: {
                    type: "number",
                    description: "相機 3D 視圖（或平面上的相機元件）的 ElementId；省略＝目前作用中的透視圖"
                },
                tile: {
                    type: "boolean",
                    description: "true＝完成後依序開啟透視／平面／剖面／等角並並排（同面板）；預設 false（不動視窗）"
                },
                sectionMode: {
                    type: "string",
                    enum: ["room", "sight"],
                    description: "只影響這次執行的剖面方向：room＝對齊房間長邊、sight＝沿視線；省略＝沿用面板設定"
                },
                sectionAnchor: {
                    type: "string",
                    enum: ["camera", "center"],
                    description: "只影響這次執行的剖面位置：camera＝剖切面剛好通過相機眼睛（預設；深度＝往看向側延伸扇形半徑）、center＝視野中心（舊行為）；方向仍依 sectionMode。省略＝沿用面板設定。回傳 Decisions.SectionAnchor"
                },
                isoMode: {
                    type: "string",
                    enum: ["full", "current"],
                    description: "只影響這次執行的等角範圍：full＝整層（見上層樑）、current＝只到當層；省略＝沿用面板設定"
                },
                isoNearWalls: {
                    type: "string",
                    enum: ["camera", "wall", "keep"],
                    description: "只影響這次執行的等角近側（室內相機）：camera＝觀看者側兩面剛好放在眼睛的 X／Y（預設，不留後退距離）、wall＝切到房間邊界往內 100 mm（切掉最靠近觀看者的兩道牆；舊名 cut 也接受）、keep＝不切（四面都留房間 +0.6 m）；省略＝沿用面板設定（預設 camera）。回傳 Decisions.IsoNearWalls／IsoNearWallsLog"
                },
                resetSection: {
                    type: "boolean",
                    description: "true＝即使剖面被手動移過也強制重算覆蓋（同面板「重設剖面位置」）；預設 false"
                },
                sectionRotate90: {
                    type: "boolean",
                    description: "只影響這台相機（不是這次執行）：設定並持久化「剖面轉 90°」——把 Key Plan 剖面方向繞垂直軸轉 90°（原方向的另一向），存在相機元素本身（同事開同一份 .rvt 也看得到）；有給值就會強制這次重設剖面位置（同 resetSection=true）讓效果立即可見。true＝開啟、false＝關閉；省略＝不變更、沿用這台相機目前已存的設定。回傳 Decisions.SectionRotate90，list_cameras 也會回報於 SectionOrientation.Rotate90"
                },
                markerMode: {
                    type: "string",
                    enum: ["B", "A", "AB"],
                    description: "只影響這次執行的相機標記型式：B＝詳圖標記、A＝模型族群標記、AB＝兩者；省略＝沿用面板設定（不寫回設定）"
                }
            },
            required: []
        }
    },
    {
        name: "set_camera_pose",
        title: "修改相機姿態（相機管理）",
        description: "修改透視相機的姿態，與相機管理面板清單就地編輯走同一條程式路徑（一個交易「編輯相機」）：可給離樓板高度 cm、水平角、俯仰角、平面 X/Y（cm，內部原點座標）、遠裁剪 cm，至少一項；未給的欄位維持原值（沿用相機目前實際值）。回傳修改前後的值與 Timing {transactionMs, refreshMs, totalMs}（編輯快速路徑：小交易＋立刻重繪透視圖）。Key Plan 標記／剖面於最後一次編輯 800 ms 後依變動分級自動更新（高度只重畫剖面標記；換樓層／房間才完整重算），不在回應中同步執行。觸發條件：調相機高度、改相機角度、移動相機、set camera pose。",
        inputSchema: {
            type: "object",
            properties: {
                cameraId: {
                    type: "number",
                    description: "相機 3D 視圖（或相機元件）的 ElementId；省略＝目前作用中的透視圖"
                },
                heightAboveLevelCm: { type: "number", description: "離下方建築樓層高度（cm）" },
                yawDeg: { type: "number", description: "水平角（度，0°=+Y 專案北、順時針為正）" },
                pitchDeg: { type: "number", description: "俯仰角（度，0°=水平、正值往上看）" },
                planXcm: { type: "number", description: "眼睛平面 X（cm，內部原點座標系）" },
                planYcm: { type: "number", description: "眼睛平面 Y（cm，內部原點座標系）" },
                farClipCm: { type: "number", description: "遠裁剪距離（cm）；該相機遠裁剪不可寫時會回錯誤" }
            },
            required: []
        }
    },
    {
        name: "copy_camera_to_level",
        title: "複製相機到其他樓層",
        description: "把一台透視相機複製到另一個樓層（同平面位置、同離樓板高度、同方向與視野），與相機管理面板「複製到其他樓層」／清單改樓層同一段程式碼；新名稱「原名 - 樓層名」，重名自動加序號。原相機與其 Key Plan 視圖不變。回傳新相機 id 與名稱。觸發條件：複製相機、相機複製到樓層、copy camera。",
        inputSchema: {
            type: "object",
            properties: {
                cameraId: {
                    type: "number",
                    description: "來源相機 3D 視圖（或相機元件）的 ElementId；省略＝目前作用中的透視圖"
                },
                levelId: { type: "number", description: "目標樓層 ElementId（與 levelName 擇一）" },
                levelName: { type: "string", description: "目標樓層名稱（完全相符、不分大小寫；與 levelId 擇一）" }
            },
            required: []
        }
    },
    {
        name: "get_view_extents",
        title: "讀取視圖範圍與表現設定",
        description: "讀取任一視圖的範圍與表現設定：視圖類型、比例、裁剪框（啟用／可見、Min/Max 與 Transform，mm）、註解裁剪與四邊外擴、3D 視圖的 Section Box／眼睛／方向／透視遠裁剪、剖面遠裁剪、Display Model、詳細等級、視覺型式、視圖樣板、被隱藏的類別（名稱與數量）、各連結模型在此視圖的顯示設定，此視圖專屬的詳圖線／填滿區域／文字註記數量，以及 Scope Box（id／名稱）、方位、圖紙上的標題、目前可見的其他視圖標記數（剖面／立面／詳圖索引 viewer 與立面標記）。唯讀。觸發條件：視圖範圍、裁剪框、Section Box、視圖為什麼看不到、view extents。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: {
                    type: "number",
                    description: "視圖 ElementId（省略＝目前作用中視圖）"
                }
            },
            required: []
        }
    },
    {
        name: "run_camera_overview",
        title: "建立／更新樓層相機總覽平面",
        description: "建立或更新相機管理的每層總覽平面「Key Plan-總覽-<樓層名>」（與面板「一鍵 4 視圖 ▾ → 更新樓層總覽」同一段程式碼）：複製該樓層由底圖挑選器選出的平面、套淡化表現法，裁剪＝該層室內相機視野扇形聯集 +3 m（有室外相機時再聯集底圖的建築範圍），把該層每台相機的 B 型標記（眼睛圓圈、視線、扇形、眼睛楔形）與名稱文字畫在同一張平面上；重跑更新同一張、不重複。複製來的平面會先解除 Scope Box、改專案北、圖紙上的標題改為「樓層總覽 <樓層>」，並隱藏其他剖面／立面／詳圖索引標記。回傳每層的視圖 id、名稱、相機數與名稱、底圖來源、裁剪範圍（mm，讀回視圖的實際值，另附預期值與不符警告）與比例。室內相機以房間所在樓層分組。寫入模型（一個交易「更新樓層總覽」），不刪除任何使用者元素。觸發條件：樓層總覽、相機總覽平面、所有相機畫在一張平面、camera overview。",
        inputSchema: {
            type: "object",
            properties: {
                levelId: { type: "number", description: "樓層 ElementId（與 levelName 擇一）" },
                levelName: { type: "string", description: "樓層名稱（完全相符、不分大小寫；與 levelId 擇一）" },
                all: {
                    type: "boolean",
                    description: "true＝所有有相機的建築樓層各建立／更新一張（忽略 levelId／levelName）；預設 false"
                }
            },
            required: []
        }
    },
    {
        name: "create_camera_sheet",
        title: "相機一鍵出圖紙",
        description: "為一台透視相機建立或更新圖紙「Key Plan-<相機名>」（與相機管理面板「一鍵出圖紙」同一段程式碼）：左側約 55% 放透視圖，右欄由上而下放 Key Plan 平面／剖面／等角（列高依內容長寬比分配）；可繪區＝圖框在圖紙上的外接框內縮邊界，再扣掉圖框標題欄（分析圖框線；偵測不到＝橫式保留右側 20%）。本工具的 Key Plan 視圖自動改為放得進格子的最細標準比例（1:20–1:200），出圖用透視複本等比放大裁剪框填滿格子約 90%（FOV 不變）；視埠類型沿用專案既有、會顯示標題的類型，不新建。相機還沒有 Key Plan 視圖時先執行一鍵 4 視圖。已在本相機圖紙上的視埠重新排版；透視圖若已在其他圖紙上，改放（或沿用）出圖用透視複本「<相機名>-Key Plan 圖紙」並同步到原相機的方向／裁剪／遠裁剪；其他視圖在別張圖紙上則回報並略過。回傳圖紙 id／編號／名稱、圖框來源、可繪區與每個視埠的 id、狀態、中心與尺寸（圖紙座標 mm）、是否超出格子。寫入模型（交易「建立相機圖紙」），不刪除任何使用者元素。觸發條件：相機出圖、一鍵出圖紙、相機圖紙、camera sheet。",
        inputSchema: {
            type: "object",
            properties: {
                cameraId: {
                    type: "number",
                    description: "相機 3D 視圖（或平面上的相機元件）的 ElementId；省略＝目前作用中的透視圖"
                },
                titleBlockTypeId: {
                    type: "number",
                    description: "圖框類型（FamilySymbol）的 ElementId；省略＝面板設定，未設定時用專案內最常用的圖框"
                }
            },
            required: []
        }
    },
    {
        name: "camera_version",
        title: "相機視角版本（列出／儲存／還原／刪除）",
        description: "管理相機的具名「視角版本」（與相機管理面板右鍵「視角版本 ▸」同一段程式碼）。版本存在相機視圖的 Extensible Storage（JSON：眼睛位置、前方向、上方向、裁剪框與其 Transform、遠裁剪），同事開同一個 .rvt 也能切換。action=list 唯讀列出；save 以 name 儲存目前視角（同名覆蓋）；restore 以一個交易「還原相機視角：<名稱>」套回該版本（面板已載入時相機標記會自動更新）；delete 只移除該筆版本資料，不刪除任何模型元素。回傳目前相機姿態與全部版本（眼睛 mm、水平角、俯仰角、裁剪框尺寸、遠裁剪）。觸發條件：相機版本、儲存視角、還原視角、A 案 B 案視角、camera version。",
        inputSchema: {
            type: "object",
            properties: {
                cameraId: {
                    type: "number",
                    description: "相機 3D 視圖（或相機元件）的 ElementId；省略＝目前作用中的透視圖"
                },
                action: {
                    type: "string",
                    enum: ["list", "save", "restore", "delete"],
                    description: "list（預設，唯讀）／save／restore／delete"
                },
                name: {
                    type: "string",
                    description: "版本名稱（save／restore／delete 必填；最多 60 字，區分大小寫）"
                }
            },
            required: []
        }
    }
];
