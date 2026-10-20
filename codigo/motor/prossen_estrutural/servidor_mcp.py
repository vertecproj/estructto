"""Servidor MCP do Estructto (ESPEC §8): adaptador fino sobre `projeto.py` (zero fórmula aqui).

Transporte stdio: `python -m prossen_estrutural.servidor_mcp [--pasta <trabalho>]`.
Estado do processo: um modelo atual e o último cálculo; toda ferramenta que muta o modelo descarta o cálculo.
stdout é o canal do protocolo: nada de `print`; logs vão para stderr.
"""
import argparse
import functools
import json
import logging
import sys
import threading
from pathlib import Path
from typing import Annotated, Any, Literal

from mcp.server.mcpserver import MCPServer
from mcp.server.mcpserver.exceptions import ResourceError, ToolError
from mcp.types import ToolAnnotations
from pydantic import Field

from . import projeto
from .projeto import ErroEntrada, ErroMecanismo, ErroNaoEncontrado

log = logging.getLogger(__name__)

PASTA_PADRAO = Path(__file__).resolve().parent.parent / "trabalho"

INSTRUCOES = """\
Estructto: análise e verificação de pórticos espaciais de aço (NBR 8800), perfis formados a frio \
(NBR 14762) e concreto armado (NBR 6118), com ações de vento (NBR 6123) e sismo (NBR 15421).

Unidades: forças em kN, comprimentos em m, momentos em kN·m, tensões/módulos do modelo em kN/m². Valores em MPa só \
aparecem em chaves com sufixo _MPa (fck_MPa, fy_MPa, E_MPa...). Eixos globais: X e Y horizontais, Z para cima \
(peso próprio e cargas gravitacionais têm Fz negativo). Deslocamentos em m e rad.

Fluxo típico: modelo_novo -> estrutura_gerar (pórtico plano, edifício ou galpão) ou no_adicionar/secao_adicionar/\
material_adicionar/barra_adicionar -> caso_adicionar -> carga_adicionar -> config_definir (tipo de uso, vento, \
sismo, peso próprio) -> acoes_gerar (opcional; projeto_calcular já faz) -> projeto_calcular -> verificacao_listar \
(taxa > 1 = reprovada) / estabilidade_consultar / resultados_consultar -> relatorio_gerar. Para começar com um \
modelo pronto: modelo_exemplos + modelo_abrir_exemplo. Comece sempre por modelo_validar se algo falhar.

Editar o modelo descarta o último cálculo: rode projeto_calcular de novo. Listas grandes aceitam limite e \
deslocamento e informam o total. Arquivos só dentro da pasta de trabalho (caminhos relativos a ela).

AVISO: ferramenta de apoio. Todo resultado exige verificação por engenheiro responsável (ART). As normas \
implementadas e suas limitações estão em normas_implementadas.
"""

mcp = MCPServer("estructto", instructions=INSTRUCOES)

_estado: dict[str, Any] = {"modelo": projeto.novo_modelo(), "calculo": None}
_pasta: Path = PASTA_PADRAO
_trava = threading.RLock()  # ferramentas síncronas rodam em threads: serializa o acesso ao estado


def definir_pasta(pasta) -> Path:
    """Define a pasta de trabalho (todo caminho de arquivo é relativo a ela e confinado nela)."""
    global _pasta
    _pasta = Path(pasta).resolve()
    return _pasta


def _caminho(relativo: str) -> Path:
    return projeto.caminho_seguro(_pasta, relativo)


def _modelo():
    return _estado["modelo"]


def _calculo():
    if _estado["calculo"] is None:
        raise ToolError("não há cálculo: rode projeto_calcular antes (qualquer edição do modelo descarta o cálculo).")
    return _estado["calculo"]


def _ferramenta(*, ro=False, destrutiva=False, idem=False, muta=None):
    """Registra a função como ferramenta: anotações, trava, tradução de erros e descarte do cálculo.
    `muta` (padrão: not ro) = a ferramenta altera o modelo, logo o cálculo deixa de valer."""
    muta = (not ro) if muta is None else muta

    def deco(fn):
        @functools.wraps(fn)
        def envoltorio(*a, **k):
            with _trava:
                try:
                    r = fn(*a, **k)
                except ToolError:
                    raise
                except (ErroEntrada, ErroNaoEncontrado) as e:
                    raise ToolError(str(e)) from None
                except ErroMecanismo as e:
                    raise ToolError(f"{e}. Confira apoios e rótulas (modelo_validar, modelo_geometria): "
                                    "a estrutura precisa estar estável.") from None
                except NotImplementedError as e:
                    raise ToolError(f"recurso ainda não disponível nesta versão: {e}") from None
                except Exception as e:  # bug: log em stderr, mensagem curta ao cliente
                    log.exception("erro interno em %s", fn.__name__)
                    raise ToolError(f"erro interno ({type(e).__name__}): {e}") from None
                if muta:
                    _estado["calculo"] = None
                return r
        return mcp.tool(annotations=ToolAnnotations(
            read_only_hint=ro, destructive_hint=destrutiva, idempotent_hint=idem, open_world_hint=False))(envoltorio)
    return deco


# ---------------------------------------------------------------- tipos e utilidades

Limite = Annotated[int, Field(ge=1, le=2000, description="máximo de itens devolvidos (padrão 200)")]
Deslocamento = Annotated[int, Field(ge=0, description="quantos itens pular (paginação; padrão 0)")]
TipoElemento = Literal["no", "material", "secao", "barra", "caso", "carga_nodal", "carga_distribuida",
                       "carga_pontual", "pavimento"]
Alvo = Literal["deslocamentos", "reacoes", "esforcos", "flechas", "pavimentos"]


def _dados(**campos) -> dict:
    """Só os campos informados (≠ None)."""
    return {k: v for k, v in campos.items() if v is not None}


def _pagina(itens: list, limite: int, deslocamento: int) -> dict[str, Any]:
    total = len(itens)
    fatia = itens[deslocamento:deslocamento + limite]
    r: dict[str, Any] = {"total": total, "deslocamento": deslocamento, "retornados": len(fatia), "itens": fatia}
    if deslocamento + limite < total:
        r["proximo_deslocamento"] = deslocamento + limite
    return r


