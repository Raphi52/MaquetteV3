// La VUE du plan de travail, telle que Pont/PlanTravailVue.cs la calcule
// (records C#, proprietes en camelCase). Un type par record, memes noms de
// champs : un champ renomme d'un cote casse le typecheck de l'autre a la relecture.

import type { VuePlanAccueil } from '../accueil/types'
import type { LigneArbreVue, TriClasseur } from '../composants/ClasseurArbre'
import type { VueKbis } from '../composants/ApercuKbis'

/** Remonte un geste a SessionPlanTravail.Agir (le rappel « plan » de Home.razor). */
export type GestePlan = (nom: string, valeur?: unknown) => void

export interface VueApplication {
  theme: string
  /** Le numero du dernier geste du plan de travail joue par le C# (voir Application.tsx). */
  geste: number
  /** Faux dans la fenetre d'un onglet sorti : ni barre, ni accueil. */
  barre: boolean
  accueil: VuePlanAccueil | null
  onglets: OngletBarreVue[] | null
  finsDeGlisse: number
  proc: ProcVue | null
  dossier: DossierVue | null
  impression: ImpressionVue | null
  envoiMail: EnvoiMailVue | null
  nouveau: NouveauVue | null
  menuNoeud: MenuNoeudVue | null
  pagination: number
  defilement: DefilementVue | null
  /** La palette Ctrl K, quand elle est ouverte (Pont/PlanTravailPalette.cs). */
  palette: PaletteVue | null
}

// ---- La palette Ctrl K (Metier/RecherchePalette.cs, Pont/PlanTravailPalette.cs) ----
export interface PaletteVue {
  texte: string
  filtre: string
  filtres: FiltrePaletteVue[]
  moi: AgentPaletteVue
  agents: AgentPaletteVue[]
  travail: TravailPaletteVue[]
  /** Combien de lignes repondent au filtre, montrees ou non. */
  travailTrouve: number
  pieces: PiecePaletteVue[]
  dossierDesPieces: string
  numeroDesPieces: string
  horsGreffe: HorsGreffePaletteVue[]
  bilan: BilanPaletteVue
  sirenInconnu: string | null
  /** Les ecrans du menu dont le nom porte ce qui est tape. */
  ecrans: EcranPaletteVue[]
  /** Les elements de l'arbre dont le libelle porte ce qui est tape (n° de depot, de formalite). */
  elements: ElementPaletteVue[]
  /** Ouverte depuis la barre de l'accueil : elle pend sous la barre, en liste deroulante. */
  ancree: boolean
  /** Les depots de l'affaire cherchee, ou la liseuse peut joindre une piece (page seule). */
  depots: DepotPaletteVue[]
  /** Ce que le dernier « Joindre a ce depot » a fait (page seule). */
  avis: string | null
  /** « Sur ce dossier » : les actions de la barre de l'affaire ouverte pour ce qui est tape,
   *  en tete (RecherchePalette.Actions) ; vide hors affaire. Entree joue « actionDossier ». */
  actions: ActionPaletteVue[]
}
/** Une action de la barre du dossier proposee par la palette (Pont/PlanTravailPalette.cs). */
export interface ActionPaletteVue {
  code: string
  /** Son groupe dans la barre (« Facturer »). */
  verbe: string
  libelle: string
  /** Sa lettre : hors de la palette, elle la joue aussi. */
  touche: string
  detail: string
  /** Elle ouvre un ecran dans un onglet « pour ce dossier ». */
  ecran: boolean
  /** Le texte qu'elle copie, joue par la page ; nul sinon. */
  copie: string | null
}
export interface DepotPaletteVue { libelle: string, chemin: string[], lieu: string, pieces: string[], choisi: boolean }
export interface EcranPaletteVue { code: string, libelle: string }
export interface ElementPaletteVue { libelle: string, chemin: string[], lieu: string, piece: boolean }
export interface FiltrePaletteVue { code: string, libelle: string, nombre: number }
export interface AgentPaletteVue { signature: string, initiales: string, nom: string }
export interface TravailPaletteVue {
  /** dem | aff | cou | doc */
  nature: string
  numero: string
  /** Le dossier qu'ouvre la ligne ; vide = l'ecran nomme par ecran. */
  cle: string
  ecran: string
  /** Le nom de cet ecran dans le menu ; vide = pas d'ecran a proposer. */
  ecranLibelle: string
  titre: string
  detail: string
  etat: string
  /** al | ok | vide */
  ton: string
  filet: string
  qui: AgentPaletteVue | null
  aMoi: boolean
  enCours: boolean
  attribuable: boolean
  delai: string | null
  siren: string
  activite: ActivitePaletteVue[]
}
export interface ActivitePaletteVue { agent: string, verbe: string, objet: string, depuis: string }
export interface PiecePaletteVue {
  libelle: string, chemin: string[], lieu: string, avant: string, trouve: string, apres: string
  passages: number, pages: number, date: string, origine: string, annonce: boolean
  /** Le texte entier de la piece (pages separees par \f ; vide dans la palette), le debut et la
   *  longueur de chaque passage trouve, et sa page (a partir de 1). */
  texte: string, positions: number[], longueurs: number[], pagesDesPassages: number[], nbPages: number
  /** Le PDF de la piece, relatif a la page (vide pour une piece de demonstration). */
  fichier: string
}
export interface HorsGreffePaletteVue { code: string, source: string, objet: string, sujet: string }
export interface BilanPaletteVue { jour: string, horsDelai: number, arriveesDuJour: number, mAttendent: number, plusAncienne: number }

