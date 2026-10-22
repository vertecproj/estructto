"""Campo editável de cada peça desenhada (contrato `geometria`: chave `campo` em chapas, cantoneiras, parafusos, porcas e
soldas). O projetista (WPF) clica na peça e vai ao campo. Chapas: pelo `nome` EXATO do contrato; as demais, por tipo.
A tabela só nomeia campos que existem no `esquema` do tipo; o que não casa fica sem `campo` (teste:
test_ligacoes_campos). Soldas com perna derivada (ligacao_tracao: 0,6 t) apontam para o campo que a deriva."""

_CHAPA = {   # nome da chapa no contrato -> campos candidatos (o 1º que o tipo tem)
    "chapa": ["t"], "chapa soldada": ["t_base"], "chapa simples": ["t_chapa", "t"], "chapa de ligação": ["t"],
    "chapa de extremidade": ["t_chapa"], "chapa de topo": ["tp"], "enrijecedor do pilar": ["t_enr_pilar"],
    "enrijecedor": ["t_enrijecedor"], "assento": ["t_assento"], "enrijecedor da chapa de topo": ["t_enrijecedor"], "chapa de alma": ["t_alma"], "chapa de mesa superior": ["t_mesa"],
    "chapa de mesa inferior": ["t_mesa"], "tala da alma": ["t_tala_alma"], "tala da mesa": ["t_tala_mesa"],
    "gusset": ["t_gusset"], "chapa de apoio da porca": ["t_chapa"], "chapa de nó": ["t_gusset"],
}
_PARAFUSO = ["d", "d_mm", "d_mesa_mm"]
_SOLDA = {"solda_filete": ["b"], "ligacao_tracao": ["t"], "chapa_topo_ajustada": ["b_mesa"], "chapa_topo_estendida": ["b_mesa"],
          "flange_plate": ["b_solda_mesa"]}


def anotar(tipo, geo, dados, nomes):
    """Põe `campo` (setdefault: o gerador pode ter posto o seu) nas peças de `geo`. `nomes`: campos do esquema do tipo."""
    if not isinstance(geo, dict):
        return geo

    def pôr(peca, cands):
        if "campo" not in peca:
            c = next((x for x in cands if x in nomes), None)
            if c:
                peca["campo"] = c
    for ch in geo.get("chapas") or []:
        pôr(ch, _CHAPA.get(ch.get("nome"), []))
    for ca in geo.get("cantoneiras") or []:
        sup = (dados or {}).get("cantoneira_superior")
        if tipo == "apoio_cantoneira" and sup and ca.get("perfil") == sup:
            pôr(ca, ["cantoneira_superior"])
        pôr(ca, ["secao"] if tipo == "cantoneira_simples_trelica" else ["cantoneira"])
    for pf in (geo.get("parafusos") or []):
        pôr(pf, _PARAFUSO)
    for pf in geo.get("porcas") or []:
        pôr(pf, ["D_barra"])
    for so in geo.get("soldas") or []:
        pôr(so, _SOLDA.get(tipo, ["b_solda"]))
    return geo
