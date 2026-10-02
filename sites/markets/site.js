"use strict";
document.documentElement.classList.add("js");

// Recorded from actual repository HTTP replay on 22 September 2026. Percentiles count the fixture's
// 1,260 prior closes at or below the observed level (ADR-0008); no simulated feed or price history.
const snapshots = {
  vix2018: {symbol: "VIX", date: "05 FEB 2018", observed: 37.32, severity: 0.9992, percentile: 0.99921, state: "Extremely stretched", series: "VIXCLS", fixture: "market_data_vix_volmageddon_2018_02_05"},
  vix2017: {symbol: "VIX", date: "05 OCT 2017", observed: 9.19, severity: -0.8849, percentile: 0, state: "Extremely compressed", series: "VIXCLS", fixture: "market_data_vix_low_vol_regime_2017_10_05"},
  ovx2019: {symbol: "OVX", date: "16 SEP 2019", observed: 48.58, severity: 0.8452, percentile: 0.84603, state: "Stretched", series: "OVXCLS", fixture: "market_data_ovx_aramco_attack_2019_09_16"},
};
let selected = "vix2018";
const element = (id) => document.getElementById(id);
const signed = (value, decimals) => `${value > 0 ? "+" : ""}${value.toFixed(decimals)}`;

function statePreview(announce = false) {
  const snapshot = snapshots[selected];
  element("percentile-value").textContent = `${(snapshot.percentile * 100).toFixed(1)}th`;
  element("state-value").textContent = snapshot.state;
  if (announce) element("preview-announcement").textContent = `${snapshot.symbol}, ${snapshot.date}. Recorded severity ${signed(snapshot.severity, 4)}. Level at the ${(snapshot.percentile * 100).toFixed(1)}th percentile of its prior closes: ${snapshot.state.toLowerCase()}. A description, not a forecast.`;
}

function snapshotPreview(id) {
  if (!Object.hasOwn(snapshots, id)) return;
  selected = id;
  const snapshot = snapshots[id];
  const compression = snapshot.severity < 0;
  const color = compression ? "#91b4ff" : "#c5f56a";
  element("index-label").textContent = `${snapshot.symbol} · daily close`;
  element("observed-value").textContent = snapshot.observed.toFixed(2);
  element("snapshot-date").textContent = snapshot.date;
  element("direction-label").textContent = compression ? "Below median" : "Above median";
  element("severity-value").textContent = signed(snapshot.severity, 4);
  element("replay-preview").classList.toggle("is-compression", compression);
  const x = (300 + snapshot.severity * 276).toFixed(2);
  element("severity-marker").setAttribute("x1", x);
  element("severity-marker").setAttribute("x2", x);
  element("severity-marker").setAttribute("stroke", color);
  element("severity-point").setAttribute("cx", x);
  element("severity-point").setAttribute("fill", color);
  element("severity-title").textContent = `Signed severity ${signed(snapshot.severity, 4)} on a scale from minus one to plus one`;
  element("source-link").href = `https://fred.stlouisfed.org/series/${snapshot.series}`;
  element("source-link").textContent = `FRED / ${snapshot.series} ↗`;
  element("snapshot-link").href = `https://github.com/raadupop/primescore-markets/blob/master/apps/classification/tests/acceptance/fixtures/${snapshot.fixture}.json`;
  for (const button of document.querySelectorAll("[data-snapshot]")) button.setAttribute("aria-pressed", String(button.dataset.snapshot === id));
  statePreview(true);
}
for (const button of document.querySelectorAll("[data-snapshot]")) button.addEventListener("click", () => snapshotPreview(button.dataset.snapshot));

const toggle = document.querySelector(".nav-toggle");
const navigation = element("site-navigation");
function closeNavigation() { navigation.classList.remove("is-open"); toggle.setAttribute("aria-expanded", "false"); }
toggle.addEventListener("click", () => {
  const open = navigation.classList.toggle("is-open");
  toggle.setAttribute("aria-expanded", String(open));
});
navigation.addEventListener("click", (event) => { if (event.target.closest("a")) closeNavigation(); });
document.addEventListener("keydown", (event) => { if (event.key === "Escape" && navigation.classList.contains("is-open")) { closeNavigation(); toggle.focus(); } });
