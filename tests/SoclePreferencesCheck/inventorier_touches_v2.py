# Inventaire OUTILLE des raccourcis clavier de RIG V2 (SOC-027 T01, lot 4 du plan « Reconstruction de RIG — phase 2 »).
#
# Usage, a la racine du depot :
#   python -I tests/SoclePreferencesCheck/inventorier_touches_v2.py Pont/InventaireTouchesV2.cs
#
# Il lit le code de la V2 (D:\RigApplication\Source, lecture seule) : dans chaque fichier .cs (hors *.Designer.cs) d'un
# dossier de procedure PROC_*, chaque comparaison d'une touche de fonction, d'Entree ou d'Echap
# (« == Keys.F7 », « case Keys.Enter: »), hors ligne de commentaire. Pour chaque touche trouvee, il lit les lignes qui
# suivent et classe ce qu'elle fait (rechercher, valider la ligne, actualiser...). Il ecrit Pont/InventaireTouchesV2.cs :
# une ligne par (procedure, touche, action), avec son fichier:ligne dans la V2.
#
# Le banc tests/SoclePreferencesCheck refait la MEME lecture (meme expression) et exige que chaque couple
# (procedure, touche) du code de la V2 soit dans l'inventaire (SOC-027 CA01) : un raccourci ajoute dans la V2 et
# absent de la fiche le rend rouge. Ce script est alors a relancer, et sa sortie relue.

import os, re, sys

BASE = 'D:/RigApplication/Source'
COMPARAISON = re.compile(r'(?:==\s*Keys\.|case\s+Keys\.)(F\d{1,2}|Enter|Return|Escape)\b')

