#!/usr/bin/env python3
"""
Revit 綠建材推送計畫擬訂引擎 v3 (基於 TASK-003 11大工程情境與 GreenMaterial_SharedParams.txt v5 六槽位共享參數)
========================================================================
功能：
  - 本檔案級承諾（僅限本檔，非專案級規則）：__main__ 示範區塊一律使用 GBM000000x 佔位符；
    解釋演算法行為的技術註解中仍可能引用真實證號（如 GBM0103810／GBM0103338），
    那屬於本專案 domain/*.md 允許的 SOP 型少量引用，不受此檔案級佔位符規則約束
  - 依據 /GM_import 指令解析材料 Set 的真實 licno 清單
  - 從 tabc_master_database.json 精確匹配全量材料數據
  - 自動判斷 Revit 品類 (Walls / Floors / Ceilings / Windows / Auxiliary)
  - 自動配置構造層 (Finish 1 / Substrate / Structure) 與預設厚度推判 (2mm / 15mm / 150mm)
  - 支援非幾何材料 (接著劑 / 填縫劑 / 防水膜) 寫入 Construction 群組
  - 依 Structure > Finish > Substrate > Other 優先序，將材料分配至 Mat1~Mat6 六槽位共享參數
  - write_back_to_set_manager(): 將對齊計畫回傳至 exported_material_sets.json
"""

import json
import os
import re
import datetime

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
WORKSPACE = os.path.dirname(os.path.dirname(SCRIPT_DIR))  # repo root (this file lives in tools/green-material/)
DB_PATH = os.path.join(WORKSPACE, "tabc_master_database.json")
# 由 GM_update_tabc_database.py 在每次真實抓取後寫入的抓取時間戳旁生檔，見該檔 _write_db_meta()。
DB_META_PATH = os.path.join(WORKSPACE, "tabc_master_database.meta.json")
SETS_FILE = os.path.join(WORKSPACE, "exported_material_sets.json")
PLAN_JSON = os.path.join(WORKSPACE, "Revit_Injection_Plan.json")
PLAN_REPORT = os.path.join(WORKSPACE, "docs", "green-material", "Revit_Injection_Plan_Report.md")

# 本機資料庫超過幾天視為「舊」。TABC 標章的核發與續證是月級節奏，30 天足以涵蓋一輪異動。
# 這是提醒門檻，不是硬擋門檻——過期標章的硬擋依據是每一筆標章自己的 period 結束日
# （見 _period_end_expired），與資料庫年齡無關。
STALE_THRESHOLD_DAYS = 30


