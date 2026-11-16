/*
 * IGN reading mode and in-memory cleaned export.
 * Derived from the host-specific keep-chain approach in the original
 * Game Twin ReadingModeFilters.kt. Runs only via WebView2 ExecuteScriptAsync
 * on an already archived IGN wiki page (publisher scripts remain disabled).
 *
 * The host substitutes the three quoted template markers below with
 * JSON-serialized trusted parameters. The ORIGINAL .mhtml is never written.
 */
(function () {
  "use strict";
  const action = "__GG_ACTION__";
  const sourceUrl = __GG_SOURCE_URL__;
  const articleTitle = __GG_ARTICLE_TITLE__;
  const STATE_KEY = "__gglReaderState";
  const junkSelectors = [
    ".wiki-bobble",
    ".adunit-wrapper",
    "[data-cy='wiki-auto-embed']",
    ".wiki-video",
    "[data-cy='page-header'] > .interface",
    ".byline-action",
    ".gpt-ad",
    ".ad-slot",
    ".ad_slot",
    "[id^='div-gpt-ad-']",
    "[id^='google_ads_iframe_']"
  ];

  if (action === "restore") {
    const state = window[STATE_KEY];
    if (!state || !state.active) return "original";
    // Restore exact pre-reader inline styles, not just the display property.
    for (let i = state.modified.length - 1; i >= 0; i--) {
      const entry = state.modified[i];
      if (entry.style === null) entry.element.removeAttribute("style");
      else entry.element.setAttribute("style", entry.style);
    }
    state.styleElement?.remove();
    delete window[STATE_KEY];
    return "original";
  }

  const body = document.body;
  const article = document.querySelector(".wiki-html");
  if (!body || !article) return "missing-article";
  const header = document.querySelector('[data-cy="page-header"]');

  if (action === "isolate") {
    // This only mutates the temporary viewer DOM prior to a NEW MHTML capture.
    // The original archive remains untouched and is reloaded by the host.
    const main = document.createElement("main");
    main.id = "ggl-clean-reader";
    main.setAttribute("role", "main");
    main.setAttribute("data-source", "ign");
    function cloneClean(element) {
      const clone = element.cloneNode(true);
      for (const selector of junkSelectors) {
        try {
          clone.querySelectorAll(selector).forEach(node => node.remove());
        } catch (_) { /* Ignore CSS selector errors on changed IGN markup. */ }
      }
      clone.querySelectorAll(
        "script, iframe, frame, object, embed, form, button, [role='button'], noscript"
      ).forEach(node => node.remove());
      for (const node of [clone, ...clone.querySelectorAll("*")]) {
        if (!node.attributes) continue;
        for (const attr of [...node.attributes]) {
          if (/^on/i.test(attr.name) || /^\s*javascript\s*:/i.test(attr.value))
            node.removeAttribute(attr.name);
        }
      }
      return clone;
    }
    if (header) main.appendChild(cloneClean(header));
    else {
      const title = document.createElement("h1");
      title.textContent = articleTitle || document.title || "IGN wiki article";
      main.appendChild(title);
    }
    main.appendChild(cloneClean(article));
    const footer = document.createElement("footer");
    footer.className = "ggl-source-note";
    const sourceLabel = document.createElement("span");
    sourceLabel.textContent = "Source: IGN wiki · ";
    footer.appendChild(sourceLabel);
    if (typeof sourceUrl === "string" && /^https:\/\/(www\.)?ign\.com\/wikis\//i.test(sourceUrl)) {
      const anchor = document.createElement("a");
      anchor.href = sourceUrl;
      anchor.textContent = sourceUrl;
      footer.appendChild(anchor);
    }
    main.appendChild(footer);

    document.body.replaceChildren(main);
    document.body.removeAttribute("class");
    document.body.removeAttribute("style");
    document.querySelectorAll("script, link[rel='preload'], link[rel='preconnect']")
      .forEach(element => element.remove());
    const style = document.createElement("style");
    style.id = "ggl-clean-export-css";
    style.textContent = [
      "html,body{height:auto!important;overflow:auto!important;background:#fff!important;color:#202832!important}",
      "body{font:17px/1.7 'Segoe UI',Arial,sans-serif!important;margin:0!important;padding:0!important}",
      "#ggl-clean-reader{display:block!important;box-sizing:border-box!important;max-width:960px!important;width:100%!important;min-height:100vh!important;margin:0 auto!important;padding:36px 32px 68px!important;background:#fff!important;color:#202832!important}",
      "#ggl-clean-reader .wiki-html,#ggl-clean-reader [data-cy='page-header']{display:block!important;box-sizing:border-box!important;width:100%!important;max-width:100%!important;margin:0!important;float:none!important;position:static!important;transform:none!important;color:inherit!important}",
      "#ggl-clean-reader img,#ggl-clean-reader picture,#ggl-clean-reader video{max-width:100%!important;height:auto!important}",
      "#ggl-clean-reader table{display:block!important;max-width:100%!important;overflow-x:auto!important}",
      "#ggl-clean-reader pre{white-space:pre-wrap!important;overflow-wrap:anywhere!important}",
      "#ggl-clean-reader .ggl-source-note{margin-top:52px;border-top:1px solid #c4cbd3;padding-top:16px;font-size:12px;color:#586371}",
      "@media(max-width:650px){#ggl-clean-reader{padding:18px 14px 50px!important}}"
    ].join("\n");
    document.head.appendChild(style);
    return "isolated";
  }

  if (action !== "apply") return "unknown-action";
  if (window[STATE_KEY]?.active) return "reader";

  const modified = [];
  const recorded = new WeakSet();
  function remember(element) {
    if (!element || recorded.has(element)) return;
    recorded.add(element);
    modified.push({ element, style: element.getAttribute("style") });
  }
  function hide(element) {
    if (!element || element.tagName === "SCRIPT" || element.tagName === "STYLE") return;
    remember(element);
    element.style.setProperty("display", "none", "important");
  }

  // Preserve the article plus its title/author block and both ancestor chains.
  const keep = new Set([body]);
  for (const item of [article, header]) {
    let node = item;
    while (node && node !== body) {
      keep.add(node);
      node = node.parentElement;
    }
  }
  for (const node of [...keep]) {
    if (node === body || !node.parentElement) continue;
    for (const sibling of node.parentElement.children) {
      if (!keep.has(sibling)) hide(sibling);
    }
  }

  // Game Twin's IGN-specific in-body exclusions.
  for (const selector of junkSelectors) {
    try {
      document.querySelectorAll(selector).forEach(element => {
        // Don't hide an ancestor of the actual article or its header.
        if (!keep.has(element)) hide(element);
      });
    } catch (_) { /* Graceful fallback on future DOM changes. */ }
  }

  // Remove layout constraints left over from the site's multi-column chrome.
  let node = article;
  while (node && node !== body) {
    remember(node);
    node.style.setProperty("width", "100%", "important");
    node.style.setProperty("max-width", "none", "important");
    node.style.setProperty("min-width", "0", "important");
    node.style.setProperty("margin-left", "auto", "important");
    node.style.setProperty("margin-right", "auto", "important");
    if (node !== article) {
      node.style.setProperty("display", "block", "important");
      node.style.setProperty("height", "auto", "important");
      node.style.setProperty("overflow", "visible", "important");
    }
    node = node.parentElement;
  }
  if (header) {
    remember(header);
    header.style.setProperty("max-width", "920px", "important");
    header.style.setProperty("margin", "12px auto 18px", "important");
  }
  remember(document.documentElement);
  document.documentElement.style.setProperty("overflow", "auto", "important");
  remember(body);
  body.style.setProperty("overflow", "auto", "important");
  body.style.setProperty("height", "auto", "important");

  const style = document.createElement("style");
  style.id = "ggl-reader-style";
  style.textContent = [
    ".wiki-html{max-width:920px!important;margin:0 auto!important;padding:12px 24px 70px!important;font-size:16px!important;line-height:1.68!important}",
    ".wiki-html img,.wiki-html picture{max-width:100%!important;height:auto!important}",
    ".wiki-html table{max-width:100%!important;overflow-x:auto!important}",
    ".wiki-html pre{white-space:pre-wrap!important;overflow-wrap:anywhere!important}",
    ".wiki-html a{overflow-wrap:anywhere}",
    ".wiki-bobble,.adunit-wrapper,[data-cy='wiki-auto-embed'],.wiki-video,.byline-action{display:none!important}"
  ].join("\n");
  document.head.appendChild(style);
  window[STATE_KEY] = { active: true, modified, styleElement: style };
  return "reader";
})()
