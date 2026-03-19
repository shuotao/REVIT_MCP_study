---
name: qa-review
description: "專案品質檢核：圖紙編號一致性、詳圖元件完整性、視圖組織、參數正確性、MCP 系統健康度。觸發條件：使用者提到 QA、品質檢查、驗證、一致性、專案檢核、系統健康、quality check、review。工具：get_all_sheets、get_detail_components、get_all_views、query_elements_with_filter。"
---

# 專案品質檢核

## Checklist

### 1. 圖紙與編號一致性
- [ ] 所有圖紙使用正確的編號格式（`[專業碼]-[類型][流水號]`）
- [ ] 沒有 `-1` 後綴的重複號碼
- [ ] 圖紙名稱與內容一致
- [ ] 所有視圖已放置到對應圖紙

### 2. 詳圖元件完整性
- [ ] 所有詳圖圖頭參數已匹配
- [ ] 沒有孤兒類型（零實例的類型）
- [ ] 類型命名遵循 `{圖紙號碼}-{圖紙名稱}-{詳圖名稱}` 規則

### 3. 視圖組織
- [ ] 所有視圖已分配到圖紙或標記為工作視圖
- [ ] 視圖樣版一致性套用
- [ ] 沒有重複的視圖名稱

### 4. 參數正確性
- [ ] 防火時效參數已填入所有相關牆體
- [ ] 房間名稱與編號完整
- [ ] 樓層指定正確

### 5. MCP 系統健康度
- [ ] WebSocket 連線（port 8964）回應正常
- [ ] 所有已註冊的工具能正確回傳結果
- [ ] Build 產物與原始碼版本一致

## Execution

依序執行檢查：
1. `get_all_sheets` → 驗證編號
2. `get_detail_components` → 驗證圖頭完整性
3. `get_all_views` → 驗證視圖組織
4. `query_elements_with_filter` → 抽查參數
5. 產出合格/不合格摘要報告

## Path Maintenance

修改檔案路徑或參照時：
- 確認所有 domain 文件的交叉參照仍然有效
- 確認 CLAUDE.md 的觸發關鍵字表是否需要更新
- 確認 Skill 描述與目前可用工具一致

## Reference

詳見 `domain/qa-checklist.md`、`domain/path-maintenance-qa.md`。