def _pagina_dict(d: dict, limite: int, deslocamento: int) -> dict[str, Any]:
    ids = list(d)
    sel = ids[deslocamento:deslocamento + limite]
    r: dict[str, Any] = {"total": len(ids), "deslocamento": deslocamento, "retornados": len(sel),
                         "dados": {k: d[k] for k in sel}}
    if deslocamento + limite < len(ids):
        r["proximo_deslocamento"] = deslocamento + limite
    return r


def _resumo_atual() -> dict[str, Any]:
    return projeto.resumo(_modelo())


# ---------------------------------------------------------------- modelo: arquivo e visão geral

@_ferramenta(destrutiva=True, idem=True)
def modelo_novo(nome: Annotated[str, Field(description="nome do projeto (texto livre)")] = "") -> dict[str, Any]:
    """Cria um modelo vazio e descarta o modelo atual e o cálculo (salve antes com modelo_salvar).
    Exemplo: modelo_novo(nome="Pórtico do galpão")."""
    _estado["modelo"] = projeto.novo_modelo(nome)
    return _resumo_atual()


@_ferramenta(destrutiva=True, idem=True)
def modelo_abrir(caminho: Annotated[str, Field(description="arquivo .json dentro da pasta de trabalho, ex. 'galpao.json'")]
                 ) -> dict[str, Any]:
    """Abre um modelo salvo (.json, formato do Estructto) como modelo atual; descarta o atual e o cálculo.
    Exemplo: modelo_abrir(caminho="meu_projeto.json")."""
    _estado["modelo"] = projeto.carregar(_caminho(caminho))
    return _resumo_atual()


@_ferramenta(destrutiva=True, idem=True, muta=False)
def modelo_salvar(caminho: Annotated[str, Field(description="arquivo .json dentro da pasta de trabalho; sobrescreve se existir")]
                  ) -> dict[str, Any]:
    """Grava o modelo atual em arquivo (cria subpastas). Devolve {caminho, bytes}.
    Exemplo: modelo_salvar(caminho="obra1/portico.json")."""
    return projeto.salvar(_modelo(), _caminho(caminho))


@_ferramenta(ro=True, idem=True)
def modelo_exemplos() -> dict[str, Any]:
    """Lista os modelos de exemplo embutidos (viga biapoiada, pórtico de aço, galpão de aço, edifício de
    concreto) com descrição. Use o campo `nome` em modelo_abrir_exemplo."""
    itens = projeto.exemplos()
    return {"total": len(itens), "itens": itens}


@_ferramenta(destrutiva=True, idem=True)
def modelo_abrir_exemplo(nome: Annotated[str, Field(description="nome do exemplo, ex. 'portico_aco' (veja modelo_exemplos)")]
                         ) -> dict[str, Any]:
    """Carrega um exemplo como modelo atual; descarta o atual e o cálculo.
    Exemplo: modelo_abrir_exemplo(nome="viga_biapoiada")."""
    _estado["modelo"] = projeto.abrir_exemplo(nome)
    return _resumo_atual()


@_ferramenta(ro=True, idem=True)
def modelo_exportar_json() -> dict[str, Any]:
    """Devolve o modelo atual completo no formato JSON do Estructto (versao_formato 1): {"modelo": {...}}.
    Unidades do arquivo: kN, m, kN/m² (E, G, fy, fu, fck em kN/m²)."""
    return {"modelo": projeto.para_dict(_modelo())}


@_ferramenta(destrutiva=True, idem=True)
def modelo_importar_json(modelo: Annotated[dict[str, Any], Field(
        description="modelo no formato JSON do Estructto (o mesmo de modelo_exportar_json), unidades kN, m, kN/m²")]
        ) -> dict[str, Any]:
    """Substitui o modelo atual pelo JSON informado; descarta o atual e o cálculo.
    Exemplo: modelo_importar_json(modelo=<conteúdo de modelo_exportar_json>["modelo"])."""
    _estado["modelo"] = projeto.de_dict(modelo)
    return _resumo_atual()


@_ferramenta(destrutiva=True, idem=True)
def modelo_importar_cype(
        caminho: Annotated[str, Field(description="arquivo .ed3 na pasta de trabalho ou absoluto em 'C:\\CYPE Ingenieros\\Projetos\\'")]
        ) -> dict[str, Any]:
    """Importa obra do CYPE 3D 2016 (.ed3): nós, apoios, barras, perfis, aço, articulações, hipóteses e cargas; o que não for importado vem em avisos.
    Substitui o modelo atual e descarta o cálculo.
    Exemplo: modelo_importar_cype(caminho="obra.ed3")."""
    m = projeto.importar_cype(caminho, pasta=_pasta)
    _estado["modelo"] = m
    res = _resumo_atual()
    res["avisos"] = list(m.avisos)
    return res


@_ferramenta(ro=True, idem=True)
def modelo_resumo() -> dict[str, Any]:
    """Resumo do modelo atual: contagens, materiais, seções, casos, pavimentos, caixa envolvente (m), peso próprio
    e configuração (config)."""
    return _resumo_atual()


@_ferramenta(ro=True, idem=True)
def modelo_validar() -> dict[str, Any]:
    """Valida o modelo atual. Itens de nível 'erro' impedem o cálculo (ex.: sem_apoio, referencia_ausente,
    barra_comprimento_nulo); 'aviso' não impede. Devolve {ok, n_erros, n_avisos, itens:[{nivel, codigo, mensagem, ref}]}."""
    itens = projeto.validar(_modelo())
    n_err = sum(1 for i in itens if i["nivel"] == "erro")
    return {"ok": n_err == 0, "n_erros": n_err, "n_avisos": len(itens) - n_err, "itens": itens}


@_ferramenta(ro=True, idem=True)
def modelo_geometria() -> dict[str, Any]:
    """Geometria do modelo (nós, barras com eixos locais e comprimento L em m, cargas por caso, pavimentos, caixa).
    Pode ser grande em modelos com muitas barras; para conferir só contagens use modelo_resumo."""
    return projeto.geometria(_modelo())


@_ferramenta(ro=True, idem=True)
def elementos_listar(
        tipo: Annotated[TipoElemento, Field(description="tipo de elemento a listar")],
        filtro: Annotated[dict[str, Any] | None, Field(description="igualdade de campos, ex. {\"caso\": \"G1\"}; None = todos")] = None,
        limite: Limite = 200, deslocamento: Deslocamento = 0) -> dict[str, Any]:
    """Lista elementos do modelo atual com paginação. Devolve {total, retornados, itens, proximo_deslocamento?}.
    Exemplo: elementos_listar(tipo="carga_distribuida", filtro={"caso": "G1"}, limite=50)."""
    return _pagina(projeto.listar(_modelo(), tipo, filtro), limite, deslocamento)


