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
//
// LISTE MODERNE (24/11/2026, plan des listes modernes, lot 5) : les formalites retenues prennent les briques de
// composants/ListeModerne.tsx -- tete (synthese, « Aller a une formalite… » Ctrl K qui filtre la liste affichee,
// Entree ouvre le dossier reste seul), vues = le critere Type RCS existant, avatar + denomination + numero de
// dossier, type en pastille, etat en point colore, pied. Les criteres, le clic sur une ligne et le bordereau ne
// changent pas. fix-ok: ajout du socle des listes en plusieurs pas (lot 5), pas un correctif.

import { useState } from 'react'
import { contientSansCasse, dateCourte, entierLisible, estBlanc } from '../criteres'
import { CelluleIdentite, EnteteListe, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe, type Teinte } from '../composants/ListeModerne'

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

/** Les vues : le critere Type RCS de l'ecran, preregle. */
const VuesType: { cle: string; libelle: string }[] = [
  { cle: '', libelle: 'Toutes' },
  { cle: 'PM', libelle: 'Personnes morales' },
  { cle: 'PP', libelle: 'Personnes physiques' },
]
const TeinteType: Record<string, Teinte> = { PM: 'bleu', PP: 'orange' }
const NomType: Record<string, string> = { PM: 'Personne morale', PP: 'Personne physique' }

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

  /** Les formalites retenues par les criteres, pour un type RCS donne (une vue compte les siennes). */
  function retenir(type: string): LigneArchi[] {
    let q = Lignes
    if (dateMin !== '') q = q.filter((l) => l.radiation >= dateMin)
    if (dateMax !== '') q = q.filter((l) => l.radiation <= dateMax)
    if (type !== '') q = q.filter((l) => l.type === type)
    return q.slice(0, Math.min(Math.max(nbDossiers, 1), 999))
  }
  const retenues = retenir(typeRcs)

  // « Aller a une formalite… » (Ctrl K) : filtre la liste AFFICHEE, sans toucher aux criteres.
  const [filtreRapide, setFiltreRapide] = useState('')
  const affichees = estBlanc(filtreRapide) ? retenues
    : retenues.filter((l) => contientSansCasse(`${l.dossier} ${l.denomination}`, filtreRapide.trim()))
  const pieces = affichees.reduce((s, l) => s + l.pieces, 0)

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

      <ListeModerne etiquette="Formalites retenues">
        <EnteteListe titre="Formalites retenues"
                     synthese={<>{affichees.length}{affichees.length > 1 ? ' retenues' : ' retenue'}{' sur '}{Lignes.length}
                       {' · '}<b>{pieces}</b>{pieces > 1 ? ' pieces' : ' piece'}{' · bordereau '}{prepare ? 'prepare' : 'non prepare'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une formalite…',
                               valider: () => { if (affichees.length === 1) ouvrirDossier(affichees[0]!.dossier) } }} />

        <VuesListe vues={VuesType.map((v) => ({ cle: v.cle, libelle: v.libelle, compte: retenir(v.cle).length }))}
                   active={typeRcs} choisir={setTypeRcs} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col /><col style={{ width: '14%' }} /><col style={{ width: '15%' }} /><col style={{ width: '13%' }} />
              <col style={{ width: '8%' }} /><col style={{ width: '16%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>Dossier</th>
                <th>Type RCS</th>
                <th>Date de radiation</th>
                <th>Dernier depot</th>
                <th className="rig-table__centre">Pieces</th>
                <th>Etat</th>
              </tr>
            </thead>
            <tbody>
              {affichees.map((ligne) => (
                <tr key={ligne.dossier} onClick={() => ouvrirDossier(ligne.dossier)}>
                  <CelluleIdentite nom={ligne.denomination} objet={ligne.dossier} titre={`${ligne.denomination} · ${ligne.dossier}`} />
                  <td><Pastille teinte={TeinteType[ligne.type] ?? 'gris'} titre={NomType[ligne.type]}>{ligne.type}</Pastille></td>
                  <td className="rig-table__mono">{dateCourte(ligne.radiation)}</td>
                  <td className="rig-table__mono">{dateCourte(ligne.dernierDepot)}</td>
                  <td className="rig-table__centre">{ligne.pieces}</td>
                  <td><PointEtat teinte={prepare ? 'vert' : 'orange'}>{prepare ? 'Sur bordereau' : 'A archiver'}</PointEtat></td>
                </tr>
              ))}
              {retenues.length === 0 && (
                <tr><td colSpan={6} className="rig-table__vide">Aucune formalite dans cette plage de radiation.</td></tr>
              )}
              {retenues.length > 0 && affichees.length === 0 && (
                <tr><td colSpan={6} className="rig-table__vide">
                  {`Aucune des ${retenues.length} formalites ne contient « ${filtreRapide.trim()} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' formalites affichees' : ' formalite affichee'}</span>
          <span>Pieces <b>{pieces}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
