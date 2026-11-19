// Codes neutres (pas de PROC_) : hors aiguillage, aucun fichier de web/src ne porte de code interne (tests/NomMetierCheck).
// Des vues du plan de travail pour les tests (records de Pont/PlanTravailVue.cs).
// Valeurs reprises du parcours joue sur l'appli (affaire 004176, etapes du
// dossier RCS) : les tests lisent le balisage rendu, pas les regles.

import type { LigneArbreVue } from '../composants/ClasseurArbre'
import type {
  ApercuVue, DossierVue, EtapesVue, PageEtapeVue, PaletteVue, PieceHistoriqueVue, ProcVue, TacheVue, VueApplication,
} from '../plan/types'

/** Une palette Ctrl K vide (Pont/PlanTravailPalette.cs), avant de taper. */
export function paletteVue(partiel: Partial<PaletteVue> = {}): PaletteVue {
  return {
    texte: '', filtre: 'tout', filtres: [{ code: 'tout', libelle: 'Tout', nombre: 0 }],
    moi: { signature: 'R. VILAIN', initiales: 'RV', nom: 'Raphael' }, agents: [],
    travail: [], travailTrouve: 0, pieces: [], dossierDesPieces: '2026B01067 AMIENS BILLARDS', numeroDesPieces: '2026B01067',
    horsGreffe: [], bilan: { jour: 'mercredi 9 septembre', horsDelai: 0, arriveesDuJour: 0, mAttendent: 0, plusAncienne: 0 },
    sirenInconnu: null, ecrans: [], elements: [], ancree: false,
    ...partiel,
  }
}

export function ligneArbre(id: string, texte: string, enfants: LigneArbreVue[] = [], niveau = 0): LigneArbreVue {
  return {
    id, niveau, texte, icone: enfants.length ? 'dossier' : 'pdf', actif: false, chemin: false,
    depliable: enfants.length > 0, ouvert: enfants.length > 0, renomme: false, compte: null, enfants, vide: false,
    groupe: 0, niveauGroupe: 0, date: '', pages: 0, derniere: '',
  }
}

export function proc(partiel: Partial<ProcVue> = {}): ProcVue {
  return {
    code: 'FACTURES', libelle: 'Factures',
    ecran: { valides: [], sirenDemande: null, cleOuverture: 'ouverture-vierge', lot: null, motif: 'Motif lu dans le menu.', recherche: null },
    ...partiel,
  }
}

export function page(code: string, titre: string, partiel: Partial<PageEtapeVue> = {}): PageEtapeVue {
  return { code, courante: false, ouverte: false, titre, source: 'table ' + code, fiches: null, recap: null, champs: [], ...partiel }
}

export function etapes(partiel: Partial<EtapesVue> = {}): EtapesVue {
  return {
    courante: 1, total: 7, titre: 'Statuts signes', oeil: false,
    pas: [
      { etat: 'complet', courant: false, pastille: 'OK', titre: 'Demande', fiches: 0, unite: 'fiche', refuses: 0, manquants: 0, remplis: 5, total: 5 },
      { etat: 'refuse', courant: true, pastille: '!', titre: 'Identite', fiches: 0, unite: 'fiche', refuses: 1, manquants: 0, remplis: 11, total: 14 },
      { etat: 'partiel', courant: false, pastille: '3', titre: 'Capital', fiches: 0, unite: 'fiche', refuses: 0, manquants: 0, remplis: 6, total: 7 },
      { etat: 'partiel', courant: false, pastille: '4', titre: 'Siege et etablissements', fiches: 2, unite: 'etablissement', refuses: 0, manquants: 2, remplis: 20, total: 22 },
    ],
    pages: [
      page('DEMANDE', 'Demande', { ouverte: true }),
      page('IDENTITE', 'Identite', {
        ouverte: true, courante: true,
        champs: [
          { libelle: 'SIREN', colonne: 'DSSRC_SIREN', type: 'text', exemple: '109 678 235', obligatoire: true, largeur: 'moitie',
            choix: [], valeur: '402 118 447', refus: 'cle de controle du SIREN incorrecte', manque: false },
          { libelle: 'Type de dossier', colonne: 'DSSRC_TYPE_RCS', type: 'text', exemple: '', obligatoire: true, largeur: 'moitie',
            choix: ['Personne morale', 'Personne physique'], valeur: 'Personne morale', refus: null, manque: false },
          { libelle: 'Patronyme', colonne: 'INSPP_PATRONYME', type: 'text', exemple: 'DUPONT', obligatoire: false, largeur: 'pleine',
            choix: [], valeur: '', refus: null, manque: true },
        ],
      }),
      page('CAPITAL', 'Capital'),
      page('SIEGE', 'Siege et etablissements', { fiches: { unite: 'Etablissement', nombre: 2, actif: 1 } }),
    ],
    recap: { valide: false, nombre: 1, titre: '1 point bloque la validation', aManquer: 0, refusees: 1, groupes: [] },
    ...partiel,
  }
}

