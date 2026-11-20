# Fabrique tests/SocleGreffesCheck/commun_rig_greffe.sql, la copie d essai de COMMUN_RIG.dbo.GREFFE
# du banc SocleGreffesCheck (lot 4). Usage, a la racine du depot :
#   python -I tests/SocleGreffesCheck/generer_commun_rig_greffe.py tests/SocleGreffesCheck/commun_rig_greffe.sql
# Sources : la classe V2 GREFFE.cs (colonnes, types) et l extrait trace des 40 greffes exploites.

import re, json, io, sys

classe = r'D:\RigApplication\Source\Outils\DLL\RigTools\SQL\COMMON\GREFFE.cs'
extrait = r'C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\controles\bases-greffes.json'
sortie = sys.argv[1]

src = open(classe, encoding='utf-8', errors='replace').read()
cols = []
for m in re.finditer(r'Champs (GRF_[A-Z0-9_]+) de type ([a-z]+(?:\([0-9a-z, ]+\))?) (NOT NULL|NULL)', src):
    if m.group(1) not in [c[0] for c in cols]:
        cols.append((m.group(1), m.group(2), m.group(3)))
d = json.load(open(extrait, encoding='utf-8'))
beta = {'4302': '1', '2801': '1', '3802': '1', '6601': '0'}

def q(s):
    return "'" + s.replace("'", "''") + "'"

out = io.StringIO()
w = out.write
w("-- COPIE D'ESSAI de COMMUN_RIG.dbo.GREFFE pour le banc SocleGreffesCheck (lot 4, SOC-007 / SOC-003).\n")
w("-- Jouee dans une base JETABLE du SQL Server local, jamais ailleurs.\n")
w("--\n")
w("-- Colonnes : les " + str(len(cols)) + " de la classe V2 " + classe + "\n")
w("-- (commentaires « Champs X de type Y NULL|NOT NULL dans la BD »), dans leur ordre.\n")
w("-- Lignes : les 40 greffes exploites de " + extrait + "\n")
w("-- (COMMUN_RIG.dbo.GREFFE, GRF_IS_EXPLOIT = 1, lu le 2026-11-20, conv-2057) : code, nom, base, serveur reels.\n")
w("-- Marque beta et marque outre-mer : carnet\\socle.json, SOC-007, source COMMUN_RIG.dbo.GREFFE lue le\n")
w("-- 2026-11-20 (beta 1 pour Le Puy, Chartres, Vienne ; 0 pour Perpignan ; vide pour les 36 autres ;\n")
w("-- outre-mer 1 pour les 6). Les autres colonnes sont NEUTRES (0, '' ou NULL) : aucune valeur ni aucun\n")
w("-- secret de production. GRF_PARAM_TEST d'Amiens est un texte de 9 000 caracteres : il essaie la\n")
w("-- reprise d'une colonne varchar(max). Deux greffes NON exploites (GRF_IS_EXPLOIT = 0) en plus : la\n")
w("-- reprise doit les laisser hors du catalogue.\n")
w("-- Fichier genere par generer_commun_rig_greffe.py (meme dossier), qui lit ces deux sources (lot 4, 2026-11-20).\n\n")
w("CREATE TABLE dbo.GREFFE (\n")
w(",\n".join("    [" + n + "] " + t.upper() + " " + nn for n, t, nn in cols))
w(",\n    CONSTRAINT [PK_GREFFE] PRIMARY KEY ([GRF_CODE_GREFFE])\n);\n")


def ligne(code, nom, base, serveur, exploit, betav, drom, test=None):
    vals = []
    for n, t, nn in cols:
        if n == 'GRF_CODE_GREFFE': v = q(code)
        elif n == 'GRF_NOM': v = q(nom)
        elif n == 'GRF_NOMBASE_BD': v = q(base)
        elif n == 'GRF_SERVEUR_BD': v = q(serveur)
        elif n == 'GRF_IS_EXPLOIT': v = exploit
        elif n == 'GRF_IS_GREFFE_BETA': v = betav
        elif n == 'GRF_IS_DROM': v = drom
        elif n == 'GRF_IS_RIG': v = '1'
        elif n == 'GRF_PARAM_TEST' and test: v = test
        elif nn == 'NOT NULL': v = '0' if t in ('bit', 'int') else "''"
        else: v = 'NULL'
        vals.append(v)
    return ("INSERT INTO dbo.GREFFE (" + ", ".join("[" + n + "]" for n, _, _ in cols) + ")\n    VALUES ("
            + ", ".join(vals) + ");\n")


for g in d['bases']:
    code = g['GRF_CODE_GREFFE']
    drom = '1' if code.startswith('97') or code.startswith('98') else '0'
    test = "REPLICATE(CAST('Amiens-8' AS varchar(max)), 1125)" if code == '8002' else None
    w(ligne(code, g['GRF_NOM'], g['GRF_NOMBASE_BD'], g['GRF_SERVEUR_BD'], '1', beta.get(code, 'NULL'), drom, test))
w("-- Non exploites : ne doivent PAS entrer au catalogue.\n")
w(ligne('9998', 'Greffe de recette (non exploite)', 'RIG_RECETTE', 'SQL-DEV\\DEV', '0', 'NULL', '0'))
w(ligne('9999', 'Greffe de developpement (non exploite)', 'RIG_DEV', 'SQL-DEV\\DEV', '0', '1', '0'))
open(sortie, 'w', encoding='utf-8', newline='').write(out.getvalue())
print(len(cols), "colonnes,", len(d['bases']), "greffes")
