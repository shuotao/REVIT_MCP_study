import urllib.request
import urllib.parse
import re
import json
import base64

def fetch_paint_items():
    all_items = []
    keywords = ["塗料", "漆", "油漆", "水泥漆", "乳膠漆"]
    
    for kw in keywords:
        page = 0
        while page < 5:
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
                                cat = '高性能' if ('節能' in title or '反射' in title or '隔熱' in title) else ('生態' if '珪藻' in title or '天然' in title or '植物' in title else '健康')
                                
                                item = {
                                    'licno': licno,
                                    'title': title,
                                    'company': comp,
                                    'period': f"{start_d} ~ {limit_d}",
                                    'img': img_path,
                                    'category': cat,
                                    'subCategory': '塗料類',
                                    'url': 'https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx',
                                    'highlight': f"✅ TABC 官方線上合格案件 ({start_d})",
                                    'specList': [
                                        f"原網頁核定編號: {licno}",
                                        f"廠商: {comp}",
                                        "TABC 官方檢索系統可查"
                                    ],
                                    'specs': f"原網頁名稱：{title}。申請公司：{comp}。有效期限：{start_d} ~ {limit_d}。通過 TABC 綠建材標章評定。",
                                    'keywords': ["塗料", "漆", "油漆", "水泥漆", "乳膠漆", "隔熱塗料", "木器漆"]
                                }
                                
                                if not any(x['licno'] == licno for x in all_items):
                                    all_items.append(item)
                                    found_in_page += 1
                                    
                    if found_in_page == 0:
                        break
                    page += 1
            except Exception as e:
                print(f"Error fetching page {page} for keyword {kw}: {e}")
                break
                
    return all_items

if __name__ == '__main__':
    items = fetch_paint_items()
    print(f"Total authentic paint items fetched from TABC online: {len(items)}")
    for idx, it in enumerate(items, 1):
        print(f"{idx}. [{it['licno']}] {it['title']} ({it['category']}綠建材) - {it['company']}")
    
    with open("real_paint_all_data.json", "w", encoding="utf-8") as f:
        json.dump(items, f, ensure_ascii=False, indent=2)
