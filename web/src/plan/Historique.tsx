// L'HISTORIQUE DES PIECES (proposition « Historique sous les donnees » du /draft
// du 19/11/2026 ; depuis la proposition 3 du meme jour, il vit dans l'onglet
// « Suivi » de la colonne de droite) : les pieces datees de l'affaire, la plus
// recente en tete, une ligne par piece -- date, point, libelle, origine. Le
// point prend la couleur du LISERE de sa rubrique dans l'arbre (or pour une
// piece RCS hors rubrique, bleu pour une piece judiciaire) : on voit ce qui va
// ensemble. Les 7 dernieres d'abord, « tout voir » deroule le reste. Un clic
// ouvre la piece et la montre choisie dans l'arbre (geste « historiqueOuvrir »).
//
// Ce n'est pas le bouton « Historique » de la fiche d'informations : celui-la
// est le journal des MODIFICATIONS de champs ; celui-ci, la suite des PIECES.

import { useState } from 'react'
import type { CSSProperties } from 'react'
import type { GestePlan, PieceHistoriqueVue } from './types'

/** Le nombre de pieces montrees avant « tout voir ». */
export const PIECES_RECENTES = 7

export interface ProprietesHistorique {
  pieces: PieceHistoriqueVue[]
  plan: GestePlan
}

function point(p: PieceHistoriqueVue): { className: string, style?: CSSProperties } {
  const base = 'rig-chrono__point rig-chrono__point--' + (p.judiciaire ? 'jud' : 'rcs')
  return p.groupe
    ? { className: base + ' rig-chrono__point--groupe', style: { '--rig-chrono-teinte': `var(--rig-groupe-${p.groupe})` } as CSSProperties }
    : { className: base }
}

export function HistoriqueDossier({ pieces, plan }: ProprietesHistorique) {
  const [tout, setTout] = useState(false)
  if (pieces.length === 0) return null
  const montrees = tout ? pieces : pieces.slice(0, PIECES_RECENTES)
  return (
    <section className="rig-chrono" aria-label="Historique des pieces">
      <div className="rig-chrono__entete">
        <span className="rig-chrono__titre">Historique des pieces · {pieces.length}</span>
        {pieces.length > PIECES_RECENTES && (
          <button type="button" className="rig-chrono__tout" aria-expanded={tout}
                  onClick={() => setTout(!tout)}>{tout ? 'les ' + PIECES_RECENTES + ' dernieres' : 'tout voir'}</button>
        )}
      </div>
      <ol className="rig-chrono__liste">
        {montrees.map((p) => (
          <li key={p.id}>
            <button type="button" className={'rig-chrono__piece' + (p.actif ? ' rig-chrono__piece--actif' : '')}
                    aria-current={p.actif ? 'true' : undefined}
                    title={p.rubrique + ' · ' + p.origine + (p.pages > 0 ? ' · ' + p.pages + ' p.' : '')}
                    onClick={() => plan('historiqueOuvrir', p.id)}>
              <span className="rig-chrono__date">{p.date}</span>
              <span {...point(p)} aria-hidden="true"></span>
              <span className="rig-chrono__libelle">{p.libelle}</span>
              <span className="rig-chrono__origine">{p.origine}</span>
            </button>
          </li>
        ))}
      </ol>
    </section>
  )
}
