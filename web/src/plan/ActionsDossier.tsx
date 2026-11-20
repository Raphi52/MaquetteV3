// LA BARRE D'ACTIONS DU DOSSIER (plan « Vue du dossier = coeur des actions »,
// lot 1, 20/11/2026), dans l'entete de la visionneuse, entre l'identite et
// « Document ». La page devient le point de depart de ce qu'un agent fait sur
// l'affaire : avant, aucune action metier n'en partait.
//
// Des BOUTONS groupes par VERBE (Delivrer, Enregistrer, Facturer, Ecrire,
// Suivre) ; un verbe qui ne tient plus dans la largeur se replie en MENU
// DEROULANT, comme un groupe du ruban d'Office. La largeur se mesure (couche de
// mesure invisible + ResizeObserver sur l'entete) : les verbes se deplient du
// plus court au plus long tant qu'ils tiennent (formeDesVerbes), sans faire passer
// les puces de l'identite sur une ligne de plus (reserveIdentite). Sans mesure
// possible (vue cachee, tests), tout reste en menus : la forme la plus compacte.
// fix-ok: la premiere version rendait TOUJOURS un <details> par verbe, quelle que
// soit la largeur (refus du controle final, 20/11/2026 : « boutons groupes, menus
// seulement si la place manque ») ; la forme suit desormais la largeur mesuree.
// Le premier tri (dans l'ordre de la barre, en sautant un verbe trop large)
// repliait « Facturer » en passant de 1500 a 1920 px pour deplier « Delivrer »
// (captures du 20/11/2026) : d'ou le tri par cout, monotone en largeur.
// fix-ok: l'identite gardait une reserve FIXE de 480 px, aveugle a ses puces : a
// 1500 px, deplier « Facturer » la ramenait de ~830 a ~640 px et les puces de 004176
// passaient de 2 a 3 lignes (refus du controle final, 20/11/2026). La reserve se
// mesure desormais sur les puces : pas une ligne de plus que barre toute repliee.
//
// Le catalogue et ce qui est disponible sur l'affaire sont decides par le C#
// (Metier/ActionsDossier.cs, Pont/PlanTravailActions.cs) ; la page remonte le
// code de l'action (geste « actionDossier »), sauf pour une COPIE, qu'elle joue
// elle-meme : le presse-papiers est le sien. Menu ouvert, la lettre d'une action
// la joue ; un clic ailleurs ou Echap le referme. Menu ferme, la meme lettre la
// joue depuis toute la page (lot 5 : AideClavier.tsx, « ? » pour la liste) : chaque
// infobulle la donne.

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import type { ActionDossierVue, GestePlan, VerbeActionsVue } from './types'

/** Met le texte dans le presse-papiers ; rend faux si la page n'y a pas acces. */
export async function copierTexte(texte: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(texte)
    return true
  } catch {
    return false
  }
}

/** Copie avec un AVIS bref (« SIREN copie ») : l'avis courant, et la fonction qui copie. */
export function useCopie(duree = 1400): [string | null, (texte: string, quoi: string) => void] {
  const [avis, poserAvis] = useState<string | null>(null)
  const minuteur = useRef(0)
  useEffect(() => () => window.clearTimeout(minuteur.current), [])
  const copier = useCallback((texte: string, quoi: string) => {
    void copierTexte(texte).then((ok) => {
      window.clearTimeout(minuteur.current)
      // Un echec se dit : annoncer « copie » sur un presse-papiers vide tromperait.
      poserAvis(ok ? quoi + ' copie' : 'Copie impossible')
      minuteur.current = window.setTimeout(() => poserAvis(null), duree)
    })
  }, [duree])
  return [avis, copier]
}

