// SOCLE DES « LISTES MODERNES » (plan « Listes modernes "Tout reuni" », lot 1, 24/11/2026).
//
// Au /draft de conv-2132, Raphael a choisi la proposition « Tout reuni » pour TOUTES les listes en
// tableau : une tete (titre, synthese, « Aller a… Ctrl K »), des cartes chiffrees qui filtrent, des
// puces de vues enregistrees, un tableau a UNE ligne par rangee (avatar d'initiales, pastille de
// nature, point d'etat, mini-barre de montant, menu …), une ligne qui se deplie, un pied de totaux.
// Maquette : Documents\Autowin\draft-listes-modernes\propositions-mix.json, propositions[0].
//
// Ces briques ne portent AUCUNE donnee ni aucun appel au pont : l'ecran calcule, elles dessinent.
// Les styles vivent dans wwwroot/rig-listes.css (classes rig-lm-*), couleurs par jetons seulement ;
// les teintes (natures, etats, avatars) y sont declarees par theme (--rig-liste-*).
//
// Mode d'emploi pour un ecran :
//   <ListeModerne etiquette="Affaires">                    le cadre (carte de surface)
//     <EnteteListe titre synthese filtre />                  la tete ; filtre = « Aller a… » + Ctrl K
//     <CartesChiffrees cartes />                             3 cartes ; clic carte, segment ou legende = filtre
//     <VuesListe vues active choisir enregistrer>            puces de vues + « + Nouvelle vue »
//       <ChoixTri … /> <button className="rig-lm-outil">Exporter</button>
//     </VuesListe>
//     <div className="rig-table-cadre"><table className="rig-table rig-lm-table">
//       <tr> <CelluleChevron/> <CelluleIdentite/> <td><Pastille/></td> <td><PointEtat/></td>
//            <td><MontantBarre/></td> <td className="rig-lm-cellule-menu"><MenuLigne/></td> </tr>
//       <LigneDetail colonnes champs actions />              sous la rangee ouverte
//     </table></div>
//     <PiedListe aide>…totaux…</PiedListe>
//   </ListeModerne>
// Une liste sans affaire (factures, comptes…) prend les briques dont elle a besoin, pas toutes.
//
// Les outils communs, a prendre ICI plutot qu'a recopier dans l'ecran (lot 8 du plan : chaque ecran les recopiait) ;
// fix-ok: chaque manque a ete mesure par un lot (civilite « Mme LEROY » -> ML au lot 2, 17 copies de normaliser, 6 de vues).
//   normaliser(texte)                    cle sans casse ni accents du filtre « Aller a… »
//   useVuesDeSession(ecran, prereglees, criteres, memes)   vues de la session : { vues, active, enregistrer }
//   teinteStatut('ok' | 'alerte' | 'action' | 'attente')   la teinte du PointEtat d'un statut de la charte (rig-etat--*)
//   montant(valeur, { devise, decimales })                  « 1 379,30 € » ; « 50 000 CHF » avec { devise: 'CHF', decimales: 0 }
//   <CelluleIdentite avatar={false} />   une ligne sans personne ni dossier (article de facture) : nom et objet seuls
//   <VuesListe etiquette="Vues des mesures" />              un second groupe de puces dans la meme liste
//
// Le panneau de criteres (aside.rig-criteres, frere de <ListeModerne> dans .rig-proc__corps) est REPLIE par defaut :
// la tete de la liste porte un bouton « Criteres » qui le rouvre (demande du 25/11/2026, conv-2132 : la maquette
// « Tout reuni » n'a pas de panneau, la liste y prend toute la largeur). Rien a faire dans l'ecran : le cadre trouve
// son panneau tout seul. Le choix vaut pour la session et tous les ecrans ; oublierRepliCriteres() le remet (tests).

import {
  createContext, useContext, useEffect, useLayoutEffect, useRef, useState, useSyncExternalStore,
  type CSSProperties, type KeyboardEvent, type ReactNode,
} from 'react'
import { EcranVisible } from '../plan/visibilite'

/** Les teintes de la palette des listes, declarees par theme dans rig-listes.css (--rig-liste-<teinte>). */
export type Teinte = 'bleu' | 'orange' | 'ardoise' | 'vert' | 'rouge' | 'violet' | 'gris'

/** Une action de ligne : bouton de la ligne depliee ou entree du menu « … ». */
export interface ActionLigne {
  libelle: string
  agir: () => void
  /** Le bouton principal (« Ouvrir ») : plein, le seul de la ligne. */
  fort?: boolean | undefined
  desactive?: boolean | undefined
}

// ---------------------------------------------------------------- le cadre

