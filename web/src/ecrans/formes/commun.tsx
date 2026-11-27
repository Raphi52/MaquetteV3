// LE SOCLE DES FORMES D'ECRAN GENERE (plan « RIG v3 100 % fonctionnel », lot 1, 25/11/2026).
//
// Chaque procedure decrite dans Pont/EcransGeneres/ porte sa FORME (FormeEcran) ; web/src/ecrans/EcranGenere.tsx
// l'aiguille vers le composant de cette forme (Forme<Nom>.tsx, a cote de ce fichier). Ce qu'elles partagent est ici :
// la lecture des lignes au pont, la provenance (base du greffe ou exemple), le champ type, et le refus honnete des
// gestes. Les listes reprennent le socle des listes modernes (composants/ListeModerne.tsx, wwwroot/rig-listes.css).
//
// LES GESTES (regle du plan) : un geste passe par useGeste, qui l'envoie au pont (EcranGenere.Geste) quand la ligne vient
// de la base (elle porte son id) ; l'ecrivain de la procedure l'ecrit dans la base du greffe (Serveur/Socle/EcransGeneres/
// EcrivainEcran.cs), puis l'ecran RELIT ses lignes et dit ce qui est fait. Un controle de RIG v2 qui refuse : son message,
// rien n'a change. Sans ecrivain, sans serveur, ou sur une ligne d'exemple : « Demonstration : rien n'est enregistre ».
// Jamais un faux succes : une ligne ne change pas d'etat, une saisie reste a l'ecran, marquee non enregistree.
// LA FICHE d'une ligne de la base se lit au pont (useFiche, EcranGenere.Fiche) : ses champs portent leur valeur, et ceux
// que l'ecrivain sait enregistrer seuls se saisissent (Sections, prop fiche). LA RECHERCHE va dans la base quand le
// lecteur sait chercher (useLignes().chercher, EcranGenere.Chercher) ; sinon l'ecran filtre ce qu'il a.

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { appeler } from '../../pont'
import {
  CelluleChevron, CelluleIdentite, MenuLigne, Pastille, PointEtat, normaliser, type ActionLigne, type Teinte,
} from '../../composants/ListeModerne'
import type { ChampEcranVue, DescripteurEcranVue, EtatEcranVue, LigneEcranVue, MarcheEcranVue } from '../../plan/types'

/** Les lignes que le pont rend (Pont/EcranGenere.cs, VueLignesEcran). */
export interface VueLignesEcran {
  /** base : tout vient de la base ; mixte : certains groupes restent en exemple ; exemple ; erreur (exemples + motif). */
  mode: 'base' | 'mixte' | 'exemple' | 'erreur'
  message: string | null
  greffe: string | null
  lignes: LigneEcranVue[]
  /** Les groupes dont les lignes sont des exemples du descripteur. */
  exemples: string[]
  /** Les groupes branches sur la base mais sans aucune ligne : ils restent affiches, avec « Aucune ligne ». */
  vides?: string[]
  /** Les groupes dont les lignes ont ete CHERCHEES dans la base pour la recherche en cours : l'ecran ne les refiltre pas. */
  cherchees?: string[]
  /** Vrai : au moins une procedure de l'ecran cherche dans la base (la recherche y part, EcranGenere.Chercher). */
  recherche?: boolean
  /** Les groupes dont les lignes s'arretent au plafond du serveur (message dit combien) : FormeEtat n'imprime pas une liste coupee. */
  plafonnees?: string[]
}

/** Une recherche : le texte du champ et les criteres remplis. */
export interface RechercheVue { texte: string; criteres: Saisies }

/** Une recherche qui ne filtre rien. */
const rechercheVide = (r: RechercheVue | null) => r === null || (r.texte.trim() === '' && Object.values(r.criteres).every((v) => v.trim() === ''))

/** Deux recherches identiques (les lignes cherchees pour l'une valent pour l'autre). */
export const memeRecherche = (a: RechercheVue | null, b: RechercheVue | null) =>
  (rechercheVide(a) && rechercheVide(b)) || JSON.stringify(a) === JSON.stringify(b)

/** Un champ d'une fiche lue (Contrats/EcransGeneres/FicheEcran.cs) : sa valeur, s'il se saisit, sa sous-liste. */
export interface ChampFicheVue { libelle: string; valeur: string; modifiable: boolean; colonnes: string[]; lignes: string[][] }
export interface SectionFicheVue { titre: string; champs: ChampFicheVue[] }
/** Une fiche lue dans la base : ses sections aux titres du descripteur ; en lecture seule avec le motif de RIG v2. */
export interface FicheVue { procedure: string; id: string; nom: string; sections: SectionFicheVue[]; lectureSeule: boolean; motif: string | null }
/** Ce que le pont rend pour une fiche (Pont/EcranGenere.cs, VueFicheEcran). */
export interface VueFicheEcran { mode: 'base' | 'exemple' | 'erreur'; message: string | null; fiche: FicheVue | null }

/** Les types de geste (Contrats/EcransGeneres/GesteEcran.cs, TypeGeste). */
export type TypeGesteVue = 'Enregistrer' | 'Creer' | 'Ajouter' | 'Modifier' | 'Supprimer' | 'Agir' | 'Confirmer' | 'Decider'
/** Ce que le geste a donne (ResultatGeste). */
export interface ResultatGesteVue { fait: boolean; message: string; sansEcrivain: boolean; ids: string[] }

/** Les trois etats qui portent les gestes : le descripteur les nomme ainsi (EcransGeneresCheck le verifie). */
export const AFAIRE = 'AFaire'
export const ENCOURS = 'EnCours'
export const VALIDEE = 'Validee'

/** Le refus d'un geste tant qu'aucun ecrivain n'existe : la phrase exacte que tout bouton dit. */
export const DEMONSTRATION = 'Démonstration : rien n’est enregistré'

