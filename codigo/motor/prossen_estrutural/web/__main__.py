"""python -m prossen_estrutural.web [--porta 8765] [--pasta <trabalho>] [--abrir]"""
import argparse
import logging
import sys
import webbrowser
from pathlib import Path

from .servidor import criar_servidor


def main(argv=None):
    ap = argparse.ArgumentParser(prog="python -m prossen_estrutural.web", description="Interface web local do Estructto")
    ap.add_argument("--porta", type=int, default=8765, help="porta em 127.0.0.1 (0 = qualquer livre)")
    ap.add_argument("--pasta", default=str(Path(__file__).resolve().parents[2] / "trabalho"), help="pasta de trabalho")
    ap.add_argument("--abrir", action="store_true", help="abrir o navegador")
    a = ap.parse_args(argv)
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(name)s: %(message)s", stream=sys.stderr)
    pasta = Path(a.pasta)
    pasta.mkdir(parents=True, exist_ok=True)
    srv = criar_servidor(a.porta, pasta)
    url = f"http://127.0.0.1:{srv.server_address[1]}/"
    print(f"Estructto em {url}  (pasta de trabalho: {srv.pasta}; Ctrl+C encerra)", file=sys.stderr, flush=True)
    if a.abrir:
        webbrowser.open(url)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        srv.server_close()


if __name__ == "__main__":
    main()
