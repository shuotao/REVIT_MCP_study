# HANDOFF — MEP Settings 領域盤點工具(Segment / Size 系統化查詢)

> 送出日期:2026-08-06｜來源:MEP 認證教學 session(C:\Users\01102088\Desktop\MEP)實測需求
> 接手:REVIT_MCP 專案 session。請依本規格開發、build、QA、count-sync。**API 可行性已確認(附反射證據),不需重驗。**
> 決策人:TAO。**開發前若對「台灣目標尺寸集」有疑義,先問 TAO 再動寫入工具。**

---

## 1. 為什麼要做(動機)

Revit 的 `Manage → MEP Settings → Mechanical Settings` 把**風管/管路的 Segment 與 Size 目錄**藏在對話框裡:
- Pipe 有約 **16 種 Segment**(Copper K/L/M、Ductile Iron 6 種、PVC Sch40/80、SS 5S/10S、Carbon Steel Sch40/80),**每種各一份 size catalog(約 16 尺寸)**。
- 使用者無法一眼看到全專案有哪些尺寸、哪些被勾 `Used in Size Lists` / `Used in Sizing`,更無法系統化跟**台灣 CNS** 對帳。
- 這些資訊**Schedule 撈不到、System Browser 也顯示不到**(那些只作用於模型元件/系統,不是設定定義)。**唯一路徑是 Revit API。**

**目標**:給 Revit 使用者一支工具,一次呼叫**完整、有系統地盤點**整個專案的 Segment/Size,讓在地化(台灣 CNS 對帳)有資料基礎。

---

## 2. API 可行性(已於 2026-08-06 反射 RevitAPI.dll 2026 確認)

**讀取路徑(Tool 1 用):**
```
FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeSegment))
  每個 PipeSegment : Segment (: Element)
    .MaterialId (ElementId)         → doc.GetElement → Material.Name
    .ScheduleTypeId (ElementId)     → 例 "Schedule 40"
    .Roughness (double)
    .SizeCount (int)
    .GetSizes() -> ICollection<MEPSize>
       MEPSize.NominalDiameter (double, 內部單位=feet)
       MEPSize.InnerDiameter   (double, feet)
       MEPSize.OuterDiameter   (double, feet)
       MEPSize.UsedInSizeLists (bool)   ← 對話框左勾選欄
       MEPSize.UsedInSizing    (bool)   ← 對話框右勾選欄
```
Duct 圓管尺寸:`Autodesk.Revit.DB.Mechanical.DuctSizes` / `DuctSizeSettings`(可迭代,`DuctSizeIterator`)。

**寫入路徑(Tool 2 用,第二階段):**
```
Segment.AddSize(MEPSize) / Segment.RemoveSize(double nominalDiameter)
new MEPSize(double nominalDiameter, double innerDiameter, double outerDiameter,
            bool usedInSizeLists, bool usedInSizing)   ← 建構子已確認
PipeSegment.Create(Document, ElementId material, ElementId schedule, ICollection<MEPSize>)
```

**單位注意**:MEPSize 全部是**內部單位 feet**,輸出前用 `UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Millimeters)` 轉 mm(與 set_project_units 一致)。所有寫入須包 Transaction。

---

## 3. 工具規格

### Tool 1（優先、唯讀、零風險）:`get_mep_segments_and_sizes`
一次回傳全專案 pipe segments +（可選）round duct sizes 的完整目錄。

**輸入**:`{ includeDuct?: boolean = true, usedOnly?: boolean = false }`
**輸出(每個 segment)**:
```json
{
  "segments": [
    {
      "id": 123456, "kind": "pipe",
      "material": "Copper", "schedule": "Copper - K",
      "roughness_mm": 0.0015, "sizeCount": 16,
      "sizes": [
        { "nominal_mm": 25.4, "inner_mm": 25.27, "outer_mm": 28.58,
          "usedInSizeLists": true, "usedInSizing": true }
      ]
    }
  ],
  "ductRoundSizes_mm": [ ... ]
}
```

### Tool 2（第二階段、寫入、**先別做**）:`curate_mep_sizes`
per-segment `add` / `remove` 尺寸。**須先與 TAO 談定「台灣 CNS 目標尺寸集」**(見 §6 註記:金屬管 JIS/CNS「A」呼稱=英制 nominal,不必換;PVC 才要換 CNS 系列)。本次**只登記,不實作**。

---

## 4. 實作位置(兩層 pattern,對照 `set-project-units` 前例 commit f32c726)

| 層 | 檔案 |
|---|---|
| C# 指令 | `MCP/Core/Commands/CommandExecutor.MepSettings.cs`(新 partial)+ `CommandExecutor.cs` dispatch switch 加 case |
| TS 工具定義 | `MCP-Server/src/tools/base-tools.ts` 加 tool def(bridge 1:1 自動映射) |
| Skill | `.claude/skills/mep-settings-inventory/SKILL.md`(可選;若做,skill 數 +1) |

**參考 skill**:`dll-to-mcp-tool`(建工具流程)、`set-project-units`(最近前例,ProjectUnits.cs 可抄骨架)。

---

## 5. Count-sync(交付前必做)

