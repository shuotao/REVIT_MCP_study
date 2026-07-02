---
name: beam-penetration-sc
description: "SC 鋼梁專屬穿孔（腹板開孔）檢核原則：腹板中心開孔、翼板保護、孔徑與端部/孔間距限制。引用 beam-penetration-base。草稿階段，部分數值待確認。"
metadata:
  version: "1.1"
  updated: "2026-07-02"
  created: "2026-05-05"
  contributors: ["User", "Antigravity", "SEven777-a"]
  references: ["beam-penetration-base"]
  related:
    - beam-penetration-base.md
    - beam-penetration-rc.md
    - beam-penetration-src.md
    - sleeve-classification-protocol.md
  tags: [beam, penetration, SC, 鋼梁, 腹板, web-opening, 穿梁]
---

# SC 鋼梁穿孔規則 (Steel Beam Web Opening)

> [!WARNING]
> 草稿待江老師確認（draft — 2026-07-02 收編自 rc 分支行為推導）。
> 本檔在 PR #68 收編輪（wave-2, `feature/seven777-beam-penetration-v2`）由既有 stub 草綱、
> `beam-penetration-base`/`-rc` 結構、與 rc `AdvancedPenetration.cs`（跨模型套管掃描行為）推導補寫。
> 凡「待補」列表示 stub 點名該規則但目前無來源給定數值，**不臆造**；請江老師依鋼構規範核定後填入。

本檔定義 **SC 鋼梁（H / I 型斷面）** 的穿孔檢核原則，為 `beam-penetration-base` 的子項。
元素識別、套管身分判定、樓層一致性與輸出格式一律沿用 `beam-penetration-base`。

## 1. 適用範圍與元素識別
*   **適用梁型**：H 型或 I 型鋼梁（依 `beam-penetration-base` §1.1 之 SC 判定）。
*   **套管識別與跨模型掃描**：沿用 base §1.3–1.4。套管可能位於主模型或連結（MEP/CSA）模型；
    掃描時同時蒐集主模型與各 `RevitLinkInstance` 內的套管，並以世界座標交集判定貫穿（對應 rc `AdvancedAnalyzeAndTag` 之跨模型行為）。
*   **穿牆/穿板排除**：長度短於梁寬（誤差 > 10 mm）或實質貼牆/貼板者，自穿梁清單排除（base §1.4）。

## 2. 腹板開孔幾何限制 (Web Opening Geometry)
*   **開孔位置**：僅限腹板 (Web) 中心區域；**嚴禁切斷或碰觸翼板 (Flange)**。
*   **孔徑上限**：$D \le 0.6 \cdot d_w$（$d_w$ 為腹板淨高）。
*   **孔形**：圓孔為原則；矩形／長圓孔之角隅需補強評估（見 §4）。

## 3. 端部避讓與孔間距 (End Clearance & Spacing)
*   **端部避讓**：孔中心距梁端接頭 $\ge 1.0 \cdot H$（$H$ 為梁深）。
*   **孔中心距**：相鄰孔中心距 $\ge 3 \cdot D$。

> 註：§2–§3 之 $0.6\,d_w$、$1.0H$、$3D$ 沿用既有 stub 所載數值，惟 stub 未附法規出處，
> 併入前請江老師對照鋼構設計規範（如 AISC Design Guide 2 / 內政部鋼構造建築物鋼結構設計技術規範）確認或修訂。

## 4. 補強要求 (Reinforcement) — 待補
*   **孔緣補強板 (Stiffener/Doubler)**：觸發門檻與補強尺寸 —— 待補（無來源，勿臆造）。
*   **矩形孔角隅補強**：需求與細節 —— 待補。
*   **集中載重/接頭鄰近區加嚴**：加嚴係數 —— 待補。

## 5. 檢核輸出
*   依 `beam-penetration-base` §3 輸出 `PASS` / `FAIL` / `WARNING` / `SPECIAL_CHECK`，
    並具體指出違反條文與數值對比（如：孔徑 $D=0.7\,d_w > 0.6\,d_w$ → FAIL）。
*   位於 SRC 重疊區之鋼梁段，改依 `beam-penetration-src` 之避讓原則處理。
