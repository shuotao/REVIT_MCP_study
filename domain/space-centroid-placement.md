---
name: space-centroid-placement
description: "把元件批次放置到每個空間代表點的通用方法：代表點的四階段決策與驗證、族群放置型式決定技術路線、座標語意必須實測、放置後的歸屬驗證。適用風口、偵煙探測器、撒水頭、燈具等「每室一件」的批次放置。當使用者提到空間中心點、代表點、centroid、批次放置、逐室放置、風口放置、air terminal、place_family_instances、get_space_centroid、Space 幾何、IsPointInSpace 時觸發。"
metadata:
  version: "1.2"
  updated: "2026-08-15"
  created: "2026-08-14"
  references:
    - "MCP/Core/Commands/CommandExecutor.SpaceGeometry.cs"
    - "MCP/Core/Commands/CommandExecutor.FamilyPlacement.cs"
    - "Autodesk MEP 課程模型 M1_02 工作副本（教學演練，非真實專案）"
  related:
    - mep-space-demand-matrix.md
    - tool-capability-boundary.md
    - smoke-detector-check.md
  referenced_by: []
  tags: [空間, space, room, 代表點, centroid, 中心點, 批次放置, placement, 風口, air terminal, 偵煙探測器, 撒水頭, IsPointInSpace, 座標語意, 歸屬驗證]
---

# 把元件放到每個空間的代表點

## 適用範圍

**「每個空間放一件，然後驗證它真的屬於那個空間」**——這個問題的形狀跨用途相同：

- 風口（送風／排風）
- 偵煙探測器
- 撒水頭
- 照明器具

本檔談的是**共通方法**，不是任一用途的設計準則。用途本身的判定（要放幾個、風量多少、間距多寬）由各自的 domain 決定。

---

## 一、代表點不等於 BoundingBox 中心

最直覺的做法是取空間 BoundingBox 的中心。**凹形空間會讓這個做法失效，而且失效時不會報錯。**

### 四階段決策（每一步都必須驗證）

```
1. LocationPoint          → IsPointInSpace 驗證
       ↓ 不通過
2. BoundingBox 中心        → IsPointInSpace 驗證
       ↓ 不通過
3. BBox 內 N×N 網格取樣    → 逐點驗證，取第一個通過者
       ↓ 全部失敗
4. 回傳 null，標示來源為 "None"
```

**第 4 步不得靜默 fallback 成任何點。** 回報「這一間我找不到安全點」是有用的資訊；回報一個落在空間外的點，會讓下游以為成功。

**每一階段都必須回報實際用了哪一種。** 呼叫端要能區分「一次就命中」與「退到第三順位才勉強找到」——後者值得人工覆核。

### 實測證據

M1_02 演練模型的 L 形走廊：

| 空間 | LocationPoint | BBoxCenter | 差距 |
|---|---|---|---:|
| `1010 PATIENT COORIDOR` | (−1554.98, −6180.73) | (1980.57, −356.19) | 約 6,500 mm |
| `2002 COORIDOR` | (−897.59, −2551.71) | (2990.22, −522.88) | 約 4,400 mm |

**BBox 中心幾乎確定落在走廊外。** 若照它放置，元件會被算進隔壁空間——呈現的症狀是「某一間漏了、另一間多了」，**不是「放錯位置」**。查錯方向會被帶偏。

### Room 與 Space 沒有共用的點在內測試

`Room.IsPointInRoom` 與 `Space.IsPointInSpace` 是**不同方法**，`SpatialElement` 基底沒有共同介面。實作時需依型別分支，不能寫一份通用的。

---

## 二、族群放置型式決定技術路線，而且不可回頭

| 放置型式 | API 需求 | 前期可行性 |
|---|---|---|
| 非宿主（level-based） | XYZ ＋ Level | **可行**——前期沒有天花板也放得了 |
| face-based / hosted | 需要面的 Reference | 前期無天花板時**無法放置** |

**不同放置型式的族群之間不能換型。** 選錯要刪掉重放，連同已設的參數。

### 選型前先列出候選

同一個專案裡常同時存在兩組同名族群。M1_02 的 Air Terminals 就有：

```
Supply Diffuser                          ← 非宿主
Supply Diffuser - Square - Hosted        ← 需要宿主面
```

**光看類型名稱（`24 x 24 Face 12 x 12 Connection`）分不出來，要看族群名稱。**

---

## 三、🔴 座標語意必須在串接前實測

**取代表點的工具與放置元件的工具，各自的座標慣例可能不一致——而不一致時不會有任何錯誤訊息。**

M1_02 的實測：

