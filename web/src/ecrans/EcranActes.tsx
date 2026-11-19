// Ecran de la procedure PROC_ACTES_NUM.
// Criteres lus dans RIG v2 : OPE_CRITERES ("N Dossier / N Depot",
// "Abrege piece"), OPE_DATE ("Date de depot" Du/Au), OPE_RECHERCHE
// ("Type de depot", "Lister actes non numerisables").
// Colonnes : RESULTAT/DataGridviewActes.cs.
//
// Porte depuis Components/Ecrans/EcranActes.razor (lot 3) : meme balisage,
// memes classes CSS. Le filtre est fait par Pont/EcranActes.cs sur
// JeuDemo.Actes ; la coquille .razor monte cet ecran et recoit ouvrirDossier.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { dateSaisie, useNombreSaisi } from '../saisie'

export interface OptionActes {
  code: string
  libelle: string
}

export interface LigneActe {
  rang: number
  dossier: string
  depot: string
  denomination: string
  categorieLisible: string
  abrege: string
  description: string
  depose: string
  acte: string
  cloture: string
  confidentiel: boolean
  demat: boolean
  anomalie: string
  numerisable: boolean
}

export interface VueActes {
  categories: OptionActes[]
  lignes: LigneActe[]
}

export interface ProprietesEcranActes {
  /** Remonte le numero de dossier a ouvrir. */
  ouvrirDossier: (numero: string) => void
}

export function EcranActes({ ouvrirDossier }: ProprietesEcranActes) {
  const [critActeCritere, setCritActeCritere] = useState('')
  const [critAbrege, setCritAbrege] = useState('')
  const [critTypeDepot, setCritTypeDepot] = useState('')
  const [critDu, setCritDu] = useState('')
  const [critAu, setCritAu] = useState('')
  const [critNonNumerisables, setCritNonNumerisables] = useState(false)
  const [critAnomalies, setCritAnomalies] = useState(false)
  const topMax = useNombreSaisi(50)
  // null tant que le pont n'a pas repondu : ni ligne ni « aucun acte ».
  const [vue, setVue] = useState<VueActes | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueActes>('EcranActes.Trouver', {
      critActeCritere, critAbrege, critTypeDepot,
      critDu: dateSaisie(critDu), critAu: dateSaisie(critAu),
      critNonNumerisables, critAnomalies, critTopMax: topMax.valeur,
    }).then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [critActeCritere, critAbrege, critTypeDepot, critDu, critAu, critNonNumerisables, critAnomalies, topMax.valeur])

  const trouves = vue?.lignes ?? []

  /**
   * Un acte numerise mene au dossier qui le porte. Un acte NON
   * numerisable n a pas d image a montrer : la ligne reste inerte.
   */
  function ouvrir(a: LigneActe) {
    if (!a.numerisable) return
    ouvrirDossier(a.dossier)
  }

  function effacer() {
    setCritActeCritere('')
    setCritAbrege('')
    setCritTypeDepot('')
    setCritDu('')
    setCritAu('')
    setCritNonNumerisables(false)
    setCritAnomalies(false)
    topMax.poser(50)
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Critere</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">N Dossier / N Depot</span>
          <input className="rig-saisie-champ__saisie" placeholder="0000, D-2026-1188, denomination"
                 value={critActeCritere} onChange={(e) => setCritActeCritere(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Abrege piece</span>
          <input className="rig-saisie-champ__saisie" placeholder="STAT, BILAN, PVAG"
                 value={critAbrege} onChange={(e) => setCritAbrege(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type de depot</span>
          <select className="rig-saisie-champ__saisie" value={critTypeDepot} onChange={(e) => setCritTypeDepot(e.target.value)}>
            <option value="">Tous</option>
            {(vue?.categories ?? []).map((c) => (
              <option key={c.code} value={c.code}>{c.libelle}</option>
            ))}
          </select>
          <span className="rig-saisie-champ__colonne">DPTRC_CATEGORIE_DEPOT · dbo.CATEGORIE_DEPOT</span>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date de depot</div>
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

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${critNonNumerisables ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritNonNumerisables(!critNonNumerisables)}>
            <span className="rig-bascule__temoin"></span>
            <span>Lister actes non numerisables</span>
          </button>
          <button className={`rig-bascule${critAnomalies ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritAnomalies(!critAnomalies)}>
            <span className="rig-bascule__temoin"></span>
            <span>Avec anomalie seulement</span>
          </button>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nombre maxi. d actes affiches</span>
          <input className="rig-saisie-champ__saisie" type="number" min="1" max="500"
                 value={topMax.texte} onChange={(e) => topMax.saisir(e.target.value)} />
        </label>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Actes numerises</div>
          <span className="rig-resultats-proc__compte">
            {/* Memes morceaux de texte que Razor (chaque nombre, puis son libelle) :
                une seule chaine decalait le lissage des lettres (web/README.md). */}
            {vue && <>
              {trouves.length}{' affiche(s) · '}
              {trouves.filter((a) => !a.demat).length}{' sans image · '}
              {trouves.filter((a) => a.anomalie !== '').length}{' anomalie(s)'}
            </>}
          </span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th className="rig-table__centre">N</th>
                <th>N Dossier</th>
                <th>N Depot</th>
                <th>Denomination</th>
                <th>Categorie de depot</th>
                <th>Abrege</th>
                <th>Description</th>
                <th>Date de depot</th>
                <th>Date de l acte</th>
                <th>Date de cloture</th>
                <th className="rig-table__centre">Confid.</th>
                <th className="rig-table__centre">Demat.</th>
                <th>Anomalies</th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((a, i) => {
                const classe = [a.anomalie === '' ? '' : 'rig-table__ligne--alerte', a.numerisable ? '' : 'rig-table__ligne--inerte']
                  .filter((c) => c !== '').join(' ')
                return (
                  <tr key={`${i}:${a.rang}`} className={classe || undefined}
                      title={a.numerisable ? `Ouvrir le dossier ${a.dossier}` : `Acte non numerisable : ${a.anomalie}`}
                      onClick={() => ouvrir(a)}>
                    <td className="rig-table__centre rig-table__mono">{a.rang}</td>
                    <td className="rig-table__mono">{a.dossier}</td>
                    <td className="rig-table__mono">{a.depot}</td>
                    <td>{a.denomination}</td>
                    <td><span className="rig-etat">{a.categorieLisible}</span></td>
                    <td className="rig-table__mono">{a.abrege}</td>
                    <td>{a.description}</td>
                    <td className="rig-table__mono">{a.depose}</td>
                    <td className="rig-table__mono">{a.acte}</td>
                    <td className="rig-table__mono">{a.cloture}</td>
                    <td className="rig-table__centre">{a.confidentiel ? 'oui' : ''}</td>
                    <td className="rig-table__centre">{a.demat ? 'oui' : ''}</td>
                    <td>
                      {a.anomalie === '' ? <span className="rig-table__sans">aucune</span> : a.anomalie}
                    </td>
                  </tr>
                )
              })}
              {vue && trouves.length === 0 && (
                <tr>
                  <td colSpan={13} className="rig-table__vide">
                    Aucun acte ne repond a ces criteres.
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
