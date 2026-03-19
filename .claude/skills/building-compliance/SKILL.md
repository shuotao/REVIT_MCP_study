---
name: building-compliance
description: "建築法規檢討：居室採光比（第 41 條）、容積率與樓地板面積計算、停車位尺寸與數量檢核。觸發條件：使用者提到採光、daylight、容積率、FAR、樓地板面積、建蔽率、停車、parking、法規檢討、送審、regulatory。工具：get_room_daylight_info、query_elements_with_filter、get_rooms_by_level。"
---

# 建築法規檢討

## 子工作流程

### 1. 居室採光檢討（第 41 條）

依建築技術規則檢核居室自然採光是否合規：
1. `get_room_daylight_info` → 回傳窗戶面積與居室面積
2. 計算：有效採光面積 ÷ 居室面積 ≥ 法定比例
3. 標示不合規的房間
4. 以顏色覆寫視覺化結果

**關鍵規則：**
- 住宅居室：採光面積 ≥ 居室面積的 1/8
- 只計算面向戶外的窗戶（不含室內窗）
- 有效開口從窗台到天花板量測

### 2. 容積率檢討

檢核樓地板面積與容積率是否合規：
1. `get_rooms_by_level` → 逐層收集所有房間
2. 計算每層的總/淨樓地板面積
3. 與都市計畫允許容積率比對
4. 產出檢討摘要

**計算注意：**
- 免計項目：機械室、樓梯、電梯（依法規排除）
- 陽台：封閉式以 50% 計入
- 地下層：適用不同計算規則

### 3. 停車位檢討

驗證停車位尺寸與淨空：
1. 查詢停車相關元素
2. 檢查標準尺寸（小型車最小 2.5m × 5.5m）
3. 檢查柱邊與牆邊淨空
4. 確認無障礙車位是否符合規定

**尺寸標準：**
- 標準車位：2500mm × 5500mm
- 無障礙車位：3500mm × 5500mm
- 車道寬度：雙向 5500mm

## 參考

詳見 `domain/daylight-area-check.md`、`domain/floor-area-review.md`、`domain/parking-space-review.md`、`domain/parking-clearance-check.md`。
