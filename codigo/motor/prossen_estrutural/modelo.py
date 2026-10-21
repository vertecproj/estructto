"""Modelo estrutural. Unidades: kN, m, kN/m² (ver ESPEC.md §3 e formato JSON v1 em §5)."""
from __future__ import annotations

import copy
import json
import re
from dataclasses import dataclass, field, asdict

MPa = 1000.0  # kN/m²
VERSAO_FORMATO = 2  # 2 = com `ligacoes` (grupos de ligações); sem grupos o arquivo continua saindo como 1
VERSOES_LIDAS = (0, 1, 2)


@dataclass
class No:
    id: str
    x: float
    y: float
    z: float
    apoio: tuple = (False,) * 6  # restrições ux, uy, uz, rx, ry, rz
    molas: tuple = (0.0,) * 6      # rigidez elástica global: kN/m (ux, uy, uz), kN·m/rad (rx, ry, rz)
    recalques: tuple = (0.0,) * 6  # deslocamento prescrito (m, rad) nos GL travados

    def __post_init__(self):
        self.apoio = tuple(bool(a) for a in self.apoio)
        self.molas = tuple(float(a) for a in self.molas)
        self.recalques = tuple(float(a) for a in self.recalques)
        for nome, v in (("apoio", self.apoio), ("molas", self.molas), ("recalques", self.recalques)):
            if len(v) != 6:
                raise ValueError(f"nó {self.id}: {nome} precisa de 6 valores (ux, uy, uz, rx, ry, rz)")
        gls = ("ux", "uy", "uz", "rx", "ry", "rz")
        for k in range(6):
            if self.molas[k] < 0:
                raise ValueError(f"nó {self.id}: mola {gls[k]} negativa ({self.molas[k]})")
            if self.recalques[k] and not self.apoio[k]:
                raise ValueError(f"nó {self.id}: recalque em {gls[k]} = {self.recalques[k]}, mas esse GL "
                                 f"não está travado (apoio); recalque só vale em GL travado")


@dataclass
class Material:
    nome: str
    E: float          # kN/m²
    G: float          # kN/m²
    peso: float = 0.0  # kN/m³
    fy: float | None = None   # kN/m²
    fu: float | None = None   # kN/m²
    fck: float | None = None  # kN/m²


@dataclass
class Secao:
    nome: str
    A: float          # m²
    Iy: float         # m⁴ (eixo local y)
    Iz: float         # m⁴ (eixo local z)
    J: float          # m⁴ torção
    props: dict = field(default_factory=dict)  # h, b, tw, tf, Wy, Zy, Cw, ...


@dataclass
class Barra:
    id: str
    ni: str
    nj: str
    secao: str
    material: str
    rot: float = 0.0     # giro da seção em torno do eixo da barra (rad)
    tipo: str = "aco"    # "aco" | "concreto" | "pff"
    params: dict = field(default_factory=dict)  # Lb, Cb, kx, ky, rotula_i/j, trelica, trecho_rigido_i/j (m), cortante...


@dataclass
class CasoCarga:
    nome: str
    tipo: str  # "G" permanente, "Q" uso, "V" vento, "S" sismo, "E" excepcional
    subtipo: str = ""  # ex.: "edificio_comercial" para ψ


@dataclass
class CargaNodal:
    caso: str
    no: str
    F: tuple  # Fx, Fy, Fz, Mx, My, Mz (global)
    id: str = ""


@dataclass
class CargaDistribuida:
    caso: str
    barra: str
    q: tuple  # qx, qy, qz global, kN/m, uniforme (com q2: valor em ni)
    id: str = ""
    q2: tuple | None = None  # valor em nj (q varia linearmente de q a q2); None = uniforme


@dataclass
class CargaPontual:
    caso: str
    barra: str
    a: float   # m, a partir de ni ao longo da barra
    F: tuple   # Fx, Fy, Fz global, kN
    id: str = ""


@dataclass
class Pavimento:
    nome: str
    z: float               # m; nós com |z - pav.z| <= 0,001 m pertencem ao pavimento
    diafragma: bool = True  # vínculo rígido no plano (ux, uy, rz)


# lista do Modelo e prefixo do id de cada tipo de carga
_CARGAS = {CargaNodal: ("cargas_nodais", "cn"), CargaDistribuida: ("cargas_distribuidas", "cd"),
           CargaPontual: ("cargas_pontuais", "cp")}


