// Ecran de la procedure PROC_GCLIENT.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_GCLIENT) :
//   - OPE_MOTCLE        : ult_TxtMotCle
//   - OPE_STATUTCLIENT  : ult_ChckBxActif, ult_ChckBxBloque, ult_ChckBxSommeil
//   - OPE_TYPECLIENT    : ult_ChckBxCompte, ult_ChckBxGreffe, ult_ChckBxPC,
//                         ult_ChckBxPreventions, ult_ChckBxWeb
//   - OPE_RESULTAT      : grille ult_DGVResultat, colonnes Id Client,
//                         N Compte, Abrege, Nom, Groupe, Statut, Type
//
// Porte depuis Components/Ecrans/EcranClients.razor (lot 4) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses clients de
// demonstration l'ont suivi ici. La coquille .razor recoit selectionner.

import { useState } from 'react'
import { contientSansCasse, estBlanc } from '../criteres'

interface ClientRcs {
  id: number
  compte: string
  abrege: string
  nom: string
  groupe: string
  statut: string
  type: string
}

// v2 : ult_ChckBxActif, ult_ChckBxBloque, ult_ChckBxSommeil.
const Statuts = ['Actif', 'Bloque', 'En sommeil']
const Types = ['En compte', 'Greffe', 'Prevention', 'PC', 'Web']

const Clients: ClientRcs[] = [
  { id: 1042, compte: 'C-000142', abrege: 'LEROY', nom: 'SELARL LEROY & ASSOCIES', groupe: 'Mandataires', statut: 'Actif', type: 'En compte' },
  { id: 1088, compte: 'C-000188', abrege: 'BLANC', nom: 'CABINET BLANC', groupe: 'Mandataires', statut: 'Actif', type: 'En compte' },
  { id: 1130, compte: 'C-000231', abrege: 'DUPONT', nom: 'SCP DUPONT-MARTIN', groupe: 'Avocats', statut: 'Bloque', type: 'En compte' },
  { id: 1204, compte: 'W-004412', abrege: 'WEBPRO', nom: 'Deposant en ligne', groupe: '', statut: 'Actif', type: 'Web' },
  { id: 1250, compte: 'G-000012', abrege: 'GRFCPY', nom: 'Greffe de Compiegne', groupe: 'Greffes', statut: 'Actif', type: 'Greffe' },
  { id: 1305, compte: 'P-000031', abrege: 'PREV51', nom: 'Cellule prevention 51', groupe: 'Prevention', statut: 'En sommeil', type: 'Prevention' },
]

export interface ProprietesEcranClients {
  /** Remonte l'abrege du client choisi (v2 : bouton « Selectionner »). */
  selectionner: (abrege: string) => void
}

function basculer(ensemble: ReadonlySet<string>, valeur: string): Set<string> {
  const s = new Set(ensemble)
  if (!s.delete(valeur)) s.add(valeur)
  return s
}

export function EcranClients({ selectionner }: ProprietesEcranClients) {
  const [motCle, setMotCle] = useState('')
  const [critStatuts, setCritStatuts] = useState<ReadonlySet<string>>(new Set())
  const [critTypes, setCritTypes] = useState<ReadonlySet<string>>(new Set())

  function effacer() {
    setMotCle('')
    setCritStatuts(new Set())
    setCritTypes(new Set())
  }

  let trouves = Clients
  if (!estBlanc(motCle)) {
    const t = motCle.trim()
    trouves = trouves.filter((c) => contientSansCasse(c.nom, t) || contientSansCasse(c.abrege, t) || contientSansCasse(c.compte, t))
  }
  if (critStatuts.size > 0) trouves = trouves.filter((c) => critStatuts.has(c.statut))
  if (critTypes.size > 0) trouves = trouves.filter((c) => critTypes.has(c.type))

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Mot cle</span>
          <input className="rig-saisie-champ__saisie" placeholder="nom, abrege ou compte"
                 value={motCle} onChange={(e) => setMotCle(e.target.value)} />
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Statut du client</div>
          {Statuts.map((v) => (
            <button key={v} className={`rig-bascule${critStatuts.has(v) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => setCritStatuts((avant) => basculer(avant, v))}>
              <span className="rig-bascule__temoin"></span>
              <span>{v}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Type de client</div>
          {Types.map((v) => (
            <button key={v} className={`rig-bascule${critTypes.has(v) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => setCritTypes((avant) => basculer(avant, v))}>
              <span className="rig-bascule__temoin"></span>
              <span>{v}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Resultat</div>
          <span className="rig-resultats-proc__compte">{trouves.length} client(s) sur {Clients.length}</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Id Client</th>
                <th>N Compte</th>
                <th>Abrege</th>
                <th>Nom</th>
                <th>Groupe</th>
                <th>Statut</th>
                <th>Type</th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((cli) => (
                <tr key={cli.id} onClick={() => selectionner(cli.abrege)}>
                  <td className="rig-table__mono">{cli.id}</td>
                  <td className="rig-table__mono">{cli.compte}</td>
                  <td className="rig-table__mono">{cli.abrege}</td>
                  <td>{cli.nom}</td>
                  <td>{cli.groupe}</td>
                  <td><span className={`rig-etat rig-etat--${cli.statut === 'Actif' ? 'ok' : 'attente'}`}>{cli.statut}</span></td>
                  <td>{cli.type}</td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr><td colSpan={7} className="rig-table__vide">Aucun client ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