/** Une ligne de l'ecran, avec son rang dans la liste (cle stable des gestes). */
export interface LigneTenue extends LigneEcranVue { i: number }

/** Une ligne est figee hors « a faire » et « en cours » : aucun geste, une fiche en lecture seule. */
export const figee = (etat: string) => etat !== AFAIRE && etat !== ENCOURS

/**
 * Les lignes de l'ecran : celles de la base, ou les exemples. Un appel qui echoue (pont absent, erreur du C#) laisse les
 * exemples, marques comme tels : l'ecran n'est jamais vide sans le dire. Lues UNE fois par procedure ([d.code]) : chaque
 * redessin de la racine renvoie un descripteur neuf (meme contenu), qui ne doit ni relire la base ni refermer la ligne
 * ouverte. null tant que le pont n'a pas repondu : rien n'est affiche avant, jamais un nom fictif sous un ecran branche.
 */
export function useLignes(d: DescripteurEcranVue): {
  vue: VueLignesEcran | null; lignes: LigneTenue[]
  /** Relit les lignes (apres un geste ecrit), pour la recherche en cours. */
  relire: () => void
  /** Cherche dans la base (si un lecteur de l'ecran sait chercher) ; une recherche vide revient a la liste de travail. */
  chercher: (r: RechercheVue) => void
  /** La recherche dont viennent les lignes cherchees (vue.cherchees) ; null : aucune. */
  cherchee: RechercheVue | null
} {
  const [vue, poserVue] = useState<VueLignesEcran | null>(null)
  const [cherchee, poserCherchee] = useState<RechercheVue | null>(null)
  const vivant = useRef(true)
  const enCours = useRef<RechercheVue | null>(null)
  useEffect(() => {
    vivant.current = true
    const recevoir = (v: VueLignesEcran) => { if (vivant.current) poserVue(v) }
    appeler<VueLignesEcran>('EcranGenere.Lignes', d.code).then(recevoir, () => recevoir({
      mode: 'erreur', message: 'Les lignes de la base n’ont pas pu être lues.', greffe: null, lignes: d.lignes, exemples: d.groupes,
    }))
    return () => { vivant.current = false }
  }, [d.code])
  // Une relecture qui echoue garde les lignes deja a l'ecran (jamais remplacees par des exemples en cours de travail).
  const lire = useCallback((r: RechercheVue | null) => {
    const appel = rechercheVide(r)
      ? appeler<VueLignesEcran>('EcranGenere.Lignes', d.code)
      : appeler<VueLignesEcran>('EcranGenere.Chercher', d.code, r!.texte, r!.criteres)
    appel.then((v) => { if (vivant.current && enCours.current === r) { poserVue(v); poserCherchee(rechercheVide(r) ? null : r) } }, () => {})
  }, [d.code])
  const relire = useCallback(() => lire(enCours.current), [lire])
  const chercher = useCallback((r: RechercheVue) => {
    if (memeRecherche(enCours.current, r)) return
    enCours.current = r
    lire(r)
  }, [lire])
  return { vue, lignes: (vue?.lignes ?? []).map((l, i) => ({ ...l, i })), relire, chercher, cherchee }
}

/** Ce que useLignes rend : les lignes, relire, chercher. */
export type LignesTenues = ReturnType<typeof useLignes>

/** Le delai entre la derniere frappe et la recherche dans la base. */
export const DELAI_RECHERCHE = 350

/**
 * LA RECHERCHE d'une forme (Consultation, Fiche, Referentiel, Audience, Operation), la meme pour toutes : quand un lecteur
 * de l'ecran sait chercher (vue.recherche), le texte et les criteres partent au serveur un instant apres la frappe
 * (useLignes().chercher, EcranGenere.Chercher) ; les lignes cherchees pour CETTE recherche (vue.cherchees) ne sont pas
 * refiltrees a l'ecran -- la base a choisi. Les autres groupes, et tout l'ecran sans lecteur qui cherche, se filtrent ICI :
 * le texte, et chaque critere rempli qui doit se lire dans la ligne (sauf criteresALEcran = faux : les parametres d'une
 * audience, une date ou une chambre, choisissent le role dans la base mais ne se lisent pas dans le texte d'une ligne).
 * fix-ok: mesure par le controle final du lot 2 (26/11/2026) -- la recherche en base vivait dans Resultats.tsx seul ;
 * FormeReferentiel.tsx:114, FormeAudience.tsx:51 et FormeOperation filtraient seulement les lignes deja lues, sans
 * jamais appeler chercher : elle est donc ici, commune aux cinq formes.
 */
export function useRecherche(lues: LignesTenues, texte: string, criteres: Saisies, criteresALEcran = true): {
  /** Les lignes a montrer. */
  visibles: LigneTenue[]
  /** Vrai : la recherche part aussi dans la base. */
  enBase: boolean
  /** Une recherche ou un critere filtre la liste. */
  filtre: boolean
} {
  const { vue, lignes, chercher, cherchee } = lues
  const enBase = vue?.recherche === true
  useEffect(() => {
    if (!enBase) return
    const minuterie = setTimeout(() => chercher({ texte, criteres }), DELAI_RECHERCHE)
    return () => clearTimeout(minuterie)
  }, [enBase, texte, criteres, chercher])
  const cherche = normaliser(texte.trim())
  const dejaCherchees = memeRecherche(cherchee, { texte, criteres }) ? vue?.cherchees ?? [] : []
  const visibles = lignes.filter((l) => dejaCherchees.includes(l.groupe)
    || ((!criteresALEcran || passeCriteres(l, criteres)) && (cherche === '' || normaliser(texteLigne(l)).includes(cherche))))
  return { visibles, enBase, filtre: cherche !== '' || Object.values(criteres).some((v) => v.trim() !== '') }
}

