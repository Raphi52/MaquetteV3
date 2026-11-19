// Separateur DEPLACABLE entre deux colonnes : on le saisit et on glisse pour
// regler la largeur de la colonne qui le PRECEDE. Composant partage, purement
// cote page : la largeur est un reglage d'affichage, elle ne passe pas par le
// pont (la Visionneuse, elle, garde ses largeurs dans le C# du plan de travail
// parce que tout son etat y vit deja).
//
// - Glisser : la largeur de depart se LIT sur la colonne au debut du geste, puis
//   suit le pointeur, bornee entre `min` et ce qui laisse `resteMin` a la colonne
//   suivante. Le pointeur est capture : le geste continue meme si la souris
//   passe au-dessus du panneau voisin, sans voile a poser.
// - Double-clic : retour a la largeur d'origine (`changer(null)`).
// - Clavier (le separateur prend le focus) : fleches gauche/droite, 16 px par appui.
//
// preventDefault a l'appui : sans lui, le navigateur demarre une SELECTION de
// texte et toute la vue se couvre de surlignage (meme remarque que la Visionneuse).

import { useRef, type KeyboardEvent, type PointerEvent, type RefObject } from 'react'

export interface ProprietesPoigneeColonne {
  /** La colonne reglee : sa largeur reelle se lit au debut de chaque geste. */
  colonne: RefObject<HTMLElement | null>
  /** Largeur minimale de la colonne (px). */
  min: number
  /** Place minimale laissee a la colonne qui suit (px). */
  resteMin: number
  /** La nouvelle largeur (px), ou null pour revenir a la largeur d'origine. */
  changer: (largeur: number | null) => void
  className: string
  libelle: string
}

/** Pas d'une fleche du clavier (px). */
export const PasClavier = 16

export function PoigneeColonne({ colonne, min, resteMin, changer, className, libelle }: ProprietesPoigneeColonne) {
  const geste = useRef<{ x: number; largeur: number; max: number } | null>(null)

  /** La largeur actuelle de la colonne, et la plus grande permise dans son conteneur. */
  const mesurer = (poignee: HTMLElement) => {
    const largeur = colonne.current?.getBoundingClientRect().width ?? min
    const conteneur = poignee.parentElement?.getBoundingClientRect().width ?? 0
    const max = Math.max(min, conteneur - poignee.getBoundingClientRect().width - resteMin)
    return { largeur, max }
  }
  const borner = (largeur: number, max: number) => Math.round(Math.min(max, Math.max(min, largeur)))

  const appuyer = (e: PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0) return
    e.preventDefault()
    const { largeur, max } = mesurer(e.currentTarget)
    geste.current = { x: e.clientX, largeur, max }
    try { e.currentTarget.setPointerCapture(e.pointerId) } catch { /* pointeur deja relache */ }
  }
  const glisser = (e: PointerEvent<HTMLDivElement>) => {
    const g = geste.current
    if (!g) return
    changer(borner(g.largeur + e.clientX - g.x, g.max))
  }
  const lacher = () => { geste.current = null }
  const touche = (e: KeyboardEvent<HTMLDivElement>) => {
    const sens = e.key === 'ArrowLeft' ? -1 : e.key === 'ArrowRight' ? 1 : 0
    if (sens === 0) return
    e.preventDefault()
    const { largeur, max } = mesurer(e.currentTarget)
    changer(borner(largeur + sens * PasClavier, max))
  }

  return (
    <div className={className} role="separator" aria-orientation="vertical" aria-label={libelle} tabIndex={0}
         title={`${libelle} — double-clic : largeur d'origine`}
         onPointerDown={appuyer} onPointerMove={glisser} onPointerUp={lacher} onPointerCancel={lacher}
         onLostPointerCapture={lacher} onDoubleClick={() => changer(null)} onKeyDown={touche}></div>
  )
}
