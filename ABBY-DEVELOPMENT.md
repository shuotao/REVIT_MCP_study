# Abby 工具開發與共用

Abby以原版`shuotao/REVIT_MCP_study`為基礎，擴充工具與既有命令模式。

- 中央庫：`s7814322-ops/REVIT_MCP_study`。
- 共用分支：**my-revit-tool**；原版main不作為Abby推送或部署目標。
- 上游整合基準：`93365e7ba5c2a7fd71446894aa3754e7d6eb5efb`。
- 基準工具253個：原版239、自訂12、保留舊版入口2；部分擴充是原工具的新模式，不另計工具。
- 支援2024／2025／2026。模型操作仍須依命令提供預覽／套用旗標與讀回驗證，不能把編譯通過等同實際模型測試。
- 原版更新只在my-revit-tool整合、測試一次，再由專屬Release供兩台接收。

入口：`Abby-Clone.cmd`、`Abby-SyncSource.cmd`、`Abby-Publish.cmd`、`Abby-Update.cmd`。

完整安裝與雙機開發方式見[雙機設定](docs/ABBY-TWO-PC-SETUP.md)。共用skill使用`.claude/skills`與domain；本機`.agents`、`.codex`、模型與資料匯出不提交。原版來源更新不可直接覆寫自訂實作，需保留參數群組精確匹配、CAD建模、接合優先序、族群生成及批次處理等擴充。

公開前執行`node scripts/abby/check-public-source.cjs`，並審查Git差異；不自動加入未知檔案。Release只由`.github/workflows/abby-release.yml`從my-revit-tool建立，頻道前綴`abby-my-revit-tool-`，不改庫的通用latest標籤。