/** Ce que dit un ecran dont la recherche part aussi dans la base. */
export function NoteRecherche({ enBase }: { enBase: boolean }) {
  return enBase ? <p className="rig-eg-note" role="note">La recherche et les critères cherchent aussi dans la base du greffe.</p> : null
}

/** La ligne vient-elle de la base (son groupe est branche, et elle porte l'identifiant de son objet) ? */
export const deLaBase = (vue: VueLignesEcran | null, l: { groupe: string; id?: string | null | undefined }) =>
  vue !== null && (vue.mode === 'base' || vue.mode === 'mixte') && !vue.exemples.includes(l.groupe) && !!l.id

/**
 * La fiche de la ligne ouverte, lue dans la base (EcranGenere.Fiche) ; null tant qu'elle n'est pas lue, ou pour une ligne
 * d'exemple (l'ecran garde alors ses champs vides). Relue par relire() apres un enregistrement.
 */
export function useFiche(d: DescripteurEcranVue, l: LigneTenue | undefined, vue: VueLignesEcran | null): {
  fiche: VueFicheEcran | null; relire: () => void
} {
  const [fiche, poserFiche] = useState<VueFicheEcran | null>(null)
  const [tour, poserTour] = useState(0)
  const cle = l && deLaBase(vue, l) ? `${l.groupe}\u0000${l.id}` : null
  useEffect(() => {
    poserFiche(null)
    if (cle === null || !l) return
    let vivant = true
    appeler<VueFicheEcran>('EcranGenere.Fiche', d.code, l.groupe, l.id).then(
      (v) => { if (vivant) poserFiche(v) },
      () => { if (vivant) poserFiche({ mode: 'erreur', message: 'La fiche n’a pas pu être lue dans la base du greffe.', fiche: null }) })
    return () => { vivant = false }
    // l : sa cle suffit (un redessin rend une ligne neuve de meme cle, qui ne relit pas la fiche).
  }, [d.code, cle, tour])
  return { fiche, relire: () => poserTour((t) => t + 1) }
}

/**
 * LA FICHE LUE d'une ligne, en lecture, par sections de SA procedure : le detail deplie d'une affaire au role (Audience),
 * d'un objet a choisir (Operation). Une ligne d'exemple montre ses sections sans valeurs, comme avant le lot 2.
 */
export function FicheLue({ d, l, vue, sections }: {
  d: DescripteurEcranVue; l: LigneTenue; vue: VueLignesEcran | null
  /** Les sections montrees ; par defaut, celles de la procedure de la ligne. */
  sections?: MarcheEcranVue[] | undefined
}) {
  const { fiche } = useFiche(d, l, vue)
  return (
    <>
      {fiche && fiche.mode !== 'base' && fiche.message && <p className="rig-eg-note" role="note">{fiche.message}</p>}
      {fiche?.fiche?.lectureSeule && fiche.fiche.motif && <p className="rig-eg-note" role="note">Verrouillé : {fiche.fiche.motif}.</p>}
      <Sections sections={sections ?? parcoursDe(d, l.groupe).sections} saisies={{}} changer={() => {}} lecture fiche={fiche?.fiche ?? null} />
    </>
  )
}

/** Un geste demande par une forme : son type, son nom (File, Audience), le groupe (la procedure) et les objets vises. */
export interface DemandeGeste {
  type: TypeGesteVue; nom?: string | undefined; groupe: string; ids: (string | null | undefined)[]; valeurs: Saisies
}

/**
 * Joue un geste : au pont si la ligne vient de la base, sinon la phrase de demonstration, AUSSITOT (sans attendre).
 * Ecrit : le message de l'ecrivain, puis apres() (relire les lignes, la fiche). Refuse : le message de RIG v2. Sans
 * ecrivain ou pont en panne : la demonstration. Rend le resultat, null s'il n'est pas alle au serveur.
 */
export function useGeste(d: DescripteurEcranVue, vue: VueLignesEcran | null, annoncer: (texte: CompteRendu) => void) {
  return async (g: DemandeGeste, demonstration: string, apres?: () => void): Promise<ResultatGesteVue | null> => {
    if (!versLaBase(vue, g)) { annoncer(demonstration); return null }
    const r = await envoyerGeste(d, g)
    annoncer(r === null || r.sansEcrivain ? demonstration : r.fait ? r.message : { refus: r.message })
    if (r?.fait) apres?.()
    return r
  }
}

/** Le geste peut-il aller a la base : des objets lus dans la base, ou une creation dans un groupe branche. */
function versLaBase(vue: VueLignesEcran | null, g: DemandeGeste): boolean {
  if (g.type === 'Creer' || g.type === 'Ajouter')
    return vue !== null && (vue.mode === 'base' || vue.mode === 'mixte') && !vue.exemples.includes(g.groupe)
  return g.ids.length > 0 && g.ids.every((id) => deLaBase(vue, { groupe: g.groupe, id }))
}

/** Le geste envoye au pont (EcranGenere.Geste) ; null si le pont n'a pas repondu (la demonstration vaut alors). */
async function envoyerGeste(d: DescripteurEcranVue, g: DemandeGeste): Promise<ResultatGesteVue | null> {
  try {
    return await appeler<ResultatGesteVue>('EcranGenere.Geste', d.code, g.groupe, g.type, g.nom ?? null, g.ids, g.valeurs)
  } catch {
    return null
  }
}

/**
 * Un geste EN LOT sur des lignes cochees (File, Audience, Operation) : un envoi par procedure (un ecran reuni en melange),
 * chacune a son ecrivain. Aucune ligne de la base : la demonstration, aussitot. Sinon chaque procedure dit ce qu'elle a
 * fait (ou sa demonstration) ; apres() si l'une a ecrit.
 */
