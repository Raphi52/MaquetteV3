// Ecran de la procedure BODACC.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/BODACC/PROC_ENV_BOD) :
// v2 a DEUX grilles distinctes, pas une, avec des colonnes differentes
// (HeaderText releves dans Controls/) :
//
//   - Controls/BodaccToSendRigDataGridView.cs (« a envoyer ») :
//     Num PC ou prevention, Code eve pro, Date eve pro, Texte Bodacc,
//     Date prevue d envoi, Type annonce
//   - Controls/BodaccAlreadySentRigDataGridView.cs (« deja envoye ») :
//     Num PC ou prevention, Code eve pro, Date eve pro, Date envoi,
//     Date trans., Num bord., Num fact., Date parution, Num parution,
//     Num ann., Type annonce, Texte Bodacc
//
// Criteres : Operations/OPE_RECH_BOD.Designer.cs -> TextRechByNum,
// TextNumBordereau, TextNumFacture, DateDu, DateAu.
//
// Porte depuis Components/Ecrans/EcranBodacc.razor (lot 5) : meme balisage,
// memes classes CSS. La date du jour, qui dit si une annonce a envoyer est en
// retard, vient de Pont/EcranBodacc.cs ; le reste etait declare dans l'ecran
// et l'a suivi. La coquille .razor monte cet ecran et recoit ouvrirDossier.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'

interface Annonce {
  numAnnonce: string
  numPc: string
  typeAnnonce: string
  codeEvePro: string
  /** Les dates sont au format aaaa-mm-jj ; null = pas encore arrive. */
  dateEvePro: string
  prevueEnvoi: string
  envoi: string | null
  numParution: string
  parution: string | null
  numBord: string
  numFact: string
  texte: string
  dossier: string
  transmission: string | null
}

const Lignes: Annonce[] = [
  { numAnnonce: 'A2026-1188', numPc: 'PC2026-041', typeAnnonce: 'Immatriculation', codeEvePro: 'IMM', dateEvePro: '2026-09-02', prevueEnvoi: '2026-09-08', envoi: null, numParution: '', parution: null, numBord: '', numFact: '', texte: 'Immatriculation SARL LES TILLEULS', dossier: '2026B00811', transmission: null },
  { numAnnonce: 'A2026-1189', numPc: '', typeAnnonce: 'Modification', codeEvePro: 'MOD', dateEvePro: '2026-09-03', prevueEnvoi: '2026-09-09', envoi: null, numParution: '', parution: null, numBord: '', numFact: '', texte: 'Changement de gerant', dossier: '2019B00233', transmission: null },
  { numAnnonce: 'A2026-1190', numPc: 'PC2026-042', typeAnnonce: 'Procedure collective', codeEvePro: 'PCO', dateEvePro: '2026-09-01', prevueEnvoi: '2026-09-04', envoi: null, numParution: '', parution: null, numBord: '', numFact: '', texte: 'Ouverture de redressement', dossier: '2026B00790', transmission: null },
  { numAnnonce: 'A2026-1150', numPc: '', typeAnnonce: 'Radiation', codeEvePro: 'RAD', dateEvePro: '2026-08-12', prevueEnvoi: '2026-08-18', envoi: '2026-08-18', numParution: '2026-171', parution: '2026-08-24', numBord: 'BD-2610', numFact: 'FA-45120', texte: 'Radiation EURL LE PETIT CAFE', dossier: '2004B01344', transmission: '2026-08-19' },
  { numAnnonce: 'A2026-1151', numPc: '', typeAnnonce: 'Depot des comptes', codeEvePro: 'DCA', dateEvePro: '2026-08-14', prevueEnvoi: '2026-08-20', envoi: '2026-08-20', numParution: '2026-172', parution: '2026-08-26', numBord: 'BD-2610', numFact: 'FA-45121', texte: 'Depot des comptes 2025', dossier: '2011B00512', transmission: '2026-08-21' },
]

/** Les annonces de l'ecran, que la palette Ctrl K cherche aussi (composants/Palette.tsx). */
export { Lignes as AnnoncesBodacc, type Annonce as AnnonceBodacc }

const date = (d: string | null): string => (d === null ? '' : dateCourte(d))