// Le panneau de criteres replie ou ouvert : UN choix pour la session et tous les ecrans (ouvert sur la Recherche
// d'affaires, il l'est aussi sur les Demandes ouvertes ensuite). Replie au depart, comme la maquette.
let criteresReplies = true
const abonnesRepli = new Set<() => void>()
function changerRepliCriteres(replie: boolean) {
  criteresReplies = replie
  for (const prevenir of abonnesRepli) prevenir()
}
function useCriteresReplies(): boolean {
  return useSyncExternalStore(
    (prevenir) => { abonnesRepli.add(prevenir); return () => { abonnesRepli.delete(prevenir) } },
    () => criteresReplies)
}
/** Remet le panneau replie, comme au demarrage (tests). */
export function oublierRepliCriteres() { changerRepliCriteres(true) }

/** Ce que la tete de liste sait du panneau de criteres de son ecran ; null = l'ecran n'en a pas. */
interface PanneauCriteres {
  replie: boolean
  /** Les criteres remplis : un filtre cache par le repli ne doit pas passer inapercu. */
  actifs: number
  basculer: () => void
}
const ContexteCriteres = createContext<PanneauCriteres | null>(null)

// La valeur de depart d'un champ du panneau : vide pour une saisie de texte ou de date (« ex. 55339 » n'est qu'une
// indication), la valeur affichee au premier regard pour le reste (« Nombre maxi 50 », « Toutes », une bascule).
const departs = new WeakMap<Element, string>()
function etatChamp(champ: Element): string {
  if (champ instanceof HTMLInputElement) return champ.type === 'checkbox' || champ.type === 'radio' ? String(champ.checked) : champ.value
  if (champ instanceof HTMLSelectElement || champ instanceof HTMLTextAreaElement) return champ.value
  return String(champ.classList.contains('rig-bascule--actif'))
}
function compterCriteres(panneau: HTMLElement): number {
  let actifs = 0
  for (const champ of panneau.querySelectorAll('input, select, textarea, button.rig-bascule')) {
    if (!departs.has(champ)) {
      const texte = champ instanceof HTMLTextAreaElement
        || (champ instanceof HTMLInputElement && ['text', 'search', 'date', 'email', 'tel', ''].includes(champ.type))
      departs.set(champ, texte ? '' : etatChamp(champ))
    }
    if (etatChamp(champ) !== departs.get(champ)) actifs++
  }
  return actifs
}

/**
 * Le cadre d'une liste moderne : la colonne des resultats en carte de surface (.rig-lm), a cote du panneau
 * de criteres. C'est aussi le composant qui donne son nom au fichier (regle de web/src : registre iles.ts).
 * Il trouve le panneau de criteres de son ecran (son frere aside.rig-criteres) et le replie : la feuille
 * (rig-listes.css) le cache et donne alors toute la largeur a la liste ; le bouton de la tete le rouvre.
 */
export function ListeModerne({ etiquette, children }: { etiquette?: string | undefined; children?: ReactNode }) {
  const cadre = useRef<HTMLElement>(null)
  const [panneau, setPanneau] = useState<HTMLElement | null>(null)
  const [actifs, setActifs] = useState(0)
  const replie = useCriteresReplies()
  // A chaque rendu de l'ecran (un critere change = un rendu) : le panneau est-il la, et combien de criteres remplis ?
  useLayoutEffect(() => {
    const trouve = cadre.current?.parentElement?.querySelector<HTMLElement>(':scope > .rig-criteres') ?? null
    if (trouve !== panneau) setPanneau(trouve)
    const compte = trouve ? compterCriteres(trouve) : 0
    if (compte !== actifs) setActifs(compte)
  })
  // Et a chaque frappe dans le panneau, meme d'un champ que l'ecran ne suit pas.
  useEffect(() => {
    if (!panneau) return
    const recompter = () => setActifs(compterCriteres(panneau))
    panneau.addEventListener('input', recompter)
    panneau.addEventListener('change', recompter)
    return () => {
      panneau.removeEventListener('input', recompter)
      panneau.removeEventListener('change', recompter)
    }
  }, [panneau])
  const etat: PanneauCriteres | null = panneau && { replie, actifs, basculer: () => changerRepliCriteres(!replie) }
  return (
    <section ref={cadre} className={`rig-resultats-proc rig-lm${etat?.replie ? ' rig-lm--criteres-replies' : ''}`}
             aria-label={etiquette}>
      <ContexteCriteres.Provider value={etat}>{children}</ContexteCriteres.Provider>
    </section>
  )
}

// ---------------------------------------------------------------- outils de texte

export interface FormeMontant {
  /** Ce qui suit le nombre : « € » (l'euro) par defaut ; un code de monnaie (« CHF », « USD », « EUR ») s'ecrit tel quel. */
  devise?: string | undefined
  /** Le nombre de decimales : 2 par defaut, 0 pour un capital. */
  decimales?: number | undefined
}

