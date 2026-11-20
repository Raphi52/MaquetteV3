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
// fix-ok: a 1516 px, 004176 avait ses cinq verbes en menus, deux clics pour le Kbis ou les
// factures (refus du chef, lot 6, 20/11/2026). Entre le menu et les boutons, une forme de
// plus : l'action PRINCIPALE du verbe (le Kbis, les factures, le courrier ; un verbe d'une
// seule action, elle) en bouton, le reste sous un chevron. Le C# la nomme
// (Metier/ActionsDossier.cs : Principale), formeDesVerbes la choisit a son cout.
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

/** La forme d'un verbe dans la barre, de la plus compacte a la plus large : un menu, son
 *  action principale en bouton et le reste sous un chevron, ou tous ses boutons. */
export type FormeVerbe = 'menu' | 'principale' | 'boutons'

/** Un verbe mesure : sa largeur en boutons, repliee en menu, et dans sa forme principale
 *  s'il en a une. */
export interface LargeursVerbe { deplie: number, replie: number, principale?: number }

/** La forme de chaque verbe pour une barre de `disponible` pixels au plus (`ecart` entre
 *  deux verbes, `cadre` = bords et marges de la barre). Tout part en menus ; puis, marche
 *  par marche, le verbe dont la forme suivante coute le moins a la barre (a egalite, dans
 *  l'ordre de la barre) prend cette forme -- sa principale avant ses boutons --, tant qu'elle
 *  tient. Une marche qui raccourcit la barre est toujours prise. L'ordre des marches ne
 *  depend pas de la largeur : en elargissant la fenetre, un verbe ne revient jamais a une
 *  forme plus compacte. Aucun verbe ne change de place, seulement de forme. */
