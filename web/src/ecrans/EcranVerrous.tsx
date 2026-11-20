// Ecran PROC_VERROUS : les dossiers actuellement OUVERTS PAR UN TIERS.
// Destination de l'alerte « dossier(s) ouvert(s) par un tiers » de l'accueil.
// Les lignes sont CALCULEES par Pont/EcranVerrous.cs sur les demandes connues
// (JeuDemo), jamais ecrites ici : une demande encore en cours portee par un
// autre login est un verrou.
//
// Porte depuis Components/Ecrans/EcranVerrous.razor (lot 1) : meme balisage,
// memes classes CSS. La coquille .razor monte cet ecran et recoit ouvrirDossier.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import type { ContexteDossierVue } from '../plan/types'

export interface LigneVerrou {
  dossier: string
  nom: string
  statut: string
  etat: string
  utilisateur: string
  age: number
}

export interface DetenteurVerrous {
  utilisateur: string
  nombre: number
}

export interface VueVerrous {
  lignes: LigneVerrou[]
  detenteurs: DetenteurVerrous[]
}

export interface ProprietesEcranVerrous {
  /** Remonte le NUMERO clique : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranVerrous({ ouvrirDossier, dossier = null }: ProprietesEcranVerrous) {
  const [utilisateur, setUtilisateur] = useState('R. VILAIN')
  const [moiCompris, setMoiCompris] = useState(false)
  // Ouvert depuis une affaire : la liste se limite a elle (« qui la tient ? ») ;
  // la bascule, montree seulement alors, rend la liste entiere.
  const [surLeDossier, setSurLeDossier] = useState(dossier !== null)
  const filtre = surLeDossier ? dossier : null
  // null tant que le pont n'a pas repondu : on n'affiche alors ni ligne ni
  // « aucun dossier », qui serait faux pendant ce court instant.
  const [vue, setVue] = useState<VueVerrous | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueVerrous>('EcranVerrous.Verrouilles', utilisateur, moiCompris)
      .then((v) => { if (!perime) setVue(v) })
    // Une frappe rapide dans « Mon login » lance plusieurs appels : seule la
    // reponse du DERNIER etat compte, meme si une plus ancienne arrive apres.
    return () => { perime = true }
  }, [utilisateur, moiCompris])

  const tous = vue?.lignes ?? []
  const verrouilles = filtre ? tous.filter((v) => v.dossier === filtre.numero) : tous

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Verrous</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Mon login</span>
          <input className="rig-saisie-champ__saisie" value={utilisateur}
                 onChange={(e) => setUtilisateur(e.target.value)} />
        </label>

        <button className={`rig-bascule${moiCompris ? ' rig-bascule--actif' : ''}`}
                onClick={() => setMoiCompris(!moiCompris)}>
          <span className="rig-bascule__temoin"></span>
          <span>Inclure mes propres dossiers</span>
        </button>

        {dossier && (
          <button className={`rig-bascule${surLeDossier ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setSurLeDossier(!surLeDossier)}>
            <span className="rig-bascule__temoin"></span>
            <span>Dossier {dossier.numero} seulement</span>
          </button>
        )}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Detenteurs</div>
          {(vue?.detenteurs ?? []).map((d) => (
            <div className="rig-alerte" key={d.utilisateur}>
              <span className="rig-filet-v rig-filet-v--attente"></span>
              <span className="rig-alerte__corps">
                <span className="rig-alerte__titre">{d.utilisateur}</span><br />
                <span className="rig-alerte__detail">{d.nombre} dossier(s)</span>
              </span>
            </div>
          ))}
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Dossiers verrouilles</div>
          <span className="rig-resultats-proc__compte">{vue ? `${verrouilles.length} dossier(s)` : ''}</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Dossier</th>
                <th>Objet</th>
                <th>Etat</th>
                <th>Detenu par</th>
                <th>Depuis</th>
              </tr>
            </thead>
            <tbody>
              {/* Cle par rang : un dossier peut porter plusieurs demandes. */}
              {verrouilles.map((v, i) => (
                <tr key={`${i}:${v.dossier}`} onClick={() => ouvrirDossier(v.dossier)}
                    title={`Ouvrir l'affaire ${v.dossier}`}>
                  <td className="rig-table__mono">{v.dossier}</td>
                  <td>{v.nom}</td>
                  <td><span className={`rig-etat rig-etat--${v.statut}`}>{v.etat}</span></td>
                  <td>{v.utilisateur}</td>
                  <td className="rig-table__mono">{v.age} j</td>
                </tr>
              ))}
              {vue && verrouilles.length === 0 && (
                <tr><td colSpan={5} className="rig-table__vide">
                  {filtre ? `Le dossier ${filtre.numero} n'est retenu par aucun autre poste.` : "Aucun dossier n'est retenu par un autre poste."}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