# ---------------------------------------------------------------- edição do modelo

@_ferramenta()
def no_adicionar(
        x: Annotated[float, Field(description="coordenada X global (m)")],
        y: Annotated[float, Field(description="coordenada Y global (m)")],
        z: Annotated[float, Field(description="coordenada Z global (m); Z é vertical, para cima")],
        id: Annotated[str | None, Field(description="identificador único, ex. 'n1'; vazio = automático")] = None,
        apoio: Annotated[str | list[bool], Field(description=(
            "vínculo: 'engaste', 'rotula' (translações travadas), 'rolete_z' (só Z travado), 'livre', ou lista de 6 "
            "booleanos [ux, uy, uz, rx, ry, rz] com true = restringido"))] = "livre") -> dict[str, Any]:
    """Adiciona um nó. Devolve o nó criado.
    Exemplo: no_adicionar(id="n1", x=0, y=0, z=0, apoio="engaste")."""
    return projeto.adicionar(_modelo(), "no", _dados(id=id, x=x, y=y, z=z, apoio=apoio))


@_ferramenta()
def material_adicionar(
        aco: Annotated[str | None, Field(description="aço da lista (materiais_listar), ex. 'A-572 GR.50'")] = None,
        concreto: Annotated[float | None, Field(description="fck do concreto (MPa), de 20 a 90")] = None,
        alfa_E: Annotated[float | None, Field(description="só com concreto: fator do módulo de elasticidade pelo agregado (adimensional, padrão 1,0)")] = None,
        armadura: Annotated[str | None, Field(description="aço de armadura, ex. 'CA-50'")] = None,
        nome: Annotated[str | None, Field(description="só no material explícito: nome")] = None,
        E_MPa: Annotated[float | None, Field(description="só no explícito: módulo de elasticidade (MPa)")] = None,
        G_MPa: Annotated[float | None, Field(description="só no explícito: módulo transversal (MPa)")] = None,
        peso_kN_m3: Annotated[float | None, Field(description="só no explícito: peso específico (kN/m³)")] = None,
        fy_MPa: Annotated[float | None, Field(description="só no explícito: tensão de escoamento (MPa)")] = None,
        fu_MPa: Annotated[float | None, Field(description="só no explícito: tensão de ruptura (MPa)")] = None,
        fck_MPa: Annotated[float | None, Field(description="só no explícito: fck (MPa)")] = None) -> dict[str, Any]:
    """Adiciona um material. Informe UMA forma: aco, ou concreto (+alfa_E), ou armadura, ou o explícito
    (nome, E_MPa, G_MPa, peso_kN_m3, fy_MPa, fu_MPa, fck_MPa). O nome do material criado é o id usado em barra_adicionar.
    Exemplos: material_adicionar(aco="A-572 GR.50"); material_adicionar(concreto=30)."""
    return projeto.adicionar(_modelo(), "material", _dados(
        aco=aco, concreto=concreto, alfa_E=alfa_E, armadura=armadura, nome=nome, E_MPa=E_MPa, G_MPa=G_MPa,
        peso_kN_m3=peso_kN_m3, fy_MPa=fy_MPa, fu_MPa=fu_MPa, fck_MPa=fck_MPa))


@_ferramenta()
def secao_adicionar(
        perfil: Annotated[str | None, Field(description="perfil do catálogo (perfis_listar), ex. 'W310x38.7'")] = None,
        forma: Annotated[Literal["retangular", "circular", "I", "tubo_circular", "tubo_retangular", "U", "Ue", "U_pf",
                                 "cantoneira"] | None, Field(description="forma paramétrica (medidas em m)")] = None,
        nome: Annotated[str | None, Field(description="nome da seção; vazio = gerado das medidas")] = None,
        b: Annotated[float | None, Field(description="largura (m): retangular, I, tubo_retangular, U, Ue, U_pf, cantoneira")] = None,
        h: Annotated[float | None, Field(description="altura (m): retangular, I, tubo_retangular, U, Ue, U_pf, cantoneira")] = None,
        d: Annotated[float | None, Field(description="diâmetro (m): circular, tubo_circular")] = None,
        tw: Annotated[float | None, Field(description="espessura da alma (m): I, U")] = None,
        tf: Annotated[float | None, Field(description="espessura da mesa (m): I, U")] = None,
        t: Annotated[float | None, Field(description="espessura da parede (m): tubos, Ue, U_pf, cantoneira")] = None,
        D: Annotated[float | None, Field(description="largura do enrijecedor de borda (m): Ue")] = None,
        r: Annotated[float | None, Field(description="raio de concordância (m): I (opcional)")] = None,
        soldado: Annotated[bool | None, Field(description="perfil I soldado (true) ou laminado (false)")] = None,
        A: Annotated[float | None, Field(description="só na seção explícita: área (m²)")] = None,
        Iy: Annotated[float | None, Field(description="só no explícito: inércia em torno do eixo local y, eixo forte (m⁴)")] = None,
        Iz: Annotated[float | None, Field(description="só no explícito: inércia em torno do eixo local z (m⁴)")] = None,
        J: Annotated[float | None, Field(description="só no explícito: constante de torção (m⁴)")] = None,
        props: Annotated[dict[str, Any] | None, Field(description="só no explícito: propriedades extras (h, b, tw, tf em m...)")] = None
        ) -> dict[str, Any]:
    """Adiciona uma seção transversal. Informe UMA forma: perfil (catálogo), forma + medidas, ou a explícita
    (A, Iy, Iz, J, props). Medidas exigidas: retangular(b,h) circular(d) I(h,b,tw,tf) tubo_circular(d,t)
    tubo_retangular(h,b,t) U(h,b,tw,tf) Ue(h,b,D,t) U_pf(h,b,t) cantoneira(b,h,t). O nome criado é o id para barra_adicionar.
    Exemplos: secao_adicionar(perfil="W310x38.7"); secao_adicionar(forma="retangular", b=0.2, h=0.5)."""
    return projeto.adicionar(_modelo(), "secao", _dados(
        perfil=perfil, forma=forma, nome=nome, b=b, h=h, d=d, tw=tw, tf=tf, t=t, D=D, r=r, soldado=soldado,
        A=A, Iy=Iy, Iz=Iz, J=J, props=props))


