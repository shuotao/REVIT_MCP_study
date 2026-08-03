import json
import re

with open('assets/green-material-showcase.html', 'r', encoding='utf-8') as f:
    html = f.read()

# Update header badge and count
html = html.replace(
    '<div class="status-tag">\n    <span class="status-dot"></span>已連線 TABC 官方線上全量資料庫 (78 筆)\n  </div>',
    '<div class="status-tag">\n    <span class="status-dot"></span>已連線 TABC 官方線上全量資料庫 (146 筆：含 68 筆塗料 + 78 筆地坪)\n  </div>'
)

# Update Banner text
html = html.replace(
    '100% 完整抓取 <span class="query-notice-highlight" id="matchCount">0</span> 項搜尋「地板/地坪」出現的合格案件！',
    '100% 完整抓取 <span class="query-notice-highlight" id="matchCount">0</span> 項符合檢索關鍵字的 TABC 官方合格案件！'
)

# Update Hero section
html = html.replace(
    '<h1 class="hero-title">TABC 原網頁全量地板綠建材呈現</h1>',
    '<h1 class="hero-title">TABC 原網頁綠建材（塗料 / 油漆 / 地坪）動態檢索平台</h1>'
)

html = html.replace(
    '<p class="hero-sub">下述所有 78 筆核定編號、廠商名稱、品名與有效期限 100% 存在於 TABC 官方採購指南線上檢索系統。勾選產品即可自動轉化為 Revit 共享參數與元件 Type。</p>',
    '<p class="hero-sub">下述所有 146 筆（含 68 筆塗料與 78 筆地坪）核定編號、廠商名稱、品名與有效期限 100% 存在於 TABC 官方採購指南線上檢索系統。勾選產品即可自動轉化為 Revit 共享參數與元件 Type。</p>'
)

# Update Input field value to "塗料"
html = html.replace(
    '<input type="text" id="searchInput" class="search-input" value="地板" placeholder="🔍 輸入關鍵字（如：地板、地坪、地磚、塑木、橡膠...）" oninput="filterMaterials()">',
    '<input type="text" id="searchInput" class="search-input" value="塗料" placeholder="🔍 輸入關鍵字（如：塗料、漆、油漆、水泥漆、乳膠漆、地板...）" oninput="filterMaterials()">'
)

# Fix filterMaterials JS logic
old_js_filter = """  function filterMaterials() {
    const rawQuery = document.getElementById('searchInput').value.trim().toLowerCase();
    const grid = document.getElementById('materialsGrid');
    grid.innerHTML = '';

    if (!rawQuery) {
      document.getElementById('matchCount').innerText = '0';
      return;
    }

    let matchCount = 0;

    tabcDatabase.forEach(data => {
      const haystack = `${data.licno} ${data.title} ${data.company} ${data.subCategory} ${data.specs}`.toLowerCase();
      if (haystack.includes(rawQuery) || ['地板', '地', '塗料', '漆', '油漆', '水泥漆', '乳膠漆'].some(k => rawQuery.includes(k) || k.includes(rawQuery))) {"""

new_js_filter = """  function filterMaterials() {
    const rawQuery = document.getElementById('searchInput').value.trim().toLowerCase();
    const grid = document.getElementById('materialsGrid');
    grid.innerHTML = '';

    if (!rawQuery) {
      document.getElementById('matchCount').innerText = '0';
      return;
    }

    let matchCount = 0;

    tabcDatabase.forEach(data => {
      const haystack = `${data.licno} ${data.title} ${data.company} ${data.subCategory} ${data.specs}`.toLowerCase();
      const kwMatch = data.keywords ? data.keywords.some(k => k.toLowerCase().includes(rawQuery) || rawQuery.includes(k.toLowerCase())) : false;
      const categoryMatch = (rawQuery.includes('塗料') || rawQuery.includes('漆')) && data.subCategory === '塗料類';
      const floorMatch = (rawQuery.includes('地') || rawQuery.includes('板')) && data.subCategory === '地板類';

      if (haystack.includes(rawQuery) || kwMatch || categoryMatch || floorMatch) {"""

html = html.replace(old_js_filter, new_js_filter)

with open('assets/green-material-showcase.html', 'w', encoding='utf-8') as f:
    f.write(html)

print("HTML showcase updated successfully!")
