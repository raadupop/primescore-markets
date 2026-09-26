"""Preview the Markets presentation site on loopback port 8091."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def main() -> None:
    directory = Path(__file__).resolve().parents[1] / "sites/markets"
    if not (directory / "index.html").is_file():
        raise SystemExit(f"Missing website: {directory}")
    handler = partial(SimpleHTTPRequestHandler, directory=str(directory))
    try:
        server = ThreadingHTTPServer(("127.0.0.1", 8091), handler)
    except OSError as error:
        raise SystemExit(f"Cannot bind port 8091: {error}") from error
    with server:
        print("Markets: http://127.0.0.1:8091", flush=True)
        print("Ctrl+C stops the website. Dashboard requires start-markets.ps1.", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
