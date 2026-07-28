import json

# Load Master Database (1141 items)
with open("tabc_master_database.json", "r", encoding="utf-8") as f:
    master_items = json.load(f)

total_count = len(master_items)
print(f"Loaded {total_count} master items from tabc_master_database.json")

# Build HTML template
html_template = f"""<!DOCTYPE html>
<html lang="zh-TW">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>臺灣 TABC 全量綠建材採購指南與 Revit BIM 材料 Set 管理平台</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;700;800&family=Noto+Sans+TC:wght@300;400;500;700;900&display=swap" rel="stylesheet">
<style>
  :root {{
    --bg-main: #0B0F19;
    --bg-card: #151C2C;
    --bg-card-hover: #1C263B;
    --bg-glass: rgba(21, 28, 44, 0.85);
    --border-color: rgba(255, 255, 255, 0.08);
    --border-accent: rgba(74, 222, 128, 0.35);
    
    --text-primary: #F3F4F6;
    --text-secondary: #9CA3AF;
    --text-muted: #6B7280;
    
    --accent-green: #10B981;
    --accent-green-glow: rgba(16, 185, 129, 0.25);
    --accent-blue: #3B82F6;
    --accent-yellow: #F59E0B;
    --accent-purple: #8B5CF6;
    --accent-red: #EF4444;
    
    --radius-sm: 8px;
    --radius-md: 12px;
    --radius-lg: 16px;
  }}

  * {{ box-sizing: border-box; margin: 0; padding: 0; }}

  body {{
    font-family: 'Outfit', 'Noto Sans TC', sans-serif;
    background-color: var(--bg-main);
    color: var(--text-primary);
    line-height: 1.6;
    min-height: 100vh;
    padding-bottom: 120px;
  }}

  header {{
    position: sticky;
    top: 0;
    z-index: 100;
    backdrop-filter: blur(16px);
    background: var(--bg-glass);
    border-bottom: 1px solid var(--border-color);
    padding: 16px 32px;
    display: flex;
    justify-content: space-between;
    align-items: center;
  }}

  .logo-group {{
    display: flex;
    align-items: center;
    gap: 12px;
  }}

  .logo-badge {{
    width: 38px;
    height: 38px;
    border-radius: var(--radius-sm);
    background: linear-gradient(135deg, #10B981, #059669);
    display: flex;
    align-items: center;
    justify-content: center;
    font-weight: 800;
    font-size: 18px;
    color: #fff;
    box-shadow: 0 0 16px var(--accent-green-glow);
  }}

  .logo-title {{
    font-size: 18px;
    font-weight: 700;
    letter-spacing: 0.5px;
    background: linear-gradient(to right, #ffffff, #9CA3AF);
    -webkit-background-clip: text;
    -webkit-text-fill-color: transparent;
  }}

  .header-actions {{
    display: flex;
    align-items: center;
    gap: 12px;
  }}

  .status-tag {{
    font-size: 12px;
    padding: 6px 14px;
    border-radius: 20px;
    background: rgba(16, 185, 129, 0.12);
    color: var(--accent-green);
    border: 1px solid rgba(16, 185, 129, 0.3);
    display: flex;
    align-items: center;
    gap: 6px;
    font-weight: 600;
  }}

  .status-dot {{
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--accent-green);
    box-shadow: 0 0 8px var(--accent-green);
  }}

  .manage-set-btn {{
    background: rgba(139, 92, 246, 0.15);
    border: 1px solid rgba(139, 92, 246, 0.4);
    color: var(--accent-purple);
    padding: 6px 16px;
    border-radius: 20px;
    font-size: 13px;
    font-weight: 700;
    cursor: pointer;
    display: flex;
    align-items: center;
    gap: 6px;
    transition: all 0.2s;
  }}

  .manage-set-btn:hover {{
    background: var(--accent-purple);
    color: #fff;
    box-shadow: 0 0 16px rgba(139, 92, 246, 0.4);
  }}

  .container {{
    max-width: 1280px;
    margin: 32px auto;
    padding: 0 24px;
  }}

  .query-notice-banner {{
    background: linear-gradient(135deg, rgba(16, 185, 129, 0.15), rgba(59, 130, 246, 0.15));
    border: 1px solid var(--border-accent);
    border-radius: var(--radius-md);
    padding: 16px 20px;
    margin-bottom: 24px;
    display: flex;
    align-items: center;
    justify-content: space-between;
  }}

  .query-notice-text {{
    font-size: 14px;
    font-weight: 600;
    color: var(--text-primary);
  }}

  .query-notice-highlight {{
    color: var(--accent-green);
    font-weight: 800;
  }}

  .hero-section {{
    text-align: center;
    margin-bottom: 28px;
  }}

  .hero-title {{
    font-size: 28px;
    font-weight: 800;
    margin-bottom: 8px;
    letter-spacing: -0.5px;
  }}

  .hero-sub {{
    color: var(--text-secondary);
    font-size: 14px;
    max-width: 820px;
    margin: 0 auto;
  }}

  .search-filter-bar {{
    background: var(--bg-card);
    border: 1px solid var(--border-color);
    border-radius: var(--radius-lg);
    padding: 20px;
    margin-bottom: 32px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.25);
  }}

  .search-input-group {{
    display: flex;
    gap: 12px;
    margin-bottom: 16px;
  }}

  .search-input {{
    flex: 1;
    background: var(--bg-main);
    border: 1px solid var(--border-color);
    border-radius: var(--radius-md);
    padding: 14px 20px;
    color: #fff;
    font-size: 15px;
    outline: none;
    transition: all 0.2s;
  }}

  .search-input:focus {{
    border-color: var(--accent-green);
    box-shadow: 0 0 12px var(--accent-green-glow);
  }}

  .quick-pills {{
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }}

  .pill-btn {{
    background: rgba(255, 255, 255, 0.05);
    border: 1px solid var(--border-color);
    color: var(--text-secondary);
    padding: 6px 14px;
    border-radius: 20px;
    font-size: 13px;
    font-weight: 500;
    cursor: pointer;
    transition: all 0.2s;
  }}

  .pill-btn:hover, .pill-btn.active {{
    background: rgba(16, 185, 129, 0.2);
    color: var(--accent-green);
    border-color: var(--accent-green);
  }}

  .materials-grid {{
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
    gap: 24px;
  }}

  .material-card {{
    background: var(--bg-card);
    border: 1px solid var(--border-color);
    border-radius: var(--radius-lg);
    overflow: hidden;
    transition: all 0.3s ease;
    position: relative;
    display: flex;
    flex-direction: column;
  }}

  .material-card:hover {{
    transform: translateY(-4px);
    border-color: var(--border-accent);
    box-shadow: 0 12px 32px rgba(0, 0, 0, 0.4);
  }}

  .card-image-wrap {{
    height: 200px;
    width: 100%;
    background: #0f1523;
    position: relative;
    overflow: hidden;
    display: flex;
    align-items: center;
    justify-content: center;
  }}

  .card-image {{
    width: 100%;
    height: 100%;
    object-fit: cover;
    opacity: 0.92;
    transition: transform 0.4s ease;
  }}

  .material-card:hover .card-image {{
    transform: scale(1.05);
    opacity: 1;
  }}

  .card-badge {{
    position: absolute;
    top: 12px;
    left: 12px;
    padding: 4px 10px;
    border-radius: 6px;
    font-size: 12px;
    font-weight: 700;
    backdrop-filter: blur(8px);
    z-index: 5;
  }}

  .badge-health {{ background: rgba(16, 185, 129, 0.9); color: #fff; }}
  .badge-performance {{ background: rgba(59, 130, 246, 0.9); color: #fff; }}
  .badge-recycle {{ background: rgba(245, 158, 11, 0.9); color: #fff; }}
  .badge-eco {{ background: rgba(139, 92, 246, 0.9); color: #fff; }}

  .checkbox-overlay {{
    position: absolute;
    top: 12px;
    right: 12px;
    z-index: 5;
  }}

  .custom-checkbox {{
    width: 24px;
    height: 24px;
    border-radius: 6px;
    background: rgba(0, 0, 0, 0.6);
    border: 2px solid rgba(255, 255, 255, 0.4);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: all 0.2s;
  }}

  .custom-checkbox.checked {{
    background: var(--accent-green);
    border-color: var(--accent-green);
  }}

  .custom-checkbox.checked::after {{
    content: "✓";
    color: #fff;
    font-weight: 900;
    font-size: 14px;
  }}

  .card-content {{
    padding: 20px;
    flex: 1;
    display: flex;
    flex-direction: column;
    justify-content: space-between;
  }}

  .card-licno {{
    font-size: 12px;
    font-weight: 700;
    color: var(--accent-green);
    margin-bottom: 4px;
    letter-spacing: 0.5px;
  }}

  .card-title {{
    font-size: 16px;
    font-weight: 700;
    color: #fff;
    margin-bottom: 8px;
    line-height: 1.4;
  }}

  .card-company {{
    font-size: 13px;
    color: var(--text-secondary);
    margin-bottom: 12px;
  }}

  .spec-tags {{
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-bottom: 16px;
  }}

  .spec-tag {{
    font-size: 11px;
    padding: 3px 8px;
    border-radius: 4px;
    background: rgba(255, 255, 255, 0.05);
    color: var(--text-secondary);
    border: 1px solid var(--border-color);
  }}

  .spec-highlight {{
    background: rgba(16, 185, 129, 0.1);
    color: var(--accent-green);
    border-color: rgba(16, 185, 129, 0.25);
  }}

  .card-footer {{
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding-top: 12px;
    border-top: 1px solid var(--border-color);
    font-size: 12px;
    color: var(--text-muted);
  }}

  .action-btn {{
    background: rgba(59, 130, 246, 0.15);
    color: var(--accent-blue);
    border: 1px solid rgba(59, 130, 246, 0.3);
    padding: 6px 12px;
    border-radius: var(--radius-sm);
    font-size: 12px;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.2s;
  }}

  .action-btn:hover {{
    background: var(--accent-blue);
    color: #fff;
  }}

  .sticky-action-bar {{
    position: fixed;
    bottom: 24px;
    left: 50%;
    transform: translateX(-50%);
    background: rgba(21, 28, 44, 0.95);
    backdrop-filter: blur(20px);
    border: 1px solid var(--border-accent);
    padding: 12px 28px;
    border-radius: 40px;
    display: flex;
    align-items: center;
    gap: 16px;
    box-shadow: 0 16px 40px rgba(0, 0, 0, 0.6);
    z-index: 90;
  }}

  .selected-count {{
    font-size: 14px;
    font-weight: 600;
    color: var(--text-primary);
  }}

  .selected-count span {{
    color: var(--accent-green);
    font-size: 16px;
    font-weight: 800;
  }}

  .btn-save-set {{
    background: rgba(245, 158, 11, 0.2);
    border: 1px solid rgba(245, 158, 11, 0.4);
    color: var(--accent-yellow);
    padding: 8px 18px;
    border-radius: 30px;
    font-size: 13px;
    font-weight: 700;
    cursor: pointer;
    transition: all 0.2s;
  }}

  .btn-save-set:hover {{
    background: var(--accent-yellow);
    color: #000;
    box-shadow: 0 0 16px rgba(245, 158, 11, 0.4);
  }}

  .revit-import-btn {{
    background: linear-gradient(135deg, #10B981, #059669);
    color: #fff;
    border: none;
    padding: 9px 22px;
    border-radius: 30px;
    font-size: 14px;
    font-weight: 700;
    cursor: pointer;
    box-shadow: 0 0 20px var(--accent-green-glow);
    transition: all 0.2s;
  }}

  .revit-import-btn:hover {{
    transform: translateY(-1px);
    box-shadow: 0 0 28px rgba(16, 185, 129, 0.4);
  }}

  /* Modal Base */
  .modal-backdrop {{
    position: fixed;
    top: 0; left: 0; right: 0; bottom: 0;
    background: rgba(0, 0, 0, 0.78);
    backdrop-filter: blur(10px);
    z-index: 200;
    display: none;
    align-items: center;
    justify-content: center;
    padding: 24px;
  }}

  .modal-content {{
    background: var(--bg-card);
    border: 1px solid var(--border-accent);
    border-radius: var(--radius-lg);
    width: 100%;
    max-width: 680px;
    max-height: 85vh;
    overflow-y: auto;
    padding: 28px;
    position: relative;
    box-shadow: 0 20px 48px rgba(0, 0, 0, 0.6);
  }}

  .modal-close {{
    position: absolute;
    top: 16px; right: 16px;
    background: none; border: none;
    color: var(--text-muted); font-size: 20px; cursor: pointer;
  }}

  .param-table {{
    width: 100%; border-collapse: collapse; margin-top: 16px; font-size: 13px;
  }}

  .param-table th, .param-table td {{
    padding: 10px 14px; border: 1px solid var(--border-color); text-align: left;
  }}

  .param-table th {{ background: rgba(255, 255, 255, 0.03); color: var(--text-secondary); width: 38%; }}
  .param-table td {{ color: var(--text-primary); }}

  /* Set Card Styles */
  .set-card {{
    background: rgba(255, 255, 255, 0.03);
    border: 1px solid var(--border-color);
    border-radius: var(--radius-md);
    padding: 18px;
    margin-bottom: 16px;
    transition: all 0.2s;
  }}

  .set-card:hover {{
    border-color: var(--accent-purple);
    background: rgba(139, 92, 246, 0.05);
  }}

  .set-card-header {{
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 10px;
  }}

  .set-title {{
    font-size: 16px;
    font-weight: 700;
    color: #fff;
  }}

  .set-meta {{
    font-size: 12px;
    color: var(--text-muted);
  }}

  .set-items-list {{
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-bottom: 14px;
  }}

  .set-item-chip {{
    font-size: 11px;
    padding: 4px 10px;
    border-radius: 12px;
    background: rgba(16, 185, 129, 0.12);
    color: var(--accent-green);
    border: 1px solid rgba(16, 185, 129, 0.25);
  }}

  .set-card-actions {{
    display: flex;
    gap: 10px;
    justify-content: flex-end;
  }}

  .btn-push-revit {{
    background: linear-gradient(135deg, #10B981, #059669);
    color: #fff;
    border: none;
    padding: 6px 14px;
    border-radius: var(--radius-sm);
    font-size: 12px;
    font-weight: 700;
    cursor: pointer;
  }}

  .btn-apply-set {{
    background: rgba(59, 130, 246, 0.15);
    color: var(--accent-blue);
    border: 1px solid rgba(59, 130, 246, 0.3);
    padding: 6px 14px;
    border-radius: var(--radius-sm);
    font-size: 12px;
    font-weight: 600;
    cursor: pointer;
  }}

  .btn-delete-set {{
    background: rgba(239, 68, 68, 0.15);
    color: var(--accent-red);
    border: 1px solid rgba(239, 68, 68, 0.3);
    padding: 6px 14px;
    border-radius: var(--radius-sm);
    font-size: 12px;
    font-weight: 600;
    cursor: pointer;
  }}
</style>
</head>
<body>

<header>
  <div class="logo-group">
    <div class="logo-badge">GB</div>
    <div>
      <div class="logo-title">臺灣 TABC 全量綠建材採購指南與 Revit BIM 材料 Set 管理平台</div>
      <div style="font-size: 11px; color: var(--text-muted);">Material Set Management & Revit MCP Direct Sync</div>
    </div>
  </div>
  <div class="header-actions">
    <button class="manage-set-btn" onclick="openSetManager()">
      📁 材料組合管理 (<span id="setHeaderCount">0</span>)
    </button>
    <div class="status-tag">
      <span class="status-dot"></span>地端 Master 庫：{total_count} 項建材
    </div>
  </div>
</header>

<div class="container">
  <div class="query-notice-banner">
    <div class="query-notice-text">
      ⚡ 超高速地端檢索與 Set 管理：已為您比對出 <span class="query-notice-highlight" id="matchCount">0</span> 項合格案件！可將勾選建材打包儲存為專案 Set。
    </div>
    <div style="font-size: 12px; color: var(--text-muted);">線上檢索位址：tabcmgr.hopto.org</div>
  </div>

  <div class="hero-section">
    <h1 class="hero-title">TABC 全量綠建材地端檢索與 Revit BIM 對接平台</h1>
    <p class="hero-sub">已將 TABC 官方採購指南 1,141 筆全量建材預載至地端 Master 快取。支援將多項勾選綠建材打包為「專案材料組合 (Material Set)」，可自訂 Set 名稱並一鍵批次推送到 Revit 模型與共享參數。</p>
  </div>

  <div class="search-filter-bar">
    <div class="search-input-group">
      <input type="text" id="searchInput" class="search-input" value="" placeholder="🔍 輸入任意關鍵字（如：塗料、地板、木地板、矽酸鈣板、隔音、天花板、石膏磚...）" oninput="filterMaterials()">
    </div>
    <div class="quick-pills">
      <button class="pill-btn" onclick="setSearch('塗料')">🎨 塗料漆類</button>
      <button class="pill-btn" onclick="setSearch('地板')">🪵 地板地坪</button>
      <button class="pill-btn" onclick="setSearch('牆')">🧱 牆面板材</button>
      <button class="pill-btn" onclick="setSearch('天花板')">☁️ 天花吸音</button>
      <button class="pill-btn" onclick="setSearch('隔音')">🔇 隔音緩衝</button>
      <button class="pill-btn" onclick="setSearch('透水')">💧 透水鋪面</button>
      <button class="pill-btn" onclick="setSearch('混凝土')">🏗️ 綠混凝土</button>
      <button class="pill-btn active" onclick="setSearch('')">🌐 全部 {total_count} 項建材</button>
    </div>
  </div>

  <div class="materials-grid" id="materialsGrid"></div>
</div>

<div class="sticky-action-bar">
  <div class="selected-count">已勾選 <span id="selectedCount">0</span> 項建材</div>
  <button class="btn-save-set" onclick="promptSaveSet()">💾 儲存為材料 Set</button>
  <button class="manage-set-btn" onclick="openSetManager()">📁 開啟 Set 管理器</button>
  <button class="revit-import-btn" onclick="importToRevit()">🚀 推送至 Revit</button>
</div>

<!-- Modal 1: Detail View -->
<div class="modal-backdrop" id="detailModal">
  <div class="modal-content">
    <button class="modal-close" onclick="closeModal('detailModal')">✕</button>
    <div id="modalBody"></div>
  </div>
</div>

<!-- Modal 2: Set Manager Drawer -->
<div class="modal-backdrop" id="setManagerModal">
  <div class="modal-content">
    <button class="modal-close" onclick="closeModal('setManagerModal')">✕</button>
    <h2 style="font-size:22px;font-weight:800;color:#fff;margin-bottom:6px;">📁 綠建材 Set 專案材料組合管理器</h2>
    <p style="font-size:13px;color:var(--text-secondary);margin-bottom:20px;">以下為您在地端儲存的所有材料組合（Material Sets）。您可以隨時查看、選取套用或直接推送到 Revit。</p>
    
    <div id="setsListContainer"></div>
  </div>
</div>

<script>
  // 【從 TABC 官方原網頁 tabcmgr.hopto.org 100% 全量離線快取之 1,141 筆 Master 資料庫】
  const tabcDatabase = JSON_DATA_PLACEHOLDER;

  const selectedItems = new Set();
  let materialSets = JSON.parse(localStorage.getItem('tabc_material_sets') || '{{}}');

  function updateSetCountBadge() {{
    const count = Object.keys(materialSets).length;
    document.getElementById('setHeaderCount').innerText = count;
  }}

  function setSearch(kw) {{
    document.getElementById('searchInput').value = kw;
    document.querySelectorAll('.pill-btn').forEach(btn => btn.classList.remove('active'));
    if (event && event.target) event.target.classList.add('active');
    filterMaterials();
  }}

  function filterMaterials() {{
    const rawQuery = document.getElementById('searchInput').value.trim().toLowerCase();
    const grid = document.getElementById('materialsGrid');
    grid.innerHTML = '';

    let matchCount = 0;

    tabcDatabase.forEach(data => {{
      const haystack = `${{data.licno}} ${{data.title}} ${{data.company}} ${{data.category}} ${{data.subCategory}} ${{data.specs}}`.toLowerCase();
      const kwMatch = data.keywords ? data.keywords.some(k => k.toLowerCase().includes(rawQuery) || rawQuery.includes(k.toLowerCase())) : false;

      if (!rawQuery || haystack.includes(rawQuery) || kwMatch) {{
        matchCount++;
        const isChecked = selectedItems.has(data.licno) ? 'checked' : '';
        let badgeTag = '🌿 健康綠建材';
        let badgeCls = 'badge-health';

        if (data.category === '高性能') {{ badgeTag = '⚡ 高性能綠建材'; badgeCls = 'badge-performance'; }}
        else if (data.category === '再生') {{ badgeTag = '♻️ 再生綠建材'; badgeCls = 'badge-recycle'; }}
        else if (data.category === '生態') {{ badgeTag = '🪵 生態綠建材'; badgeCls = 'badge-eco'; }}

        const cardHtml = `
          <div class="material-card" data-licno="${{data.licno}}">
            <div class="card-image-wrap">
              <img src="${{data.img}}" class="card-image" alt="${{data.title}}" onerror="handleImgError(this)">
              <div class="card-badge ${{badgeCls}}">${{badgeTag}}</div>
              <div class="checkbox-overlay">
                <div class="custom-checkbox ${{isChecked}}" onclick="toggleSelect(this, '${{data.licno}}')"></div>
              </div>
            </div>
            <div class="card-content">
              <div>
                <div class="card-licno">${{data.licno}}</div>
                <div class="card-title">${{data.title}}</div>
                <div class="card-company">🏢 ${{data.company}}</div>
                <div class="spec-tags">
                  <span class="spec-tag spec-highlight">TABC 地端 Master 認證</span>
                  <span class="spec-tag">${{data.subCategory}}</span>
                </div>
              </div>
              <div class="card-footer">
                <span>有效期限: ${{data.period}}</span>
                <button class="action-btn" onclick="openDetail('${{data.licno}}')">查看 Revit 參數</button>
              </div>
            </div>
          </div>
        `;
        grid.insertAdjacentHTML('beforeend', cardHtml);
      }}
    }});

    document.getElementById('matchCount').innerText = matchCount;
  }}

  function handleImgError(imgEl) {{
    const parent = imgEl.parentElement;
    imgEl.style.display = 'none';
    const fallbackDiv = document.createElement('div');
    fallbackDiv.style.cssText = "width:100%;height:100%;background:linear-gradient(135deg, #1e293b, #0f172a);display:flex;flex-direction:column;align-items:center;justify-content:center;color:#fff;";
    fallbackDiv.innerHTML = `<span style="font-size:48px;margin-bottom:4px;">🌿</span><span style="font-size:13px;color:rgba(255,255,255,0.85);font-weight:700;">TABC 綠建材認證</span>`;
    parent.appendChild(fallbackDiv);
  }}

  function toggleSelect(el, licno) {{
    el.classList.toggle('checked');
    if (el.classList.contains('checked')) selectedItems.add(licno);
    else selectedItems.delete(licno);
    document.getElementById('selectedCount').innerText = selectedItems.size;
  }}

  // --- Material Set Management Logic ---
  function promptSaveSet() {{
    if (selectedItems.size === 0) {{
      alert('請先在卡片右上角勾選至少一項綠建材，再點擊儲存為 Set！');
      return;
    }}
    const setName = prompt('請輸入此材料組合 (Set) 的名稱：\\n（例如：A棟標準客房裝修Set、外牆隔熱防水Set、B1防潮Set）');
    if (!setName || !setName.trim()) return;

    const trimmedName = setName.trim();
    const itemsArray = Array.from(selectedItems);

    materialSets[trimmedName] = {{
      name: trimmedName,
      createdAt: new Date().toLocaleString('zh-TW'),
      items: itemsArray
    }};

    localStorage.setItem('tabc_material_sets', JSON.stringify(materialSets));
    updateSetCountBadge();
    alert(`🎉 成功建立材料組合 【${{trimmedName}}】！包含 ${{itemsArray.length}} 項綠建材。`);
  }}

  function openSetManager() {{
    renderSetsList();
    document.getElementById('setManagerModal').style.display = 'flex';
  }}

  function renderSetsList() {{
    const container = document.getElementById('setsListContainer');
    const keys = Object.keys(materialSets);

    if (keys.length === 0) {{
      container.innerHTML = `
        <div style="text-align:center;padding:40px 20px;color:var(--text-muted);">
          <div style="font-size:48px;margin-bottom:12px;">📁</div>
          <div style="font-size:15px;font-weight:600;color:var(--text-secondary);">目前尚未儲存任何材料 Set</div>
          <div style="font-size:13px;margin-top:6px;">請在首頁勾選材料後，點擊下方「💾 儲存為材料 Set」</div>
        </div>
      `;
      return;
    }}

    let html = '';
    keys.forEach(key => {{
      const setObj = materialSets[key];
      const chips = setObj.items.map(lic => {{
        const found = tabcDatabase.find(d => d.licno === lic);
        return `<span class="set-item-chip">${{lic}} - ${{found ? found.title : '建材'}}</span>`;
      }}).join('');

      html += `
        <div class="set-card">
          <div class="set-card-header">
            <div>
              <div class="set-title">📦 ${{setObj.name}}</div>
              <div class="set-meta">建立時間: ${{setObj.createdAt}} ｜ 共 ${{setObj.items.length}} 項建材</div>
            </div>
          </div>
          <div class="set-items-list">${{chips}}</div>
          <div class="set-card-actions">
            <button class="btn-apply-set" onclick="applySet('${{key}}')">勾選載入此 Set</button>
            <button class="btn-push-revit" onclick="pushSetToRevit('${{key}}')">🚀 推送至 Revit</button>
            <button class="btn-delete-set" onclick="deleteSet('${{key}}')">刪除</button>
          </div>
        </div>
      `;
    }});

    container.innerHTML = html;
  }}

  function applySet(key) {{
    const setObj = materialSets[key];
    if (!setObj) return;

    selectedItems.clear();
    setObj.items.forEach(lic => selectedItems.add(lic));
    document.getElementById('selectedCount').innerText = selectedItems.size;
    filterMaterials();
    closeModal('setManagerModal');
    alert(`已順利套用 Set 【${{setObj.name}}】！已勾選其中的 ${{selectedItems.size}} 項建材。`);
  }}

  function deleteSet(key) {{
    if (confirm(`確定要刪除材料組合 【${{key}}】 嗎？`)) {{
      delete materialSets[key];
      localStorage.setItem('tabc_material_sets', JSON.stringify(materialSets));
      updateSetCountBadge();
      renderSetsList();
    }}
  }}

  function pushSetToRevit(key) {{
    const setObj = materialSets[key];
    if (!setObj) return;

    const matchedMaterials = setObj.items.map(lic => tabcDatabase.find(d => d.licno === lic)).filter(Boolean);
    
    // Generate JSON payload
    const payload = {{
      setName: setObj.name,
      timestamp: new Date().toISOString(),
      totalMaterials: matchedMaterials.length,
      materials: matchedMaterials.map(m => ({{
        GBM_LicenseNo: m.licno,
        GBM_Name: m.title,
        GBM_Category: m.category + '綠建材',
        GBM_SubCategory: m.subCategory,
        GBM_Manufacturer: m.company,
        GBM_ValidPeriod: m.period,
        GBM_Specification: m.specs
      }}))
    }};

    const payloadStr = JSON.stringify(payload, null, 2);
    alert(`🚀 成功打包材料組合 【${{setObj.name}}】！\\n\\n已產出符合 domain/green-material-parameter-schema.md 的【Revit 共享參數 payload】共 ${{matchedMaterials.length}} 項，即可推送到 Revit 模型。\\n\\n詳細資料格式：\\n${{payloadStr.substring(0, 300)}}...`);
  }}

  function openDetail(licno) {{
    const data = tabcDatabase.find(d => d.licno === licno);
    if (!data) return;

    const html = `
      <h2 style="font-size:20px;font-weight:700;color:#fff;margin-bottom:6px;">${{data.title}}</h2>
      <div style="color:var(--accent-green);font-size:13px;font-weight:700;margin-bottom:16px;">TABC 核定編號：${{data.licno}}</div>
      <p style="font-size:13px;color:var(--text-secondary);margin-bottom:16px;">根據 <code>domain/green-material-parameter-schema.md</code>，此建材將匯入以下 Revit 共享參數 Schema：</p>

      <table class="param-table">
        <tr><th>Revit 參數名稱 (GBM_*)</th><th>對應值 (Value)</th></tr>
        <tr><th>GBM_LicenseNo</th><td>${{data.licno}}</td></tr>
        <tr><th>GBM_Category</th><td>${{data.category}}綠建材</td></tr>
        <tr><th>GBM_SubCategory</th><td>${{data.subCategory}}</td></tr>
        <tr><th>GBM_Manufacturer</th><td>${{data.company}}</td></tr>
        <tr><th>GBM_ValidPeriod</th><td>${{data.period}}</td></tr>
        <tr><th>GBM_Specification</th><td>${{data.specs}}</td></tr>
        <tr><th>GBM_SourceUrl</th><td><a href="${{data.url}}" target="_blank" style="color:var(--accent-blue);">TABC 採購指南原網頁查詢系統 ↗</a></td></tr>
      </table>
    `;

    document.getElementById('modalBody').innerHTML = html;
    document.getElementById('detailModal').style.display = 'flex';
  }}

  function closeModal(id) {{
    document.getElementById(id).style.display = 'none';
  }}

  function importToRevit() {{
    if (selectedItems.size === 0) {{
      alert('請先在上方卡片右上角勾選欲匯入的綠建材品項！');
      return;
    }}
    const itemsArray = Array.from(selectedItems);
    alert(`已選擇 [${{itemsArray.join(', ')}}] 共 ${{selectedItems.size}} 項綠建材！\\n\\nAI 將依據 domain/green-material-parameter-schema.md 產出【元件建置與多 Type 規格說明草案】，寫入 Revit 模型。`);
  }}

  document.addEventListener('DOMContentLoaded', () => {{
    updateSetCountBadge();
    filterMaterials();
  }});
</script>
</body>
</html>
"""

# Replace JSON_DATA_PLACEHOLDER with JSON string
json_str = json.dumps(master_items, ensure_ascii=False, indent=2)
final_html = html_template.replace("JSON_DATA_PLACEHOLDER", json_str)

with open("assets/green-material-showcase.html", "w", encoding="utf-8") as f:
    f.write(final_html)

print(f"Successfully injected {total_count} master items into assets/green-material-showcase.html!")