# Les noms des ecrans de la V2, tels qu'un agent les connait (titres des fiches
# D:\RigApplication\Documentation\reference\proc\*.md et du menu de la V2).
NOMS = {
    'PROC_ACCUEIL': "Accueil de l'ancien RIG",
    'PROC_ACCUEIL_PARAM': "Parametres de l'accueil",
    'PROC_ACTES_NUM': 'Actes numerises',
    'PROC_ACTREJ': 'Actes rejetes par Infogreffe',
    'PROC_ACTREJ_CMD_WEB': 'Commandes web rejetees',
    'PROC_ACTRET': "Retention d'actes",
    'PROC_BATCH': 'Traitements en lot (batch)',
    'PROC_BATEAU': 'Navires',
    'PROC_BEN': 'Beneficiaires effectifs',
    'PROC_CFENET_RET': 'Retours CFE-NET',
    'PROC_COFFRE_FORT': 'Coffre-fort',
    'PROC_CORRESP_MIGRATION': 'Correspondances de migration',
    'PROC_CORRESP_ORACLE': 'Correspondances Oracle',
    'PROC_DBEN': 'Divergences BEN',
    'PROC_DEMANDE': 'Demandes',
    'PROC_EDI_RPJC_INS': 'EDI RPJC (inscriptions)',
    'PROC_EDI_RPVAINS': 'EDI RPVA (inscriptions)',
    'PROC_ENV_BOD': 'Envoi au BODACC',
    'PROC_FACTURE': 'Facture',
    'PROC_FCLIENT': 'Fiche client',
    'PROC_GCLIENT': 'Gestion des clients',
    'PROC_GCOM': 'Gestion des commandes',
    'PROC_GDEST_INST': "Destinataires d'une instance",
    'PROC_GEDJUD': 'GED judiciaire',
    'PROC_GESTAUDS': 'Gestion des audiences',
    'PROC_GESTBOD': 'Publications BODACC',
    'PROC_GESTIONHABILITATION': 'Habilitations',
    'PROC_GESTIONMENU': 'Gestion du menu',
    'PROC_GEXPERT': 'Experts',
    'PROC_GJUGE': 'Juges',
    'PROC_IMPRIMANTEWINDOWS': 'Imprimantes Windows',
    'PROC_IMP_WIN': "Imprimantes du greffe",
    'PROC_INPIPAY': 'Reversements INPI',
    'PROC_IPSUIVI': 'Suivi des preventions',
    'PROC_ISURV': 'Surveillance des injonctions',
    'PROC_JCAISSE': 'Journal de caisse',
    'PROC_JOIN': "Jonction d'instances",
    'PROC_OPE2': "Ecran d'essai des developpeurs (OPE2)",
    'PROC_OPE3': "Ecran d'essai des developpeurs (OPE3)",
    'PROC_PARAM_VIDIGED': 'Parametres VidiGED',
    'PROC_PDF_WRITER': 'Pivoter des pages',
    'PROC_POOLEDITION': "File des editions",
    'PROC_PREAUD': "Preparation d'audience",
    'PROC_RAPPRO_ACTEURS': 'Rapprocher des acteurs',
    'PROC_RECHAFF': "Recherche d'affaires",
    'PROC_RECHENC': "Recherche d'encaissements",
    'PROC_RECHERCHE_AUDCAB': "Recherche d'audience ou de cabinet",
    'PROC_RECHERCHE_BATEAU': 'Recherche de navires',
    'PROC_RECHERCHE_DOSSIERJUDICIAIRE': 'Recherche de dossier judiciaire',
    'PROC_RECHERCHE_ENTREPRISE_INFOGREFFE': "Recherche d'entreprise Infogreffe",
    'PROC_RECHERCHE_INSTANCE': "Recherche d'instance",
    'PROC_RECHERCHE_MESURE': 'Recherche de mesure',
    'PROC_RECHERCHE_PREVENTION': 'Recherche de prevention',
    'PROC_RECHERCHE_PROCEDURECOLLECTIVE': 'Recherche de procedure collective',
    'PROC_RECHRECHE_JUGE': 'Recherche de juge',
    'PROC_RECHRECHE_MANDATAIRE_PC': 'Recherche de mandataire',
    'PROC_REJETSKBISXML': 'Rejets des Kbis XML',
    'PROC_REJETS_BILANS': 'Bilans en rejet',
    'PROC_RELCLI': 'Releve de compte',
    'PROC_REQUI': 'Requisitions',
    'PROC_RETAUD': "Retour d'audience",
    'PROC_RETOURLRAR': 'Retours LRAR',
    'PROC_RIGCLIENTNEWSRIG': "Actualites de l'accueil",
    'PROC_RIGPARAMACCUEIL': "Reglages de l'accueil (video)",
    'PROC_ROFSURV': 'Surveillance des ordonnances',
    'PROC_ROFSURVEILLANCES': 'Surveillances ROF',
    'PROC_RSURV': 'Surveillance des relances',
    'PROC_SRBE_RAPPEL': 'Rappels RBE',
    'PROC_SURVBOD': 'Surveillance BODACC',
    'PROC_SURVMES': 'Surveillance des mesures',
    'PROC_TABLEAU_LOT_2': 'Tableau des lots',
    'PROC_TRAITEMENTNPAI': 'Courriers revenus (NPAI)',
    'PROC_VDOSSIER': 'Dossier RCS',
    'PROC_VGED': 'Dossier et sa GED',
    'PROC_VNOTIF': 'Notifications',
    'PROC_VTRID': 'Tribunal digital',
    'PROC_WIKI': 'Aide du greffe (wiki)',
}

