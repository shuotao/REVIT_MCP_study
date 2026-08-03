#!/usr/bin/env python3
"""
Revit 綠建材推送計畫擬訂引擎 v3 (基於 TASK-003 11大工程情境與19共享參數)
========================================================================
功能：
  - 依據 /GMimport 指令解析材料 Set 的真實 licno 清單
  - 從 tabc_master_database.json 精確匹配全量材料數據
  - 自動判斷 Revit 品類 (Walls / Floors / Ceilings / Windows / Auxiliary)
  - 自動配置構造層 (Finish 1 / Substrate / Structure) 與預設厚度推判 (2mm / 15mm / 150mm)
  - 支援非幾何材料 (接著劑 / 填縫劑 / 防水膜) 寫入 Construction 群組
  - 產出 19 個共享參數寫入計畫與專業 BIM 執行步驟
  - write_back_to_set_manager(): 將對齊計畫回傳至 exported_material_sets.json
"""

import json
import os
import re
import datetime

WORKSPACE = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.path.join(WORKSPACE, "tabc_master_database.json")
SETS_FILE = os.path.join(WORKSPACE, "exported_material_sets.json")
PLAN_JSON = os.path.join(WORKSPACE, "Revit_Injection_Plan.json")
PLAN_REPORT = os.path.join(WORKSPACE, "docs", "Revit_Injection_Plan_Report.md")


def load_database():
    with open(DB_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


def load_exported_sets():
    if os.path.exists(SETS_FILE):
        try:
            with open(SETS_FILE, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            pass
    return {}


def _normalize_licno(licno: str) -> str:
    """去除尾端的 (續)/(增)/(改) 等註記後綴，僅供比對使用。
    輸出永遠採用資料庫記錄「原始、帶後綴」的 licno，此函式不用於任何輸出欄位。
    """
    if not licno:
        return licno
    return re.sub(r"[（(].*?[）)]\s*$", "", licno).strip()


def analyze_material_mapping(sub_cat: str, title: str) -> dict:
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
            "defaultThickness": "150 mm (結構牆)" if is_structure_material else "120 mm (輕隔間)",
            "buiNaming": "W_INT_RC15",
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
            "defaultThickness": "150 mm (結構牆)",
            "buiNaming": "W_INT_RC15",
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
    """從 /GMimport 文字裡的 [需求對齊：...品類: Floor...] 擷取 Set 宣告的品類，用於解析 needsManualReview 的材料。"""
    if not user_intent:
        return ""
    m = re.search(r"品類[:：]\s*([A-Za-z一-鿿]+)", user_intent)
    if not m:
        return ""
    raw = m.group(1).strip()
    return _CATEGORY_HINT_ALIASES.get(raw, raw)


def generate_injection_plan(set_name: str, licno_list=None, user_intent: str = "") -> dict:
    """
    生成 Revit 推送計畫 (v3 精細化版)。
    """
    database = load_database()
    category_hint = _extract_category_hint(user_intent)

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

    # 3. 組建計畫
    timestamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    plan_id = f"PLAN-{datetime.datetime.now().strftime('%Y%m%d%H%M%S')}"
    target_categories = set()
    plan_items = []
    has_auxiliary = False
    has_loadable_family = False

    for item in matched:
        cat = item.get("category", "健康")
        sub_cat = item.get("subCategory", "通用類")
        title = item.get("title", "")

        # 進行 TASK-003 11大情境深度分析
        mapping_info = analyze_material_mapping(sub_cat, title)

        # 材料本身跨用途、判斷不出來時，改用 Set 宣告的品類解析（如混凝土可用於
        # Wall/Floor/Column/Beam，材料自己的資料無法決定，這次要用在哪由 Set 情境決定）。
        if mapping_info.get("needsManualReview") and category_hint in _CATEGORY_HINT_FALLBACK:
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

        # 組裝 19 個共享參數 schema
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
            "GreenMaterial_AcousticNRC": 0.75 if "吸音" in sub_cat else 0.0,
            "GreenMaterial_DecorArea": 0.0,
            "GreenMaterial_QualifyArea": 0.0,
            "GreenMaterial_RatioContribution": 0.0,
            "GreenMaterial_CNSSpec": item.get("cnsSpec", "依 CNS 國家標準試驗合格"),
            "GreenMaterial_TestItems": item.get("testItems", "TVOC逸散率、甲醛釋出量、重金屬檢測"),
            "GreenMaterial_QualifiedItems": item.get("qualifiedItems", f"{cat}綠建材"),
        }

        # 若為非幾何輔助材料，掛載 Group 5 自訂欄位
        if mapping_info.get("isAuxiliary"):
            sp[mapping_info["auxiliaryParam"]] = f"{item.get('title')} ({item.get('licno')})"

        plan_items.append({
            "licno": item.get("licno"),
            "title": title,
            "company": item.get("company"),
            "category": cat,
            "subCategory": sub_cat,
            "targetRevitCategory": revit_cat,
            "targetLayer": mapping_info["layer"],
            "defaultThickness": mapping_info["defaultThickness"],
            "buiNaming": mapping_info["buiNaming"],
            "mappingDetails": mapping_info,
            "sharedParameters": sp,
        })

    # 動態擬訂 4~6 個專業執行步驟
    execution_steps = [
        "1. 載入 GreenMaterial_SharedParams.txt (包含 19 個共享參數) 至 Revit 專案",
        f"2. 掃描專案模型對應品類：{', '.join(sorted(target_categories))}",
        "3. 依據 TASK-003 規範自動配置構造層位階 (Finish 1 / Substrate / Structure) 與預設厚度推判",
    ]

    if has_auxiliary:
        execution_steps.append("4. 偵測到非幾何輔助材料 (填縫劑/接著劑)，自動寫入 Type 的 Construction 自訂欄位")
    if has_loadable_family:
        execution_steps.append("5. 偵測到獨立門窗元件，採用方法 7.1 備份既有 .rfa 家族檔並寫入 Family Type 參數")

    execution_steps.append(f"{len(execution_steps)+1}. 批量將 TABC 履歷與 CNS 試驗數據寫入 OST_Materials 與 Type Identity Data")
    execution_steps.append(f"{len(execution_steps)+1}. 自動匯出綠建材明細表 (Schedule) 至 Excel 歸檔")

    plan = {
        "planId": plan_id,
        "setName": set_name,
        "generatedAt": timestamp,
        "agentName": "antigravity (建築 Agent)",
        "userIntent": user_intent,
        "targetRevitCategories": list(target_categories),
        "totalMaterialsCount": len(plan_items),
        "materialsMapping": plan_items,
        "executionSteps": execution_steps,
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
        f"",
        f"---",
        f"",
        f"## 1. 受影響 Revit 元件品類與對映架構",
    ]
    for cat in plan["targetRevitCategories"]:
        lines.append(f"- **`{cat}`**")

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
        if m["mappingDetails"].get("isAuxiliary"):
            aux_key = m["mappingDetails"]["auxiliaryParam"]
            lines.append(f"- **非幾何欄位**: `{aux_key}` ➔ {sp.get(aux_key, '')}")
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


if __name__ == "__main__":
    licnos = ["GBM0104204", "GBM0104194"]
    user_intent = "/GMimport 請為材料 Set 【室內牆】(GBM0104204, GBM0104194) 擬訂 Revit 綠建材寫入計畫"
    plan = generate_injection_plan("室內牆", licnos, user_intent)
    write_back_to_set_manager("室內牆", plan)