export function formeDesVerbes(verbes: LargeursVerbe[], disponible: number, ecart: number, cadre: number): FormeVerbe[] {
  const marches = verbes.map((v): [FormeVerbe, number][] => [
    ['menu', v.replie],
    ...(v.principale === undefined ? [] : [['principale', v.principale] as [FormeVerbe, number]]),
    ['boutons', v.deplie],
  ])
  const rang = verbes.map(() => 0)
  let total = cadre + ecart * Math.max(0, verbes.length - 1) + verbes.reduce((s, v) => s + v.replie, 0)
  for (;;) {
    let choix = -1
    let cout = Infinity
    marches.forEach((m, i) => {
      const ici = m[rang[i]!]!
      const suivante = m[rang[i]! + 1]
      if (suivante && suivante[1] - ici[1] < cout) { choix = i; cout = suivante[1] - ici[1] }
    })
    // La moins chere ne tient pas : aucune autre ne tiendrait.
    if (choix < 0 || (cout > 0 && total + cout > disponible)) break
    rang[choix]!++
    total += cout
  }
  return rang.map((r, i) => marches[i]![r]![0])
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
function mesurer(barre: HTMLElement, mesure: HTMLElement, verbes: VerbeActionsVue[]): FormeVerbe[] | null {
  const entete = barre.parentElement
  if (!entete) return null
  const se = getComputedStyle(entete)
  const contenu = largeur(entete) - cadreDe(entete)
  if (contenu <= 0) return null
  // Les voisins dans le flux : leur largeur est fixe, sauf l'identite, qui cede la
  // place a la barre jusqu'a sa reserve. Aucun ne depend de la forme de la barre, ni
  // les puces de l'identite : pas de boucle. C'est la feuille qui le garantit
  // (rig-accueil.css : « .rig-vged__entete > :not(.rig-vged__identite) », flex-shrink 0) ;
  // sans elle, les boutons retour et fermer se tassaient sous une barre depliee et la
  // barre basculait d'une forme a l'autre (004151 a 1516 px, 20/11/2026).
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
  // La couche de mesure porte chaque forme de chaque verbe, nommee (data-verbe, data-forme).
  const formes = [...mesure.children] as HTMLElement[]
  const largeurDe = (verbe: string, forme: FormeVerbe) => {
    const e = formes.find((x) => x.dataset.verbe === verbe && x.dataset.forme === forme)
    return e ? largeur(e) : 0
  }
  const mesures: LargeursVerbe[] = verbes.map((v) => ({
    deplie: largeurDe(v.verbe, 'boutons'), replie: largeurDe(v.verbe, 'menu'),
    ...(v.principale !== null ? { principale: largeurDe(v.verbe, 'principale') } : {}),
  }))
  if (mesures.some((m) => m.deplie <= 0 || m.replie <= 0 || (m.principale !== undefined && m.principale <= 0))) return null
  const sb = getComputedStyle(barre)
  const cadre = cadreDe(barre)
  const ecart = px(sb.columnGap)
  const toutReplie = cadre + ecart * Math.max(0, verbes.length - 1) + mesures.reduce((s, v) => s + v.replie, 0)
  const reserve = identite ? reserveIdentite(identite, libre - toutReplie) : 0
  return formeDesVerbes(mesures, libre - reserve, ecart, cadre)
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
  // La forme de chaque verbe ; d'entree, tout en menus.
  const [formes, poserFormes] = useState<FormeVerbe[]>([])

  // Mesure avant l'affichage (pas de saut visible), puis a chaque changement de
  // largeur de l'entete ou des textes mesures (police chargee, autre affaire).
  useLayoutEffect(() => {
    const b = barre.current
    const m = mesure.current
    if (!b || !m) return
    const ajuster = () => {
      const forme = mesurer(b, m, verbes) ?? verbes.map((): FormeVerbe => 'menu')
      poserFormes((avant) => (avant.length === forme.length && avant.every((x, i) => x === forme[i]) ? avant : forme))
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

  // `mesure` : le double inerte d'une forme, dans la couche de mesure, qui la nomme.
  const nommer = (v: VerbeActionsVue, forme: FormeVerbe, mesure: boolean) =>
    mesure ? { 'data-verbe': v.verbe, 'data-forme': forme } : { role: 'group', 'aria-label': v.verbe }
  const bouton = (a: ActionDossierVue, mesure: boolean) => (
    <button key={a.code} type="button" title={infobulle(a)} tabIndex={mesure ? -1 : undefined}
            className={'rig-vged__agir-bouton' + (a.ecran ? ' rig-vged__agir-bouton--ecran' : '')}
            onClick={mesure ? undefined : () => jouer(a, null)}>
      {a.libelle}
      {a.ecran && <VersEcran />}
    </button>
  )
  // Le groupe de BOUTONS d'un verbe.
  const groupe = (v: VerbeActionsVue, mesure = false) => (
    <div key={v.verbe + (mesure ? ' boutons' : '')} className="rig-vged__agir-groupe" {...nommer(v, 'boutons', mesure)}>
      <span className="rig-vged__agir-legende">{v.verbe}</span>
      {v.actions.map((a) => bouton(a, mesure))}
    </div>
  )
  // Le MENU d'un verbe : son titre (le verbe, ou le seul chevron a cote de l'action
  // principale), et toutes ses actions avec leur lettre.
  const menu = (v: VerbeActionsVue, i: number, titre: 'verbe' | 'chevron') => (
    <details key={v.verbe} onToggle={(e) => ouvert(e.currentTarget)} onKeyDown={(e) => touche(e, v)}
             className={'rig-vged__agir-verbe' + (i >= verbes.length - 2 ? ' rig-vged__agir-verbe--droite' : '')}>
      <summary className={'rig-vged__agir-titre' + (titre === 'chevron' ? ' rig-vged__agir-titre--chevron' : '')}
               aria-label={titre === 'chevron' ? v.verbe + ' : toutes les actions' : undefined}
               title={(titre === 'chevron' ? v.verbe + ' : ' : '') + v.actions.map((a) => `${a.libelle} (${a.touche})`).join(' · ')}>
        {titre === 'verbe' && v.verbe}<Chevron />
      </summary>
      <div className="rig-vged__agir-menu" role="menu" aria-label={v.verbe}>
        {titre === 'chevron' && <div className="rig-vged__agir-entete" aria-hidden="true">{v.verbe}</div>}
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
  )
  // L'action PRINCIPALE en bouton, et le reste du verbe sous un chevron (aucun chevron pour un
  // verbe d'une seule action : un menu d'un item coute un clic pour rien).
  const principale = (v: VerbeActionsVue, i: number, mesure = false) => {
    const a = v.actions.find((x) => x.code === v.principale)
    if (!a) return mesure ? null : menu(v, i, 'verbe')
    return (
      <div key={v.verbe + (mesure ? ' principale' : '')} className="rig-vged__agir-principale" {...nommer(v, 'principale', mesure)}>
        {bouton(a, mesure)}
        {v.actions.length > 1 && (mesure
          ? <span className="rig-vged__agir-titre rig-vged__agir-titre--chevron"><Chevron /></span>
          : menu(v, i, 'chevron'))}
      </div>
    )
  }

  return (
    <div className="rig-vged__agir" role="toolbar" aria-label="Actions sur le dossier" ref={barre}>
      {verbes.map((v, i) => formes[i] === 'boutons' ? groupe(v)
        : formes[i] === 'principale' ? principale(v, i)
        : menu(v, i, 'verbe'))}
      {/* La couche de MESURE : chaque forme de chaque verbe, invisible, hors du flux et hors
          de l'arbre d'accessibilite ; elle dit ce que chaque forme demande. */}
      <div className="rig-vged__agir-mesure" aria-hidden="true" inert>
        <div ref={mesure}>
          {verbes.flatMap((v, i) => [
            groupe(v, true),
            principale(v, i, true),
            <span key={v.verbe + ' menu'} className="rig-vged__agir-titre" data-verbe={v.verbe} data-forme="menu">{v.verbe}<Chevron /></span>,
          ])}
        </div>
      </div>
    </div>
  )
}
