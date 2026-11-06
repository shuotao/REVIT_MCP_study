#!/usr/bin/env python3
"""
TABC 綠建材主資料庫更新工具
================================
從 TABC 官網 (https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx) 依 GBMTYPE 1~4
（健康/高性能/再生/生態）分頁抓取列表頁真實資料（licno/title/company/period/
category/subCategory/img），與現有 tabc_master_database.json 依 licno 合併：

  - 新出現的 licno -> 新增
  - 既有 licno 但 title/company/period/category/subCategory 有異動 -> 更新
  - 既有 licno 且本次未再出現於列表頁 -> 保留原紀錄不刪除，僅在報告中列為
    「本次未再出現」（可能已下架，也可能是本次擷取不完整），交由使用者判斷，
    避免把單次網路失敗誤判成資料下架而整批刪掉。

cnsSpec/testItems/qualifiedItems/productSpecFull/specList/specs/keywords 這些
欄位不是逐筆從 TABC 詳細頁面抓取的真實試驗數據，而是沿用原始開發分支
tools/green-material/archive/scripts/catalog/enrich_tabc_specs_database.py
既有的「關鍵字規則模板」重新套用。該路徑未隨本 repo 收編（原始 PR 分支的
archive/ 目錄含第三方 TABC 資料，收編時整批排除，見 tools/green-material/
README.md「archive/ 目錄未隨本 repo 收編」一節），本檔案延續其推論規則，
不代表該原始腳本存在於本 repo。新增或有異動的記錄會重新套用模板；未變動
的既有記錄維持原樣，不覆寫既有欄位。

更新完成後，會用 assets/green-material-showcase.template.html（git 追蹤的 UI 樣板）
重新產生 assets/green-material-showcase.html：把樣板裡的 `const tabcDatabase = [...]`
標記替換成最新資料，讓網頁離線快取跟 tabc_master_database.json 保持一致，同時保證
展示頁的 UI 一律是樣板目前的最新版本（不會殘留本機舊版 UI）。

tabc_master_database.json 與 assets/green-material-showcase.html 都是本機專屬、不入
git 的檔案（見 tools/green-material/README.md）。**兩者任一個或都不存在時（例如全新
clone 這個 repo 之後第一次執行），本腳本會自動視為首次建立，不會報錯**：
tabc_master_database.json 缺席就視為空資料庫，全部抓到的項目都會被當成新增。

用法：
    python GM_update_tabc_database.py             # 執行更新（列表頁抓取 + 合併 + 回寫；首次執行=全量匯入）
    python GM_update_tabc_database.py --dry-run    # 只抓取並輸出差異報告，不寫入任何檔案
    python GM_update_tabc_database.py --resync-html  # 不連線 TABC，只用現有本機資料庫 + 最新樣板
                                                      # 重新產生展示頁（git pull 到樣板更新後用這個最快）
"""

import base64
import datetime
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
WORKSPACE = os.path.dirname(os.path.dirname(SCRIPT_DIR))  # repo root (this file lives in tools/green-material/)
DB_PATH = os.path.join(WORKSPACE, "tabc_master_database.json")
# 抓取時間戳的旁生檔（sidecar）。主資料庫本身是一個純 JSON 陣列（`[{...}, ...]`），沒有可以
# 放中繼資料的外層物件；把它改成帶 header 的物件會同時打斷 load_database()、_build_showcase_html()
# 與展示頁的 `const tabcDatabase = [...]` 拼接，所以時間戳改寫在旁生檔。檔名刻意含
# "tabc_master_database"，讓 QA/QC 閘門 3-5 的路徑樣式（子字串比對）自動涵蓋它，不必動閘門。
DB_META_PATH = os.path.join(WORKSPACE, "tabc_master_database.meta.json")
SHOWCASE_PATH = os.path.join(WORKSPACE, "assets", "green-material-showcase.html")
TEMPLATE_PATH = os.path.join(WORKSPACE, "assets", "green-material-showcase.template.html")

