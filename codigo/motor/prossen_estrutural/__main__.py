"""CLI: python -m prossen_estrutural <modelo.json> -o <pasta> [--pdelta] [--formato html,md,json]

Código 0 = ok; 1 = erro de entrada ou mecanismo (mensagem em stderr); 2 = uso errado.
"""
import argparse
import logging
import sys
from pathlib import Path

from . import projeto

_FORMATOS = ("html", "md", "json")


def _f(v, casas=2):
    return "-" if v is None else f"{v:.{casas}f}".replace(".", ",")


def main(argv=None):
    ap = argparse.ArgumentParser(prog="python -m prossen_estrutural",
                                 description="Estructto: calcula um modelo estrutural e grava o relatório (html, md, json).")
    ap.add_argument("modelo", help="arquivo .json do modelo")
    ap.add_argument("-o", "--saida", required=True, help="pasta de saída (criada se não existir)")
    ap.add_argument("--pdelta", action="store_true", help="análise de 2ª ordem P-Δ nas combinações ELU")
    ap.add_argument("--formato", default=",".join(_FORMATOS), help="lista separada por vírgula: html,md,json")
    a = ap.parse_args(argv)
    for fluxo in (sys.stdout, sys.stderr):
        fluxo.reconfigure(errors="replace")  # console cp1252 não quebra com γ
    formatos = [f.strip() for f in a.formato.split(",") if f.strip()]
    if not formatos or any(f not in _FORMATOS for f in formatos):
        ap.error(f"--formato deve ser uma lista de {', '.join(_FORMATOS)}")
    logging.basicConfig(level=logging.WARNING, format="%(levelname)s %(name)s: %(message)s", stream=sys.stderr)
    try:
        modelo = projeto.carregar(a.modelo)
        calc = projeto.calcular(modelo, {"pdelta": True} if a.pdelta else None)
        pasta, nome = Path(a.saida), Path(a.modelo).stem
        arquivos = [projeto.gerar_relatorio(modelo, calc, f, pasta / f"{nome}.{f}")["caminho"] for f in formatos]
    except (projeto.ErroEntrada, projeto.ErroMecanismo) as e:
        print(f"erro: {e}", file=sys.stderr)
        return 1
    except NotImplementedError as e:
        print(f"erro: {e}", file=sys.stderr)
        return 1
    r, est = calc.resumo(), calc.estabilidade
    gz = " ".join(f"γz {d} = {_f(v['valor'], 3)}" for d, v in est.get("gama_z", {}).items())
    b2 = " ".join(f"B2 {d} = {_f(v['max'], 3)}" for d, v in est.get("B2", {}).items())
    print(f"{modelo.nome or nome}: {r['n_barras']} barras, {len(calc.combinacoes)} combinações, "
          f"{r['tempo_s']:.1f} s")
    print(f"Barras reprovadas: {r['n_reprovadas'] - r['n_nao_verificadas']} de {r['n_barras']}"
          + (f"; não verificadas: {r['n_nao_verificadas']}" if r["n_nao_verificadas"] else ""))
    print(f"Taxa máxima: {_f(r['taxa_max'])} (barra {r['barra_critica'] or '-'})")
    print("Estabilidade global: " + (" ".join((gz, b2)).strip() or est.get("motivo", "não aplicável")))
    mj = est.get("majoracao") or {}
    if mj.get("n_majoradas") or mj.get("n_excedem"):  # 2ª ordem global sem P-Δ (γz/B2)
        print(f"2ª ordem global: ações horizontais majoradas em {mj['n_majoradas']} combinações"
              + (f" (fator máx. {_f(mj['fator_max'], 3)})" if mj.get("fator_max") else "")
              + (f"; {mj['n_excedem']} excedem o limite: rode com --pdelta" if mj.get("n_excedem") else ""))
    print("Arquivos: " + ", ".join(arquivos))
    return 0


if __name__ == "__main__":
    sys.exit(main())
