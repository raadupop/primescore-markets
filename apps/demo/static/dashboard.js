"use strict";

const byId = (id) => document.getElementById(id);
const ui = {events: [], selected: null, result: null, busy: false, replayed: new Set()};
const number = (value, digits = 2) => Number(value).toFixed(digits);
const signed = (value, digits = 2) => `${value > 0 ? "+" : ""}${number(value, digits)}`;

function status(message, error = false) {
  byId("status").textContent = message;
  byId("status").classList.toggle("error", error);
}

function busy(value) {
  ui.busy = value;
  for (const id of ["replay", "reset", "history-mode", "sensitivity"]) byId(id).disabled = value;
  for (const button of byId("event-tape").querySelectorAll("button")) button.disabled = value;
}

async function api(path, payload) {
  const response = await fetch(path, payload === undefined ? {} : {
    method: "POST", headers: {"Content-Type": "application/json"}, body: JSON.stringify(payload),
  });
  const body = await response.json();
  if (!response.ok) throw new Error(typeof body.detail === "string" ? body.detail : `Request rejected (HTTP ${response.status}).`);
  return body;
}

function eventTape() {
  byId("event-tape").replaceChildren();
  for (const event of ui.events) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "event-row";
    button.setAttribute("aria-pressed", String(event.event_id === ui.selected));
    button.dataset.eventId = event.event_id;
    for (const [tag, value, className] of [
      ["strong", event.symbol, ""], ["span", event.timestamp.slice(0, 10), "date"],
      ["small", `${event.source.provider} · ${event.source.series_id}`, ""],
      ["span", ui.replayed.has(event.event_id) ? "Replayed" : "", "replayed"],
    ]) {
      const element = document.createElement(tag);
      element.textContent = value;
      element.className = className;
      button.append(element);
    }
    button.addEventListener("click", () => {
      ui.selected = event.event_id;
      clearResult();
      eventTape();
      status(`${event.symbol} · ${event.timestamp.slice(0, 10)} selected. Replay to classify.`);
    });
    byId("event-tape").append(button);
  }
}

function svgElement(tag, attributes, text) {
  const element = document.createElementNS("http://www.w3.org/2000/svg", tag);
  for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, value);
  if (text !== undefined) element.textContent = text;
  return element;
}

function historyChart(history, current, symbol) {
  const chart = byId("history-chart");
  chart.replaceChildren();
  const lower = Math.min(...history, current);
  const upper = Math.max(...history, current);
  const span = upper - lower || 1;
  const x = (index) => 48 + index / history.length * 550;
  const y = (value) => 173 - (value - lower) / span * 148;
  chart.setAttribute("aria-label", `${symbol}: ${history.length} prior closes from ${number(lower)} to ${number(upper)} index points, with the selected observation highlighted.`);
  chart.append(svgElement("title", {}, chart.getAttribute("aria-label")));
  for (const value of [lower, upper]) {
    chart.append(svgElement("line", {x1: 48, y1: y(value), x2: 600, y2: y(value), class: "chart-guide"}));
    chart.append(svgElement("text", {x: 1, y: y(value) + 4, class: "chart-label"}, number(value, 1)));
  }
  chart.append(svgElement("polyline", {points: history.map((value, index) => `${x(index)},${y(value)}`).join(" "), class: "chart-history"}));
  const last = history.length - 1;
  if (history.length === 1) chart.append(svgElement("circle", {cx: x(0), cy: y(history[0]), r: 4, class: "chart-point"}));
  chart.append(svgElement("line", {x1: x(last), y1: y(history[last]), x2: x(history.length), y2: y(current), class: "chart-join"}));
  chart.append(svgElement("circle", {cx: x(history.length), cy: y(current), r: 4, class: "chart-event"}));
  chart.append(svgElement("text", {x: 48, y: 202, class: "chart-label"}, "Prior observation 1"));
  chart.append(svgElement("text", {x: 600, y: 202, "text-anchor": "end", class: "chart-label"}, `${history.length} prior → event`));
}

function provenance(result) {
  const source = result.event.source;
  const details = byId("source-details");
  details.replaceChildren();
  for (const [label, value] of [
    ["Provider / series", `${source.provider} / ${source.series_id}`],
    ["Declared full history range", source.long_horizon_window_range],
    ["Latest prior close", result.last_update],
    ["History retrieved", source.long_horizon_retrieved_at],
    ["Observations used", `${result.history.length} (${result.history_mode})`],
    ["Review status", result.event.review_status],
  ]) {
    const term = document.createElement("dt");
    const definition = document.createElement("dd");
    term.textContent = label;
    definition.textContent = value;
    details.append(term, definition);
  }
  const link = document.createElement("a");
  link.textContent = "Provider series and data terms ↗";
  const url = new URL(source.url);
  if (url.protocol === "https:") link.href = url.href;
  details.append(link);
}

