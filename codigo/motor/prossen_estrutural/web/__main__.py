"""python -m prossen_estrutural.web [--porta 8765] [--pasta <trabalho>] [--abrir] [--pai PID]"""
import argparse
import logging
import os
import threading
import time
import sys
import webbrowser
from pathlib import Path

from .servidor import criar_servidor


def _vigiar_pai(pid):
    """Encerra o servidor quando o processo pai termina (Estructto morto à força não deixa motor órfão)."""
    if os.name == "nt":
        import ctypes
        k = ctypes.windll.kernel32
        h = k.OpenProcess(0x00100000, False, pid)  # SYNCHRONIZE
        if h:
            k.WaitForSingleObject(h, 0xFFFFFFFF)
        os._exit(0)
    while True:
        try:
            os.kill(pid, 0)
        except OSError:
            os._exit(0)
        time.sleep(2)


def main(argv=None):
    ap = argparse.ArgumentParser(prog="python -m prossen_estrutural.web", description="Interface web local do Estructto")
    ap.add_argument("--porta", type=int, default=8765, help="porta em 127.0.0.1 (0 = qualquer livre)")
    ap.add_argument("--pasta", default=str(Path(__file__).resolve().parents[2] / "trabalho"), help="pasta de trabalho")
    ap.add_argument("--abrir", action="store_true", help="abrir o navegador")
    ap.add_argument("--pai", type=int, help="PID do Estructto: o servidor encerra quando ele terminar")
    a = ap.parse_args(argv)
    if a.pai:
        threading.Thread(target=_vigiar_pai, args=(a.pai,), daemon=True).start()
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