export function useGesteEnLot(d: DescripteurEcranVue, vue: VueLignesEcran | null, annoncer: (texte: CompteRendu) => void) {
  const jouer = useGeste(d, vue, annoncer)
  return async (type: TypeGesteVue, nom: string, visees: LigneTenue[], valeurs: Saisies, demonstration: (n: number) => string,
                apres?: () => void): Promise<void> => {
    const groupes = [...new Set(visees.map((l) => l.groupe))]
    if (groupes.length <= 1) {
      await jouer({ type, nom, groupe: groupes[0] ?? d.titre, ids: visees.map((l) => l.id), valeurs }, demonstration(visees.length), apres)
      return
    }
    const demande = (g: string): DemandeGeste =>
      ({ type, nom, groupe: g, ids: visees.filter((l) => l.groupe === g).map((l) => l.id), valeurs })
    if (!groupes.some((g) => versLaBase(vue, demande(g)))) { annoncer(demonstration(visees.length)); return }
    const textes: string[] = []
    let ecrit = false
    for (const g of groupes) {
      const n = visees.filter((l) => l.groupe === g).length
      const r = versLaBase(vue, demande(g)) ? await envoyerGeste(d, demande(g)) : null
      textes.push(`${g} : ${r === null || r.sansEcrivain ? demonstration(n) : r.message}`)
      ecrit ||= r?.fait === true
    }
    annoncer(textes.join(' '))
    if (ecrit) apres?.()
  }
}

/** La procedure d'une ligne : celle de son groupe dans un ecran reuni, sinon l'ecran lui-meme. */
export function parcoursDe(d: DescripteurEcranVue, groupe: string): DescripteurEcranVue {
  return d.membres.find((m) => m.titre === groupe) ?? d
}

/** Les parties d'une procedure : ses etapes (Saisie), sinon ses sections. */
export const partiesDe = (d: DescripteurEcranVue): MarcheEcranVue[] => (d.marches.length > 0 ? d.marches : d.sections)

/** Les gestes d'une procedure : ceux que la fiche nomme, sinon son bouton final. */
export const gestesDe = (d: DescripteurEcranVue): string[] => (d.gestes.length > 0 ? d.gestes : [d.valider])

/** L'etat d'une ligne, au nom de SA procedure (« A relancer ») ; a defaut, celui de l'ecran. */
export function etatDe(d: DescripteurEcranVue, l: { groupe: string; etat: string }): EtatEcranVue | undefined {
  return parcoursDe(d, l.groupe).etats.find((e) => e.code === l.etat) ?? d.etats.find((e) => e.code === l.etat)
}

/** La teinte de liste de chaque icone d'etat de l'accueil (a-traiter, saisie-manuelle...). */
const TeinteIcone: Record<string, Teinte> = {
  'a-traiter': 'bleu', 'saisie-manuelle': 'orange', validee: 'vert', 'en-attente': 'ardoise', rejetee: 'rouge',
}
export const teinteEtat = (e: EtatEcranVue | undefined): Teinte => (e ? TeinteIcone[e.icone] ?? 'gris' : 'gris')

/** Une teinte stable par groupe : son rang dans les groupes de l'ecran. */
const TeintesGroupe: Teinte[] = ['bleu', 'violet', 'orange', 'vert', 'ardoise', 'rouge']
export const teinteGroupe = (d: DescripteurEcranVue, groupe: string): Teinte =>
  TeintesGroupe[Math.max(0, d.groupes.indexOf(groupe)) % TeintesGroupe.length] ?? 'gris'

/** La mention « exemple » : des lignes du descripteur, aux noms fictifs. */
export function Exemple() {
  return <span className="rig-tcw__pastille rig-tcw__pastille--accent rig-tcw__exemple">exemple</span>
}

/** D'ou viennent les lignes : la base du greffe, ou des exemples (avec la raison et le plafond s'il y en a). */
export function Provenance({ vue }: { vue: VueLignesEcran }) {
  const suite = vue.message ? ` ${vue.message}` : ''
  if (vue.mode === 'base' || vue.mode === 'mixte') {
    return (
      <p className="rig-eg-note" role="note">
        Lignes de la base du greffe {vue.greffe}
        {vue.mode === 'mixte' && <>, sauf les groupes marqués <Exemple /></>}.{suite}
      </p>
    )
  }
  return (
    <p className="rig-eg-note" role="note">
      <Exemple /> Lignes d’exemple : noms fictifs, aucun dossier réel.{suite}
    </p>
  )
}

/** Une ligne en exemple dans un ecran branche (mode mixte : son groupe n'a pas encore de lecteur). */
export const enExemple = (vue: VueLignesEcran | null, l: { groupe: string }) => vue?.mode === 'mixte' && vue.exemples.includes(l.groupe)

/** Le compte rendu d'un geste : ce que l'ecran a fait, ou pourquoi il n'a rien fait. Annonce aux lecteurs d'ecran. */
export function useAnnonce(): [CompteRendu, (texte: CompteRendu) => void] {
  return useState<CompteRendu>(null)
}
/**
 * Ce qu'un geste a dit : du texte (fait, ou demonstration), ou { refus } quand RIG v2 a refuse et que RIEN n'a ete ecrit.
 * Le refus porte le badge « Refus » et role="alert" (convention de EcranCourriels.tsx, EcranComptes.tsx) ; le reste
 * garde role="status" : sans cela un refus ressemblait a un succes (lot 2, captures du 26/11/2026).
 */
// fix-ok: cause mesuree sur les captures lot2-09/11 : Annonce rendait tout message en <p role="status"> sans distinguer ResultatGesteVue.fait.
export type CompteRendu =string | { refus: string } | null
export function Annonce({ texte }: { texte: CompteRendu }) {
  if (texte === null) return null
  if (typeof texte === 'string') return <p className="rig-eg-annonce" role="status">{texte}</p>
  return <p className="rig-eg-annonce" role="alert"><span className="rig-etat rig-etat--alerte">Refus</span>{' '}{texte.refus}</p>
}

