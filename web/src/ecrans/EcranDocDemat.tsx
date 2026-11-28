// Ecran de la procedure PROC_DOC_DEMAT (RIG v2 : arbre de documents
// dematerialises, rigTreeViewAdv1). Colonnes v2 : DisplayLibelle,
// AbrPieceLibelle, DatePiece. Actions du menu contextuel v2 : Voir,
// Modifier..., Requalifier..., Enregistrer sous..., Imprimer...,
// Proprietes..., Supprimer, Restaurer, Remettre en vigueur,
// Demander le controle de la piece..., Verifier le justificatif...,
// Verifier la conformite de la carte d'identite.
//
// Porte depuis Components/Ecrans/EcranDocDemat.razor (lot 5) : meme balisage,
// memes classes CSS. La coquille .razor monte cet ecran et recoit ouvrirDossier.
//
// BRANCHE SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026, marche du lot 17) : le pont
// Pont/EcranDocDemat.cs rend les pieces dematerialisees du greffe (DEMAT_CREFO_PJ, Serveur/Socle/EcransMain/
// LectureDocDemat.cs), groupees par dossier. Sans serveur, les pieces de demonstration ci-dessous, marquees
// « exemple » (provenance.tsx) ; en panne, le motif et aucune piece. Une piece de la base montre sa demat, son nom de
// fichier et son etat Infogreffe (les colonnes de DEMAT_CREFO_PJ) ; le controle de la piece n'en est pas une.
// Les actions autres que « Voir » sont des traitements de fichier ou des services de RIG v2 (visionneuse, impression,
// controle de la piece) : elles ne s'enregistrent pas et le disent (« Demonstration : rien n'est enregistre »).
// SAUF « Supprimer » et « Remettre en vigueur » une piece de la base (plan « RIG v3 complet, mesure par le banc du parcours
// complet », lot 10, 28/11/2026) : apres les deux confirmations de RIG v2 (UltTreeViewDocDemat.cs:292-293, :307-308), le pont
// les fait ecrire par le serveur (Pont/EcranDocDemat.cs, ModifierEtat ; refus : hors Amitel, l'arbre est en lecture seule), puis
// relit les pieces.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, dateCourte, estBlanc } from '../criteres'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { refus, type ResultatGesteVue } from './formes/commun'

interface Piece {
  dossier: string
  abrege: string
  libelle: string
  /** aaaa-mm-jj */
  date: string
  etat: string
  controle: string
  /** Une piece de la base : son identifiant, sa demat, son fichier et son etat Infogreffe (Pont/EcranDocDemat.cs). */
  id?: string
  demat?: string
  fichier?: string
  infogreffe?: string
}

/** Une piece telle que la rend le pont (PieceDematVue). */
export interface PieceDematVue {
  id: string
  dossier: string
  demande: string
  demat: string
  abrege: string
  libelle: string
  date: string
  etat: string
  fichier: string
  infogreffe: string
}

/** Ce que rend Pont/EcranDocDemat.cs : les pieces, leur total et d'ou elles viennent. */
export interface VueDocDemat {
  pieces: PieceDematVue[]
  total: number
  mode?: ModeLecture
  message?: string | null
}

const Pieces: Piece[] = [
  { dossier: '2026B00811', abrege: 'STAT', libelle: 'Statuts constitutifs', date: '2026-08-28', etat: 'En vigueur', controle: 'Conforme' },
  { dossier: '2026B00811', abrege: 'CNI', libelle: 'Carte d\'identite du gerant', date: '2026-08-28', etat: 'En vigueur', controle: 'A verifier' },
  { dossier: '2026B00811', abrege: 'DOM', libelle: 'Justificatif de domiciliation', date: '2026-08-29', etat: 'En vigueur', controle: 'Conforme' },
  { dossier: '2019B00233', abrege: 'PVAG', libelle: 'PV d\'assemblee generale', date: '2026-09-01', etat: 'En vigueur', controle: 'Conforme' },
  { dossier: '2019B00233', abrege: 'STAT', libelle: 'Statuts mis a jour', date: '2026-09-01', etat: 'Remplace', controle: 'Conforme' },
  { dossier: '2011B00512', abrege: 'CPT', libelle: 'Comptes annuels 2025', date: '2026-08-14', etat: 'En vigueur', controle: 'Non controle' },
]

