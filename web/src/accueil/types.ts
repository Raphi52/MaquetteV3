// La VUE de l'accueil, telle que Pont/Accueil.cs la calcule (records C#,
// proprietes en camelCase). Un type par record, memes noms de champs : un
// champ renomme d'un cote casse le typecheck de l'autre a la relecture.

/** Remonte un geste a SessionAccueil.Agir (un seul rappel pour les trois iles). */
export type GesteAccueil = (nom: string, valeur?: unknown) => void

/** Ce que React passe au rappel « geste » de la coquille (GesteIle cote C#). */
export interface GesteIle {
  nom: string
  valeur: unknown
}

export interface VueAccueil {
  theme: string
  barre: VueBarre
  accueil: VuePlanAccueil
}

export interface ThemeVue { code: string, libelle: string }

export interface VueBarre {
  version: string
  reglages: boolean
  theme: string
  themes: ThemeVue[]
  profil: ProfilVue | null
}

export interface CompteVue { login: string, initiales: string, profil: string, domaines: string, actif: boolean }

export interface ProfilVue {
  dansLaBarre: boolean
  ouvert: boolean
  initiales: string
  titre: string
  login: string
  comptes: CompteVue[]
}

export interface FavoriVue { code: string, libelle: string, detail: string, actif: boolean }
export interface EntreeVue { code: string, libelle: string, detail: string, actif: boolean, favori: boolean }
export interface SousVue { titre: string, ouvert: boolean, compte: number, entrees: EntreeVue[] }
export interface MomentVue { titre: string, teinte: string, signe: string, compte: number, ouvert: boolean, sous: SousVue[] }
export interface MenuVue { favorisOuverts: boolean, favoris: FavoriVue[], moments: MomentVue[] }

export interface PhraseVue { horsDelaiConnu: boolean, horsDelai: number, arrivees: number, traites: string | null }
export interface DispositionVue { id: string, titre: string, aide: string, actif: boolean }
export interface BasculeVue { id: string, libelle: string, titre: string | null, actif: boolean }
export interface PanneauTuilesVue { dispositions: DispositionVue[], tuiles: BasculeVue[] }
export interface EnteteVue {
  date: string
  utilisateur: string
  phrase: PhraseVue
  panneauOuvert: boolean
  panneau: PanneauTuilesVue | null
  profil: ProfilVue | null
}

export interface TraitementTrouveVue { libelle: string }
export interface AffaireTrouveeVue { numero: string, objet: string, siren: string, type: string, statut: string }
export interface ElementTrouveVue { statut: string, nom: string, numero: string, chemin: string }
export interface RechercheVue {
  texte: string
  traitements: TraitementTrouveVue[]
  affaires: AffaireTrouveeVue[]
  elements: ElementTrouveVue[]
}

export interface ChiffresVue {
  deplie: number | null
  arrivees: number
  detail1: string
  titreDetail1: string | null
  anneau1: string
  cible2: number
  valeur2: string
  objectif2: string | null
  alerte3: boolean
  cible3: number
  valeur3: string
  objectif3: string | null
}

export interface AlerteLigneVue { id: string, nombre: number, palier: string, titre: string, detail: string, libelleCible: string }
export interface MesureVue {
  libelle: string
  valeur: string
  part: number
  statut: string
  objectif: number
  objectifTexte: string
  atteinte: number
}
export interface HorsDelaiVue { dossier: string, nom: string, jours: number, numero: string, creation: string }
export interface MorceauVue { texte: string, gras: boolean }
export interface ResumeSpanVue { morceaux: MorceauVue[], blancAvant: boolean }
export interface ResumeTuileVue { id: string, titre: string, ligne: ResumeSpanVue[] }
export interface ProueVue {
  rang: number
  compte: string
  lien: boolean
  arrivees: AlerteLigneVue[]
  mesures: MesureVue[]
  horsDelai: HorsDelaiVue[]
  resumes: ResumeTuileVue[]
}

export interface GroupeAlertesVue { categorie: string, repliee: boolean, charge: number, lignes: AlerteLigneVue[] }
export interface AlertesTuileVue { vide: boolean, afficherDetail: boolean, groupes: GroupeAlertesVue[] }
export interface NouveauteVue {
  module: string
  pictogramme: string
  teinte: string
  action: string
  suite: string
  detail: string
  ouverte: boolean
}
export interface ActualiteVue { filet: string, type: string, titre: string, message: string, auteur: string, horodatage: string }
export interface ActualitesTuileVue { actifs: ActualiteVue[], etat: string }
export interface AgentVue { login: string, nom: string }
export interface CompteActiviteVue { agent: string, nombre: number }
export interface GesteVue { agent: string, verbe: string, objet: string, depuis: string }
export interface ActiviteTuileVue {
  filtre: string
  agents: AgentVue[]
  total: number
  comptes: CompteActiviteVue[]
  gestes: GesteVue[]
}
export interface DemandeVue {
  chrono: string
  societe: string
  siren: string
  type: string
  recuLe: string
  anciennete: number
  etat: string
}
export interface PanneauDemandesVue { ouverte: AlerteLigneVue | null, demandes: DemandeVue[], titreFermer: string }
export interface MetierVue {
  deploye: boolean
  registre: string
  titre: string
  charge: number
  palierCharge: string
  menuOuvert: boolean
  gestes: string[]
  titreOuvrir: string | null
  filtre: string
  filtreActif: boolean
  urgents: number
  demat: number
  lignes: AlerteLigneVue[]
  active: string | null
  panneau: PanneauDemandesVue | null
}
export interface AlertesBisVue { vide: boolean, groupes: GroupeAlertesVue[], active: string | null, panneau: PanneauDemandesVue }
export interface ReglagesVue {
  genre: 'alertes' | 'performance' | 'aucun'
  categories: BasculeVue[]
  alertes: BasculeVue[]
  niveauMini: number
  tri: string
  limite: number
  afficherDetail: boolean
  mesures: BasculeVue[]
}

export interface TuileVue {
  id: string
  titre: string
  maitresse: boolean
  devant: boolean
  colonne: number
  ligne: number
  largeur: number
  hauteur: number
  rang: number
  avecTitre: boolean
  reglagesOuverts: boolean
  reglages: ReglagesVue | null
  performance: MesureVue[] | null
  alertes: AlertesTuileVue | null
  alertesBis: AlertesBisVue | null
  taches: AlerteLigneVue[] | null
  nouveautes: NouveauteVue[] | null
  actualites: ActualitesTuileVue | null
  activite: ActiviteTuileVue | null
  metier: MetierVue | null
}

export interface SignalVue { id: string, numero: number }

export interface VuePlanAccueil {
  menuReduit: boolean
  menu: MenuVue
  entete: EnteteVue
  recherche: RechercheVue
  chiffres: ChiffresVue
  proue: ProueVue | null
  tuiles: TuileVue[]
  signal: SignalVue | null
}

/** Arrete la propagation d'un appui : le `@onmousedown:stopPropagation` de Razor. */
export function arreter(e: { stopPropagation: () => void }): void {
  e.stopPropagation()
}
