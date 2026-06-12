---
name: sleeve-classification-protocol
description: "MEP 套管身分識別協議。定義如何透過幾何特徵、碰撞關係與參數比對，精確區分穿梁、穿牆與穿板套管。"
metadata:
  version: "1.0"
  updated: "2026-05-14"
  created: "2026-05-14"
  contributors: ["Antigravity"]
---

# MEP 套管身分識別協議 (Sleeve Classification Protocol)

## 1. 識別維度

系統應依據以下三個維度進行權重評分，以判定套管類型：

### 1.1 放置方向 (Orientation)
*   **水平方向 (Horizontal)**：套管中心線向量 $Z \approx 0$。可能是**穿梁**或**穿牆**。
*   **垂直方向 (Vertical)**：套管中心線向量 $Z \approx 1$ 或 $-1$。極高機率為**穿樓板**。

### 1.2 碰撞關係 (Intersection)
透過 `detect_clashes` 或 `get_element_geometry` 偵測交集：
*   **優先權 1 (樑)**：若與 `StructuralFraming` 交集，且長度匹配，初判為穿梁。
*   **優先權 2 (牆)**：若與 `Walls` 交集，且長度匹配，初判為穿牆。
*   **優先權 3 (板)**：若與 `Floors` 交集，且長度匹配，初判為穿板。

### 1.3 長度與主體匹配 (Dimension Match)
*   套管長度 $L_{sleeve}$ 應與主體厚度 $T_{host}$ 進行 $\pm 10\text{mm}$ 比對。
*   若 $L_{sleeve} \approx$ 牆厚 $\rightarrow$ 判定為穿牆。
*   若 $L_{sleeve} \approx$ 梁寬 $\rightarrow$ 判定為穿梁。

---

## 2. 判定邏輯 (Classification Matrix)

| 套管類型 | 方向 | 碰撞對象 (優先級) | 長度特徵 |
| :--- | :--- | :--- | :--- |
| **穿梁套管** | 水平 | `StructuralFraming` | $L \approx$ 梁寬 |
| **穿牆套管** | 水平 | `Walls` | $L \approx$ 牆厚 |
| **穿板套管** | 垂直 | `Floors` | $L \approx$ 板厚 |

### 2.1 幾何與屬性優先級 (Priority Logic)

*   **最高優先級：關鍵字判定 (Keyword Priority)**：
    系統應優先掃描套管之 `開口類型`、`備註` 或 `描述` 參數。
    *   **判定為排除**：包含 `W.O.`、`Wall`、`穿牆`、`F.O.`、`Floor`、`穿板`、`穿地板` 等關鍵字。
    *   **判定為保留**：包含 `B.O.`、`Beam`、`穿梁` 等關鍵字。
*   **第二優先級：長度匹配原則 (Length-Match Priority)**：
    若無明確關鍵字，則比對「套管長度 (L)」與相交之「結構梁寬度 (B)」。
    *   **判定基準**：若 $|L - B| \le 10\text{mm}$，則認定為「穿梁套管」。
*   **次要判定：排除流程**：
    若長度不匹配，則依序執行以下排除：
    1.  **垂直方向排除**：凡方向為垂直者，判定為穿板套管並排除。
    2.  **牆/板碰撞排除**：與牆/板相交且長度不符梁寬者，判定為穿牆/穿板套管並排除。

### 2.2 多重防禦機制 (Defense-in-Depth)
當單一判斷條件缺失時（如元件無宿主），系統必須依序執行以下防線進行判定：

1.  **第一防線：宿主屬性 (Host Check)**：
    *   若套管具備 `Host` 參數且類別為 `Walls` 或 `Floors`，則**直接判定為排除項**。
2.  **第二防線：實體幾何碰撞 (Solid Intersection)**：
    *   **若無宿主**：系統必須取得套管的 `Solid` 實體，與所有連結模型（STR/ARC）的 `Walls` 與 `Floors` 進行幾何交集檢查。凡有物理重疊，則判定為排除項。
3.  **第三防線：維度精確匹配 (Dimension Matching)**：
    *   比對「套管物理長度」與「相交梁寬度」。若誤差 $> 10\text{mm}$，則該套管不具備穿梁身分（判定為 `IsExcluded = true`）。
