// Une ligne de l'arbre GED, et ses enfants par recursion.
//
// L'arbre de RIG v2 (PROC_VDOSSIER/Control/TreeviewGed) est un vrai arbre :
// dossier > sous-dossier > piece, sur autant de niveaux que le dossier en compte
// (Judiciaire > Ordonnances > Demandeur > piece). Un composant qui s'appelle
// lui-meme rend cette profondeur sans la plafonner ; l'ancienne liste plate de
// categories ne le pouvait pas.
//
// Porte en React (lot 6 de la migration, 19/11/2026) depuis
// Components/ArbreGedNoeud.razor : meme balisage, memes classes CSS. Les regles
// (enfants visibles, filtre, branches ouvertes, chemin, repli au clic) vivent
// dans Pont/ArbreGedNoeud.cs : la coquille les applique aux objets vivants de
// l'accueil et passe ici la ligne racine, enfants deja calcules. Chaque geste
// remonte a la coquille avec l'identifiant du noeud.

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
  /** La teinte du lisere de sa rubrique (1 a 8), 0 hors rubrique. */
  groupe: number
  /** Le niveau de la tete de sa rubrique : le lisere se pose a son retrait. */
  niveauGroupe: number
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

export interface ProprietesArbreGedNoeud {
  ligne: LigneArbreVue
  /** Clic sur la ligne : la coquille replie/deplie puis leve Selectionner. */
  activer: (id: string) => void
  /** Renommage valide (texte vide = renommage abandonne). */
  renommer: (renommage: { id: string, texte: string }) => void
  /** Bouton « trois points » d'une ligne : ouvre le menu des operations. */
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

const niveau = (n: number) => ({ '--rig-arbre-niveau': n }) as CSSProperties

/**
 * Le style d'une ligne : son retrait et, dans une rubrique, la teinte et le
 * niveau de la tete de son LISERE (rig-accueil.css, .rig-arbre__ligne--groupe).
 */
export function styleLigne(ligne: Pick<LigneArbreVue, 'niveau' | 'groupe' | 'niveauGroupe'>): CSSProperties {
  if (!ligne.groupe) return niveau(ligne.niveau)
  return {
    '--rig-arbre-niveau': ligne.niveau,
    '--rig-arbre-teinte': `var(--rig-groupe-${ligne.groupe})`,
    '--rig-arbre-niveau-groupe': ligne.niveauGroupe,
  } as CSSProperties
}

export function ArbreGedNoeud({ ligne, activer, renommer, menuContextuel }: ProprietesArbreGedNoeud) {
  return (
    <>
      {/* CHEMIN : la ligne ou l'on se trouve, et TOUS ses parents, portent un
          filet a gauche -- on lit d'un coup d'oeil ou l'on est dans le dossier,
          sans deplier ni compter les retraits.
          LISERE (19/11/2026) : chaque rubrique (Pieces jointes, Depots d'actes...)
          a sa couleur ; sa tete et tout ce qu'elle range portent un filet de cette
          couleur, au retrait de la tete -- on voit ce qui va ensemble. */}
      <div className={`rig-arbre__ligne ${ligne.actif ? 'rig-arbre__ligne--actif' : ''} ${ligne.chemin ? 'rig-arbre__ligne--chemin' : ''}`
                      + (ligne.groupe ? ' rig-arbre__ligne--groupe' + (ligne.niveau === ligne.niveauGroupe ? ' rig-arbre__ligne--tete' : '') : '')}
           style={styleLigne(ligne)}
           onClick={() => activer(ligne.id)}>
        <span className="rig-arbre__libelle">
          {ligne.depliable
            ? <span className={`rig-arbre__chevron ${ligne.ouvert ? 'rig-arbre__chevron--ouvert' : ''}`}>&#9656;</span>
            : <span className="rig-arbre__chevron rig-arbre__chevron--vide"></span>}
          <IconeGed nature={ligne.icone} classe="rig-arbre__icone" />
          {ligne.renomme
            /* Renommage SUR PLACE : c'est ici qu'on nomme un element cree,
               plutot que dans un champ de la fenetre de creation. */
            ? <Saisie className="rig-arbre__saisie" valeur={ligne.texte} autoFocus quand="change"
                      onClick={(e) => e.stopPropagation()}
                      changer={(texte) => renommer({ id: ligne.id, texte })}
                      onKeyDown={(e) => { if (e.key === 'Escape') renommer({ id: ligne.id, texte: '' }) }} />
            : <span className="rig-arbre__texte">{ligne.texte}</span>}
          {ligne.compte !== null && <span className="rig-arbre__compte">({ligne.compte})</span>}
        </span>
        {' '}
        {/* ACTIONS : trois points a DROITE de la ligne, comme la liste des
            conversations d'Autowin OS. Le clic droit etait invisible -- rien a
            l'ecran ne disait qu'un menu existait. Le bouton reste discret
            (revele au survol ou quand son menu est ouvert). */}
        <button type="button" className="rig-arbre__actions" title="Actions"
                aria-label={'Actions sur ' + ligne.texte}
                onClick={(e) => { e.stopPropagation(); menuContextuel(souris(ligne.id, e)) }}>&#8942;</button>
      </div>
      {ligne.ouvert && ligne.enfants.map((enfant) => (
        <ArbreGedNoeud key={enfant.id} ligne={enfant} activer={activer}
                       renommer={renommer} menuContextuel={menuContextuel} />
      ))}
      {ligne.vide && (
        <div className="rig-arbre__vide" style={niveau(ligne.niveau + 1)}>aucune piece</div>
      )}
    </>
  )
}