/** Le refus d'un geste : la phrase de demonstration, le geste et sa portee. */
export const refus = (geste: string, portee?: string) => `${DEMONSTRATION} — « ${geste} »${portee ? ` ${portee}` : ''}.`

/** Les saisies d'un formulaire : une valeur par champ, cle « section › champ ». */
export type Saisies = Readonly<Record<string, string>>
export const cleChamp = (section: string, champ: string) => `${section} › ${champ}`

/** Un champ se compte rempli : une saisie non blanche, une case cochee. Une sous-liste ne se saisit pas ici. */
const rempli = (c: ChampEcranVue, valeur: string | undefined) => c.type !== 'Tableau' && (valeur ?? '').trim() !== ''

/** Une section a au moins un champ qu'on saisit ici (pas une sous-liste seule). */
export const saisissable = (s: MarcheEcranVue) => s.champs.some((c) => c.type !== 'Tableau')

/** Une section est faite quand l'un de ses champs saisissables est rempli. */
export const sectionFaite = (s: MarcheEcranVue, saisies: Saisies) => s.champs.some((c) => rempli(c, saisies[cleChamp(s.titre, c.libelle)]))

/**
 * Les controles de la fiche RIG v2 a l'enregistrement : chaque section OBLIGATOIRE de la fiche (« Obligatoire pour
 * valider ») doit avoir au moins un champ rempli. Rend les titres des sections qui manquent. Pour ce qu'on CREE (Saisie)
 * ou qu'on ENGAGE (Operation) ; pas pour une Fiche, ou un champ vide garde la valeur de la base (FormeFiche).
 */
export function manquantes(sections: MarcheEcranVue[], saisies: Saisies): string[] {
  return sections.filter((s) => s.obligatoire && saisissable(s) && !sectionFaite(s, saisies)).map((s) => s.titre)
}

/** Un champ saisi : son controle selon son type. Une sous-liste (Tableau) se lit avec l'objet, elle ne se saisit pas. */
export function ChampSaisie({ champ, valeur, changer, lecture = false, compact = false }: {
  champ: ChampEcranVue; valeur: string; changer: (valeur: string) => void; lecture?: boolean; compact?: boolean
}) {
  const commun = { 'aria-label': champ.libelle, disabled: lecture, className: 'rig-saisie-champ__saisie' }
  let controle: ReactNode
  switch (champ.type) {
    case 'Zone':
      controle = <textarea {...commun} rows={compact ? 1 : 3} value={valeur} onChange={(e) => changer(e.target.value)} />
      break
    case 'Date':
      controle = <input {...commun} type="date" value={valeur} onChange={(e) => changer(e.target.value)} />
      break
    case 'Periode': {
      const [du = '', au = ''] = valeur.split('|')
      controle = (
        <span className="rig-eg-periode">
          <input {...commun} aria-label={`${champ.libelle} : du`} type="date" value={du} onChange={(e) => changer(`${e.target.value}|${au}`)} />
          <span aria-hidden="true">au</span>
          <input {...commun} aria-label={`${champ.libelle} : au`} type="date" value={au} onChange={(e) => changer(`${du}|${e.target.value}`)} />
        </span>
      )
      break
    }
    case 'Nombre':
      controle = <input {...commun} type="number" value={valeur} onChange={(e) => changer(e.target.value)} />
      break
    case 'Montant':
      controle = <input {...commun} inputMode="decimal" placeholder="0,00 €" value={valeur} onChange={(e) => changer(e.target.value)} />
      break
    case 'Case':
      return (
        <label className="rig-eg-case">
          <input type="checkbox" aria-label={champ.libelle} disabled={lecture} checked={valeur === 'oui'}
                 onChange={(e) => changer(e.target.checked ? 'oui' : '')} />
          {!compact && <span>{champ.libelle}</span>}
        </label>
      )
    case 'Liste':
      controle = (
        <select {...commun} value={valeur} onChange={(e) => changer(e.target.value)}>
          <option value="">—</option>
          {champ.options.map((o) => <option key={o} value={o}>{o}</option>)}
        </select>
      )
      break
    case 'Tableau':
      controle = <div className="rig-eg-tableau" role="note">Liste lue avec l’objet dans la base du greffe.</div>
      break
    default:
      controle = <input {...commun} type="text" value={valeur} onChange={(e) => changer(e.target.value)} />
  }
  if (compact) return <>{controle}</>
  return (
    <label className={`rig-saisie-champ${champ.type === 'Zone' || champ.type === 'Tableau' ? ' rig-saisie-champ--pleine' : ''}`}>
      <span className="rig-saisie-champ__libelle">{champ.libelle}</span>
      {controle}
    </label>
  )
}

/** Le champ lu d'une fiche, a la section et au libelle du descripteur ; undefined si la V3 ne le lit pas. */
export const champLu = (fiche: FicheVue | null | undefined, section: string, libelle: string) =>
  fiche?.sections.find((s) => s.titre === section)?.champs.find((c) => c.libelle === libelle)

/** Une valeur lue, comme l'agent l'ecrit : une date en jj/mm/aaaa, une case en Oui, le vide en « — ». */
const lue = (c: ChampEcranVue, valeur: string) =>
  valeur === '' ? '—'
    : c.type === 'Case' ? (valeur === 'oui' ? 'Oui' : 'Non')
      : c.type === 'Date' && /^\d{4}-\d{2}-\d{2}$/.test(valeur) ? valeur.split('-').reverse().join('/') : valeur

