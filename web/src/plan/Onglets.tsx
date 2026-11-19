// LES ONGLETS (esprit navigateur), portes de Components/Pages/Home.razor
// (lot 2 du plan « finir le passage a React », 19/11/2026). Depuis le
// 19/11/2026 (« comme Brave ») ils vivent DANS la barre du haut (BarreRig.tsx) :
// un appui sur un onglet ne doit donc pas deplacer la fenetre.
// Chaque vue ouverte prend un onglet. On les reordonne en les glissant ;
// glisse HORS de la fenetre, un onglet part dans sa PROPRE fenetre (FenetreVue),
// qu'on peut poser sur un second ecran (demande du 17/09/2026). L'agencement
// est retenu d'une session a l'autre (SessionPlanTravail.RetenirAgencement).

import { useEffect } from 'react'
import { arreter } from '../accueil/types'
import { ecouterGlisse, finirSuivi, mesurerOnglets } from './glisse'
import type { GestePlan, OngletBarreVue } from './types'

export interface ProprietesOnglets {
  onglets: OngletBarreVue[]
  /** Avance quand le C# a termine lui-meme le geste (onglet arrache) : l'onglet reprend sa place. */
  finsDeGlisse: number
  plan: GestePlan
}

export function Onglets({ onglets, finsDeGlisse, plan }: ProprietesOnglets) {
  useEffect(() => ecouterGlisse(), [])
  // finirGlisseOnglet de rig-accueil.js : appele quand l'onglet vient de partir dans sa fenetre.
  useEffect(() => { if (finsDeGlisse > 0) finirSuivi() }, [finsDeGlisse])

  return (
    <div className="rig-vonglets" id="rig-vonglets">
      {onglets.map((onglet) => (
        <div className={'rig-vonglet ' + (onglet.actif ? 'rig-vonglet--actif' : '') + ' ' + (onglet.detache ? 'rig-vonglet--sorti' : '') + ' ' + (onglet.glisse ? 'rig-vonglet--glisse' : '')}
             key={onglet.code}
             data-rig-onglet={onglet.code}
             title={onglet.detache ? 'Ouvert dans sa propre fenetre' : 'Glissez l onglet hors de la fenetre pour le sortir sur un autre ecran'}
             onPointerDown={(e) => plan('ongletDebut', { code: onglet.code, x: e.clientX, y: e.clientY, mesure: mesurerOnglets() })}
             // Sans bouton enfonce, le C# n'a rien a faire d'un mouvement (GlisseCode
             // nul) : on ne lui envoie que ceux d'un geste en cours.
             onPointerMove={(e) => { if (e.buttons !== 0) plan('ongletGlisse', { x: e.clientX, y: e.clientY }) }}
             onPointerUp={(e) => plan('ongletFin', { x: e.clientX, y: e.clientY })}
             // La barre deplace la fenetre au mousedown : sur un onglet, ce serait
             // la fenetre qui partirait au lieu de l'onglet.
             onMouseDown={arreter}
             onClick={() => plan('ongletActiver', onglet.code)}>
          {/* La pastille du verbe, la MEME que dans le menu de gauche (19/11/2026) :
              son survol nomme le verbe, a la place de l'aide de l'onglet. */}
          {onglet.verbe && (
            <span className={'rig-menu__pastille rig-vonglet__verbe rig-vonglet__verbe--' + onglet.verbe.teinte}
                  title={onglet.verbe.titre}>{onglet.verbe.signe}</span>
          )}
          <span className="rig-vonglet__titre">{onglet.libelle}</span>
          {onglet.detache && <span className="rig-vonglet__sorti" aria-label="sorti dans sa fenetre">{'↗'}</span>}
          {/* La croix est DESSINEE, du meme trait que le « + » et la maison de
              la barre (BarreRig.tsx, 19/11/2026) : le caractere « × » d'avant
              etait trop fin a cote d'eux. */}
          <button className="rig-vonglet__fermer" aria-label={'Fermer ' + onglet.libelle}
                  onClick={(e) => { e.stopPropagation(); plan('ongletFermer', onglet.code) }}
                  onPointerDown={(e) => e.stopPropagation()}>
            <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 4l8 8M12 4l-8 8" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round"></path></svg>
          </button>
        </div>
      ))}
    </div>
  )
}
