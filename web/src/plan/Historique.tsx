// L'HISTORIQUE DES PIECES (proposition « Historique sous les donnees » du /draft
// du 19/11/2026 ; depuis la proposition 3 du meme jour, il vit dans l'onglet
// « Suivi » de la colonne de droite) : les pieces datees de l'affaire, la plus
// recente en tete, groupees par annee -- date, point, puis le libelle et son
// origine sur deux lignes. Le point prend la couleur du LISERE de sa rubrique
// dans l'arbre (or pour une piece RCS hors rubrique, bleu pour une piece
// judiciaire) : on voit ce qui va ensemble. Un clic ouvre la piece et la montre
// choisie dans l'arbre (geste « historiqueOuvrir »).
//
// TOUTES les pieces, dans une liste qui defile et prend la hauteur de la
// colonne (passe de finition du 19/11/2026) : les 7 dernieres puis « tout
// voir » datent du temps ou l'historique vivait SOUS les donnees ; seul dans sa
// colonne, il laissait un grand vide en bas. Le libelle et l'origine cote a
// cote se coupaient tous deux (« Certificat en matiere de di… Editio… ») :
// sur deux lignes, chacun a la largeur de la colonne.
//
// Ce n'est pas le bouton « Historique » de la fiche d'informations : celui-la
// est le journal des MODIFICATIONS de champs ; celui-ci, la suite des PIECES.

import type { CSSProperties } from 'react'
import type { GestePlan, PieceHistoriqueVue } from './types'

export interface ProprietesHistorique {
  pieces: PieceHistoriqueVue[]
  plan: GestePlan
}

/** « 19/11/2026 » : « 2026 ». */
const annee = (p: PieceHistoriqueVue) => p.date.slice(6)

function point(p: PieceHistoriqueVue): { className: string, style?: CSSProperties } {
  const base = 'rig-chrono__point rig-chrono__point--' + (p.judiciaire ? 'jud' : 'rcs')
  return p.groupe
    ? { className: base + ' rig-chrono__point--groupe', style: { '--rig-chrono-teinte': `var(--rig-groupe-${p.groupe})` } as CSSProperties }
    : { className: base }
}

export function HistoriqueDossier({ pieces, plan }: ProprietesHistorique) {
  if (pieces.length === 0) return null
  return (
    <section className="rig-chrono" aria-label="Historique des pieces">
      <div className="rig-chrono__entete">
        <span className="rig-chrono__titre">Historique des pieces · {pieces.length}</span>
      </div>
      <ol className="rig-chrono__liste">
        {pieces.map((p, i) => [
          // L'ANNEE en tete de ses pieces (le draft du 19/11/2026) : la date de
          // chaque ligne se reduit alors au jour et au mois.
          annee(p) !== (i > 0 ? annee(pieces[i - 1]!) : '') && (
            <li key={'an-' + annee(p)} className="rig-chrono__annee" aria-hidden="true">{annee(p)}</li>
          ),
          <li key={p.id}>
            <button type="button" className={'rig-chrono__piece' + (p.actif ? ' rig-chrono__piece--actif' : '')}
                    aria-current={p.actif ? 'true' : undefined}
                    title={p.date + ' · ' + p.rubrique + ' · ' + p.origine + (p.pages > 0 ? ' · ' + p.pages + ' p.' : '')}
                    onClick={() => plan('historiqueOuvrir', p.id)}>
              <span className="rig-chrono__date">{p.date.slice(0, 5)}</span>
              <span {...point(p)} aria-hidden="true"></span>
              <span className="rig-chrono__texte">
                <span className="rig-chrono__libelle">{p.libelle}</span>
                <span className="rig-chrono__origine">{p.origine}</span>
              </span>
            </button>
          </li>,
        ])}
      </ol>
    </section>
  )
}
