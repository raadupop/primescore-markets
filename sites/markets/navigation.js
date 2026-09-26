"use strict";

// The two sites deploy independently. Public HTML always leads to public pages;
// only known loopback previews connect to the local operator workspace.
(() => {
  const loopback = ["localhost", "127.0.0.1", "[::1]"].includes(location.hostname);
  const previews = {
    "8090": {brand: "8090", markets: "8091"},
    "8091": {brand: "8090", markets: "8091"},
    "4173": {brand: "4173", markets: "4174"},
    "4174": {brand: "4173", markets: "4174"},
  };
  const ports = loopback ? previews[location.port] : null;
  if (!ports) return;

  for (const link of document.querySelectorAll("a[href]")) {
    if (link.hasAttribute("data-dashboard")) {
      link.href = `http://${location.hostname}:5080/`;
      const label = link.querySelector("[data-public-label]");
      if (label) label.textContent = "Open dashboard";
      continue;
    }
    const target = new URL(link.href, location.href);
    const port = target.hostname === "primescore.ai" ? ports.brand
      : target.hostname === "markets.primescore.ai" ? ports.markets : null;
    if (port) {
      target.protocol = "http:";
      target.hostname = location.hostname;
      target.port = port;
      link.href = target.href;
    }
  }
  for (const element of document.querySelectorAll("[data-local-only]")) element.hidden = false;
})();
