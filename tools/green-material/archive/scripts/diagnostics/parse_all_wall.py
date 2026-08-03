import urllib.request
import urllib.parse
import re
import json

page_urls = [
    "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?EinB64=R0JNX05hbWU9JUU3JTg5JTg2",
    "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?EinB64=R0JNX05hbWU9JUU3JTg5JTg2JldDOVZfQkxDYXNlX0RhdGEyZWlucGFnZT0x",
    "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?EinB64=R0JNX05hbWU9JUU3JTg5JTg2JldDOVZfQkxDYXNlX0RhdGEyZWlucGFnZT0y"
]

all_wall_items = []

for idx, url in enumerate(page_urls):
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    try:
        with urllib.request.urlopen(req) as resp:
            html = resp.read().decode('utf-8')
            
            # Split table rows
            tr_blocks = html.split('<TR>')
            for b in tr_blocks:
                if 'GBM' in b and 'openLargeModal' in b:
                    # Extract Licno
                    licno_m = re.search(r'GBM\d+', b)
                    # Extract Title inside openLargeModal
                    title_m = re.search(r'openLargeModal\([^)]+\)>([^<]+)</a>', b)
                    # Extract Company
                    comp_m = re.search(r'CLASS="Default">\s*<span[^>]*>\s*([^<]+?)\s*</span>', b)
                    # Extract Dates
                    dates = re.findall(r'\d{2,3}/\d{2}/\d{2}', b)
                    # Extract Image URL
                    img_m = re.search(r"src='(\./Object/ProductImages/[^']*)'", b)
                    
                    if licno_m and title_m:
                        licno = licno_m.group(0)
                        title = title_m.group(1).strip()
                        company = comp_m.group(1).strip() if comp_m else "原網頁合格廠商"
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
                        
                        if not any(x['licno'] == licno for x in all_wall_items):
                            all_wall_items.append(item)
    except Exception as e:
        print(f"Error page {idx}: {e}")

print(f"Total authentic items fetched for 牆: {len(all_wall_items)}")
with open("all_wall_30_items.json", "w", encoding="utf-8") as f:
    json.dump(all_wall_items, f, ensure_ascii=False, indent=2)

print("Successfully written to all_wall_30_items.json")