export function tache(partiel: Partial<TacheVue> = {}): TacheVue {
  return {
    code: 'DENO', titre: 'Renommer la societe et son nom de domaine', toutLeDossier: false,
    texteTout: 'Afficher tout le dossier (14 branches, 42 pieces masquees)',
    faits: 1, exiges: 1, facultatifs: 1, reste: null, complete: true, peutClore: false,
    raison: 'Reste a traiter : Transmission INPI (registre national)', lignes: null,
    cases: [{ libelle: "Denomination d'usage", exige: true, rempli: true, valeur: 'NORMARINE ATLANTIQUE', titre: 'INSCRIT_PM.DENOMINATION_USAGE' }],
    casesFaites: 1, casesExigees: 1,
    effets: [
      { code: 'B0111', libelle: 'Publication BODACC', obligatoire: true, atteste: true, coche: true, cible: false,
        titre: 'Declare en base', sansPiece: false, titreLibelle: "Montrer dans l'arbre : Avis BODACC", piece: 'Avis BODACC' },
      { code: '0F', libelle: 'Transmission INPI (registre national)', obligatoire: true, atteste: false, coche: false, cible: false,
        titre: 'Declare en base', sansPiece: false, titreLibelle: "Montrer dans l'arbre : Accuse INPI", piece: 'Accuse INPI' },
    ],
    effetsTraites: 1, effetsObligatoires: 4,
    ...partiel,
  }
}

export function apercu(partiel: Partial<ApercuVue> = {}): ApercuVue {
  return {
    extension: 'PDF', pages: 2, date: '12/03/2024', libelle: 'Statuts signes', message: '', pdf: false, fichier: '',
    kbis: null,
    feuilles: [
      { numero: 1, total: 2, texte: 'STATUTS DE LA SOCIETE', valeurs: [{ libelle: 'Montant du capital', valeur: '10000' }] },
      { numero: 2, total: 2, texte: 'Article 2', valeurs: null },
    ],
    ...partiel,
  }
}

export function dossier(partiel: Partial<DossierVue> = {}): DossierVue {
  return {
    numero: '004176', objet: 'Modification - SCI DU PARC', siren: '812 445 901', type: 'RCS', statut: 'alerte',
    boutonDocument: false,
    sommaire: { champs: [
      { libelle: 'SIREN', valeur: '812 445 901', mono: true },
      { libelle: 'Forme juridique', valeur: 'Societe civile immobiliere', mono: false },
    ], mention: '' },
    bandeau: { nombre: 1, erreur: true, liens: [
      { indexEtape: 1, fiche: 0, refuse: true, titre: 'SIREN : cle de controle du SIREN incorrecte', etape: 'Identite', suffixe: '', nombre: 1 },
    ] },
    styleCorps: 'grid-template-columns: 400px 6px 420px 6px minmax(0, 1fr)',
    tache: null, filtre: '',
    arbre: ligneArbre('n1', '004176 Modification - SCI DU PARC', [ligneArbre('n2', 'Statuts signes', [], 1)]),
    poignee: true, fiche: null, etapes: etapes(), poignee2: true,
    apercu: { detachees: 0, apercu: apercu() }, voile: false,
    historique: historique(),
    arbreReplie: false, chemin: ['004176', 'Dossier RCS', 'Statuts signes'], pieces: 42,
    fil: [], suiviReplie: false, triClasseur: 'date', sortie: false,
    ...partiel,
  }
}

/** Neuf pieces datees de l'affaire 004176, la plus recente en tete (Pont : VueHistorique). */
export function historique(): PieceHistoriqueVue[] {
  // groupe : la teinte du lisere de la rubrique dans l'arbre (ordre de l'arbre de demonstration :
  // Pieces jointes 1, Depots d'actes 2, comptes 3, Formalites 4, Instances 6, Procedures collectives 7).
  const piece = (id: string, date: string, libelle: string, rubrique: string, judiciaire = false, actif = false, groupe = 0): PieceHistoriqueVue =>
    ({ id, date, libelle, rubrique, origine: 'Edition greffe', pages: 1, judiciaire, actif, groupe })
  return [
    piece('n10', '19/11/2026', 'Kbis a jour', 'Dossier RCS', false, true),
    piece('n11', '15/09/2026', 'Certificat en matiere de difficultes des entreprises', 'Pieces jointes', false, false, 1),
    piece('n12', '12/12/2024', 'Depot de l etat des creances', 'Depots sur la procedure', true, false, 7),
    piece('n13', '15/11/2024', 'Depot du rapport du mandataire', 'Depots sur la procedure', true, false, 7),
    piece('n14', '12/11/2024', 'Lettre de regularisation du depot', 'Depots des comptes annuels', false, false, 3),
    piece('n15', '15/10/2024', 'Certificat de depot d\'actes', "Depots d'actes", false, false, 2),
    piece('n16', '08/10/2024', 'Synthese de depot au Guichet unique', 'Formalites', false, false, 4),
    piece('n17', '18/06/2024', 'Jugement d ouverture du redressement judiciaire', 'Procedure n 2024P00042 - Redressement judiciaire', true, false, 7),
    piece('n18', '03/03/2023', 'Rapport d expertise', 'Mesures d instruction', true, false, 6),
  ]
}

export function vueApplication(partiel: Partial<VueApplication> = {}): VueApplication {
  return {
    theme: 'clair', geste: 0, barre: false, accueil: null, onglets: null, finsDeGlisse: 0,
    proc: null, dossier: null, impression: null, envoiMail: null, nouveau: null, menuNoeud: null,
    pagination: 0, defilement: null, palette: null,
    ...partiel,
  }
}