/** Une sous-liste lue avec l'objet (Tableau) : ses colonnes et ses lignes. */
function TableauLu({ champ }: { champ: ChampFicheVue }) {
  if (champ.lignes.length === 0) return <div className="rig-eg-tableau" role="note">Aucune ligne dans la base du greffe.</div>
  return (
    <table className="rig-table rig-eg-tableau-lu" aria-label={champ.libelle}>
      <thead><tr>{champ.colonnes.map((c) => <th key={c}>{c}</th>)}</tr></thead>
      <tbody>{champ.lignes.map((l, k) => <tr key={k}>{l.map((v, j) => <td key={j}>{v === '' ? '—' : v}</td>)}</tr>)}</tbody>
    </table>
  )
}

/** Ce que dit un champ que la V3 ne lit pas encore dans la base (il n'est pas dans la fiche). */
export const NON_LU = 'Non lu dans la base par la V3'

/**
 * Les sections d'une fiche : en formulaire (saisies), ou en lecture (lecture = vrai). Avec la fiche LUE (fiche) : chaque
 * champ porte sa valeur de la base ; seuls ceux que l'ecrivain sait enregistrer se saisissent ; un champ absent de la
 * fiche le dit (NON_LU). Sans fiche (ligne d'exemple) : les champs vides, comme avant.
 * LES REGLES D'AFFICHAGE DE LA FICHE RIG v2 (descripteur : visibleSi, bloqueSi, exclut) se jouent sur la valeur COURANTE de la
 * case qui commande (saisie, sinon lue) : un champ visibleSi ne se montre que case cochee, et se vide quand elle se decoche (en
 * chaine : ce qu'il commande se vide aussi) ; un champ bloqueSi, case cochee, se montre sans se saisir ; une case exclut, cochee,
 * decoche au clic celles qu'elle nomme. PROC_GTABREF, un etat de PC (OPE_AFFICHE_SQLBASE_PC_ETAT.cs:89-108, :116-119, :135-156).
 * fix-ok: controle final du lot 14 (27/11/2026, 5e reprise) -- cocher « Clôture » laissait « Ouverture » cochee a l'ecran
 * (capture lot14-captures-reprise5\08) : aucune regle d'exclusion n'atteignait la grille ; test « cocher « Clôture » décoche
 * « Ouverture » au clic » rouge sans elle. fix-ok: controle final du lot 14 (27/11/2026, 4e reprise) -- « Résolution
 * du plan », « Code clôture » et « À sauver » s'affichaient toujours et se saisissaient, quand la V2 les masque ou les bloque :
 * aucune regle d'affichage n'atteignait la grille (test « Sections : les regles d'affichage de la fiche RIG v2 », rouge sans elles).
 */
export function Sections({ sections, saisies, changer, lecture = false, fiche = null }: {
  sections: MarcheEcranVue[]; saisies: Saisies; changer: (cle: string, valeur: string) => void; lecture?: boolean
  fiche?: FicheVue | null
}) {
  // La valeur courante d'un champ de la section : saisie, sinon lue dans la fiche.
  const courante = (s: MarcheEcranVue, libelle: string) =>
    saisies[cleChamp(s.titre, libelle)] ?? champLu(fiche, s.titre, libelle)?.valeur ?? ''
  const masque = (s: MarcheEcranVue, c: ChampEcranVue) => !!c.visibleSi && courante(s, c.visibleSi) !== 'oui'
  const bloque = (s: MarcheEcranVue, c: ChampEcranVue) => !!c.bloqueSi && courante(s, c.bloqueSi) === 'oui'
  // Une case cochee decoche celles qu'elle exclut (si elles le sont) ; une case decochee vide les champs qu'elle montre, et
  // ceux qu'ils montrent a leur tour (une fois chacun) : decochee par exclusion, « Ouverture » vide aussi ce qu'elle montre.
  const changerSuivi = (s: MarcheEcranVue, libelle: string, valeur: string, vus = new Set<string>()) => {
    vus.add(libelle)
    changer(cleChamp(s.titre, libelle), valeur)
    if (valeur === 'oui') {
      for (const e of s.champs.find((x) => x.libelle === libelle)?.exclut ?? [])
        if (!vus.has(e) && courante(s, e) === 'oui') changerSuivi(s, e, '', vus)
      return
    }
    for (const d of s.champs) if (d.visibleSi === libelle && !vus.has(d.libelle)) changerSuivi(s, d.libelle, '', vus)
  }
  if (fiche) {
    return (
      <div className="rig-eg-sections">
        {sections.map((s) => (
          <section key={s.titre} className="rig-eg-section" aria-label={s.titre}>
            <header className="rig-eg-section__tete"><span className="rig-eg-section__titre">{s.titre}</span></header>
            <p className="rig-eg-section__consigne">{s.consigne}</p>
            <div className="rig-champs rig-eg-champs">
              {s.champs.map((c) => {
                if (masque(s, c)) return null
                const lu = champLu(fiche, s.titre, c.libelle)
                const cle = cleChamp(s.titre, c.libelle)
                if (c.type === 'Tableau' || lecture || !lu?.modifiable) {
                  return (
                    <div key={c.libelle} className={`rig-saisie-champ rig-eg-lu${c.type === 'Tableau' || c.type === 'Zone' ? ' rig-saisie-champ--pleine' : ''}`}>
                      <span className="rig-saisie-champ__libelle">{c.libelle}</span>
                      {!lu ? <span className="rig-eg-non-lu">{NON_LU}</span>
                        : c.type === 'Tableau' ? <TableauLu champ={lu} />
                          : <span className="rig-eg-lu__valeur" aria-label={c.libelle}>{lue(c, lu.valeur)}</span>}
                    </div>
                  )
                }
                return <ChampSaisie key={c.libelle} champ={c} valeur={saisies[cle] ?? lu.valeur} lecture={bloque(s, c)}
                                    changer={(v) => changerSuivi(s, c.libelle, v)} />
              })}
            </div>
          </section>
        ))}
      </div>
    )
  }
  return (
    <div className="rig-eg-sections">
      {sections.map((s) => (
        <section key={s.titre} className="rig-eg-section" aria-label={s.titre}>
          <header className="rig-eg-section__tete">
            <span className="rig-eg-section__titre">{s.titre}</span>
            {s.obligatoire && !lecture && saisissable(s) && <span className="rig-saisie-champ__requis">obligatoire</span>}
          </header>
          <p className="rig-eg-section__consigne">{s.consigne}</p>
          {s.champs.length > 0 && (lecture ? (
            <dl className="rig-eg-lecture">
              {s.champs.map((c) => (
                <div key={c.libelle} className="rig-eg-lecture__champ">
                  <dt>{c.libelle}</dt>
                  <dd>{c.type === 'Tableau' ? 'Liste lue avec l’objet' : '—'}</dd>
                </div>
              ))}
            </dl>
          ) : (
            <div className="rig-champs rig-eg-champs">
              {s.champs.filter((c) => !masque(s, c)).map((c) => (
                <ChampSaisie key={c.libelle} champ={c} valeur={saisies[cleChamp(s.titre, c.libelle)] ?? ''} lecture={bloque(s, c)}
                             changer={(v) => changerSuivi(s, c.libelle, v)} />
              ))}
            </div>
          ))}
        </section>
      ))}
    </div>
  )
}

