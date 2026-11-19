// LE CLASSEUR DE L'ARBRE du dossier (/draft du 19/11/2026, conv-2009 : « Classeur »
// > « Deux classeurs » > « Cote Judiciaire » > proposition 5 « Chronologie »,
// choisie). Il remplace l'arbre en retrait (ArbreGedNoeud.tsx, retire) :
//   - une BASCULE par domaine (« Dossier RCS 21 | Judiciaire 23 ») : un seul
//     classeur a la fois, celui de la piece ouverte par defaut ;
//   - un RAPPEL or quand la piece ouverte est dans l'autre classeur ;
//   - une carte pour les pieces posees directement sous le domaine (hors rubrique) ;
//   - une CARTE par rubrique (filet de sa couleur en haut, nombre de pieces et date
//     de la plus recente) ; une seule ouverte par domaine (geste « arbreRubrique »,
//     regle dans Pont/PlanTravailSuivi.cs, BasculerRubrique) ;
//   - dans la carte ouverte, « Par dossier » (les sous-dossiers en intertitres,
//     recursion de Contenu, chacun se replie d'un clic) ou « Par date » (une frise
//     par annee, la plus recente en tete, les decisions en or). Le classement est
//     retenu par le pont, d'une affaire a l'autre et au redemarrage (geste
//     « arbreTri », AccueilEtat.TriClasseur) ; les sous-dossiers replies aussi, par
//     affaire (geste « arbreReplierDossier », PersistanceGed : ArbreGedEnregistre.Replies).
//
// Les donnees arrivent du pont (Pont/ArbreGedNoeud.cs, LigneArbreVue) : branches
// ouvertes, filtre, chemin, renommage, droits du login, classement, replis. Ici,
// seulement la mise en page et un choix d'affichage tenu par l'ecran : le classeur
// montre.

import { Fragment, useState } from 'react'
import type { CSSProperties, MouseEvent } from 'react'
import { Saisie } from '../session'
import { IconeGed } from './IconeGed'

/** Une ligne calculee par Pont/ArbreGedNoeud.cs (LigneArbreVue). */
export interface LigneArbreVue {
  id: string
  niveau: number
  texte: string
  icone: string
  actif: boolean
  chemin: boolean
  depliable: boolean
  ouvert: boolean
  renomme: boolean
  compte: number | null
  enfants: LigneArbreVue[]
  vide: boolean
  /** La teinte de sa rubrique (1 a 8), 0 hors rubrique. */
  groupe: number
  /** Le niveau de la tete de sa rubrique. */
  niveauGroupe: number
  /** Une piece : sa date de depot (jj/mm/aaaa), vide sinon. */
  date: string
  /** Une piece : son nombre de pages, 0 sinon. */
  pages: number
  /** Un dossier : la date de sa piece la plus recente, vide sinon. */
  derniere: string
  /** Un sous-dossier replie en « Par dossier » (ses enfants sont la quand meme, pour « Par date »). */
  replie: boolean
}

/** Le clic sur les trois points : la position de la souris, pour placer le menu. */
export interface SourisArbre {
  id: string
  clientX: number
  clientY: number
  screenX: number
  screenY: number
  offsetX: number
  offsetY: number
  pageX: number
  pageY: number
  button: number
  buttons: number
  detail: number
  ctrlKey: boolean
  shiftKey: boolean
  altKey: boolean
  metaKey: boolean
}

export type TriClasseur = 'dossier' | 'date'

export interface ProprietesClasseurArbre {
  /** La racine de l'arbre (l'affaire) ; ses enfants sont les domaines. */
  arbre: LigneArbreVue
  /** De l'affaire a l'element choisi (DossierVue.chemin) : [numero, domaine, ..., element]. */
  chemin: string[]
  /** Le texte du filtre : pendant une recherche, la bascule compte les pieces trouvees. */
  filtre: string
  /** Le classement de la carte ouverte (DossierVue.triClasseur, retenu par le pont). */
  tri: TriClasseur
  /** « Par dossier » / « Par date » clique : le pont le retient. */
  trier: (tri: TriClasseur) => void
  /** Clic sur une piece ou une fiche : la coquille la choisit et l'ouvre. */
  activer: (id: string) => void
  /** Clic sur la carte d'une rubrique : elle s'ouvre (et ferme les autres) ou se ferme. */
  rubrique: (id: string) => void
  /** Clic sur l'intertitre d'un sous-dossier : il se replie ou se rouvre, et le pont le retient. */
  replier: (id: string) => void
  /** Renommage valide (texte vide = renommage abandonne). */
  renommer: (renommage: { id: string, texte: string }) => void
  /** Bouton « trois points » : ouvre le menu des operations. */
  menuContextuel: (souris: SourisArbre) => void
}

