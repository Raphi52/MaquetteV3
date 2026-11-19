// La VUE du plan de travail, telle que Pont/PlanTravailVue.cs la calcule
// (records C#, proprietes en camelCase). Un type par record, memes noms de
// champs : un champ renomme d'un cote casse le typecheck de l'autre a la relecture.

import type { VueBarre, VuePlanAccueil } from '../accueil/types'
import type { LigneArbreVue } from '../composants/ArbreGedNoeud'
import type { VueKbis } from '../composants/ApercuKbis'

/** Remonte un geste a SessionPlanTravail.Agir (le rappel « plan » de Home.razor). */
export type GestePlan = (nom: string, valeur?: unknown) => void

export interface VueApplication {
  theme: string
  /** Le numero du dernier geste du plan de travail joue par le C# (voir Application.tsx). */
  geste: number
  barre: VueBarre | null
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
}

export interface OngletBarreVue { code: string, libelle: string, actif: boolean, detache: boolean, glisse: boolean }
export interface DefilementVue { numero: number, index: number, doux: boolean }

// ---- L'ecran de procedure ----
export interface ProcVue {
  code: string
  libelle: string
  favori: boolean
  boutonFermer: boolean
  sujet: SujetVue | null
  ecran: EcranProcVue
}
export interface SujetVue { titre: string, onglets: OngletSujetVue[] }
export interface OngletSujetVue { code: string, libelle: string, actif: boolean, detail: string | null }
export interface SessionEcranVue { session: string, version: number }
export interface EcranProcVue {
  valides: string[]
  sirenDemande: string | null
  cleOuverture: string
  lot: SessionEcranVue | null
  motif: string | null
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
}
export interface SommaireVue { designation: string, champs: ChampSommaireVue[], mention: string }
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
  total: number
  titre: string
  oeil: boolean
  pas: PasEtapeVue[]
  pages: PageEtapeVue[]
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
  rendue: boolean
  titre: string
  source: string
  fiches: OngletsFichesVue | null
  recap: RecapVue | null
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
export interface MenuNoeudVue { titre: string, x: number, y: number, estPiece: boolean }
