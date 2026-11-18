// Ecran de la procedure PROC_ARCHIRAD.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_ARCHIRAD) :
//   - Operations/OPE_ARCHRSEL : ult_DateMin, ult_DateMax (bornes de date de
//     radiation), ult_TypeRcs, ult_CBListe et ult_TextListe
//   - Operations/OPE_ARCHRTOT : ult_NbDossiers, ult_TypeBrd, ult_cboImprimante
//
// NOTE : v2 n a PAS de grille de resultat sur cet ecran, seulement ces
// criteres et une impression. Le tableau ci-dessous est un ajout de la
// maquette, pour que l ecran montre quelque chose en demonstration ; il ne
// correspond a aucune grille v2.
//
// Porte depuis Components/Ecrans/EcranArchivage.razor (lot 5) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses formalites de
// demonstration sont ici, sans pont. La coquille .razor monte cet ecran et
// recoit ouvrirDossier.

import { useState } from 'react'
import { dateCourte, entierLisible } from '../criteres'

interface LigneArchi {
  dossier: string
  denomination: string
  type: string
  /** aaaa-mm-jj */
  radiation: string
  /** aaaa-mm-jj */
  dernierDepot: string
  pieces: number
}

const Lignes: LigneArchi[] = [
  { dossier: '1998B00214', denomination: 'SARL MENUISERIE COLLIN', type: 'PM', radiation: '2014-03-12', dernierDepot: '2013-11-04', pieces: 18 },
  { dossier: '2001B00877', denomination: 'SAS ELEC DU NORD', type: 'PM', radiation: '2016-07-01', dernierDepot: '2016-02-20', pieces: 24 },
  { dossier: '1995A00120', denomination: 'DUPONT Jean', type: 'PP', radiation: '2012-10-09', dernierDepot: '2012-05-30', pieces: 7 },
  { dossier: '2004B01344', denomination: 'EURL LE PETIT CAFE', type: 'PM', radiation: '2018-01-25', dernierDepot: '2017-09-12', pieces: 11 },
  { dossier: '1999A00456', denomination: 'MARTIN Claire', type: 'PP', radiation: '2015-06-03', dernierDepot: '2015-01-15', pieces: 9 },
]

export interface ProprietesEcranArchivage {
  /** Remonte le NUMERO de dossier de la formalite cliquee : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
}

export function EcranArchivage({ ouvrirDossier }: ProprietesEcranArchivage) {
  const [dateMin, setDateMin] = useState('2012-01-01')
  const [dateMax, setDateMax] = useState('2016-12-31')
  // v2 : ult_TextListe, le libelle associe a la case ult_CBListe.
  const [libelleListe, setLibelleListe] = useState('')

  const [typeRcs, setTypeRcs] = useState('')
  const [typeBrd, setTypeBrd] = useState('ARCH')
  const [liste, setListe] = useState('COMPLETE')
  // Le texte saisi et la derniere valeur LISIBLE (le @bind d'un int).
  const [nbDossiersTexte, setNbDossiersTexte] = useState('50')
  const [nbDossiers, setNbDossiers] = useState(50)
  const [imprimante, setImprimante] = useState('Greffe - Etage 1')
  const [prepare, setPrepare] = useState(false)

  function saisirNbDossiers(texte: string) {
    setNbDossiersTexte(texte)
    const n = entierLisible(texte)
    if (n !== null) setNbDossiers(n)
  }

  let q = Lignes
  if (dateMin !== '') q = q.filter((l) => l.radiation >= dateMin)
  if (dateMax !== '') q = q.filter((l) => l.radiation <= dateMax)
  if (typeRcs !== '') q = q.filter((l) => l.type === typeRcs)
  const retenues = q.slice(0, Math.min(Math.max(nbDossiers, 1), 999))

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Dates de radiation</div>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Date mini.</span>
            <input className="rig-saisie-champ__saisie" type="date" value={dateMin} onChange={(e) => setDateMin(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Date maxi.</span>
            <input className="rig-saisie-champ__saisie" type="date" value={dateMax} onChange={(e) => setDateMax(e.target.value)} />
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Selection</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Type RCS</span>
            <select className="rig-saisie-champ__saisie" value={typeRcs} onChange={(e) => setTypeRcs(e.target.value)}>
              <option value="">Tous</option>
              <option value="PM">Personne morale</option>
              <option value="PP">Personne physique</option>
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Type de bordereau</span>
            <select className="rig-saisie-champ__saisie" value={typeBrd} onChange={(e) => setTypeBrd(e.target.value)}>
              <option value="ARCH">Archivage</option>
              <option value="RAD">Radiation</option>
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Liste</span>
            <select className="rig-saisie-champ__saisie" value={liste} onChange={(e) => setListe(e.target.value)}>
              <option value="COMPLETE">Liste complete</option>
              <option value="CONTROLE">Liste de controle</option>
            </select>
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Libelle de la liste</span>
            <input className="rig-saisie-champ__saisie" value={libelleListe} onChange={(e) => setLibelleListe(e.target.value)} />
          </label>

          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nombre de dossiers</span>
            <input className="rig-saisie-champ__saisie" type="number" min="1" max="999" value={nbDossiersTexte}
                   onChange={(e) => saisirNbDossiers(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Imprimante</span>
            <select className="rig-saisie-champ__saisie" value={imprimante} onChange={(e) => setImprimante(e.target.value)}>
              <option>Greffe - Etage 1</option>
              <option>Greffe - Accueil</option>
              <option>PDF (fichier)</option>
            </select>
          </label>
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" onClick={() => setPrepare(true)}>Preparer le bordereau</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Formalites retenues</div>
          <span className="rig-resultats-proc__compte">
            {retenues.length} retenue(s) sur {Lignes.length} — bordereau {prepare ? 'prepare' : 'non prepare'}
          </span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>N Dossier</th>
                <th>Denomination</th>
                <th>Type RCS</th>
                <th>Date de radiation</th>
                <th>Dernier depot</th>
                <th className="rig-table__centre">Pieces</th>
                <th>Etat</th>
              </tr>
            </thead>
            <tbody>
              {retenues.map((ligne) => (
                <tr key={ligne.dossier} onClick={() => ouvrirDossier(ligne.dossier)}>
                  <td className="rig-table__mono">{ligne.dossier}</td>
                  <td>{ligne.denomination}</td>
                  <td className="rig-table__mono">{ligne.type}</td>
                  <td className="rig-table__mono">{dateCourte(ligne.radiation)}</td>
                  <td className="rig-table__mono">{dateCourte(ligne.dernierDepot)}</td>
                  <td className="rig-table__centre">{ligne.pieces}</td>
                  <td><span className={`rig-etat rig-etat--${prepare ? 'ok' : 'attente'}`}>{prepare ? 'Sur bordereau' : 'A archiver'}</span></td>
                </tr>
              ))}
              {retenues.length === 0 && (
                <tr><td colSpan={7} className="rig-table__vide">Aucune formalite dans cette plage de radiation.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