# Ce que fait la touche, lu dans les lignes qui suivent sa comparaison : le premier motif reconnu gagne.
# fix-ok: six touches sortaient « autre » (mesure du 2026-11-21) : Echap sans effet de REQUI (frmREQUI.cs:460),
# arret du chargement de l'arbre (OPE_VGED.cs:282, CancelAsync), et trois recherches dont l'appel tombe au-dela de
# 8 lignes (OPE_RECHERCHE_DJ.cs:277) ou passe par _handlesEnterKey / ope.GetValues : motifs ajoutes, fenetre a 10.
ACTIONS = [
    ('sans-effet', r'Keys\.Escape: break'),
    ('arreter-chargement', r'CancelAsync'),
    ('liste-entree', r"ProcessDataGridViewKey|empeche l'activation de la ligne suivante|Enter\"?\)?\s*return false|Enter\)\s*return false"),
    # Entree qui ne fait que prendre la valeur du champ (intervalle des alertes de l'accueil, date du releve de compte).
    ('valider-saisie', r'_intervalSlideshowAlertes|_txtEditEncours'),
    ('valider-ligne', r'valider la ligne|ValiderRecherche|_Valider\(\)|_valider\(\)|Traiter\(_itemSelected|ActionMetierListViewEnter|_ShowFormValidation'),
    ('terminer', r'Terminer\(\)'),
    ('effacer-criteres', r'RAZ\(\)|_RetourEtatInitial'),
    ('adresse', r'Adresse|SaisirAdresse|_DoCallAddressEditor'),
    ('masquer-encaissements', r'ETP_ENCAISSEMENT_REPRIS'),
    ('remplir-automatiquement', r'_AutoRemplissageSaisieIP'),
    ('derniers-resultats', r'btnLastDCA_Click|btnLastStatut_Click|btnDCAStatut_Click'),
    ('aide', r'TexteAide'),
    ('essai-developpeur', r'rgbTest|_styleDisplayBtnEtapes'),
    ('ouvrir-ligne', r'VoirLigneSelected|_ExecuteAlerte|_ExecuteProcessusFromMenu|_DisplayNews|ModifierNews|_Modifier\(\)|OnKeyEnterDataGridView|GetValueRowSelected|_GoToSaisie|IsClickToExpand|OnButtonClick\(TypeButtonRigToolBar\.EDITER'),
    ('fermer', r'Close\(\)|Annuler\(\)|_Annuler|fullScreen = false|MaximizeOrRestore'),
    ('actualiser', r'_Actualiser|_Reload|LoadNavigationPage|ReloadVidigedBases|_PoursuivreChargement|ChargerBateaux|ChargerDonnees\(\); // actualisation|_opeConfigLot1|_opeLstImp|page\.Reload|GetAllDestinataires'),
    ('rechercher', r'[Rr]echerch|Refresh|ChargerDonnees|Search|_Charger|LancerRechercher|Initialiser\("SELECT|RemplirDest|processusSearch|utilisateurSearch|_actualiserLv|MotCle_Validated|_OnKeyEnterInTxtSearch|bRech_Click|GetDemandeByDatamatrix|FocusNumLRAR|_handlesEnterKey|ope\.GetValues\(ope\.VerifUlt'),
]
TOUCHE = {'Enter': 'Entree', 'Return': 'Entree', 'Escape': 'Echap'}


def classer(contexte, touche):
    for action, motif in ACTIONS:
        if re.search(motif, contexte):
            return action
    return 'autre'


# La fin de la BRANCHE d'une touche : la touche suivante comparee, un « else », la fin du « case », la methode suivante.
# Sans elle, les lignes d'apres melaient la branche voisine (mesure du 2026-11-21 : F3 et F4 de REQUI classees
# « effacer les criteres », l'Entree des correspondances classee « fermer »).
BORNE = re.compile(r'case\s+Keys\.\w+\s*:|\bdefault\s*:|\belse\b|(?:KeyCode|keyData|keyCode|simpleKey)\s*==\s*Keys\.\w+'
                   r'|\bbreak\s*;|\b(?:private|protected|public|internal)\s')

# Les branches que la lecture automatique coupe mal (une condition imbriquee sur la meme touche), lues a la main :
# (fichier, ligne) -> action. Une ligne qui bouge dans la V2 sort de cette liste : sa touche est alors reclassee,
# ou le script s'arrete sur « non classee ».
LUES_A_LA_MAIN = {
    ('OPE_BLOC_SAISIE.cs', 246): 'rechercher',   # PROC_BATCH : F5, F7, Entree -> la recherche (l. 246-256)
    # F7 et Entree ne font que valider le champ (Focus) : l'appel de la recherche y est en commentaire dans la V2.
    ('OPE_RECHERCHE_GESTAUDS.cs', 119): 'valider-saisie',
    ('OPE_RECHERCHE_GESTAUDS.cs', 120): 'valider-saisie',
    ('OPE_EDI_RPJC_INS_RECHERCHE_MANDATAIRE.cs', 42): 'valider-saisie',
    ('OPE_EDI_RPJC_INS_RECHERCHE_MANDATAIRE.cs', 43): 'valider-saisie',
    ('FORM_GCOM.cs', 94): 'rechercher',            # arme la recherche sur Entree (faite l. 103)
    ('BodaccToSendRigDataGridView.cs', 28): 'liste-entree',
    ('RigMediaPlayer.cs', 258): 'fermer',           # quitte le plein ecran de la video
    ('frmREQUI.cs', 435): 'ouvrir-ligne',           # F10 : la ligne choisie, comme un double-clic
    ('frmREQUI.cs', 444): 'ouvrir-ligne',           # F11 : idem, avec un Kbis
    ('TreeViewTabLot2.cs', 1250): 'annuler-modifications',
}