export interface OngletBarreVue {
  code: string, libelle: string, actif: boolean, detache: boolean, glisse: boolean
  /** La pastille du verbe du menu qui range la vue ; nul pour une vue hors du menu. */
  verbe: VerbeOngletVue | null
}
/** Un verbe du menu (MomentMenu) : la teinte nomme sa couleur (.rig-vonglet__verbe--<teinte>). */
export interface VerbeOngletVue { titre: string, teinte: string, signe: string }
export interface DefilementVue { numero: number, index: number, doux: boolean }

// ---- L'ecran de procedure ----
export interface ProcVue {
  code: string
  libelle: string
  ecran: EcranProcVue
}
export interface SessionEcranVue { session: string, version: number }
export interface EcranProcVue {
  valides: string[]
  sirenDemande: string | null
  cleOuverture: string
  lot: SessionEcranVue | null
  motif: string | null
  /** La page « Recherche » (la palette etalee en page, ecran EcranRecherche) ; null ailleurs. */
  recherche: PaletteVue | null
  /** L'affaire depuis laquelle l'ecran a ete ouvert (barre d'actions du dossier) ; null sinon. */
  dossier: ContexteDossierVue | null
  /** Le descripteur d'un ecran generique (Pont/EcransGeneres/), monte par EcranGenere ; null : ecran « a venir ». */
  descripteur: DescripteurEcranVue | null
  /** Un ecran REUNI EN ONGLETS (Pont/MenuAccueil.cs, EntreeMenu.EnOnglets), monte par EcranOnglets ; null sinon. */
  reunis: OngletsReunisVue | null
}
/** Les onglets d'un ecran reuni, un par procedure, dans l'ordre du menu. */
export interface OngletsReunisVue {
  onglets: OngletReuniVue[]
  /** Le code de la procedure montree : demandee par son code, ou choisie dans l'ecran. */
  actif: string
}
/** Un onglet : sa procedure, son nom au menu, son descripteur quand son ecran est generique. */
export interface OngletReuniVue {
  code: string
  libelle: string
  descripteur: DescripteurEcranVue | null
}
/** Le contexte d'une affaire transmis a l'ecran ouvert pour elle (Pont/PlanTravailActions.cs). */
export interface ContexteDossierVue {
  numero: string
  siren: string
  objet: string
  type: string
  /** La denomination, sinon l'objet de l'affaire. */
  nom: string
}

