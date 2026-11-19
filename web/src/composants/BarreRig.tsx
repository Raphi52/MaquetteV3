// LA BARRE DU HAUT de la fenetre RIG, « comme Brave » (19/11/2026) : la
// maison (retour a l'accueil), les onglets des vues ouvertes, puis les trois
// boutons de la fenetre. Rien
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
  /** Rien ne recouvre l'accueil (ni ecran, ni affaire) : la maison est l'onglet actif. */
  accueil: boolean
  geste: (g: GesteIle) => void
  plan: GestePlan
}

export function BarreRig({ onglets, finsDeGlisse, accueil, geste, plan }: ProprietesBarreRig) {
  const agir: GesteAccueil = (nom, valeur) => { geste({ nom, valeur: valeur ?? null }) }
  return (
    <header className="rig-barre" onMouseDown={() => agir('deplacer')}>
      {/* LA MAISON, premier onglet (19/11/2026) : retour a l'accueil, les
          onglets restent ouverts (SessionPlanTravail.AllerALAccueil). Toujours
          la, meme sans onglet : une affaire ouverte n'en a pas. Hors de
          #rig-vonglets et sans data-rig-onglet : elle ne se glisse pas. */}
      <button className={'rig-vonglet rig-vonglet--maison' + (accueil ? ' rig-vonglet--actif' : '')}
              title="Accueil" aria-label="Accueil" aria-current={accueil ? 'page' : undefined}
              onClick={() => plan('accueil')} onMouseDown={arreter}>
        <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 7.6 8 2.8l6 4.8M3.8 6.3v6.9h3.1V9.8h2.2v3.4h3.1V6.3" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"></path></svg>
      </button>
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