function souris(id: string, e: MouseEvent<HTMLButtonElement>): SourisArbre {
  return {
    id,
    clientX: e.clientX, clientY: e.clientY,
    screenX: e.screenX, screenY: e.screenY,
    offsetX: e.nativeEvent.offsetX, offsetY: e.nativeEvent.offsetY,
    pageX: e.pageX, pageY: e.pageY,
    button: e.button, buttons: e.buttons, detail: e.detail,
    ctrlKey: e.ctrlKey, shiftKey: e.shiftKey, altKey: e.altKey, metaKey: e.metaKey,
  }
}

export const estDossier = (l: LigneArbreVue) => l.icone === 'dossier'
/** La tete d'une rubrique : le pont lui donne sa teinte (dossier pose sous un domaine). */
export const estRubrique = (l: LigneArbreVue) => l.groupe > 0 && l.niveau === l.niveauGroupe
const pieces = (n: number) => n + (n > 1 ? ' pieces' : ' piece')
const teinte = (groupe: number) => ({ '--g': `var(--rig-groupe-${groupe})` }) as CSSProperties

/** Les pieces sous cette ligne (pendant une recherche : celles que le filtre garde). */
export function compterPieces(l: LigneArbreVue): number {
  return l.enfants.reduce((n, e) => n + (estDossier(e) ? compterPieces(e) : e.icone === 'note' ? 0 : 1), 0)
}

/** Une piece de la frise : la ligne, et le dossier qui la range. */
export interface PieceDatee { ligne: LigneArbreVue, dossier: string }

/** « 14/02/2022 » -> « 2022-02-14 », pour trier. */
const cleDate = (d: string) => d.slice(6) + '-' + d.slice(3, 5) + '-' + d.slice(0, 2)

/**
 * Les pieces d'une rubrique pour la vue « Par date » : toutes, sous-dossiers
 * compris, de la plus RECENTE a la plus ancienne (demande du 19/11/2026 : la
 * derniere piece deposee d'abord) ; a date egale, l'ordre de l'arbre. Les fiches
 * et les pieces sans date a la fin.
 */
export function frise(rubrique: LigneArbreVue): { datees: PieceDatee[], sansDate: PieceDatee[] } {
  const toutes: PieceDatee[] = []
  const parcourir = (dossier: LigneArbreVue) => {
    for (const e of dossier.enfants) {
      if (estDossier(e)) parcourir(e)
      else toutes.push({ ligne: e, dossier: dossier.texte })
    }
  }
  parcourir(rubrique)
  const datees = toutes.filter((p) => p.ligne.date.length === 10)
    .sort((a, b) => cleDate(b.ligne.date).localeCompare(cleDate(a.ligne.date)))
  return { datees, sansDate: toutes.filter((p) => p.ligne.date.length !== 10) }
}

/** Une DECISION (jugement, arret) : une piece rangee sous « Decisions judiciaires ». Elle passe en or. */
export const estDecision = (p: PieceDatee) => /^d[eé]cision/i.test(p.dossier)

function Libelle({ ligne, renommer }: { ligne: LigneArbreVue, renommer: ProprietesClasseurArbre['renommer'] }) {
  return ligne.renomme
    /* Renommage SUR PLACE : c'est ici qu'on nomme un element cree. */
    ? <Saisie className="rig-arbre__saisie" valeur={ligne.texte} autoFocus quand="change"
              onClick={(e) => e.stopPropagation()}
              changer={(texte) => renommer({ id: ligne.id, texte })}
              onKeyDown={(e) => { if (e.key === 'Escape') renommer({ id: ligne.id, texte: '' }) }} />
    : <span className="rig-arbre__texte">{ligne.texte}</span>
}

function Actions({ ligne, menuContextuel }: { ligne: LigneArbreVue, menuContextuel: ProprietesClasseurArbre['menuContextuel'] }) {
  // TROIS POINTS a droite (revele au survol) : le clic droit n'annoncait rien.
  return (
    <button type="button" className="rig-arbre__actions" title="Actions"
            aria-label={'Actions sur ' + ligne.texte}
            onClick={(e) => { e.stopPropagation(); menuContextuel(souris(ligne.id, e)) }}>&#8942;</button>
  )
}

/** Une piece ou une fiche : icone, nom, pages, date ; un clic l'ouvre. */
function Ligne({ ligne, profondeur, p }: { ligne: LigneArbreVue, profondeur: number, p: ProprietesClasseurArbre }) {
  return (
    <div className={'rig-arbre__ligne rig-classeur__piece' + (ligne.actif ? ' rig-arbre__ligne--actif' : '')}
         style={{ '--p': profondeur } as CSSProperties} onClick={() => p.activer(ligne.id)}>
      <IconeGed nature={ligne.icone} classe="rig-arbre__icone" />
      <Libelle ligne={ligne} renommer={p.renommer} />
      {ligne.pages > 0 && <span className="rig-classeur__pages">{ligne.pages} p.</span>}
      {ligne.date && <span className="rig-classeur__date">{ligne.date}</span>}
      <Actions ligne={ligne} menuContextuel={p.menuContextuel} />
    </div>
  )
}