TABC_SEARCH_URL = "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx"
CATEGORY_NAMES = {1: "健康", 2: "高性能", 3: "再生", 4: "生態"}
MAX_PAGES_PER_CATEGORY = 80  # 安全上限，避免來源網站分頁邏輯異常造成無窮迴圈
REQUEST_TIMEOUT = 10
REQUEST_DELAY = 0.05

TABC_DATABASE_MARKER_START = "  const tabcDatabase = ["
TABC_DATABASE_MARKER_END = "\n];\n"


def _tag_sub_category(title: str) -> str:
    if any(x in title for x in ["塗料", "漆", "水泥漆", "乳膠漆", "薄塗", "底漆"]):
        return "塗料類"
    if any(x in title for x in ["地板", "地坪", "地磚", "木地板", "塑木", "棧板"]):
        return "地板類"
    if any(x in title for x in ["牆", "矽酸鈣", "石膏", "水泥板", "壁板"]):
        return "牆壁類"
    if any(x in title for x in ["天花板", "吸音板", "吊頂"]):
        return "天花板類"
    if any(x in title for x in ["隔音", "緩衝", "樓板隔音"]):
        return "隔音緩衝類"
    if any(x in title for x in ["透水", "高壓磚", "滲透"]):
        return "透水鋪面類"
    return "綜合建材類"


def _fetch_list_page(gbm_type: int, page: int):
    """抓取單一分類單一分頁，回傳該頁解析出的原始項目 list。失敗時拋出例外。"""
    b64_param = base64.b64encode(
        f"GBMTYPE={gbm_type}&WC9V_BLCase_Data2einpage={page}".encode("utf-8")
    ).decode("utf-8")
    url = f"{TABC_SEARCH_URL}?EinB64={b64_param}"
    req = urllib.request.Request(
        url, headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
    )
    with urllib.request.urlopen(req, timeout=REQUEST_TIMEOUT) as resp:
        content = resp.read()
    try:
        html = content.decode("utf-8")
    except UnicodeDecodeError:
        html = content.decode("big5", errors="ignore")

    cat_name = CATEGORY_NAMES[gbm_type]
    items = []
    for block in html.split("<TR>"):
        if "GBM" not in block or "openLargeModal" not in block:
            continue
        licno_m = re.search(r"GBM\d+[\(（]?[^<]*", block)
        title_m = re.search(r"openLargeModal\([^)]+\)['>\s]*([^<]+)</a>", block)
        if not (licno_m and title_m):
            continue
        img_m = re.search(r"src=['\"](\./Object/ProductImages/[^'\"]+)['\"]", block)
        dates = re.findall(r"\d{2,3}/\d{2}/\d{2}", block)
        spans = re.findall(r"<span[^>]*>(.*?)</span>", block, re.DOTALL)
        clean_spans = [
            re.sub(r"<[^>]+>", "", s).strip() for s in spans if re.sub(r"<[^>]+>", "", s).strip()
        ]

        licno = licno_m.group(0).strip()
        title = title_m.group(1).strip()
        company = clean_spans[3] if len(clean_spans) > 3 else (
            clean_spans[-3] if len(clean_spans) >= 3 else "TABC合格廠商"
        )
        start_d = dates[0] if len(dates) >= 1 else ""
        limit_d = dates[1] if len(dates) >= 2 else ""
        img_path = "https://tabcmgr.hopto.org/mgr" + img_m.group(1)[1:] if img_m else ""
        sub_cat = _tag_sub_category(title)

        items.append({
            "licno": licno,
            "title": title,
            "company": company,
            "period": f"{start_d} ~ {limit_d}",
            "img": img_path,
            "category": cat_name,
            "subCategory": sub_cat,
            "url": TABC_SEARCH_URL,
            "highlight": f"✅ TABC 官方線上合格案件 ({start_d})",
        })
    return items