@_ferramenta()
def barra_adicionar(
        ni: Annotated[str, Field(description="id do nó inicial (já existente)")],
        nj: Annotated[str, Field(description="id do nó final (já existente)")],
        secao: Annotated[str, Field(description="nome de uma seção existente")],
        material: Annotated[str, Field(description="nome de um material existente")],
        id: Annotated[str | None, Field(description="identificador único, ex. 'b1'; vazio = automático")] = None,
        tipo: Annotated[Literal["aco", "pff", "concreto"] | None, Field(
            description="tipo de verificação; vazio = automático (material com fck → concreto; seção Ue/U_pf → pff; senão aço)")] = None,
        rot: Annotated[float, Field(description="giro da seção em torno do eixo da barra (rad)")] = 0.0,
        params: Annotated[dict[str, Any] | None, Field(description=(
            "opcionais: kx, ky, kz (fatores de flambagem, adimensional), Lb (comprimento destravado, m), Cb, "
            "rotula_i, rotula_j (true libera os momentos na extremidade), trelica (true = só axial), "
            "papel ('viga'|'pilar'), elemento_desloc, An (m²), Ct, fyk_MPa, le_x, le_y (m, pilar de concreto)"))] = None
        ) -> dict[str, Any]:
    """Adiciona uma barra entre dois nós já criados, com seção e material já criados.
    Exemplo: barra_adicionar(id="b1", ni="n1", nj="n2", secao="W310x38.7", material="A-572 GR.50", params={"kx": 2.0})."""
    return projeto.adicionar(_modelo(), "barra", _dados(
        id=id, ni=ni, nj=nj, secao=secao, material=material, tipo=tipo, rot=rot, params=params))


@_ferramenta()
def caso_adicionar(
        nome: Annotated[str, Field(description="nome único do caso de carga, ex. 'G1', 'Q1'")],
        tipo: Annotated[Literal["G", "Q", "V", "S", "E"], Field(
            description="G permanente, Q variável (acidental), V vento, S sobrecarga especial/sismo-variável, E excepcional")],
        subtipo: Annotated[str, Field(description=(
            "Q: uso para ψ da NBR 8681 (ex. 'escritorio', 'comercial'); V: grupo de exclusão; vazio = padrão"))] = ""
        ) -> dict[str, Any]:
    """Adiciona um caso de carga. Casos de vento/sismo automáticos (V+X, SX...) vêm de acoes_gerar.
    Exemplo: caso_adicionar(nome="Q1", tipo="Q", subtipo="escritorio")."""
    return projeto.adicionar(_modelo(), "caso", _dados(nome=nome, tipo=tipo, subtipo=subtipo or None))


_CARGA = {"nodal": ("no", "F"), "distribuida": ("barra", "q"), "pontual": ("barra", "a", "F")}


@_ferramenta()
def carga_adicionar(
        tipo: Annotated[Literal["nodal", "distribuida", "pontual"], Field(description="tipo de carga")],
        caso: Annotated[str, Field(description="nome de um caso existente")],
        no: Annotated[str | None, Field(description="nodal: id do nó carregado")] = None,
        barra: Annotated[str | None, Field(description="distribuida/pontual: id da barra carregada")] = None,
        F: Annotated[list[float] | None, Field(description=(
            "nodal: 6 valores [Fx, Fy, Fz (kN), Mx, My, Mz (kN·m)] globais; pontual: 3 valores [Fx, Fy, Fz] (kN) globais"))] = None,
        q: Annotated[list[float] | None, Field(description=(
            "distribuida: 3 valores [qx, qy, qz] (kN/m) globais, uniformes no comprimento da barra; "
            "gravidade para baixo = qz negativo"))] = None,
        a: Annotated[float | None, Field(description="pontual: posição a partir do nó inicial da barra (m), 0 ≤ a ≤ L")] = None
        ) -> dict[str, Any]:
    """Adiciona uma carga. Parâmetros por tipo: nodal(no, F[6]); distribuida(barra, q[3]); pontual(barra, a, F[3]).
    Exemplos: carga_adicionar(tipo="distribuida", caso="Q1", barra="b2", q=[0, 0, -10]);
    carga_adicionar(tipo="nodal", caso="V1", no="n3", F=[10, 0, 0, 0, 0, 0])."""
    dados = _dados(no=no, barra=barra, F=F, q=q, a=a)
    obrig = _CARGA[tipo]
    falta, sobra = [c for c in obrig if c not in dados], [c for c in dados if c not in obrig]
    if falta or sobra:
        raise ToolError(f"carga {tipo}: informe {', '.join(obrig)}"
                        + (f" (faltam: {', '.join(falta)})" if falta else "")
                        + (f" (não se aplicam: {', '.join(sobra)})" if sobra else ""))
    return projeto.adicionar(_modelo(), f"carga_{tipo}", {"caso": caso, **dados})


@_ferramenta()
def pavimento_adicionar(
        nome: Annotated[str, Field(description="nome do pavimento, ex. 'P1'")],
        z: Annotated[float, Field(description="cota do pavimento (m); nós com |z − cota| ≤ 0,001 m pertencem a ele")],
        diafragma: Annotated[bool, Field(description="true = laje rígida no plano (ux, uy, rz comuns aos nós do pavimento)")] = True
        ) -> dict[str, Any]:
    """Adiciona um pavimento (necessário para vento, sismo, γz e B2). estrutura_gerar tipo 'edificio' já cria os pavimentos.
    Exemplo: pavimento_adicionar(nome="P1", z=3.0)."""
    return projeto.adicionar(_modelo(), "pavimento", {"nome": nome, "z": z, "diafragma": diafragma})


@_ferramenta()
def lote_adicionar(itens: Annotated[list[dict[str, Any]], Field(description=(
        "lista de {\"tipo\": <tipo de elemento>, \"dados\": {...}} na ordem de criação (nós antes das barras). "
        "tipos: no, material, secao, barra, caso, carga_nodal, carga_distribuida, carga_pontual, pavimento; "
        "'dados' como nas ferramentas *_adicionar (materiais/seções no formato de spec: {\"aco\": ...}, {\"perfil\": ...})"))]
        ) -> dict[str, Any]:
    """Adiciona vários elementos de uma vez; atômico (se um item falha, nada é criado; o erro indica 'itens[3].ni').
    Devolve {criados: [{tipo, id}]}.
    Exemplo: lote_adicionar(itens=[{"tipo": "no", "dados": {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": "engaste"}},
    {"tipo": "no", "dados": {"id": "n2", "x": 0, "y": 0, "z": 3}}])."""
    return projeto.adicionar_lote(_modelo(), itens)


