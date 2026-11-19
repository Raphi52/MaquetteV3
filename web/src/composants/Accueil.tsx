// L'ACCUEIL DE RIG : le menu de gauche (les metiers du greffe) et le plan de
// travail de l'accueil -- entete, recherche, chiffres du jour, grille de tuiles.
//
// Porte en React (lot 1 du plan « finir le passage a React », 19/11/2026)
// depuis le <div class="rig-accueil"> de Components/Pages/Home.razor : meme
// balisage, memes classes CSS (rig-accueil.css inchange), memes morceaux de
// texte. Home.razor monte cette ile avec la vue calculee par SessionAccueil
// (Pont/Accueil.cs) et recoit chaque geste par le rappel « geste » ; un ecran
// ouvert (plan de travail, lot 2) recouvre l'accueil sans le demonter.

import { Chiffres, Proue } from '../accueil/Chiffres'
import { Entete } from '../accueil/Entete'
import { Grille } from '../accueil/Grille'
import { Menu } from '../accueil/Menu'
import { Recherche } from '../accueil/Recherche'
import type { GesteAccueil, GesteIle, VuePlanAccueil } from '../accueil/types'

export interface ProprietesAccueil {
  vue: VuePlanAccueil
  geste: (g: GesteIle) => void
}

export function Accueil({ vue, geste }: ProprietesAccueil) {
  const agir: GesteAccueil = (nom, valeur) => { geste({ nom, valeur: valeur ?? null }) }
  return (
    <div className={'rig-accueil ' + (vue.menuReduit ? 'rig-accueil--menu-reduit' : '')}>
      <Menu menu={vue.menu} reduit={vue.menuReduit} geste={agir} />
      {' '}
      <div className="rig-plan">
        {/* La tete du plan : le bandeau noir du theme sombre (« En-tete noir »,
            /draft conv-2001) passe sous la date, le salut ET la recherche. */}
        <div className="rig-plan__tete">
          <Entete entete={vue.entete} geste={agir} />
          {' '}
          <Recherche geste={agir} />
        </div>
        {' '}
        <Chiffres vue={vue.chiffres} geste={agir} />
        {vue.proue && <Proue proue={vue.proue} geste={agir} />}
        <Grille tuiles={vue.tuiles} repliee={vue.chiffres.deplie !== null} signal={vue.signal} geste={agir} />
      </div>
    </div>
  )
}
