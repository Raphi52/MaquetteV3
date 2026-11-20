// Ecran de la procedure PROC_CJUD (RIG v2 : consultation judiciaire,
// « Enrolement de toute affaire » + bloc CJUDINFOG). Les champs sont les
// controles v2 : ULT_COMBO_ETAT_DEMAT, ult_ComboBoxEtatTraitement,
// ult_ComboBoxTypeRejet, ult_DateCreation, ult_DateTraitement,
// ult_LabelNbPiece, ult_TextMultiLineAnomalies, ult_TextMultiLineMotifRejet,
// et les boutons ult_ButtonVisuPiece / ult_ButtonvisuXML.
//
// Porte depuis Components/Ecrans/EcranConsultationJud.razor (lot 5) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// enrolements de demonstration et ses filtres sont ici, sans pont. La
// coquille .razor monte cet ecran et recoit ouvrirDossier.

import { useState } from 'react'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'
import type { ContexteDossierVue } from '../plan/types'

interface DossierJud {
  affaire: string
  denomination: string
  etatDemat: string
  etatTraitement: string
  typeRejet: string
  /** aaaa-mm-jj */
  creation: string
  /** aaaa-mm-jj, null tant que l'affaire n'est pas traitee */
  traitement: string | null
  nbPieces: number
  anomalies: string
  motifRejet: string
  xml: string
  dossier: string
}

const Dossiers: DossierJud[] = [
  { affaire: '2026J00121', denomination: 'SARL BOULANGERIE DUVAL', etatDemat: 'Dematerialise', etatTraitement: 'Traite', typeRejet: '', creation: '2026-03-04', traitement: '2026-03-06', nbPieces: 12, anomalies: '', motifRejet: '', xml: '<enrolement num="2026J00121" />', dossier: '2024B00318' },
  { affaire: '2026J00134', denomination: 'SAS TRANSPORTS MARNE', etatDemat: 'Partiel', etatTraitement: 'En attente', typeRejet: '', creation: '2026-05-19', traitement: null, nbPieces: 5, anomalies: 'Piece 3 illisible', motifRejet: '', xml: '<enrolement num="2026J00134" />', dossier: '2024B00407' },
  { affaire: '2026J00140', denomination: 'EURL COIFFURE MARIE', etatDemat: 'Dematerialise', etatTraitement: 'Rejete', typeRejet: 'Piece manquante', creation: '2026-06-02', traitement: '2026-06-03', nbPieces: 3, anomalies: 'Acte de saisine absent du depot', motifRejet: 'Dossier incomplet', xml: '<enrolement num="2026J00140" />', dossier: '2023B01120' },
  { affaire: '2026J00155', denomination: 'SCI DU MOULIN', etatDemat: 'Non demat.', etatTraitement: 'En attente', typeRejet: '', creation: '2026-07-21', traitement: null, nbPieces: 0, anomalies: 'Aucun flux recu', motifRejet: '', xml: '', dossier: '2025B00044' },
]

