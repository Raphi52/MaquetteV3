// Ecran de la procedure PROC_STAT — statistiques du greffe.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_STAT) :
//   - STATS_INTERNES/OPE_STATS_INTERNES.Designer.cs : la procedure n'a pas
//     UNE liste mais CINQ volets, montes cote a cote et nommes par leur
//     .Text — « Statistiques internes », « Statistiques web »,
//     « Repartitions des JC et OP », « Ouverture par activites : par
//     chiffre d'affaire », « Ouverture par activites : par nombre de
//     salaries ». D'ou les onglets ci-dessous : ce sont les volets de v2,
//     pas un decoupage invente.
//   - STATS_INTERNES/Commun/OPE_DATES.Designer.cs   : ultDateDebut, ultDateFin.
//   - STATS_INTERNES/Commun/OPE_NIVEAU.Designer.cs  : « Niveau » (ult_Cbx_Niveau),
//     filtre commun a tous les volets internes.
//   - STATS_SEMESTRIELLES/OPE_STATS_SEMESTRIELLES.Designer.cs : deuxieme
//     famille, avec ses propres dates et le champ « Nombre de PC ».
//
// Les valeurs sont des donnees de demonstration : en v2 elles viennent de
// la base du greffe, la maquette ne s'y connecte pas. Lot 9 du plan « RIG v3
// 100 % fonctionnel » (27/11/2026) : l'ecran le DIT (mention « exemple »), meme
// branche au serveur -- il affichait ses chiffres comme ceux du greffe. Les
// calculer demande les requetes de RIG v2
// (Outils\DLLEdition\RigRapport\Jud\Stats\Internes\MetierStats.cs : 1 730
// lignes, environ 150 requetes, qui remplissent un classeur Excel), pas encore
// reprises.
//
// Porte depuis Components/Ecrans/EcranStatistiques.razor (lot 4) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// periodes partent de la date du POSTE (DateTime.Today), comme avant.

import { useState } from 'react'
import { Exemple } from './formes/commun'

type VoletStat = 'Internes' | 'Web' | 'RepartitionsJcOp' | 'ActivitesCa' | 'ActivitesSalaries' | 'Semestrielles'

// Dans l'ordre de declaration du dictionnaire C#.
const Volets: ReadonlyArray<readonly [VoletStat, string]> = [
  ['Internes', 'Statistiques internes'],
  ['Web', 'Statistiques web'],
  ['RepartitionsJcOp', 'Repartitions des JC et OP'],
  ['ActivitesCa', 'Ouverture par activites : par chiffre d\'affaire'],
  ['ActivitesSalaries', 'Ouverture par activites : par nombre de salaries'],
  ['Semestrielles', 'Statistiques semestrielles'],
]

interface LigneStat {
  libelle: string
  courant: number
  precedent: number
}

const ligne = (libelle: string, courant: number, precedent: number): LigneStat => ({ libelle, courant, precedent })

const Donnees: Record<VoletStat, LigneStat[]> = {
  Internes: [
    ligne('Immatriculations', 184, 171),
    ligne('Modifications', 412, 438),
    ligne('Radiations', 96, 102),
    ligne('Depots d\'actes', 533, 489),
    ligne('Comptes annuels', 267, 254),
  ],
  Web: [
    ligne('Demandes deposees en ligne', 621, 547),
    ligne('Comptes crees', 88, 74),
    ligne('Documents telecharges', 1450, 1312),
    ligne('Paiements aboutis', 598, 521),
  ],
  RepartitionsJcOp: [
    ligne('Jugements de cloture', 58, 61),
    ligne('Ordonnances du president', 143, 128),
    ligne('Jugements d\'ouverture', 41, 37),
    ligne('Ordonnances de designation', 77, 69),
  ],
  ActivitesCa: [
    ligne('Moins de 100 k EUR', 92, 85),
    ligne('100 a 500 k EUR', 64, 71),
    ligne('500 k a 2 M EUR', 28, 24),
    ligne('Plus de 2 M EUR', 11, 9),
    ligne('Chiffre d\'affaire inconnu', 17, 22),
  ],
  ActivitesSalaries: [
    ligne('Aucun salarie', 118, 104),
    ligne('1 a 5 salaries', 63, 68),
    ligne('6 a 20 salaries', 24, 21),
    ligne('Plus de 20 salaries', 8, 7),
  ],
  Semestrielles: [
    ligne('Dossiers traites au semestre', 1264, 1187),
    ligne('Delai moyen en jours', 3, 4),
    ligne('Procedures collectives', 102, 94),
    ligne('Postes de travail declares', 46, 44),
  ],
}

