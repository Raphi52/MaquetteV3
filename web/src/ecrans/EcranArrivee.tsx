// Ecran de la procedure PROC_ARRIVEE.
// Blocs de criteres lus dans RIG v2 : OPE_ARRIVEE.Designer.cs ("Arrivee",
// "Date d'arrivee", "Mode d'arrivee") et OPE_DEMANDE.Designer.cs
// ("N Demande", "Type de demande", "Utilisateur designe", "Numerisation",
// "Attestation de depot"). Colonnes : table ARRIVEE (ARRV_*).
//
// Porte depuis Components/Ecrans/EcranArrivee.razor (lot 3) : meme balisage,
// memes classes CSS. Le filtre est fait par Pont/EcranArrivee.cs sur
// JeuDemo.Arrivees ; la coquille .razor monte cet ecran et recoit ouvrirDossier.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { dateSaisie, useNombreSaisi } from '../saisie'

export interface OptionArrivee {
  code: string
  libelle: string
}

export interface LigneArrivee {
  numero: string
  horodatage: string
  modeLisible: string
  nom: string
  references: string
  case: string
  paiementJoint: boolean
  numDemande: string
  typeDemande: string
  utilisateur: string
  numerise: boolean
  attestation: boolean
}

export interface VueArrivee {
  modes: OptionArrivee[]
  lignes: LigneArrivee[]
  /** Toutes les arrivees connues, filtre ou pas. */
  total: number
  aNumeriser: number
}

export interface ProprietesEcranArrivee {
  /** Remonte le numero de dossier a ouvrir. */
  ouvrirDossier: (numero: string) => void
}

