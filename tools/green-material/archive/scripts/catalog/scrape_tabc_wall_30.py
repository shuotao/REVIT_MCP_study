import urllib.request
import urllib.parse
import re
import json

base_url = "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx"
page_urls = [
    f"{base_url}?EinB64=R0JNX05hbWU9JUU3JTg5JTg2", # Page 1
    f"{base_url}?EinB64=R0JNX05hbWU9JUU3JTg5JTg2JldDOVZfQkxDYXNlX0RhdGEyZWlucGFnZT0x", # Page 2
    f"{base_url}?EinB64=R0JNX05hbWU9JUU3JTg5JTg2JldDOVZfQkxDYXNlX0RhdGEyZWlucGFnZT0y"  # Page 3
]

all_wall_items = []

for idx, url in enumerate(page_urls):
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    try:
        with urllib.request.urlopen(req) as resp:
            html = resp.read().decode('utf-8')
            
            # Find tr rows containing GBM
            rows = re.findall(r'<TR>(.*?)</TR>', html, re.DOTALL | re.IGNORECASE)
            for r in rows:
                if 'GBM' in r and 'openLargeModal' in r:
                    licno_m = re.search(r'(GBM\d+)', r)
                    title_m = re.search(r'openLargeModal\("[^"]*"\)>([^<]+)</a>', r)
                    comp_m = re.search(r'CLASS="Default">\s*<span[^>]*>\s*([^<]+?)\s*</span>', r, re.DOTALL)
                    dates = re.findall(r'\d{2,3}/\d{2}/\d{2}', r)
                    img_m = re.search(r"src='(\./Object/ProductImages/[^']*)'", r)
                    
                    if licno_m and title_m:
                        licno = licno_m.group(1)
                        title = title_m.group(1).strip()
                        comp = comp_m.group(1).strip() if comp_m else ''
                        start_d = dates[0] if len(dates) >= 1 else ''
                        limit_d = dates[1] if len(dates) >= 2 else ''
                        img_path = 'https://tabcmgr.hopto.org/mgr' + img_m.group(1)[1:] if img_m else ''
                        
                        item = {
                            'licno': licno,
                            'title': title,
                            'company': comp,
                            'period': f"{start_d} ~ {limit_d}",
                            'img': img_path,
                            'category': '健康' if '膠' not in title and '節能' not in title else ('高性能' if '節能' in title or '隔音' in title else '再生'),
                            'subCategory': '牆壁類',
                            'url': 'https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx'
                        }
                        
                        if not any(x['licno'] == licno for x in all_wall_items):
                            all_wall_items.append(item)
    except Exception as e:
        print(f"Error on page {idx+1}:", e)

print(f"Total scraped authentic items for 牆: {len(all_wall_items)}")
with open("all_wall_30_items.json", "w", encoding="utf-8") as f:
    json.dump(all_wall_items, f, ensure_ascii=False, indent=2)
print("Saved all_wall_30_items.json")
