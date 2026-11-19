// LA RECHERCHE DE L'ACCUEIL : centree, toujours a la meme place, jamais
// deplacable.
//
// Depuis le 19/11/2026 (conv-2018, /draft « Page Recherche »), elle est la PORTE
// de la palette Ctrl K (composants/Palette.tsx), qui cherche partout : le
// travail le plus urgent, les ecrans du menu, les elements de l'arbre, le texte
// des pieces, les annonces. Un clic ouvre la palette ; une frappe l'ouvre avec
// la lettre tapee, et la palette continue la saisie. La barre n'a plus de liste
// a elle (traitements, affaires, elements) : la palette l'a remplacee, avec les
// memes resultats et davantage -- deux recherches qui ne trouvent pas la meme
// chose, c'est une de trop.
//
// Ouverte sur l'accueil (clic dans la barre, ou Ctrl K), la palette est la LISTE
// DEROULANTE de cette barre (19/11/2026) : elle se dessine a sa place (`ancree`),
// sa saisie dans la pilule, ses resultats pendus dessous. Seul Ctrl K depuis un
// ecran ou une affaire la fait flotter au milieu.

import { ATTENTE_BARRE, Palette } from '../composants/Palette'
import type { GestePlan, PaletteVue } from '../plan/types'
import type { GesteAccueil } from './types'

export interface ProprietesRecherche {
  geste: GesteAccueil
  /** La palette ouverte depuis cette barre (vue.palette.ancree), sinon null. */
  palette?: PaletteVue | null
  plan?: GestePlan
}

export function Recherche({ geste, palette = null, plan }: ProprietesRecherche) {
  if (palette && plan) return <Palette vue={palette} plan={plan} ancree />
  return (
    <div className="rig-recherche rig-recherche--fixe">
      <span style={{ font: '13px var(--rig-font-mono)', color: 'var(--rig-focus)' }}>{'⌕'}</span>
      {' '}
      {/* Le champ reste vide : ce qui y est tape part a la palette, qui s'ouvre
          par-dessus et garde la suite de la frappe. Le clic n'y pose pas le
          curseur (preventDefault) : c'est la saisie de la palette qui le prend. */}
      <input className="rig-recherche__saisie" value="" aria-label="Rechercher partout"
             placeholder={ATTENTE_BARRE}
             onMouseDown={(e) => { e.preventDefault(); geste('palette', '') }}
             onChange={(e) => geste('palette', e.target.value)} />
      {' '}
      {/* La pastille ouvre la PALETTE, comme la touche. */}
      <button type="button" className="rig-touche rig-touche--palette" title="Chercher partout, depuis n importe quel ecran"
              onClick={() => geste('palette')}>Ctrl K</button>
    </div>
  )
}
