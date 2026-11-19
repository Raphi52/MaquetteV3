// L'ECRAN DE PROCEDURE (div.rig-proc) de Components/Pages/Home.razor, porte au
// lot 2 du plan « finir le passage a React » (19/11/2026). Le cadre est
// commun ; chaque procedure a son composant dans src/ecrans/, monte par
// l'aiguillage (Aiguillage.tsx). Les procedures sans ecran dedie tombent sur
// EcranNonMaquette, qui se nomme.
//
// Le cadre ne porte plus rien au-dessus de l'ecran (19/11/2026, conv-2010, a la
// demande de l'utilisateur : « ca prend de la place pour rien ») : ni la ligne
// du titre (etoile, Fermer), ni le bandeau des ecrans voisins du meme sujet.
// L'onglet porte le titre et sa croix ferme l'ecran ; l'etoile reste dans le
// menu de gauche.
//
// `cachee` : l'onglet de cet ecran est ouvert mais ne se montre pas. L'ecran
// reste monte (son etat survit au changement d'onglet, Application.tsx) ; seul
// son cadre sort de la page.

import { Aiguillage } from './Aiguillage'
import type { GestePlan, ProcVue } from './types'
import { EcranVisible } from './visibilite'

export interface ProprietesProcedure {
  proc: ProcVue
  plan: GestePlan
  cachee?: boolean
}

export function Procedure({ proc, plan, cachee = false }: ProprietesProcedure) {
  return (
    // display:none en ligne : .rig-proc pose display:flex, qui l'emporterait sur l'attribut hidden.
    <div className="rig-proc" {...(cachee ? { style: { display: 'none' }, 'aria-hidden': true } : {})}>
      <EcranVisible.Provider value={!cachee}>
        <Aiguillage proc={proc} plan={plan} />
      </EcranVisible.Provider>
    </div>
  )
}