/**
 * Un montant a la francaise : « 1 379,30 € » ; avec une devise, « 50 000 CHF » (capital de l'Ouverture de dossier,
 * dans la monnaie que rend le pont). Le separateur des milliers est une espace insecable NORMALE (U+00A0) : celle de
 * toLocaleString('fr-FR') est l'espace FINE (U+202F), quasi invisible a 12 px -- la synthese et le pied lisaient
 * « 1379,30 » quand la carte lisait « 1 379,30 » (captures du lot 1, conv-2135).
 */
export function montant(valeur: number, { devise = '€', decimales = 2 }: FormeMontant = {}): string {
  const nombre = valeur.toLocaleString('fr-FR', { minimumFractionDigits: decimales, maximumFractionDigits: decimales })
  return `${nombre.replace(/\u202f/g, '\u00a0')} ${devise}`
}

/**
 * Un texte sans casse ni accents : la cle de comparaison du filtre « Aller a… » (« dépôt » trouve « Depot au guichet »,
 * « CLOTUREE » trouve « Clôturée »). contientSansCasse (criteres.ts) garde, elle, les accents.
 */
export function normaliser(texte: string): string {
  return texte.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '')
}

// Ni une forme juridique, ni un mot de liaison, ni une civilite ne donnent une initiale : « SCI DU PARC » -> « P »,
// « BATIPRO SARL » -> « B », « COMPTOIR DU NORD » -> « CN » (les avatars de la maquette) ; « Mme LEROY » -> « L »,
// « M. DUVAL » -> « D » (juges et experts, capture du lot 2). Les mots se comparent sans accents : « Maître » = MAITRE.
const MotsSansInitiale = new Set(['SA', 'SAS', 'SASU', 'SARL', 'EURL', 'SCI', 'SNC', 'SCP', 'SCM', 'SELARL', 'SELAS', 'EI', 'EIRL',
  'DU', 'DE', 'DES', 'LA', 'LE', 'LES', 'ET', 'D', 'L', 'AU', 'AUX', '&',
  'M', 'MME', 'MLLE', 'ME', 'MAITRE', 'DR'])

/** Les initiales de l'avatar : la premiere lettre des deux premiers mots qui comptent. */
export function initiales(nom: string): string {
  const mots = normaliser(nom).toUpperCase()
    .split(/[^A-Z0-9&]+/).filter((m) => m.length > 0)
  const utiles = mots.filter((m) => !MotsSansInitiale.has(m))
  return (utiles.length > 0 ? utiles : mots).slice(0, 2).map((m) => m[0]).join('') || '?'
}

/** Les statuts de la charte : les classes rig-etat--* (ok, alerte, action, attente). */
export type StatutCharte = 'ok' | 'alerte' | 'action' | 'attente'

const TeinteDuStatut: Record<StatutCharte, Teinte> = { ok: 'vert', alerte: 'rouge', action: 'bleu', attente: 'orange' }

/**
 * La teinte de liste d'un statut de la charte, pour un PointEtat : ok vert, alerte rouge, action bleu, attente orange ;
 * tout autre statut (vide, inconnu) en gris. Une seule table pour tous les ecrans (lot 6 : cinq la recopiaient).
 */
export function teinteStatut(statut: string | null | undefined): Teinte {
  return statut !== null && statut !== undefined && Object.hasOwn(TeinteDuStatut, statut) ? TeinteDuStatut[statut as StatutCharte] : 'gris'
}

/** Nombre de teintes d'avatar declarees dans rig-listes.css (--rig-liste-avatar-1 a -8). */
export const TeintesAvatar = 8

/** La teinte de l'avatar, STABLE pour un meme nom (1 a 8) : un nom garde sa couleur d'une liste a l'autre. */
export function rangAvatar(nom: string): number {
  let h = 0
  for (const c of nom.trim().toUpperCase()) h = (h * 31 + c.charCodeAt(0)) >>> 0
  return (h % TeintesAvatar) + 1
}

// ---------------------------------------------------------------- tete de liste

export interface FiltreRapide {
  valeur: string
  changer: (valeur: string) => void
  /** Le texte gris du champ : « Aller a une affaire… ». */
  indication: string
  /** Entree dans le champ (aller a la ligne trouvee). */
  valider?: (() => void) | undefined
}

export interface ProprietesEnteteListe {
  titre: string
  /** La phrase sous le titre : « 15 resultats · 12 en cours · solde 1 379,30 € ». */
  synthese?: ReactNode
  /** Infobulle de la synthese (le detail qu'elle resume). */
  detail?: string | undefined
  /** Le champ « Aller a… » ; absent, pas de champ ni de Ctrl K. */
  filtre?: FiltreRapide | undefined
}