// ---- L'ecran generique (Pont/EcransGeneres/DescripteurEcran) : le contenu d'une procedure ----
export interface DescripteurEcranVue {
  code: string
  titre: string
  /** La forme d'interface (Pont/EcransGeneres/Descripteur.cs, FormeEcran) : le composant de web/src/ecrans/formes/. */
  forme: FormeEcranVue
  sujet: string
  recherche: string
  criteres: string[]
  valider: string
  /** La teinte du verbe du menu : la classe rig-tcw__rang-N. */
  rang: number
  etats: EtatEcranVue[]
  groupes: string[]
  /** Les ETAPES, dans l'ordre : la forme Saisie seule. */
  marches: MarcheEcranVue[]
  /** Les SECTIONS : les parties de l'objet, sans ordre a franchir (toutes les autres formes). */
  sections: MarcheEcranVue[]
  /** La grille d'un Referentiel, le tableau de resultats d'un Etat. */
  colonnes: ChampEcranVue[]
  /** La periode d'un Etat, la date et la chambre d'une Audience. */
  parametres: ChampEcranVue[]
  /** Les gestes nommes par la fiche RIG v2 (File, Audience, Etat, Document, Consultation ; boutons de confirmation d'une
   * Operation ; menu de ligne d'un Referentiel, en plus de Modifier et Supprimer) ; vide : le bouton « valider » seul. */
  gestes: string[]
  lignes: LigneEcranVue[]
  /** Un ecran REUNI : les procedures qu'il reunit, une par groupe (titre = groupe). Chaque ligne
   * suit les parties et le bouton de SA procedure ; vide pour l'ecran d'une seule procedure. */
  membres: DescripteurEcranVue[]
  /** Ecran reuni sans liste commune : un onglet par procedure, chacune dans sa forme. */
  enOnglets: boolean
  /** Une Operation qui vise UN seul objet, que la fiche RIG v2 RECOIT d'un autre ecran : aucune liste, il se designe par
   * son numero dans le seul critere. */
  objetUnique?: boolean
  /** Un Referentiel dont la fiche RIG v2 n'ajoute pas de ligne (faux) : pas de bouton « Ajouter ». Absent = vrai. */
  ajout?: boolean
  /** Un Referentiel dont la fiche RIG v2 ne supprime pas de ligne (faux) : pas de « Supprimer » au menu de ligne. Absent = vrai. */
  suppression?: boolean
  /** Un Referentiel dont la fiche RIG v2 DUPLIQUE une ligne : « Dupliquer » au menu de ligne ouvre une ligne neuve pre-remplie,
   * sa premiere cellule (la cle) vide ; Enregistrer l'envoie en Ajouter, ids = [la ligne copiee]. Absent = faux. */
  duplication?: boolean
  /** Un Referentiel : par groupe, les titres des sections dont les champs se saisissent sous une ligne neuve de ce groupe
   * (Ajouter dans son onglet, Dupliquer une de ses lignes), envoyes en « Section › Libelle ». Absent : les colonnes seules. */
  sectionsAjout?: Record<string, string[]>
  /** Un Referentiel dont les lignes ont chacune le formulaire de leur groupe : par groupe, les sections montrees sous une ligne
   * ouverte en modification (les champs que la fiche dit modifiables, hors colonnes, s'y saisissent). Groupe absent : toutes. */
  sectionsParGroupe?: Record<string, string[]>
  /** Un Referentiel : les groupes ou RIG v2 ne fait que modifier -- ni Ajouter dans leur onglet, ni Supprimer ni Dupliquer. */
  groupesModificationSeule?: string[]
  /** Le RESULTAT d'une operation sur un objet unique : montre apres le geste, et des l'ouverture d'un objet deja traite. */
  resultat?: MarcheEcranVue[]
  /** Le titre de la section dessinee en EXTRAIT KBIS (composants/ApercuKbis.tsx) au lieu de ses champs ; PROC_XEX seul. */
  apercuKbis?: string | null
}
/** Les formes d'ecran, aux noms de FormeEcran (C#). */
export type FormeEcranVue = 'File' | 'Consultation' | 'Fiche' | 'Saisie' | 'Referentiel' | 'Etat' | 'Audience' | 'Document' | 'Operation'
/** Les types de champ, aux noms de TypeChamp (C#). */
export type TypeChampVue = 'Texte' | 'Zone' | 'Date' | 'Periode' | 'Nombre' | 'Montant' | 'Case' | 'Liste' | 'Tableau'
/** Un champ de la fiche RIG v2 : son libelle, son type, les valeurs d'une liste. */
/** visibleSi : le champ ne se montre que quand cette case de sa section est cochee (decochee, il se vide) ; bloqueSi : cette
 * case cochee, il se montre sans se saisir -- les regles d'affichage de la fiche RIG v2 (ChampEcran.VisibleSi / BloqueSi).
 * fix-ok: controle final du lot 14 (27/11/2026, 4e reprise) -- sans elles, « Résolution du plan », « Code clôture » et « À sauver »
 * d'un etat de PC s'affichaient et se saisissaient toujours ; ReferentielParGroupe.test.tsx rouge (3 tests) sans la regle. */
