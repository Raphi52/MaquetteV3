// Ecran de la procedure PROC_DOC_DEMAT (RIG v2 : arbre de documents
// dematerialises, rigTreeViewAdv1). Colonnes v2 : DisplayLibelle,
// AbrPieceLibelle, DatePiece. Actions du menu contextuel v2 : Voir,
// Modifier..., Requalifier..., Enregistrer sous..., Imprimer...,
// Proprietes..., Supprimer, Restaurer, Remettre en vigueur,
// Demander le controle de la piece..., Verifier le justificatif...,
// Verifier la conformite de la carte d'identite.
//
// Porte depuis Components/Ecrans/EcranDocDemat.razor (lot 5) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses pieces de
// demonstration sont ici, sans pont. La coquille .razor monte cet ecran et
// recoit ouvrirDossier.

import { useState } from 'react'
import { contientSansCasse, dateCourte, estBlanc } from '../criteres'

interface Piece {
  dossier: string
  abrege: string
  libelle: string
  /** aaaa-mm-jj */
  date: string
  etat: string
  controle: string
}

const Pieces: Piece[] = [
  { dossier: '2026B00811', abrege: 'STAT', libelle: 'Statuts constitutifs', date: '2026-08-28', etat: 'En vigueur', controle: 'Conforme' },
  { dossier: '2026B00811', abrege: 'CNI', libelle: 'Carte d\'identite du gerant', date: '2026-08-28', etat: 'En vigueur', controle: 'A verifier' },
  { dossier: '2026B00811', abrege: 'DOM', libelle: 'Justificatif de domiciliation', date: '2026-08-29', etat: 'En vigueur', controle: 'Conforme' },
  { dossier: '2019B00233', abrege: 'PVAG', libelle: 'PV d\'assemblee generale', date: '2026-09-01', etat: 'En vigueur', controle: 'Conforme' },
  { dossier: '2019B00233', abrege: 'STAT', libelle: 'Statuts mis a jour', date: '2026-09-01', etat: 'Remplace', controle: 'Conforme' },
  { dossier: '2011B00512', abrege: 'CPT', libelle: 'Comptes annuels 2025', date: '2026-08-14', etat: 'En vigueur', controle: 'Non controle' },
]

/** `Pieces.GroupBy(p => p.Dossier)` : les dossiers dans l'ordre de leur premiere piece. */
const Groupes: Array<[string, Piece[]]> = [...Pieces.reduce((g, p) => {
  g.set(p.dossier, [...(g.get(p.dossier) ?? []), p])
  return g
}, new Map<string, Piece[]>())]

export interface ProprietesEcranDocDemat {
  /** Remonte le NUMERO de dossier de la piece : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
}

export function EcranDocDemat({ ouvrirDossier }: ProprietesEcranDocDemat) {
  const [ouverts, setOuverts] = useState<ReadonlySet<string>>(new Set(['2026B00811']))
  const [recherche, setRecherche] = useState('')
  const [action, setAction] = useState<string | null>(null)
  // Au demarrage, la premiere piece du dossier deja deplie est selectionnee :
  // sans cela, la moitie basse de l'ecran restait vide tant qu'on n'avait
  // rien clique dans l'arbre.
  const [selection, setSelection] = useState<Piece | null>(Pieces[0] ?? null)

  function basculerGroupe(d: string) {
    setOuverts((avant) => {
      const s = new Set(avant)
      if (!s.delete(d)) s.add(d)
      return s
    })
  }

  const correspond = (p: Piece): boolean =>
    estBlanc(recherche)
    || contientSansCasse(p.libelle, recherche.trim())
    || contientSansCasse(p.abrege, recherche.trim())

  const s = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Dossiers dematerialises</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Rechercher</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        {Groupes.map(([groupe, pieces]) => {
          const ouvert = ouverts.has(groupe)
          return (
            <div key={groupe} className={`rig-menu__groupe${ouvert ? ' rig-menu__groupe--ouvert' : ''}`}>
              <button className="rig-menu__tete" onClick={() => basculerGroupe(groupe)}>
                <span className="rig-menu__chevron">&#9656;</span>
                <span>{groupe}</span>
                <span className="rig-menu__compte">{pieces.length}</span>
              </button>
              {ouvert && (
                <ul className="rig-menu__liste">
                  {pieces.filter(correspond).map((piece) => (
                    <li key={piece.abrege + piece.libelle}>
                      <button className={`rig-menu__lien${selection === piece ? ' rig-menu__lien--actif' : ''}`}
                              onClick={() => setSelection(piece)}>
                        <span className="rig-menu__code">{piece.abrege}</span>{piece.libelle}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )
        })}
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Piece dematerialisee</div>
          <span className="rig-resultats-proc__compte">{selection?.libelle ?? 'aucune piece selectionnee'}</span>
        </div>

        {s ? (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Abrege de la piece</span>
                <input className="rig-saisie-champ__saisie" value={s.abrege} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Date de la piece</span>
                <input className="rig-saisie-champ__saisie" value={dateCourte(s.date)} readOnly />
              </label>
            </div>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Libelle affiche</span>
              <input className="rig-saisie-champ__saisie" value={s.libelle} readOnly />
            </label>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Etat</span>
                <input className="rig-saisie-champ__saisie" value={s.etat} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Controle de la piece</span>
                <input className="rig-saisie-champ__saisie" value={s.controle} readOnly />
              </label>
            </div>

            <div className="rig-criteres__actions">
              <button className="rig-bouton" onClick={() => ouvrirDossier(s.dossier)}>Voir</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Modifier')}>Modifier</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Requalifier')}>Requalifier</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Enregistrer sous')}>Enregistrer sous</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Imprimer')}>Imprimer</button>
            </div>
            <div className="rig-criteres__actions">
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Demande de controle de la piece envoyee')}>Demander le controle</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Verification du justificatif demandee')}>Verifier le justificatif</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Conformite de la carte d identite verifiee')}>Verifier la carte d'identite</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Piece supprimee (restaurable)')}>Supprimer</button>
            </div>

            {action !== null && <p className="rig-nonmaq__texte">{action}</p>}
          </div>
        ) : (
          <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>
            Choisissez une piece dans l'arbre de gauche.
          </div>
        )}
      </section>
    </div>
  )
}