/**
 * Titre, synthese et champ de filtre rapide. Ctrl K pose le curseur dans le champ : ecoute a la
 * CAPTURE avec preventDefault, comme Commandes web, pour passer avant la palette de l'application
 * (Application.tsx), et seulement quand l'ecran est MONTRE (plan/visibilite.ts) -- un onglet cache
 * volerait sinon la touche a l'ecran visible. L'ecoute est posee A L'AFFICHAGE (useLayoutEffect) : posee par un
 * effet differe, une frappe arrivee entre l'affichage du champ et cet effet n'etait pas prise (rouge intermittent
 * d'EcranSupervision.test.tsx sous charge, reproduit par le test « des que le champ est affiche »).
 */
export function EnteteListe({ titre, synthese, detail, filtre }: ProprietesEnteteListe) {
  const visible = useContext(EcranVisible)
  const panneau = useContext(ContexteCriteres)
  const champ = useRef<HTMLInputElement>(null)
  const avecFiltre = filtre !== undefined
  useLayoutEffect(() => {
    if (!visible || !avecFiltre) return
    const surTouche = (e: globalThis.KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== 'k' || !champ.current) return
      e.preventDefault()
      champ.current.focus()
      champ.current.select()
    }
    window.addEventListener('keydown', surTouche, true)
    return () => window.removeEventListener('keydown', surTouche, true)
  }, [visible, avecFiltre])

  function surTouche(e: KeyboardEvent<HTMLInputElement>) {
    if (!filtre) return
    if (e.key === 'Enter' && filtre.valider) { e.preventDefault(); filtre.valider() }
    // Echap vide d'abord le champ ; il n'atteint pas l'application (qui fermerait l'ecran).
    else if (e.key === 'Escape' && filtre.valeur !== '') { e.preventDefault(); filtre.changer('') }
  }

  return (
    <div className="rig-lm-tete">
      {panneau && (
        <button type="button" className={`rig-lm-outil rig-lm-criteres${panneau.replie ? '' : ' rig-lm-criteres--ouvert'}`}
                aria-expanded={!panneau.replie} onClick={panneau.basculer}
                title={panneau.replie ? 'Afficher les criteres de recherche' : 'Replier les criteres : la liste prend toute la largeur'}>
          <svg viewBox="0 0 16 16" aria-hidden="true"><rect x="1.5" y="2.5" width="13" height="11" rx="2" /><path d="M6 2.5v11" /></svg>
          Criteres
          {panneau.actifs > 0 && <span className="rig-lm-criteres__compte">{panneau.actifs}</span>}
        </button>
      )}
      <div className="rig-lm-tete__textes">
        <div className="rig-lm-titre">{titre}</div>
        {synthese !== undefined && <div className="rig-lm-synthese" title={detail}>{synthese}</div>}
      </div>
      {filtre && (
        <label className="rig-lm-cherche">
          <input ref={champ} className="rig-lm-cherche__saisie" aria-label={filtre.indication} placeholder={filtre.indication}
                 value={filtre.valeur} onChange={(e) => filtre.changer(e.target.value)} onKeyDown={surTouche} />
          <span className="rig-lm-touche">Ctrl K</span>
        </label>
      )}
    </div>
  )
}

// ---------------------------------------------------------------- cartes chiffrees

export interface SegmentCarte {
  cle: string
  /** Le nom du segment, dans la legende : « Instance ». */
  libelle: string
  /** Sa part dans la barre (un montant, un compte). */
  valeur: number
  teinte: Teinte
  /** Ce que la legende affiche apres le libelle (« 334,30 € ») ; absent, rien. */
  texte?: string | undefined
  /** Le filtre de ce segment est pose. */
  actif?: boolean | undefined
  /** Clic sur le segment ou sur sa legende : le filtre. */
  choisir?: (() => void) | undefined
}

export interface CarteChiffree {
  cle: string
  titre: string
  valeur: ReactNode
  /** Petit texte gris apres la valeur : « sur 15 ». */
  complement?: ReactNode
  segments?: SegmentCarte[] | undefined
  /** Montrer la legende sous la barre. */
  legende?: boolean | undefined
  /** Un anneau a gauche de la valeur : part sur total. */
  anneau?: { part: number; total: number; teinte?: Teinte | undefined } | undefined
  /** Le filtre de la carte est pose. */
  actif?: boolean | undefined
  /** Clic sur la carte (hors segment) : le filtre. */
  choisir?: (() => void) | undefined
  /** Infobulle : ce que fait le clic. */
  indication?: string | undefined
}

const Circonference = 2 * Math.PI * 15