export interface ChampEcranVue { libelle: string; type: TypeChampVue; options: string[]; visibleSi?: string | null; bloqueSi?: string | null }
/** Une tuile : son code (AFaire, EnCours, Validee, EnAttente, Rejetee) et son icone de l'accueil. */
export interface EtatEcranVue { code: string; libelle: string; icone: string }
/** Une etape (Saisie) ou une section (les autres formes). */
export interface MarcheEcranVue { titre: string; consigne: string; obligatoire: boolean; champs: ChampEcranVue[] }
/** Une ligne ; valeurs : ses cellules dans l'ordre des colonnes (Referentiel, Etat), vide si le descripteur n'en donne pas. */
export interface LigneEcranVue { nom: string; precisions: string; groupe: string; etat: string; faites: number[]; valeurs?: string[]; id?: string | null }

// ---- La visionneuse d'affaire ----
export interface DossierVue {
  numero: string
  objet: string
  siren: string
  type: string
  statut: string
  boutonDocument: boolean
  sommaire: SommaireVue
  bandeau: BandeauDefautsVue | null
  styleCorps: string
  tache: TacheVue | null
  filtre: string
  arbre: LigneArbreVue
  poignee: boolean
  fiche: FicheColonneVue | null
  etapes: EtapesVue | null
  poignee2: boolean
  apercu: ApercuColonneVue | null
  voile: boolean
  /** Les pieces datees, la plus recente en tete (l'historique, onglet Suivi). */
  historique: PieceHistoriqueVue[]
  /** L'arbre est replie : sa colonne disparait, le chemin de l'element choisi le remplace. */
  arbreReplie: boolean
  /** Du numero de l'affaire a l'element choisi dans l'arbre. */
  chemin: string[]
  /** Le nombre de pieces que l'arbre montre a ce login. */
  pieces: number
  /** Le fil de l'affaire, du plus ancien au plus recent (onglet Fil). */
  fil: MessageFilVue[]
  /** La colonne de suivi est repliee a droite : un rail la remplace, sa poignee disparait. */
  suiviReplie: boolean
  /** Le classement de la carte ouverte du classeur, retenu d'une affaire a l'autre et au redemarrage. */
  triClasseur: TriClasseur
  /** Montree dans la fenetre de son onglet sorti : pas d'accueil derriere, pas de fleche « Retour ». */
  sortie: boolean
  /** La barre d'actions de l'entete, un groupe par verbe (Metier/ActionsDossier.cs). */
  actions: VerbeActionsVue[]
  /** Ce qui est en cours sur l'affaire, en tete de l'onglet Suivi (Pont/PlanTravailSuivi.cs). */
  enCours: EnCoursDossierVue
}
/** Le travail en cours sur l'affaire et les derniers gestes poses dessus. */
export interface EnCoursDossierVue {
  /** Le plus urgent d'abord. */
  travail: TravailDossierVue[]
  /** Les lignes dont le delai legal est depasse. */
  horsDelai: number
  /** Du plus recent au plus ancien. */
  activite: ActivitePaletteVue[]
}
export interface TravailDossierVue {
  /** dem | cmd | cou | doc */
  nature: string
  numero: string
  titre: string
  detail: string
  etat: string
  /** al | ok | vide */
  ton: string
  filet: string
  qui: AgentPaletteVue | null
  aMoi: boolean
  /** « M'attribuer » (geste « paletteAttribuer ») : meme regle que la palette. */
  attribuable: boolean
  delai: string | null
  /** L'ecran que la ligne ouvre pour ce dossier (geste « enCoursOuvrir ») ; vide = aucun. */
  ecranLibelle: string
}
/** Un verbe de la barre d'actions du dossier et ses actions disponibles. */
export interface VerbeActionsVue {
  verbe: string
  actions: ActionDossierVue[]
  /** Le code de l'action que le verbe garde en BOUTON quand la barre le replie (le Kbis, les
   *  factures, le courrier ; un verbe d'une seule action, elle) ; null : le verbe entier en menu. */
  principale: string | null
}
export interface ActionDossierVue {
  /** Ce que remonte le geste « actionDossier ». */
  code: string
  libelle: string
  /** Sa lettre au clavier, menu du verbe ouvert. */
  touche: string
  detail: string
  /** Elle ouvre un ecran dans un onglet (sinon elle agit dans la page). */
  ecran: boolean
  /** Le texte qu'elle copie dans le presse-papiers, sans remonter au C# ; null sinon. */
  copie: string | null
}
export interface PieceHistoriqueVue {
  id: string
  date: string
  libelle: string
  rubrique: string
  origine: string
  pages: number
  judiciaire: boolean
  actif: boolean
  /** La teinte du lisere de sa rubrique dans l'arbre (1 a 8), 0 hors rubrique. */
  groupe: number
}
/** Une note du fil, ou une valeur changee dans le formulaire (modification = vrai). */
export interface MessageFilVue {
  initiales: string
  auteur: string
  quand: string
  texte: string
  modification: boolean
  champ: string
  avant: string
  apres: string
}
export interface SommaireVue { champs: ChampSommaireVue[], mention: string }
export interface ChampSommaireVue { libelle: string, valeur: string, mono: boolean }
export interface BandeauDefautsVue { nombre: number, erreur: boolean, liens: LienDefautVue[] }
export interface LienDefautVue {
  indexEtape: number
  fiche: number
  refuse: boolean
  titre: string
  etape: string
  suffixe: string
  nombre: number
}