/** Le texte d'une ligne pour la recherche : nom, precisions, groupe et cellules. */
export const texteLigne = (l: LigneEcranVue) => [l.nom, l.precisions, l.groupe, ...(l.valeurs ?? [])].join(' ')

/** Un montant ou un nombre lu dans une cellule (« 180.00 », « 1 234,50 ») ; NaN s'il n'y en a pas. */
export function nombreDe(texte: string | undefined): number {
  if (texte === undefined || texte.trim() === '') return Number.NaN
  return Number(texte.replace(/[\s  €]/g, '').replace(',', '.'))
}

/** Une ligne passe-t-elle les criteres remplis ? Chaque critere rempli doit se lire dans la ligne (sans casse ni accents). */
export function passeCriteres(l: LigneEcranVue, criteres: Saisies): boolean {
  const texte = normaliser(texteLigne(l))
  return Object.values(criteres).every((v) => v.trim() === '' || texte.includes(normaliser(v.trim())))
}

/** Le panneau des criteres de la fiche RIG v2 (aside.rig-criteres, replie par defaut par la liste moderne). */
export function PanneauCriteres({ d, criteres, changer, effacer, children }: {
  d: DescripteurEcranVue; criteres: Saisies; changer: (critere: string, valeur: string) => void; effacer: () => void
  children?: ReactNode
}) {
  return (
    <aside className="rig-criteres" aria-label={`Critères — ${d.titre}`}>
      <div className="rig-kicker">Critères</div>
      {d.criteres.map((c) => (
        <label key={c} className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">{c}</span>
          <input className="rig-saisie-champ__saisie" aria-label={c} value={criteres[c] ?? ''} onChange={(e) => changer(c, e.target.value)} />
        </label>
      ))}
      {children}
      <div className="rig-criteres__actions">
        <button type="button" className="rig-bouton rig-bouton--plat" onClick={effacer}>Effacer</button>
      </div>
    </aside>
  )
}

/** Ce qu'une table de lignes sait faire en plus de lister : cocher, deplier, ouvrir. */
export interface ProprietesTableLignes {
  d: DescripteurEcranVue
  vue: VueLignesEcran | null
  /** Les lignes a montrer (deja filtrees). */
  lignes: LigneTenue[]
  /** Toutes les lignes de l'ecran : pour dire « aucune ligne » ou « rien ne correspond ». */
  total: number
  /** Une recherche, un etat ou un critere filtre la liste : les groupes vides se cachent alors. */
  filtre?: boolean | undefined
  /** Les cases a cocher (File, Audience, Operation) ; absent, pas de case. Sans « toutes », une seule ligne se choisit
   * (Operation sur UN objet) : pas de case « Tout cocher ». */
  cases?: { cochees: ReadonlySet<number>; basculer: (i: number) => void; toutes?: ((cocher: boolean) => void) | undefined } | undefined
  /** Le detail d'une ligne depliee (chevron) ; absent, pas de chevron. */
  detail?: ((l: LigneTenue) => ReactNode) | undefined
  /** Clic sur la ligne : l'ouvrir (Fiche, Consultation, Document). */
  ouvrir?: ((l: LigneTenue) => void) | undefined
  /** La ligne ouverte, soulignee. */
  ouverte?: number | null | undefined
  /** Les actions du menu « … » de la ligne. */
  actions?: ((l: LigneTenue) => ActionLigne[]) | undefined
}

