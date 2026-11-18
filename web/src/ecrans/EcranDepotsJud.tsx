// Ecran de la procedure PROC_DEPOTJUD.
//
// Etapes lues dans Processus/PROC_DEPOTJUD.Designer.cs (RIG v2), avec le
// libelle que v2 leur donne :
//   ETP_INFO_DEMANDE               « Demande »
//   ETP_INSTANCE_RECHERCHE         « Etape de recherche d'instance »
//   ETP_ENTITE_JUDICIAIRE_READONLY « Entite judiciaire (lecture seule) »
//   ETP_INSTANCE_SUMMARY           « Informations sur l'instance »
//   ETP_EVENEMENT_SURVENU          « Nouvel evenement survenu »
//   ETP_FACTU_GESTCOM              « Facturation et gestion commerciale »
//   ETP_POST_VAL_INSTANCE          « Recapitulatif instance »
// La maquette garde cet enchainement.
//
// Porte depuis Components/Ecrans/EcranDepotsJud.razor (lot 5) : meme balisage,
// memes classes CSS. La date du jour (valeur de depart de l'evenement) vient
// de Pont/EcranDepotsJud.cs ; le reste etait declare dans l'ecran et l'a suivi.
// La coquille .razor monte cet ecran et recoit ouvrirDossier.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, dateCourte, estBlanc } from '../criteres'

interface Etape {
  code: string
  libelle: string
}

interface Instance {
  numero: string
  denomination: string
  nature: string
  chambre: string
  /** aaaa-mm-jj */
  ouverture: string
  dernierEvenement: string
  etat: string
  statut: string
  aFacturer: boolean
  dossier: string
}

const Etapes: Etape[] = [
  { code: 'ETP_INFO_DEMANDE', libelle: 'Demande' },
  { code: 'ETP_INSTANCE_RECHERCHE', libelle: 'Etape de recherche d\'instance' },
  { code: 'ETP_ENTITE_JUDICIAIRE_READONLY', libelle: 'Entite judiciaire (lecture seule)' },
  { code: 'ETP_INSTANCE_SUMMARY', libelle: 'Informations sur l\'instance' },
  { code: 'ETP_EVENEMENT_SURVENU', libelle: 'Nouvel evenement survenu' },
  { code: 'ETP_FACTU_GESTCOM', libelle: 'Facturation et gestion commerciale' },
  { code: 'ETP_POST_VAL_INSTANCE', libelle: 'Recapitulatif instance' },
]

const Lignes: Instance[] = [
  { numero: '2026J00121', denomination: 'SARL BOULANGERIE DUVAL', nature: 'Redressement judiciaire', chambre: '1re', ouverture: '2026-03-04', dernierEvenement: 'Jugement d\'ouverture', etat: 'En cours', statut: 'action', aFacturer: true, dossier: '2024B00318' },
  { numero: '2026J00134', denomination: 'SAS TRANSPORTS MARNE', nature: 'Liquidation judiciaire', chambre: '2e', ouverture: '2026-05-19', dernierEvenement: 'Designation d\'expert', etat: 'En cours', statut: 'attente', aFacturer: false, dossier: '2024B00407' },
  { numero: '2025J00088', denomination: 'EURL COIFFURE MARIE', nature: 'Sauvegarde', chambre: '1re', ouverture: '2025-11-02', dernierEvenement: 'Ordonnance', etat: 'Suspendue', statut: 'alerte', aFacturer: true, dossier: '2023B01120' },
  { numero: '2024J00507', denomination: 'SCI DU MOULIN', nature: 'Redressement judiciaire', chambre: '3e', ouverture: '2024-06-12', dernierEvenement: 'Cloture', etat: 'Close', statut: 'ok', aFacturer: false, dossier: '2025B00044' },
]

export interface ProprietesEcranDepotsJud {
  /** Remonte le NUMERO de dossier de l'instance cliquee : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
}

export function EcranDepotsJud({ ouvrirDossier }: ProprietesEcranDepotsJud) {
  const [etapeCourante, setEtapeCourante] = useState('ETP_INSTANCE_RECHERCHE')
  const [numInstance, setNumInstance] = useState('')
  const [denomination, setDenomination] = useState('')
  const [evenement, setEvenement] = useState('JUG')
  // null tant que le pont n'a pas repondu (ou une fois la date effacee).
  const [dateEvenement, setDateEvenement] = useState<string | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranDepotsJud.DateDuJour')
      .then((d) => { if (!perime) setDateEvenement((avant) => avant ?? d) })
    return () => { perime = true }
  }, [])

  let q = Lignes
  if (!estBlanc(numInstance)) q = q.filter((l) => contientSansCasse(l.numero, numInstance.trim()))
  if (!estBlanc(denomination)) q = q.filter((l) => contientSansCasse(l.denomination, denomination.trim()))
  if (etapeCourante === 'ETP_FACTU_GESTCOM') q = q.filter((l) => l.aFacturer)
  const trouvees = q

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Etapes du depot</div>

        {Etapes.map((etape) => (
          <button key={etape.code} className={`rig-bascule${etapeCourante === etape.code ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setEtapeCourante(etape.code)}>
            <span className="rig-bascule__temoin"></span>
            <span>{etape.libelle}</span>
          </button>
        ))}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Instance</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">N d'instance</span>
            <input className="rig-saisie-champ__saisie" value={numInstance} onChange={(e) => setNumInstance(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Denomination</span>
            <input className="rig-saisie-champ__saisie" value={denomination} onChange={(e) => setDenomination(e.target.value)} />
          </label>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">{Etapes.find((e) => e.code === etapeCourante)!.libelle}</div>
          <span className="rig-resultats-proc__compte">{trouvees.length} instance(s)</span>
        </div>

        {etapeCourante === 'ETP_EVENEMENT_SURVENU' && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Evenement survenu</span>
              <select className="rig-saisie-champ__saisie" value={evenement} onChange={(e) => setEvenement(e.target.value)}>
                <option value="JUG">Jugement</option>
                <option value="ORD">Ordonnance</option>
                <option value="EXP">Designation d'expert</option>
                <option value="CLO">Cloture</option>
              </select>
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Date de l'evenement</span>
              <input className="rig-saisie-champ__saisie" type="date" value={dateEvenement ?? ''}
                     onChange={(e) => setDateEvenement(e.target.value || null)} />
            </label>
          </div>
        )}

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>N Instance</th>
                <th>Denomination</th>
                <th>Nature</th>
                <th>Chambre</th>
                <th>Date d'ouverture</th>
                <th>Dernier evenement</th>
                <th>Etat</th>
                <th className="rig-table__centre">A facturer</th>
              </tr>
            </thead>
            <tbody>
              {trouvees.map((inst) => (
                <tr key={inst.numero} onClick={() => ouvrirDossier(inst.dossier)}>
                  <td className="rig-table__mono">{inst.numero}</td>
                  <td>{inst.denomination}</td>
                  <td>{inst.nature}</td>
                  <td className="rig-table__mono">{inst.chambre}</td>
                  <td className="rig-table__mono">{dateCourte(inst.ouverture)}</td>
                  <td>{inst.dernierEvenement}</td>
                  <td><span className={`rig-etat rig-etat--${inst.statut}`}>{inst.etat}</span></td>
                  <td className="rig-table__centre">{inst.aFacturer ? 'oui' : ''}</td>
                </tr>
              ))}
              {trouvees.length === 0 && (
                <tr><td colSpan={8} className="rig-table__vide">Aucune instance ne repond a cette recherche.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
