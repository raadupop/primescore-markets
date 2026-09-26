"""Public dashboard links must remain usable without a local engine or JavaScript."""
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[2]


class PageLinks(HTMLParser):
    def __init__(self, path):
        super().__init__()
        self.links = []
        self.assets = []
        self.ids = set()
        self.feed(path.read_text(encoding="utf-8"))

    def handle_starttag(self, tag, attributes):
        attrs = dict(attributes)
        if "id" in attrs:
            self.ids.add(attrs["id"])
        if tag == "a":
            self.links.append(attrs)
        if tag in ("script", "img"):
            self.assets.append(attrs.get("src", ""))
        if tag == "link" and attrs.get("rel") in ("stylesheet", "icon", "preload"):
            self.assets.append(attrs.get("href", ""))


def test_public_navigation_has_real_destinations_and_assets():
    directory = ROOT / "sites/markets"
    page = PageLinks(directory / "index.html")
    dashboard_links = [link for link in page.links if "data-dashboard" in link]
    assert dashboard_links, "The product must have a visible route to dashboard access"
    for link in dashboard_links:
        destination = urlparse(link["href"])
        assert destination.hostname not in ("localhost", "127.0.0.1", "::1")
        if destination.hostname == "markets.primescore.ai" or not destination.hostname:
            assert destination.fragment == "dashboard"
            assert "dashboard" in PageLinks(ROOT / "sites/markets/index.html").ids
        else:
            assert destination.path.endswith("/docs/ENGINE.md")
            assert (ROOT / "docs/ENGINE.md").is_file()
    for link in page.links:
        destination = urlparse(link.get("href", ""))
        if not destination.netloc and not destination.path and destination.fragment:
            assert destination.fragment in page.ids
    for asset in page.assets:
        assert (directory / asset.lstrip("/")).is_file(), asset
