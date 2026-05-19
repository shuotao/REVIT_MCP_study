(function () {
  function qs(s, r) { return (r || document).querySelector(s); }
  function qsa(s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); }

  // ============================================================
  // initTOC — 自動產生 + IntersectionObserver active + 已讀標記
  // ============================================================
  window.initTOC = function initTOC() {
    var toc = qs(".bim-toc");
    if (!toc) {
      var anchorsForToc = qsa(".bim-anchor[id]");
      if (anchorsForToc.length === 0) return;
      toc = document.createElement("aside");
      toc.className = "bim-toc";
      toc.innerHTML = '<div class="bim-toc-title">On this page</div>' + anchorsForToc.map(function (section, i) {
        var label = qs(".bim-section-title", section) || qs("h2", section) || section;
        var num = String(i + 1).padStart(2, "0");
        return '<a href="#' + section.id + '"><span class="bim-toc-counter">' + num + '</span>' + label.textContent.trim().replace(/\s+/g, " ").slice(0, 28) + "</a>";
      }).join("");
      document.body.appendChild(toc);
    }

    // 既有 TOC 沒有 counter 編號 → 自動補上
    qsa("a[href^='#']", toc).forEach(function (link, i) {
      if (!qs(".bim-toc-counter", link)) {
        var num = String(i + 1).padStart(2, "0");
        var span = document.createElement("span");
        span.className = "bim-toc-counter";
        span.textContent = num;
        link.insertBefore(span, link.firstChild);
      }
    });

    var links = qsa("a[href^='#']", toc);
    var sections = links.map(function (l) {
      return document.getElementById(l.getAttribute("href").slice(1));
    }).filter(Boolean);

    if (!("IntersectionObserver" in window) || sections.length === 0) return;
    var obs = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        links.forEach(function (link, i) {
          var match = link.getAttribute("href") === "#" + entry.target.id;
          link.classList.toggle("active", match);
          // 已 active 的這個之前的全部標 visited
          if (match) {
            for (var j = 0; j < i; j++) links[j].classList.add("visited");
          }
        });
      });
    }, { rootMargin: "-35% 0px -55% 0px", threshold: 0 });
    sections.forEach(function (s) { obs.observe(s); });
  };

  // ============================================================
  // initProgress — 右側 scroll 進度條
  // ============================================================
  window.initProgress = function initProgress() {
    if (qs(".bim-progress")) return;
    var rail = document.createElement("div");
    rail.className = "bim-progress";
    var fill = document.createElement("div");
    fill.className = "bim-progress-fill";
    rail.appendChild(fill);
    document.body.appendChild(rail);

    function update() {
      var doc = document.documentElement;
      var scrolled = doc.scrollTop || document.body.scrollTop;
      var height = (doc.scrollHeight || document.body.scrollHeight) - doc.clientHeight;
      var pct = height > 0 ? (scrolled / height) * 100 : 0;
      fill.style.height = pct + "%";
    }
    update();
    window.addEventListener("scroll", update, { passive: true });
    window.addEventListener("resize", update);
  };

  // ============================================================
  // initContext — sticky context bar 顯示「第幾節 / 章節名」
  // ============================================================
  window.initContext = function initContext() {
    var nav = qs(".bim-topnav");
    if (!nav) return;
    if (qs(".bim-context-bar")) return;

    var anchors = qsa(".bim-anchor[id]");
    if (anchors.length === 0) return;

    var bar = document.createElement("div");
    bar.className = "bim-context-bar";
    bar.innerHTML =
      '<div class="bim-context-step">' +
        '<span class="bim-context-num">00 / ' + String(anchors.length).padStart(2, "0") + '</span>' +
        '<span class="bim-context-name">頁首</span>' +
      '</div>' +
      '<a href="#" class="bim-context-back" data-back-top>↑ 回頁首</a>';
    nav.insertAdjacentElement("afterend", bar);

    var numEl = qs(".bim-context-num", bar);
    var nameEl = qs(".bim-context-name", bar);
    var backTop = qs("[data-back-top]", bar);
    if (backTop) {
      backTop.addEventListener("click", function (e) {
        e.preventDefault();
        window.scrollTo({ top: 0, behavior: "smooth" });
      });
    }

    if (!("IntersectionObserver" in window)) return;
    var obs = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        var idx = anchors.indexOf(entry.target);
        if (idx < 0) return;
        var title = qs(".bim-section-title", entry.target) || qs("h2", entry.target);
        if (numEl) numEl.textContent = String(idx + 1).padStart(2, "0") + " / " + String(anchors.length).padStart(2, "0");
        if (nameEl && title) nameEl.textContent = title.textContent.trim().replace(/\s+/g, " ").slice(0, 36);
      });
    }, { rootMargin: "-25% 0px -65% 0px", threshold: 0 });
    anchors.forEach(function (s) { obs.observe(s); });
  };

  // ============================================================
  // initFooterPreview — 把 data-prev/data-next 變成預覽卡
  // PAGE_META 內建所有站內頁的標題 + 描述，footer 直接讀
  // ============================================================
  var PAGE_META = {
    "index": { title: "BIM_MCP Hub", desc: "Reference / archive / latest 三層入口。" },
    "../index": { title: "BIM_MCP Hub", desc: "回站點首頁。" },
    "reference/philosophy-22-propositions": { title: "22 命題完整宣言", desc: "從「為什麼 MCP」到「會議場景」六群組。" },
    "reference/three-constitutions": { title: "三條憲法", desc: "Data Honesty / Domain Method / Passive Ready。" },
    "reference/industry-evidence": { title: "業界證據", desc: "100× / 70% / $15-25K + 三套成熟方法論。" },
    "reference/spectrum-decision": { title: "光譜決策框架", desc: "FAIL 之後的 A/B/C/D 四條路線。" },
    "reference/skills-index": { title: "Skills 索引（19）", desc: "編排層：何時觸發 / 什麼順序。" },
    "reference/domain-index": { title: "Domain 索引（35+）", desc: "知識層：法規 / SOP / lessons。" },
    "reference/deployment-guide": { title: "部署指南", desc: "Nice3point / Release.R{YY} / setup.ps1。" },
    "reference/troubleshooting": { title: "Troubleshooting", desc: "5 經典 + 4 個 5/18 demo 修復。" },
    "reference/contributor-template": { title: "Contributor Template", desc: "新增 Domain / Skill / Tool 的順序。" },
    "reference/architecture-v2": { title: "三層架構 V2", desc: "Skill / CLAUDE.md / Domain 分工。" },
    "philosophy-22-propositions": { title: "22 命題完整宣言", desc: "哲學中軸。" },
    "three-constitutions": { title: "三條憲法", desc: "資料誠實 / Domain method / 被動就緒。" },
    "industry-evidence": { title: "業界證據", desc: "100× / 70% / $15-25K。" },
    "spectrum-decision": { title: "光譜決策", desc: "A/B/C/D 框架。" },
    "skills-index": { title: "Skills 索引", desc: "19 個編排層 Skill。" },
    "domain-index": { title: "Domain 索引", desc: "40 個 SOP 知識。" },
    "deployment-guide": { title: "部署指南", desc: "多版本 build / DLL 部署。" },
    "troubleshooting": { title: "Troubleshooting", desc: "經典問題 + live fixes。" },
    "contributor-template": { title: "Contributor Template", desc: "雙寫流程：知識先、編排後。" },
    "architecture-v2": { title: "三層架構 V2", desc: "Skill / Domain / Tool。" }
  };

  window.initFooterPreview = function initFooterPreview() {
    var body = document.body;
    var prev = body.getAttribute("data-prev");
    var next = body.getAttribute("data-next");
    var footer = qs(".bim-footer");
    if (!footer || (!prev && !next)) return;
    if (qs(".bim-footer-preview")) return;

    function metaFor(path) {
      // 從 href 抽出 key
      var key = path.replace(/^(\.\.\/|\.\/)/, "").replace(/\.html$/, "");
      return PAGE_META[key] || { title: key, desc: "" };
    }

    function card(dir, href, meta) {
      var dirLabel = dir === "prev" ? "← Prev" : "Next →";
      return '<a class="bim-footer-preview-card ' + dir + '" href="' + href + '">' +
        '<div class="bim-footer-preview-dir">' + dirLabel + '</div>' +
        '<div class="bim-footer-preview-title">' + meta.title + '</div>' +
        '<div class="bim-footer-preview-desc">' + (meta.desc || "") + '</div>' +
      '</a>';
    }

    var wrap = document.createElement("div");
    wrap.className = "bim-footer-preview";
    var html = "";
    if (prev) html += card("prev", prev, metaFor(prev)); else html += '<div></div>';
    if (next) html += card("next", next, metaFor(next));
    wrap.innerHTML = html;
    footer.parentNode.insertBefore(wrap, footer);
  };

  // ============================================================
  // initKeyNav — Esc / ←→ / g+h
  // ============================================================
  window.initKeyNav = function initKeyNav(options) {
    options = options || {};
    var prev = options.prev || document.body.getAttribute("data-prev");
    var next = options.next || document.body.getAttribute("data-next");
    var home = options.home || "../index.html";
    var pendingG = false;

    document.addEventListener("keydown", function (event) {
      if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return;
      var tag = (event.target && event.target.tagName || "").toLowerCase();
      if (tag === "input" || tag === "textarea" || tag === "select") return;
      if (event.key === "Escape") {
        var close = qs("[data-drawer-close]");
        if (close) close.click();
        return;
      }
      if (event.key === "ArrowLeft" && prev) window.location.href = prev;
      if (event.key === "ArrowRight" && next) window.location.href = next;
      if (event.key.toLowerCase() === "g") {
        pendingG = true;
        window.setTimeout(function () { pendingG = false; }, 900);
        return;
      }
      if (pendingG && event.key.toLowerCase() === "h") window.location.href = home;
    });
  };

  // ============================================================
  // initDrawer — 強化版：支援 data-source（從 DOM 拉內容）或 data-body（內聯）
  // 自動建立 drawer shell + overlay 若不存在
  // ============================================================
  window.initDrawer = function initDrawer() {
    var drawer = qs(".bim-drawer");
    var overlay = qs(".bim-drawer-overlay");

    if (!drawer) {
      overlay = document.createElement("div");
      overlay.className = "bim-drawer-overlay";
      document.body.appendChild(overlay);

      drawer = document.createElement("aside");
      drawer.className = "bim-drawer";
      drawer.setAttribute("aria-hidden", "true");
      drawer.innerHTML =
        '<div class="bim-drawer-head">' +
          '<div>' +
            '<div class="bim-drawer-eyebrow" data-drawer-eyebrow>DETAIL</div>' +
            '<div class="bim-drawer-title" data-drawer-title>—</div>' +
          '</div>' +
          '<button class="bim-drawer-close" data-drawer-close aria-label="關閉">✕</button>' +
        '</div>' +
        '<div class="bim-drawer-body" data-drawer-body></div>' +
        '<div class="bim-drawer-foot">' +
          '<span>Esc 關閉 · ← / → 翻頁</span>' +
          '<a href="#" data-drawer-permalink>open as page ↗</a>' +
        '</div>';
      document.body.appendChild(drawer);
    }

    var titleEl = qs("[data-drawer-title]", drawer);
    var eyebrowEl = qs("[data-drawer-eyebrow]", drawer);
    var bodyEl = qs("[data-drawer-body]", drawer);
    var permaEl = qs("[data-drawer-permalink]", drawer);
    var closeBtn = qs("[data-drawer-close]", drawer);

    function open(opener) {
      var title = opener.getAttribute("data-title") || opener.textContent.trim();
      var eyebrow = opener.getAttribute("data-eyebrow") || "DETAIL";
      var src = opener.getAttribute("data-source");
      var body = opener.getAttribute("data-body");

      if (eyebrowEl) eyebrowEl.textContent = eyebrow;
      if (titleEl) titleEl.textContent = title;

      if (src) {
        var sourceEl = qs(src);
        if (sourceEl && bodyEl) bodyEl.innerHTML = sourceEl.innerHTML;
        if (permaEl) permaEl.setAttribute("href", src);
      } else if (body && bodyEl) {
        bodyEl.innerHTML = body;
        if (permaEl) permaEl.style.display = "none";
      }

      drawer.classList.add("open");
      overlay.classList.add("open");
      drawer.setAttribute("aria-hidden", "false");
      document.body.style.overflow = "hidden";

      // scroll drawer body to top
      if (bodyEl) bodyEl.scrollTop = 0;
    }

    function close() {
      drawer.classList.remove("open");
      overlay.classList.remove("open");
      drawer.setAttribute("aria-hidden", "true");
      document.body.style.overflow = "";
    }

    qsa("[data-drawer-open]").forEach(function (op) {
      op.addEventListener("click", function (e) {
        e.preventDefault();
        open(op);
      });
    });
    if (closeBtn) closeBtn.addEventListener("click", close);
    overlay.addEventListener("click", close);
  };

  // ============================================================
  // initSectionDrawers — 自動把 how-tech / empathy-quote /
  // further-reading / pitfalls 從 [data-collapse] section 抽出來
  // 變成 section 底部的抽屜觸發按鈕。讓每節只保留主要內容。
  // ============================================================
  window.initSectionDrawers = function initSectionDrawers() {
    var hidden = qs("#__section_drawer_templates");
    if (!hidden) {
      hidden = document.createElement("div");
      hidden.id = "__section_drawer_templates";
      hidden.style.display = "none";
      document.body.appendChild(hidden);
    }

    var COLLAPSE_RULES = [
      { sel: ".how-tech", label: "WHY / HOW / TECHNIQUE", short: "WHY / HOW", eyebrow: "TECHNICAL · WHY / HOW" },
      { sel: ".empathy-quote", label: "業界口訣", short: "業界口訣", eyebrow: "INSIDER LINGO · 共感句" },
      { sel: ".pitfalls", label: "踩雷警示", short: "踩雷", eyebrow: "PITFALLS · 警告" },
      { sel: ".further-reading", label: "深度閱讀", short: "深度閱讀", eyebrow: "FURTHER READING · 進階參考" }
    ];

    qsa(".bim-section[data-collapse]").forEach(function (section) {
      var sectionId = section.id || "sec-" + Math.random().toString(36).slice(2, 8);
      if (!section.id) section.id = sectionId;
      var sectionTitle = (qs(".bim-section-title", section) || qs("h2", section));
      var titlePrefix = sectionTitle ? sectionTitle.textContent.trim().replace(/\s+/g, " ").slice(0, 40) : sectionId;

      var triggers = [];

      COLLAPSE_RULES.forEach(function (rule) {
        var nodes = qsa(rule.sel, section);
        if (nodes.length === 0) return;

        var templateId = sectionId + "__" + rule.sel.replace(/\W/g, "");
        var tpl = document.createElement("div");
        tpl.id = templateId;
        nodes.forEach(function (node) {
          tpl.appendChild(node);  // 搬走（不是 clone）
        });
        hidden.appendChild(tpl);

        // 計數（適用於 further-reading 的 li 數量）
        var count = "";
        if (rule.sel === ".further-reading") {
          var items = qsa("li", tpl);
          if (items.length) count = '<span class="count">' + items.length + "</span>";
        }

        triggers.push({
          source: "#" + templateId,
          label: rule.short + count,
          title: titlePrefix + " · " + rule.label,
          eyebrow: rule.eyebrow
        });
      });

      if (triggers.length > 0) {
        var bar = document.createElement("div");
        bar.className = "section-drawer-bar";
        bar.innerHTML = triggers.map(function (t) {
          return '<button class="section-drawer-trigger" data-drawer-open data-source="' + t.source + '" data-title="' + t.title + '" data-eyebrow="' + t.eyebrow + '">' + t.label + '</button>';
        }).join("");
        section.appendChild(bar);
      }
    });
  };

  // ============================================================
  // initAll — 一次啟用所有
  // ============================================================
  window.initAll = function initAll(opts) {
    window.initProgress();
    window.initSectionDrawers();   // 必須在 initDrawer 之前（產生 trigger）
    window.initTOC();
    window.initContext();
    window.initFooterPreview();
    window.initDrawer();
    window.initKeyNav(opts);
  };
})();