export interface TacheVue {
  code: string
  titre: string
  toutLeDossier: boolean
  texteTout: string
  faits: number
  exiges: number
  facultatifs: number
  reste: string | null
  complete: boolean
  peutClore: boolean
  raison: string
  lignes: LignesTacheVue | null
  cases: CaseTacheVue[]
  casesFaites: number
  casesExigees: number
  effets: EffetTacheVue[]
  effetsTraites: number
  effetsObligatoires: number
}
export interface LignesTacheVue { libelle: string, lignes: LigneTacheVue[], titreAjout: string, retrait: boolean }
export interface LigneTacheVue { rang: number, trous: number, courante: boolean }
export interface CaseTacheVue { libelle: string, exige: boolean, rempli: boolean, valeur: string, titre: string }
export interface EffetTacheVue {
  code: string
  libelle: string
  obligatoire: boolean
  atteste: boolean
  coche: boolean
  cible: boolean
  titre: string
  sansPiece: boolean
  titreLibelle: string
  piece: string
}

export interface FicheColonneVue { branche: string, session: string, version: number }

export interface EtapesVue {
  courante: number
  /** Le nombre d'etapes de la liste : le controle n'en fait plus partie. */
  total: number
  titre: string
  oeil: boolean
  pas: PasEtapeVue[]
  pages: PageEtapeVue[]
  /** Le controle du dossier, calcule a chaque rendu : il vit dans le pied du panneau. */
  recap: RecapVue
  /** Les champs de l'etape CONTROLE (etat du dossier, mention), dans le pied a cote de « Valider le dossier ». */
  controle: ChampSaisieVue[]
}
export interface PasEtapeVue {
  etat: string
  courant: boolean
  pastille: string
  titre: string
  fiches: number
  unite: string
  refuses: number
  manquants: number
  remplis: number
  total: number
}
export interface PageEtapeVue {
  code: string
  courante: boolean
  /** Depliee dans l'accordeon : seules les etapes ouvertes portent leurs champs. */
  ouverte: boolean
  titre: string
  source: string
  fiches: OngletsFichesVue | null
  champs: ChampSaisieVue[]
}
export interface OngletsFichesVue { unite: string, nombre: number, actif: number }
export interface ChampSaisieVue {
  libelle: string
  colonne: string
  type: string
  exemple: string
  obligatoire: boolean
  largeur: string
  choix: string[]
  valeur: string
  refus: string | null
  manque: boolean
}
export interface RecapVue {
  valide: boolean
  nombre: number
  titre: string
  aManquer: number
  refusees: number
  groupes: GroupeRecapVue[]
}
export interface GroupeRecapVue {
  indexEtape: number
  fiche: number
  etape: string
  multi: boolean
  unite: string
  nombre: number
  points: PointRecapVue[]
}
export interface PointRecapVue { champ: string, motif: string, colonne: string, manquant: boolean }

