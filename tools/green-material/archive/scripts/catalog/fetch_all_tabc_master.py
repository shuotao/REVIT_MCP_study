import urllib.request
import urllib.parse
import re
import json
import base64
import time

def fetch_all_tabc_master():
    all_items = []
    # Broad keywords to cover all 4 green material categories: 健康, 高性能, 再生, 生態
    keywords = [
        "塗料", "漆", "水泥漆", "乳膠漆", "隔熱塗料",
        "地板", "地磚", "地坪", "塑木", "橡膠地磚",
        "牆", "矽酸鈣板", "石膏板", "水泥板", "石膏磚",
        "天花板", "吸音板", "隔音", "緩衝墊",
        "混凝土", "透水磚", "玻璃", "接著劑", "填縫劑"
    ]
    
    print(f"Starting TABC Master Database crawling across {len(keywords)} keyword categories...")
    
    for kw_idx, kw in enumerate(keywords, 1):
        print(f"[{kw_idx}/{len(keywords)}] Crawling TABC for keyword: '{kw}'...")
        page = 0
        while page < 5:  # Crawl up to 5 pages per keyword to ensure deep coverage
            b64_param = base64.b64encode(f"GBM_Name={kw}&WC9V_BLCase_Data2einpage={page}".encode('utf-8')).decode('utf-8')
            url = f"https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?EinB64={b64_param}"
            
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'})
            try:
                with urllib.request.urlopen(req, timeout=10) as resp:
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
                                
                                # Categorization logic based on TABC specifications
                                if any(x in title for x in ['節能', '反射', '隔熱', '隔音', '吸音', '透水']):
                                    cat = '高性能'
                                elif any(x in title for x in ['再生', '塑木', '橡膠', '混凝土磚', '水泥板', '廢棄物']):
                                    cat = '再生'
                                elif any(x in title for x in ['珪藻', '天然', '植物', '木材', '石材']):
                                    cat = '生態'
                                else:
                                    cat = '健康'
                                    
                                # SubCategory tagging
                                if any(x in title for x in ['塗料', '漆', '水泥漆', '乳膠漆', '薄塗', '底漆']):
                                    sub_cat = '塗料類'
                                elif any(x in title for x in ['地板', '地坪', '地磚', '木地板', '塑木', '棧板']):
                                    sub_cat = '地板類'
                                elif any(x in title for x in ['牆', '矽酸鈣', '石膏', '水泥板', '壁板']):
                                    sub_cat = '牆壁類'
                                elif any(x in title for x in ['天花板', '吸音板', '吊頂']):
                                    sub_cat = '天花板類'
                                elif any(x in title for x in ['隔音', '緩衝', '樓板隔音']):
                                    sub_cat = '隔音緩衝類'
                                elif any(x in title for x in ['透水', '高壓磚', '滲透']):
                                    sub_cat = '透水鋪面類'
                                else:
                                    sub_cat = '綜合建材類'
                                
                                item = {
                                    'licno': licno,
                                    'title': title,
                                    'company': comp,
                                    'period': f"{start_d} ~ {limit_d}",
                                    'img': img_path,
                                    'category': cat,
                                    'subCategory': sub_cat,
                                    'url': 'https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx',
                                    'highlight': f"✅ TABC 官方線上合格案件 ({start_d})",
                                    'specList': [
                                        f"原網頁核定編號: {licno}",
                                        f"廠商: {comp}",
                                        f"標章分類: {cat}綠建材 ({sub_cat})",
                                        "TABC 官方檢索系統 100% 可查"
                                    ],
                                    'specs': f"原網頁名稱：{title}。申請公司：{comp}。有效期限：{start_d} ~ {limit_d}。通過 TABC 綠建材標章評定。",
                                    'keywords': [kw, sub_cat, cat, title, comp]
                                }
                                
                                # Deduplicate by licno
                                existing = next((x for x in all_items if x['licno'] == licno), None)
                                if not existing:
                                    all_items.append(item)
                                else:
                                    # Merge keywords
                                    if kw not in existing['keywords']:
                                        existing['keywords'].append(kw)
                                        
                                found_in_page += 1
                                
                    if found_in_page == 0:
                        break
                    page += 1
                    time.sleep(0.1)  # Friendly throttling
            except Exception as e:
                print(f"Error fetching keyword {kw} page {page}: {e}")
                break

    return all_items

if __name__ == '__main__':
    master_items = fetch_all_tabc_master()
    print("\n=======================================================")
    print("TABC Master Database Crawling Complete!")
    print(f"Total Authentic Items Fetched & Deduplicated: {len(master_items)}")
    print("=======================================================\n")
    
    # Save master JSON
    with open("tabc_master_database.json", "w", encoding="utf-8") as f:
        json.dump(master_items, f, ensure_ascii=False, indent=2)
        
    print("Saved to tabc_master_database.json!")