```
get_space_centroid       回傳世界座標          Z = 30480（樓層標高）
place_family_instances   的 z 實為相對樓層偏移
```

第一次測試傳入 `z = 33123.2`（世界座標），實際落點是 `Z = 63603.2`——**多了 30480，正是樓層標高**。風口被放到 63.6 公尺高，遠在天花板之上。

**驗證方式**：那一間的 `Actual Supply Airflow` 維持 0.0，而 `Specified` 是 89.2。修正 `z` 為偏移值後重放，`Actual` 立刻變成 89.2。

### 規則

1. **不要假設兩支工具的座標慣例一致**，即使它們設計上就是要串接使用。
2. **先放一件，讀回實際落點，與要求值相減。** 差值若等於某個樓層標高，就是偏移／世界座標的語意差。
3. 這個錯誤**在批次放置時完全無感**——45 件全部等量偏移，彼此相對位置正確，3D 視圖看起來像一個完整樓層浮在空中。

**這一節記錄的教訓本身不因後續修正而失效**——它是「兩支工具串接前必須實測座標語意」這個方法論，不是某一次錯誤的一次性修補記錄。

> **2026-08-15 後續（已完成 Revit 實測）**：本專案的 `place_family_instances` 已把 `z` 改為世界座標（換算改在工具內部用 `Level.ProjectElevation` 完成，`get_space_centroid` 的輸出可直接餵入），與 `get_space_centroid` 統一座標系。
>
> **實測紀錄**：部署新 DLL 並重啟 Revit 後，於 M1_02 工作副本明確指定 `levelName = "GROUND FLOOR"`（`get_all_levels` 回報該樓層標高 30480 mm）、傳 `z = 33480`，回傳 `PlacedPoint = (0, 0, 33480)`、`PlacedPointSource = "LocationPoint"`。**與送入值相等，不是舊語意會產生的 63960（33480 + 30480）。** 測試族群為非宿主的 `Supply Diffuser`／`24 x 24 Face 12 x 12 Connection`，驗畢即刪除該測試元件。
>
> ⚠️ **實測通過不解除本節的驗證程序。** C# 端改了不代表你連到的 add-in 已經更新——DLL 未重新部署時行為仍是舊的相對樓層偏移，**且不會有任何錯誤提示**。**版本判別訊號：回傳中有沒有 `PlacedBBoxCenter` 這個 key**（舊版不回傳此欄位）。每次重新部署、換機器、或不確定對接的是哪一版時，仍請走一次「先放一件、讀回實際落點」。

> **族群相依提醒**：上述「相對樓層偏移」的實測結論本身也只在單一模型的特定設備族群（如 `Exhauster with Cabinet`）上驗證過。Revit API 文件對 `NewFamilyInstance(XYZ, FamilySymbol, Level, StructuralType)` 的 `location` 參數僅定義為「the physical location where the instance is to be placed」，並未記載偏移語意；同一份 remarks 說明此 overload 對 Beams 等多端點族群「插入方式與單點族群相同」（"inserted in the same manner as single point instances"），但插入後其端點需改由 `Element.Location` 調整——亦即 `Location` 的語意本來就隨族群型式而異。也就是說，這個偏移假設本身可能是族群／放置型式相依，不是對所有族群都成立的固定規則——與第四節、第七節記載的族群相依現象屬同一類問題，換一批未實測過的族群時務必重新驗證。

---

## 四、🔴 放置工具必須回報「實際落點」而非回聲

**如果工具把呼叫端送進去的座標原封不動回傳，上一節那個錯誤就查不出來。**

放置工具的回傳必須包含：

| 欄位 | 意義 |
|---|---|
| 實際落點 | 從元件的 `LocationPoint` 讀回 |
| **落點來源標示** | `LocationPoint`（真的讀到）／`RequestedFallback`（讀不到，這是你自己給的值） |
| **BoundingBox 中心** | 從元件的 `get_BoundingBox(null)` 中心讀回，與 `LocationPoint` 是**獨立的第二訊號**，兩者互不補值 |

**沒有來源標示，呼叫端無法區分「Revit 沒有調整位置」與「我根本讀不到位置」。**

Revit 放置時可能對點做吸附或調整。能偵測到吸附，前提是拿到的是實測值；而拿到的是不是實測值，只有工具自己知道——**所以它必須說**。

### `LocationPoint` 轉型成功不等於那個點有意義