/** « Copier le SIREN » -> « SIREN » : ce que l'avis annonce comme copie. */
function objetCopie(libelle: string): string {
  return libelle.replace(/^Copier (le |la |l')?/, '')
}

/** Ce que joue une action, d'un clic, de sa lettre ou depuis la palette Ctrl K : une COPIE
 *  dans la page (le presse-papiers est le sien ; `copier` recoit l'objet copie, « SIREN »),
 *  sinon le geste « actionDossier ». */
export function jouerAction(a: { code: string, libelle: string, copie: string | null }, plan: GestePlan,
                            copier: (texte: string, quoi: string) => void): void {
  if (a.copie !== null) copier(a.copie, objetCopie(a.libelle))
  else plan('actionDossier', a.code)
}

/** L'infobulle d'une action : ce qu'elle fait, et la lettre qui la joue. */
export function infobulle(a: ActionDossierVue): string {
  return `${a.detail} — touche ${a.touche}`
}

/** Le plancher de largeur de l'identite de l'affaire (n°, objet, puces) quand la barre
 *  deplie ses verbes : le titre y tient. La reserve reelle se mesure sur les puces
 *  (reserveIdentite) et ne descend jamais sous ce plancher. */
export const IDENTITE_MIN = 480

/** Le nombre de lignes que prennent des puces de ces largeurs dans `largeur` pixels,
 *  `ecart` entre deux puces : le retour a la ligne de flex-wrap (une puce passe a la
 *  ligne suivante quand elle ne tient plus a la suite ; seule sur sa ligne, elle reste). */
export function lignesDesPuces(puces: number[], ecart: number, largeur: number): number {
  let lignes = 0
  let fin = 0
  for (const p of puces) {
    // Un centieme de pixel de jeu : les largeurs mesurees sont fractionnaires.
    if (lignes === 0 || fin + ecart + p > largeur + 0.01) { lignes++; fin = p }
    else fin += ecart + p
  }
  return lignes
}

/** La largeur la plus etroite (au pixel) ou ces puces tiennent sur `lignes` lignes au
 *  plus, cherchee entre la plus large puce (plus etroit, elle serait coupee) et
 *  `plafond`. Plus large, jamais plus de lignes : dichotomie. */
export function largeurDesPuces(puces: number[], ecart: number, lignes: number, plafond: number): number {
  let haut = Math.max(0, Math.ceil(plafond))
  let bas = Math.min(haut, Math.ceil(Math.max(0, ...puces)))
  while (bas < haut) {
    const milieu = Math.floor((bas + haut) / 2)
    if (lignesDesPuces(puces, ecart, milieu) <= lignes) haut = milieu
    else bas = milieu + 1
  }
  return haut
}

/** Un verbe mesure : sa largeur en boutons, et repliee en menu. */
export interface LargeursVerbe { deplie: number, replie: number }

/** Quels verbes s'affichent en BOUTONS (vrai) plutot qu'en menu, pour une barre de
 *  `disponible` pixels au plus (`ecart` entre deux verbes, `cadre` = bords et marges
 *  de la barre). Les verbes se deplient du moins couteux au plus couteux (ce que le
 *  deplier ajoute a la barre ; a egalite, dans l'ordre de la barre), tant qu'elle
 *  tient : le plus de boutons possible, et en elargissant la fenetre un verbe
 *  deplie ne se replie jamais. Aucun verbe ne change de place, seulement de forme. */
export function formeDesVerbes(verbes: LargeursVerbe[], disponible: number, ecart: number, cadre: number): boolean[] {
  const deplie = verbes.map(() => false)
  let total = cadre + ecart * Math.max(0, verbes.length - 1) + verbes.reduce((s, v) => s + v.replie, 0)
  const ordre = verbes.map((v, i) => ({ i, cout: v.deplie - v.replie })).sort((a, b) => a.cout - b.cout || a.i - b.i)
  for (const { i, cout } of ordre) {
    // Trie par cout : si celui-ci ne tient pas, aucun des suivants ne tiendra.
    if (total + cout > disponible) break
    deplie[i] = true
    total += cout
  }
  return deplie
}

const px = (v: string) => parseFloat(v) || 0
const largeur = (e: Element) => e.getBoundingClientRect().width
/** Bords et marges interieures d'un element, a gauche et a droite. */
function cadreDe(e: Element): number {
  const s = getComputedStyle(e)
  return px(s.paddingLeft) + px(s.paddingRight) + px(s.borderLeftWidth) + px(s.borderRightWidth)
}

/** La largeur que l'identite garde quand la barre deplie des verbes : celle ou ses
 *  puces ne prennent pas PLUS de lignes que barre toute repliee (`repliee` = la
 *  largeur de l'identite dans ce cas), et jamais moins que IDENTITE_MIN. L'avis
 *  « SIREN copie » ne compte pas : il passe, la forme de la barre ne doit pas suivre. */
export function reserveIdentite(identite: Element, repliee: number): number {
  const puces = identite.querySelector('.rig-vged__puces')
  if (!puces) return IDENTITE_MIN
  const largeurs = [...puces.children].filter((e) => !e.classList.contains('rig-vged__copie')).map(largeur)
  const ecart = px(getComputedStyle(puces).columnGap)
  const cadre = cadreDe(identite)
  const lignes = lignesDesPuces(largeurs, ecart, repliee - cadre)
  return Math.max(IDENTITE_MIN, cadre + largeurDesPuces(largeurs, ecart, lignes, repliee - cadre))
}

/** Mesure la place de la barre dans son entete et rend la forme de chaque verbe ;
 *  null quand rien n'est mesurable (vue cachee, pas de mise en page). */
function mesurer(barre: HTMLElement, mesure: HTMLElement, nombre: number): boolean[] | null {
  const entete = barre.parentElement
  if (!entete) return null
  const se = getComputedStyle(entete)
  const contenu = largeur(entete) - cadreDe(entete)
  if (contenu <= 0) return null
  // Les voisins dans le flux : leur largeur est fixe, sauf l'identite, qui cede la
  // place a la barre jusqu'a sa reserve. Aucun ne depend de la forme de la barre, ni
  // les puces de l'identite : pas de boucle.
  let pris = 0
  let enFlux = 0
  let identite: Element | null = null
  for (const e of entete.children) {
    const s = getComputedStyle(e)
    if (s.display === 'none' || s.position === 'absolute' || s.position === 'fixed') continue
    enFlux++
    if (e === barre) continue
    if (e.classList.contains('rig-vged__identite')) identite = e
    else pris += largeur(e)
    pris += px(s.marginLeft) + px(s.marginRight)
  }
  // Ce que se partagent l'identite et la barre.
  const libre = contenu - pris - px(se.columnGap) * Math.max(0, enFlux - 1)
  // La couche de mesure porte, dans l'ordre des verbes, la forme depliee puis la repliee.
  const mesures = [...mesure.children].map(largeur)
  if (mesures.length !== 2 * nombre || mesures.some((l) => l <= 0)) return null
  const sb = getComputedStyle(barre)
  const cadre = cadreDe(barre)
  const ecart = px(sb.columnGap)
  const verbes = Array.from({ length: nombre }, (_, i) => ({ deplie: mesures[2 * i]!, replie: mesures[2 * i + 1]! }))
  const toutReplie = cadre + ecart * Math.max(0, nombre - 1) + verbes.reduce((s, v) => s + v.replie, 0)
  const reserve = identite ? reserveIdentite(identite, libre - toutReplie) : 0
  return formeDesVerbes(verbes, libre - reserve, ecart, cadre)
}

const Chevron = () => <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 9l6 6 6-6"></path></svg>
/** Un ecran s'ouvre dans un onglet : la fleche le dit avant le clic. */
const VersEcran = () => <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 17 17 7"></path><path d="M8 7h9v9"></path></svg>

export interface ProprietesActionsDossier {
  verbes: VerbeActionsVue[]
  plan: GestePlan
  copier: (texte: string, quoi: string) => void
}

export function ActionsDossier({ verbes, plan, copier }: ProprietesActionsDossier) {
  const barre = useRef<HTMLDivElement>(null)
  const mesure = useRef<HTMLDivElement>(null)
  // La forme de chaque verbe (vrai = boutons) ; d'entree, tout en menus.
  const [deplies, poserDeplies] = useState<boolean[]>([])

  // Mesure avant l'affichage (pas de saut visible), puis a chaque changement de
  // largeur de l'entete ou des textes mesures (police chargee, autre affaire).
  useLayoutEffect(() => {
    const b = barre.current
    const m = mesure.current
    if (!b || !m) return
    const ajuster = () => {
      const forme = mesurer(b, m, verbes.length) ?? verbes.map(() => false)
      poserDeplies((avant) => (avant.length === forme.length && avant.every((x, i) => x === forme[i]) ? avant : forme))
    }
    ajuster()
    if (typeof ResizeObserver === 'undefined') return
    const observateur = new ResizeObserver(ajuster)
    if (b.parentElement) observateur.observe(b.parentElement)
    observateur.observe(m)
    return () => observateur.disconnect()
  }, [verbes])

  // Un clic hors du menu ouvert le referme (comme la liste des points a corriger).
  useEffect(() => {
    const fermer = (e: PointerEvent) => {
      barre.current?.querySelectorAll<HTMLDetailsElement>('details[open]').forEach((menu) => {
        if (!menu.contains(e.target as Node)) menu.open = false
      })
    }
    document.addEventListener('pointerdown', fermer)
    return () => document.removeEventListener('pointerdown', fermer)
  }, [])
  if (verbes.length === 0) return null

  const jouer = (a: ActionDossierVue, menu: HTMLDetailsElement | null) => {
    if (menu) menu.open = false
    jouerAction(a, plan, copier)
  }
  // Un menu qui s'ouvre referme les autres.
  const ouvert = (menu: HTMLDetailsElement) => {
    if (!menu.open) return
    barre.current?.querySelectorAll<HTMLDetailsElement>('details[open]').forEach((autre) => {
      if (autre !== menu) autre.open = false
    })
  }
  const touche = (e: KeyboardEvent<HTMLDetailsElement>, v: VerbeActionsVue) => {
    const menu = e.currentTarget
    if (!menu.open || e.ctrlKey || e.altKey || e.metaKey) return
    if (e.key === 'Escape') {
      // Echap referme le menu SEUL : sans cela, il remonterait au document et
      // fermerait l'affaire (web/src/plan/clavier.ts).
      e.stopPropagation()
      menu.open = false
      return
    }
    const a = v.actions.find((x) => x.touche.toLowerCase() === e.key.toLowerCase())
    if (!a) return
    e.preventDefault()
    jouer(a, menu)
  }

  // Le groupe de BOUTONS d'un verbe ; `mesure` = son double inerte de la couche de mesure.
  const groupe = (v: VerbeActionsVue, mesure = false) => (
    <div key={v.verbe} className="rig-vged__agir-groupe" role={mesure ? undefined : 'group'} aria-label={mesure ? undefined : v.verbe}>
      <span className="rig-vged__agir-legende">{v.verbe}</span>
      {v.actions.map((a) => (
        <button key={a.code} type="button" title={infobulle(a)} tabIndex={mesure ? -1 : undefined}
                className={'rig-vged__agir-bouton' + (a.ecran ? ' rig-vged__agir-bouton--ecran' : '')}
                onClick={mesure ? undefined : () => jouer(a, null)}>
          {a.libelle}
          {a.ecran && <VersEcran />}
        </button>
      ))}
    </div>
  )

  return (
    <div className="rig-vged__agir" role="toolbar" aria-label="Actions sur le dossier" ref={barre}>
      {verbes.map((v, i) => deplies[i] ? groupe(v) : (
        <details key={v.verbe} onToggle={(e) => ouvert(e.currentTarget)} onKeyDown={(e) => touche(e, v)}
                 className={'rig-vged__agir-verbe' + (i >= verbes.length - 2 ? ' rig-vged__agir-verbe--droite' : '')}>
          <summary className="rig-vged__agir-titre" title={v.actions.map((a) => `${a.libelle} (${a.touche})`).join(' · ')}>
            {v.verbe}<Chevron />
          </summary>
          <div className="rig-vged__agir-menu" role="menu" aria-label={v.verbe}>
            {v.actions.map((a) => (
              <button key={a.code} type="button" role="menuitem" title={infobulle(a)}
                      className={'rig-vged__agir-action' + (a.ecran ? ' rig-vged__agir-action--ecran' : '')}
                      onClick={(e) => jouer(a, e.currentTarget.closest('details'))}>
                <span className="rig-vged__agir-libelle">{a.libelle}</span>
                {a.ecran && <VersEcran />}
                {/* L'espace separe la lettre du libelle pour un lecteur d'ecran (« Factures F ») ;
                    dans la ligne flex, il ne se dessine pas. */}
                {' '}<kbd className="rig-vged__agir-touche">{a.touche}</kbd>
              </button>
            ))}
          </div>
        </details>
      ))}
      {/* La couche de MESURE : chaque verbe deplie puis replie, invisible, hors du flux
          et hors de l'arbre d'accessibilite ; elle dit ce que chaque forme demande. */}
      <div className="rig-vged__agir-mesure" aria-hidden="true" inert>
        <div ref={mesure}>
          {verbes.flatMap((v) => [
            groupe(v, true),
            <span key={v.verbe + ' replie'} className="rig-vged__agir-titre">{v.verbe}<Chevron /></span>,
          ])}
        </div>
      </div>
    </div>
  )
}
