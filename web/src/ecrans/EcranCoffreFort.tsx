// Ecran de la procedure PROC_COFFRE_FORT.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_COFFRE_FORT) :
//   - OPE_RECHERCHE_COFFRE   : ult_DateDebut, ult_DateFin (bornes), et les
//     filtres de nature ult_Archive, ult_Audience, ult_Erreur, ult_GedJud,
//     ult_Message, ult_Reda
//   - OPE_FILTRE_DESTINATAIRE : cboDest, CBChoixDestinataire, txtNbLigneHisto
//   - OPE_DESTINATAIRE        : txtNomCoffre, cboType, cboMand, dtDateDemarrage
//   - OPE_PARAMETRAGE_COFFRE  : ult_GroupRadioButton1 (coffre par defaut)
//   - OPE_TREEVIEW_TRANSMETTRE / OPE_TREEVIEW_RETRANSMETTRE : les deux listes
// Colonnes des grilles : Code, Abrege, Destinataire, Etat, Date envoi
// infogreffe, Date AR Infogreffe, Date AR destinataire, Date lecture
// destinataire, Date traitement RIG, Date avis de depot coffre,
// Code de depot dans le coffre, Erreur.
//
// Porte depuis Components/Ecrans/EcranCoffreFort.razor (lot 4) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// depots de demonstration l'ont suivi ici.

import { useState } from 'react'
import { contientSansCasse, dateCourte, distinctsTries, entierLisible, estBlanc } from '../criteres'

interface DepotCoffre {
  code: string
  abrege: string
  destinataire: string
  coffre: string
  typeDecision: string
  etat: string
  statut: string
  /** aaaa-mm-jj, ou null */
  envoiInfogreffe: string | null
  arInfogreffe: string | null
  arDestinataire: string | null
  lectureDestinataire: string | null
  traitementRig: string | null
  avisDepotCoffre: string | null
  codeDepot: string
  erreur: string
  historique: string[]
}

const Depots: DepotCoffre[] = [
  {
    code: 'CF-2026-0412', abrege: 'DUVAL', destinataire: 'Me LEROY', coffre: 'COFFRE_LEROY', typeDecision: 'ACAD', etat: 'Transmis', statut: 'ok',
    envoiInfogreffe: '2026-03-07', arInfogreffe: '2026-03-07', arDestinataire: '2026-03-08', lectureDestinataire: '2026-03-09', traitementRig: '2026-03-07', avisDepotCoffre: '2026-03-08',
    codeDepot: 'DEP-77120', erreur: '', historique: ['07/03/2026 — depose', '08/03/2026 — accuse de reception', '09/03/2026 — lu'],
  },
  {
    code: 'CF-2026-0518', abrege: 'MARNE', destinataire: 'Me BLANC', coffre: 'COFFRE_BLANC', typeDecision: 'ACAD', etat: 'Transmis', statut: 'ok',
    envoiInfogreffe: '2026-06-03', arInfogreffe: '2026-06-03', arDestinataire: '2026-06-04', lectureDestinataire: null, traitementRig: '2026-06-03', avisDepotCoffre: '2026-06-04',
    codeDepot: 'DEP-77455', erreur: '', historique: ['03/06/2026 — depose', '04/06/2026 — accuse de reception'],
  },
  {
    code: 'CF-2026-0603', abrege: 'MARIE', destinataire: 'Me LEROY', coffre: 'COFFRE_LEROY', typeDecision: 'CD', etat: 'A envoyer', statut: 'attente',
    envoiInfogreffe: null, arInfogreffe: null, arDestinataire: null, lectureDestinataire: null, traitementRig: '2026-09-01', avisDepotCoffre: null,
    codeDepot: '', erreur: '', historique: ['01/09/2026 — prepare'],
  },
  {
    code: 'CF-2026-0610', abrege: 'MOULIN', destinataire: 'Me PETIT', coffre: 'COFFRE_PETIT', typeDecision: 'CD', etat: 'En erreur', statut: 'alerte',
    envoiInfogreffe: '2026-09-04', arInfogreffe: null, arDestinataire: null, lectureDestinataire: null, traitementRig: '2026-09-04', avisDepotCoffre: null,
    codeDepot: '', erreur: 'Destinataire inconnu', historique: ['04/09/2026 — envoi refuse par le coffre'],
  },
]

const Natures = ['Archive', 'Audience', 'Erreur', 'GED judiciaire', 'Message', 'REDA']

const Coffres = distinctsTries(Depots.map((d) => d.coffre))

const jour = (d: string | null) => (d === null ? '' : dateCourte(d))