/** `pieces.GroupBy(p => p.Dossier)` : les dossiers dans l'ordre de leur premiere piece. */
const groupesDe = (pieces: Piece[]): Array<[string, Piece[]]> => [...pieces.reduce((g, p) => {
  g.set(p.dossier, [...(g.get(p.dossier) ?? []), p])
  return g
}, new Map<string, Piece[]>())]

/** Une piece de la base, a la forme de l'ecran. */
const depuisBase = (p: PieceDematVue): Piece => ({
  dossier: p.dossier, abrege: p.abrege, libelle: p.libelle, date: p.date, etat: p.etat, controle: '',
  id: p.id, demat: p.demat, fichier: p.fichier, infogreffe: p.infogreffe,
})

export interface ProprietesEcranDocDemat {
  /** Remonte le NUMERO de dossier de la piece : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
}

export function EcranDocDemat({ ouvrirDossier }: ProprietesEcranDocDemat) {
  // Les pieces du pont ; sans reponse (pas de pont), la demonstration reste, comme avant le branchement.
  const [vue, setVue] = useState<VueDocDemat | null>(null)
  const [ouverts, setOuverts] = useState<ReadonlySet<string>>(new Set([Pieces[0]?.dossier ?? '']))
  const [recherche, setRecherche] = useState('')
  const [action, setAction] = useState<string | null>(null)
  // Au demarrage, la premiere piece du dossier deja deplie est selectionnee :
  // sans cela, la moitie basse de l'ecran restait vide tant qu'on n'avait
  // rien clique dans l'arbre.
  const [selection, setSelection] = useState<Piece | null>(Pieces[0] ?? null)
  // « Supprimer » ou « Remettre en vigueur » une piece de la base : la confirmation en cours (1re ou 2e question de RIG v2) et
  // l'envoi au pont ; apres un geste fait, les pieces sont relues et la piece reste choisie.
  const [confirmation, setConfirmation] = useState<{ supprimer: boolean; etape: 1 | 2 } | null>(null)
  const [envoi, setEnvoi] = useState(false)
  // Chaque lecture des pieces ; apres un geste, la piece a garder choisie (son etat relu).
  const [relecture, setRelecture] = useState<{ n: number; garder: string | undefined }>({ n: 0, garder: undefined })

  useEffect(() => {
    let perime = false
    void appeler<VueDocDemat>('EcranDocDemat.Pieces')
      .then((v) => {
        if (perime || v.mode === 'demo' || v.mode === undefined) return
        setVue(v)
        const gardee = relecture.garder !== undefined ? v.pieces.find((p) => p.id === relecture.garder) : undefined
        if (gardee) { setSelection(depuisBase(gardee)); return }
        // La base remplace la demonstration : son premier dossier deplie, sa premiere piece choisie.
        const premiere = v.pieces[0]
        setOuverts(new Set(premiere ? [premiere.dossier] : []))
        setSelection(premiere ? depuisBase(premiere) : null)
      })
      .catch(() => { /* pas de pont (tests, page hors WebView2) : la demonstration reste */ })
    return () => { perime = true }
  }, [relecture])

  async function modifierEtat(piece: Piece, supprimer: boolean) {
    setConfirmation(null)
    setEnvoi(true)
    try {
      const r = await appeler<ResultatGesteVue>('EcranDocDemat.ModifierEtat', piece.id, supprimer)
      setAction(r.message)
      if (r.fait) setRelecture((x) => ({ n: x.n + 1, garder: piece.id }))
    } catch {
      setAction("Le pont n'a pas répondu : rien n'est enregistré.")
    } finally {
      setEnvoi(false)
    }
  }

  const pieces = vue ? vue.pieces.map(depuisBase) : Pieces
  const groupes = groupesDe(pieces)

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
  const memePiece = (a: Piece | null, b: Piece) => a !== null && (a.id !== undefined ? a.id === b.id : a === b)

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Dossiers dematerialises</div>
        {vue ? <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Pièces" /> : <ProvenanceEcran mode="demo" quoi="Pièces" />}

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Rechercher</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        {groupes.map(([groupe, piecesDuGroupe]) => {
          const ouvert = ouverts.has(groupe)
          return (
            <div key={groupe} className={`rig-menu__groupe${ouvert ? ' rig-menu__groupe--ouvert' : ''}`}>
              <button className="rig-menu__tete" onClick={() => basculerGroupe(groupe)}>
                <span className="rig-menu__chevron">&#9656;</span>
                <span>{groupe}</span>
                <span className="rig-menu__compte">{piecesDuGroupe.length}</span>
              </button>
              {ouvert && (
                <ul className="rig-menu__liste">
                  {piecesDuGroupe.filter(correspond).map((piece) => (
                    <li key={piece.id ?? piece.abrege + piece.libelle}>
                      <button className={`rig-menu__lien${memePiece(selection, piece) ? ' rig-menu__lien--actif' : ''}`}
                              onClick={() => { setSelection(piece); setConfirmation(null) }}>
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
                <input className="rig-saisie-champ__saisie" value={s.date ? dateCourte(s.date) : ''} readOnly />
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
              {s.id === undefined ? (
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Controle de la piece</span>
                  <input className="rig-saisie-champ__saisie" value={s.controle} readOnly />
                </label>
              ) : (
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Etat Infogreffe</span>
                  <input className="rig-saisie-champ__saisie" value={s.infogreffe ?? ''} readOnly />
                </label>
              )}
            </div>
            {s.id !== undefined && (
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Demat</span>
                  <input className="rig-saisie-champ__saisie" value={s.demat ?? ''} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Nom du fichier</span>
                  <input className="rig-saisie-champ__saisie" value={s.fichier ?? ''} readOnly />
                </label>
              </div>
            )}

            <div className="rig-criteres__actions">
              <button className="rig-bouton" onClick={() => ouvrirDossier(s.dossier)}>Voir</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction(refus('Modifier'))}>Modifier</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction(refus('Requalifier'))}>Requalifier</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction(refus('Enregistrer sous'))}>Enregistrer sous</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction(refus('Imprimer'))}>Imprimer</button>
            </div>
            <div className="rig-criteres__actions">
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction(refus('Demander le controle'))}>Demander le controle</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction(refus('Verifier le justificatif'))}>Verifier le justificatif</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction(refus("Verifier la carte d'identite"))}>Verifier la carte d'identite</button>{' '}
              {s.id === undefined
                ? <button className="rig-bouton rig-bouton--plat" onClick={() => setAction(refus('Supprimer'))}>Supprimer</button>
                : s.etat === 'Supprimée'
                  ? <button className="rig-bouton rig-bouton--plat" disabled={envoi}
                            onClick={() => { setAction(null); setConfirmation({ supprimer: false, etape: 1 }) }}>Remettre en vigueur</button>
                  : <button className="rig-bouton rig-bouton--plat" disabled={envoi}
                            onClick={() => { setAction(null); setConfirmation({ supprimer: true, etape: 1 }) }}>Supprimer</button>}
            </div>

            {confirmation !== null && s.id !== undefined && (
              <div className="rig-criteres__actions" role="alertdialog">
                <p className="rig-nonmaq__texte">
                  {confirmation.etape === 2 ? 'En êtes-vous sûr ?'
                    : confirmation.supprimer ? `Mettre la pièce '${s.libelle}' en état 'Supprimé' dans la base de données du greffe ?`
                      : `Remettre en vigueur la pièce '${s.libelle}' dans la base de données du greffe ?`}
                </p>
                <button className="rig-bouton" disabled={envoi}
                        onClick={() => confirmation.etape === 1 ? setConfirmation({ ...confirmation, etape: 2 }) : void modifierEtat(s, confirmation.supprimer)}>Oui</button>{' '}
                <button className="rig-bouton rig-bouton--plat" onClick={() => setConfirmation(null)}>Non</button>
              </div>
            )}

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
