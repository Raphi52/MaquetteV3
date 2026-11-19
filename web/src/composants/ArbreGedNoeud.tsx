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
  /**
   * Le niveau du dossier qui contient la ligne CHOISIE, quand cette ligne est
   * dans son sous-arbre (-1 sinon) : son sous-lisere prend la teinte pleine.
   * Calcule par la recursion elle-meme ; la racine n'a rien a passer.
   */
  sousActif?: number
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

type GeometrieLigne = Pick<LigneArbreVue, 'niveau' | 'groupe' | 'niveauGroupe' | 'ouvert' | 'vide'> & {
  enfants: readonly unknown[]
}

/**
 * Un SOUS-DOSSIER ouvert d'une rubrique (« Depot n 2024/0141 », « Documents
 * generes lors de la formalite ») : il porte son propre sous-lisere, du haut de
 * sa ligne a sa derniere piece. Ferme, il n'en porte pas : un trait d'une ligne
 * ne relierait rien.
 */
export function estSousTete(ligne: GeometrieLigne): boolean {
  return ligne.groupe > 0 && ligne.niveau > ligne.niveauGroupe && ligne.ouvert
         && (ligne.enfants.length > 0 || ligne.vide)
}

/**
 * Le style d'une ligne : son retrait et, dans une rubrique, ses filets
 * (rig-accueil.css, .rig-arbre__ligne--groupe) :
 *   - le LISERE de la rubrique : sa teinte, au retrait de la tete ;
 *   - les SOUS-LISERES (19/11/2026) : un filet fin de la meme teinte par
 *     sous-dossier qui contient la ligne (--rig-arbre-sous), plus le sien si
 *     elle est elle-meme un sous-dossier ouvert -- chacun au retrait de son
 *     dossier, les niveaux se lisent comme des accolades emboitees ;
 *   - le sous-lisere du dossier qui contient la ligne choisie, en teinte pleine
 *     (--rig-arbre-sous-actif : son rang parmi les sous-liseres).
 */
export function styleLigne(ligne: GeometrieLigne, sousActif = -1): CSSProperties {
  if (!ligne.groupe) return niveau(ligne.niveau)
  const style: Record<string, string | number> = {
    '--rig-arbre-niveau': ligne.niveau,
    '--rig-arbre-teinte': `var(--rig-groupe-${ligne.groupe})`,
    '--rig-arbre-niveau-groupe': ligne.niveauGroupe,
  }
  const g = ligne.niveauGroupe
  // Les sous-dossiers qui contiennent la ligne : ceux entre la tete et elle.
  const sous = Math.max(0, ligne.niveau - 1 - g)
  if (sous > 0) style['--rig-arbre-sous'] = sous
  const propre = estSousTete(ligne)
  if (sousActif > g && (sousActif < ligne.niveau || (sousActif === ligne.niveau && propre))) {
    style['--rig-arbre-sous-actif'] = sousActif - g - 1
    // Sur la ligne du dossier lui-meme, le filet part du meme cran que le sien.
    if (sousActif === ligne.niveau) style['--rig-arbre-sous-actif-haut'] = '4px'
  }
  return style as CSSProperties
}

/**
 * Les TRAITS DE NIVEAU neutres (19/11/2026) : hors rubrique (« Dossier RCS »,
 * qui range Kbis, statuts... et les rubriques elles-memes), chaque dossier
 * ouvert porte aussi son trait, fin et gris, a son retrait -- comme un lisere
 * sans couleur. Chaque niveau de l'arbre a ainsi le sien. La racine (niveau 0,
 * l'affaire) n'en porte pas : son trait longerait tout l'arbre sans rien
 * distinguer.
 *   contenants : les dossiers neutres qui CONTIENNENT la ligne, niveaux 1 a
 *                (tete de sa rubrique ou elle-meme) - 1 ;
 *   propre     : elle est elle-meme un dossier neutre ouvert, non vide.
 */
export function traitsNeutres(ligne: GeometrieLigne): { contenants: number, propre: boolean } {
  const plafond = ligne.groupe ? ligne.niveauGroupe : ligne.niveau
  return {
    contenants: Math.max(0, plafond - 1),
    propre: !ligne.groupe && ligne.niveau >= 1 && ligne.ouvert && (ligne.enfants.length > 0 || ligne.vide),
  }
}

/** « aucune piece » sous un dossier vide : une piece de plus, un niveau plus bas. */
const rangeeDans = (dossier: GeometrieLigne): GeometrieLigne =>
  ({ ...dossier, niveau: dossier.niveau + 1, ouvert: false, vide: false, enfants: [] })

/** Les traits neutres d'une ligne : un element pose sous elle (rig-accueil.css, .rig-arbre__niveaux). */
function TraitsNeutres({ ligne }: { ligne: GeometrieLigne }) {
  const { contenants, propre } = traitsNeutres(ligne)
  if (contenants === 0 && !propre) return null
  return <span className={'rig-arbre__niveaux' + (propre ? ' rig-arbre__niveaux--propre' : '')} aria-hidden="true"
               style={{ '--rig-arbre-neutres': contenants } as CSSProperties}></span>
}

export function ArbreGedNoeud({ ligne, activer, renommer, menuContextuel, sousActif = -1 }: ProprietesArbreGedNoeud) {
  // Cette ligne contient-elle la ligne choisie ? Alors c'est SON sous-lisere
  // qui s'allume, sur elle et sur tout ce qu'elle range.
  const actifDessous = ligne.enfants.some((e) => e.actif) ? ligne.niveau : sousActif
  const groupe = ligne.groupe
    ? ' rig-arbre__ligne--groupe'
      + (ligne.niveau === ligne.niveauGroupe ? ' rig-arbre__ligne--tete' : '')
      + (estSousTete(ligne) ? ' rig-arbre__ligne--sous-tete' : '')
    : ''
  return (
    <>
      {/* CHEMIN : la ligne ou l'on se trouve, et TOUS ses parents, sont marques
          (rig-arbre__ligne--chemin) -- on lit d'un coup d'oeil ou l'on est dans
          le dossier, sans deplier ni compter les retraits.
          LISERE (19/11/2026) : chaque rubrique (Pieces jointes, Depots d'actes...)
          a sa couleur ; sa tete et tout ce qu'elle range portent un filet de cette
          couleur, au retrait de la tete -- on voit ce qui va ensemble. Chaque
          sous-dossier ouvert y ajoute son SOUS-LISERE, plus fin, a son retrait ;
          hors rubrique, les dossiers portent un TRAIT DE NIVEAU neutre. */}
      <div className={`rig-arbre__ligne ${ligne.actif ? 'rig-arbre__ligne--actif' : ''} ${ligne.chemin ? 'rig-arbre__ligne--chemin' : ''}` + groupe}
           style={styleLigne(ligne, actifDessous)}
           onClick={() => activer(ligne.id)}>
        <TraitsNeutres ligne={ligne} />
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
                       renommer={renommer} menuContextuel={menuContextuel} sousActif={actifDessous} />
      ))}
      {/* « aucune piece » est rangee dans le dossier : ses filets (lisere,
          sous-liseres, traits neutres) continuent dessous au lieu de s'interrompre. */}
      {ligne.vide && (
        <div className={'rig-arbre__vide' + (ligne.groupe ? ' rig-arbre__vide--groupe' : '')}
             style={styleLigne(rangeeDans(ligne), actifDessous)}>
          <TraitsNeutres ligne={rangeeDans(ligne)} />aucune piece</div>
      )}
    </>
  )
}