@_ferramenta(idem=True)
def elemento_editar(
        tipo: Annotated[TipoElemento, Field(description="tipo do elemento")],
        id: Annotated[str, Field(description="id (nós, barras, cargas) ou nome (material, seção, caso, pavimento)")],
        dados: Annotated[dict[str, Any], Field(description=(
            "campos a alterar (parcial); o id não muda. params de barra é mesclado (valor null apaga a chave). "
            "Mesmos nomes das ferramentas *_adicionar"))]) -> dict[str, Any]:
    """Edita parcialmente um elemento. Nada muda se houver erro. Devolve o elemento atualizado.
    Exemplo: elemento_editar(tipo="no", id="n2", dados={"z": 3.5}); elemento_editar(tipo="barra", id="b1", dados={"params": {"kx": 2.0}})."""
    return projeto.editar(_modelo(), tipo, id, dados)


@_ferramenta(destrutiva=True)
def elemento_remover(
        tipo: Annotated[TipoElemento, Field(description="tipo do elemento")],
        id: Annotated[str, Field(description="id ou nome do elemento")],
        cascata: Annotated[bool, Field(description="true = remove também os dependentes (barras de um nó, cargas de uma barra...)")] = False
        ) -> dict[str, Any]:
    """Remove um elemento. Se houver dependentes e cascata=False o erro lista os dependentes (nada é removido).
    Devolve {removidos: [{tipo, id}]}. Exemplo: elemento_remover(tipo="barra", id="b3", cascata=True)."""
    return projeto.remover(_modelo(), tipo, id, cascata)


@_ferramenta()
def estrutura_gerar(dados: Annotated[dict[str, Any], Field(description=(
        "especificação do gerador. Comum: tipo ('portico_plano'|'edificio'|'galpao'), material (spec ou nome existente, "
        "ex. {\"aco\": \"A-572 GR.50\"} ou {\"concreto\": 30}), secoes ({pilar, viga, ...}: nome existente ou spec "
        "como {\"perfil\": \"W310x38.7\"} / {\"forma\": \"retangular\", \"b\": 0.25, \"h\": 0.5}), apoio ('engaste'|'rotula'), "
        "origem [x, y, z] (m). portico_plano: vaos_x [m], alturas [m por pavimento, de baixo para cima]. "
        "edificio: vaos_x, vaos_y, alturas (cria pavimentos com diafragma). galpao: vao, altura_pilar, altura_cumeeira, "
        "n_modulos, espacamento (m), secoes pilar/viga/terca; contraventamento (bool, padrão false; "
        "true exige secoes.terca), secoes.contraventamento (opcional; padrão = terça)"))]) -> dict[str, Any]:
    """Gera nós, barras e pavimentos de uma tipologia (pórtico plano, edifício ou galpão) no modelo atual e cria os
    casos G1 (peso próprio) e Q1 se não existirem. Cargas não são criadas: use carga_adicionar/lote_adicionar.
    Devolve {nos_criados, barras_criadas, pavimentos_criados, ids, avisos}.
    Exemplo: estrutura_gerar(dados={"tipo": "portico_plano", "material": {"aco": "A-572 GR.50"},
    "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W360x51"}}, "vaos_x": [6, 6], "alturas": [4], "apoio": "engaste"})."""
    return projeto.gerar_estrutura(_modelo(), dados)


@_ferramenta(idem=True)
def config_definir(dados: Annotated[dict[str, Any], Field(description=(
        "chaves de config (merge raso): tipo_uso (ex. 'comercial', 'residencial'; define ψ), "
        "tipo_edificacao (null | 'tipo1' | 'tipo2' | 'separadas'), pdelta (bool), peso_proprio (nome do caso G "
        "que recebe o peso próprio, ex. 'G1'), variabilidade ('grandes'|'pequenas'), armadura ('CA-50'...), "
        "cobrimento_m (m), caa ('I'..'IV'), vento ({V0 m/s, S1, S3, categoria 'I'..'V', classe 'A'|'B'|'C', "
        "direcoes ['+X','-X','+Y','-Y'], dimensoes {lx, ly} m, Ca?, e para galpões: tipo='galpao', geometria "
        "{vao, altura_pilar, altura_cumeeira, n_modulos, espacamento}, permeabilidade, cpi}; null remove), "
        "sismo ({zona 0-4, classe_terreno 'A'..'E', categoria_utilizacao, R, sistema, sistema_resistente, "
        "dimensoes {lx, ly} m para torção acidental}; null remove)"))]
        ) -> dict[str, Any]:
    """Altera a configuração do modelo. vento e sismo são substituídos inteiros (envie o objeto completo). Devolve a config.
    Exemplo: config_definir(dados={"peso_proprio": "G1", "vento": {"V0": 35, "S1": 1.0, "categoria": "IV", "classe": "B",
    "S3": 1.0, "dimensoes": {"lx": 15, "ly": 10}}})."""
    return {"config": projeto.configurar(_modelo(), dados)}


@_ferramenta(idem=True)
def acoes_gerar() -> dict[str, Any]:
    """Recria os casos e cargas automáticos de vento (V+X, V-X, V+Y, V-Y; NBR 6123) e sismo (SX, SY; NBR 15421) a partir
    de config.vento, config.sismo e dos pavimentos. Opcional: projeto_calcular já faz isso. Devolve casos, forcas
    por pavimento (kN), parametros e avisos."""
    return projeto.gerar_acoes(_modelo())


# ---------------------------------------------------------------- análise

@_ferramenta(ro=True, idem=True)
def combinacoes_listar() -> dict[str, Any]:
    """Lista as combinações de ações geradas (NBR 8681) para o modelo atual: nome, estado (ELU, ELU_EXC, ELS_QP,
    ELS_FREQ, ELS_RARA) e fatores por caso."""
    itens = projeto.combinacoes(_modelo())
    return {"total": len(itens), "itens": itens}