2026-08-14 實測：`place_family_instances` 放置 `Exhauster with Cabinet` 族群後，回傳的 `LocationPoint`（即 `PlacedPoint`）為 `(0, 0, 0)`，而 `BoundingBox` 證實元件位置正確；同批放置的 AHU 則回報正確——**族群相依**，不是每個族群都會發生。`PlacedPointSource` 當時仍如實回報 `"LocationPoint"`（因為 `Location as LocationPoint` 確實非 null），代表「轉型成功」與「那個點有意義」是兩件事。若只看來源標示不看數值本身，會誤判為放置失敗而刪掉實際放對的元件——當時因此差點誤刪三台放對的機器。

本專案的修法：① 放置後、讀取 `LocationPoint`（含旋轉軸）之前，補一次 `Regenerate()`；② 新增 `PlacedBBoxCenter` 作為與 `PlacedPoint` 完全獨立的第二訊號，任一個讀不到就回 `null`，不互相補值。`LocationPoint` 因族群相依而失真時，用 `PlacedBBoxCenter` 交叉核對。

**注意**：`PlacedBBoxCenter` 是元件整體外接盒的中心，對非對稱族群本來就不等於插入點座標，且依 Revit API 文件（`Element.BoundingBox(View)` remarks）「this bounding box volume may enclose geometry that is not obvious ... the flip controls ... will be included」，該盒會納入未必可見的幾何（如 flip controls）。交叉核對的用途是判斷落點量級是否合理（例如 `PlacedPoint` 回 (0,0,0) 而 BBox 中心落在預期位置附近），**不是要求兩者逐位相等**——把正常落差誤判為放置異常，正是本節開頭警告的「誤判為放置失敗而刪掉放對的元件」風險方向。

**2026-08-15 實測**：同一次 z 語意驗證中，`PlacedBBoxCenter.Z` 回 `33530.8`，比 `PlacedPoint.Z`（33480）高 **50.8 mm**——正好是 2 英吋，符合該風口本體向上延伸的幾何。**這個帶英制尾數的落差本身就是「該欄位讀的是真實幾何」的旁證**：若它是用 `PlacedPoint` 頂替（互相補值），兩者會逐位相等；若它是回聲，也不會出現族群幾何特有的量值。**兩個訊號的價值來自它們不相等。**

> 這與第一節「代表點來源必須標示」是同一條原則：**不確定性要顯性回報，不能用一個看起來正常的值蓋過去。**

---

## 五、批次放置的實作要求

1. **單一 Transaction 包住全部**，不要每件開一次交易。
2. 用會吞 warning 的交易輔助（本專案為 `TransactionHelper.Begin`）——MEP 族群放置常觸發 warning dialog。
3. **部分失敗不中止整批**：逐件 try/catch，失敗記進該件的錯誤欄位，繼續下一件，最後回報成功／失敗計數。
4. **元件已建立但後續步驟失敗時，仍要回傳其 ElementId** 並註明。否則交易提交後會留下追蹤不到的孤兒元件。
5. **用型別 ID 而非名稱比對**。批次規模下，名稱比對的重名／大小寫／全形半形問題**只會讓其中一件報錯、其餘靜靜放錯型號**。
6. **`doc.Regenerate()` 不要放在「逐件 × 逐參數」的雙層迴圈內**。每件在「放置後」與「設完該件全部參數後」這兩個時點各至多一次、上限共兩次即可（前者修正未重生 `LocationPoint` 的讀值問題，見上一節；後者為一次性讀回，不逐參數各自 Regenerate）；另外每個尚未啟用的族群類型（`FamilySymbol`）在批次中第一次用到時會多觸發一次 `Activate()` 後的 Regenerate，但只發生在該 symbol 第一次出現時，不隨 placement 筆數累加。呼叫端有逾時上限時，超時但交易已提交會呈現「工具說失敗、模型裡卻有東西」的最壞狀態——重試就會變成兩倍，2026-08-14 曾因雙層迴圈內的 Regenerate 撞破 MCP-Server 30 秒 timeout 而實際發生過。

### 批量估算

先放 1 件量測往返時間 `t`，再取 `batchSize = floor(逾時上限 × 0.6 ÷ t)`，並設一個上限（例如 20）。

**不要憑估算決定批量。** 交易提交本身還會觸發一次完整 regeneration，成本不在逐件耗時裡。

---

## 六、參數寫入：內部單位與雙欄讀回

放置後設定的實例參數走 Revit **內部單位**（見 `tool-capability-boundary.md` 的 lesson L16）。

**工具不應在內部做任何單位換算或猜測**，而應設定完成後讀回**兩個互不補值的欄位**：

| 欄位 | 內容 |
|---|---|
| 顯示值 | `Parameter.AsValueString()`——Revit 依專案顯示單位格式化 |
| 原始值 | 依 `StorageType` 取得的內部單位值 |

**兩者都可能為 null，null 就照實回 null，不可互相補值。**

