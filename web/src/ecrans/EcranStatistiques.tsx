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
//   - STATS_INTERNES/Commun/OPE_DATES.cs : la periode, par defaut le mois
//     precedent complet (DoInitZoneSaisie, :8-14), refusee si le debut suit la fin.
//   - STATS_INTERNES/Commun/OPE_NIVEAU.cs : « Niveau » NAF, 1 a 4 (1 par defaut),
//     dans les deux volets d'activites seulement (OPE_OUVERTURE_ACTIVITES_*.Designer.cs).
//   - STATS_SEMESTRIELLES/OPE_STATS_SEMESTRIELLES.cs : deuxieme famille ; « Nombre
//     de PC » y est un libelle CALCULE (ult_LblNbPc, _MajNbPC :145-152), pas une saisie.
//
// Lot 9 du plan « RIG v3 100 % fonctionnel » (27/11/2026) : les chiffres sont CALCULES dans la base du greffe par le pont
// (Pont/EcranStatistiques.cs -> Serveur/Socle/EcransMain/LectureStatistiques.cs), avec les requetes de RIG v2
// (Outils\DLLEdition\RigRapport\Jud\Stats). RIG v2 les ecrit dans un classeur Excel ; la V3 les montre ici, tableau par
// tableau. Sans serveur : la demonstration du pont, marquee « exemple ». Les anciens chiffres en dur de cet ecran (et ses
// colonnes « periode precedente / ecart », ses niveaux Greffe / Chambre / Section) n'avaient pas de source V2 : retires.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { ProvenanceEcran, type ModeLecture } from '../provenance'

type VoletStat = 'Internes' | 'Web' | 'RepartitionsJcOp' | 'ActivitesCa' | 'ActivitesSalaries' | 'Semestrielles'

// Dans l'ordre des volets de v2.
const Volets: ReadonlyArray<readonly [VoletStat, string]> = [
  ['Internes', 'Statistiques internes'],
  ['Web', 'Statistiques web'],
  ['RepartitionsJcOp', 'Repartitions des JC et OP'],
  ['ActivitesCa', 'Ouverture par activites : par chiffre d\'affaire'],
  ['ActivitesSalaries', 'Ouverture par activites : par nombre de salaries'],
  ['Semestrielles', 'Statistiques semestrielles'],
]

export interface LigneStatVue {
  libelle: string
  valeurs: string[]
}

export interface TableauStatVue {
  titre: string
  colonnes: string[]
  lignes: LigneStatVue[]
  cause?: string | null
}

export interface VueStatistiques {
  tableaux: TableauStatVue[]
  /** Le message de RIG v2 quand la periode est refusee : rien n'est calcule. */
  refus?: string | null
  /** Le nombre de PC de la periode (volet semestriel). */
  nombrePc?: string | null
  mode?: ModeLecture
  message?: string | null
}

/** Une date locale au format d'un <input type="date"> (aaaa-mm-jj). */
function iso(d: Date): string {
  return `${String(d.getFullYear()).padStart(4, '0')}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** Le mois precedent complet (OPE_DATES.DoInitZoneSaisie) : du premier au dernier jour. */
export function moisPrecedent(aujourdhui: Date): { debut: string; fin: string } {
  const premier = new Date(aujourdhui.getFullYear(), aujourdhui.getMonth() - 1, 1)
  const dernier = new Date(aujourdhui.getFullYear(), aujourdhui.getMonth(), 0)
  return { debut: iso(premier), fin: iso(dernier) }
}

const avecNiveau = (v: VoletStat) => v === 'ActivitesCa' || v === 'ActivitesSalaries'

export function EcranStatistiques() {
  const [volet, setVolet] = useState<VoletStat>('Internes')
  const [periode] = useState(() => moisPrecedent(new Date()))
  const [dateDebut, setDateDebut] = useState(periode.debut)
  const [dateFin, setDateFin] = useState(periode.fin)
  const [niveau, setNiveau] = useState('1')
  // null tant que le pont n'a pas repondu : ni tableau ni « aucune ligne », qui seraient faux.
  const [vue, setVue] = useState<VueStatistiques | null>(null)

  useEffect(() => {
    let perime = false
    setVue(null)
    void appeler<VueStatistiques>('EcranStatistiques.Calculer', volet, dateDebut, dateFin, avecNiveau(volet) ? niveau : null)
      .then((v) => { if (!perime) setVue(v) })
      .catch((e: unknown) => { if (!perime) setVue({ tableaux: [], mode: 'erreur', message: e instanceof Error ? e.message : String(e) }) })
    // Seule la reponse du DERNIER volet / de la derniere periode compte.
    return () => { perime = true }
  }, [volet, dateDebut, dateFin, niveau])

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

        {avecNiveau(volet) && (
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Niveau</span>
            <select className="rig-saisie-champ__saisie" value={niveau} onChange={(e) => setNiveau(e.target.value)}>
              {['1', '2', '3', '4'].map((n) => <option key={n} value={n}>{n}</option>)}
            </select>
          </label>
        )}

        {volet === 'Semestrielles' && (
          // « Nombre de PC » : calcule sur la periode (ult_LblNbPc), pas saisi.
          <div className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nombre de PC</span>
            <output className="rig-saisie-champ__saisie" aria-label="Nombre de PC">{vue?.nombrePc ?? '…'}</output>
          </div>
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
          <span className="rig-resultats-proc__compte">{vue === null ? 'calcul…' : `${vue.tableaux.length} tableau(x)`}</span>
        </div>
        {vue && <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Statistiques" />}
        {vue?.refus && <p className="rig-eg-note" role="alert">{vue.refus}</p>}

        {/* Plusieurs tableaux : UN defilement pour tous. Chaque .rig-table-cadre (rig-accueil.css:1633, flex 1 1 auto,
            overflow auto) retrecirait sinon a quelques lignes, chacun avec sa barre. fix-ok: mesure le 27/11/2026 sur le bureau
            cache (capture y1, volet interne de 2000) -- « Statistiques judiciaires » montrait 4 lignes sur 10 et les clotures 1
            sur 4, alors que le serveur rendait les 10 et les 4 (39 + 13 + 1 + 1 = 54, le total du volet web). */}
        <div style={{ overflow: 'auto', flex: '1 1 auto', minHeight: 0 }}>
        {vue?.tableaux.map((t, i) => (
          <div key={`${t.titre}-${i}`} className="rig-table-cadre" style={{ flex: '0 0 auto', overflow: 'visible', marginBottom: 'var(--rig-esp-4)' }}>
            <div className="rig-kicker">{t.titre}</div>
            {t.cause && <p className="rig-eg-note" role="note">{t.cause}</p>}
            <table className="rig-table">
              <thead>
                <tr>
                  {t.colonnes.map((c, k) => <th key={k} className={k === 0 ? undefined : 'rig-table__centre'}>{c}</th>)}
                </tr>
              </thead>
              <tbody>
                {t.lignes.map((l, k) => (
                  <tr key={k}>
                    <td>{l.libelle}</td>
                    {l.valeurs.length === 1 && t.colonnes.length > 2
                      // Une ligne non calculee : sa cause s'etale sur les colonnes de chiffres.
                      ? <td className="rig-table__centre" colSpan={t.colonnes.length - 1}>{l.valeurs[0]}</td>
                      : l.valeurs.map((v, j) => <td key={j} className="rig-table__mono rig-table__centre">{v}</td>)}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ))}
        </div>
      </section>
    </div>
  )
}