/**
 * « Par dossier » : les pieces, et chaque sous-dossier en intertitre avec ce qu'il range ;
 * un clic sur l'intertitre le replie (le pont le retient, et le rouvre quand une piece
 * qu'il range est choisie ailleurs : historique, palette, frise).
 */
function Contenu({ lignes, profondeur, p }: { lignes: LigneArbreVue[], profondeur: number, p: ProprietesClasseurArbre }) {
  return (
    <>
      {lignes.map((l) => {
        if (!estDossier(l)) return <Ligne key={l.id} ligne={l} profondeur={profondeur} p={p} />
        const ouvert = !l.replie
        return (
          <Fragment key={l.id}>
            <div className={'rig-classeur__it' + (l.chemin ? ' rig-classeur__it--chemin' : '')}
                 style={{ '--p': profondeur } as CSSProperties}
                 role="button" aria-expanded={ouvert} onClick={() => p.replier(l.id)}>
              <span className={'rig-arbre__chevron' + (ouvert ? ' rig-arbre__chevron--ouvert' : '')} aria-hidden="true">&#9656;</span>
              <IconeGed nature="dossier" classe="rig-classeur__it-icone" />
              <Libelle ligne={l} renommer={p.renommer} />
              {l.compte !== null && <b className="rig-classeur__it-n">{l.compte}</b>}
              <Actions ligne={l} menuContextuel={p.menuContextuel} />
            </div>
            {ouvert && l.vide && <div className="rig-classeur__vide" style={{ '--p': profondeur + 1 } as CSSProperties}>aucune piece</div>}
            {ouvert && <Contenu key={l.id + ':c'} lignes={l.enfants} profondeur={profondeur + 1} p={p} />}
          </Fragment>
        )
      })}
    </>
  )
}

/** « Par date » : la frise de la rubrique, par annee ; les decisions en or. */
function Chronologie({ rubrique, p }: { rubrique: LigneArbreVue, p: ProprietesClasseurArbre }) {
  const { datees, sansDate } = frise(rubrique)
  const evenement = (x: PieceDatee, jour: string) => (
    <div key={x.ligne.id}
         className={'rig-arbre__ligne rig-classeur__ev' + (estDecision(x) ? ' rig-classeur__ev--decision' : '')
                    + (x.ligne.actif ? ' rig-arbre__ligne--actif' : '')}
         onClick={() => p.activer(x.ligne.id)}>
      <span className="rig-classeur__jour">{jour}</span>
      <i className="rig-classeur__point" aria-hidden="true"></i>
      <span className="rig-classeur__ev-texte">
        <Libelle ligne={x.ligne} renommer={p.renommer} />
        <em>{x.dossier}{x.ligne.pages > 0 ? ' · ' + x.ligne.pages + ' p.' : ''}</em>
      </span>
      <Actions ligne={x.ligne} menuContextuel={p.menuContextuel} />
    </div>
  )
  return (
    <div className="rig-classeur__frise">
      {datees.map((x, i) => [
        (i === 0 || x.ligne.date.slice(6) !== datees[i - 1]!.ligne.date.slice(6)) && (
          <div key={'an-' + x.ligne.date.slice(6)} className="rig-classeur__an">{x.ligne.date.slice(6)}</div>
        ),
        evenement(x, x.ligne.date.slice(0, 5)),
      ])}
      {sansDate.length > 0 && <div className="rig-classeur__an rig-classeur__an--sans">sans date</div>}
      {sansDate.map((x) => evenement(x, ''))}
      {datees.length + sansDate.length === 0 && <div className="rig-classeur__vide">aucune piece</div>}
    </div>
  )
}

/** La periode d'une rubrique : de sa piece la plus ancienne a la plus recente (la frise va dans l'autre sens). */
function periode(rubrique: LigneArbreVue): string {
  const { datees } = frise(rubrique)
  if (datees.length === 0) return ''
  const derniere = datees[0]!.ligne.date
  const premiere = datees[datees.length - 1]!.ligne.date
  return premiere === derniere ? 'le ' + premiere : 'du ' + premiere + ' au ' + derniere
}