export interface ApercuColonneVue { detachees: number, apercu: ApercuVue | null }
export interface ApercuVue {
  extension: string
  pages: number
  date: string
  libelle: string
  message: string
  pdf: boolean
  fichier: string
  /** La page ou le PDF s'ouvre : 1, ou celle du passage lu quand la page Recherche l'ouvre. */
  page: number
  kbis: VueKbis | null
  feuilles: FeuilleApercuVue[]
  /** « Editer » ouvre le formulaire de la piece ; faux pour un courrier ecrit pour l'affaire, qui n'en a aucun. */
  editable: boolean
}
export interface FeuilleApercuVue { numero: number, total: number, texte: string, valeurs: ValeurApercuVue[] | null }
export interface ValeurApercuVue { libelle: string, valeur: string }

// ---- Les modales ----
export interface ImpressionVue {
  libelle: string
  imprimantes: string[]
  files: string[]
  papiers: string[]
  imprimante: string
  file: string
  papier: string
  exemplaires: number
  rectoVerso: boolean
}
export interface EnvoiMailVue {
  libelle: string
  destinataires: string[]
  messages: string[]
  message: number
  corps: string
  destinataire: string
  peutEnvoyer: boolean
}
export interface NouveauVue { cible: string, natures: NatureVue[] }
export interface NatureVue { code: string, titre: string, extension: string }
/** Le menu du clic droit (Pont/PlanTravailVue.cs, MenuNoeudVue). */
export interface MenuNoeudVue {
  titre: string
  x: number
  y: number
  estPiece: boolean
  /** Un dossier : les pieces qu'il range, sous-dossiers compris ; 0 pour une piece ou une fiche. */
  pieces: number
  /** Ce que « Copier » met dans le presse-papiers : le numero d'un dossier numerote, sinon le libelle. */
  copie: string
  copieNumero: boolean
}