@dataclass
class Modelo:
    nos: dict = field(default_factory=dict)
    materiais: dict = field(default_factory=dict)
    secoes: dict = field(default_factory=dict)
    barras: dict = field(default_factory=dict)
    casos: dict = field(default_factory=dict)
    cargas_nodais: list = field(default_factory=list)
    cargas_distribuidas: list = field(default_factory=list)
    peso_proprio: str | None = None  # nome do caso que recebe o peso próprio
    config: dict = field(default_factory=dict)  # tipo_uso, vento, sismo...
    nome: str = ""
    versao_formato: int = 1  # em memória; to_dict grava 2 só quando há `ligacoes`
    cargas_pontuais: list = field(default_factory=list)
    pavimentos: dict = field(default_factory=dict)
    ligacoes: dict = field(default_factory=dict)  # {"grupos": {...}} — aco/ligacoes_modelo.py

    def __post_init__(self):
        self._ultimo_id = {}  # prefixo -> maior número já atribuído (não reaproveita)
        self._max_lista = {}  # prefixo -> (len da lista, maior número nela): add em lote sem varrer a lista a cada item

    def add(self, obj):
        if type(obj) in _CARGAS:
            lista, pref = _CARGAS[type(obj)]
            lista = getattr(self, lista)
            if not obj.id:
                vis = self._max_lista.get(pref)
                if vis is None or vis[0] != len(lista):  # lista mudou por fora (from_dict, remoção): varre de novo
                    usados = [int(mt.group(1)) for c in lista
                              if (mt := re.fullmatch(pref + r"(\d+)", c.id))]
                    vis = (len(lista), max(usados, default=0))
                n = max(vis[1], self._ultimo_id.get(pref, 0)) + 1
                self._ultimo_id[pref] = n
                obj.id = f"{pref}{n}"
                self._max_lista[pref] = (len(lista) + 1, n)
            else:
                self._max_lista.pop(pref, None)  # id explícito: o maior número pode mudar; a próxima automática varre de novo
            lista.append(obj)
            return obj
        destino = {No: self.nos, Material: self.materiais, Secao: self.secoes,
                   Barra: self.barras, CasoCarga: self.casos, Pavimento: self.pavimentos}
        chave = obj.id if hasattr(obj, "id") else obj.nome
        destino[type(obj)][chave] = obj
        return obj

    def to_dict(self):
        """Dict JSON-serializável no formato v1 (ESPEC §5); cópia profunda."""
        def lst(o, *campos):
            d = asdict(o)
            for c in campos:
                d[c] = list(d[c])
            return d
        return {
            "versao_formato": VERSAO_FORMATO if self._tem_ligacoes() else 1,
            "nome": self.nome,
            "nos": {k: {c: x for c, x in lst(v, "apoio", "molas", "recalques").items()
                        if c not in ("molas", "recalques") or any(x)}  # sem mola/recalque: JSON de antes
                    for k, v in self.nos.items()},
            "materiais": {k: asdict(v) for k, v in self.materiais.items()},
            "secoes": {k: asdict(v) for k, v in self.secoes.items()},
            "barras": {k: asdict(v) for k, v in self.barras.items()},
            "casos": {k: asdict(v) for k, v in self.casos.items()},
            "cargas_nodais": [lst(c, "F") for c in self.cargas_nodais],
            "cargas_distribuidas": [lst(c, "q", "q2") if c.q2 is not None
                                    else {k: v for k, v in lst(c, "q").items() if k != "q2"}  # uniforme: sem q2
                                    for c in self.cargas_distribuidas],
            "cargas_pontuais": [lst(c, "F") for c in self.cargas_pontuais],
            "pavimentos": {k: asdict(v) for k, v in self.pavimentos.items()},
            "peso_proprio": self.peso_proprio,
            "config": copy.deepcopy(self.config),
            **({"ligacoes": copy.deepcopy(self.ligacoes)} if self._tem_ligacoes() else {}),
        }

    def _tem_ligacoes(self):
        return isinstance(self.ligacoes, dict) and bool(self.ligacoes.get("grupos"))

    @classmethod
    def from_dict(cls, d):
        """Lê v1 ou v0 (núcleo antigo: sem versao_formato, ids, cargas_pontuais, pavimentos)."""
        d = copy.deepcopy(d)
        versao = d.get("versao_formato", 0)
        if versao not in VERSOES_LIDAS:
            raise ValueError(f"versao_formato {versao} não suportada (máximo {VERSAO_FORMATO})")
        m = cls(nome=d.get("nome", ""), peso_proprio=d.get("peso_proprio"),
                config=d.get("config", {}), ligacoes=d.get("ligacoes") or {})
        for k, c in (("nos", No), ("materiais", Material), ("secoes", Secao),
                     ("barras", Barra), ("casos", CasoCarga), ("pavimentos", Pavimento)):
            for chave, v in d.get(k, {}).items():
                if "apoio" in v:
                    v["apoio"] = tuple(v["apoio"])
                obj = c(**v)
                ident = obj.id if hasattr(obj, "id") else obj.nome
                if chave != ident:
                    raise ValueError(f"{k}: chave {chave!r} difere do id/nome {ident!r}")
                m.add(obj)
        for k, c, vet in (("cargas_nodais", CargaNodal, "F"), ("cargas_distribuidas", CargaDistribuida, "q"),
                          ("cargas_pontuais", CargaPontual, "F")):
            vistos = set()
            for v in d.get(k, []):
                v[vet] = tuple(v[vet])
                if v.get("q2") is not None:
                    v["q2"] = tuple(v["q2"])
                if v.get("id"):
                    if v["id"] in vistos:
                        raise ValueError(f"{k}: id {v['id']!r} repetido")
                    vistos.add(v["id"])
                m.add(c(**v))
        return m

    def to_json(self, caminho):
        with open(caminho, "w", encoding="utf-8") as f:
            json.dump(self.to_dict(), f, ensure_ascii=False, indent=1)

    @classmethod
    def from_json(cls, caminho):
        with open(caminho, encoding="utf-8") as f:
            return cls.from_dict(json.load(f))
