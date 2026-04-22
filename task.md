# 📜 [Quest Log] 轉職任務：大機電自動化領主之路

**任務等級：** Level 20+ (Advanced BIM Automation)  
**任務描述：** 為了變身為究極機電自動化專家，您必須在本地基地（Hard Drive）中部署完整的「大神武器庫」。本任務將導引您將全球最頂尖的機電開發資源一次到位，並將它們與您的 `MCP-Tools` 進行連結。

---

## 🛠️ 第一階段：開路先鋒 (基地目錄規劃)

在開始召喚大神之前，請先在您的電腦中規劃出專屬的「軍械庫」資料夾，避免裝備散落一地：

```powershell
# 1. 建立並進入機電開發基地
cd "H:\"
mkdir "0_REVIT_MCP\External_Resources"
cd "0_REVIT_MCP"
```

---

## 📍 第二階段：勘查地圖 (Git 武器庫清單)

這是您必須佔領的七個戰略據點，分為「核心主機」與「全球大神擴充」：

### **1. 核心基地 (Core Base)**
*   🏰 **[知識中樞] REVIT_MCP_study**  
    *   `https://github.com/shuotao/REVIT_MCP_study.git`
*   ⚔️ **[核心武器] MCP-Tools-extension**  
    *   `https://github.com/CyberPotato0416/MCP-Tools-extension.git`

### **2. 全球大神武器庫 (Global God-tier Resources)**
*   📐 **[幾何之神] pyRevitMEP (Cyril Waechter)**：機電自動化與剖面建立的標竿。
*   📑 **[出圖之神] pyrevitplus (Gui Talarico)**：標註對齊、標籤管理的極致美學。
*   🧬 **[計算之神] OpenMEP (Chuong Mep)**：強大的機電底層庫與 Dynamo 整合。
*   🚀 **[效率之神] EF-Tools (Erik Frits)**：消滅重複勞動的 50+ 個實用工具。
*   📊 **[交付之神] guRoo (Gavin Crump)**：針對 BIM 交付與數據同步的專業方案。

---

## 🔮 第三階段：全員集合 (一鍵克隆指令)

請將以下指令複製到您的終端機（PowerShell）中，我會自動幫您把所有裝備整齊地擺放在對應的位置：

```powershell
# --- 1. 克隆核心基地 ---
git clone https://github.com/shuotao/REVIT_MCP_study.git "REVIT_MCP_study-main"
git clone https://github.com/CyberPotato0416/MCP-Tools-extension.git "pyRevit-MCP-Tools"

# --- 2. 前往外部資源區 ---
cd "External_Resources"

# --- 3. 克隆全球大神庫 ---
git clone https://github.com/CyrilWaechter/pyRevitMEP.git
git clone https://github.com/gtalarico/pyrevitplus.git
git clone https://github.com/chuongmep/OpenMEP.git
git clone https://github.com/ErikFrits/EF-Tools.git
git clone https://github.com/aussieBIMguru/guRoo.git

# --- 4. 任務完成檢閱 ---
Get-ChildItem -Path ".." -Recurse -Depth 1
```

---

## 🪄 第四階段：合體掛載 (裝備賦魔)

裝備下載後，請依照以下順序將它們掛載到您的 Revit 中，這將啟動您的 **[自動化全速運轉]** 狀態：

1.  **掛載主工具**：
    `pyrevit extend ui MCP_Tools "H:\0_REVIT_MCP\pyRevit-MCP-Tools"`
2.  **掛載參考庫**：
    開啟 Revit -> pyRevit 頁籤 -> Settings -> 在 **Custom Extension Directories** 中加入 `H:\0_REVIT_MCP\External_Resources`。

---

### **🏆 任務完成獎勵：**
- **解鎖權限**：`[大神邏輯存取權]`
- **獲得被動技能**：`[代碼復用術]` (可隨時抄襲...不，是參考大神的 `lib` 庫)
- **最終進度**：恭喜您！您的電腦現在已經變成了全球最強大的 Revit 機電自動化開發站！

---
**維護者**：CYBERPOTATO0416  
**最後更新時間**：2026-04-22 