function clearResult() {
  ui.result = null;
  for (const id of ["severity", "certainty", "history-coverage", "recency", "http-status", "observed", "scenario", "gap", "conviction"]) byId(id).textContent = "—";
  byId("selected-event").textContent = "Select a snapshot and replay it.";
  byId("reasoning").textContent = "Awaiting an HTTP classification response.";
  byId("scenario-status").textContent = "No scenario calculated.";
  byId("history-chart").replaceChildren(svgElement("text", {x: 24, y: 100, class: "chart-label"}, "Prior closes appear after replay."));
  byId("history-chart").setAttribute("aria-label", "History chart appears after replay");
  byId("history-caption").textContent = "Observation index, not an invented timeline. The selected event is shown separately.";
  byId("source-details").replaceChildren();
}

function showResult(result) {
  ui.result = result;
  const classification = result.classification;
  byId("selected-event").textContent = `${result.event.symbol} · ${result.event.timestamp.slice(0, 10)} · ${number(result.event.observed_iv)} index points`;
  byId("severity").textContent = signed(classification.score, 4);
  byId("certainty").textContent = `${number(classification.certainty * 100, 1)}%`;
  byId("history-coverage").textContent = `${number(classification.history_sufficiency * 100, 1)}%`;
  byId("recency").textContent = `${number(classification.temporal_relevance * 100, 1)}%`;
  byId("http-status").textContent = `HTTP ${result.classifier_http_status} · ${classification.classification_method}`;
  byId("reasoning").textContent = classification.reasoning_trace;
  byId("observed").textContent = number(result.event.observed_iv);
  historyChart(result.history, result.event.observed_iv, result.event.symbol);
  byId("history-caption").textContent = `${result.history.length.toLocaleString()} prior sourced closes in blue; selected event in lime. No per-observation timestamps are present.`;
  provenance(result);
  if (result.scenario === null) {
    for (const id of ["scenario", "gap", "conviction"]) byId(id).textContent = "Unavailable";
    byId("scenario-status").textContent = "Degraded classification. See reasoning trace; no scenario produced.";
    status("HTTP classification completed with degraded certainty; scenario withheld.");
    return;
  }
  byId("scenario").textContent = number(result.scenario.scenario_iv);
  byId("gap").textContent = signed(result.scenario.gap);
  byId("conviction").textContent = signed(result.scenario.conviction, 4);
  byId("scenario-status").textContent = result.scenario.gap === 0 ? "No gap under the chosen assumption." : `${result.scenario.gap > 0 ? "Expansion" : "Compression"} scenario under the chosen assumption.`;
  status("Replay complete · HTTP 200 · independent snapshot reset before classification.");
}

async function replay() {
  if (ui.busy || !ui.selected) return;
  busy(true);
  clearResult();
  status("Replaying prior snapshot through the HTTP classifier…");
  try {
    const result = await api("/demo/api/replay", {
      event_id: ui.selected, sensitivity: Number(byId("sensitivity").value), history_mode: byId("history-mode").value,
    });
    showResult(result);
    ui.replayed.add(ui.selected);
    eventTape();
  } catch (error) {
    clearResult();
    byId("scenario-status").textContent = "Unavailable. No scenario produced.";
    status(error.message, true);
  } finally {
    busy(false);
  }
}

byId("controls").addEventListener("submit", (event) => { event.preventDefault(); replay(); });
byId("sensitivity").addEventListener("input", () => { byId("sensitivity-value").textContent = number(byId("sensitivity").value); });
byId("sensitivity").addEventListener("change", () => { if (ui.result) replay(); });
byId("history-mode").addEventListener("change", () => { clearResult(); status("History setting changed. Replay to apply."); });
byId("reset").addEventListener("click", async () => {
  if (ui.busy) return;
  busy(true);
  try {
    await api("/demo/api/reset", {});
    ui.replayed.clear();
    clearResult();
    eventTape();
    status("Reset complete. Replay a snapshot to seed prior history.");
  } catch (error) { clearResult(); status(error.message, true); }
  finally { busy(false); }
});

async function initialize() {
  try {
    const result = await api("/demo/api/events");
    ui.events = result.events;
    ui.selected = ui.events.find((event) => event.event_id.includes("volmageddon"))?.event_id ?? ui.events[0]?.event_id;
    eventTape();
    byId("replay").disabled = !ui.selected;
    status(`${ui.events.length} sourced snapshots ready. Select one and replay.`);
  } catch (error) { status(`Dashboard unavailable: ${error.message}`, true); }
}

initialize();