def branche(texte):
    """Le corps de la branche qui commence a texte (la touche) : jusqu'a la borne suivante, sans compter une touche
    chainee a la meme condition (« || e.KeyCode == Keys.F5 ») ; un « case » vide continue sur le suivant."""
    debut = len(texte.split('.', 1)[0]) + 1
    pos = debut
    while True:
        m = BORNE.search(texte, pos)
        if m is None:
            return texte[debut:]
        avant = texte[max(0, m.start() - 14):m.start()]
        corps = texte[debut:m.start()]
        if '||' in avant or '&&' in avant:
            pos = m.end()                       # meme condition : la branche continue
            continue
        if m.group(0).startswith('case') and not re.search(r'\w+\s*\(', corps):
            pos = m.end()                       # « case Keys.F7: case Keys.Enter: » : meme corps
            continue
        if m.group(0).startswith('break'):
            return texte[debut:m.end()]
        return corps


def lire():
    trouve = {}
    for racine, _, fichiers in os.walk(BASE):
        for f in sorted(fichiers):
            if not f.endswith('.cs') or 'Designer' in f:
                continue
            chemin = os.path.join(racine, f).replace(os.sep, '/')
            proc = next((x for x in chemin.split('/') if x.upper().startswith('PROC_')), None)
            if not proc:
                continue
            lignes = open(chemin, encoding='utf-8', errors='ignore').read().split('\n')
            for i, l in enumerate(lignes):
                if l.strip().startswith('//'):
                    continue
                for k in sorted(set(COMPARAISON.findall(l))):
                    # La touche, sa ligne et les suivantes (sans leurs commentaires), coupees a la fin de SA branche :
                    # ce qu'elle declenche. Une branche sans aucun appel ne fait rien (« case Keys.Escape: break; »).
                    debut = l[l.find('Keys.' + k):]
                    suite = [re.sub(r'//.*$', '', x).strip() for x in lignes[i + 1:i + 25]]
                    contexte = branche(' '.join([re.sub(r'//.*$', '', debut)] + suite))
                    touche = TOUCHE.get(k, k)
                    action = LUES_A_LA_MAIN.get((f, i + 1)) or (
                        classer(contexte, touche) if re.search(r'\w+\s*\(', contexte) else 'sans-effet')
                    cle = (proc.upper(), touche, action)
                    if cle not in trouve:
                        trouve[cle] = f'{chemin[len(BASE) + 1:]}:{i + 1}'
    return trouve


def cs(texte):
    return '"' + texte.replace('\\', '\\\\').replace('"', '\\"') + '"'


def main():
    sortie = sys.argv[1]
    trouve = lire()
    procs = sorted({p for p, _, _ in trouve})
    sans_nom = [p for p in procs if p not in NOMS]
    if sans_nom:
        sys.exit('Ecran V2 sans nom dans NOMS : ' + ', '.join(sans_nom))
    autres = [(c, s) for c, s in trouve.items() if c[2] == 'autre']
    if autres:
        sys.exit('Touche non classee (ajouter un motif dans ACTIONS) : ' + '; '.join(f'{c} {s}' for c, s in autres))
    lignes = []
    w = lignes.append
    w('// FICHIER GENERE par tests/SoclePreferencesCheck/inventorier_touches_v2.py (SOC-027 T01) : ne pas modifier a la main.')
    w('// Relancer le script quand le banc SoclePreferencesCheck dit que le code de la V2 a change.')
    w(f'// Source : {BASE.replace("/", chr(92))} (lecture seule) ; {len(trouve)} lignes, {len(procs)} ecrans de la V2.')
    w('namespace RigV3Desktop.Pont;')
    w('')
    w('/// <summary>Un raccourci de la V2 : l\'ecran (procedure), la touche, ce qu\'elle fait, et ou le code le dit.</summary>')
    w('public sealed record ToucheV2(string Procedure, string EcranV2, string Touche, string Action, string Source);')
    w('')
    w('public static class InventaireTouchesV2')
    w('{')
    w('    public static readonly ToucheV2[] Lignes =')
    w('    [')
    for (proc, touche, action), source in sorted(trouve.items()):
        w(f'        new({cs(proc)}, {cs(NOMS[proc])}, {cs(touche)}, {cs(action)}, {cs(source)}),')
    w('    ];')
    w('}')
    with open(sortie, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lignes) + '\n')
    print(f'{len(trouve)} lignes, {len(procs)} ecrans -> {sortie}')


if __name__ == '__main__':
    main()
