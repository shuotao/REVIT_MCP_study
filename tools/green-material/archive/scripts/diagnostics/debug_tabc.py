import urllib.request
import urllib.parse
import re
import json

url = "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx?GBM_Name=" + urllib.parse.quote("牆", encoding='utf-8')
req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
with urllib.request.urlopen(req) as resp:
    html = resp.read().decode('utf-8')
    print("HTML length:", len(html))
    
    # Save raw html for inspection
    with open("raw_tabc_search.html", "w", encoding="utf-8") as f:
        f.write(html)
    print("Saved raw_tabc_search.html")