def fetch_all_from_tabc(progress=print):
    """依 GBMTYPE 1~4 逐分類逐頁抓取，回傳 {licno: item} 去重後的結果。
    單一分頁失敗只中止該分類的後續分頁抓取，不影響其他分類（部分失敗仍回傳已抓到的資料，
    交由呼叫端在合併階段用「本次未再出現」機制處理，不會誤刪其他分類資料）。
    """
    fetched = {}
    for gbm_type, cat_name in CATEGORY_NAMES.items():
        progress(f"抓取分類 GBMTYPE={gbm_type}（{cat_name}）...")
        page = 0
        while page < MAX_PAGES_PER_CATEGORY:
            try:
                page_items = _fetch_list_page(gbm_type, page)
            except (urllib.error.URLError, TimeoutError, OSError) as exc:
                progress(f"  第 {page + 1} 頁擷取失敗（{exc}），停止此分類後續分頁")
                break
            if not page_items:
                break
            new_in_page = 0
            for item in page_items:
                if item["licno"] not in fetched:
                    fetched[item["licno"]] = item
                    new_in_page += 1
            if new_in_page == 0:
                break
            page += 1
            time.sleep(REQUEST_DELAY)
        progress(f"  {cat_name} 分類累計 {sum(1 for v in fetched.values() if v['category'] == cat_name)} 筆")
    return fetched


def _enrich_record(item: dict) -> dict:
    """套用 enrich_tabc_specs_database.py 的關鍵字規則模板，補上 cnsSpec/testItems/
    qualifiedItems/productSpecFull/specList/specs/keywords。這些欄位是推論值，不是逐筆
    從 TABC 詳細頁面抓取的真實試驗數據（見本檔案開頭說明）。"""
    title = item.get("title", "")
    cat = item.get("category", "健康")
    sub_cat = item.get("subCategory", "塗料類")
    licno = item.get("licno", "")

    if "抗壓" in title or "混凝土" in title or "水泥" in title or "石膏磚" in title or cat == "再生":
        cns_std = "依 CNS3090 試驗，符合規定。"
        pass_item = f"{cat}綠建材"
        test_item = "① 28天抗壓強度：343kgf/cm2。② 56天氯離子滲透電量：942庫倫。"
    elif sub_cat == "塗料類" or "漆" in title or "塗材" in title:
        cns_std = "依 CNS16082 / CNS15200 試驗，符合規定。"
        pass_item = "健康綠建材 (低有機揮發物)"
        test_item = "① TVOC逸散率：0.08 mg/m²·h。② 游離甲醛逸散率：0.01 mg/m²·h。③ 4大重金屬(鉛/鎘/汞/六價鉻)：未檢出。"
    elif sub_cat == "地板類" or "地板" in title or "地磚" in title:
        cns_std = "依 CNS1349 / CNS16083 試驗，符合規定。"
        pass_item = "健康綠建材 (地板類)"
        test_item = "① 游離甲醛釋出量：0.02 mg/m²·h (F1等級)。② TVOC逸散率：0.05 mg/m²·h。③ 吸水厚度膨脹率：<0.5%。"
    elif sub_cat == "天花板類" or "吸音" in title or "岩棉" in title:
        cns_std = "依 CNS9056 / CNS14705-1 試驗，符合規定。"
        pass_item = "高性能吸音綠建材"
        test_item = "① 降噪係數 NRC：0.75。② 聲學吸音率 SAA：0.78。③ 耐燃性能：耐燃一級。"
    elif "防音" in title or "隔音" in title or "玻璃" in title:
        cns_std = "依 CNS8465-1 試驗，符合規定。"
        pass_item = "高性能防音綠建材"
        test_item = "① 空氣音隔音等級 Rw：42 dB。② 樓板衝擊音降低量 △Lw：20 dB。"
    else:
        cns_std = "依 CNS16082 試驗，符合規定。"
        pass_item = f"{cat}綠建材 ({sub_cat})"
        test_item = "① TVOC逸散率：< 0.19 mg/m²·h。② 游離甲醛逸散率：< 0.05 mg/m²·h。③ 毒性化學物質：無。"

    item["cnsSpec"] = cns_std
    item["qualifiedItems"] = pass_item
    item["testItems"] = test_item
    item["productSpecFull"] = f"【產品規格與性能】\n{cns_std}\n合格項目：{pass_item}\n試驗項目：{test_item}"
    item["specList"] = [
        f"原網頁核定編號: {licno}",
        f"廠商: {item.get('company', '')}",
        f"標章分類: {cat}綠建材 ({sub_cat})",
        f"國家標準: {cns_std}",
        f"合格項目: {pass_item}",
        f"試驗項目: {test_item}",
    ]
    item["specs"] = (
        f"原網頁名稱：{title}。申請公司：{item.get('company', '')}。"
        f"有效期限：{item.get('period', '')}。{cns_std} 合格項目：{pass_item}。試驗項目：{test_item}"
    )
    item["keywords"] = [sub_cat, cat, title, item.get("company", "")]
    return item


