// LES RACCOURCIS CLAVIER DE LA PAGE DU DOSSIER (plan « Vue du dossier = coeur des
// actions », lot 5, 20/11/2026). Un agent de greffe conduit RIG au clavier : chaque
// action de la barre (Metier/ActionsDossier.cs, sa TOUCHE) se joue par sa lettre,
// sans ouvrir de menu ni la palette, et « ? » montre la liste.
//
//   - RaccourcisDossier ecoute la page (web/src/plan/clavier.ts : jamais dans un
//     champ, jamais une touche deja traitee, jamais avec Ctrl, Alt ou Cmd) quand
//     aucune fenetre n'est ouverte par-dessus (Application.tsx le dit : palette,
//     impression, mail, nouveau, menu du clic droit). La lettre joue l'action
//     comme son bouton (jouerAction : le geste « actionDossier », ou la copie).
//   - AideClavier est cette liste, par verbe dans l'ordre de la barre, plus les
//     touches de toute la page. Echap la ferme SEULE (ecouterEchapDabord), « ? »
//     aussi ; un clic sur le voile aussi. Le focus revient ou il etait.

import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent, MouseEvent } from 'react'
import { jouerAction } from './ActionsDossier'
import { ecouterEchapDabord, ecouterRaccourcis } from './clavier'
import type { GestePlan, VerbeActionsVue } from './types'

/** Les touches de toute la page, apres celles des actions. */
export const TOUCHES_DE_LA_PAGE: [string, string][] = [
  ['Ctrl K', 'Rechercher partout, et agir sur ce dossier'],
  ['?', 'Afficher ou fermer cette aide'],
  ['Echap', 'Fermer : cette aide, un menu, la liste des points, puis l affaire'],
]

export interface ProprietesRaccourcis {
  verbes: VerbeActionsVue[]
  numero: string
  plan: GestePlan
  /** La copie avec avis de la page (useCopie, ActionsDossier.tsx). */
  copier: (texte: string, quoi: string) => void
  /** Aucune fenetre n'est ouverte par-dessus la page (Application.tsx). */
  actif: boolean
}

/** Ecoute les lettres des actions et « ? » ; montre l'aide quand elle est demandee. */
export function RaccourcisDossier({ verbes, numero, plan, copier, actif }: ProprietesRaccourcis) {
  const [aide, poserAide] = useState(false)
  // Une fenetre qui s'ouvre par-dessus (la palette Ctrl K, l'impression...) ferme l'aide :
  // elle ne reste pas cachee dessous, a attendre un Echap qui irait a l'autre.
  useEffect(() => { if (!actif) poserAide(false) }, [actif])
  useEffect(() => {
    if (!actif || aide) return
    return ecouterRaccourcis((touche) => {
      const action = verbes.flatMap((v) => v.actions).find((a) => a.touche.toUpperCase() === touche)
      if (touche !== '?' && !action) return false
      // Un menu de la barre ou la liste des points ouverts se referment : la touche a agi.
      document.querySelectorAll<HTMLDetailsElement>('.rig-vged details[open]').forEach((d) => { d.open = false })
      if (action) jouerAction(action, plan, copier)
      else poserAide(true)
      return true
    })
  }, [actif, aide, verbes, plan, copier])
  return aide ? <AideClavier verbes={verbes} numero={numero} fermer={() => poserAide(false)} /> : null
}

export interface ProprietesAide {
  verbes: VerbeActionsVue[]
  numero: string
  fermer: () => void
}

/** La liste des raccourcis de la page, en fenetre par-dessus elle. */
export function AideClavier({ verbes, numero, fermer }: ProprietesAide) {
  const boite = useRef<HTMLDivElement>(null)
  // Le rappel change a chaque rendu du parent : l'ecoute et le focus, eux, ne bougent
  // qu'a l'ouverture et a la fermeture.
  const fermerCourant = useRef(fermer)
  fermerCourant.current = fermer
  useEffect(() => {
    const avant = document.activeElement instanceof HTMLElement ? document.activeElement : null
    boite.current?.focus()
    const arreter = ecouterEchapDabord(() => fermerCourant.current())
    return () => { arreter(); avant?.focus() }
  }, [])
  const touche = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== '?') return
    e.preventDefault()
    fermer()
  }
  const retenir = (e: MouseEvent) => e.stopPropagation()

  const ligne = (t: string, libelle: string) => (
    <li key={t + libelle} className="rig-vged__aide-ligne">
      <kbd className="rig-vged__agir-touche">{t}</kbd><span>{libelle}</span>
    </li>
  )
  return (
    <div className="rig-voile rig-voile--haut" onMouseDown={fermer}>
      <div ref={boite} className="rig-impression rig-vged__aide" role="dialog" aria-modal="true"
           aria-label="Raccourcis clavier" tabIndex={-1} onKeyDown={touche} onMouseDown={retenir}>
        <div className="rig-vged__aide-tete">
          <div className="rig-kicker">Raccourcis clavier · dossier {numero}</div>
          <button type="button" className="rig-bouton rig-bouton--icone rig-bouton--plat" title="Fermer (Echap)"
                  aria-label="Fermer l'aide" onClick={fermer}>
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12"></path><path d="M18 6L6 18"></path></svg>
          </button>
        </div>
        <div className="rig-vged__aide-verbes">
          {verbes.map((v) => (
            <section key={v.verbe} className="rig-vged__aide-verbe" aria-label={v.verbe}>
              <h3 className="rig-vged__aide-legende">{v.verbe}</h3>
              <ul>{v.actions.map((a) => ligne(a.touche, a.libelle))}</ul>
            </section>
          ))}
        </div>
        <section className="rig-vged__aide-verbe rig-vged__aide-partout" aria-label="Toute la page">
          <h3 className="rig-vged__aide-legende">Toute la page</h3>
          <ul>{TOUCHES_DE_LA_PAGE.map(([t, libelle]) => ligne(t, libelle))}</ul>
        </section>
        <p className="rig-vged__aide-note">
          Une lettre ne joue rien pendant une saisie (un champ, une liste, le filtre de l arbre, une note)
          ni sous une fenetre ouverte.
        </p>
      </div>
    </div>
  )
}
