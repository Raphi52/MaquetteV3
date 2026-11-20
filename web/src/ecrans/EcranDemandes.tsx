// Ecran de la procedure PROC_DEMANDE.
// Motif de RIG v2 : CRITERES DE RECHERCHE a gauche, RESULTATS a droite
// (PROC_DEMANDE/RECHERCHE/OPE_SEARCH.cs et PROC_DEMANDE/RESULTATS).
// Les colonnes viennent de PROC_DEMANDE/DemandeDataGridView.cs.
//
// Porte depuis Components/Ecrans/EcranDemandes.razor (lot 3) : meme balisage,
// memes classes CSS. Le filtre et l'etat AFFICHE (la validation au controle
// final prime) sont calcules par Pont/EcranDemandes.cs sur JeuDemo.Demandes.
// La coquille .razor passe la liste des dossiers valides (sa fonction EstValide
// ne traverse pas vers React) et recoit ouvrir.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { dateSaisie, useNombreSaisi } from '../saisie'
import type { ContexteDossierVue } from '../plan/types'

export interface LigneDemande {
  numero: string
  nom: string
  dossier: string
  siren: string
  client: string
  traitement: string
  /** L'etat AFFICHE : « Validee » des que le dossier a passe le controle final. */
  etat: string
  statut: string
  enCours: boolean
  creation: string
  validation: string
  utilisateur: string
  alerte: boolean
}

export interface VueDemandes {
  traitements: string[]
  etats: string[]
  lignes: LigneDemande[]
  total: number
}

export interface ProprietesEcranDemandes {
  /** Les dossiers qui ont passe le controle final (EstValide de Home.razor). */
  valides: string[]
  /** Remonte le numero du dossier a ouvrir dans la visionneuse. */
  ouvrir: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranDemandes({ valides, ouvrir, dossier = null }: ProprietesEcranDemandes) {
  // Ouvert depuis une affaire : ses demandes, cherchees par son numero dans le
  // critere « Demande » (une demande RCS porte le numero de son dossier,
  // JeuDemo.Demandes), sans saisie. « Effacer » le vide, comme tout critere.
  const [critDemande, setCritDemande] = useState(dossier?.numero ?? '')
  const [critClient, setCritClient] = useState('')
  const [critProcessus, setCritProcessus] = useState('')
  const [critDu, setCritDu] = useState('')
  const [critAu, setCritAu] = useState('')
  const [critAlerte, setCritAlerte] = useState(false)
  const [critEnCours, setCritEnCours] = useState(false)
  const [critEtats, setCritEtats] = useState<string[]>([])
  const topMax = useNombreSaisi(50)
  // null tant que le pont n'a pas repondu : ni ligne ni « aucune demande ».
  const [vue, setVue] = useState<VueDemandes | null>(null)

  // La liste des valides est comparee par son CONTENU : la coquille la
  // recalcule a chaque rendu de l'accueil, sans qu'elle ait change.
  const clefValides = valides.join('|')

  useEffect(() => {
    let perime = false
    void appeler<VueDemandes>('EcranDemandes.Trouver', {
      critDemande, critClient, critProcessus,
      critDu: dateSaisie(critDu), critAu: dateSaisie(critAu),
      critEtats, critAlerte, critEnCours, critTopMax: topMax.valeur,
    }, clefValides === '' ? [] : clefValides.split('|')).then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [critDemande, critClient, critProcessus, critDu, critAu, critEtats, critAlerte, critEnCours, topMax.valeur, clefValides])

  const trouvees = vue?.lignes ?? []

  function basculerEtat(etat: string) {
    setCritEtats((avant) => (avant.includes(etat) ? avant.filter((e) => e !== etat) : [...avant, etat]))
  }

  function effacer() {
    setCritDemande('')
    setCritClient('')
    setCritProcessus('')
    setCritDu('')
    setCritAu('')
    setCritAlerte(false)
    setCritEnCours(false)
    topMax.poser(50)
    setCritEtats([])
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Demande</span>
          <input className="rig-saisie-champ__saisie" placeholder="numero ou nom"
                 value={critDemande} onChange={(e) => setCritDemande(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Client</span>
          <input className="rig-saisie-champ__saisie" placeholder="nom ou numero de compte"
                 value={critClient} onChange={(e) => setCritClient(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Processus</span>
          <select className="rig-saisie-champ__saisie" value={critProcessus} onChange={(e) => setCritProcessus(e.target.value)}>
            <option value="">Tous</option>
            {(vue?.traitements ?? []).map((trait) => (
              <option key={trait} value={trait}>{trait}</option>
            ))}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Dates de la demande</div>
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
          <div className="rig-kicker">Etats de la demande</div>
          {(vue?.etats ?? []).map((e) => (
            <button key={e} className={`rig-bascule${critEtats.includes(e) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerEtat(e)}>
              <span className="rig-bascule__temoin"></span>
              <span>{e}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${critAlerte ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritAlerte(!critAlerte)}>
            <span className="rig-bascule__temoin"></span>
            <span>Alerte uniquement</span>
          </button>
          <button className={`rig-bascule${critEnCours ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritEnCours(!critEnCours)}>
            <span className="rig-bascule__temoin"></span>
            <span>En cours uniquement</span>
          </button>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nombre maxi. de demandes affichees</span>
          <input className="rig-saisie-champ__saisie" type="number" min="1" max="500"
                 value={topMax.texte} onChange={(e) => topMax.saisir(e.target.value)} />
        </label>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Resultats de la recherche</div>
          <span className="rig-resultats-proc__compte">
            {vue && <>{trouvees.length}{' affichee(s) sur '}{vue.total}</>}
          </span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>N Demande</th>
                <th>Nom de la demande</th>
                <th>N Dossier</th>
                <th>Siren</th>
                <th>Client</th>
                <th>Traitement</th>
                <th>Etat</th>
                <th className="rig-table__centre">En cours</th>
                <th>Date de creation</th>
                <th>Date de validation</th>
                <th>Utilisateur</th>
              </tr>
            </thead>
            <tbody>
              {trouvees.map((dem, i) => (
                <tr key={`${i}:${dem.numero}`} className={dem.alerte ? 'rig-table__ligne--alerte' : undefined}
                    onClick={() => ouvrir(dem.dossier)}>
                  <td className="rig-table__mono">{dem.numero}</td>
                  <td>{dem.nom}</td>
                  <td className="rig-table__mono">{dem.dossier}</td>
                  <td className="rig-table__mono">{dem.siren}</td>
                  <td>{dem.client}</td>
                  <td className="rig-table__mono">{dem.traitement}</td>
                  <td>
                    <span className={`rig-etat rig-etat--${dem.statut}`}>
                      {dem.etat}
                    </span>
                  </td>
                  <td className="rig-table__centre">{dem.enCours ? 'oui' : ''}</td>
                  <td className="rig-table__mono">{dem.creation}</td>
                  <td className="rig-table__mono">{dem.validation}</td>
                  <td>{dem.utilisateur}</td>
                </tr>
              ))}
              {vue && trouvees.length === 0 && (
                <tr>
                  <td colSpan={11} className="rig-table__vide">
                    Aucune demande ne repond a ces criteres.
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
