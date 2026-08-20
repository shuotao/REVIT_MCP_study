import urllib.request
import urllib.parse
import re
import json

def fetch_all_floor_items():
    all_items = []
    # Fetch for keywords: 地板, 地, 地磚
    keywords = ["地板", "地"]
    
    for kw in keywords:
        page = 0
        while page < 5:
            import base64
            b64_param = base64.b64encode(f"GBM_Name={kw}&WC9V_BLCase_Data2einpage={page}".encode('utf-8')).decode('utf-8')
            url = f"https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?EinB64={b64_param}"
            
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
            try:
                with urllib.request.urlopen(req) as resp:
                    content = resp.read()
                    try:
                        html = content.decode('utf-8')
                    except:
                        html = content.decode('big5', errors='ignore')
                    
                    tr_blocks = html.split('<TR>')
                    found_in_page = 0
                    
                    for b in tr_blocks:
                        if 'GBM' in b and 'openLargeModal' in b:
                            licno_m = re.search(r'GBM\d+[\(（]?[^<]*', b)
                            title_m = re.search(r'openLargeModal\([^)]+\)[\'>\s]*([^<]+)</a>', b)
                            img_m = re.search(r"src=['\"](\./Object/ProductImages/[^'\"]+)['\"]", b)
                            dates = re.findall(r'\d{2,3}/\d{2}/\d{2}', b)
                            spans = re.findall(r'<span[^>]*>(.*?)</span>', b, re.DOTALL)
                            clean_spans = [re.sub(r'<[^>]+>', '', s).strip() for s in spans if re.sub(r'<[^>]+>', '', s).strip()]
                            
                            if licno_m and title_m:
                                licno = licno_m.group(0).strip()
                                title = title_m.group(1).strip()
                                comp = clean_spans[3] if len(clean_spans) > 3 else (clean_spans[-3] if len(clean_spans) >= 3 else 'TABC合格廠商')
                                start_d = dates[0] if len(dates) >= 1 else ''
                                limit_d = dates[1] if len(dates) >= 2 else ''
                                img_path = 'https://tabcmgr.hopto.org/mgr' + img_m.group(1)[1:] if img_m else ''
                                cat = '高性能' if ('節能' in title or '隔音' in title or '透水' in title) else ('再生' if '塑木' in title or '橡膠' in title or '再生' in title else '健康')
                                
                                item = {
                                    'licno': licno,
                                    'title': title,
                                    'company': comp,
                                    'period': f"{start_d} ~ {limit_d}",
                                    'img': img_path,
                                    'category': cat,
                                    'subCategory': '地板類',
                                    'url': 'https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx'
                                }
                                
                                if not any(x['licno'] == licno for x in all_items):
                                    all_items.append(item)
                                    found_in_page += 1
                                    
                    if found_in_page == 0:
                        break
                    page += 1
            except Exception as e:
                print(f"Error: {e}")
                break
                
    return all_items

floor_items = fetch_all_floor_items()
print(f"Total authentic floor items fetched from TABC online: {len(floor_items)}")

for idx, it in enumerate(floor_items, 1):
    print(f"{idx}. [{it['licno']}] {it['title']} - {it['company']}")

with open("real_floor_all_data.json", "w", encoding="utf-8") as f:
    json.dump(floor_items, f, ensure_ascii=False, indent=2)
