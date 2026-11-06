(function () {
  function qs(s, r) { return (r || document).querySelector(s); }
  function qsa(s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); }

  // ============================================================
  // MEETUP_SIGNUP — 月小聚報名設定（url + eventDate 成對維護）
  // 開放報名：兩個欄位都填。url = 報名表單網址；eventDate = 活動日期 YYYY-MM-DD（台北時間）。
  // 自動關閉：活動當天（含）之前按鈕可按；台北時間過了 eventDate 就自動停用，不必記得回來改。
  // 沒有新連結時：url 與 eventDate 都留空字串，所有 [data-meetup-link] 按鈕會顯示「目前未開放報名」且按不下去。
  // QA 7-17 會檢查：url 有填但 eventDate 缺漏／格式錯誤／已過期 → FAIL（逼你換下一場的連結＋日期，或清空 url）。
  // 格式：必須是單一一行 var MEETUP_SIGNUP = { url: "…", eventDate: "…" }; —— url 與 eventDate 都要用單引號或雙引號的純字串寫在這一行（不可用樣板字串、不可串接、值前後不可有空白），兩個鍵順序不拘、可有尾逗號，也不要另外對它重新賦值。
  // QA 7-17 會實際執行本檔，只信 window.__meetupSignupEffective() 回傳的 url / eventDate（即訪客實際看到的設定），並要求上面那一行原文含有同樣的純字串。
  // 舊的八月表單連結可從 git 歷史 e805fac 找回，不要再填回來。
  // ============================================================
  var MEETUP_SIGNUP = { url: "", eventDate: "" };  // 上一期已結束；下一場尚未提供，保持關閉。
  // 唯讀 getter：執行期與 QA 7-17 都讀這個物件，所以閘門檢查的就是訪客實際看到的設定。
  window.__meetupSignupEffective = function () { return { url: MEETUP_SIGNUP.url, eventDate: MEETUP_SIGNUP.eventDate }; };

  // 今天的日期（台北時間）YYYY-MM-DD；Intl 不可用時退回本機時間
  function taipeiToday() {
    try {
      var s = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Taipei", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
      if (/^\d{4}-\d{2}-\d{2}$/.test(s)) return s;
    } catch (e) { /* 落到下面的本機時間 */ }
    var d = new Date();
    function p(n) { return (n < 10 ? "0" : "") + n; }
    return d.getFullYear() + "-" + p(d.getMonth() + 1) + "-" + p(d.getDate());
  }

  function isRealDate(s) {
    var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s || "");
    if (!m) return false;
    var d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
    return d.getUTCFullYear() === +m[1] && d.getUTCMonth() === +m[2] - 1 && d.getUTCDate() === +m[3];
  }

  // 只讀 getter 一次；url 必須是 https:// 開頭且不含空白，日期必須是真實日期且台北時間今天 <= eventDate，否則回傳 null（＝關閉）
  function meetupConfig() {
    var c = window.__meetupSignupEffective();
    if (!(c && typeof c.url === "string" && /^https:\/\/\S+$/.test(c.url) && isRealDate(c.eventDate) && taipeiToday() <= c.eventDate)) return null;
    return { url: c.url, eventDate: c.eventDate };
  }

  window.initMeetupLinks = function initMeetupLinks() {
    var c = meetupConfig();
    qsa("[data-meetup-link]").forEach(function (el) {
      if (c) {
        el.href = c.url;
        if (!el.getAttribute("target")) el.setAttribute("target", "_blank");
        if (!el.getAttribute("rel")) el.setAttribute("rel", "noopener");
        el.removeAttribute("aria-disabled");
        el.classList.remove("is-disabled");
      } else {
        el.removeAttribute("href");
        el.removeAttribute("target");
        el.removeAttribute("rel");
        el.setAttribute("aria-disabled", "true");
        el.classList.add("is-disabled");
        el.textContent = "\uD83D\uDCDD 目前未開放報名";
        el.title = "下一場小聚的報名連結尚未公布";
      }
    });
  };

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
    "index": { title: "BIM_MCP Hub", desc: "入口：安裝、第一次查詢、找方法、貢獻。" },
    "reference/philosophy-22-propositions": { title: "22 命題完整宣言", desc: "從「為什麼 MCP」到「會議場景」六群組。" },
    "reference/three-constitutions": { title: "四條憲法", desc: "Passive Ready / Data Honesty / Domain Method / Active Re-Anchoring。" },
    "reference/industry-evidence": { title: "業界證據", desc: "100× / 70% / $15-25K + 三套成熟方法論。" },
    "reference/spectrum-decision": { title: "光譜決策框架", desc: "FAIL 之後的 A/B/C/D 四條路線。" },
    "reference/skills-index": { title: "Skills 索引（61）", desc: "編排層：何時觸發 / 什麼順序。" },
    "reference/mep-playbook": { title: "機電教戰手冊", desc: "domain/mep-space-demand-matrix 的教學版，三章 + 配套模型導覽。" },
    "reference/tools-index": { title: "Tools 索引", desc: "253 個執行層工具，依用途分八類。" },
    "reference/domain-index": { title: "Domain 索引（94）", desc: "知識層：法規 / SOP / lessons。" },
    "reference/deployment-guide": { title: "部署指南", desc: "安裝到你的 Revit，完成第一次唯讀查詢。" },
    "reference/troubleshooting": { title: "排錯指南", desc: "三種症狀分流：看不到外掛、無法連線、結果不符預期。" },
    "reference/contributor-template": { title: "貢獻範本", desc: "新增 Domain / Skill / Tool 的順序。" },
    "reference/personal-llm-wiki": { title: "個人 LLM Wiki", desc: "把 Domain pull 回家，長成個人知識庫。" },
    "reference/contributors": { title: "貢獻者致謝", desc: "27 位建置協助人：GitHub handle + PR／commit。" },
    "reference/architecture-v2": { title: "三層架構 V2", desc: "Skill / Domain / Tool 的分工，以及軟體連線。" },
    "reference/kanban-index": { title: "看板卡索引", desc: "Monstrare 看板卡的歷史紀錄，不是現行方法。" }
    // 新增 reference/ 頁時，只加一筆帶 "reference/" 前綴的項目：metaFor() 會自動退一步補上前綴。
    // 同一頁若同時有裸鍵與帶前綴鍵，就會有兩份各自漂移的描述（architecture 頁曾因此自相矛盾）。
  };

  // ============================================================
  // BIM_ROUTES — 站內閱讀順序（單一來源）。鍵 = "index" 或 "reference/<頁名>"（不含 .html）
  // initRoutes() 依此補 body 的 data-prev/data-next，並填入 [data-route-nav] 容器
  // ============================================================
  window.BIM_ROUTES = {
    "index":                                { next: "reference/deployment-guide" },
    "reference/deployment-guide":           { prev: "index", next: "reference/domain-index" },
    "reference/troubleshooting":            { prev: "reference/deployment-guide", next: "reference/domain-index" },
    "reference/domain-index":               { prev: "reference/deployment-guide", next: "reference/skills-index" },
    "reference/mep-playbook":               { prev: "reference/domain-index", next: "reference/skills-index" },
    "reference/skills-index":               { prev: "reference/domain-index", next: "reference/tools-index" },
    "reference/tools-index":                { prev: "reference/skills-index", next: "reference/contributor-template" },
    "reference/contributor-template":       { prev: "reference/tools-index", next: "reference/contributors" },
    "reference/contributors":               { prev: "reference/contributor-template", next: "index" },
    "reference/kanban-index":               { prev: "reference/contributors", next: "reference/personal-llm-wiki" },
    "reference/personal-llm-wiki":          { prev: "reference/kanban-index", next: "index" },
    "reference/architecture-v2":            { prev: "index", next: "reference/industry-evidence" },
    "reference/industry-evidence":          { prev: "reference/architecture-v2", next: "reference/philosophy-22-propositions" },
    "reference/philosophy-22-propositions": { prev: "reference/industry-evidence", next: "reference/three-constitutions" },
    "reference/three-constitutions":        { prev: "reference/philosophy-22-propositions", next: "reference/spectrum-decision" },
    "reference/spectrum-decision":          { prev: "reference/three-constitutions", next: "reference/domain-index" }
  };

  // 由 location.pathname 推出路由鍵；凍結頁（YYYY-MM/…）與未登錄頁回傳 ""
  function routeKey() {
    var path = (window.location && window.location.pathname) || "";
    var segs = path.split("/").filter(function (x) { return x; });
    var raw = segs.length ? segs[segs.length - 1] : "";
    var parent, last;
    if (raw.indexOf(".") === -1) {
      // 目錄 URL（…/BIM_MCP/）視為該目錄的 index
      parent = raw; last = "index";
    } else {
      parent = segs.length > 1 ? segs[segs.length - 2] : "";
      last = raw.replace(/\.html$/, "");
    }
    if (/^\d{4}-\d{2}$/.test(parent)) return "";
    var key = last === "index" ? "index" : parent + "/" + last;
    return Object.prototype.hasOwnProperty.call(window.BIM_ROUTES, key) ? key : "";
  }

  // 從 fromKey 頁面指向 toKey 頁面的相對 href
  function routeHref(fromKey, toKey) {
    var fromHub = fromKey === "index";
    if (toKey === "index") return fromHub ? "index.html" : "../index.html";
    var name = toKey.replace(/^reference\//, "") + ".html";
    return fromHub ? "reference/" + name : name;
  }

  window.initRoutes = function initRoutes() {
    var key = routeKey();
    if (!key) return;
    var route = window.BIM_ROUTES[key];
    var body = document.body;
    var prevHref = route.prev ? routeHref(key, route.prev) : "";
    var nextHref = route.next ? routeHref(key, route.next) : "";
    // 舊頁仍帶 body 屬性時不覆寫
    if (!body.getAttribute("data-prev") && !body.getAttribute("data-next")) {
      if (prevHref) body.setAttribute("data-prev", prevHref);
      if (nextHref) body.setAttribute("data-next", nextHref);
    }
    function link(cls, href, text) {
      var a = document.createElement("a");
      a.className = cls;
      a.setAttribute("href", href);
      a.textContent = text;
      return a;
    }
    var nextMeta = route.next ? (PAGE_META[route.next] || PAGE_META["reference/" + route.next] || { title: route.next }) : null;
    qsa('[data-route-nav="top"]').forEach(function (box) {
      if (box.getAttribute("data-route-filled")) return;
      box.setAttribute("data-route-filled", "1");
      if (prevHref) box.insertBefore(link("bim-topnav-btn", prevHref, "← 上一篇"), box.firstChild);
      if (nextHref) box.appendChild(link("bim-topnav-btn", nextHref, "下一篇 →"));
    });
    qsa('[data-route-nav="foot"]').forEach(function (box) {
      if (box.getAttribute("data-route-filled")) return;
      box.setAttribute("data-route-filled", "1");
      var first = box.firstChild;
      if (prevHref) box.insertBefore(link("bim-footer-btn", prevHref, "← 上一篇"), first);
      // 頁面自己已有主要動作（如「複製建置說明」「開啟看板 ↗」）時，下一篇改用一般按鈕，避免一列兩顆主按鈕
      var hasPrimary = !!box.querySelector(".bim-footer-btn.primary");
      if (nextHref) box.insertBefore(link(hasPrimary ? "bim-footer-btn" : "bim-footer-btn primary", nextHref, "下一篇 → " + nextMeta.title), first);
    });
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
      // 裸鍵查不到時，退一步試 "reference/" + key —— 新頁面只需維護一組帶前綴的鍵即可涵蓋
      // index.html（用 "reference/xxx" 連到 reference/ 頁）與 reference/*.html 互連（用裸 "xxx"）兩種呼叫方式。
      return PAGE_META[key] || PAGE_META["reference/" + key] || { title: key, desc: "" };
    }

    function card(dir, href, meta) {
      var dirLabel = dir === "prev" ? "← 上一篇" : "下一篇 →";
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
      if (event.target && (event.target.isContentEditable || (event.target.closest && event.target.closest("[contenteditable]")))) return;
      if (event.key === "Escape") {
        var close = qs("[data-drawer-close]");
        if (close) close.click();
        return;
      }
      // 抽屜開啟時，方向鍵與 g+h 不翻頁（避免閱讀抽屜時被帶離本頁）
      if (qs(".bim-drawer.open") || qs(".bim-toc.open")) return;
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
          '<span>Esc 關閉</span>' +
          '<a href="#" data-drawer-permalink>open as page ↗</a>' +
        '</div>';
      document.body.appendChild(drawer);
    }

    var titleEl = qs("[data-drawer-title]", drawer);
    var eyebrowEl = qs("[data-drawer-eyebrow]", drawer);
    var bodyEl = qs("[data-drawer-body]", drawer);
    var permaEl = qs("[data-drawer-permalink]", drawer);
    var closeBtn = qs("[data-drawer-close]", drawer);
    var lastOpener = null;

    drawer.setAttribute("role", "dialog");
    drawer.setAttribute("aria-modal", "true");
    if (titleEl) {
      if (!titleEl.id) titleEl.id = "bim-drawer-title";
      drawer.setAttribute("aria-labelledby", titleEl.id);
    }

    function open(opener) {
      lastOpener = opener;
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
      if (closeBtn) closeBtn.focus();
    }

    function close() {
      drawer.classList.remove("open");
      overlay.classList.remove("open");
      drawer.setAttribute("aria-hidden", "true");
      document.body.style.overflow = "";
      if (lastOpener && document.contains(lastOpener) && lastOpener.focus) lastOpener.focus();
      lastOpener = null;
    }

    // 抽屜開啟時，Tab 鍵只在抽屜內循環（掛在 document，焦點落在 body 時也能拉回抽屜）
    document.addEventListener("keydown", function (e) {
      if (e.key !== "Tab" || !drawer.classList.contains("open")) return;
      var f = qsa('a[href], button, [tabindex]:not([tabindex="-1"]), input, select, textarea', drawer)
        .filter(function (n) { return !n.disabled && n.offsetParent !== null; });
      if (f.length === 0) { e.preventDefault(); return; }
      var first = f[0], last = f[f.length - 1];
      var ae = document.activeElement;
      if (!ae || !drawer.contains(ae)) { e.preventDefault(); first.focus(); }
      else if (e.shiftKey && ae === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && ae === last) { e.preventDefault(); first.focus(); }
    });

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
        var nodes = qsa(rule.sel, section).filter(function (n) {
          return !n.closest("[data-keep]") && !n.closest(".bim-callout");
        });
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
  // initSearch — 目錄頁搜尋 + 篩選（純屬性介面，無 JS 時全部可見）
  //   root      [data-search-root]
  //   input     input[type=search][data-search-input]
  //   items     [data-search-item]  比對 textContent + data-search-keys（不分大小寫，空白分隔的詞全部符合）
  //   filters   [data-filter-group=G][data-filter-value=V] 切換 aria-pressed；
  //             項目的 data-G 值落在已按下的值之中即通過（同組 OR、跨組 AND）
  //   groups    [data-search-group=K] 標題；沒有任何可見的 [data-search-in=K] 項目時隱藏
  //   status    [data-search-status]  「顯示 X／共 Y 項」（aria-live="polite"）
  //   clear     [data-search-clear]
  //   只用 hidden 屬性隱藏，不移除節點、不加 class。
  // ============================================================
  window.initSearch = function initSearch() {
    qsa("[data-search-root]").forEach(function (root) {
      if (root.__bimSearch) return;
      var input = qs("input[type=search][data-search-input]", root);
      var items = qsa("[data-search-item]", root);
      var groups = qsa("[data-search-group]", root);
      var status = qs("[data-search-status]", root);
      var clearBtns = qsa("[data-search-clear]", root);
      var filterBtns = qsa("[data-filter-group][data-filter-value]", root);
      var pressed = {};

      if (status && !status.getAttribute("aria-live")) status.setAttribute("aria-live", "polite");
      filterBtns.forEach(function (b) {
        if (!b.hasAttribute("aria-pressed")) b.setAttribute("aria-pressed", "false");
        if (b.getAttribute("aria-pressed") === "true") {
          var g0 = b.getAttribute("data-filter-group");
          (pressed[g0] = pressed[g0] || []).push(b.getAttribute("data-filter-value"));
        }
      });

      var haystacks = items.map(function (el) {
        // [data-search-ignore] 是每張卡都有的介面字（「方法原文 ↗」「展開摘要」），不列入比對
        var clone = el.cloneNode(true);
        Array.prototype.forEach.call(clone.querySelectorAll("[data-search-ignore]"), function (n) { n.parentNode.removeChild(n); });
        return ((clone.textContent || "") + " " + (el.getAttribute("data-search-keys") || "")).toLowerCase();
      });

      function passesGroups(el) {
        for (var g in pressed) {
          if (!pressed.hasOwnProperty(g) || pressed[g].length === 0) continue;
          var have = (el.getAttribute("data-" + g) || "").split(/\s+/);
          var ok = pressed[g].some(function (v) { return have.indexOf(v) >= 0; });
          if (!ok) return false;
        }
        return true;
      }

      function update() {
        var terms = ((input && input.value) || "").toLowerCase().split(/\s+/).filter(Boolean);
        var shown = 0;
        var inGroup = {};
        items.forEach(function (el, i) {
          var hay = haystacks[i];
          var ok = terms.every(function (term) { return hay.indexOf(term) >= 0; }) && passesGroups(el);
          el.hidden = !ok;
          if (ok) {
            shown++;
            var k = el.getAttribute("data-search-in");
            if (k) inGroup[k] = true;
          }
        });
        groups.forEach(function (h) {
          h.hidden = !inGroup[h.getAttribute("data-search-group")];
        });
        if (status) status.textContent = "顯示 " + shown + "／共 " + items.length + " 項";
      }

      if (input) input.addEventListener("input", update);
      filterBtns.forEach(function (b) {
        b.addEventListener("click", function () {
          var g = b.getAttribute("data-filter-group");
          var v = b.getAttribute("data-filter-value");
          var on = b.getAttribute("aria-pressed") !== "true";
          b.setAttribute("aria-pressed", on ? "true" : "false");
          var list = pressed[g] = pressed[g] || [];
          var idx = list.indexOf(v);
          if (on && idx < 0) list.push(v);
          if (!on && idx >= 0) list.splice(idx, 1);
          update();
        });
      });
      function clearAll() {
        if (input) input.value = "";
        pressed = {};
        filterBtns.forEach(function (b) { b.setAttribute("aria-pressed", "false"); });
        update();
      }
      clearBtns.forEach(function (b) { b.addEventListener("click", clearAll); });

      root.__bimSearch = { clear: clearAll, update: update };
      update();
    });
  };

  // ============================================================
  // revealHash — 讓深層連結在抽屜 / details / 搜尋篩選之下仍然可到達
  // ============================================================
  window.revealHash = function revealHash(opts) {
    // focusOnly：load 後補做用，只重新聚焦，不清搜尋/篩選、不開關抽屜或 details
    var focusOnly = !!(opts && opts.focusOnly === true);
    if (focusOnly) {
      var ae = document.activeElement;
      if (ae && (/^(INPUT|TEXTAREA|SELECT)$/.test(ae.tagName) || ae.isContentEditable)) return;
    }
    var id = "";
    try { id = decodeURIComponent((location.hash || "").slice(1)); } catch (e) { id = (location.hash || "").slice(1); }
    if (!id) return;
    var el = document.getElementById(id);
    if (!el) return;
    var changed = false;

    if (focusOnly) {
      var tr = qs("#__section_drawer_templates");
      if (!tr || !tr.contains(el)) return;
      var dr = qs(".bim-drawer");
      var be = dr && qs("[data-drawer-body]", dr);
      if (!be || !dr.classList.contains("open")) return;
      if (document.activeElement && be.contains(document.activeElement) && document.activeElement !== be) return;
      var cp = qs('[id="' + id.replace(/"/g, '\\"') + '"]', be);
      if (cp) {
        if (!cp.hasAttribute("tabindex")) cp.setAttribute("tabindex", "-1");
        cp.focus();
        if (cp.scrollIntoView) cp.scrollIntoView({ block: "start" });
      }
      return;
    }

    // 1) 搜尋/篩選把目標藏起來 → 先清除
    var item = el.closest("[data-search-item]");
    if (item && item.hidden) {
      var root = item.closest("[data-search-root]");
      if (root && root.__bimSearch) { root.__bimSearch.clear(); changed = true; }
    }

    // 2) 位於關閉的 <details> 內 → 逐層展開
    var d = el.parentElement;
    while (d) {
      if (d.tagName === "DETAILS" && !d.open) { d.open = true; changed = true; }
      d = d.parentElement;
    }

    // 3) 位於抽屜樣板內 → 開啟對應抽屜並聚焦其中的副本
    var tplRoot = qs("#__section_drawer_templates");
    if (tplRoot && tplRoot.contains(el)) {
      var tpl = el;
      while (tpl.parentElement && tpl.parentElement !== tplRoot) tpl = tpl.parentElement;
      var drawer = qs(".bim-drawer");
      var bodyEl = drawer && qs("[data-drawer-body]", drawer);
      var copy = bodyEl && qs('[id="' + id.replace(/"/g, '\\"') + '"]', bodyEl);
      if (!copy || !drawer.classList.contains("open")) {
        var opener = qs('[data-drawer-open][data-source="#' + tpl.id + '"]');
        if (!opener) return;
        opener.click();
        copy = bodyEl && qs('[id="' + id.replace(/"/g, '\\"') + '"]', bodyEl);
      }
      if (copy) {
        if (!copy.hasAttribute("tabindex")) copy.setAttribute("tabindex", "-1");
        copy.focus();
        if (copy.scrollIntoView) copy.scrollIntoView({ block: "start" });
      }
      return;
    }

    // 4) 一般目標：若剛剛改變過版面，重新捲動並聚焦
    if (changed) {
      if (!el.hasAttribute("tabindex") && !/^(A|BUTTON|INPUT|SELECT|TEXTAREA|SUMMARY)$/.test(el.tagName)) el.setAttribute("tabindex", "-1");
      if (el.scrollIntoView) el.scrollIntoView({ block: "start" });
      el.focus({ preventScroll: true });
    }
  };

  // ============================================================
  // initTheme — 淺色／深色主題（預設深色；不跟隨系統偏好）
  // 快照頁（body > div[data-snapshot]）固定深色：直接返回，不加切換鈕、不改 data-theme
  // ============================================================
  window.initTheme = function initTheme() {
    if (qs("body > div[data-snapshot]")) return;
    var root = document.documentElement;
    var KEY = "bim-theme";
    function readStored() {
      try { return window.localStorage.getItem(KEY); } catch (e) { return null; }
    }
    function writeStored(v) {
      try { window.localStorage.setItem(KEY, v); } catch (e) { /* 無痕或停用儲存時略過 */ }
    }
    function apply(light) {
      if (light) root.setAttribute("data-theme", "light");
      else root.removeAttribute("data-theme");
    }
    var light = readStored() === "light";
    apply(light);

    var host = qs(".bim-topnav-actions") || qs(".bim-topnav");
    if (!host || qs(".bim-theme-toggle")) return;
    var btn = document.createElement("button");
    btn.type = "button";
    btn.className = "bim-theme-toggle";
    btn.setAttribute("aria-label", "切換淺色／深色主題");
    function render() {
      btn.setAttribute("aria-pressed", light ? "true" : "false");
      btn.textContent = light ? "深色" : "淺色";
    }
    render();
    btn.addEventListener("click", function () {
      light = !light;
      apply(light);
      writeStored(light ? "light" : "dark");
      render();
    });
    host.appendChild(btn);
  };

  // ============================================================
  // initMobileToc — 手機目錄按鈕 + 任務列（≤900px 由 styles.css 控制顯示）
  // 凍結頁只有行為（切換鈕），不注入路由連結（T32）
  // ============================================================
  window.initMobileToc = function initMobileToc(opts) {
    opts = opts || {};
    var toc = qs(".bim-toc");
    var bar = qs(".bim-context-bar");
    if (!toc || !bar) return;
    if (qs(".bim-toc-toggle", bar)) return;
    if (!toc.id) toc.id = "bim-toc";

    var btn = document.createElement("button");
    btn.className = "bim-toc-toggle";
    btn.type = "button";
    btn.setAttribute("aria-expanded", "false");
    btn.setAttribute("aria-controls", toc.id);
    btn.textContent = "目錄";
    bar.appendChild(btn);

    var key = routeKey();
    if (key && !qs(".bim-toc-tasks", toc)) {
      var tasks = document.createElement("div");
      tasks.className = "bim-toc-tasks";
      var add = function (href, text, src) {
        var a = document.createElement("a");
        a.setAttribute("href", href);
        a.textContent = text;
        if (src) {
          if (src.getAttribute("target")) a.setAttribute("target", src.getAttribute("target"));
          if (src.getAttribute("rel")) a.setAttribute("rel", src.getAttribute("rel"));
        }
        tasks.appendChild(a);
      };
      var prev = document.body.getAttribute("data-prev");
      var next = document.body.getAttribute("data-next");
      if (prev) add(prev, "← 上一篇");
      if (next) add(next, "下一篇 →");
      var homeHref = opts.home || (key === "index" ? "index.html" : "../index.html");
      if (key !== "index" && homeHref !== next && homeHref !== prev) add(homeHref, "回首頁");
      var meetup = qs("[data-meetup-link]");
      if (meetup && !meetup.classList.contains("is-disabled") && meetup.getAttribute("aria-disabled") !== "true" && meetup.getAttribute("href")) {
        add(meetup.getAttribute("href"), (meetup.textContent || "").trim() || "小聚報名", meetup);
      }
      toc.appendChild(tasks);
    }

    function isOpen() { return toc.classList.contains("open"); }
    function setOpen(open, restoreFocus) {
      if (open === isOpen()) return;
      if (open) {
        toc.classList.add("open");
        btn.setAttribute("aria-expanded", "true");
        var first = qs("a", toc);
        if (!toc.hasAttribute("tabindex")) toc.setAttribute("tabindex", "-1");
        (first || toc).focus();
      } else {
        toc.classList.remove("open");
        btn.setAttribute("aria-expanded", "false");
        if (restoreFocus) btn.focus();
      }
    }
    btn.addEventListener("click", function () { setOpen(!isOpen(), true); });
    toc.addEventListener("click", function (e) {
      var t = e.target;
      while (t && t !== toc && t.tagName !== "A") t = t.parentNode;
      if (t && t.tagName === "A") setOpen(false, false);
    });
    // capture 階段：先於 initKeyNav 的 Escape 處理，確保焦點回到切換鈕
    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && isOpen()) setOpen(false, true);
    }, true);
    document.addEventListener("click", function (e) {
      if (!isOpen()) return;
      var t = e.target;
      if (t && (toc.contains(t) || btn.contains(t))) return;
      setOpen(false, false);
    });
  };

  var hashBound = false;

  // ============================================================
  // initAll — 一次啟用所有
  // ============================================================
  window.initAll = function initAll(opts) {
    window.initRoutes();
    window.initTheme();
    window.initProgress();
    window.initSectionDrawers();   // 必須在 initDrawer 之前（產生 trigger）
    window.initTOC();
    window.initContext();
    window.initFooterPreview();
    window.initDrawer();
    window.initKeyNav(opts);
    window.initMeetupLinks();
    window.initMobileToc(opts);   // 必須在 initMeetupLinks 之後（報名連結的 href 由它寫入）
    window.initSearch();
    window.revealHash();
    if (!hashBound) {
      hashBound = true;
      window.addEventListener("hashchange", function () { window.revealHash(); });
      // 追蹤使用者在載入前聚焦的欄位：瀏覽器的 fragment 聚焦會把它 blur 成 body，load 後若使用者沒有主動離開就還給它
      var lastField = null, userLeft = false;
      var isField = function (n) { return !!n && (/^(INPUT|TEXTAREA|SELECT)$/.test(n.tagName) || n.isContentEditable); };
      document.addEventListener("focusin", function (e) { if (isField(e.target)) { lastField = e.target; userLeft = false; } });
      document.addEventListener("pointerdown", function (e) { if (e.target !== lastField) userLeft = true; }, true);
      document.addEventListener("keydown", function (e) { if (e.key === "Tab" || e.key === "Escape") userLeft = true; }, true);
      // 瀏覽器在載入後才執行自己的 fragment 捲動/聚焦，會把剛聚焦的副本 blur；load 之後只補做聚焦（不清篩選、不開關抽屜）
      window.addEventListener("load", function () {
        window.setTimeout(function () {
          var ae = document.activeElement;
          if ((!ae || ae === document.body) && lastField && !userLeft && document.contains(lastField)) {
            lastField.focus({ preventScroll: true });
            return;
          }
          window.revealHash({ focusOnly: true });
        }, 0);
      });
    }
  };
})();
