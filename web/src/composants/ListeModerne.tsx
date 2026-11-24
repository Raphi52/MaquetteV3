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

import { useContext, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react'
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

/**
 * Le cadre d'une liste moderne : la colonne des resultats en carte de surface (.rig-lm), a cote du panneau
 * de criteres. C'est aussi le composant qui donne son nom au fichier (regle de web/src : registre iles.ts).
 */
export function ListeModerne({ etiquette, children }: { etiquette?: string | undefined; children?: ReactNode }) {
  return <section className="rig-resultats-proc rig-lm" aria-label={etiquette}>{children}</section>
}

// ---------------------------------------------------------------- outils de texte

/** Un montant en euros, a la francaise : « 1 379,30 € ». */
export function montant(valeur: number): string {
  return `${valeur.toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} €`
}

// Ni une forme juridique ni un mot de liaison ne donnent une initiale : « SCI DU PARC » -> « P »,
// « BATIPRO SARL » -> « B », « COMPTOIR DU NORD » -> « CN » (les avatars de la maquette).
const MotsSansInitiale = new Set(['SA', 'SAS', 'SASU', 'SARL', 'EURL', 'SCI', 'SNC', 'SCP', 'SCM', 'SELARL', 'SELAS', 'EI', 'EIRL',
  'DU', 'DE', 'DES', 'LA', 'LE', 'LES', 'ET', 'D', 'L', 'AU', 'AUX', '&'])

/** Les initiales de l'avatar : la premiere lettre des deux premiers mots qui comptent. */
export function initiales(nom: string): string {
  const mots = nom.toUpperCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
    .split(/[^A-Z0-9&]+/).filter((m) => m.length > 0)
  const utiles = mots.filter((m) => !MotsSansInitiale.has(m))
  return (utiles.length > 0 ? utiles : mots).slice(0, 2).map((m) => m[0]).join('') || '?'
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
 * volerait sinon la touche a l'ecran visible.
 */
export function EnteteListe({ titre, synthese, detail, filtre }: ProprietesEnteteListe) {
  const visible = useContext(EcranVisible)
  const champ = useRef<HTMLInputElement>(null)
  const avecFiltre = filtre !== undefined
  useEffect(() => {
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
export function VuesListe({ vues, active, choisir, enregistrer, children }: ProprietesVuesListe) {
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
      <div className="rig-lm-vues" role="group" aria-label="Vues">
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
  /** En gris apres le nom : « Modification · 004176 ». */
  objet?: ReactNode
  /** Apres l'objet, jamais coupe : un badge. */
  apres?: ReactNode
  /** Le texte entier, au survol de la cellule coupee. */
  titre?: string | undefined
}

/** La cellule « qui » : avatar rond d'initiales (teinte stable tiree du nom), nom en gras, objet en gris. */
export function CelluleIdentite({ nom, objet, apres, titre }: ProprietesCelluleIdentite) {
  return (
    <td title={titre}>
      <div className="rig-lm-identite">
        <span className={`rig-lm-avatar rig-lm-avatar--${rangAvatar(nom)}`} aria-hidden="true">{initiales(nom)}</span>
        <b className="rig-lm-identite__nom">{nom}</b>
        {objet !== undefined && <span className="rig-lm-identite__objet">{objet}</span>}
        {apres}
      </div>
    </td>
  )
}

/** Une nature (ou tout classement) en pastille coloree. */
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