def load_database():
    with open(DB_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


def database_freshness(threshold_days: int = STALE_THRESHOLD_DAYS) -> dict:
    """回報本機 tabc_master_database.json 有多舊，供 /GM_import 在擬訂計畫前讀回並提醒使用者。

    時間戳來源有兩級，回傳的 fetchedAtSource 明示採用了哪一級，不讓呼叫端把推估值當成確據：
      - "meta"：tabc_master_database.meta.json 的 fetchedAt，由 /GM_update 真實抓取後寫入，是確據。
      - "mtime"：旁生檔不存在（例如資料庫是這次改動之前產生的）時，退而用主資料庫檔案的修改時間。
        這是近似值——複製、rsync、還原備份都會讓 mtime 與真實抓取時間脫鉤。

    永遠不拋例外：資料庫不存在是全新 clone 的正常狀態（/GM_update 本身就是 bootstrap 入口），
    回 status="missing" 並附可執行的下一步，而不是讓呼叫端崩在 FileNotFoundError。
    """
    result = {
        "dbPath": DB_PATH,
        "exists": os.path.exists(DB_PATH),
        "recordCount": None,
        "fetchedAt": None,
        "fetchedAtSource": None,
        "ageDays": None,
        "thresholdDays": threshold_days,
        "stale": False,
        "status": "missing",
        "recommendation": "",
    }

    if not result["exists"]:
        result["recommendation"] = (
            "本機尚無 tabc_master_database.json。請先執行 /GM_update 從 TABC 官網建立資料庫"
            "（全新 clone 後的首次建立入口，不是錯誤狀態）。"
        )
        return result

    try:
        with open(DB_PATH, "r", encoding="utf-8") as f:
            result["recordCount"] = len(json.load(f))
    except (OSError, ValueError):
        result["recordCount"] = None

    fetched_at = None
    if os.path.exists(DB_META_PATH):
        try:
            with open(DB_META_PATH, "r", encoding="utf-8") as f:
                meta = json.load(f)
            fetched_at = datetime.datetime.fromisoformat(meta.get("fetchedAt", ""))
            result["fetchedAtSource"] = "meta"
        except (OSError, ValueError, TypeError):
            fetched_at = None

    if fetched_at is None:
        try:
            fetched_at = datetime.datetime.fromtimestamp(os.path.getmtime(DB_PATH))
            result["fetchedAtSource"] = "mtime"
        except OSError:
            fetched_at = None

    if fetched_at is None:
        result["status"] = "unknown"
        result["recommendation"] = (
            "讀不到本機資料庫的抓取時間（旁生檔與檔案 mtime 皆不可用），無法判斷資料多舊。"
            "若不確定，執行 /GM_update 重新抓取最新資料。"
        )
        return result

    age_days = (datetime.datetime.now() - fetched_at).days
    result["fetchedAt"] = fetched_at.isoformat(timespec="seconds")
    result["ageDays"] = age_days
    result["stale"] = age_days > threshold_days
    result["status"] = "stale" if result["stale"] else "fresh"
    if result["stale"]:
        result["recommendation"] = (
            f"本機資料庫已 {age_days} 天未更新（門檻 {threshold_days} 天）。建議先執行 /GM_update 再擬訂計畫；"
            "不更新仍可繼續，但計畫可能依據已失效或已異動的標章資料。"
        )
    else:
        result["recommendation"] = ""
    return result


def load_exported_sets():
    if os.path.exists(SETS_FILE):
        try:
            with open(SETS_FILE, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            pass
    return {}


def _extract_acoustic_nrc(test_items: str) -> float:
    """從 testItems 文字中擷取降噪係數 NRC 真實數值（如「降噪係數 NRC：0.75」，天花板類吸音材料的
    testItems 一律採此固定格式）。抓不到時回傳 0.0，不臆測數字——舊邏輯 `0.75 if "吸音" in sub_cat
    else 0.0` 是錯的：subCategory 實際值是「天花板類」，字面上從不包含「吸音」兩字，導致所有天花板
    材料的 AcousticNRC 永遠被硬寫成 0.0，即使 testItems 裡明明有真實數值（TASK-005.8 發現）。
    """
    if not test_items:
        return 0.0
    m = re.search(r"NRC[：:]\s*([\d.]+)", test_items)
    if not m:
        return 0.0
    try:
        return float(m.group(1))
    except ValueError:
        return 0.0


def _normalize_licno(licno: str) -> str:
    """去除尾端的 (續)/(增)/(改) 等註記後綴，僅供比對使用。
    輸出永遠採用資料庫記錄「原始、帶後綴」的 licno，此函式不用於任何輸出欄位。
    """
    if not licno:
        return licno
    return re.sub(r"[（(].*?[）)]\s*$", "", licno).strip()


# ── 牆體用途厚度矩陣（TASK-005.6）──────────────────────────────────────────
# TABC 標章資料本身無法判斷一面牆是外牆/分戶牆/輕隔間（這是專案/使用情境資訊，不是材料屬性；
# 掃描過 106 筆牆壁類材料標題，僅少數含數字，且那些數字是矽酸鈣板密度/耐燃等級代碼「0.8FK」
# 「1.0FK」，不是厚度——矽酸鈣板實際常見厚度是 6-12mm，把 0.8FK 當 0.8mm 厚度是臆測，違反
# 「異常或缺失規格不得自行臆測」的驗收條件）。因此改為：從 Q3「補充條件」自由文字讀取使用者
# 明確填寫的牆體用途關鍵字，找到才套用對應預設厚度；找不到就用最保守的通用結構牆預設值，
# 並標記 wallUsageUnspecified，供 /import 技能在確認步驟明確提示使用者覆寫。
_WALL_USAGE_ALIASES = [
    (("外牆", "外部牆", "戶外牆", "exterior"), "Exterior"),
    (("分戶牆", "戶間牆", "共同壁", "party wall"), "PartyWall"),
    (("輕隔間", "隔間牆", "室內隔間", "light partition"), "LightPartition"),
]

_WALL_STRUCTURE_THICKNESS_MATRIX = {
    "Exterior": "150 mm（外牆 RC 結構，依 Q3 補充條件解析）",
    "PartyWall": "135 mm（分戶牆 RC/磚牆結構，依 Q3 補充條件解析；如設計另有指定厚度請於確認步驟覆寫）",
    "LightPartition": "100 mm（室內輕隔間 石膏板/矽酸鈣板，依 Q3 補充條件解析；如為防火時效隔間，實際系統厚度可能更大，請於確認步驟覆寫）",
}
_WALL_STRUCTURE_THICKNESS_DEFAULT = "150 mm（結構牆，未指定用途，已套用保守預設值，請於確認步驟覆寫或於 Q3 補充條件說明外牆/分戶牆/輕隔間）"


def _extract_wall_usage_hint(user_intent: str) -> str:
    """從 /GM_import 文字（含 Q3 補充條件自由文字）掃描牆體用途關鍵字。補充條件是自由文字，
    不像品類/掛載類別有固定「標籤: 值」格式，所以直接在整段 user_intent 裡找關鍵字，
    而非嘗試解析成單一欄位。找不到回傳空字串（未指定，呼叫端套用保守預設值）。"""
    if not user_intent:
        return ""
    for keywords, canonical in _WALL_USAGE_ALIASES:
        if any(k in user_intent for k in keywords):
            return canonical
    return ""


def _wall_structure_thickness(wall_usage_hint: str) -> str:
    return _WALL_STRUCTURE_THICKNESS_MATRIX.get(wall_usage_hint, _WALL_STRUCTURE_THICKNESS_DEFAULT)


def analyze_material_mapping(sub_cat: str, title: str, wall_usage_hint: str = "") -> dict:
    """
    依據 TASK-003 的 11 大工程情境，自動推判：
      1. 目標 Revit 品類 (Category)
      2. 建議構造層 (Layer)
      3. 預設厚度 (Default Thickness)
      4. 特殊處理標籤 (IsAuxiliary / IsLoadableFamily / Pattern)

    分類優先序：subCategory 優先，title 關鍵字只用於 subCategory 是
    「綜合建材類」這種跨品類的 catch-all，或用於同一 subCategory 內部的細分（例如
    牆壁類底下要再分辨是板材 Structure 還是飾面 Finish）。
    Master DB 實測只有 7 種 subCategory：綜合建材類/塗料類/地板類/牆壁類/天花板類/隔音緩衝類/透水鋪面類——
    不要單靠 title 是否包含「矽酸鈣板」這類牆板/天花板通用建材字樣去猜品類，
    矽酸鈣板、石膏板等板材同時可用於牆壁與天花板，只有 subCategory 才可靠區分兩者
    （例如 GBM0103810「日本NICHIAS NA LUX矽酸鈣板」subCategory 是牆壁類，不是天花板類）。
    """
    sub_cat = sub_cat or ""
    title = title or ""

    # === 第一階段：subCategory 明確標示的 5 個實體品類，優先分派 ===
    # 這一階段刻意跑在關鍵字判斷之前，因為關鍵字非常容易誤判：
    # 「膠」會誤中「塑膠地磚」「乳膠漆」「橡膠地磚」；「天花」「矽酸鈣板」會跨牆壁/天花板誤判。
    # subCategory 是 Master DB 自己標的權威分類，永遠比從 title 猜測可靠。

    # 天花板類 -> 情境 8
    if "天花" in sub_cat:
        return {
            "revitCategory": "OST_Ceilings",
            "layer": "Finish 1 [4]",
            "defaultThickness": "12 mm (飾面板)",
            "buiNaming": "C_INT_Ceiling",
        }

    # 地板類 -> 情境 2, 9
    if "地板" in sub_cat:
        is_soundproof = "防音" in title or "隔音" in title
        return {
            "revitCategory": "OST_Floors",
            "layer": "Substrate [2]" if is_soundproof else "Finish 1 [4]",
            "defaultThickness": "10 mm (防音墊)" if is_soundproof else "15 mm (飾面地磚) + 20mm 打底",
            "surfacePattern": "600x600 Grid Pattern / Wood Grain",
            "buiNaming": "F_INT_FloorTile",
        }

    # 隔音緩衝類 -> 通常鋪在地板下的緩衝墊
    if "隔音緩衝" in sub_cat:
        return {
            "revitCategory": "OST_Floors",
            "layer": "Substrate [2]",
            "defaultThickness": "10 mm (防音墊)",
            "buiNaming": "F_INT_FloorTile",
        }

    # 塗料類 -> 情境 1
    if "塗料" in sub_cat:
        return {
            "revitCategory": "OST_Walls",
            "layer": "Finish 1 [4]",
            "defaultThickness": "2 mm (薄塗層)",
            "buiNaming": "W_INT_Paint",
        }

    # 牆壁類 -> 情境 1, 6（板材 Structure vs 飾面 Finish，subCategory 已確定是牆面材料，
    # 只需再用關鍵字判斷是板材本體還是飾面）
    if "牆壁" in sub_cat:
        is_structure_material = any(
            k in title
            for k in ["磚", "RC", "石膏板", "矽酸鈣板", "纖維水泥板", "木心板", "合板", "隔間板", "水泥板"]
        )
        return {
            "revitCategory": "OST_Walls",
            "layer": "Structure [1]" if is_structure_material else "Finish 1 [4]",
            "defaultThickness": (
                _wall_structure_thickness(wall_usage_hint) if is_structure_material else "120 mm (輕隔間)"
            ),
            "buiNaming": "W_INT_RC15",
            "wallUsageHint": wall_usage_hint or None,
            "wallUsageUnspecified": is_structure_material and not wall_usage_hint,
        }

    # 透水鋪面類 -> 場地鋪面，非 Wall/Floor/Ceiling 標準構件，交由人工判斷對應方式
    if "透水鋪面" in sub_cat:
        return {
            "revitCategory": "OST_Materials",
            "layer": "Unclassified - Manual Review Required",
            "defaultThickness": "N/A",
            "buiNaming": "UNCLASSIFIED_Pavement",
            "needsManualReview": True,
        }

    # === 第二階段：subCategory 是「綜合建材類」（或未知值）的 catch-all，
    # 才使用關鍵字做進一步判斷 ===

    # 非幾何輔助材料 (填縫劑 / 接著劑 / 矽利康 / 防水膜 / 環氧樹脂) -> 情境 4, 5
    # 注意：不可用單字「膠」當關鍵字，會誤中「塑膠地磚」「乳膠漆」等完全無關的詞。
    if any(k in title for k in ["接著劑", "黏著劑", "填縫", "矽利康", "防水", "環氧樹脂"]):
        aux_type = "GreenMaterial_Adhesive"
        if "填縫" in title or "矽利康" in title:
            aux_type = "GreenMaterial_Sealant"
        elif "防水" in title:
            aux_type = "GreenMaterial_Waterproofing"

        return {
            "revitCategory": "OST_Materials",
            "layer": "Attached Parameter (Construction)",
            "defaultThickness": "0 mm (非幾何屬性)",
            "isAuxiliary": True,
            "auxiliaryParam": aux_type,
            "buiNaming": "AUX_Adhesive_Sealant",
        }

    # 門窗/幕牆/玻璃類 -> 情境 7 (載入家族 .rfa 方法 7.1)
    if any(k in title for k in ["門", "窗", "玻璃", "帷幕"]):
        return {
            "revitCategory": "OST_Windows",
            "layer": "Family Type Parameters (.rfa)",
            "defaultThickness": "依原 Family 規範",
            "isLoadableFamily": True,
            "familyBackupSOP": "另存既有 .rfa 家族檔案並注入 Type 參數",
            "buiNaming": "WIN_GBM_Family",
        }

    # 板材類關鍵字（石膏板/矽酸鈣板/纖維水泥板/木心板/合板/隔間板/磚/RC 等），
    # 落在「綜合建材類」裡但看得出是牆面板材的，當作牆面 Structure 材料
    if any(
        k in title
        for k in ["磚", "RC", "石膏板", "矽酸鈣板", "纖維水泥板", "木心板", "合板", "隔間板", "水泥板"]
    ):
        return {
            "revitCategory": "OST_Walls",
            "layer": "Structure [1]",
            "defaultThickness": _wall_structure_thickness(wall_usage_hint),
            "buiNaming": "W_INT_RC15",
            "wallUsageHint": wall_usage_hint or None,
            "wallUsageUnspecified": not wall_usage_hint,
        }

    # === 第三階段：真的判斷不出來，誠實回報需要人工判斷，不要硬猜品類 ===
    # 舊版邏輯在這裡會不分青紅皂白直接回傳 OST_Walls，導致「綠混凝土G類」這種
    # 泛用建材（可能用在牆、地板、基礎）被誤判成牆面材料。寧可標記為未分類。
    return {
        "revitCategory": "OST_Materials",
        "layer": "Unclassified - Manual Review Required",
        "defaultThickness": "N/A",
        "buiNaming": "UNCLASSIFIED",
        "needsManualReview": True,
    }


# 當材料本身跨用途（如混凝土可用於 Wall 也可用於 Floor），analyze_material_mapping
# 會誠實回報 needsManualReview，此時改用 Set 自己宣告的「品類」解析（Set 的使用情境
# 比材料自身資料更有資格決定它這次要用在哪）。層位選保守預設（Substrate/核心層，
# 而非直接假設是外露飾面），仍標記為 Set 層級覆寫，供人工複核。
_CATEGORY_HINT_FALLBACK = {
    "Wall": {"revitCategory": "OST_Walls", "layer": "Structure [1]", "defaultThickness": "150 mm (結構層，經 Set 品類覆寫，建議人工確認)", "buiNaming": "W_INT_RC15"},
    "Floor": {"revitCategory": "OST_Floors", "layer": "Substrate [2]", "defaultThickness": "依實際配比厚度 (經 Set 品類覆寫，建議人工確認)", "buiNaming": "F_INT_FloorTile"},
    "Ceiling": {"revitCategory": "OST_Ceilings", "layer": "Finish 1 [4]", "defaultThickness": "12 mm (經 Set 品類覆寫，建議人工確認)", "buiNaming": "C_INT_Ceiling"},
    # 柱/樑不是 CompoundStructure 分層構件，是 FamilySymbol 的單一材質參數
    # (STRUCTURAL_MATERIAL_PARAM)，assign_existing_material / duplicate 邏輯跟 Wall/Floor/Ceiling 不同。
    "Column": {"revitCategory": "OST_Columns", "layer": "Structural Material Parameter (單一材質參數，非構造層)", "defaultThickness": "N/A (依柱斷面尺寸)", "buiNaming": "COL_Structural"},
    "Beam": {"revitCategory": "OST_StructuralFraming", "layer": "Structural Material Parameter (單一材質參數，非構造層)", "defaultThickness": "N/A (依梁斷面尺寸)", "buiNaming": "BEAM_Structural"},
}

# 中文別名 -> 上面表格的 key（Set 的「品類」欄位可能填中文或英文）
_CATEGORY_HINT_ALIASES = {
    "牆": "Wall", "牆壁": "Wall", "Wall": "Wall",
    "地板": "Floor", "樓板": "Floor", "Floor": "Floor",
    "天花板": "Ceiling", "天花": "Ceiling", "Ceiling": "Ceiling",
    "柱": "Column", "Column": "Column",
    "梁": "Beam", "樑": "Beam", "Beam": "Beam",
}


def _extract_category_hint(user_intent: str) -> str:
    """從 /GM_import 文字裡的 [需求對齊：...品類: Floor...] 擷取 Set 宣告的品類，用於解析 needsManualReview 的材料。"""
    if not user_intent:
        return ""
    m = re.search(r"品類[:：]\s*([A-Za-z一-鿿]+)", user_intent)
    if not m:
        return ""
    raw = m.group(1).strip()
    return _CATEGORY_HINT_ALIASES.get(raw, raw)


def _extract_attach_category_hint(user_intent: str) -> str:
    """從 /GM_import 文字裡的 [需求對齊：...掛載類別: Wall...] 擷取「純材料」Set 要依附的既有品類
    （TASK-005.5：情境 5 單選非模型綠建材）。故意用「掛載類別」而非「依附品類」當標籤字串，
    避免與 _extract_category_hint 的「品類[:：]」正則互相誤吃子字串。"""
    if not user_intent:
        return ""
    m = re.search(r"掛載類別[:：]\s*([A-Za-z一-鿿]+)", user_intent)
    if not m:
        return ""
    raw = m.group(1).strip()
    return _CATEGORY_HINT_ALIASES.get(raw, raw)


# ── layerComposition 覆寫（見 domain/GM_parameter-schema.md 「明確層級覆寫」）──
# 使用者在 green-material-showcase.html 對 Wall/Floor 單一組合 Set 明確拖曳指定每項材料的
# Structure/Substrate/Finish 角色與 Core Boundary 位置時，該設定存在 exported_material_sets.json
# 的 Set 物件 layerComposition.sequence 欄位。此欄位一旦存在，即為權威來源，優先於
# analyze_material_mapping() 的關鍵字啟發式判斷與 _CATEGORY_HINT_FALLBACK 的品類覆寫。

_LC_CATEGORY_TO_REVIT = {
    "Wall": {"revitCategory": "OST_Walls", "prefix": "W"},
    "Floor": {"revitCategory": "OST_Floors", "prefix": "F"},
    "Ceiling": {"revitCategory": "OST_Ceilings", "prefix": "C"},
}


def _find_set_entry(set_name: str, sets: dict):
    """比對邏輯與 write_back_to_set_manager 一致：完全相符優先，其次互為子字串。"""
    if set_name in sets:
        return sets[set_name]
    for key, val in sets.items():
        if set_name in key or key in set_name:
            return val
    return None


def _load_layer_composition(set_name: str):
    sets = load_exported_sets()
    entry = _find_set_entry(set_name, sets)
    if not entry:
        return None
    lc = entry.get("layerComposition")
    if not lc or not isinstance(lc.get("sequence"), list):
        return None
    return lc


def _build_layer_composition_role_map(layer_composition: dict) -> dict:
    """回傳 { licno: (role, finish_index_or_None) }。finish_index 依 sequence 出現順序
    分配 Finish 1 / Finish 2（第一個 Finish 角色的材料 = index 0 → Finish 1，以此類推）。"""
    role_map = {}
    finish_counter = 0
    for entry in layer_composition["sequence"]:
        if entry.get("type") != "material":
            continue
        role = entry.get("role")
        licno = entry.get("licno")
        if role == "Finish":
            role_map[licno] = (role, finish_counter)
            finish_counter += 1
        else:
            role_map[licno] = (role, None)
    return role_map


def _lookup_layer_composition_role(licno: str, role_map: dict):
    """先精確比對 licno，找不到時用 _normalize_licno 去除 (續)/(增) 後綴回補比對。"""
    if licno in role_map:
        return role_map[licno]
    norm = _normalize_licno(licno)
    for k, v in role_map.items():
        if _normalize_licno(k) == norm:
            return v
    return None


_LC_AUX_TYPE_TO_PARAM = {
    "Adhesive": "GreenMaterial_Adhesive",
    "Sealant": "GreenMaterial_Sealant",
    "Waterproofing": "GreenMaterial_Waterproofing",
}


def _build_layer_composition_aux_map(layer_composition: dict) -> dict:
    """回傳 { licno: auxType }，來源為使用者在檢索平台材料層級設定視窗把材料明確拖曳到
    「輔助材料」區的結果（見 assets/green-material-showcase.html 的 auxCompDraft）。"""
    aux_map = {}
    for entry in (layer_composition or {}).get("auxiliary", []) or []:
        licno = entry.get("licno")
        aux_type = entry.get("auxType")
        if licno and aux_type:
            aux_map[licno] = aux_type
    return aux_map


def _lookup_layer_composition_aux(licno: str, aux_map: dict):
    """先精確比對 licno，找不到時用 _normalize_licno 去除 (續)/(增) 後綴回補比對。"""
    if licno in aux_map:
        return aux_map[licno]
    norm = _normalize_licno(licno)
    for k, v in aux_map.items():
        if _normalize_licno(k) == norm:
            return v
    return None


def _resolve_layer_composition_auxiliary(aux_type_key: str) -> dict:
    """依使用者在材料層級設定視窗明確指定的輔助材料分類，產出與 analyze_material_mapping()
    的非幾何輔助材料分支相容的 mapping_info——即使標題沒有命中接著劑/填縫/防水關鍵字，
    使用者的明確分類一律優先（跟 _resolve_layer_composition_mapping 對幾何角色的處理方式一致）。"""
    aux_param = _LC_AUX_TYPE_TO_PARAM.get(aux_type_key, "GreenMaterial_Adhesive")
    return {
        "revitCategory": "OST_Materials",
        "layer": "Attached Parameter (Construction)",
        "defaultThickness": "0 mm (非幾何屬性)",
        "isAuxiliary": True,
        "auxiliaryParam": aux_param,
        "buiNaming": "AUX_Adhesive_Sealant",
        "resolvedByLayerComposition": True,
        "layerCompositionAuxType": aux_type_key,
    }


def _resolve_layer_composition_mapping(role: str, finish_index, category: str, wall_usage_hint: str = "") -> dict:
    """依 layerComposition 指定的角色，產出與 analyze_material_mapping() 相容的 mapping_info。"""
    info = _LC_CATEGORY_TO_REVIT.get(category, {"revitCategory": "OST_Materials", "prefix": "X"})
    prefix = info["prefix"]

    if role == "Structure":
        layer = "Structure [1]"
        # TASK-005.6：Wall 的 Structure 層厚度依牆體用途矩陣解析；Floor/Ceiling 維持原本的
        # 通用結構層預設（沒有外牆/分戶牆/輕隔間這種區分，樓板厚度矩陣已由情境 2/9 涵蓋）。
        thickness = (
            _wall_structure_thickness(wall_usage_hint) + "（依材料層級設定指定）"
            if category == "Wall"
            else "150 mm（結構層，依材料層級設定指定，建議人工確認實際配比厚度）"
        )
        bui = f"{prefix}_INT_Structure"
    elif role == "Substrate":
        layer = "Substrate [2]"
        thickness = "15 mm（底材層，依材料層級設定指定，建議人工確認）"
        bui = f"{prefix}_INT_Substrate"
    elif role == "Finish":
        is_first = not finish_index
        layer = "Finish 1 [4]" if is_first else "Finish 2 [5]"
        thickness = "2 mm（面材層，依材料層級設定指定，建議人工確認）"
        bui = f"{prefix}_INT_Finish{1 if is_first else 2}"
    else:
        return None

    result = {
        "revitCategory": info["revitCategory"],
        "layer": layer,
        "defaultThickness": thickness,
        "buiNaming": bui,
        "resolvedByLayerComposition": True,
        "layerCompositionRole": role,
    }
    if role == "Structure" and category == "Wall":
        result["wallUsageHint"] = wall_usage_hint or None
        result["wallUsageUnspecified"] = not wall_usage_hint
    return result


def _layer_composition_sequence_labels(layer_composition: dict, database) -> list:
    """把 sequence 轉成人類可讀的順序清單，供 Markdown 報告與回報摘要使用。"""
    title_by_licno = {item.get("licno"): item.get("title") for item in database}
    labels = []
    for entry in layer_composition["sequence"]:
        if entry.get("type") == "boundary":
            labels.append("— Core Boundary 分界線（不填入實際材料）—")
        else:
            licno = entry.get("licno")
            title = title_by_licno.get(licno, "")
            labels.append(f"{licno}｜{title}（{entry.get('role')}）")
    return labels


# ── Mat1~Mat6 六槽位分配（見 domain/GM_parameter-schema.md「六槽位分配規則」）──
# GreenMaterial_SharedParams.txt 的 v5 schema 定義了 6 個材料槽位（64 個欄位），但 Scenario 3
# 的單一組合仍可能有 7 個以上材料，超過槽位上限。過去「材料數超過槽位數時選誰進槽位」的判斷，
# 是由執行 /GM_inject revit 的 AI Agent 在對話當下臨場決定，同一個 Set 換一次對話可能得到不同結果。
# 這裡把該規則寫成確定性函式：優先序固定為 Structure > Finish > Substrate > 其他，
# 同優先序內依材料在 plan_items（已依 layerComposition 或 DB 順序排列）中的原始順序決定，
# 任何人重跑同一個 Set 永遠得到同一個分配結果。槽位數等於材料數（最多到 6 個），
# 不會不管材料數多寡就強制填滿或強制只填 3 個。

_SLOT_ROLE_PRIORITY = {"Structure": 0, "Finish": 1, "Substrate": 2, "Other": 3}
_SLOT_KEYS = ["mat1", "mat2", "mat3", "mat4", "mat5", "mat6"]


def _infer_role_bucket(mapping_details: dict) -> str:
    """從 mappingDetails 判斷材料的角色分類，優先採用 layerComposition 明確指定的角色，
    否則退回從 layer 描述字串（如 'Structure [1]'）判斷。判斷不出來歸類為 'Other'。"""
    role = (mapping_details or {}).get("layerCompositionRole")
    if role in ("Structure", "Substrate", "Finish"):
        return role
    layer = (mapping_details or {}).get("layer") or ""
    if "Structure" in layer:
        return "Structure"
    if "Substrate" in layer:
        return "Substrate"
    if "Finish" in layer:
        return "Finish"
    return "Other"


def _assign_material_slots(plan_items: list) -> dict:
    """依 Structure > Finish > Substrate > Other 的固定優先序，從 plan_items 挑出前
    len(_SLOT_KEYS)（目前 6）名依序進 Mat1~Mat6 槽位，材料數 <= 6 時全部都會分到槽位；
    超過 6 個才會有材料被標記為未分配（unassigned）。同時把 assignedSlot
    （'mat1'~'mat6'/None）直接寫回每個 plan_items 項目，供下游（Markdown 報告、
    /GM_inject revit）直接讀取，不需要重新判斷一次。

    非幾何輔助材料（接著劑/填縫劑/防水材料）一樣參與排名、可以拿到 Mat 槽位——
    Mat1~Mat6 的用途是記錄「這個元件用了哪些綠建材」的完整清單，不是只有物理
    構造層才算數；輔助材料沒有 CompoundStructure 層（見 layerComposition.auxiliary
    與 _resolve_layer_composition_auxiliary），但仍然是這個元件的一部分，一樣需要
    被 Mat 槽位記錄下來、可回查。歸類為 'Other'（最低優先序），跟其他判斷不出角色
    的材料一樣，材料數多時最先被擠到 unassigned。"""
    ranked = sorted(
        enumerate(plan_items),
        key=lambda pair: (_SLOT_ROLE_PRIORITY.get(_infer_role_bucket(pair[1]["mappingDetails"]), 9), pair[0]),
    )

    assignment = {}
    unassigned = []
    for rank, (_, item) in enumerate(ranked):
        role_bucket = _infer_role_bucket(item["mappingDetails"])
        entry = {"licno": item["licno"], "title": item["title"], "roleBucket": role_bucket}
        if rank < len(_SLOT_KEYS):
            slot = _SLOT_KEYS[rank]
            assignment[slot] = entry
            item["assignedSlot"] = slot
        else:
            unassigned.append(entry)
            item["assignedSlot"] = None

    return {"assignment": assignment, "unassigned": unassigned}


def generate_injection_plan(set_name: str, licno_list=None, user_intent: str = "") -> dict:
    """
    生成 Revit 推送計畫 (v3 精細化版)。
    """
    database = load_database()
    category_hint = _extract_category_hint(user_intent)
    attach_category_hint = _extract_attach_category_hint(user_intent)
    wall_usage_hint = _extract_wall_usage_hint(user_intent)

    # layerComposition 覆寫：若此 Set 在網頁上已明確指定材料層級，取得角色對照表與順序
    layer_composition = _load_layer_composition(set_name)
    lc_role_map = _build_layer_composition_role_map(layer_composition) if layer_composition else {}
    lc_aux_map = _build_layer_composition_aux_map(layer_composition) if layer_composition else {}
    lc_category = (layer_composition or {}).get("category") or category_hint

    # 1. 解析 licnos
    extracted = []
    if isinstance(licno_list, list) and licno_list:
        extracted = licno_list
    elif isinstance(licno_list, str):
        extracted = re.findall(r"GBM\d+", licno_list)

    if not extracted and user_intent:
        extracted = re.findall(r"GBM\d+", user_intent)

    if not extracted:
        sets = load_exported_sets()
        for key, val in sets.items():
            if set_name in key or key in set_name:
                extracted = val.get("items", [])
                break

    # 2. 匹配 Master DB
    # TABC 續證/增證案件在 Master DB 裡的 licno 帶有 (續)/(增) 等後綴（如 "GBM0103810(續)"），
    # 但 Set 清單常只存裸編號（如 "GBM0103810"）。做法：先精確比對；比對不到的裸編號，
    # 再用去除後綴的正規化比對回補——但輸出一律採用 DB 記錄「原始（帶後綴）」的 licno，
    # 絕不輸出被裁切掉後綴的版本。
    # 正規化只用於「比對」，1141 筆 Master DB 中僅 1 組（GBM0103338）同時存在裸碼與帶後綴版本，
    # 故一律優先精確比對、找不到才退回正規化比對，避免誤配到錯誤的那一筆。
    licno_set = set(extracted)
    matched = [item for item in database if item.get("licno") in licno_set]

    matched_licnos = {item.get("licno") for item in matched}
    unmatched = [l for l in licno_set if l not in matched_licnos]
    if unmatched:
        normalized_targets = {_normalize_licno(l) for l in unmatched}
        already_covered = set()
        for item in database:
            raw_licno = item.get("licno")
            if raw_licno in matched_licnos:
                continue
            norm = _normalize_licno(raw_licno)
            if norm in normalized_targets and norm not in already_covered:
                matched.append(item)
                already_covered.add(norm)

    # 2b. 若有 layerComposition，依使用者拖曳指定的順序重排 matched（供計畫呈現真實物理層序）
    if layer_composition:
        seq_licnos = [e["licno"] for e in layer_composition["sequence"] if e.get("type") == "material"]

        def _lc_sort_key(rec):
            rl = rec.get("licno")
            if rl in seq_licnos:
                return seq_licnos.index(rl)
            norm = _normalize_licno(rl)
            for i, sl in enumerate(seq_licnos):
                if _normalize_licno(sl) == norm:
                    return i
            return len(seq_licnos)

        matched.sort(key=_lc_sort_key)

    # 3. 組建計畫
    timestamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    plan_id = f"PLAN-{datetime.datetime.now().strftime('%Y%m%d%H%M%S')}"
    target_categories = set()
    plan_items = []
    has_auxiliary = False
    has_loadable_family = False

    expired_licenses = []

    for item in matched:
        cat = item.get("category", "健康")
        sub_cat = item.get("subCategory", "通用類")
        title = item.get("title", "")

        # 標章效期檢查（issue #128 第 3 層）：以標章自己的 period 結束日與今日比對，與資料庫年齡無關。
        # 這裡只「標記」，不在 Python 端擋下——擋在 /GM_inject 寫入 Revit 之前，因為擬訂計畫本身
        # 是唯讀動作，看得到過期材料才有辦法決定要換料還是重新抓資料。
        is_expired, valid_until = _period_end_expired(item.get("period", ""))

        # 進行 TASK-003 11大情境深度分析
        mapping_info = analyze_material_mapping(sub_cat, title, wall_usage_hint)

        # layerComposition 為權威來源：使用者已在網頁上為此材料明確指定 Structure/Substrate/
        # Finish 角色，或明確拖曳到「輔助材料」區時，優先採用該設定，覆寫關鍵字啟發式判斷結果
        # （見 domain/GM_parameter-schema.md「明確層級覆寫」）。輔助材料區優先於
        # 幾何角色檢查——兩者理論上互斥（同一 licno 不會同時出現在 sequence 與 auxiliary），
        # 但輔助材料的使用者意圖更明確，優先權更高。
        lc_aux_entry = _lookup_layer_composition_aux(item.get("licno"), lc_aux_map)
        if lc_aux_entry is not None:
            mapping_info = _resolve_layer_composition_auxiliary(lc_aux_entry)
        else:
            lc_entry = _lookup_layer_composition_role(item.get("licno"), lc_role_map)
            if lc_entry is not None:
                role, finish_index = lc_entry
                resolved = _resolve_layer_composition_mapping(role, finish_index, lc_category, wall_usage_hint)
                if resolved:
                    mapping_info = resolved

            # 材料本身跨用途、判斷不出來，且沒有 layerComposition 可用時，改用 Set 宣告的品類解析
            # （如混凝土可用於 Wall/Floor/Column/Beam，材料自己的資料無法決定，這次要用在哪由 Set 情境決定）。
            elif mapping_info.get("needsManualReview") and category_hint in _CATEGORY_HINT_FALLBACK:
                resolved = dict(_CATEGORY_HINT_FALLBACK[category_hint])
                resolved["resolvedBySetCategoryOverride"] = True
                resolved["originalUnclassifiedReason"] = (
                    f"材料本身 subCategory='{sub_cat}' 屬跨用途通用建材，無法單獨判斷；"
                    f"依 Set 宣告品類 '{category_hint}' 解析，非材料自身資料判斷結果"
                )
                mapping_info = resolved
        revit_cat = mapping_info["revitCategory"]
        target_categories.add(revit_cat)

        if mapping_info.get("isAuxiliary"):
            has_auxiliary = True
        if mapping_info.get("isLoadableFamily"):
            has_loadable_family = True

        # 組裝單一材料的共享參數欄位（供計畫報告呈現，後續依 _assign_material_slots 結果轉為 Mat1~Mat6 槽位）
        sp = {
            "GreenMaterial_Certified": True,
            "GreenMaterial_CertNo": item.get("licno"),
            "GreenMaterial_Category": f"{cat}綠建材",
            "GreenMaterial_SubCategory": sub_cat,
            "GreenMaterial_Applicant": item.get("company"),
            "GreenMaterial_ValidUntil": item.get("period"),
            "GreenMaterial_TVOC": 0.08,
            "GreenMaterial_Formaldehyde": 0.01,
            "GreenMaterial_RecycledRatio": 0.0,
            "GreenMaterial_AcousticNRC": _extract_acoustic_nrc(item.get("testItems", "")),
            "GreenMaterial_CNSSpec": item.get("cnsSpec", "依 CNS 國家標準試驗合格"),
            "GreenMaterial_TestItems": item.get("testItems", "TVOC逸散率、甲醛釋出量、重金屬檢測"),
            "GreenMaterial_QualifiedItems": item.get("qualifiedItems", f"{cat}綠建材"),
        }

        # 若為非幾何輔助材料，掛載 Group 5 自訂欄位
        if mapping_info.get("isAuxiliary"):
            sp[mapping_info["auxiliaryParam"]] = f"{item.get('title')} ({item.get('licno')})"

        if is_expired:
            expired_licenses.append({
                "licno": item.get("licno"),
                "title": title,
                "company": item.get("company"),
                "period": item.get("period"),
                "validUntil": valid_until,
            })

        plan_items.append({
            "licno": item.get("licno"),
            "title": title,
            "company": item.get("company"),
            "licenseExpired": is_expired,
            "licenseValidUntil": valid_until,
            "category": cat,
            "subCategory": sub_cat,
            "targetRevitCategory": revit_cat,
            "targetLayer": mapping_info["layer"],
            "defaultThickness": mapping_info["defaultThickness"],
            "buiNaming": mapping_info["buiNaming"],
            "mappingDetails": mapping_info,
            "sharedParameters": sp,
        })

    # Mat1~Mat6 六槽位分配（確定性規則，見上方 _assign_material_slots）
    slot_assignment = _assign_material_slots(plan_items)

    # 動態擬訂 4~6 個專業執行步驟
    if layer_composition:
        layer_step = (
            "3. 依據使用者於檢索平台明確指定的材料層級設定 (layerComposition) 配置構造層位階，"
            "不採用關鍵字啟發式推判"
        )
    else:
        layer_step = "3. 依據 TASK-003 規範自動配置構造層位階 (Finish 1 / Substrate / Structure) 與預設厚度推判"

    execution_steps = [
        "1. 載入 GreenMaterial_SharedParams.txt (包含 64 個共享參數，Mat1~Mat6 六槽位) 至 Revit 專案",
        f"2. 掃描專案模型對應品類：{', '.join(sorted(target_categories))}",
        layer_step,
    ]

    if has_auxiliary:
        execution_steps.append("4. 偵測到非幾何輔助材料 (填縫劑/接著劑)，自動寫入 Type 的 Construction 自訂欄位")
    if has_loadable_family:
        execution_steps.append("5. 偵測到獨立門窗元件，採用方法 7.1 備份既有 .rfa 家族檔並寫入 Family Type 參數")

    execution_steps.append(f"{len(execution_steps)+1}. 批量將 TABC 履歷與 CNS 試驗數據寫入 OST_Materials 與 Type Identity Data")

    plan = {
        "planId": plan_id,
        "setName": set_name,
        "generatedAt": timestamp,
        "agentName": "antigravity (建築 Agent)",
        "userIntent": user_intent,
        "targetRevitCategories": list(target_categories),
        "totalMaterialsCount": len(plan_items),
        "materialsMapping": plan_items,
        # issue #128 第 1 層：把本機資料庫的抓取時間一併帶進計畫，讓計畫自帶「它是依據多舊的資料擬的」。
        "databaseFreshness": database_freshness(),
        # issue #128 第 3 層：效期已過今日的標章清單。非空時 /GM_inject 必須停下來要求使用者明確核准，
        # 不得靜默寫入——已失效的證號會跟著 Type 進到交付模型、數量表與送審文件。
        "expiredLicenses": expired_licenses,
        "hasExpiredLicense": bool(expired_licenses),
        "executionSteps": execution_steps,
        "layerComposition": layer_composition,
        "layerCompositionSequenceLabels": (
            _layer_composition_sequence_labels(layer_composition, database) if layer_composition else None
        ),
        "materialSlotAssignment": slot_assignment,
        # TASK-005.5：純材料（isAuxiliary）Set 要依附的既有品類，來自 showcase 頁面的
        # 「純材料掛載品類」子問題（[需求對齊：...掛載類別: Wall]）。非純材料 Set 通常為 None。
        "pureMaterialAttachCategory": attach_category_hint or None,
        # TASK-005.6：從 Q3 補充條件自由文字解析出的牆體用途（外牆/分戶牆/輕隔間），
        # 用於 Wall Structure 層的厚度矩陣。None 表示使用者未指定，各 Structure 材料的
        # mappingDetails.wallUsageUnspecified 會標記為 True，/import 技能須在確認步驟提示覆寫。
        "wallUsageHint": wall_usage_hint or None,
    }

    # 儲存 JSON
    with open(PLAN_JSON, "w", encoding="utf-8") as f:
        json.dump(plan, f, ensure_ascii=False, indent=2)

    # 產出 Markdown 報告
    os.makedirs(os.path.dirname(PLAN_REPORT), exist_ok=True)
    _write_markdown_report(plan)

    print(f"Successfully generated injection plan {plan_id} with {len(plan_items)} materials: "
          f"{[m['licno'] for m in plan_items]}.")
    return plan


def _write_markdown_report(plan: dict):
    """將計畫輸出為 Markdown 報告"""
    lines = [
        f"# 🤖 AI 建築 Agent：Revit 綠建材推送執行計畫書 (v3 專業版)",
        f"",
        f"- **計畫編號 (Plan ID)**: `{plan['planId']}`",
        f"- **材料 Set 名稱**: `{plan['setName']}`",
        f"- **擬訂時間**: `{plan['generatedAt']}`",
        f"- **執行 Agent**: `{plan['agentName']}`",
    ]

    # 0. 資料新鮮度與標章效期（issue #128）。永遠輸出，即使全部正常——「沒有這一段」與
    # 「這一段說沒問題」在報告上必須看得出差別，否則讀報告的人無從得知這件事到底有沒有被檢查過。
    fresh = plan.get("databaseFreshness") or {}
    lines += ["", "---", "", "## 0. 資料來源新鮮度與標章效期", ""]
    if fresh.get("status") == "missing":
        lines.append("- ⛔ **本機無 tabc_master_database.json** —— 本計畫沒有可用的資料來源，請先執行 `/GM_update`。")
    elif fresh.get("status") == "unknown":
        lines.append("- ⚠️ **無法判斷本機資料庫的抓取時間**，不確定本計畫依據的資料有多舊。")
    else:
        src = "／來源：`/GM_update` 抓取時間戳" if fresh.get("fetchedAtSource") == "meta" else "／來源：檔案 mtime 推估（非確據）"
        icon = "⚠️" if fresh.get("stale") else "✅"
        lines.append(
            f"- {icon} 本機資料庫抓取於 `{fresh.get('fetchedAt')}`，距今 **{fresh.get('ageDays')} 天**，"
            f"共 **{fresh.get('recordCount')}** 筆（門檻 {fresh.get('thresholdDays')} 天{src}）"
        )
    if fresh.get("recommendation"):
        lines.append(f"- {fresh['recommendation']}")

    if plan.get("hasExpiredLicense"):
        lines += ["", f"### ⛔ 標章效期已過（{len(plan['expiredLicenses'])} 項）—— 寫入 Revit 前必須經使用者明確核准", ""]
        for e in plan["expiredLicenses"]:
            lines.append(f"- `{e['licno']}`｜{e['title']}｜{e['company']}｜效期 `{e['period']}`（結束於 `{e['validUntil']}`）")
        lines.append("")
        lines.append("**不得靜默寫入**：已失效的證號會隨 Type 進入交付模型、數量明細表與送審文件。")
    else:
        lines += ["", "- ✅ 本計畫所有標章的效期結束日皆晚於今日（或效期格式無法解析，未被判定為過期）。"]

    lines += [
        f"",
        f"---",
        f"",
        f"## 1. 受影響 Revit 元件品類與對映架構",
    ]
    for cat in plan["targetRevitCategories"]:
        lines.append(f"- **`{cat}`**")

    if plan.get("layerCompositionSequenceLabels"):
        lines += [
            "",
            "---",
            "",
            "## 1b. 材料層級順序（使用者於檢索平台明確指定，權威來源）",
            "",
        ]
        for label in plan["layerCompositionSequenceLabels"]:
            lines.append(f"1. {label}")

    lines += ["", "---", "", "## 2. 材料與 Revit 19個共享參數對映清單", ""]

    for idx, m in enumerate(plan["materialsMapping"], 1):
        sp = m["sharedParameters"]
        lines += [
            f"### [{idx}] {m['title']} (`{m['licno']}`)",
            f"- **製造廠商**: {m['company']}",
            f"- **標章分類**: {m['category']}綠建材 ({m['subCategory']})",
            f"- **目標 Revit 品類**: `{m['targetRevitCategory']}`",
            f"- **建議構造層**: `{m['targetLayer']}` ｜ **預設厚度**: `{m['defaultThickness']}`",
            f"- **BIM 建議命名**: `{m['buiNaming']}`",
            f"- **CNS 依據**: {sp['GreenMaterial_CNSSpec']}",
            f"- **合格項目**: {sp['GreenMaterial_QualifiedItems']}",
            f"- **試驗數據**: {sp['GreenMaterial_TestItems']}",
        ]
        if m["mappingDetails"].get("resolvedByLayerComposition"):
            lines.append("- **層級來源**: 使用者於檢索平台材料層級設定明確指定（非關鍵字啟發式判斷）")
        elif m["mappingDetails"].get("resolvedBySetCategoryOverride"):
            lines.append(f"- **層級來源**: {m['mappingDetails'].get('originalUnclassifiedReason', '')}")
        if m["mappingDetails"].get("isAuxiliary"):
            aux_key = m["mappingDetails"]["auxiliaryParam"]
            lines.append(f"- **非幾何欄位**: `{aux_key}` ➔ {sp.get(aux_key, '')}")
        slot = m.get("assignedSlot")
        lines.append(
            f"- **GreenMaterial_Mat 槽位**: `{slot.upper()}`" if slot
            else "- **GreenMaterial_Mat 槽位**: 未分配（超過 6 槽位上限，僅有實體構造層，無 GreenMaterial_Mat* 參數）"
        )
        lines.append("")

    slot_assignment = plan.get("materialSlotAssignment")
    if slot_assignment and (len(plan["materialsMapping"]) > len(_SLOT_KEYS)):
        lines += ["---", "", "## 2b. Mat1~Mat6 六槽位分配（確定性規則：Structure > Finish > Substrate > 其他）", ""]
        for slot_key in _SLOT_KEYS:
            entry = slot_assignment["assignment"].get(slot_key)
            if entry:
                lines.append(f"- **{slot_key.upper()}**: {entry['title']} (`{entry['licno']}`)｜角色: {entry['roleBucket']}")
        if slot_assignment["unassigned"]:
            lines.append("")
            lines.append("**未分配（僅建立實體構造層，無共享參數紀錄）**：")
            for entry in slot_assignment["unassigned"]:
                lines.append(f"- {entry['title']} (`{entry['licno']}`)｜角色: {entry['roleBucket']}")
        lines.append("")

    lines += [
        "---",
        "",
        "## 3. 預備執行動作 SOP",
    ]
    for step in plan["executionSteps"]:
        lines.append(step)

    with open(PLAN_REPORT, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def write_back_to_set_manager(set_name: str, plan: dict, purpose_override: str = "", planned_actions_override: str = ""):
    """
    將對齊與討論後的計畫/用途自動回傳寫入 exported_material_sets.json。
    """
    sets = load_exported_sets()

    cats = "、".join(plan.get("targetRevitCategories", []))
    materials_summary = "、".join(
        f"{m['title']} ({m['licno']})" for m in plan["materialsMapping"]
    )

    if purpose_override:
        purpose = purpose_override
    else:
        purpose = (
            f"將 {len(plan['materialsMapping'])} 項綠建材寫入 Revit 模型 [{cats}]：{materials_summary}"
        )

    if planned_actions_override:
        planned_actions = planned_actions_override
    else:
        planned_actions = "\n".join(plan["executionSteps"])

    matched_key = None
    for key in sets:
        if key == set_name or set_name in key or key in set_name:
            matched_key = key
            break

    if matched_key is None:
        matched_key = set_name
        sets[matched_key] = {
            "name": set_name,
            "createdAt": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
            "items": [m["licno"] for m in plan["materialsMapping"]],
        }

    sets[matched_key]["purpose"] = purpose
    sets[matched_key]["plannedActions"] = planned_actions
    sets[matched_key]["planStatus"] = "已完成 Revit 牆體元件注入" if "Element ID" in planned_actions else "已對齊 Agent 計畫"
    sets[matched_key]["planId"] = plan["planId"]
    sets[matched_key]["updatedAt"] = datetime.datetime.now().isoformat()

    with open(SETS_FILE, "w", encoding="utf-8") as f:
        json.dump(sets, f, ensure_ascii=False, indent=2)

    print(f"[OK] Plan written back to Set Manager: [{matched_key}]")
    print(f"     Status: Aligned with Agent Plan")
    print(f"     planId: {plan['planId']}")
    return sets[matched_key]


# ── /GM_set compare：Set 與最新 Master DB 比對（見 domain 觸發字「green material search」相關流程，
# 由 .claude/skills/GM_set/SKILL.md 驅動）──
# Revit_Injection_Plan.json 每次 generate_injection_plan() 都會被覆寫，不保留逐 Set 歷史；
# 唯一持久保存的歷史快照是 write_back_to_set_manager() 寫入 exported_material_sets.json 的
# purpose 摘要文字（格式："...：title1 (licno1)、title2 (licno2)"），比對材料名稱是否變動時
# 從這段文字還原上次擬訂計畫當下的 licno -> title 對照。

def _roc_to_date(roc_str: str):
    """將 '115/07/09' 這種民國年格式轉為 datetime.date；格式不符則回傳 None。"""
    m = re.match(r"^\s*(\d{2,3})/(\d{1,2})/(\d{1,2})\s*$", roc_str or "")
    if not m:
        return None
    roc_year, month, day = (int(x) for x in m.groups())
    try:
        return datetime.date(roc_year + 1911, month, day)
    except ValueError:
        return None


def _period_end_expired(period: str):
    """回傳 (is_expired, end_date_raw)。period 格式為 '115/07/09 ~ 119/07/08'；
    解析不出結束日期時回傳 (False, None)，不把格式異常誤判為過期。"""
    if not period or "~" not in period:
        return False, None
    end_raw = period.split("~")[-1].strip()
    end_date = _roc_to_date(end_raw)
    if end_date is None:
        return False, None
    return end_date < datetime.date.today(), end_raw


_PURPOSE_TITLE_RE = re.compile(r"([^、\[\]：:]+?)\s*\((GBM\d+[^\)]*)\)")


def _parse_purpose_title_snapshot(purpose: str) -> dict:
    """從 purpose 摘要文字還原上次擬訂計畫當下，各 licno 對應的材料名稱快照。"""
    if not purpose:
        return {}
    return {licno: title.strip() for title, licno in _PURPOSE_TITLE_RE.findall(purpose)}


def diff_set_with_latest(set_name: str, entry: dict, database: list) -> dict:
    """比對單一 Set 的 items（licno 清單）與目前 tabc_master_database.json 的最新資料：
    - missing: licno 在目前資料庫已找不到（含正規化後綴比對）
    - expired: 憑證有效期限已過今日
    - changed: 材料名稱與上次擬訂計畫時的快照不同
    """
    items = entry.get("items", []) or []
    db_by_licno = {d.get("licno"): d for d in database}
    db_by_norm = {}
    for d in database:
        db_by_norm.setdefault(_normalize_licno(d.get("licno")), d)

    old_titles = _parse_purpose_title_snapshot(entry.get("purpose", ""))

    matched, missing, expired, changed = [], [], [], []
    for licno in items:
        rec = db_by_licno.get(licno) or db_by_norm.get(_normalize_licno(licno))
        if rec is None:
            missing.append(licno)
            continue
        matched.append(rec.get("licno"))

        is_expired, end_date = _period_end_expired(rec.get("period", ""))
        if is_expired:
            expired.append({"licno": rec.get("licno"), "title": rec.get("title"), "period": rec.get("period")})

        old_title = old_titles.get(licno) or old_titles.get(rec.get("licno"))
        new_title = (rec.get("title") or "").strip()
        if old_title and old_title != new_title:
            changed.append({"licno": rec.get("licno"), "oldTitle": old_title, "newTitle": new_title})

    return {
        "setName": set_name,
        "totalItems": len(items),
        "matched": matched,
        "missing": missing,
        "expired": expired,
        "changed": changed,
        "hasDiff": bool(missing or expired or changed),
    }


def compare_all_sets() -> list:
    """比對 exported_material_sets.json 內所有 Set，回傳每個 Set 的 diff 結果清單。"""
    database = load_database()
    sets = load_exported_sets()
    return [diff_set_with_latest(name, entry, database) for name, entry in sets.items()]


def compare_and_refresh_set(set_name: str) -> dict:
    """比對單一 Set，若有差異則用目前最新資料庫重新執行 generate_injection_plan() +
    write_back_to_set_manager()，刷新 Revit_Injection_Plan.json / 報告 / Set 的
    plannedActions、planStatus、planId。這一步只更新計畫檔與 Set 狀態，不寫入 Revit——
    實際 Revit 寫入仍需使用者另外執行 /GM_inject revit。"""
    database = load_database()
    sets = load_exported_sets()
    entry = _find_set_entry(set_name, sets)
    if entry is None:
        raise KeyError(f"找不到 Set: {set_name}")

    diff = diff_set_with_latest(set_name, entry, database)
    if not diff["hasDiff"]:
        return {"diff": diff, "refreshed": False}

    items = entry.get("items", [])
    user_intent = entry.get("purpose", "")
    plan = generate_injection_plan(set_name, items, user_intent)
    write_back_to_set_manager(set_name, plan)
    return {"diff": diff, "refreshed": True, "planId": plan["planId"]}


if __name__ == "__main__":
    import sys

    if "--freshness" in sys.argv[1:]:
        # /GM_import 第 1、2 層的確定性入口：只讀本機資料庫的年齡，不擬計畫、不寫任何檔案。
        print(json.dumps(database_freshness(), ensure_ascii=False, indent=2))
        sys.exit(0)

    licnos = ["GBM0000000", "GBM0000001"]
    user_intent = "/GM_import 請為材料 Set 【室內牆】(GBM0000000, GBM0000001) 擬訂 Revit 綠建材寫入計畫"
    plan = generate_injection_plan("室內牆", licnos, user_intent)
    write_back_to_set_manager("室內牆", plan)
