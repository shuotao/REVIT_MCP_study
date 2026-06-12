# Personal Vault Template（個人知識庫範本）

這個資料夾提供「個人 LLM Wiki」vault 的固定契約：

- `VAULT-CLAUDE.md`：複製到你的 vault 根目錄改名為 `CLAUDE.md`。
  Fixed Core 區塊**逐字複製、不得改寫**（這是跨 AI Agent、跨模型
  維持所有人 vault 一致的機制）；只有 Personal 區由你個人化。

完整概念、為什麼要做、一鍵複製的建置說明，見知識站：
`docs/BIM_MCP/reference/personal-llm-wiki.html`

升級機制：範本以 `schema_version` 版本化。你每次 `git pull` 就會拿到
最新範本，vault 的 lint 操作會比對版本並提示升級——schema 飄移會
自我修復，不需要人工盯。