/** La table des lignes d'un ecran genere : identite (nom, precisions), procedure ou groupe, etat. */
export function TableLignes({ d, vue, lignes, total, filtre = false, cases, detail, ouvrir, ouverte, actions }: ProprietesTableLignes) {
  const [depliees, poserDepliees] = useState<ReadonlySet<number>>(() => new Set())
  const avecGroupe = d.groupes.length > 1
  const nbColonnes = (cases ? 1 : 0) + (detail ? 1 : 0) + 2 + (avecGroupe ? 1 : 0) + (actions ? 1 : 0)
  const cochables = cases ? lignes.filter((l) => !figee(l.etat)) : []
  const toutesCochees = cases !== undefined && cochables.length > 0 && cochables.every((l) => cases.cochees.has(l.i))
  const basculerDepliee = (i: number) => poserDepliees((avant) => {
    const apres = new Set(avant)
    if (!apres.delete(i)) apres.add(i)
    return apres
  })
  const aucuneLigne = `Aucune ligne dans la base du greffe${vue?.greffe ? ` ${vue.greffe}` : ''}.`
  const videTable = (
    <tr>
      <td colSpan={nbColonnes} className="rig-table__vide">
        {vue === null ? 'Lecture des lignes…' : total === 0 ? aucuneLigne : 'Rien ne correspond à la recherche.'}
      </td>
    </tr>
  )
  const rangee = (l: LigneTenue) => {
    const etat = etatDe(d, l)
    const ouverteIci = depliees.has(l.i)
    return [
      <tr key={l.i} className={[ouvrir ? 'rig-eg-ligne--ouvrable' : '', ouverte === l.i ? 'rig-eg-ligne--ouverte' : ''].join(' ').trim() || undefined}
          aria-current={ouverte === l.i ? 'true' : undefined} onClick={ouvrir ? () => ouvrir(l) : undefined}>
        {cases && (
          <td className="rig-eg-case-cellule" onClick={(e) => e.stopPropagation()}>
            <input type="checkbox" aria-label={`Cocher ${l.nom}`} checked={cases.cochees.has(l.i)} disabled={figee(l.etat)}
                   title={figee(l.etat) ? `Ligne figée (${etat?.libelle ?? l.etat}) : aucun geste` : undefined}
                   onChange={() => cases.basculer(l.i)} />
          </td>
        )}
        {detail && <CelluleChevron ouverte={ouverteIci} basculer={() => basculerDepliee(l.i)} />}
        <CelluleIdentite nom={l.nom} objet={l.precisions} titre={`${l.nom} — ${l.precisions}`} />
        {avecGroupe && (
          <td title={l.groupe}>
            <Pastille teinte={teinteGroupe(d, l.groupe)}>{l.groupe}</Pastille>
            {enExemple(vue, l) && <> <Exemple /></>}
          </td>
        )}
        <td><PointEtat teinte={teinteEtat(etat)}>{etat?.libelle ?? l.etat}</PointEtat></td>
        {actions && <td className="rig-lm-cellule-menu"><MenuLigne actions={actions(l)} libelle={`Actions — ${l.nom}`} /></td>}
      </tr>,
      detail && ouverteIci ? <tr key={`${l.i}-detail`} className="rig-eg-detail"><td colSpan={nbColonnes}>{detail(l)}</td></tr> : null,
    ]
  }
  // Un ecran REUNI (plusieurs procedures) range ses lignes par procedure, un bloc chacune, titre et nombre en tete. Un
  // groupe branche sur la base mais sans ligne (vue.vides) reste a l'ecran avec son titre et « Aucune ligne » : un
  // message unique ne les remplace pas (cas « Envois et retours », 25/11/2026, plan-f7ba4de2 lot 2). Une recherche ou
  // un etat qui filtre cache les groupes vides.
  const vides = filtre ? [] : vue?.vides ?? []
  const groupes = d.membres.length === 0 ? null : [...new Set([...d.groupes, ...lignes.map((l) => l.groupe)])]
    .map((titre) => ({ titre, lignes: lignes.filter((l) => l.groupe === titre) }))
    .filter((g) => g.lignes.length > 0 || vides.includes(g.titre))
  return (
    <div className="rig-table-cadre">
      <table className="rig-table rig-lm-table rig-eg-table">
        <colgroup>
          {cases && <col style={{ width: '34px' }} />}
          {detail && <col style={{ width: '30px' }} />}
          <col />
          {avecGroupe && <col style={{ width: '24%' }} />}
          <col style={{ width: '18%' }} />
          {actions && <col style={{ width: '40px' }} />}
        </colgroup>
        <thead>
          <tr>
            {cases && (
              <th className="rig-eg-case-cellule">
                {cases.toutes && (
                  <input type="checkbox" aria-label="Tout cocher" checked={toutesCochees} disabled={cochables.length === 0}
                         onChange={(e) => cases.toutes?.(e.target.checked)} />
                )}
              </th>
            )}
            {detail && <th aria-label="Déplier" />}
            <th>{d.sujet.charAt(0).toUpperCase() + d.sujet.slice(1)}</th>
            {avecGroupe && <th>{d.membres.length > 0 ? 'Procédure' : 'Groupe'}</th>}
            <th>État</th>
            {actions && <th aria-label="Actions" />}
          </tr>
        </thead>
        {groupes === null ? (
          <tbody>
            {lignes.flatMap(rangee)}
            {lignes.length === 0 && videTable}
          </tbody>
        ) : groupes.length === 0 ? <tbody>{videTable}</tbody> : groupes.map((g) => (
          <tbody key={g.titre} className="rig-tcw__groupe rig-eg-groupe">
            <tr className="rig-eg-groupe__tete">
              <th colSpan={nbColonnes} scope="colgroup">
                <span className="rig-tcw__groupe-titre">{g.titre}</span>
                <span className="rig-tcw__groupe-nombre">{g.lignes.length}</span>
              </th>
            </tr>
            {g.lignes.length > 0 ? g.lignes.flatMap(rangee) : (
              <tr><td colSpan={nbColonnes} className="rig-tcw__vide-groupe">{aucuneLigne}</td></tr>
            )}
          </tbody>
        ))}
      </table>
    </div>
  )
}

/** Les groupes branches mais vides (ecran reuni) : dits sous la table, pour que l'agent ne croie pas la fonction absente. */
export function GroupesVides({ vue }: { vue: VueLignesEcran | null }) {
  const vides = vue?.vides ?? []
  if (vides.length === 0) return null
  return <p className="rig-eg-note">Aucune ligne dans la base du greffe{vue?.greffe ? ` ${vue.greffe}` : ''} pour : {vides.join(' · ')}.</p>
}

/** La synthese d'une liste : les lignes reelles, et a part celles en exemple. */
export function synthese(d: DescripteurEcranVue, vue: VueLignesEcran | null, lignes: LigneTenue[]): ReactNode {
  const exemples = lignes.filter((l) => enExemple(vue, l)).length
  return <><b>{lignes.length - exemples}</b> {d.sujet}{exemples > 0 && <> · {exemples} en exemple</>}</>
}