def _filtrar_partes(r: dict, incluir: list[Alvo] | None) -> dict:
    if incluir is None:
        return r
    return {k: v for k, v in r.items() if k in ("nome", "pdelta") or k in incluir}


Incluir = Annotated[list[Alvo] | None, Field(description=(
    "partes do resultado a devolver (deslocamentos, reacoes, esforcos, flechas, pavimentos); None = todas. "
    "esforcos e flechas são grandes: peça só o necessário"))]


@_ferramenta(ro=True, idem=True)
def analise_caso(
        caso: Annotated[str, Field(description="nome de um caso de carga (caso_adicionar)")],
        pdelta: Annotated[bool, Field(description="true = análise de 2ª ordem P-Δ")] = False,
        incluir: Incluir = None) -> dict[str, Any]:
    """Analisa um caso de carga sem fatores. Deslocamentos em m/rad por nó; reações em kN e kN·m (globais) nos apoios;
    esforços (N, Vy, Vz, T, My, Mz em kN e kN·m, locais) e flechas locais (m) por barra em vários pontos x (m).
    Exemplo: analise_caso(caso="Q1", incluir=["reacoes", "deslocamentos"])."""
    return _filtrar_partes(projeto.analisar_caso(_modelo(), caso, pdelta), incluir)


@_ferramenta(ro=True, idem=True)
def analise_combinacao(
        nome: Annotated[str, Field(description="nome da combinação, exatamente como em combinacoes_listar")],
        pdelta: Annotated[bool | None, Field(description="None = usa config.pdelta; true/false força")] = None,
        incluir: Incluir = None) -> dict[str, Any]:
    """Analisa uma combinação de ações (os fatores já aplicados). Mesmo formato de analise_caso.
    Exemplo: analise_combinacao(nome="ELU Q1 princ.", incluir=["reacoes"])."""
    return _filtrar_partes(projeto.analisar_combinacao(_modelo(), nome, pdelta), incluir)


@_ferramenta(ro=True, idem=True)
def analise_modal(n_modos: Annotated[int, Field(ge=1, le=60, description="número de modos de vibração")] = 6) -> dict[str, Any]:
    """Análise modal com massa de G + ψ2·Q. Devolve frequencias_hz, periodos_s, participacao [[px, py, pz]] por modo
    e modos (deslocamentos modais por nó).
    Exemplo: analise_modal(n_modos=3)."""
    return projeto.modal(_modelo(), n_modos)


@_ferramenta(ro=True, idem=True, muta=False)
def projeto_calcular(
        pdelta: Annotated[bool | None, Field(description="None = usa config.pdelta; true = 2ª ordem P-Δ nas combinações ELU")] = None,
        barras: Annotated[list[str] | None, Field(description="ids das barras a verificar; None = todas")] = None,
        n_pontos: Annotated[int | None, Field(ge=3, le=101, description="pontos por barra nos esforços (padrão 13)")] = None
        ) -> dict[str, Any]:
    """Cálculo completo: valida, gera ações, combina, analisa e verifica cada barra pela norma do seu material, deslocamentos,
    γz (concreto) e B2 (aço). Guarda o cálculo para as consultas. Devolve resumo enxuto: resumo (taxa_max, barra_critica,
    reprovadas), estabilidade, 10 piores barras (taxa > 1 = reprovada), avisos e contagem de combinações; o detalhe vem de
    verificacao_listar, verificacao_barra, resultados_consultar. Exemplo: projeto_calcular()."""
    opcoes = _dados(pdelta=pdelta, barras=barras, n_pontos=n_pontos)
    calculo = projeto.calcular(_modelo(), opcoes)
    _estado["calculo"] = calculo
    d = calculo.to_dict()
    piores = [l for l in d["verificacao"] if l["taxa"] is not None][:10]
    avisos = d["avisos"]
    estados: dict[str, int] = {}
    for c in d["combinacoes"]:
        estados[c["estado"]] = estados.get(c["estado"], 0) + 1
    return {"modelo": d["modelo"], "data": d["data"], "opcoes": d["opcoes"], "normas": [{k: n[k] for k in ("norma", "edicao", "modulo")} for n in d["normas"]],
            "resumo": d["resumo"],
            "estabilidade": d["estabilidade"], "piores_barras": piores,
            "deslocamentos": {"n_verticais": len(d["deslocamentos"]["verticais"]),
                              "n_horizontais": len(d["deslocamentos"]["horizontais"]),
                              "reprovados": [x for x in d["deslocamentos"]["verticais"] + d["deslocamentos"]["horizontais"]
                                             if not x["ok"]][:20]},
            "n_combinacoes": len(d["combinacoes"]), "combinacoes_por_estado": estados,
            "n_avisos": len(avisos), "avisos": avisos[:30],
            "proximo_passo": "verificacao_listar (todas as barras), verificacao_barra, resultados_consultar, relatorio_gerar"}


@_ferramenta(ro=True, idem=True, muta=False)
def resultados_consultar(
        alvo: Annotated[Alvo, Field(description="o que consultar")],
        ids: Annotated[list[str] | None, Field(description="nós (deslocamentos, reacoes), barras (esforcos, flechas) ou pavimentos; None = todos")] = None,
        combinacao: Annotated[str | None, Field(description="nome de uma combinação; None = envoltória {max, min, comb_max, comb_min}")] = None,
        estados: Annotated[list[Literal["ELU", "ELU_EXC", "ELS_QP", "ELS_FREQ", "ELS_RARA"]] | None, Field(
            description="estados da envoltória; None = ELU e ELU_EXC (esforcos, reacoes) ou ELS (deslocamentos, flechas, pavimentos)")] = None,
        limite: Limite = 200, deslocamento: Deslocamento = 0) -> dict[str, Any]:
    """Consulta resultados do último cálculo (exige projeto_calcular). Unidades: deslocamentos m/rad, reações kN e kN·m
    (globais), esforços e flechas locais (kN, kN·m, m). A paginação é por id (nó/barra/pavimento); prefira passar `ids`.
    Exemplo: resultados_consultar(alvo="reacoes"); resultados_consultar(alvo="esforcos", ids=["b7"], combinacao="ELU Q1 princ.")."""
    dados = projeto.resultados(_calculo(), alvo, ids, combinacao, estados)
    return {"alvo": alvo, "combinacao": combinacao, **_pagina_dict(dados, limite, deslocamento)}


