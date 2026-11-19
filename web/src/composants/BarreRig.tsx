// LA BARRE DU HAUT de la fenetre RIG, « comme Brave » (19/11/2026) : les
// onglets des vues ouvertes, puis les trois boutons de la fenetre. Rien
// d'autre : la marque, la version, le rond de profil et le panneau de
// reglages en sont partis -- le theme se choisit dans les reglages de
// l'accueil (Entete.tsx), le profil reste a droite du titre de l'accueil.
// Un appui sur la barre HORS d'un onglet ou d'un bouton DEPLACE la fenetre.
//
// Deux rappels : « geste » pour la fenetre (SessionAccueil.Agir, Pont/Accueil.cs,
// qui en fait une DemandeAccueil executee par FenetreInterop), « plan » pour
// les onglets (SessionPlanTravail.Agir : activer, fermer, glisser, arracher).

import { arreter, type GesteAccueil, type GesteIle } from '../accueil/types'
import { Onglets } from '../plan/Onglets'
import type { GestePlan, OngletBarreVue } from '../plan/types'

export interface ProprietesBarreRig {
  /** Nul quand aucune vue n'est ouverte : la barre ne garde que ses boutons. */
  onglets: OngletBarreVue[] | null
  /** Avance quand le C# a termine lui-meme un glisser d'onglet (onglet arrache). */
  finsDeGlisse: number
  geste: (g: GesteIle) => void
  plan: GestePlan
}

export function BarreRig({ onglets, finsDeGlisse, geste, plan }: ProprietesBarreRig) {
  const agir: GesteAccueil = (nom, valeur) => { geste({ nom, valeur: valeur ?? null }) }
  return (
    <header className="rig-barre" onMouseDown={() => agir('deplacer')}>
      {onglets && <Onglets onglets={onglets} finsDeGlisse={finsDeGlisse} plan={plan} />}
      <div className="rig-fenetre">
        <button className="rig-fenetre__bouton" title="Reduire"
                onClick={() => agir('reduire')} onMouseDown={arreter}>{'–'}</button>
        <button className="rig-fenetre__bouton" title="Agrandir"
                onClick={() => agir('agrandir')} onMouseDown={arreter}>{'□'}</button>
        <button className="rig-fenetre__bouton rig-fenetre__bouton--fermer" title="Fermer"
                onClick={() => agir('fermer')} onMouseDown={arreter}>{'✕'}</button>
      </div>
    </header>
  )
}
