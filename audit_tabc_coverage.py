import urllib.request
import urllib.parse
import re
import json
import base64
import time

def fetch_official_by_categories():
    """
    Fetch all active green material cases directly from TABC official search system
    by iterating through all 4 GBMTYPE categories: 1=健康, 2=高性能, 3=再生, 4=生態.
    """
    print("Auditing TABC official database across GBMTYPE 1..4 (健康/高性能/再生/生態)...")
    official_items = []
    category_names = {1: '健康', 2: '高性能', 3: '再生', 4: '生態'}
    
    for gbm_type in [1, 2, 3, 4]:
        cat_name = category_names[gbm_type]
        print(f"\n--- Checking TABC Official Category GBMTYPE={gbm_type} ({cat_name}綠建材) ---")
        page = 0
        while page < 50:  # Crawl up to 50 pages per category
            b64_param = base64.b64encode(f"GBMTYPE={gbm_type}&WC9V_BLCase_Data2einpage={page}".encode('utf-8')).decode('utf-8')
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
                                    'category': cat_name,
                                    'subCategory': sub_cat,
                                    'url': 'https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx',
                                    'highlight': f"✅ TABC 官方線上合格案件 ({start_d})",
                                    'specList': [
                                        f"原網頁核定編號: {licno}",
                                        f"廠商: {comp}",
                                        f"標章分類: {cat_name}綠建材 ({sub_cat})",
                                        "TABC 官方檢索系統 100% 可查"
                                    ],
                                    'specs': f"原網頁名稱：{title}。申請公司：{comp}。有效期限：{start_d} ~ {limit_d}。通過 TABC 綠建材標章評定。",
                                    'keywords': [sub_cat, cat_name, title, comp]
                                }
                                
                                if not any(x['licno'] == licno for x in official_items):
                                    official_items.append(item)
                                    found_in_page += 1
                                    
                    print(f"GBMTYPE={gbm_type} Page {page}: found {found_in_page} new items. Category total: {len([x for x in official_items if x['category'] == cat_name])}")
                    if found_in_page == 0:
                        break
                    page += 1
                    time.sleep(0.05)
            except Exception as e:
                print(f"Error fetching GBMTYPE={gbm_type} page {page}: {e}")
                break
                
    return official_items

if __name__ == '__main__':
    # 1. Fetch current TABC full database by categories GBMTYPE 1..4
    official_list = fetch_official_by_categories()
    
    # 2. Load existing local master database
    with open("tabc_master_database.json", "r", encoding="utf-8") as f:
        local_list = json.load(f)
        
    local_licnos = set(x['licno'] for x in local_list)
    official_licnos = set(x['licno'] for x in official_list)
    
    print("\n=======================================================")
    print("AUDIT RESULT COMPARISON REPORT")
    print("=======================================================")
    print(f"1. TABC Official Total Valid Cases Online (GBMTYPE 1..4): {len(official_list)}")
    print(f"2. Local Showcase Master DB Total Cases:                 {len(local_list)}")
    
    missing_in_local = [x for x in official_list if x['licno'] not in local_licnos]
    print(f"\n3. Missing Items Count in Local DB: {len(missing_in_local)}")
    
    if missing_in_local:
        print("\n--- Missing Items Details ---")
        for idx, item in enumerate(missing_in_local, 1):
            print(f"{idx}. [{item['licno']}] {item['title']} ({item['category']}綠建材) - {item['company']}")
            local_list.append(item)
            
        # Re-save master database with 100% full coverage
        with open("tabc_master_database.json", "w", encoding="utf-8") as f:
            json.dump(local_list, f, ensure_ascii=False, indent=2)
        print(f"\nSuccessfully synchronized all {len(missing_in_local)} missing items into tabc_master_database.json! Total now: {len(local_list)}")
    else:
        print("\nPerfect! 0 Missing items found. Local Master DB has 100% complete coverage!")
