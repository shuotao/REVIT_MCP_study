import re

with open("raw_tabc_search.html", "r", encoding="utf-8") as f:
    html = f.read()

rows = re.findall(r'<tr[^>]*>(.*?)</tr>', html, re.DOTALL | re.IGNORECASE)
for i, r in enumerate(rows):
    if 'GBM' in r:
        print(f"--- ROW {i} ---")
        print(r)