用 `claude-md-sync` skill 同步。基線(set_project_units 後)= **168 tools / 51 skills**:
- Tool 1 → tools **168 → 169**
- 若加 skill → skills **51 → 52**
- 同步位置:`CLAUDE.md`、`README.md`、`README.zh-TW.md`、BIM_MCP 相關 HTML、skills-index 卡片(與 f32c726 同一組檔案)。

## 6. 驗證與交付

1. Build 0 errors(dll-to-mcp-tool 的 build 流程)。
2. 在模型 `_CTEST_M1_01`(桌面 MEP\M1\)上實測 Tool 1 回傳,人工核對 Copper-K 目錄應含 6.35 / 25.4 / 152.4 / 304.8 mm 等。
3. **commit 到本地即可,不要 push**(依 REVIT_MCP 專案慣例 / TAO 決定;set_project_units 亦僅本地 commit f32c726 未 push)。
4. 完成後回填一行到本檔末「執行結果」。

---

## 附:台灣在地化判讀(給 Tool 2 前的背景,勿在 Tool 1 硬編)
- **金屬管(鋼/銅/不鏽鋼)**:台灣 CNS 沿用 JIS「A」呼稱(15A/20A/25A…),物理尺寸=英制 nominal(25A=1"=25.4mm)→ **現有尺寸對台灣鋼管正確,只是標籤醜,不必砍**。
- **PVC**:台灣 CNS PVC 系列 ≠ 英制 Schedule 40/80 → **這才是要 curate 的**。
- 結論:在地化不是「全換公制」,要**逐 segment 判**,所以 Tool 1 的全量 dump 是前提。

## 執行結果(接手 session 回填)
- [x] Tool 1 開發完成 / commit:`get_mep_segments_and_sizes`。C# = `MCP/Core/Commands/CommandExecutor.MepSettings.cs` + `CommandExecutor.cs` dispatch;TS 定義放 **`mep-tools.ts`(非本文建議的 base-tools.ts)** —— mep-tools 同時在 `full` 與 `mep` profile 內,歸屬更正確。API 先以 MetadataLoadContext 反射 RevitAPI **2026 與 2022** 雙版確認介面一致(且 `PipeSegment` 是 `Segment` 唯一衍生類別,故管段掃描完備)。R26 + R24 build 皆 0 errors。
- [x] 模型實測回傳核對:**Revit 2024 / Snowdon Towers Sample HVAC**(桌面 MEP 那批課程檔全是 `Format: 2025`,Revit 2024 開不了,故改用此範例模型)。16 管段 / 235 筆管尺寸 / 風管 Round 72 + Rect 59 + Oval 93。Copper-K 含 6.35 / 25.4 / 152.4 / 304.8 mm ✅,整組即英制序列換算,單位正確。red→green:取消勾選 Copper-K 的 6.35 mm 後,旗標翻 false、counts 15/16、`usedOnly=true` 正確濾掉。
- [x] count-sync:tools **168 → 169**(10 處計數宣稱);skill 經 TAO 裁決本次不做,skills 維持 **51**。`verify-qaqc.ps1 -SkipBuild -SkipDeploy` = 59 PASS / 0 FAIL / 0 WARN / 3 SKIP。

### 實測後對規格的兩處修正(§3 輸出格式已與本文不同)
1. **風管不輸出 inner/outer**。實測三張表 224 筆的 `inner`/`outer` **全部是固定佔位值 12 ft(3657.6 mm)** —— Revit 的風管尺寸表只有 nominal 一個維度。輸出會讓對帳誤讀,故改為只給 `nominal_mm`,並附 `DuctSizeNote` 說明。`ductRoundSizes_mm` 依原規格保留。
2. **新增 `summaryOnly` / `segmentName` 兩個輸入參數**。全量 dump 為 **87,534 字元**,超過 MCP 單次回傳上限。建議用法:先 `summaryOnly=true` 看全案 16 段的尺寸數與勾選數,再 `segmentName="Copper - K"` 鑽單段。另為每段/每形狀補 `usedInSizeListsCount` / `usedInSizingCount`,回傳補 `SegmentTotalCount`(有篩選時同時告知全案總數,避免局部誤讀為全案)。

### 給 Tool 2 的新情報:對話框會量化 inner diameter(重要)
實測過程中發現的 **Revit 行為**(非本工具所致):進出 `Mechanical Settings → Segments and Sizes` 按 OK 之後,Copper-K 全 16 筆的 **inner diameter 被量化到最近的 1/32"**。
- 開檔當下(run 1)是真實銅管 K 型 ID:0.305" / 0.995" / 5.741" …
- 按過 OK 之後(run 2)全變成 1/32 的整數倍:0.3125" / 1.0" / 5.75" …
- outer diameter 未變(它本來就落在 1/32 格線上)。

也就是說**光是「打開那個對話框按 OK」就會靜默劣化內徑資料**,而內徑正是水力計算與 CNS 對帳要用的欄位。這對 §6 的在地化判讀有兩個含意:
- 做 CNS 對帳前應先跑一次 Tool 1 存基準,不要在未存檔前反覆進出對話框。
- Tool 2(`curate_mep_sizes`)以 API 寫入反而**比手動改對話框安全** —— API 直接給 `MEPSize(nominal, inner, outer, ...)`,不經過顯示精度的往返。這是 Tool 2 的一個額外正當理由。
