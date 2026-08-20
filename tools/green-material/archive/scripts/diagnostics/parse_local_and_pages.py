import urllib.request
import re
import json

def parse_html_to_items(html):
    items = []
    tr_blocks = html.split('<TR>')
    for b in tr_blocks:
        if 'GBM' in b and 'openLargeModal' in b:
            licno_m = re.search(r'GBM\d+', b)
            title_m = re.search(r'openLargeModal\([^)]+\)>([^<]+)</a>', b)
            comp_m = re.search(r'CLASS="Default">\s*<span[^>]*>\s*([^<]+?)\s*</span>', b)
            dates = re.findall(r'\d{2,3}/\d{2}/\d{2}', b)
            img_m = re.search(r"src='(\./Object/ProductImages/[^']*)'", b)
            
            if licno_m and title_m:
                licno = licno_m.group(0)
                title = title_m.group(1).strip()
                company = comp_m.group(1).strip() if comp_m else "合格廠商"
                start_d = dates[0] if len(dates) >= 1 else ""
                end_d = dates[1] if len(dates) >= 2 else ""
                img_path = "https://tabcmgr.hopto.org/mgr" + img_m.group(1)[1:] if img_m else ""
                cat = "高性能" if ("節能" in title or "隔音" in title or "防火" in title) else "健康"
                
                item = {
                    "licno": licno,
                    "title": title,
                    "company": company,
                    "period": f"{start_d} ~ {end_d}",
                    "img": img_path,
                    "category": cat,
                    "subCategory": "牆壁類",
                    "url": "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx"
                }
                items.append(item)
    return items

all_items = []

# Page 1 from local file
with open("raw_tabc_search.html", "r", encoding="utf-8") as f:
    html1 = f.read()
    all_items.extend(parse_html_to_items(html1))

# Page 2 & 3 from URLs
urls = [
    "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?EinB64=R0JNX05hbWU9JUU3JTg5JTg2JldDOVZfQkxDYXNlX0RhdGEyZWlucGFnZT0x",
    "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?EinB64=R0JNX05hbWU9JUU3JTg5JTg2JldDOVZfQkxDYXNlX0RhdGEyZWlucGFnZT0y"
]

for url in urls:
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    try:
        with urllib.request.urlopen(req) as resp:
            html = resp.read().decode('utf-8')
            all_items.extend(parse_html_to_items(html))
    except Exception as e:
        print("Fetch error:", e)

# Deduplicate
unique_items = []
for item in all_items:
    if not any(x['licno'] == item['licno'] for x in unique_items):
        unique_items.append(item)

print(f"Total authentic items parsed for 牆: {len(unique_items)}")
for i, it in enumerate(unique_items, 1):
    print(f"{i}. [{it['licno']}] {it['title']} - {it['company']}")

with open("all_wall_30_items.json", "w", encoding="utf-8") as f:
    json.dump(unique_items, f, ensure_ascii=False, indent=2)
print("Saved to all_wall_30_items.json")
