import urllib.request
import re
import json

def fetch_page_items(url):
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    try:
        with urllib.request.urlopen(req) as resp:
            content = resp.read()
            # Try big5 or utf-8
            try:
                html = content.decode('utf-8')
            except:
                html = content.decode('big5', errors='ignore')
                
            items = []
            rows = re.findall(r'<TR>(.*?)</TR>', html, re.DOTALL | re.IGNORECASE)
            for r in rows:
                if 'GBM' in r:
                    licno_m = re.search(r'GBM\d+[\(（]?[^<]*', r)
                    # title link
                    title_m = re.search(r'openLargeModal\([^)]+\)[\'>\s]*([^<]+)</a>', r)
                    # image
                    img_m = re.search(r"src=['\"](\./Object/ProductImages/[^'\"]+)['\"]", r)
                    # dates
                    dates = re.findall(r'\d{2,3}/\d{2}/\d{2}', r)
                    # spans
                    spans = re.findall(r'<span[^>]*>(.*?)</span>', r, re.DOTALL)
                    clean_spans = [re.sub(r'<[^>]+>', '', s).strip() for s in spans if re.sub(r'<[^>]+>', '', s).strip()]
                    
                    if licno_m:
                        licno = licno_m.group(0).strip()
                        title = title_m.group(1).strip() if title_m else (clean_spans[2] if len(clean_spans) > 2 else '綠建材產品')
                        comp = clean_spans[3] if len(clean_spans) > 3 else (clean_spans[-3] if len(clean_spans) >= 3 else 'TABC合格廠商')
                        start_d = dates[0] if len(dates) >= 1 else ''
                        limit_d = dates[1] if len(dates) >= 2 else ''
                        img_path = 'https://tabcmgr.hopto.org/mgr' + img_m.group(1)[1:] if img_m else ''
                        
                        cat = '高性能' if ('節能' in title or '隔音' in title or '防火' in title) else '健康'
                        
                        items.append({
                            'licno': licno,
                            'title': title,
                            'company': comp,
                            'period': f"{start_d} ~ {limit_d}",
                            'img': img_path,
                            'category': cat,
                            'subCategory': '牆壁類',
                            'url': 'https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx'
                        })
            return items
    except Exception as e:
        print(f"Error fetching {url}: {e}")
        return []

page_urls = [
    "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?EinB64=R0JNX05hbWU9JUU3JTg5JTg2",
    "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?EinB64=R0JNX05hbWU9JUU3JTg5JTg2JldDOVZfQkxDYXNlX0RhdGEyZWlucGFnZT0x",
    "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?EinB64=R0JNX05hbWU9JUU3JTg5JTg2JldDOVZfQkxDYXNlX0RhdGEyZWlucGFnZT0y"
]

all_items = []
for url in page_urls:
    res = fetch_page_items(url)
    all_items.extend(res)

# Deduplicate
unique_items = []
for item in all_items:
    if not any(x['licno'] == item['licno'] for x in unique_items):
        unique_items.append(item)

print(f"Total authentic wall items fetched: {len(unique_items)}")
with open("real_wall_30_data.json", "w", encoding="utf-8") as f:
    json.dump(unique_items, f, ensure_ascii=False, indent=2)

print("Saved to real_wall_30_data.json")
