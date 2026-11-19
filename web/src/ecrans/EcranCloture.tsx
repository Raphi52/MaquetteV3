// Ecran de la procedure PROC_CLOT.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_CLOT/PROC_CLOT) :
// v2 ne presente pas UNE grille mais DEUX, a deux etapes differentes, avec
// des colonnes distinctes (HeaderText releves dans les .Designer.cs) :
//
//   - « 1-4-5 Selection PC » : N PC, Nom de l Affaire, Chambre, Date
//     d audience, Heure d appel, Heure de debut, Type de classement,
//     A facturer, Limite Examen, Dernier courrier envoye, Dirigeants,
//     Liquidateurs, Forfait, Huissier, Code Postal - Ville
//   - « 2-3 Enrolement Requete » : N Role, N PC, Demandeur, Defendeur,
//     Acte de saisine, Audience, Depot, Facturation
//
// La maquette montrait une grille unique melangeant les deux. Elle presente
// desormais les deux etapes en onglets, chacune avec SES colonnes.
//
// Porte depuis Components/Ecrans/EcranCloture.razor (lot 4) : meme balisage,
// memes classes CSS. La date du jour (JeuDemo.Aujourdhui), a laquelle se
// compare la limite d'examen, vient de Pont/EcranCloture.cs ; les affaires de
// demonstration etaient declarees dans l'ecran et l'ont suivi. La coquille
// .razor recoit ouvrirDossier.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'

interface LigneClot {
  role: string
  affaire: string
  demandeur: string
  defendeur: string
  chambre: string
  /** aaaa-mm-jj */
  audience: string
  heureAppel: string
  classement: string
  /** aaaa-mm-jj */
  limiteExamen: string
  aFacturer: boolean
  dernierCourrier: string
  dossier: string
  numPc: string
  heureDebut: string
  dirigeants: string
  liquidateurs: string
  forfait: string
  huissier: string
  codePostalVille: string
  acteSaisine: string
  /** aaaa-mm-jj */
  depot: string
  facturation: string
}

const Classements = ['Insuffisance d\'actif', 'Extinction du passif', 'Conversion en liquidation', 'Report']

const Lignes: LigneClot[] = [
  { role: '2024/00318', affaire: 'SARL BOULANGERIE DUVAL', demandeur: 'Me LEROY', defendeur: 'SARL BOULANGERIE DUVAL', chambre: '1re', audience: '2026-09-15', heureAppel: '09:00', classement: 'Insuffisance d\'actif', limiteExamen: '2026-09-12', aFacturer: true, dernierCourrier: '28/08/2026', dossier: '2024B00318', numPc: 'PC-2024-0118', heureDebut: '08:30', dirigeants: 'DUVAL Marc', liquidateurs: 'Me LEROY', forfait: '450,00', huissier: 'SCP HUISSIERS REIMS', codePostalVille: '51100 Reims', acteSaisine: 'Assignation', depot: '2026-03-04', facturation: 'Facturee' },
  { role: '2024/00407', affaire: 'SAS TRANSPORTS MARNE', demandeur: 'Me BLANC', defendeur: 'SAS TRANSPORTS MARNE', chambre: '2e', audience: '2026-09-15', heureAppel: '09:30', classement: 'Extinction du passif', limiteExamen: '2026-09-05', aFacturer: false, dernierCourrier: '12/08/2026', dossier: '2024B00407', numPc: 'PC-2024-0207', heureDebut: '09:00', dirigeants: 'MARTIN Luc', liquidateurs: 'Me BLANC', forfait: '450,00', huissier: '', codePostalVille: '51000 Chalons', acteSaisine: 'Requete', depot: '2026-05-19', facturation: 'A facturer' },
  { role: '2023/01120', affaire: 'EURL COIFFURE MARIE', demandeur: 'Me LEROY', defendeur: 'EURL COIFFURE MARIE', chambre: '1re', audience: '2026-09-22', heureAppel: '10:15', classement: 'Conversion en liquidation', limiteExamen: '2026-09-20', aFacturer: true, dernierCourrier: '02/09/2026', dossier: '2023B01120', numPc: 'PC-2023-0920', heureDebut: '10:00', dirigeants: 'MARIE Sophie', liquidateurs: 'Me LEROY', forfait: '620,00', huissier: 'SCP HUISSIERS REIMS', codePostalVille: '51100 Reims', acteSaisine: 'Assignation', depot: '2025-11-02', facturation: 'Facturee' },
  { role: '2025/00044', affaire: 'SCI DU MOULIN', demandeur: 'Me PETIT', defendeur: 'SCI DU MOULIN', chambre: '3e', audience: '2026-09-29', heureAppel: '11:00', classement: 'Report', limiteExamen: '2026-10-02', aFacturer: false, dernierCourrier: '', dossier: '2025B00044', numPc: 'PC-2025-0044', heureDebut: '10:45', dirigeants: 'BERTIN Andre', liquidateurs: 'Me PETIT', forfait: '380,00', huissier: '', codePostalVille: '51200 Epernay', acteSaisine: 'Requete', depot: '2024-06-12', facturation: 'A facturer' },
  { role: '2022/00876', affaire: 'SARL PLOMBERIE NORD', demandeur: 'Me BLANC', defendeur: 'SARL PLOMBERIE NORD', chambre: '2e', audience: '2026-10-06', heureAppel: '14:00', classement: 'Insuffisance d\'actif', limiteExamen: '2026-09-30', aFacturer: true, dernierCourrier: '19/07/2026', dossier: '2022B00876', numPc: 'PC-2022-0876', heureDebut: '13:30', dirigeants: 'NOEL Patrick', liquidateurs: 'Me BLANC', forfait: '450,00', huissier: 'SCP HUISSIERS NORD', codePostalVille: '51100 Reims', acteSaisine: 'Assignation', depot: '2022-09-08', facturation: 'Facturee' },
]