理由：只看其中一個，分不出「數字寫錯」與「單位換算錯」——**而這兩種錯誤的處置完全不同**（前者改輸入，後者改換算係數）。

實測範例（風量）：

```
Requested          0.875016     ← 內部單位 ft³/s
WrittenBackDisplay "89.2"       ← 顯示 m³/h
換算係數           101.9405     （1 ft³/s = 101.9405 m³/h）
```

---

## 七、🔴 放置後的驗證是方法的一部分，不是可選項

**「放進去了」不等於「被歸屬到正確的空間」。**

歸屬確認的判準：**該空間的「實際值」欄位出現非零值**。

- 風口 → `Actual Supply Airflow` / `Actual Exhaust Airflow`
- 其他用途 → 各自對應的彙總欄位

### 要驗對欄位

同一品類底下，不同族群餵養不同欄位。M1_02 實測：

| 族群 | 餵養的欄位 |
|---|---|
| `Supply Diffuser` | `Actual Supply Airflow` |
| `Exhaust Grill` | `Actual Exhaust Airflow` |
| `Return Diffuser` | **`Actual Return Airflow`**——不是 Exhaust |

**回風與排風是兩件事。** 選錯族群時，元件數量一件不缺、外觀正常，但目標欄位維持 0。

### 這個驗證證明什麼、不證明什麼

若「實際值」是從與「目標值」同一份資料寫入的，**兩者相等是必然的，不構成對數值本身的驗算**。

它**確實**證明三件事：

1. **歸屬正確**——元件被算進它該屬於的空間
2. **單位換算正確**——內部值與顯示值對得上
3. **沒有遺漏、重複或誤放**

它**不**證明：這些數值本身對不對。那要靠上游的判定依據（見 `mep-space-demand-matrix.md` 5-4）。

**把這兩件事講清楚，比讓表格全綠重要。**

---

## 八、刻意不放的那幾間，要看得出來

依判定應暫緩的空間（例如判定依據尚未查證者），**不要為了讓表格好看而先放進去**。

正確的狀態是：**該空間的「目標值」有值而「實際值」為 0**——它會在明細表上單獨跳出來。

> 若先放下去、之後才發現要改，它會混在一批已驗證通過的元件裡，**沒有人記得那一間是特例**。

---

## 九、誠實邊界

- 本方法的實測基礎是**單一演練模型**（Autodesk MEP 課程模型 M1_02 工作副本），非跨案驗證通則。
- 第三節記載的座標語意錯誤（z 為相對樓層偏移）是**當時的實測結果**；2026-08-15 已將 `place_family_instances` 的 `z` 改為世界座標並統一兩支工具的座標系，**並已於同日部署新 DLL、重啟 Revit 後實測確認**（見第三節實測紀錄）。但該次實測**只涵蓋一個非宿主風口族群、一個樓層、一筆放置**，不是跨族群或跨專案的通則；也不保證你當下連到的 add-in 已是新版（判別訊號見第三節）。
- 第三節記載的「相對樓層偏移」語意本身也只在單一模型的特定族群上驗證過，**可能是族群相依而非該 API 方法的固定性質**（見第三節「族群相依提醒」段）；若換一批未實測過的族群，偏移方向或有無偏移都可能不同，不要照抄套用。
- `place_family_instances` 在 `levelName` 省略時的自動選層邏輯（取 `ProjectElevation` 最接近 z 且不高於 z 的樓層）連同其比較基準改用 `ProjectElevation` 一併，至今**未被本次演練觸發、也未經 Revit 實測**（僅靜態驗證）——2026-08-15 的 z 語意實測**刻意明確指定了 `levelName`**，因此沒有走到這條分支，它仍是本檔唯一未經實測的路徑之一。需要確定樓層歸屬時仍建議明確指定 `levelName`。
- 第四節新增的 `PlacedBBoxCenter`（BoundingBox 中心讀回）**已於 2026-08-15 實測確認會回傳真實幾何**（見第四節實測段：與 `PlacedPoint` 差 50.8 mm，非補值亦非回聲）。但**尚未在「`LocationPoint` 實際失真」的情境下驗證過它的救援效果**——2026-08-14 觀察到 `Exhauster with Cabinet` 回 `(0,0,0)` 時，`PlacedBBoxCenter` 這個欄位還不存在。該情境的交叉核對價值仍屬推論。
- 第七節的族群對欄位對照表僅涵蓋實測過的三個族群，其餘族群需自行驗證。
- 網格取樣的階段在本次演練中**未被觸發**（89 個 Space 全部在第一階段命中 `LocationPoint`）。該分支的實效**尚未經實測驗證**。
