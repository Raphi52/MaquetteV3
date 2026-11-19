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

import type { GesteAccueil } from './types'

export function Recherche({ geste }: { geste: GesteAccueil }) {
  return (
    <div className="rig-recherche rig-recherche--fixe">
      <span style={{ font: '13px var(--rig-font-mono)', color: 'var(--rig-focus)' }}>{'⌕'}</span>
      {' '}
      {/* Le champ reste vide : ce qui y est tape part a la palette, qui s'ouvre
          par-dessus et garde la suite de la frappe. Le clic n'y pose pas le
          curseur (preventDefault) : c'est la saisie de la palette qui le prend. */}
      <input className="rig-recherche__saisie" value="" aria-label="Rechercher partout"
             placeholder="Rechercher un SIREN, une societe, un dirigeant, un n° de demande…"
             onMouseDown={(e) => { e.preventDefault(); geste('palette', '') }}
             onChange={(e) => geste('palette', e.target.value)} />
      {' '}
      {/* La pastille ouvre la PALETTE, comme la touche. */}
      <button type="button" className="rig-touche rig-touche--palette" title="Chercher partout, depuis n importe quel ecran"
              onClick={() => geste('palette')}>Ctrl K</button>
    </div>
  )
}
