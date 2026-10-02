// Run with: node --test tests/sites/navigation.test.cjs
const {test} = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const script = fs.readFileSync(path.join(__dirname, "../../sites/markets/navigation.js"), "utf8");

function linksAt(origin) {
  const links = [
    {href: "https://primescore.ai/#products"},
    {href: "https://markets.primescore.ai/#platform"},
    {href: "https://markets.primescore.ai/#dashboard", dashboard: true},
    {href: "https://github.com/raadupop/primescore-markets"},
  ].map((link) => ({...link, hasAttribute: () => !!link.dashboard, querySelector: () => null}));
  const notice = {hidden: true};
  vm.runInNewContext(script, {
    URL, location: new URL(origin),
    document: {querySelectorAll: (selector) => selector === "a[href]" ? links : [notice]},
  });
  return {links: links.map((link) => link.href), notice};
}

test("current previews connect brand, Markets and the dashboard", () => {
  for (const port of ["8090", "8091"]) {
    const {links, notice} = linksAt(`http://127.0.0.1:${port}/`);
    assert.deepEqual(links.slice(0, 3), [
      "http://127.0.0.1:8090/#products", "http://127.0.0.1:8091/#platform", "http://127.0.0.1:5080/",
    ]);
    assert.equal(notice.hidden, false);
    assert.equal(links[3], "https://github.com/raadupop/primescore-markets");
  }
});

test("legacy preview ports and loopback hostnames retain their own host", () => {
  for (const host of ["localhost", "[::1]"]) {
    for (const port of ["4173", "4174"]) {
      const {links} = linksAt(`http://${host}:${port}/`);
      assert.deepEqual(links.slice(0, 3), [
        `http://${host}:4173/#products`, `http://${host}:4174/#platform`, `http://${host}:5080/`,
      ]);
    }
  }
});

test("public deployments and unknown local ports keep canonical access guidance", () => {
  for (const origin of ["https://primescore.ai/", "https://markets.primescore.ai/", "https://preview.example:8090/", "http://127.0.0.1:9000/"]) {
    const {links, notice} = linksAt(origin);
    assert.deepEqual(links.slice(0, 3), [
      "https://primescore.ai/#products", "https://markets.primescore.ai/#platform", "https://markets.primescore.ai/#dashboard",
    ]);
    assert.equal(notice.hidden, true);
  }
});
