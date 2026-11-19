// Ecran PROC_TELETRANS : le suivi des flux TELETRANSMIS (EDI) recus au
// greffe. Destination de l'alerte « Teletransmission » de l'accueil. Les lignes
// viennent du courrier arrive (JeuDemo, table ARRIVEE), calculees par
// Pont/EcranTeletransmission.cs : un flux EDI est une arrivee de mode « EDI ».
//
// Porte depuis Components/Ecrans/EcranTeletransmission.razor (lot 3) : meme
// balisage, memes classes CSS. La coquille .razor monte cet ecran et recoit
// ouvrirDossier.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'

export interface LigneFlux {
  numero: string
  horodatage: string
  modeLisible: string
  nom: string
  references: string
  numDemande: string
  numerise: boolean
}

export interface VueTeletransmission {
  flux: LigneFlux[]
  /** « dernier flux : 09/09/2026 09:12 », ou le conseil quand rien n'est arrive. */
  dernier: string
}

export interface ProprietesEcranTeletransmission {
  /** Remonte le numero de l'affaire rattachee au flux clique. */
  ouvrirDossier: (numero: string) => void
}

export function EcranTeletransmission({ ouvrirDossier }: ProprietesEcranTeletransmission) {
  const [mode, setMode] = useState('EDI')
  // null tant que le pont n'a pas repondu : ni « Canal au vert » ni « Aucun
  // flux recu », qui seraient faux pendant cet instant.
  const [vue, setVue] = useState<VueTeletransmission | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueTeletransmission>('EcranTeletransmission.Flux', mode)
      .then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [mode])

  const flux = vue?.flux ?? []

  /** Un flux sans demande rattachee ne mene nulle part. */
  function aller(f: LigneFlux) {
    if (f.numDemande.trim() === '') return
    ouvrirDossier(f.numDemande)
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Flux recus</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Mode</span>
          <select className="rig-saisie-champ__saisie" value={mode} onChange={(e) => setMode(e.target.value)}>
            <option value="EDI">EDI (teletransmission)</option>
            <option value="TOUS">Tous les modes</option>
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Etat du canal</div>
          {vue && (
            <div className="rig-alerte">
              <span className={`rig-filet-v rig-filet-v--${flux.length > 0 ? 'ok' : 'action'}`}></span>
              <span className="rig-alerte__corps">
                <span className="rig-alerte__titre">{flux.length > 0 ? 'Canal au vert' : 'Aucun flux recu'}</span><br />
                <span className="rig-alerte__detail">{vue.dernier}</span>
              </span>
            </div>
          )}
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Teletransmissions</div>
          <span className="rig-resultats-proc__compte">{vue ? `${flux.length} flux` : ''}</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Arrivee</th>
                <th>Recu le</th>
                <th>Mode</th>
                <th>Emetteur</th>
                <th>Reference</th>
                <th>Demande</th>
                <th>Numerise</th>
              </tr>
            </thead>
            <tbody>
              {flux.map((f, i) => (
                <tr key={`${i}:${f.numero}`} onClick={() => aller(f)}
                    title={f.numDemande.length > 0 ? `Ouvrir l'affaire ${f.numDemande}` : 'Aucune affaire rattachee'}>
                  <td className="rig-table__mono">{f.numero}</td>
                  <td className="rig-table__mono">{f.horodatage}</td>
                  <td>{f.modeLisible}</td>
                  <td>{f.nom}</td>
                  <td className="rig-table__mono">{f.references}</td>
                  <td className="rig-table__mono">{f.numDemande}</td>
                  <td><span className={`rig-etat rig-etat--${f.numerise ? 'ok' : 'attente'}`}>{f.numerise ? 'oui' : 'non'}</span></td>
                </tr>
              ))}
              {vue && flux.length === 0 && (
                <tr><td colSpan={7} className="rig-table__vide">Aucun flux pour ce mode.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
