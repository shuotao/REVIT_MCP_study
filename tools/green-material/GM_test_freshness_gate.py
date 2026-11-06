# -*- coding: utf-8 -*-
"""issue #128「綠建材資料新鮮度」三層的 RED/GREEN 驗收測試。

執行：  python tools/green-material/GM_test_freshness_gate.py
        （exit 0 = 全過；exit 1 = 有項目失敗，逐項印出 PASS/FAIL）

不需要網路、不需要 Revit、不讀寫本機的 tabc_master_database.json：整份測試在 tempfile 目錄的
合成 fixture 上跑，證號一律使用 GBM000000x 佔位符，不使用也不產生任何 TABC 真實資料
（見 QA/QC 閘門 3-5 與 tools/green-material/README.md 的第三方資料政策）。

涵蓋：
  第 1 層 讀回時間戳    —— meta 旁生檔／mtime 退路／資料庫不存在三種來源
  第 2 層 30 天門檻     —— RED 31 天要提醒、GREEN 29 天不提醒
  第 3 層 過期標章硬擋  —— RED 已過期要被標記進計畫與報告、GREEN 有效標章正常擬訂
"""
import datetime, json, os, sys, tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import GM_generate_revit_injection_plan as g

TMP = tempfile.mkdtemp(prefix="gm128-")
g.DB_PATH = os.path.join(TMP, "tabc_master_database.json")
g.DB_META_PATH = os.path.join(TMP, "tabc_master_database.meta.json")
g.SETS_FILE = os.path.join(TMP, "exported_material_sets.json")
g.PLAN_JSON = os.path.join(TMP, "Revit_Injection_Plan.json")
g.PLAN_REPORT = os.path.join(TMP, "report.md")

def roc(d):
    return f"{d.year - 1911}/{d.month:02d}/{d.day:02d}"

TODAY = datetime.date.today()
VALID_END = TODAY + datetime.timedelta(days=365)
EXPIRED_END = TODAY - datetime.timedelta(days=1)

def write_db(records):
    with open(g.DB_PATH, "w", encoding="utf-8") as f:
        json.dump(records, f, ensure_ascii=False)

def write_meta(days_ago):
    ts = (datetime.datetime.now() - datetime.timedelta(days=days_ago)).isoformat()
    with open(g.DB_META_PATH, "w", encoding="utf-8") as f:
        json.dump({"fetchedAt": ts, "recordCount": 2, "bootstrap": False,
                   "source": "test"}, f)

def rec(licno, end_date, title):
    return {"licno": licno, "title": title, "company": "測試公司",
            "category": "健康", "subCategory": "地板類",
            "period": f"{roc(TODAY - datetime.timedelta(days=30))} ~ {roc(end_date)}",
            "testItems": "TVOC逸散率", "cnsSpec": "CNS 測試", "qualifiedItems": "健康綠建材"}

VALID = rec("GBM0000000", VALID_END, "測試有效材料")
EXPIRED = rec("GBM0000001", EXPIRED_END, "測試過期材料")

results = []
def check(name, ok, detail=""):
    results.append((name, ok, detail))
    print(("PASS  " if ok else "FAIL  ") + name + (f"  -- {detail}" if detail else ""))

# ── 第 1 層：讀回時間戳 ──
write_db([VALID, EXPIRED]); write_meta(0)
f1 = g.database_freshness()
check("L1 meta 存在時讀回 fetchedAt / ageDays / recordCount",
      f1["status"] == "fresh" and f1["ageDays"] == 0 and f1["recordCount"] == 2
      and f1["fetchedAtSource"] == "meta", json.dumps(f1, ensure_ascii=False))

os.remove(g.DB_META_PATH)
f1b = g.database_freshness()
check("L1 meta 缺席時退回檔案 mtime 且明示來源",
      f1b["fetchedAtSource"] == "mtime" and f1b["ageDays"] == 0, str(f1b["fetchedAtSource"]))

db_bak = g.DB_PATH; g.DB_PATH = os.path.join(TMP, "does-not-exist.json")
f1c = g.database_freshness()
check("L1 資料庫不存在時不拋例外，給可執行下一步",
      f1c["status"] == "missing" and "/GM_update" in f1c["recommendation"], f1c["recommendation"])
g.DB_PATH = db_bak

# ── 第 2 層：30 天門檻（RED = 31 天要提醒，GREEN = 29 天不提醒）──
write_meta(31)
f2r = g.database_freshness()
check("L2 RED  31 天 → stale=True 且有提醒文字",
      f2r["stale"] is True and f2r["status"] == "stale" and "/GM_update" in f2r["recommendation"],
      f"ageDays={f2r['ageDays']}")
write_meta(29)
f2g = g.database_freshness()
check("L2 GREEN 29 天 → stale=False 且無提醒文字",
      f2g["stale"] is False and f2g["status"] == "fresh" and f2g["recommendation"] == "",
      f"ageDays={f2g['ageDays']}")

# ── 第 3 層：過期標章（RED = 過期要被標出來，GREEN = 有效不被標）──
write_db([EXPIRED]); write_meta(0)
plan_r = g.generate_injection_plan("過期測試", ["GBM0000001"], "test")
report_r = open(g.PLAN_REPORT, encoding="utf-8").read()
check("L3 RED  過期標章 → hasExpiredLicense=True + 清單 + 逐項 licenseExpired",
      plan_r["hasExpiredLicense"] is True
      and [e["licno"] for e in plan_r["expiredLicenses"]] == ["GBM0000001"]
      and plan_r["materialsMapping"][0]["licenseExpired"] is True,
      json.dumps(plan_r["expiredLicenses"], ensure_ascii=False))
check("L3 RED  報告書出現硬擋警語", "標章效期已過" in report_r and "不得靜默寫入" in report_r)

write_db([VALID])
plan_g = g.generate_injection_plan("有效測試", ["GBM0000000"], "test")
report_g = open(g.PLAN_REPORT, encoding="utf-8").read()
check("L3 GREEN 有效標章 → hasExpiredLicense=False，正常擬訂",
      plan_g["hasExpiredLicense"] is False and plan_g["expiredLicenses"] == []
      and plan_g["materialsMapping"][0]["licenseExpired"] is False
      and plan_g["totalMaterialsCount"] == 1)
check("L3 GREEN 報告書不出現硬擋警語，但仍有第 0 節",
      "標章效期已過" not in report_g and "## 0. 資料來源新鮮度與標章效期" in report_g)

# 計畫自帶新鮮度
check("L1 計畫 JSON 自帶 databaseFreshness",
      plan_g["databaseFreshness"]["status"] == "fresh"
      and plan_g["databaseFreshness"]["recordCount"] == 1)

# 舊資料庫 + 過期標章同時發生時兩者都要出現
write_db([EXPIRED]); write_meta(31)
plan_b = g.generate_injection_plan("雙紅測試", ["GBM0000001"], "test")
check("整合 舊資料庫與過期標章互不遮蔽",
      plan_b["databaseFreshness"]["stale"] is True and plan_b["hasExpiredLicense"] is True)

print()
bad = [r for r in results if not r[1]]
print(f"{len(results) - len(bad)} PASS / {len(bad)} FAIL")
sys.exit(1 if bad else 0)
