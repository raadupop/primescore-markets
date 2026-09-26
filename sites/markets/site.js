"use strict";
document.documentElement.classList.add("js");

// Recorded from actual repository HTTP replay on 22 September 2026.
// Three observed closes and classifier results; no simulated feed or price history.
const snapshots = {
  vix2018: {symbol: "VIX", date: "05 FEB 2018", observed: 37.32, severity: 0.9992, certainty: 1, series: "VIXCLS", fixture: "market_data_vix_volmageddon_2018_02_05"},
  vix2017: {symbol: "VIX", date: "05 OCT 2017", observed: 9.19, severity: -0.8849, certainty: 1, series: "VIXCLS", fixture: "market_data_vix_low_vol_regime_2017_10_05"},
  ovx2019: {symbol: "OVX", date: "16 SEP 2019", observed: 48.58, severity: 0.8452, certainty: 1, series: "OVXCLS", fixture: "market_data_ovx_aramco_attack_2019_09_16"},
};
let selected = "vix2018";
const element = (id) => document.getElementById(id);
const signed = (value, decimals) => `${value > 0 ? "+" : ""}${value.toFixed(decimals)}`;

function scenarioPreview(announce = false) {
  const snapshot = snapshots[selected];
  const sensitivity = Number(element("sensitivity").value);
  const scenario = snapshot.observed * (1 + snapshot.severity * snapshot.certainty * sensitivity);
  const gap = scenario - snapshot.observed;
  element("sensitivity-value").textContent = sensitivity.toFixed(2);
  element("scenario-value").textContent = scenario.toFixed(2);
  element("gap-value").textContent = signed(gap, 2);
  if (announce) element("preview-announcement").textContent = `${snapshot.symbol}, ${snapshot.date}. Recorded severity ${signed(snapshot.severity, 4)}. At multiplier ${sensitivity.toFixed(2)}, experimental scenario ${scenario.toFixed(2)}, gap ${signed(gap, 2)} index points.`;
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
  element("direction-label").textContent = compression ? "Compression" : "Expansion";
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
  element("snapshot-link").href = `https://github.com/raadupop/primescore-ai/blob/master/apps/classification/tests/acceptance/fixtures/${snapshot.fixture}.json`;
  for (const button of document.querySelectorAll("[data-snapshot]")) button.setAttribute("aria-pressed", String(button.dataset.snapshot === id));
  scenarioPreview(true);
}
for (const button of document.querySelectorAll("[data-snapshot]")) button.addEventListener("click", () => snapshotPreview(button.dataset.snapshot));
element("sensitivity").addEventListener("input", () => scenarioPreview());
element("sensitivity").addEventListener("change", () => scenarioPreview(true));

const toggle = document.querySelector(".nav-toggle");
const navigation = element("site-navigation");
function closeNavigation() { navigation.classList.remove("is-open"); toggle.setAttribute("aria-expanded", "false"); }
toggle.addEventListener("click", () => {
  const open = navigation.classList.toggle("is-open");
  toggle.setAttribute("aria-expanded", String(open));
});
navigation.addEventListener("click", (event) => { if (event.target.closest("a")) closeNavigation(); });
document.addEventListener("keydown", (event) => { if (event.key === "Escape" && navigation.classList.contains("is-open")) { closeNavigation(); toggle.focus(); } });