export function EcranArrivee({ ouvrirDossier }: ProprietesEcranArrivee) {
  const [critNumArrivee, setCritNumArrivee] = useState('')
  const [critNomArrivee, setCritNomArrivee] = useState('')
  const [critReferences, setCritReferences] = useState('')
  const [critMode, setCritMode] = useState('')
  const [critCase, setCritCase] = useState('')
  const [critDu, setCritDu] = useState('')
  const [critAu, setCritAu] = useState('')
  const [critPaiement, setCritPaiement] = useState(false)
  const [critNonNumerise, setCritNonNumerise] = useState(false)
  const topMax = useNombreSaisi(50)
  // null tant que le pont n'a pas repondu : ni ligne ni « aucune arrivee ».
  const [vue, setVue] = useState<VueArrivee | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueArrivee>('EcranArrivee.Trouver', {
      critNumArrivee, critNomArrivee, critReferences, critMode, critCase,
      critDu: dateSaisie(critDu), critAu: dateSaisie(critAu),
      critPaiement, critNonNumerise, critTopMax: topMax.valeur,
    }).then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [critNumArrivee, critNomArrivee, critReferences, critMode, critCase, critDu, critAu,
      critPaiement, critNonNumerise, topMax.valeur])

  const trouvees = vue?.lignes ?? []

  /**
   * Une arrivee mene a SA demande. Toutes n en ont pas : un pli peut
   * arriver avant qu une demande soit creee (colis, navette interne).
   */
  function ouvrir(a: LigneArrivee) {
    if (a.numDemande.trim() === '') return
    ouvrirDossier(a.numDemande)
  }

  function effacer() {
    setCritNumArrivee('')
    setCritNomArrivee('')
    setCritReferences('')
    setCritMode('')
    setCritCase('')
    setCritDu('')
    setCritAu('')
    setCritPaiement(false)
    setCritNonNumerise(false)
    topMax.poser(50)
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Arrivee</span>
          <input className="rig-saisie-champ__saisie" placeholder="n arrivee ou n demande"
                 value={critNumArrivee} onChange={(e) => setCritNumArrivee(e.target.value)} />
          <span className="rig-saisie-champ__colonne">ARRV_NUM_ARRIVEE</span>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nom</span>
          <input className="rig-saisie-champ__saisie" placeholder="expediteur"
                 value={critNomArrivee} onChange={(e) => setCritNomArrivee(e.target.value)} />
          <span className="rig-saisie-champ__colonne">ARRV_NOM</span>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">References</span>
          <input className="rig-saisie-champ__saisie" placeholder="n de suivi, reference"
                 value={critReferences} onChange={(e) => setCritReferences(e.target.value)} />
          <span className="rig-saisie-champ__colonne">ARRV_REFERENCES</span>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Mode d arrivee</span>
          <select className="rig-saisie-champ__saisie" value={critMode} onChange={(e) => setCritMode(e.target.value)}>
            <option value="">Tous</option>
            {(vue?.modes ?? []).map((m) => (
              <option key={m.code} value={m.code}>{m.libelle}</option>
            ))}
          </select>
          <span className="rig-saisie-champ__colonne">ARRV_MODE_ARRIVEE · dbo.MODE_ARRIVEE</span>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date d arrivee</div>
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

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Case courrier</span>
          <input className="rig-saisie-champ__saisie" placeholder="C-01"
                 value={critCase} onChange={(e) => setCritCase(e.target.value)} />
          <span className="rig-saisie-champ__colonne">ARRV_CASE_COURRIER</span>
        </label>

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${critPaiement ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritPaiement(!critPaiement)}>
            <span className="rig-bascule__temoin"></span>
            <span>Paiement joint</span>
          </button>
          <button className={`rig-bascule${critNonNumerise ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritNonNumerise(!critNonNumerise)}>
            <span className="rig-bascule__temoin"></span>
            <span>Reste a numeriser</span>
          </button>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nombre maxi. d arrivees affichees</span>
          <input className="rig-saisie-champ__saisie" type="number" min="1" max="500"
                 value={topMax.texte} onChange={(e) => topMax.saisir(e.target.value)} />
        </label>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Courrier arrive</div>
          <span className="rig-resultats-proc__compte">
            {vue ? `${trouvees.length} affichee(s) sur ${vue.total} · ${vue.aNumeriser} a numeriser` : ''}
          </span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>N Arrivee</th>
                <th>Date et heure</th>
                <th>Mode</th>
                <th>Nom</th>
                <th>References</th>
                <th className="rig-table__centre">Case</th>
                <th className="rig-table__centre">Paiement</th>
                <th>N Demande</th>
                <th>Type de demande</th>
                <th>Utilisateur designe</th>
                <th className="rig-table__centre">Numerisation</th>
                <th className="rig-table__centre">Attestation</th>
              </tr>
            </thead>
            <tbody>
              {trouvees.map((a, i) => {
                const liee = a.numDemande.trim() !== ''
                const classe = [a.numerise ? '' : 'rig-table__ligne--alerte', liee ? '' : 'rig-table__ligne--inerte']
                  .filter((c) => c !== '').join(' ')
                return (
                  <tr key={`${i}:${a.numero}`} className={classe || undefined}
                      title={liee ? `Ouvrir la demande ${a.numDemande}` : 'Aucune demande rattachee a cette arrivee'}
                      onClick={() => ouvrir(a)}>
                    <td className="rig-table__mono">{a.numero}</td>
                    <td className="rig-table__mono">{a.horodatage}</td>
                    <td><span className="rig-etat">{a.modeLisible}</span></td>
                    <td>{a.nom}</td>
                    <td className="rig-table__mono">{a.references}</td>
                    <td className="rig-table__centre rig-table__mono">{a.case}</td>
                    <td className="rig-table__centre">{a.paiementJoint ? 'oui' : ''}</td>
                    <td className="rig-table__mono">
                      {liee ? a.numDemande : <span className="rig-table__sans">non rattachee</span>}
                    </td>
                    <td>{a.typeDemande}</td>
                    <td>{a.utilisateur}</td>
                    <td className="rig-table__centre">{a.numerise ? 'faite' : 'a faire'}</td>
                    <td className="rig-table__centre">{a.attestation ? 'oui' : ''}</td>
                  </tr>
                )
              })}
              {vue && trouvees.length === 0 && (
                <tr>
                  <td colSpan={12} className="rig-table__vide">
                    Aucune arrivee ne repond a ces criteres.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