def merge_database(existing: list, fetched: dict) -> tuple:
    """合併既有資料庫與本次抓取結果。回傳 (merged_list, diff_report_dict)。"""
    existing_by_licno = {item["licno"]: item for item in existing}
    added, updated, not_seen = [], [], []
    merged = []

    for licno, new_item in fetched.items():
        old_item = existing_by_licno.get(licno)
        if old_item is None:
            _enrich_record(new_item)
            merged.append(new_item)
            added.append(licno)
            continue

        changed_fields = [
            f for f in ("title", "company", "period", "category", "subCategory")
            if old_item.get(f) != new_item.get(f)
        ]
        if changed_fields:
            merged_item = dict(old_item)
            merged_item.update(new_item)
            _enrich_record(merged_item)
            merged.append(merged_item)
            updated.append({"licno": licno, "changedFields": changed_fields})
        else:
            merged.append(old_item)

    fetched_licnos = set(fetched.keys())
    for licno, old_item in existing_by_licno.items():
        if licno not in fetched_licnos:
            merged.append(old_item)
            not_seen.append(licno)

    merged.sort(key=lambda x: x.get("licno", ""))
    diff = {
        "added": added,
        "updated": updated,
        "notSeen": not_seen,
        "totalBefore": len(existing),
        "totalAfter": len(merged),
    }
    return merged, diff


def _build_showcase_html(merged: list) -> str:
    """從 assets/green-material-showcase.template.html（git 追蹤的 UI 樣板）讀取 UI 外殼，
    把 merged 資料庫塞進 `const tabcDatabase = [...]` 標記位置，回傳完整的展示頁 HTML 字串。
    永遠以樣板為 UI 基底，確保重新產生出來的展示頁不會殘留本機手動改過的舊版 UI。"""
    with open(TEMPLATE_PATH, "r", encoding="utf-8") as f:
        html = f.read()

    start_idx = html.find(TABC_DATABASE_MARKER_START)
    if start_idx == -1:
        raise RuntimeError(f"樣板檔案 {TEMPLATE_PATH} 找不到 tabcDatabase 起始標記，樣板可能已損壞")
    array_body_start = start_idx + len(TABC_DATABASE_MARKER_START)
    end_idx = html.find(TABC_DATABASE_MARKER_END, array_body_start)
    if end_idx == -1:
        raise RuntimeError(f"樣板檔案 {TEMPLATE_PATH} 找不到 tabcDatabase 結尾標記，樣板可能已損壞")

    full_array = json.dumps(merged, ensure_ascii=False, indent=2)  # "[\n  {...}\n]"
    inner = full_array[1:-1]  # 去掉最外層的 "[" 與 "]"，保留原本的縮排與換行

    return (
        html[:start_idx]
        + TABC_DATABASE_MARKER_START
        + inner
        + "];\n"
        + html[end_idx + len(TABC_DATABASE_MARKER_END):]
    )