@_ferramenta(ro=True, idem=True, muta=False)
def verificacao_listar(
        reprovadas: Annotated[bool | None, Field(description="true = só barras com taxa > 1")] = None,
        tipo: Annotated[Literal["aco", "pff", "concreto"] | None, Field(description="filtra por tipo de barra")] = None,
        taxa_min: Annotated[float | None, Field(ge=0, description="só barras com taxa de utilização ≥ este valor (adimensional)")] = None,
        limite: Limite = 200, deslocamento: Deslocamento = 0) -> dict[str, Any]:
    """Verificação por barra do último cálculo (exige projeto_calcular), da maior taxa para a menor. Cada linha: barra, tipo,
    norma, taxa (solicitação/resistência; > 1 reprova), criterio, item da norma, combinacao, x_m, armadura (concreto).
    Exemplo: verificacao_listar(reprovadas=True) ou verificacao_listar(taxa_min=0.8, limite=20)."""
    filtro = _dados(reprovadas=reprovadas, tipo=tipo, taxa_min=taxa_min)
    return _pagina(projeto.verificacao(_calculo(), filtro or None), limite, deslocamento)


@_ferramenta(ro=True, idem=True, muta=False)
def verificacao_barra(barra: Annotated[str, Field(description="id da barra, ex. 'b7'")]) -> dict[str, Any]:
    """Detalhe da verificação de uma barra: linha de verificacao_listar + `detalhes` do motor da norma (resistências,
    esbeltezes, itens) + `por_combinacao` [{combinacao, taxa, criterio}]. Exige projeto_calcular.
    Exemplo: verificacao_barra(barra="b7")."""
    return projeto.verificacao_barra(_calculo(), barra)


@_ferramenta(ro=True, idem=True, muta=False)
def estabilidade_consultar() -> dict[str, Any]:
    """Estabilidade global do último cálculo: γz por direção (NBR 6118 15.5.3; nós fixos se ≤ 1,1), B2 por pavimento
    (NBR 8800 Anexo D) e avisos. Exige projeto_calcular. 'aplicavel' = false sem pavimentos e ações horizontais."""
    return projeto.estabilidade(_calculo())


@_ferramenta(ro=True, idem=True)
def cype_comparar(
        caminho: Annotated[str, Field(description="arquivo .ed3 na pasta de trabalho ou absoluto em 'C:\\CYPE Ingenieros\\Projetos\\'")],
        limite: Limite = 200,
        deslocamento: Deslocamento = 0
        ) -> dict[str, Any]:
    """Compara o cálculo do Estructto com os resultados gravados pelo CYPE 3D no arquivo .ed3 (diferenças em %).
    Devolve a tabela de comparação (deslocamentos, reações, esforços), taxas de aproveitamento e avisos.
    Exemplo: cype_comparar(caminho="obra.ed3")."""
    res = projeto.comparar_cype(caminho, pasta=_pasta)
    total = len(res["tabela"])
    pag = _pagina(res["tabela"], limite, deslocamento)
    res["total"] = total
    res["deslocamento"] = deslocamento
    res["retornados"] = pag["retornados"]
    res["tabela"] = pag["itens"]
    if "proximo_deslocamento" in pag:
        res["proximo_deslocamento"] = pag["proximo_deslocamento"]
    res["total_taxas"] = len(res["taxas"])
    res["taxas"] = res["taxas"][deslocamento:deslocamento + limite]
    return res


# ---------------------------------------------------------------- ligações, catálogo, normas

@_ferramenta(ro=True, idem=True)
def ligacao_tipos() -> dict[str, Any]:
    """Tipos de ligação de aço verificáveis (parafuso, solda_filete, chapa_simples, ligacao_tracao) com seus campos
    (nome, unidade, descrição, padrão, opcoes) e os esforços aceitos. Use antes de ligacao_verificar."""
    itens = projeto.tipos_ligacao()
    return {"total": len(itens), "itens": itens}


@_ferramenta(ro=True, idem=True)
def ligacao_verificar(
        tipo: Annotated[str, Field(description="tipo de ligacao_tipos, ex. 'parafuso'")],
        dados: Annotated[dict[str, Any], Field(description="campos do tipo (medidas em m; diâmetros de parafuso em chaves *_mm)")],
        esforcos: Annotated[dict[str, float], Field(description="esforços solicitantes de cálculo (kN), chaves conforme o tipo")]
        ) -> dict[str, Any]:
    """Verifica uma ligação isolada (independe do modelo). Devolve taxa_max, criterio, item, ok e a lista de verificações
    {nome, solicitante, resistente, unidade, taxa, item}. Consulte ligacao_tipos para os campos.
    Exemplo: ligacao_verificar(tipo="parafuso", dados={...}, esforcos={...})."""
    return projeto.verificar_ligacao(tipo, dados, esforcos)


@_ferramenta(muta=False, idem=True)
def ligacoes_identificar() -> dict[str, Any]:
    """Identifica as ligações nos nós (base, viga–pilar mesa/alma, viga–viga, emenda, gusset, nó de treliça) e agrupa
    as iguais (tipo + seções + ângulo + rótula + material) em modelo.ligacoes, guardando as edições anteriores
    (grupos travados/manuais). Não descarta o cálculo. Devolve {grupos, membros, novos}. Depois: ligacoes_grupos."""
    return projeto.ligacoes_identificar(_modelo())


@_ferramenta(ro=True, idem=True)
def ligacoes_grupos(limite: Limite = 200, deslocamento: Deslocamento = 0) -> dict[str, Any]:
    """Grupos de ligações do modelo: id, nome, tipo, assinatura, travado, projeto {tipo_ligacao, dados} e membros
    {no, barra, ponta}. Com cálculo (projeto_calcular), cada grupo vem dimensionado: taxa por membro, governante
    (pior membro e combinação) e taxa_max. Rode ligacoes_identificar antes."""
    return _pagina(projeto.ligacoes_grupos(_modelo(), _estado["calculo"]), limite, deslocamento)


