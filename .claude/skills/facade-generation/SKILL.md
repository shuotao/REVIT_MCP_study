---
name: facade-generation
description: "立面面板生成：透過 AI 照片分析設計立面面板，套用到 Revit 牆面。支援弧形面板、斜切凹窗、傾斜平板、圓角開口、平面面板五種幾何。觸發條件：使用者提到立面、facade、面板、弧形面板、凹窗、導角、傾斜面板、DirectShape。工具：get_curtain_wall_info、create_facade_panel、create_facade_from_analysis。"
---

# 立面面板生成

執行前請先讀取 domain/facade-generation.md 了解五種幾何類型與參數設計。

## 工作流程

### 步驟 1：分析參考圖片
使用者提供立面照片 → AI 分析面板排列規律、材質、色彩

### 步驟 2：確認目標牆面
`get_curtain_wall_info` 或 `get_wall_info` → 取得牆面尺寸與位置

### 步驟 3：設計面板參數
根據分析結果定義 panelTypes（id、寬度、高度、弧深、顏色、幾何類型）

### 步驟 4：預覽（選用）
使用網頁預覽工具 localhost:10002 微調後確認效果

### 步驟 5：套用到 Revit
`create_facade_from_analysis` 批次建立整面立面（或 `create_facade_panel` 逐片建立）