def _sync_showcase_html(merged: list) -> bool:
    """從樣板重新產生完整的 assets/green-material-showcase.html 並寫入磁碟。
    樣板不存在或標記缺失時回傳 False（不拋例外，呼叫端可自行決定要不要警告使用者）。"""
    if not os.path.exists(TEMPLATE_PATH):
        return False
    try:
        new_html = _build_showcase_html(merged)
    except RuntimeError:
        return False
    with open(SHOWCASE_PATH, "w", encoding="utf-8") as f:
        f.write(new_html)
    return True


def update_tabc_database(dry_run: bool = False, progress=print) -> dict:
    if os.path.exists(DB_PATH):
        with open(DB_PATH, "r", encoding="utf-8") as f:
            existing = json.load(f)
    else:
        existing = []  # 本機尚無主資料庫（例如全新 clone 後第一次執行）：視為首次建立，走全量匯入

    fetched = fetch_all_from_tabc(progress=progress)
    if not fetched:
        raise RuntimeError("本次未從 TABC 官網抓取到任何資料，可能是網路無法連線或來源網站結構已變動；為避免誤刪，已中止且未修改任何檔案。")

    merged, diff = merge_database(existing, fetched)
    diff["dryRun"] = dry_run
    diff["bootstrap"] = len(existing) == 0
    diff["timestamp"] = datetime.datetime.now().isoformat()

    if dry_run:
        return diff

    tmp_path = DB_PATH + ".tmp"
    with open(tmp_path, "w", encoding="utf-8") as f:
        json.dump(merged, f, ensure_ascii=False, indent=2)
    os.replace(tmp_path, DB_PATH)

    diff["metaWritten"] = _write_db_meta(diff["timestamp"], len(merged), diff["bootstrap"])
    diff["showcaseSynced"] = _sync_showcase_html(merged)
    return diff


def _write_db_meta(fetched_at: str, record_count: int, bootstrap: bool) -> bool:
    """把本次「真的連線抓取並寫入主資料庫」的時間戳持久化到旁生檔，供 /GM_import 讀回計算資料年齡。
    只有真實寫入路徑會呼叫：--dry-run 不寫（沒有改動資料庫），--resync-html 也不寫（沒有連線抓取，
    資料年齡沒有變新）。寫入失敗時回傳 False 而不拋例外——時間戳是輔助資訊，不該讓一次成功的
    資料庫更新因為旁生檔寫不出來而失敗；讀取端（database_freshness）本來就有檔案 mtime 的退路。"""
    meta = {
        "fetchedAt": fetched_at,
        "recordCount": record_count,
        "bootstrap": bootstrap,
        "source": "GM_update_tabc_database.py",
    }
    tmp_path = DB_META_PATH + ".tmp"
    try:
        with open(tmp_path, "w", encoding="utf-8") as f:
            json.dump(meta, f, ensure_ascii=False, indent=2)
        os.replace(tmp_path, DB_META_PATH)
        return True
    except OSError:
        return False


def resync_html_only(progress=print) -> dict:
    """不連線 TABC，只用現有本機 tabc_master_database.json（不存在則視為空資料庫）+
    最新的 assets/green-material-showcase.template.html 重新產生展示頁。
    用於 git pull 到樣板更新後，快速套用最新 UI，不必等 1~3 分鐘的即時網站抓取。"""
    if os.path.exists(DB_PATH):
        with open(DB_PATH, "r", encoding="utf-8") as f:
            existing = json.load(f)
    else:
        existing = []
    progress(f"本機資料庫現有 {len(existing)} 筆，僅重新產生展示頁（不連線 TABC 官網）...")
    synced = _sync_showcase_html(existing)
    return {
        "resyncOnly": True,
        "showcaseSynced": synced,
        "recordCount": len(existing),
        "timestamp": datetime.datetime.now().isoformat(),
    }


if __name__ == "__main__":
    args = sys.argv[1:]
    if "--resync-html" in args:
        result = resync_html_only()
    else:
        is_dry_run = "--dry-run" in args
        result = update_tabc_database(dry_run=is_dry_run)
    print(json.dumps(result, ensure_ascii=False, indent=2))
