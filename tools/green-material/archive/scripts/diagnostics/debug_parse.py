import re
import json

with open("raw_tabc_search.html", "r", encoding="utf-8") as f:
    html = f.read()

# Normalize case for tags
rows = re.findall(r'<tr[^>]*>(.*?)</tr>', html, re.DOTALL | re.IGNORECASE)
print(f"Found TR count: {len(rows)}")

items = []
for r in rows:
    if 'GBM' in r and 'openLargeModal' in r:
        licno_m = re.search(r'GBM\d+', r)
        title_m = re.search(r'openLargeModal\([^)]+\)>([^<]+)</a>', r)
        comp_m = re.search(r'CLASS="Default">\s*<span[^>]*>\s*([^<]+?)\s*</span>', r, re.IGNORECASE | re.DOTALL)
        dates = re.findall(r'\d{2,3}/\d{2}/\d{2}', r)
        img_m = re.search(r"src='(\./Object/ProductImages/[^']*)'", r)
        
        if licno_m and title_m:
            licno = licno_m.group(0)
            title = title_m.group(1).strip()
            company = comp_m.group(1).strip() if comp_m else "合格廠商"
            start_d = dates[0] if len(dates) >= 1 else ""
            end_d = dates[1] if len(dates) >= 2 else ""
            img_path = "https://tabcmgr.hopto.org/mgr" + img_m.group(1)[1:] if img_m else ""
            
            items.append({
                "licno": licno,
                "title": title,
                "company": company,
                "period": f"{start_d} ~ {end_d}",
                "img": img_path
            })

print(f"Extracted items count: {len(items)}")
for it in items:
    print(it)
