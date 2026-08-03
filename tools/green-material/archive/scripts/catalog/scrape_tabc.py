import urllib.request
import urllib.parse
import re
import json

def fetch_tabc_items(keyword):
    all_items = []
    page = 1
    
    # TABC Uses pagination with EinB64
    # Page 1: EinB64=V0M5Vl9CTENhc2VfRGF0YWVpbnBhZ2U9MA (page 0 in parameter = page 1)
    # Page 2: EinB64=V0M5Vl9CTENhc2VfRGF0YWVpbnBhZ2U9MQ (page 1 in parameter = page 2)
    import base64
    
    while page <= 10:
        b64_str = base64.b64encode(f"WC9V_BLCase_Dataeinpage={page-1}".encode('utf-8')).decode('utf-8')
        url = f"https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?GBM_Name={urllib.parse.quote(keyword)}&EinB64={b64_str}"
        req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
        
        try:
            with urllib.request.urlopen(req) as resp:
                html = resp.read().decode('utf-8')
                
                # Find all table rows
                rows = re.findall(r'<TR>(.*?)</TR>', html, re.DOTALL | re.IGNORECASE)
                found_in_page = 0
                
                for r in rows:
                    if 'GBM' in r and ('openLargeModal' in r or 'openImageModal' in r):
                        licno_m = re.search(r'(GBM\d+)', r)
                        title_m = re.search(r'openLargeModal\([^)]*\)">([^<]+)</a>', r)
                        
                        # Company is in text cell
                        # Extract all text in <span>
                        spans = re.findall(r'<span[^>]*>(.*?)</span>', r, re.DOTALL)
                        clean_spans = [re.sub(r'<[^>]+>', '', s).strip() for s in spans if s.strip()]
                        
                        dates = re.findall(r'\d{2,3}/\d{2}/\d{2}', r)
                        img_m = re.search(r"src=['\"](\./Object/ProductImages/[^'\"]+)['\"]", r)
                        
                        if licno_m and title_m:
                            licno = licno_m.group(1)
                            title = title_m.group(1).strip()
                            company = clean_spans[2] if len(clean_spans) >= 3 else ''
                            start_date = dates[0] if len(dates) >= 1 else ''
                            end_date = dates[1] if len(dates) >= 2 else ''
                            img_url = 'https://tabcmgr.hopto.org/mgr' + img_m.group(1)[1:] if img_m else ''
                            
                            item = {
                                'licno': licno,
                                'title': title,
                                'company': company,
                                'period': f"{start_date} ~ {end_date}",
                                'img': img_url,
                                'category': '健康',
                                'subCategory': '牆壁類',
                                'url': f"https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx"
                            }
                            
                            if not any(x['licno'] == licno for x in all_items):
                                all_items.append(item)
                                found_in_page += 1
                
                if found_in_page == 0:
                    break
                page += 1
        except Exception as e:
            print("Fetch error:", e)
            break
            
    return all_items

if __name__ == '__main__':
    items = fetch_tabc_items("牆")
    print(f"Scraped {len(items)} real items for 牆:")
    with open("scraped_wall_items.json", "w", encoding="utf-8") as f:
        json.dump(items, f, ensure_ascii=False, indent=2)
    print("Saved to scraped_wall_items.json")
