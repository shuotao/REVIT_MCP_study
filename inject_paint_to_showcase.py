import json
import re

# Load floor items and paint items
with open('real_floor_all_data.json', 'r', encoding='utf-8') as f:
    floor_items = json.load(f)

with open('real_paint_all_data.json', 'r', encoding='utf-8') as f:
    paint_items = json.load(f)

# Combine both list
combined = floor_items + paint_items
print(f"Total combined materials: {len(combined)} (Floor: {len(floor_items)}, Paint: {len(paint_items)})")

# Format for JS insertion
js_items = []
for it in combined:
    js_item = {
        "licno": it['licno'],
        "title": it['title'],
        "company": it['company'],
        "period": it['period'],
        "img": it['img'],
        "category": it['category'],
        "subCategory": it['subCategory'],
        "url": it['url'],
        "highlight": f"✅ TABC 官方線上合格案件 ({it['period'].split('~')[0].strip()})",
        "specList": [
            f"原網頁核定編號: {it['licno']}",
            f"廠商: {it['company']}",
            "TABC 官方檢索系統可查"
        ],
        "specs": f"原網頁名稱：{it['title']}。申請公司：{it['company']}。有效期限：{it['period']}。通過 TABC 綠建材標章評定。",
        "keywords": ["塗料", "漆", "油漆", "水泥漆", "乳膠漆", "隔熱塗料", "地", "地板", "地坪", "木地板", "地磚"] if it['subCategory'] == '塗料類' else ["地", "地板", "地坪", "木地板", "地磚", "鋪面", "棧板"]
    }
    js_items.append(js_item)

# Read HTML file
with open('assets/green-material-showcase.html', 'r', encoding='utf-8') as f:
    html_content = f.read()

# Replace tabcDatabase content
new_js_data = "const tabcDatabase = " + json.dumps(js_items, ensure_ascii=False, indent=2) + ";"

# Use regex to replace tabcDatabase definition
updated_html = re.sub(
    r'const tabcDatabase = \[.*?\];',
    new_js_data,
    html_content,
    flags=re.DOTALL
)

# Make sure filterMaterials handles "塗料", "漆", "油漆", "水泥漆", "乳膠漆"
updated_html = updated_html.replace(
    "if (haystack.includes(rawQuery) || rawQuery === '地板' || rawQuery === '地') {",
    "if (haystack.includes(rawQuery) || ['地板', '地', '塗料', '漆', '油漆', '水泥漆', '乳膠漆'].some(k => rawQuery.includes(k) || k.includes(rawQuery))) {"
)

with open('assets/green-material-showcase.html', 'w', encoding='utf-8') as f:
    f.write(updated_html)

print("Successfully injected all paint and floor green materials into assets/green-material-showcase.html!")
