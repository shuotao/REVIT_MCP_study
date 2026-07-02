---
name: beam-penetration-src
description: "SRC 鋼骨混凝土梁專屬穿孔檢核原則：內部鋼骨避讓、RC×鋼骨重疊區(SRC 映射)偵測、依區位切換 RC/SC 原則。引用 beam-penetration-base。草稿階段，部分數值待確認。"
metadata:
  version: "1.1"
  updated: "2026-07-02"
  created: "2026-05-05"
  contributors: ["User", "Antigravity", "SEven777-a"]
  references: ["beam-penetration-base"]
  related:
    - beam-penetration-base.md
    - beam-penetration-rc.md
    - beam-penetration-sc.md
    - sleeve-classification-protocol.md
  tags: [beam, penetration, SRC, 鋼骨混凝土, 鋼骨避讓, 穿梁]
---

# SRC 鋼骨混凝土梁穿孔規則 (Steel Reinforced Concrete)

> [!WARNING]
> 草稿待江老師確認（draft — 2026-07-02 收編自 rc 分支行為推導）。
> 本檔在 PR #68 收編輪（wave-2, `feature/seven777-beam-penetration-v2`）由既有 stub 草綱、
> `beam-penetration-base`/`-rc`/`-sc` 結構、與 rc `AdvancedPenetration.cs`（`get_src_beam_mapping` /
> `AdvancedAnalyzeAndTag` 之 RC×鋼骨重疊偵測行為）推導補寫。
> 凡「待補」列表示 stub 點名該規則但目前無來源給定數值，**不臆造**；請江老師依 SRC 規範核定後填入。
> 補充：現版 `get_src_beam_mapping` 工具僅回傳佔位結果（stub），實際重疊偵測邏輯位於 `AdvancedAnalyzeAndTag`；量化門檻尚未於程式碼定案。

本檔定義 **SRC 梁（矩形混凝土包覆 H 型鋼）** 的穿孔檢核原則，為 `beam-penetration-base` 的子項。
SRC 的關鍵在於：外層 RC 依 `beam-penetration-rc`、內部鋼骨依 `beam-penetration-sc`，並以「鋼骨避讓」為最高優先。

## 1. 適用範圍與元素識別
*   **適用梁型**：矩形混凝土斷面內含 H 型鋼骨（依 `beam-penetration-base` §1.1 之 SRC 判定）。
*   **套管識別與跨模型掃描**：沿用 base §1.3–1.4 與跨主/連結模型套管蒐集行為（對應 rc `AdvancedAnalyzeAndTag`）。

## 2. 鋼骨避讓原則 (Steel Core Avoidance) — 最高優先
*   **核心避讓**：**嚴禁碰撞內部鋼骨**（翼板與腹板實體）。
*   **人工複核帶**：套管邊緣落於鋼骨翼板 $5\ \text{cm}$ 以內者，標記 `SPECIAL_CHECK` 交人工複核。
*   **鋼骨腹板開孔**：若確認套管落於鋼骨腹板可開孔範圍，改依 `beam-penetration-sc` §2–§3 之腹板原則（$D \le 0.6\,d_w$、端部 $\ge 1.0H$、孔距 $\ge 3D$）。

## 3. RC×鋼骨重疊區偵測 (SRC Mapping)
*   **目的**：識別同一空間中「RC 梁幾何」與「鋼梁/鋼骨幾何」重疊的區域，該區須優先套用鋼骨（SC）原則，而非單純 RC 原則。
*   **偵測方法（來源：rc 跨模型行為）**：
    1. 蒐集主模型與各連結模型中的梁/鋼骨元素。
    2. 以套管世界座標 BoundingBox 轉換至各梁所屬（連結）模型本地座標，建立 `Outline` 求交集。
    3. 以參考樓層 (Reference Level) 一致性過濾誤判（base §2）。
*   **重疊判定門檻**（重疊比例 / 最小交集深度等量化值）—— 待補（程式碼未定案，勿臆造）。

## 4. 分區檢核邏輯 (Zoned Check Logic)
*   **鋼骨包覆區內**：以鋼骨避讓（§2）為準；可開孔處套 `beam-penetration-sc`。
*   **純 RC 保護層區**（鋼骨之外的混凝土）：套 `beam-penetration-rc`（禁開區/限制區/一般區、垂直淨距、相鄰套管淨距）。
*   **交界過渡帶**：距鋼骨翼板 $5\ \text{cm}$ 內 → `SPECIAL_CHECK`；其餘過渡帶之加嚴規則 —— 待補。

## 5. 檢核輸出
*   依 `beam-penetration-base` §3 輸出 `PASS` / `FAIL` / `WARNING` / `SPECIAL_CHECK`，
    並註明所屬分區（鋼骨區 / RC 區 / 過渡帶）與引用之子規則檔（`-sc` 或 `-rc`）。