function Rubrique({ r, p }: { r: LigneArbreVue, p: ProprietesClasseurArbre }) {
  const { tri, trier } = p
  const meta = r.compte !== null ? pieces(r.compte) + (r.derniere ? ' · ' + r.derniere : '') : 'aucune piece'
  return (
    <section className={'rig-classeur__carte' + (r.ouvert ? ' rig-classeur__carte--ouverte' : '')} style={teinte(r.groupe)}
             aria-label={r.texte}>
      <div className={'rig-classeur__tete' + (r.chemin ? ' rig-classeur__tete--chemin' : '')}
           role="button" aria-expanded={r.ouvert} onClick={() => p.rubrique(r.id)}>
        <Libelle ligne={r} renommer={p.renommer} />
        <span className="rig-classeur__meta">{meta}</span>
        <span className={'rig-arbre__chevron' + (r.ouvert ? ' rig-arbre__chevron--ouvert' : '')}>&#9656;</span>
        <Actions ligne={r} menuContextuel={p.menuContextuel} />
      </div>
      {r.ouvert && (
        <div className="rig-classeur__corps">
          <div className="rig-classeur__outils">
            <span className="rig-classeur__periode">{periode(r)}</span>
            <span className="rig-classeur__tri" role="group" aria-label="Classer les pieces">
              <button type="button" className={tri === 'dossier' ? 'rig-classeur__tri--on' : undefined}
                      aria-pressed={tri === 'dossier'} onClick={() => { if (tri !== 'dossier') trier('dossier') }}>Par dossier</button>
              <button type="button" className={tri === 'date' ? 'rig-classeur__tri--on' : undefined}
                      aria-pressed={tri === 'date'} onClick={() => { if (tri !== 'date') trier('date') }}>Par date</button>
            </span>
          </div>
          {r.vide && <div className="rig-classeur__vide">aucune piece</div>}
          {tri === 'date' ? <Chronologie rubrique={r} p={p} /> : <Contenu lignes={r.enfants} profondeur={0} p={p} />}
        </div>
      )}
    </section>
  )
}

export function ClasseurArbre(p: ProprietesClasseurArbre) {
  const { arbre, chemin, filtre } = p
  // Le classeur choisi par la bascule vaut pour la SELECTION du moment : ouvrir
  // une autre piece (depuis l'historique, la palette...) montre le sien.
  const cle = chemin.join('␟')
  const [choix, setChoix] = useState<{ id: string, cle: string } | null>(null)
  const domaines = arbre.enfants.filter(estDossier)
  const deLaSelection = domaines.find((d) => d.chemin || d.actif)
  const affiche = (choix !== null && choix.cle === cle ? domaines.find((d) => d.id === choix.id) : undefined)
                  ?? deLaSelection ?? domaines[0] ?? arbre
  const choisir = (id: string) => setChoix({ id, cle })
  const recherche = filtre.trim().length > 0
  const ailleurs = deLaSelection && deLaSelection.id !== affiche.id && chemin.length >= 3 ? deLaSelection : null
  const hors = affiche.enfants.filter((e) => !estRubrique(e))
  const piecesHors = hors.filter((e) => !estDossier(e) && e.icone !== 'note').length

  return (
    <div className="rig-classeur">
      {domaines.length > 1 && (
        <div className="rig-classeur__bascule" role="tablist" aria-label="Classeurs du dossier">
          {domaines.map((d) => (
            <button key={d.id} type="button" role="tab" aria-selected={d.id === affiche.id}
                    className={'rig-classeur__domaine' + (d.id === affiche.id ? ' rig-classeur__domaine--on' : '')}
                    onClick={() => choisir(d.id)}>
              {d.texte} <b>{recherche ? compterPieces(d) : d.compte ?? 0}</b>
            </button>
          ))}
        </div>
      )}
      {/* RAPPEL : la piece ouverte est de l'autre cote ; un clic y ramene. */}
      {ailleurs && (
        <button type="button" className="rig-classeur__rappel" onClick={() => choisir(ailleurs.id)}>
          <i aria-hidden="true"></i>
          <span><b>{chemin[chemin.length - 1]}</b> est ouvert</span>
          <em>{ailleurs.texte} &#8250;</em>
        </button>
      )}
      {hors.length > 0 && (
        <section className="rig-classeur__hors" aria-label={affiche.texte + ' hors rubrique'}>
          <div className="rig-classeur__hors-titre">{affiche.texte} <span>{piecesHors > 0 ? pieces(piecesHors) + ' hors rubrique' : 'hors rubrique'}</span></div>
          <Contenu lignes={hors} profondeur={0} p={p} />
        </section>
      )}
      {affiche.enfants.filter(estRubrique).map((r) => <Rubrique key={r.id} r={r} p={p} />)}
      {affiche.enfants.length === 0 && <div className="rig-classeur__vide">aucune piece</div>}
    </div>
  )
}