function Anneau({ part, total, teinte }: { part: number; total: number; teinte: Teinte }) {
  const plein = total > 0 ? Math.min(1, Math.max(0, part / total)) * Circonference : 0
  return (
    <svg className={`rig-lm-anneau rig-lm-t-${teinte}`} viewBox="0 0 36 36" aria-hidden="true">
      <circle cx="18" cy="18" r="15" className="rig-lm-anneau__fond" />
      <circle cx="18" cy="18" r="15" className="rig-lm-anneau__plein"
              strokeDasharray={`${plein.toFixed(1)} ${Circonference.toFixed(1)}`} transform="rotate(-90 18 18)" />
    </svg>
  )
}

/** Les cartes chiffrees de la tete de liste. Une carte, un segment ou une entree de legende qui sait filtrer est cliquable. */
export function CartesChiffrees({ cartes }: { cartes: CarteChiffree[] }) {
  return (
    <div className="rig-lm-cartes">
      {cartes.map((c) => {
        const cliquable = c.choisir !== undefined
        const agir = c.choisir
        return (
          <div key={c.cle} className={`rig-lm-carte${cliquable ? ' rig-lm-carte--cliquable' : ''}${c.actif ? ' rig-lm-carte--active' : ''}`}
               {...(agir ? {
                 role: 'button', tabIndex: 0, 'aria-pressed': c.actif ? true : false, title: c.indication,
                 onClick: agir,
                 onKeyDown: (e: KeyboardEvent<HTMLDivElement>) => {
                   if (e.target === e.currentTarget && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); agir() }
                 },
               } : {})}>
            <div className="rig-lm-carte__titre">{c.titre}</div>
            <div className="rig-lm-carte__ligne">
              {c.anneau && <Anneau part={c.anneau.part} total={c.anneau.total} teinte={c.anneau.teinte ?? 'bleu'} />}
              <div className="rig-lm-carte__valeur">{c.valeur}{c.complement !== undefined && <small> {c.complement}</small>}</div>
            </div>
            {c.segments && c.segments.length > 0 && (
              <div className="rig-lm-carte__barre">
                {c.segments.filter((s) => s.valeur > 0).map((s) => (
                  <button key={s.cle} type="button" tabIndex={-1} aria-label={`${s.libelle}${s.texte ? ' ' + s.texte : ''}`}
                          title={`${s.libelle}${s.texte ? ' ' + s.texte : ''}`}
                          className={`rig-lm-segment rig-lm-t-${s.teinte}${s.actif ? ' rig-lm-segment--actif' : ''}`}
                          style={{ flex: `${s.valeur} 1 0` }}
                          onClick={(e) => { e.stopPropagation(); s.choisir?.() }} />
                ))}
              </div>
            )}
            {c.legende && c.segments && (
              <div className="rig-lm-legende">
                {c.segments.map((s) => (
                  <button key={s.cle} type="button" aria-pressed={s.actif ? true : false}
                          className={`rig-lm-legende__entree rig-lm-t-${s.teinte}${s.actif ? ' rig-lm-legende__entree--active' : ''}`}
                          disabled={!s.choisir} onClick={(e) => { e.stopPropagation(); s.choisir?.() }}>
                    <i />{s.libelle}{s.texte ? ' ' + s.texte : ''}
                  </button>
                ))}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}

// ---------------------------------------------------------------- vues enregistrees

export interface VueListe {
  cle: string
  libelle: string
  /** Combien de lignes la vue montre ; absent tant qu'on ne le sait pas. */
  compte?: number | undefined
}

export interface ProprietesVuesListe {
  /** Le nom du groupe de puces pour un lecteur d'ecran : « Vues » par defaut. Deux groupes d'une meme liste
   *  (experts et mesures) doivent porter chacun le leur. */
  etiquette?: string | undefined
  vues: VueListe[]
  /** La vue dont les criteres sont ceux de l'ecran, ou null (criteres a la main). */
  active: string | null
  choisir: (cle: string) => void
  /** « + Nouvelle vue » : enregistre les criteres courants sous ce nom. Absent, pas de bouton. */
  enregistrer?: ((nom: string) => void) | undefined
  /** Les outils a droite : tri, export. */
  children?: ReactNode
}

/** Les puces de vues a la place des onglets, et les outils de la liste. */
export function VuesListe({ etiquette = 'Vues', vues, active, choisir, enregistrer, children }: ProprietesVuesListe) {
  const [nommer, setNommer] = useState(false)
  const [nom, setNom] = useState('')
  function valider() {
    const propre = nom.trim()
    if (propre !== '' && enregistrer) enregistrer(propre)
    setNommer(false)
    setNom('')
  }
  return (
    <div className="rig-lm-barre">
      <div className="rig-lm-vues" role="group" aria-label={etiquette}>
        {vues.map((v) => (
          <button key={v.cle} type="button" aria-pressed={v.cle === active}
                  className={`rig-lm-vue${v.cle === active ? ' rig-lm-vue--active' : ''}`} onClick={() => choisir(v.cle)}>
            {v.libelle}{v.compte !== undefined && <>{' '}<span className="rig-lm-vue__compte">{v.compte}</span></>}
          </button>
        ))}
        {enregistrer && !nommer && (
          <button type="button" className="rig-lm-vue rig-lm-vue--plus" onClick={() => setNommer(true)}>+ Nouvelle vue</button>
        )}
        {enregistrer && nommer && (
          <input className="rig-lm-vue rig-lm-vue--nom" autoFocus aria-label="Nom de la nouvelle vue" placeholder="Nom de la vue, Entree"
                 value={nom} onChange={(e) => setNom(e.target.value)} onBlur={() => { setNommer(false); setNom('') }}
                 onKeyDown={(e) => {
                   if (e.key === 'Enter') { e.preventDefault(); valider() }
                   else if (e.key === 'Escape') { e.preventDefault(); setNommer(false); setNom('') }
                 }} />
        )}
      </div>
      {children !== undefined && <div className="rig-lm-outils">{children}</div>}
    </div>
  )
}

/** Une vue enregistree d'un ecran : un nom et un jeu complet de ses criteres. */
export interface VueEnregistree<C> {
  cle: string
  libelle: string
  criteres: C
}

/** Ce que rend useVuesDeSession : les vues a montrer, celle des criteres courants, et « + Nouvelle vue ». */
export interface VuesDeSession<C> {
  /** Les vues prereglees de l'ecran, puis celles ajoutees pendant la session. */
  vues: VueEnregistree<C>[]
  /** La cle de la vue dont les criteres sont les criteres courants, ou null (criteres a la main). */
  active: string | null
  /** « + Nouvelle vue » : enregistre les criteres courants sous ce nom. */
  enregistrer: (nom: string) => void
}

// Les vues ajoutees par « + Nouvelle vue », par ecran, pour la SESSION : un tableau de module survit a la fermeture
// de l'onglet, pas au redemarrage (aucun reglage d'ecran ne les porte, decision du lot 1 du plan).
const VuesDeLaSession = new Map<string, readonly VueEnregistree<unknown>[]>()

/**
 * Les vues d'un ecran : ses vues prereglees, puis celles enregistrees pendant la session (cles session-1, session-2…).
 * ecran = le nom qui range ses vues (celui du fichier : « RechAffaires ») ; memes = « ces deux jeux de criteres sont
 * les memes », pour reconnaitre la vue active. Les criteres enregistres sont ceux du rendu courant, tels quels :
 * l'ecran les tient en etat React (jamais modifies en place), ils ne bougent plus ensuite.
 */
export function useVuesDeSession<C>(ecran: string, prereglees: readonly VueEnregistree<C>[], criteres: C,
                                    memes: (a: C, b: C) => boolean): VuesDeSession<C> {
  const lire = () => (VuesDeLaSession.get(ecran) ?? []) as readonly VueEnregistree<C>[]
  const [ajoutees, setAjoutees] = useState(lire)
  const vues = [...prereglees, ...ajoutees]
  return {
    vues,
    active: vues.find((v) => memes(v.criteres, criteres))?.cle ?? null,
    enregistrer(nom: string) {
      const deja = lire()
      const suite = [...deja, { cle: `session-${deja.length + 1}`, libelle: nom, criteres }]
      VuesDeLaSession.set(ecran, suite)
      setAjoutees(suite)
    },
  }
}

/** Pour les tests : repartir d'une session sans vue ajoutee, sur tous les ecrans. */
export function oublierVuesDeSession(): void {
  VuesDeLaSession.clear()
}

export interface ProprietesChoixTri {
  /** Les cles de tri que l'ecran comprend : [libelle, cle]. */
  cles: ReadonlyArray<readonly [libelle: string, cle: string]>
  /** null = l'ordre d'origine de la liste. */
  cleActive: string | null
  ascendant: boolean
  choisir: (cle: string | null) => void
  inverser: () => void
  /** Le libelle de l'ordre d'origine : « Plus recentes ». */
  origine: string
}

/** L'outil « Trier » : la liste des cles de tri de l'ecran, et le sens. */
export function ChoixTri({ cles, cleActive, ascendant, choisir, inverser, origine }: ProprietesChoixTri) {
  return (
    <span className="rig-lm-outil rig-lm-outil--tri">
      <label className="rig-lm-outil__tri">
        <em>Trier</em>
        <select aria-label="Trier" value={cleActive ?? ''} onChange={(e) => choisir(e.target.value === '' ? null : e.target.value)}>
          <option value="">{origine}</option>
          {cles.map(([libelle, cle]) => <option key={cle} value={cle}>{libelle}</option>)}
        </select>
      </label>
      <button type="button" className="rig-lm-outil__sens" disabled={cleActive === null} onClick={inverser}
              aria-label={ascendant ? 'Ordre croissant, inverser' : 'Ordre decroissant, inverser'}
              title={ascendant ? 'Croissant (cliquer pour inverser)' : 'Decroissant (cliquer pour inverser)'}>
        {ascendant ? '▲' : '▼'}
      </button>
    </span>
  )
}

// ---------------------------------------------------------------- export

/** Le texte CSV d'une liste : point-virgule (Excel francais), guillemets doubles echappes. */
export function texteCsv(entetes: readonly string[], lignes: ReadonlyArray<readonly string[]>): string {
  const cellule = (v: string) => (/[";\r\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v)
  return [entetes, ...lignes].map((l) => l.map(cellule).join(';')).join('\r\n')
}

/** Telecharge la liste en CSV (BOM UTF-8 : Excel lit les accents). */
export function exporterCsv(nomFichier: string, entetes: readonly string[], lignes: ReadonlyArray<readonly string[]>): void {
  const blob = new Blob(['﻿' + texteCsv(entetes, lignes)], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const lien = document.createElement('a')
  lien.href = url
  lien.download = nomFichier
  document.body.appendChild(lien)
  lien.click()
  lien.remove()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}

// ---------------------------------------------------------------- cellules du tableau

export interface ProprietesCelluleIdentite {
  /** Le nom, en gras ; il donne l'avatar. */
  nom: string
  /** L'avatar d'initiales (oui par defaut) ; false pour une ligne dont le sujet n'est ni une personne, ni une
   *  societe, ni un dossier (un article de facture) : nom et objet seuls. */
  avatar?: boolean | undefined
  /** En gris apres le nom : « Modification · 004176 ». */
  objet?: ReactNode
  /** Apres l'objet, jamais coupe : un badge. */
  apres?: ReactNode
  /** Le texte entier, au survol de la cellule coupee. */
  titre?: string | undefined
}

/** La cellule « qui » : avatar rond d'initiales (teinte stable tiree du nom), nom en gras, objet en gris. */
export function CelluleIdentite({ nom, objet, apres, titre, avatar = true }: ProprietesCelluleIdentite) {
  return (
    <td title={titre}>
      <div className="rig-lm-identite">
        {avatar && <span className={`rig-lm-avatar rig-lm-avatar--${rangAvatar(nom)}`} aria-hidden="true">{initiales(nom)}</span>}
        <b className="rig-lm-identite__nom">{nom}</b>
        {objet !== undefined && <span className="rig-lm-identite__objet">{objet}</span>}
        {apres}
      </div>
    </td>
  )
}

/**
 * Une nature (ou tout classement) en pastille coloree. Un libelle trop long finit par « … » ; le texte entier se lit
 * au survol par le title de la cellule, que l'ecran pose (il y met souvent plus que la pastille : le service entier
 * derriere son abrege, dans Comptes).
 */
export function Pastille({ teinte, children, titre }: { teinte: Teinte; children: ReactNode; titre?: string | undefined }) {
  return <span className={`rig-lm-pastille rig-lm-t-${teinte}`} title={titre}><i />{children}</span>
}

/** Un etat : point colore et texte. */
export function PointEtat({ teinte, children }: { teinte: Teinte; children: ReactNode }) {
  return <span className={`rig-lm-etat rig-lm-t-${teinte}`}><i />{children}</span>
}

/** Un montant et sa mini-barre, proportionnelle au plus grand de la liste ; zero en gris, sans barre. */
export function MontantBarre({ valeur, max, texte }: { valeur: number; max: number; texte: string }) {
  const part = max > 0 && valeur > 0 ? Math.min(100, Math.round((valeur / max) * 100)) : 0
  return (
    <div className="rig-lm-montant">
      <span className="rig-lm-montant__barre" aria-hidden="true"><b style={{ width: `${part}%` }} /></span>
      <span className={valeur === 0 ? 'rig-lm-montant__texte rig-lm-montant__texte--zero' : 'rig-lm-montant__texte'}>{texte}</span>
    </div>
  )
}

/** La premiere cellule : le chevron qui deplie la ligne. Son clic ne choisit pas la ligne et n'ouvre rien. */
export function CelluleChevron({ ouverte, basculer }: { ouverte: boolean; basculer: () => void }) {
  return (
    <td className="rig-lm-cellule-chevron">
      <button type="button" className="rig-lm-chevron" aria-expanded={ouverte} aria-label={ouverte ? 'Replier la ligne' : 'Deplier la ligne'}
              onClick={(e) => { e.stopPropagation(); basculer() }} onDoubleClick={(e) => e.stopPropagation()}>
        <svg viewBox="0 0 12 12" aria-hidden="true">
          <path d={ouverte ? 'M2.5 4.5 6 8l3.5-3.5' : 'M4.5 2.5 8 6l-3.5 3.5'} />
        </svg>
      </button>
    </td>
  )
}

export interface ChampDetail {
  libelle: string
  valeur: ReactNode
}

export interface ProprietesLigneDetail {
  /** Le nombre de colonnes du tableau : la ligne les couvre toutes. */
  colonnes: number
  champs: ChampDetail[]
  actions?: ActionLigne[] | undefined
  /** Sous les actions : un compte rendu. */
  children?: ReactNode
}

/** La ligne depliee, sous sa rangee : les colonnes secondaires et les actions de la ligne. */
export function LigneDetail({ colonnes, champs, actions, children }: ProprietesLigneDetail) {
  return (
    <tr className="rig-lm-detail">
      <td colSpan={colonnes}>
        <div className="rig-lm-detail__champs">
          {champs.map((c) => (
            <div key={c.libelle} className="rig-lm-champ">
              <span className="rig-lm-champ__libelle">{c.libelle}</span>
              <div className="rig-lm-champ__valeur">{c.valeur === '' ? '—' : c.valeur}</div>
            </div>
          ))}
        </div>
        {actions && actions.length > 0 && (
          <div className="rig-lm-detail__actions">
            {actions.map((a) => (
              <button key={a.libelle} type="button" className={a.fort ? 'rig-lm-outil rig-lm-outil--fort' : 'rig-lm-outil'}
                      disabled={a.desactive} onClick={a.agir}>{a.libelle}</button>
            ))}
          </div>
        )}
        {children}
      </td>
    </tr>
  )
}

/**
 * Le menu « … » d'une ligne : ses actions, dans une liste flottante. La liste est en position FIXE,
 * calculee sur le bouton : la cellule coupe ce qui deborde (overflow:hidden de .rig-table), elle
 * l'aurait coupee. Echap ou un clic dehors la ferme ; Echap ne va pas plus loin (l'ecran reste ouvert).
 * L'ecran pose le bouton dans <td className="rig-lm-cellule-menu">.
 */
export function MenuLigne({ actions, libelle = 'Actions de la ligne' }: { actions: ActionLigne[]; libelle?: string }) {
  const [ouvert, setOuvert] = useState(false)
  const [place, setPlace] = useState<CSSProperties>({})
  const bouton = useRef<HTMLButtonElement>(null)
  const liste = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    if (!ouvert || !bouton.current) return
    const r = bouton.current.getBoundingClientRect()
    setPlace({ top: r.bottom + 2, right: Math.max(4, window.innerWidth - r.right) })
    liste.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
  }, [ouvert])

  useEffect(() => {
    if (!ouvert) return
    const dehors = (e: MouseEvent) => {
      const cible = e.target as Node
      if (!liste.current?.contains(cible) && !bouton.current?.contains(cible)) setOuvert(false)
    }
    document.addEventListener('mousedown', dehors)
    return () => document.removeEventListener('mousedown', dehors)
  }, [ouvert])

  return (
    <>
      <button ref={bouton} type="button" className="rig-lm-menu" aria-label={libelle} aria-haspopup="menu" aria-expanded={ouvert}
              onClick={(e) => { e.stopPropagation(); setOuvert(!ouvert) }} onDoubleClick={(e) => e.stopPropagation()}>
        <svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="3.5" cy="8" r="1.3" /><circle cx="8" cy="8" r="1.3" /><circle cx="12.5" cy="8" r="1.3" /></svg>
      </button>
      {ouvert && (
        <div ref={liste} className="rig-lm-menu__liste" role="menu" style={place}
             onClick={(e) => e.stopPropagation()} onDoubleClick={(e) => e.stopPropagation()}
             onKeyDown={(e) => { if (e.key === 'Escape') { e.preventDefault(); setOuvert(false); bouton.current?.focus() } }}>
          {actions.map((a) => (
            <button key={a.libelle} type="button" role="menuitem" className="rig-lm-menu__action" disabled={a.desactive}
                    onClick={() => { setOuvert(false); a.agir() }}>{a.libelle}</button>
          ))}
        </div>
      )}
    </>
  )
}

/** Le pied de liste : les totaux (enfants), puis l'aide a droite. */
export function PiedListe({ aide, children }: { aide?: ReactNode; children?: ReactNode }) {
  return (
    <div className="rig-lm-pied">
      {children}
      {aide !== undefined && <span className="rig-lm-pied__aide">{aide}</span>}
    </div>
  )
}