4.  **第四防線：方向性否決 (Orientation Override)**：
    *   偵測套管主軸向量。凡 Z 向量 $> 0.8$（垂直放置），一律判定為穿板套管。

### 2.3 異常與排除項警告機制 (Exclusion & Warning Mechanism)

當套管與結構梁發生碰撞，但觸發了防禦機制的排除條件時，系統應採取以下處理流程：
*   **套管與牆/樓板相交但未穿梁**：
    *   處理：判定為純穿牆或穿板套管，**直接排除**，不回傳亦不在圖面上標註。
*   **套管同時與梁及牆/樓板相交（梁側貼牆/貼板）**：
    *   處理：**不可在此階段直接排除**。系統必須將套管交由「第二層幾何篩選」進行長度比對。
        *   若套管長度與梁寬匹配（誤差 $\le 10\text{mm}$），判定為**穿梁套管**，予以保留並正常進行穿梁法規檢核。
        *   若套管長度與梁寬不匹配（誤差 $> 10\text{mm}$），則判定為排除項，標記為 `EXCLUDED_WARNING`，將其從 Revit 平面圖的圖面標籤標註中**剔除**，但必須在**明細表與文字報告中保留並顯示 `⚠️ EXCLUDED_WARNING`**，並註明原因（如「套管長度與梁寬不匹配，疑似穿牆套管」）以提醒使用者。

---

## 3. 執行技術指南 (Technical Implementation)

### 3.1 幾何檢測與標記簡化要求 (Integrity & Annotation Requirements)
1.  **Solid 碰撞強制化**：針對點位族群套管，**嚴禁**僅使用中心線 (Curve) 偵測。必須調用 `get_element_geometry` 獲取套管實體 Solid。
2.  **跨模型遍歷**：碰撞偵測必須包含所有 `RevitLinkInstance` 的 `Walls` 與 `Floors` 實體，確保不遺漏任何連結模型中的主體。
3.  **零容忍過濾**：在「穿梁檢核」模式下，凡是通過以上四道防線後仍存在「穿牆」或「穿板」嫌疑的套管，必須標記為 `UNIDENTIFIED` 並交由人工複核，不得直接標記為 PASS。
4.  **平面圖標記簡化原則**：
    *   在平面視圖中，檢核標記文字僅顯示 `● PASS` 或 `● FAIL`，**不顯示**詳細違規原因（詳細原因僅呈現在明細表與報告中）。
    *   標記樣式控制：`● PASS` 的 TextNote 文字必須變更為 **綠色**，`● FAIL` 則保持 **紅色**。
    *   排除項（`IsExcluded`）不在此視圖上產生任何 TextNote 標籤。

### 3.3 物理幾何權重機制 (Geometric Weighting)
當參數缺失或不可信時，系統必須启用物理幾何判定：
1.  **穿透長度比 (Penetration Ratio Check)**：若套管同時與多個品類碰撞，計算套管在各品類內的「穿透段長度」。
    *   **判定**：判定「穿透長度」與「主體厚度」匹配度最高者為其身分。
2.  **向量對齊校驗 (Vector Alignment)**：
    *   **穿梁**：套管長軸應與梁中心線接近垂直。
    *   **穿板**：套管長軸應與 Z 軸接近平行。
3.  **實體測量 (Direct Solid Measurement)**：系統必須調用 `get_element_geometry` 直接測量套管物理長度 $L_{actual}$，而非僅依賴欄位數值。

---

## 4. 通用化執行策略 (Universal Execution Strategy)

1.  **幾何先行**：AI 執行檢核前，優先以物理碰撞與維度比對進行分類。
2.  **參數校驗**：將讀取到的參數（如 `開口類型`）與幾何結果比對。
    *   若兩者一致 $\rightarrow$ 高信心度執行。
    *   若兩者矛盾 $\rightarrow$ 以**幾何實體結果**為準，並標記 `Metadata Mismatch` 警告。
3.  **自動適應**：系統不預設任何參數名稱，應透過語義搜尋（Semantic Search）動態鎖定當前專案的長度欄位。