export function EcranCoffreFort() {
  const [recherche, setRecherche] = useState('')
  const [coffre, setCoffre] = useState('')
  const [typeDecision, setTypeDecision] = useState('')
  // Criteres de OPE_RECHERCHE_COFFRE (RIG v2) : vides au depart.
  const [dateDebut, setDateDebut] = useState('')
  const [dateFin, setDateFin] = useState('')
  const [naturesActives, setNaturesActives] = useState<ReadonlySet<string>>(new Set())
  // Le texte saisi et la derniere valeur LISIBLE (le @bind d'un int).
  const [nbLignesTexte, setNbLignesTexte] = useState('20')
  const [nbLignesHisto, setNbLignesHisto] = useState(20)
  const [coffreDefaut, setCoffreDefaut] = useState('COFFRE_LEROY')
  const [aTransmettre, setATransmettre] = useState(true)
  const [selection, setSelection] = useState<DepotCoffre | null>(null)

  function basculerNature(n: string) {
    setNaturesActives((avant) => {
      const s = new Set(avant)
      if (!s.delete(n)) s.add(n)
      return s
    })
  }

  function saisirNbLignes(texte: string) {
    setNbLignesTexte(texte)
    const n = entierLisible(texte)
    if (n !== null) setNbLignesHisto(n)
  }

  let trouves = Depots.filter((d) => (d.envoiInfogreffe === null || d.erreur !== '') === aTransmettre)
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouves = trouves.filter((d) => contientSansCasse(d.code, t) || contientSansCasse(d.destinataire, t))
  }
  if (coffre !== '') trouves = trouves.filter((d) => d.coffre === coffre)
  if (typeDecision !== '') trouves = trouves.filter((d) => d.typeDecision === typeDecision)
  const s = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Code ou destinataire</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nom du coffre</span>
          <select className="rig-saisie-champ__saisie" value={coffre} onChange={(e) => setCoffre(e.target.value)}>
            <option value="">Tous</option>
            {Coffres.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type de decision</span>
          <select className="rig-saisie-champ__saisie" value={typeDecision} onChange={(e) => setTypeDecision(e.target.value)}>
            <option value="">Tous</option>
            <option value="ACAD">Avec code de depot</option>
            <option value="CD">Sans code de depot</option>
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Periode</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date" value={dateDebut} onChange={(e) => setDateDebut(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date" value={dateFin} onChange={(e) => setDateFin(e.target.value)} />
            </label>
          </div>
        </div>

        {/* v2 : ult_Archive, ult_Audience, ult_Erreur, ult_GedJud,
            ult_Message, ult_Reda -- les natures de contenu recherchees. */}
        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Nature du contenu</div>
          {Natures.map((nature) => (
            <button key={nature} className={`rig-bascule${naturesActives.has(nature) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerNature(nature)}>
              <span className="rig-bascule__temoin"></span>
              <span>{nature}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Historique</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nombre de lignes d'historique</span>
            <input className="rig-saisie-champ__saisie" type="number" min="1" max="200"
                   value={nbLignesTexte} onChange={(e) => saisirNbLignes(e.target.value)} />
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Parametrage du coffre</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Coffre par defaut</span>
            <input className="rig-saisie-champ__saisie" value={coffreDefaut} onChange={(e) => setCoffreDefaut(e.target.value)} />
          </label>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-onglets">
            <button className={`rig-onglet${aTransmettre ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setATransmettre(true)}>A transmettre</button>
            <button className={`rig-onglet${!aTransmettre ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setATransmettre(false)}>Deja transmis</button>
          </div>
          <span className="rig-resultats-proc__compte">{trouves.length} depot(s)</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Code</th>
                <th>Abrege</th>
                <th>Destinataire</th>
                <th>Etat</th>
                <th>Date envoi infogreffe</th>
                <th>Date AR Infogreffe</th>
                <th>Date AR destinataire</th>
                <th>Date lecture destinataire</th>
                <th>Date traitement RIG</th>
                <th>Date avis de depot coffre</th>
                <th>Code de depot</th>
                <th>Erreur</th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((dep) => (
                <tr key={dep.code} className={dep.erreur === '' ? '' : 'rig-table__ligne--alerte'}
                    onClick={() => setSelection(dep)}>
                  <td className="rig-table__mono">{dep.code}</td>
                  <td className="rig-table__mono">{dep.abrege}</td>
                  <td>{dep.destinataire}</td>
                  <td><span className={`rig-etat rig-etat--${dep.statut}`}>{dep.etat}</span></td>
                  <td className="rig-table__mono">{jour(dep.envoiInfogreffe)}</td>
                  <td className="rig-table__mono">{jour(dep.arInfogreffe)}</td>
                  <td className="rig-table__mono">{jour(dep.arDestinataire)}</td>
                  <td className="rig-table__mono">{jour(dep.lectureDestinataire)}</td>
                  <td className="rig-table__mono">{jour(dep.traitementRig)}</td>
                  <td className="rig-table__mono">{jour(dep.avisDepotCoffre)}</td>
                  <td className="rig-table__mono">{dep.codeDepot}</td>
                  <td>{dep.erreur}</td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr><td colSpan={12} className="rig-table__vide">Aucun depot ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {s && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-kicker">Historique de {s.code} ({nbLignesHisto} ligne(s) au plus)</div>
            <ul className="rig-historique__liste">
              {s.historique.slice(0, Math.max(nbLignesHisto, 1)).map((h, i) => <li key={i}>{h}</li>)}
            </ul>
          </div>
        )}
      </section>
    </div>
  )
}