function enteteLibelle(volet: VoletStat): string {
  switch (volet) {
    case 'ActivitesCa': return 'Tranche de chiffre d\'affaire'
    case 'ActivitesSalaries': return 'Tranche d\'effectif'
    case 'RepartitionsJcOp': return 'Nature de la decision'
    default: return 'Indicateur'
  }
}

function ecart(l: LigneStat): string {
  const d = l.courant - l.precedent
  return d > 0 ? `+${d}` : String(d)
}

/** Une date locale au format d'un <input type="date"> (aaaa-mm-jj). */
function iso(annee: number, mois: number, jour: number): string {
  return `${String(annee).padStart(4, '0')}-${String(mois).padStart(2, '0')}-${String(jour).padStart(2, '0')}`
}

export function EcranStatistiques() {
  const [volet, setVolet] = useState<VoletStat>('Internes')
  // DateTime.Today : la date du poste, pas celle du jeu de demonstration.
  const [dateDebut, setDateDebut] = useState(() => iso(new Date().getFullYear(), 1, 1))
  const [dateFin, setDateFin] = useState(() => {
    const d = new Date()
    return iso(d.getFullYear(), d.getMonth() + 1, d.getDate())
  })
  const [niveau, setNiveau] = useState('Greffe')
  // Le @bind d'un int : seul le texte saisi s'affiche.
  const [nombrePc, setNombrePc] = useState('46')

  const lignes = Donnees[volet]
  const titre = Volets.find(([cle]) => cle === volet)![1]

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Periode</div>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Date de debut</span>
            <input className="rig-saisie-champ__saisie" type="date" value={dateDebut} onChange={(e) => setDateDebut(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Date de fin</span>
            <input className="rig-saisie-champ__saisie" type="date" value={dateFin} onChange={(e) => setDateFin(e.target.value)} />
          </label>
        </div>

        {volet !== 'Semestrielles' ? (
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Niveau</span>
            <select className="rig-saisie-champ__saisie" value={niveau} onChange={(e) => setNiveau(e.target.value)}>
              <option value="Greffe">Greffe</option>
              <option value="Chambre">Chambre</option>
              <option value="Section">Section</option>
            </select>
          </label>
        ) : (
          // « Nombre de PC » n'existe QUE dans les statistiques semestrielles
          // (ult_LblNbPc).
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nombre de PC</span>
            <input className="rig-saisie-champ__saisie" type="number" min="0" value={nombrePc} onChange={(e) => setNombrePc(e.target.value)} />
          </label>
        )}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Volets</div>
          {Volets.map(([cle, libelle]) => (
            <button key={cle} className={`rig-bascule${volet === cle ? ' rig-bascule--actif' : ''}`}
                    onClick={() => setVolet(cle)}>
              <span className="rig-bascule__temoin"></span>
              <span>{libelle}</span>
            </button>
          ))}
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">{titre}</div>
          <span className="rig-resultats-proc__compte">{lignes.length} ligne(s)</span>
        </div>
        <p className="rig-eg-note" role="note">
          <Exemple /> Chiffres de démonstration : les statistiques de RIG v2 (classeur Excel de MetierStats.cs) ne sont pas encore calculées dans la base du greffe.
        </p>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>{enteteLibelle(volet)}</th>
                <th className="rig-table__centre">Periode en cours</th>
                <th className="rig-table__centre">Periode precedente</th>
                <th className="rig-table__centre">Ecart</th>
              </tr>
            </thead>
            <tbody>
              {lignes.map((l) => (
                <tr key={l.libelle}>
                  <td>{l.libelle}</td>
                  <td className="rig-table__mono rig-table__centre">{l.courant}</td>
                  <td className="rig-table__mono rig-table__centre">{l.precedent}</td>
                  <td className="rig-table__mono rig-table__centre">{ecart(l)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
