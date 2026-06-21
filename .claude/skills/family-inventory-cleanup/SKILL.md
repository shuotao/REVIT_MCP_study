---
name: family-inventory-cleanup
description: "族群盤點與清整編排:對指定 category 做雙軸盤點(載入 vs 放置)→ 參數簽章比對 → 提案 → 使用者裁決 → 執行(purge 未使用 / merge 重複型 / rename)→ 驗證。每個破壞性動作前執行 7 項強制前置檢查。觸發條件:使用者提到族群整理、類型盤點、未使用類型、purge、重複類型、合併類型、family cleanup、type inventory、unused type、duplicate type、清掉沒用到的類型。工具:list_family_symbols、query_elements_with_filter、get_element_info、change_element_type、delete_element。"
---

# 族群盤點與清整

`category` 為變數(本次可為 Doors、下次 Windows 或任意族群)。方法與安全規則以 `domain/family-inventory-cleanup.md` 為準;本檔是工具編排。

## 流程

### Phase 0:錨定
`get_active_view`(備援 `get_all_views`)。盤點本身是全專案、與視圖無關,但仍確認連線活著。

### Phase 1:雙軸盤點
0. **族群發現(未知 category 必做)**:先 `query_elements(category)` 取得有哪些 family + 放置;**不可假設已知 family 名稱**,也不可從載入軸起手。
1. **放置軸**:`query_elements_with_filter`(`filters:[{field:"Family",operator:"equals",value:<族群>}]`,高 `maxCount`,無 `viewId`)→ 每實例 Type / 尺寸。回報 `Count` 並比對 `maxCount` 防截斷。
2. **載入軸**:`list_family_symbols`(**逐族群名稱過濾**,例 `"Rectangular Column (Off Center)"`)→ 該族群所有類型 + TypeId。
   - ⚠️ 禁用廣詞(如 `"Door"`/`"Column"`):撞 100 筆上限被截斷、且跨類別污染。
3. 交叉:**載入 − 放置 = 未使用類型**;每型算實例數。

### Phase 2:比對(簽章強制,嚴禁名稱捷徑)
**判重複/名實一律 `get_element_info` 逐型讀實際參數簽章**(門 Width/Height;柱 Depth/Width…)。名稱、` N` 尾碼、`get_category_fields` 只是線索,**不得作判定依據**(`get_category_fields` 在異質類別會抽錯樣本)。群聚(同層+連號 Mark)僅在有 Mark 的類別有效。
**名≠參數時**:簽章只揭露不一致、不決定設計本意 → 可能是結構錯誤(斷面不足),**停下交使用者裁決**。

### Phase 3:提案【STOP / G2】
分型輸出:`[PURGE 未使用]`、`[MERGE 重複]`、`[RENAME]`(需新工具)、`[MANUAL]`(格式/族群定義,Family Editor)。每項列影響筆數 + 將執行工具 + 可逆性。**停下等使用者裁決。**

### Phase 4:執行【G3 前置檢查 → 動作】
動作前**逐項過 7 項強制前置檢查**(見 domain)。重點:
- 刪類型前**驗 0 實例**;有實例先 `change_element_type` 轉移再刪。
- **逐一執行,不並行**(ExternalEventManager 單一 action 會覆寫)。
- 列出 `delete_element` 的**連帶刪除**影響(工具不會自己報)。

範式 A — Purge:驗 0 實例 → `delete_element` 逐一。
範式 B — Merge:`get_element_info` 比對 → `change_element_type` 轉移 → 驗來源 0 實例 → `delete_element` 刪空型。

### Phase 5:驗證【G4】
重查類型數/實例數(類型應降、放置實例不應誤減)→ 輸出 `SELF-AUDIT`(資料誠實 / 0 實例已驗 / 連帶已揭露 / 未並行 / 步驟完整)。

## Quick Reference

```
盤點:      Phase 0 → Phase 1(雙軸) → Phase 2
Purge:     ...→ Phase 3【STOP】→ 驗0實例 → delete_element 逐一 → 驗證
Merge:     ...→ Phase 3【STOP】→ get_element_info 比對 → change_element_type → 驗空 → delete_element → 驗證
```

## Reference

方法、強制前置檢查與能力邊界詳見 `domain/family-inventory-cleanup.md`;另見 `domain/element-query-workflow.md`、`domain/tool-capability-boundary.md`、`domain/qa-checklist.md`。
