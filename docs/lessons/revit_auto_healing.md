---
description: 關於 Revit MEP 管網之「ConnectTo Auto-Healing」的血淚教訓與防呆守則
---

# 讀者必讀：為什麼在 Revit 裡面切除與接管這麼困難？

這真的是所有寫過 Revit MEP API 的開發者共同的血淚心聲！

在一般的 3D 繪圖軟體裡（像是 AutoCAD, SketchUp 或是 Rhino），切開管子並放進兩片法蘭，就真的只是「切除線條」加上「把模型擺上去」而已。但在 Revit 裡面，它是被設計成 **「參數化與連動式」的邏輯水網**。

## 致命的安全機制：Auto-Healing

Revit 的管網有幾個嚴格的潛規則：
1. **不允許邏輯上相連的元件在物理上脫節**：它不允許兩個物理空間上「不相連（距離太遠）」的管件，被程式腳本以 `ConnectTo()` 強制邏輯相連。
2. **原力拉扯（Auto-Routing 自動修復）**：當你把背對背的兩片法蘭「邏輯上」強制接在一起，但它們物理距離上還差了一段空隙時，Revit 就會覺察到不合法。它會擅自發動「原力拉扯」，把兩根水管硬生生拉到碰在一起。
3. **無聲的吞噬**：接著 Revit 會發現兩片法蘭與被拉在一起的管線發生重疊，於是不發出任何錯誤訊息（無聲無息地），自作聰明地將法蘭刪除，把管路變回了一根沒切過的原本水管！

## 解決方案：防呆防線（Safety Lock）

在寫 MEP 腳本需要「切斷並置換配件」時，請務必加上這道安全鎖：

```python
# 取得對接元件的 Connector
mating_A = ...
mating_B = ...

if mating_A and mating_B:
    # 永遠先計算物理間隙
    dist_mating = mating_A.Origin.DistanceTo(mating_B.Origin) * 304.8
    
    # [安全鎖] 如果間隙大於容許的公差 (如 2mm)，絕對不要執行 ConnectTo!
    if dist_mating > 2.0:
        logger.warning("法蘭背對背間距過大 ({}mm)，已攔截 ConnectTo 避免 Revit 自動連線扯壞管線。".format(dist_mating))
    else:
        try:
            mating_A.ConnectTo(mating_B)
        except Exception as e:
            logger.error("互接失敗: {}".format(e))
```

這樣一來，我們就能保住這些透過 API 辛辛苦苦放置的管件，讓它們乖乖待在它們該待的位置上，不再被 Revit 修復機制吃掉！