const Chambres = distinctsTries(Lignes.map((l) => l.chambre))

export interface ProprietesEcranCloture {
  /** Remonte le dossier a ouvrir dans la visionneuse. */
  ouvrirDossier: (dossier: string) => void
}

export function EcranCloture({ ouvrirDossier }: ProprietesEcranCloture) {
  // v2 presente ces affaires a deux etapes distinctes, avec des colonnes
  // differentes : « Selection PC » puis « Enrolement / requete ».
  const [etapeSelection, setEtapeSelection] = useState(true)
  const [critAffaire, setCritAffaire] = useState('')
  const [critChambre, setCritChambre] = useState('')
  const [critDu, setCritDu] = useState('')
  const [critAu, setCritAu] = useState('')
  const [critAFacturer, setCritAFacturer] = useState(false)
  const [critExamenDepasse, setCritExamenDepasse] = useState(false)
  const [critTypes, setCritTypes] = useState<ReadonlySet<string>>(new Set())
  // JeuDemo.Aujourdhui : null tant que le pont n'a pas repondu (aucune ligne
  // n'est alors marquee en retard).
  const [aujourdhui, setAujourdhui] = useState<string | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranCloture.DateDuJour').then((d) => { if (!perime) setAujourdhui(d) })
    return () => { perime = true }
  }, [])

  const enRetard = (l: LigneClot) => aujourdhui !== null && l.limiteExamen < aujourdhui

  function basculer(t: string) {
    setCritTypes((avant) => {
      const s = new Set(avant)
      if (!s.delete(t)) s.add(t)
      return s
    })
  }

  function effacer() {
    setCritAffaire(''); setCritChambre('')
    setCritDu(''); setCritAu('')
    setCritAFacturer(false); setCritExamenDepasse(false)
    setCritTypes(new Set())
  }

  let trouvees = Lignes
  if (!estBlanc(critAffaire)) {
    const t = critAffaire.trim()
    trouvees = trouvees.filter((l) => contientSansCasse(l.affaire, t) || contientSansCasse(l.role, t))
  }
  if (!estBlanc(critChambre)) trouvees = trouvees.filter((l) => l.chambre === critChambre)
  if (critDu !== '') trouvees = trouvees.filter((l) => l.audience >= critDu)
  if (critAu !== '') trouvees = trouvees.filter((l) => l.audience <= critAu)
  if (critTypes.size > 0) trouvees = trouvees.filter((l) => critTypes.has(l.classement))
  if (critAFacturer) trouvees = trouvees.filter((l) => l.aFacturer)
  if (critExamenDepasse) trouvees = trouvees.filter(enRetard)

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres d'examen</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Affaire</span>
          <input className="rig-saisie-champ__saisie" placeholder="nom ou n de role"
                 value={critAffaire} onChange={(e) => setCritAffaire(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Chambre</span>
          <select className="rig-saisie-champ__saisie" value={critChambre} onChange={(e) => setCritChambre(e.target.value)}>
            <option value="">Toutes</option>
            {Chambres.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date d'audience</div>
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
          <div className="rig-kicker">Type de classement</div>
          {Classements.map((v) => (
            <button key={v} className={`rig-bascule${critTypes.has(v) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculer(v)}>
              <span className="rig-bascule__temoin"></span>
              <span>{v}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${critAFacturer ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritAFacturer(!critAFacturer)}>
            <span className="rig-bascule__temoin"></span>
            <span>A facturer uniquement</span>
          </button>
          <button className={`rig-bascule${critExamenDepasse ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritExamenDepasse(!critExamenDepasse)}>
            <span className="rig-bascule__temoin"></span>
            <span>Limite d'examen depassee</span>
          </button>
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-onglets" style={{ margin: 'var(--rig-esp-2) var(--rig-esp-3) 0' }}>
          <button className={`rig-onglet${etapeSelection ? ' rig-onglet--actif' : ''}`}
                  onClick={() => setEtapeSelection(true)}>Selection PC</button>
          <button className={`rig-onglet${etapeSelection ? '' : ' rig-onglet--actif'}`}
                  onClick={() => setEtapeSelection(false)}>Enrolement / requete</button>
        </div>

        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">{etapeSelection ? 'Selection des procedures collectives' : 'Enrolement et requete'}</div>
          <span className="rig-resultats-proc__compte">
            {trouvees.length} affichee(s) sur {Lignes.length}
          </span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              {etapeSelection ? (
                // Colonnes de « 1-4-5 Selection PC » (RIG v2).
                <tr>
                  <th>N PC</th>
                  <th>Nom de l'Affaire</th>
                  <th>Chambre</th>
                  <th>Date d'audience</th>
                  <th>Heure d'appel</th>
                  <th>Heure de debut</th>
                  <th>Type de classement</th>
                  <th className="rig-table__centre">A facturer</th>
                  <th>Limite Examen</th>
                  <th>Dernier courrier envoye</th>
                  <th>Dirigeants</th>
                  <th>Liquidateurs</th>
                  <th>Forfait</th>
                  <th>Huissier</th>
                  <th>Code Postal - Ville</th>
                </tr>
              ) : (
                // Colonnes de « 2-3 Enrolement Requete » (RIG v2).
                <tr>
                  <th>N Role</th>
                  <th>N PC</th>
                  <th>Demandeur</th>
                  <th>Defendeur</th>
                  <th>Acte de saisine</th>
                  <th>Audience</th>
                  <th>Depot</th>
                  <th>Facturation</th>
                </tr>
              )}
            </thead>
            <tbody>
              {trouvees.map((ligne) => (
                <tr key={ligne.role} className={enRetard(ligne) ? 'rig-table__ligne--alerte' : ''}
                    onClick={() => ouvrirDossier(ligne.dossier)}>
                  {etapeSelection ? (
                    <>
                      <td className="rig-table__mono">{ligne.numPc}</td>
                      <td>{ligne.affaire}</td>
                      <td className="rig-table__mono">{ligne.chambre}</td>
                      <td className="rig-table__mono">{dateCourte(ligne.audience)}</td>
                      <td className="rig-table__mono">{ligne.heureAppel}</td>
                      <td className="rig-table__mono">{ligne.heureDebut}</td>
                      <td>{ligne.classement}</td>
                      <td className="rig-table__centre">{ligne.aFacturer ? 'oui' : ''}</td>
                      <td className="rig-table__mono">{dateCourte(ligne.limiteExamen)}</td>
                      <td className="rig-table__mono">{ligne.dernierCourrier}</td>
                      <td>{ligne.dirigeants}</td>
                      <td>{ligne.liquidateurs}</td>
                      <td className="rig-table__mono">{ligne.forfait}</td>
                      <td>{ligne.huissier}</td>
                      <td>{ligne.codePostalVille}</td>
                    </>
                  ) : (
                    <>
                      <td className="rig-table__mono">{ligne.role}</td>
                      <td className="rig-table__mono">{ligne.numPc}</td>
                      <td>{ligne.demandeur}</td>
                      <td>{ligne.defendeur}</td>
                      <td>{ligne.acteSaisine}</td>
                      <td className="rig-table__mono">{dateCourte(ligne.audience)}</td>
                      <td className="rig-table__mono">{dateCourte(ligne.depot)}</td>
                      <td>{ligne.facturation}</td>
                    </>
                  )}
                </tr>
              ))}
              {trouvees.length === 0 && (
                <tr><td colSpan={etapeSelection ? 15 : 8} className="rig-table__vide">Aucune affaire ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