@_ferramenta(muta=False)
def ligacoes_grupo_editar(
        acao: Annotated[Literal["renomear", "travar", "projeto", "mover", "dividir", "unir"],
                        Field(description="o que fazer com os grupos")],
        grupo: Annotated[str | None, Field(description="id do grupo (renomear, travar, projeto, dividir)")] = None,
        nome: Annotated[str | None, Field(description="novo nome (renomear)")] = None,
        travado: Annotated[bool | None, Field(description="travar/destravar (travar)")] = None,
        tipo_ligacao: Annotated[str | None, Field(description="projeto: placa_base ou um tipo de ligacao_tipos; vazio = sem projeto")] = None,
        dados: Annotated[dict[str, Any] | None, Field(description="projeto: campos do tipo de ligação (m, mm em *_mm)")] = None,
        membros: Annotated[list[dict[str, str]] | None, Field(description="mover/dividir: [{no, barra, ponta: i|j}]")] = None,
        destino: Annotated[str | None, Field(description="mover: grupo de destino; vazio = grupo novo")] = None,
        grupos: Annotated[list[str] | None, Field(description="unir: ids (todos vão para o primeiro)")] = None,
        ) -> dict[str, Any]:
    """Edita os grupos de ligações (o usuário manda): renomear, travar, escolher o projeto (tipo_ligacao + dados),
    mover membros entre grupos, dividir (membros vão para um grupo novo) ou unir grupos. Grupo travado não aceita
    mover/unir. Edições sobrevivem a ligacoes_identificar. Exemplo: ligacoes_grupo_editar(acao="renomear",
    grupo="G2", nome="Cobertura")."""
    return projeto.ligacoes_editar(_modelo(), _dados(acao=acao, grupo=grupo, nome=nome, travado=travado,
                                                     tipo_ligacao=tipo_ligacao, dados=dados, membros=membros,
                                                     destino=destino, grupos=grupos))


@_ferramenta(ro=True, idem=True)
def ligacoes_grupo_dimensionar(grupo: Annotated[str, Field(description="id do grupo, ex. 'G1'")]) -> dict[str, Any]:
    """Dimensiona o grupo pela pior ligação: verifica o projeto do grupo com os esforços de cada membro em todas as
    combinações ELU (exige projeto_calcular). Devolve membros com taxa e combinação, governante, taxa_max, ok e o
    resultado completo do governante; o projeto vale para todos os membros. Sem projeto: governante pela demanda."""
    return projeto.ligacoes_dimensionar(_modelo(), _calculo(), grupo)


@_ferramenta(ro=True, idem=True)
def perfis_listar(
        filtro: Annotated[str, Field(description="trecho do nome, ex. 'W310'; vazio = todos")] = "",
        familia: Annotated[Literal["W", "HP", "Ue", "U"] | None, Field(description="família do catálogo; None = todas")] = None,
        limite: Limite = 200, deslocamento: Deslocamento = 0) -> dict[str, Any]:
    """Lista perfis do catálogo (W/HP laminados Gerdau; Ue e U formados a frio NBR 6355) com paginação.
    Exemplo: perfis_listar(filtro="W310", familia="W")."""
    return _pagina(projeto.catalogo_perfis(filtro, familia), limite, deslocamento)


@_ferramenta(ro=True, idem=True)
def perfil_consultar(nome: Annotated[str, Field(description="nome do perfil, ex. 'W310x38.7'")]) -> dict[str, Any]:
    """Propriedades de um perfil (massa kg/m, A cm², Ix, Iy cm⁴, Wx, Zx cm³, rx, ry cm, J cm⁴, Cw cm⁶...) e `secao`
    (em m, pronta para secao_adicionar). Exemplo: perfil_consultar(nome="W310x38.7")."""
    return projeto.perfil(nome)


@_ferramenta(ro=True, idem=True)
def materiais_listar() -> dict[str, Any]:
    """Materiais disponíveis: aços estruturais (fy_MPa, fu_MPa), faixa de fck do concreto (MPa) e aços de armadura."""
    return projeto.materiais_disponiveis()


@_ferramenta(ro=True, idem=True)
def normas_implementadas() -> dict[str, Any]:
    """Normas, edições, itens verificados e limitações declaradas do que o software implementa (descrição própria do
    software, não o texto da ABNT). Consulte antes de confiar num resultado fora do caso comum."""
    itens = projeto.normas_implementadas()
    return {"total": len(itens), "itens": itens}


# ---------------------------------------------------------------- relatório

@_ferramenta(destrutiva=True, idem=True, muta=False)
def relatorio_gerar(
        caminho: Annotated[str, Field(description="arquivo de saída dentro da pasta de trabalho, ex. 'relatorios/obra1.html'; sobrescreve")],
        formato: Annotated[Literal["html", "md", "json"], Field(description="html (imprimível), md ou json")] = "html"
        ) -> dict[str, Any]:
    """Gera o relatório do modelo atual e do último cálculo (exige projeto_calcular; se editou o modelo, calcule de novo).
    Devolve {caminho, bytes} (caminho absoluto do arquivo gravado).
    Exemplo: relatorio_gerar(caminho="relatorios/portico.html", formato="html")."""
    calc = _calculo()
    return projeto.gerar_relatorio(calc.modelo, calc, formato, _caminho(caminho))


# ---------------------------------------------------------------- recursos

@mcp.resource("modelo://atual", mime_type="application/json", description="Modelo atual completo (JSON do Estructto, kN e m).")
def recurso_modelo() -> str:
    return json.dumps(projeto.para_dict(_modelo()), ensure_ascii=False)


@mcp.resource("calculo://ultimo", mime_type="application/json",
              description="Último cálculo (resumo, verificação de todas as barras, deslocamentos, estabilidade, avisos).")
def recurso_calculo() -> str:
    if _estado["calculo"] is None:
        raise ResourceError("não há cálculo: rode a ferramenta projeto_calcular antes.")
    return json.dumps(_estado["calculo"].to_dict(), ensure_ascii=False)


# ---------------------------------------------------------------- execução

def main(argv=None) -> None:
    ap = argparse.ArgumentParser(prog="python -m prossen_estrutural.servidor_mcp",
                                 description="Servidor MCP (stdio) do Estructto.")
    ap.add_argument("--pasta", default=str(PASTA_PADRAO), help="pasta de trabalho para abrir/salvar arquivos")
    args = ap.parse_args(argv)
    logging.basicConfig(stream=sys.stderr, level=logging.INFO, format="%(levelname)s %(name)s: %(message)s")
    log.info("pasta de trabalho: %s", definir_pasta(args.pasta))
    mcp.run()


if __name__ == "__main__":
    main()
