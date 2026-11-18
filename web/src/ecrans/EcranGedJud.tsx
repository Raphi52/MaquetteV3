// Ecran de la procedure PROC_GEDJUD.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_GEDJUD) :
//   - ETP_RECHERCHE_DOC/OPE_RECHERCHE_DOC : ulT_txt_numero_metier,
//     ult_cbb_type, ult_date_reception_debut, ult_date_reception_fin,
//     ult_txt_audience_cabinet, ult_txt_commentaire,
//     ult_chb_afficher_minute_non_num, ult_chb_afficher_tout,
//     ult_chb_expandAll, ult_chb_selectAll
//   - ETP_RECHERCHE_DJ/OPE_RECHERCHE_DJ : ult_txt_numero, ult_cbb_type
//   - PROC_GED_LIER_DJ, PROC_GED_HISTORIQUE, PROC_GEDJUD_EDITION :
//     les actions Lier a une decision, Historique, Imprimer, Envoyer.
//
// Porte depuis Components/Ecrans/EcranGedJud.razor (lot 5) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses documents de
// demonstration et ses filtres sont ici, sans pont. La coquille .razor monte
// cet ecran et recoit ouvrirDossier.

import { useState } from 'react'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'

interface DocGed {
  decision: string
  minute: string
  instance: string
  nature: string
  /** aaaa-mm-jj, null pour un document que nulle decision ne porte encore */
  dateDecision: string | null
  /** aaaa-mm-jj */
  datePrevue: string
  nbLies: number
  etat: string
  statut: string
  dossier: string
  historiques: string[]
}

const Docs: DocGed[] = [
  { decision: 'Jugement d\'ouverture', minute: '2026/0412', instance: '2026J00121', nature: 'Jugement', dateDecision: '2026-03-06', datePrevue: '2026-03-05', nbLies: 4, etat: 'Notifie', statut: 'ok', dossier: '2024B00318', historiques: ['06/03/2026 — signe par le president', '07/03/2026 — notifie au mandataire'] },
  { decision: 'Ordonnance de designation', minute: '2026/0518', instance: '2026J00134', nature: 'Ordonnance', dateDecision: '2026-06-02', datePrevue: '2026-06-01', nbLies: 2, etat: 'Signe', statut: 'action', dossier: '2024B00407', historiques: ['02/06/2026 — signe par le juge-commissaire'] },
  { decision: 'Jugement de cloture', minute: '', instance: '2025J00088', nature: 'Jugement', dateDecision: null, datePrevue: '2026-09-20', nbLies: 0, etat: 'Brouillon', statut: 'attente', dossier: '2023B01120', historiques: ['01/09/2026 — projet cree'] },
  { decision: 'Copie conforme', minute: '2024/1188', instance: '2024J00507', nature: 'Copie', dateDecision: '2024-11-08', datePrevue: '2024-11-08', nbLies: 1, etat: 'Notifie', statut: 'ok', dossier: '2025B00044', historiques: ['08/11/2024 — copie delivree'] },
]

