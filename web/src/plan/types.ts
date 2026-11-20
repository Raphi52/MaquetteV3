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
export interface VerbeActionsVue { verbe: string, actions: ActionDossierVue[] }
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
