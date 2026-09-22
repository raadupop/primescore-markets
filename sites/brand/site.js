"use strict";

document.documentElement.classList.add("js");

const localPreview = ["localhost", "127.0.0.1"].includes(location.hostname)
  && ["4173", "4174"].includes(location.port);
if (localPreview) {
  document.querySelectorAll('a[data-site="markets"]').forEach((link) => {
    link.href = `http://${location.hostname}:4174/`;
  });
}

const menuButton = document.querySelector(".nav-toggle");
const menu = document.querySelector(".site-nav");
function closeMenu() {
  menu.classList.remove("is-open");
  menuButton.setAttribute("aria-expanded", "false");
  menuButton.textContent = "Menu";
}
menuButton.addEventListener("click", () => {
  const open = menu.classList.toggle("is-open");
  menuButton.setAttribute("aria-expanded", String(open));
  menuButton.textContent = open ? "Close" : "Menu";
});
menu.querySelectorAll("a").forEach((link) => link.addEventListener("click", closeMenu));
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && menu.classList.contains("is-open")) {
    closeMenu();
    menuButton.focus();
  }
});
document.addEventListener("click", (event) => {
  if (!event.target.closest(".site-header")) closeMenu();
});

const motionPreference = matchMedia("(prefers-reduced-motion: reduce)");
const motionButton = document.querySelector(".motion-toggle");
const motionLabel = motionButton.querySelector(".motion-label");
const motionSymbol = motionButton.querySelector(".motion-symbol");
const visual = document.querySelector(".score-visual");
const stage = document.querySelector(".score-stage");
let userPaused = false;

function updateMotion() {
  const paused = userPaused || motionPreference.matches;
  document.body.classList.toggle("motion-paused", paused);
  motionButton.setAttribute("aria-pressed", String(paused));
  motionButton.setAttribute("aria-label", paused ? "Resume diagram animation" : "Pause diagram animation");
  motionLabel.textContent = motionPreference.matches ? "Reduced motion" : paused ? "Motion off" : "Motion on";
  motionSymbol.textContent = paused ? "▷" : "Ⅱ";
  motionButton.disabled = motionPreference.matches;
}
motionButton.hidden = false;
motionButton.addEventListener("click", () => {
  userPaused = !userPaused;
  updateMotion();
});
motionPreference.addEventListener("change", updateMotion);
updateMotion();

stage.addEventListener("pointermove", (event) => {
  if (event.pointerType !== "mouse" || userPaused || motionPreference.matches) return;
  const box = stage.getBoundingClientRect();
  visual.style.setProperty("--tilt-x", `${((event.clientX - box.left) / box.width - 0.5) * 8}deg`);
  visual.style.setProperty("--tilt-y", `${((event.clientY - box.top) / box.height - 0.5) * -8}deg`);
});
stage.addEventListener("pointerleave", () => {
  visual.style.setProperty("--tilt-x", "0deg");
  visual.style.setProperty("--tilt-y", "0deg");
});