export interface ProprietesEcranGedJud {
  /** Remonte le NUMERO de dossier du document : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
}

export function EcranGedJud({ ouvrirDossier }: ProprietesEcranGedJud) {
  // Criteres de OPE_RECHERCHE_DOC (RIG v2).
  const [receptionDebut, setReceptionDebut] = useState('')
  const [receptionFin, setReceptionFin] = useState('')
  const [audienceCabinet, setAudienceCabinet] = useState('')
  const [critCommentaire, setCritCommentaire] = useState('')
  const [minutesNonNumerisees, setMinutesNonNumerisees] = useState(false)
  const [afficherTout, setAfficherTout] = useState(false)

  const [recherche, setRecherche] = useState('')
  const [critNature, setCritNature] = useState('')
  const [critEtat, setCritEtat] = useState('')
  const [sansDecision, setSansDecision] = useState(false)
  const [historique, setHistorique] = useState(false)
  const [action, setAction] = useState<string | null>(null)
  const [selection, setSelection] = useState<DocGed | null>(null)

  let q = Docs
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    q = q.filter((d) => contientSansCasse(d.instance, t) || contientSansCasse(d.decision, t))
  }
  if (critNature !== '') q = q.filter((d) => d.nature === critNature)
  if (critEtat !== '') q = q.filter((d) => d.etat === critEtat)
  if (sansDecision) q = q.filter((d) => d.dateDecision === null)
  const trouves = q

  const s = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Documents de la GED</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Instance / decision</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nature du document</span>
          <select className="rig-saisie-champ__saisie" value={critNature} onChange={(e) => setCritNature(e.target.value)}>
            <option value="">Toutes</option>
            {distinctsTries(Docs.map((d) => d.nature)).map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Etat</span>
          <select className="rig-saisie-champ__saisie" value={critEtat} onChange={(e) => setCritEtat(e.target.value)}>
            <option value="">Tous</option>
            <option value="Brouillon">Brouillon</option>
            <option value="Signe">Signe</option>
            <option value="Notifie">Notifie</option>
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date de reception</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date" value={receptionDebut}
                     onChange={(e) => setReceptionDebut(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date" value={receptionFin}
                     onChange={(e) => setReceptionFin(e.target.value)} />
            </label>
          </div>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Audience / cabinet</span>
          <input className="rig-saisie-champ__saisie" value={audienceCabinet} onChange={(e) => setAudienceCabinet(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Commentaire</span>
          <input className="rig-saisie-champ__saisie" value={critCommentaire} onChange={(e) => setCritCommentaire(e.target.value)} />
        </label>

        {/* v2 : ult_chb_afficher_minute_non_num et ult_chb_afficher_tout. */}
        <button className={`rig-bascule${minutesNonNumerisees ? ' rig-bascule--actif' : ''}`}
                onClick={() => setMinutesNonNumerisees(!minutesNonNumerisees)}>
          <span className="rig-bascule__temoin"></span>
          <span>Afficher les minutes non numerisees</span>
        </button>

        <button className={`rig-bascule${afficherTout ? ' rig-bascule--actif' : ''}`}
                onClick={() => setAfficherTout(!afficherTout)}>
          <span className="rig-bascule__temoin"></span>
          <span>Afficher tout</span>
        </button>

        <button className={`rig-bascule${sansDecision ? ' rig-bascule--actif' : ''}`}
                onClick={() => setSansDecision(!sansDecision)}>
          <span className="rig-bascule__temoin"></span>
          <span>Non lies a une decision</span>
        </button>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={() => setAction('Inserer un document')}>Inserer un document</button>{' '}
          <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Lier a une decision judiciaire')}>Lier a une decision</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Decisions judiciaires et documents lies</div>
          <span className="rig-resultats-proc__compte">{trouves.length} document(s){action === null ? '' : ' — ' + action}</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Decision judiciaire</th>
                <th>Numero minute</th>
                <th>Instance</th>
                <th>Nature du document</th>
                <th>Date Decision</th>
                <th>Date prevue de la decision</th>
                <th className="rig-table__centre">Nb docs lies</th>
                <th>Etat</th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((doc) => (
                <tr key={doc.instance} onClick={() => setSelection(doc)}>
                  <td>{doc.decision}</td>
                  <td className="rig-table__mono">{doc.minute}</td>
                  <td className="rig-table__mono">{doc.instance}</td>
                  <td>{doc.nature}</td>
                  <td className="rig-table__mono">{doc.dateDecision ? dateCourte(doc.dateDecision) : ''}</td>
                  <td className="rig-table__mono">{dateCourte(doc.datePrevue)}</td>
                  <td className="rig-table__centre">{doc.nbLies}</td>
                  <td><span className={`rig-etat rig-etat--${doc.statut}`}>{doc.etat}</span></td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr><td colSpan={8} className="rig-table__vide">Aucun document ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {s && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-kicker">{s.decision} — minute {s.minute}</div>
            <div className="rig-criteres__actions">
              <button className="rig-bouton" onClick={() => ouvrirDossier(s.dossier)}>Afficher le document</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Imprimer')}>Imprimer</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Envoyer par e-mail')}>Envoyer par e-mail</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Envoyer par courrier')}>Envoyer par courrier</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setHistorique(!historique)}>Historique</button>
            </div>
            {historique && (
              <ul className="rig-historique__liste">
                {s.historiques.map((h, i) => <li key={i}>{h}</li>)}
              </ul>
            )}
          </div>
        )}
      </section>
    </div>
  )
}