export interface ProprietesEcranConsultationJud {
  /** Remonte le NUMERO de dossier de la piece : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranConsultationJud({ ouvrirDossier, dossier = null }: ProprietesEcranConsultationJud) {
  // Ouvert depuis une affaire : cherchee par son numero dans « Affaire / dossier ».
  const [critTexte, setCritTexte] = useState(dossier?.numero ?? '')
  const [critDemat, setCritDemat] = useState('')
  const [critTraitement, setCritTraitement] = useState('')
  const [critCreationDu, setCritCreationDu] = useState('')
  const [critTraitementAu, setCritTraitementAu] = useState('')
  const [rejetsSeuls, setRejetsSeuls] = useState(false)
  const [xmlOuvert, setXmlOuvert] = useState(false)
  const [selection, setSelection] = useState<DossierJud | null>(null)

  let q = Dossiers
  if (!estBlanc(critTexte)) {
    const t = critTexte.trim()
    q = q.filter((d) => contientSansCasse(d.affaire, t) || contientSansCasse(d.denomination, t))
  }
  if (critDemat !== '') q = q.filter((d) => d.etatDemat === critDemat)
  if (critTraitement !== '') q = q.filter((d) => d.etatTraitement === critTraitement)
  if (critCreationDu !== '') q = q.filter((d) => d.creation >= critCreationDu)
  if (critTraitementAu !== '') q = q.filter((d) => d.traitement !== null && d.traitement <= critTraitementAu)
  if (rejetsSeuls) q = q.filter((d) => d.etatTraitement === 'Rejete')
  const trouves = q

  const s = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de consultation</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Affaire / dossier</span>
          <input className="rig-saisie-champ__saisie" value={critTexte} onChange={(e) => setCritTexte(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Etat de dematerialisation</span>
          <select className="rig-saisie-champ__saisie" value={critDemat} onChange={(e) => setCritDemat(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(Dossiers.map((d) => d.etatDemat)).map((e) => <option key={e} value={e}>{e}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Etat de traitement</span>
          <select className="rig-saisie-champ__saisie" value={critTraitement} onChange={(e) => setCritTraitement(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(Dossiers.map((d) => d.etatTraitement)).map((e) => <option key={e} value={e}>{e}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Dates</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Creation a partir du</span>
            <input className="rig-saisie-champ__saisie" type="date" value={critCreationDu}
                   onChange={(e) => setCritCreationDu(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Traitement jusqu'au</span>
            <input className="rig-saisie-champ__saisie" type="date" value={critTraitementAu}
                   onChange={(e) => setCritTraitementAu(e.target.value)} />
          </label>
        </div>

        <button className={`rig-bascule${rejetsSeuls ? ' rig-bascule--actif' : ''}`}
                onClick={() => setRejetsSeuls(!rejetsSeuls)}>
          <span className="rig-bascule__temoin"></span>
          <span>Rejets uniquement</span>
        </button>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Enrolements</div>
          <span className="rig-resultats-proc__compte">{trouves.length} affaire(s)</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>N Affaire</th>
                <th>Denomination</th>
                <th>Etat demat.</th>
                <th>Etat traitement</th>
                <th>Type de rejet</th>
                <th>Date creation</th>
                <th>Date traitement</th>
                <th className="rig-table__centre">Nb pieces</th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((dos) => (
                <tr key={dos.affaire} className={dos.etatTraitement === 'Rejete' ? 'rig-table__ligne--alerte' : ''}
                    onClick={() => setSelection(dos)}>
                  <td className="rig-table__mono">{dos.affaire}</td>
                  <td>{dos.denomination}</td>
                  <td>{dos.etatDemat}</td>
                  <td>{dos.etatTraitement}</td>
                  <td>{dos.typeRejet}</td>
                  <td className="rig-table__mono">{dateCourte(dos.creation)}</td>
                  <td className="rig-table__mono">{dos.traitement ? dateCourte(dos.traitement) : ''}</td>
                  <td className="rig-table__centre">{dos.nbPieces}</td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr><td colSpan={8} className="rig-table__vide">Aucune affaire ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {s && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-kicker">Detail de {s.affaire}</div>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Anomalies</span>
              <textarea className="rig-saisie-champ__saisie" rows={3} readOnly value={s.anomalies} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Motif de rejet</span>
              <textarea className="rig-saisie-champ__saisie" rows={2} readOnly value={s.motifRejet} />
            </label>
            <div className="rig-criteres__actions">
              <button className="rig-bouton" onClick={() => ouvrirDossier(s.dossier)}>Visualiser la piece</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setXmlOuvert(!xmlOuvert)}>Visualiser le XML</button>
            </div>
            {xmlOuvert && <pre className="rig-code">{s.xml}</pre>}
          </div>
        )}
      </section>
    </div>
  )
}
