// Ecran de la procedure PROC_AUDIENCE (RIG v2 : « Creer une Audience »,
// etape ETP_CREATION_AUDIENCE). Les champs sont ceux d'OPE_CREATION_AUDIENCE :
// ULTDateAudience, UltHeureAudience, ULTCBTypeAudience, ULTCBChambre,
// ULTCBSection, ULTCheckBPublique, ULTCheckBChoixWeb, plus le bloc
// OPE_TYPE_CLASSEMENT_ADMIS et le temoin ult_LabelMailActive.
//
// Porte depuis Components/Ecrans/EcranAudiences.razor (lot 5) : meme balisage,
// memes classes CSS. La date proposee (JeuDemo.Aujourdhui + 7 jours) vient de
// Pont/EcranAudiences.cs ; le reste etait declare dans l'ecran et l'a suivi.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { dateCourte } from '../criteres'

export interface AudienceLigne {
  /** aaaa-mm-jj */
  date: string
  heure: string
  type: string
  chambre: string
  section: string
  publique: boolean
  web: boolean
  nbAffaires: number
}

const TypesAudience = [
  'Audience de procedure collective', 'Audience de reference', 'Audience de plaidoirie',
  'Audience de conciliation', 'Audience du juge-commissaire',
]

const Classements = ['Insuffisance d\'actif', 'Extinction du passif', 'Conversion en liquidation', 'Report']

const Programmees: AudienceLigne[] = [
  { date: '2026-09-15', heure: '09:00', type: 'Audience de procedure collective', chambre: '1re', section: 'A', publique: true, web: true, nbAffaires: 14 },
  { date: '2026-09-22', heure: '10:15', type: 'Audience du juge-commissaire', chambre: '1re', section: 'A', publique: false, web: false, nbAffaires: 6 },
  { date: '2026-09-29', heure: '14:00', type: 'Audience de plaidoirie', chambre: '2e', section: 'B', publique: true, web: true, nbAffaires: 9 },
]

// Le @bind d'un TimeOnly ecrit « HH:mm:ss » dans <input type="time"> ; une
// heure illisible (champ efface) ne remet pas l'heure a zero.
const heureLisible = (texte: string): string | null =>
  /^\d{2}:\d{2}(:\d{2})?$/.test(texte) ? texte.slice(0, 5) : null

export function EcranAudiences() {
  const [programmees, setProgrammees] = useState<AudienceLigne[]>(Programmees)
  // null tant que le pont n'a pas repondu (ou une fois la date effacee).
  const [dateAudience, setDateAudience] = useState<string | null>(null)
  const [heureTexte, setHeureTexte] = useState('09:00:00')
  const [heure, setHeure] = useState('09:00')
  const [typeAudience, setTypeAudience] = useState(TypesAudience[0]!)
  const [chambre, setChambre] = useState('1re')
  const [section, setSection] = useState('A')
  const [publique, setPublique] = useState(true)
  const [choixWeb, setChoixWeb] = useState(false)
  const mailActif = true
  const [admis, setAdmis] = useState<ReadonlySet<string>>(new Set(['Insuffisance d\'actif']))

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranAudiences.DateProposee')
      .then((d) => { if (!perime) setDateAudience((avant) => avant ?? d) })
    return () => { perime = true }
  }, [])

  function saisirHeure(texte: string) {
    setHeureTexte(texte)
    const h = heureLisible(texte)
    if (h !== null) setHeure(h)
  }

  function basculerAdmis(c: string) {
    setAdmis((avant) => {
      const s = new Set(avant)
      if (!s.delete(c)) s.add(c)
      return s
    })
  }

  function creer() {
    if (!dateAudience) return
    const neuve: AudienceLigne = {
      date: dateAudience, heure, type: typeAudience, chambre, section,
      publique, web: choixWeb, nbAffaires: 0,
    }
    // List.Sort par date ; a date egale, l'ordre d'arrivee est garde.
    setProgrammees((avant) => [...avant, neuve].sort((x, y) => x.date.localeCompare(y.date)))
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Creation d'une audience</div>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Date d'audience</span>
            <input className="rig-saisie-champ__saisie" type="date" value={dateAudience ?? ''}
                   onChange={(e) => setDateAudience(e.target.value || null)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Heure de debut</span>
            <input className="rig-saisie-champ__saisie" type="time" value={heureTexte}
                   onChange={(e) => saisirHeure(e.target.value)} />
          </label>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type d'audience</span>
          <select className="rig-saisie-champ__saisie" value={typeAudience}
                  onChange={(e) => setTypeAudience(e.target.value)}>
            {TypesAudience.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Chambre</span>
            <select className="rig-saisie-champ__saisie" value={chambre} onChange={(e) => setChambre(e.target.value)}>
              <option>1re</option><option>2e</option><option>3e</option>
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Section</span>
            <select className="rig-saisie-champ__saisie" value={section} onChange={(e) => setSection(e.target.value)}>
              <option>A</option><option>B</option><option>C</option>
            </select>
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${publique ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setPublique(!publique)}>
            <span className="rig-bascule__temoin"></span>
            <span>Audience publique</span>
          </button>
          <button className={`rig-bascule${choixWeb ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setChoixWeb(!choixWeb)}>
            <span className="rig-bascule__temoin"></span>
            <span>Publiee sur le web</span>
          </button>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Types de classement admis</div>
          {Classements.map((c) => (
            <button key={c} className={`rig-bascule${admis.has(c) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerAdmis(c)}>
              <span className="rig-bascule__temoin"></span>
              <span>{c}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" onClick={creer}>Creer l'audience</button>
        </div>
        <span className={`rig-etat rig-etat--${mailActif ? 'ok' : 'attente'}`}>
          {mailActif ? 'Envoi de mail actif' : 'Envoi de mail inactif'}
        </span>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Audiences programmees</div>
          <span className="rig-resultats-proc__compte">{programmees.length} audience(s)</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Date d'audience</th>
                <th>Heure de debut</th>
                <th>Type d'audience</th>
                <th>Chambre</th>
                <th>Section</th>
                <th className="rig-table__centre">Publique</th>
                <th className="rig-table__centre">Web</th>
                <th className="rig-table__centre">Affaires</th>
              </tr>
            </thead>
            <tbody>
              {programmees.map((a, i) => (
                <tr key={i}>
                  <td className="rig-table__mono">{dateCourte(a.date)}</td>
                  <td className="rig-table__mono">{a.heure}</td>
                  <td>{a.type}</td>
                  <td className="rig-table__mono">{a.chambre}</td>
                  <td className="rig-table__mono">{a.section}</td>
                  <td className="rig-table__centre">{a.publique ? 'oui' : ''}</td>
                  <td className="rig-table__centre">{a.web ? 'oui' : ''}</td>
                  <td className="rig-table__centre">{a.nbAffaires}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
