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

import { Aiguillage } from './Aiguillage'
import type { GestePlan, ProcVue } from './types'

export interface ProprietesProcedure {
  proc: ProcVue
  plan: GestePlan
}

export function Procedure({ proc, plan }: ProprietesProcedure) {
  return (
    <div className="rig-proc">
      <Aiguillage proc={proc} plan={plan} />
    </div>
  )
}