export interface ProprietesEcranBodacc {
  /** Remonte le NUMERO de dossier de l'annonce cliquee : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
}

export function EcranBodacc({ ouvrirDossier }: ProprietesEcranBodacc) {
  const [aEnvoyer, setAEnvoyer] = useState(true)
  const [critNumBord, setCritNumBord] = useState('')
  const [critNumFact, setCritNumFact] = useState('')

  const [critTexte, setCritTexte] = useState('')
  const [critType, setCritType] = useState('')
  const [critDu, setCritDu] = useState('')
  const [critAu, setCritAu] = useState('')
  // null tant que le pont n'a pas repondu : sans elle, on ne sait pas quelle
  // annonce est en retard, et les lignes attendent plutot que de le taire.
  const [aujourdhui, setAujourdhui] = useState<string | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranBodacc.DateDuJour').then((d) => { if (!perime) setAujourdhui(d) })
    return () => { perime = true }
  }, [])

  function effacer() {
    setCritTexte(''); setCritType('')
    setCritDu(''); setCritAu('')
  }

  let q = Lignes.filter((l) => (l.envoi === null) === aEnvoyer)
  if (!estBlanc(critTexte)) {
    const t = critTexte.trim()
    q = q.filter((l) => contientSansCasse(l.numAnnonce, t) || contientSansCasse(l.dossier, t))
  }
  if (critType !== '') q = q.filter((l) => l.typeAnnonce === critType)
  if (critDu !== '') q = q.filter((l) => l.prevueEnvoi >= critDu)
  if (critAu !== '') q = q.filter((l) => l.prevueEnvoi <= critAu)
  const trouvees = q

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Recherche BODACC</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Num annonce / dossier</span>
          <input className="rig-saisie-champ__saisie" placeholder="num ann. ou dossier"
                 value={critTexte} onChange={(e) => setCritTexte(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type d'annonce</span>
          <select className="rig-saisie-champ__saisie" value={critType} onChange={(e) => setCritType(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(Lignes.map((l) => l.typeAnnonce)).map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date prevue d'envoi</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date" value={critDu} onChange={(e) => setCritDu(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date" value={critAu} onChange={(e) => setCritAu(e.target.value)} />
            </label>
          </div>
        </div>

        {/* v2 : TextNumBordereau et TextNumFacture (OPE_RECH_BOD). */}
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Num bordereau</span>
          <input className="rig-saisie-champ__saisie" value={critNumBord} onChange={(e) => setCritNumBord(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Num facture</span>
          <input className="rig-saisie-champ__saisie" value={critNumFact} onChange={(e) => setCritNumFact(e.target.value)} />
        </label>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-onglets">
            <button className={`rig-onglet${aEnvoyer ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setAEnvoyer(true)}>BODACC a envoyer</button>
            <button className={`rig-onglet${!aEnvoyer ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setAEnvoyer(false)}>BODACC deja envoye</button>
          </div>
          <span className="rig-resultats-proc__compte">{trouvees.length} annonce(s)</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              {aEnvoyer ? (
                // Colonnes de BodaccToSendRigDataGridView.cs (RIG v2).
                <tr>
                  <th>Num PC ou prevention</th>
                  <th>Code eve pro</th>
                  <th>Date eve pro</th>
                  <th>Texte Bodacc</th>
                  <th>Date prevue d'envoi</th>
                  <th>Type annonce</th>
                </tr>
              ) : (
                // Colonnes de BodaccAlreadySentRigDataGridView.cs (RIG v2).
                <tr>
                  <th>Num PC ou prevention</th>
                  <th>Code eve pro</th>
                  <th>Date eve pro</th>
                  <th>Date envoi</th>
                  <th>Date trans.</th>
                  <th>Num bord.</th>
                  <th>Num fact.</th>
                  <th>Date parution</th>
                  <th>Num parution</th>
                  <th>Num ann.</th>
                  <th>Type annonce</th>
                  <th>Texte Bodacc</th>
                </tr>
              )}
            </thead>
            <tbody>
              {aujourdhui !== null && trouvees.map((ligne) => {
                const retard = aEnvoyer && ligne.prevueEnvoi < aujourdhui
                return (
                  <tr key={ligne.numAnnonce} className={retard ? 'rig-table__ligne--alerte' : ''}
                      onClick={() => ouvrirDossier(ligne.dossier)}>
                    {aEnvoyer ? (
                      <>
                        <td className="rig-table__mono">{ligne.numPc}</td>
                        <td className="rig-table__mono">{ligne.codeEvePro}</td>
                        <td className="rig-table__mono">{dateCourte(ligne.dateEvePro)}</td>
                        <td>{ligne.texte}</td>
                        <td className="rig-table__mono">{dateCourte(ligne.prevueEnvoi)}</td>
                        <td>{ligne.typeAnnonce}</td>
                      </>
                    ) : (
                      <>
                        <td className="rig-table__mono">{ligne.numPc}</td>
                        <td className="rig-table__mono">{ligne.codeEvePro}</td>
                        <td className="rig-table__mono">{dateCourte(ligne.dateEvePro)}</td>
                        <td className="rig-table__mono">{date(ligne.envoi)}</td>
                        <td className="rig-table__mono">{date(ligne.transmission)}</td>
                        <td className="rig-table__mono">{ligne.numBord}</td>
                        <td className="rig-table__mono">{ligne.numFact}</td>
                        <td className="rig-table__mono">{date(ligne.parution)}</td>
                        <td className="rig-table__mono">{ligne.numParution}</td>
                        <td className="rig-table__mono">{ligne.numAnnonce}</td>
                        <td>{ligne.typeAnnonce}</td>
                        <td>{ligne.texte}</td>
                      </>
                    )}
                  </tr>
                )
              })}
              {aujourdhui !== null && trouvees.length === 0 && (
                <tr><td colSpan={aEnvoyer ? 6 : 12} className="rig-table__vide">Aucune annonce ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
