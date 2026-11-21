// Ecran « Certification NF525 » : « a maquetter » dans le carnet (SOC-018 T03, CA07). Procedure RIG v2 : PROC_CERTIF
// (FORM_CERTIF : onglets « Journal non purgeable », « Journal purgeable », « Grands totaux Factures », « Grands totaux
// Encaissements », bouton « Purger journal periodique », etat « Integrite » ; sources dans la branche
// origin/Development de D:\RigApplication, Source\RIG\DLL\Processus\PROC_CERTIF, sans fiche de reference).
//
// A gauche, les criteres du journal des evenements : la partie (non purgeable, purgeable, tout), le type d'evenement
// et la periode (heure du greffe) ; puis le controle d'integrite des 7 chaines, et la purge de la SEULE partie
// purgeable d'un exercice dont l'archive fiscale est faite. A droite, l'etat des 7 chaines et trois onglets en lecture
// seule : le journal, les grands totaux des factures, ceux des encaissements. Les regles (signature, controle, purge
// apres archive) vivent dans le serveur (Serveur/Socle/Chaines, prouvees par tests/SocleChainesCheck) ; l'ecran montre
// sa reponse. Tout passe par Pont/EcranChaines.cs.
// fix-ok: fichier neuf, aucune correction a l'aveugle -- les retouches posent Entree dans un critere (decision du chef msg-e5e55277, regle lue dans tests/SoclePreferencesCheck/Program.cs:386, l'ecran inscrit dans EntreeLanceLaRecherche de Pont/EcranCorrespondances.cs).

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { toucheDeRecherche } from '../plan/clavier'
import { quand } from './EcranComptes'

export interface EtatChaine {
  code: string
  libelle: string
  maillons: number
  derniereSeq: number
  dernierMaillonLe: string | null
}

export interface EtatCertification {
  greffe: string
  ouverte: boolean
  active: boolean
  ouverteLe: string | null
  fuseau: string | null
  empreinteCle: string | null
  enAttente: number
  chaines: EtatChaine[]
  exercicesArchives: number[]
}

export interface CodeEvenement {
  code: number
  libelle: string
  purgeable: boolean
  exigibilite: string
}

export interface EvenementSigne {
  id: number
  instant: string
  code: number
  libelle: string
  detail: string
  auteur: string
  purgeable: boolean
  chaine: string | null
  exercice: number | null
  seq: number | null
}

export interface TotalParTaux {
  taux: number
  montantTtc: number
}

export interface GrandTotal {
  chaine: string
  type: string
  periode: string
  total: number
  cumulReel: number
  cumulAbsolu: number
  nbPieces: number
  calculeLe: string
  information: string | null
  seq: number
  parTaux: TotalParTaux[]
}

export interface BilanChaine {
  code: string
  libelle: string
  maillons: number
  valides: number
  defauts: string[]
  ruptures: string[]
}

export interface BilanIntegrite {
  le: string
  valeurProbante: boolean
  reserve: string | null
  signeesAvant: number
  chaines: BilanChaine[]
  archives: string[]
  evenementsEcrits: number[]
  integre: boolean
}

export interface FiltreJournal {
  partie: string | null
  code: number | null
  du: string | null
  au: string | null
}

export interface VueEcranChaines {
  mode: 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur'
  message: string | null
  echec: boolean
  greffe: string | null
  etat: EtatCertification | null
  filtre: FiltreJournal
  journal: EvenementSigne[]
  totaux: GrandTotal[]
  codes: CodeEvenement[]
  controle: BilanIntegrite | null
}

type Onglet = 'journal' | 'GTF' | 'GTE'

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const GrilleRepartie = { tableLayout: 'fixed' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis' } as const
const Aide = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)' } as const
const Bandeau = { display: 'flex', flexWrap: 'wrap', gap: 'var(--rig-esp-2)', padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const

/** « 1 284,60 € » */
export function euros(montant: number): string {
  return montant.toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' €'
}

/** La periode d'un grand total telle qu'on la lit : « 20/11/2026 », « 11/2026 », « exercice 2026 ». */
export function periodeLisible(type: string, periode: string): string {
  if (type === 'GTP' && periode.length === 8) return `${periode.slice(6, 8)}/${periode.slice(4, 6)}/${periode.slice(0, 4)}`
  if (type === 'GTM' && periode.length === 6) return `${periode.slice(4, 6)}/${periode.slice(0, 4)}`
  return `exercice ${periode}`
}

const NomType: Record<string, string> = { GTP: 'Jour', GTM: 'Mois', 'GTA/E': 'Exercice' }

export function EcranChaines() {
  const [vue, setVue] = useState<VueEcranChaines | null>(null)
  const [onglet, setOnglet] = useState<Onglet>('journal')
  const [partie, setPartie] = useState('')
  const [code, setCode] = useState('')
  const [du, setDu] = useState('')
  const [au, setAu] = useState('')
  const [exercice, setExercice] = useState(String(new Date().getFullYear() - 1))
  const [occupe, setOccupe] = useState(false)

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranChaines>('EcranChaines.Ouvrir').then((v) => { if (vivant) setVue(v) })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement des chaînes signées…</p></div>

  if (vue.mode === 'refuse' || vue.mode === 'erreur' || vue.mode === 'sans-greffe') {
    const etat = vue.mode === 'refuse' ? 'Accès refusé' : vue.mode === 'sans-greffe' ? 'Aucun greffe ouvert' : 'Serveur injoignable'
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete">
            <div className="rig-kicker">Certification NF525</div>
          </div>
          <p style={Message} role="alert">
            <span className="rig-etat rig-etat--alerte">{etat}</span>{' '}{vue.message}
          </p>
        </section>
      </div>
    )
  }

  const lecture = vue.mode === 'demo'
  const etat = vue.etat
  const filtre = (): FiltreJournal => ({
    partie: partie === '' ? null : partie,
    code: code === '' ? null : Number(code),
    du: du === '' ? null : du,
    au: au === '' ? null : au,
  })
  const jouer = async (methode: string, argument: unknown) => {
    setOccupe(true)
    try { setVue(await appeler<VueEcranChaines>(methode, argument)) } finally { setOccupe(false) }
  }
  const rechercher = () => { setOnglet('journal'); void jouer('EcranChaines.Rechercher', filtre()) }
  const controler = () => void jouer('EcranChaines.Controler', filtre())
  const purger = () => {
    setPartie('p'); setOnglet('journal')
    void jouer('EcranChaines.Purger', { exercice: Number(exercice) || 0 })
  }
  const totaux = vue.totaux.filter((t) => t.chaine === onglet)
  const archive = etat?.exercicesArchives.includes(Number(exercice)) ?? false
  const defauts = vue.controle
    ? vue.controle.chaines.flatMap((c) => [...c.defauts.map((d) => `${c.libelle} — ${d}`), ...c.ruptures.map((r) => `${c.libelle} — ${r}`)])
        .concat(vue.controle.archives.map((a) => `Archive fiscale — ${a}`))
    : []

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Journal des événements</div>
        {/* Entrée, F5 et F7 dans un critère lancent la recherche, comme « Rechercher » : la touche commune des écrans de
            recherche (web/src/plan/clavier.ts, lot 4 ; PROC_CERTIF de la V2 ; Pont/EcranCorrespondances.cs). */}
        <div onKeyDown={(e) => { if (toucheDeRecherche(e)) rechercher() }}>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Partie du journal</span>
          <select className="rig-saisie-champ__saisie" value={partie} onChange={(e) => setPartie(e.target.value)}>
            <option value="">Tout le journal</option>
            <option value="np">Non purgeable</option>
            <option value="p">Purgeable</option>
          </select>
        </label>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type d'événement</span>
          <select className="rig-saisie-champ__saisie" value={code} onChange={(e) => setCode(e.target.value)}>
            <option value="">Tous</option>
            {vue.codes.map((c) => <option key={c.code} value={String(c.code)}>{`${c.code} — ${c.libelle}`}</option>)}
          </select>
        </label>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Du</span>
          <input className="rig-saisie-champ__saisie" type="date" value={du} onChange={(e) => setDu(e.target.value)} />
        </label>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Au</span>
          <input className="rig-saisie-champ__saisie" type="date" value={au} onChange={(e) => setAu(e.target.value)} />
        </label>
        </div>
        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" disabled={occupe} onClick={rechercher}>Rechercher</button>
        </div>

        <div className="rig-kicker" style={{ margin: 'var(--rig-esp-4) 0 var(--rig-esp-3)' }}>Intégrité</div>
        <p style={Aide}>Refait la signature de chaque maillon des 7 chaînes et des archives ; un défaut s'écrit au journal (90, 95).</p>
        <div className="rig-criteres__actions">
          <button className="rig-bouton" disabled={lecture || occupe || !etat?.ouverte} onClick={controler}>Contrôler l'intégrité</button>
        </div>

        <div className="rig-kicker" style={{ margin: 'var(--rig-esp-4) 0 var(--rig-esp-3)' }}>Purge</div>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Exercice à purger</span>
          <input className="rig-saisie-champ__saisie" inputMode="numeric" value={exercice} onChange={(e) => setExercice(e.target.value)} />
        </label>
        <p style={Aide}>
          {archive
            ? `L'archive fiscale ${exercice} est faite : la partie purgeable de cet exercice peut être purgée.`
            : 'Seule la partie purgeable se purge, et seulement après l\'archive fiscale de l\'exercice.'}
        </p>
        <div className="rig-criteres__actions">
          <button className="rig-bouton" disabled={lecture || occupe || !etat?.ouverte} onClick={purger}>Purger le journal purgeable</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-onglets">
            <button className={`rig-onglet${onglet === 'journal' ? ' rig-onglet--actif' : ''}`} onClick={() => setOnglet('journal')}>
              Journal ({vue.journal.length})
            </button>
            <button className={`rig-onglet${onglet === 'GTF' ? ' rig-onglet--actif' : ''}`} onClick={() => setOnglet('GTF')}>
              Grands totaux des factures
            </button>
            <button className={`rig-onglet${onglet === 'GTE' ? ' rig-onglet--actif' : ''}`} onClick={() => setOnglet('GTE')}>
              Grands totaux des encaissements
            </button>
          </div>
          <span className="rig-resultats-proc__compte">
            {lecture ? 'Démonstration'
              : etat?.ouverte ? `Greffe ${vue.greffe ?? ''} — chaînes ouvertes le ${etat.ouverteLe ? quand(etat.ouverteLe) : '—'}`
              : `Greffe ${vue.greffe ?? ''} — chaînes pas encore ouvertes`}
          </span>
        </div>

        {lecture && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Démonstration</span>
            {' '}Sans serveur : des événements et des totaux inventés, en lecture seule.
          </p>
        )}
        {etat && !etat.ouverte && !lecture && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Pas ouvertes</span>
            {' '}Les chaînes signées de la V3 s'ouvrent au passage du greffe ; celles de l'ancien RIG restent consultables dans l'ancien RIG.
          </p>
        )}
        {vue.message && (
          <p style={Message} role={vue.echec ? 'alert' : 'status'}>
            <span className={`rig-etat rig-etat--${vue.echec ? 'alerte' : 'ok'}`}>{vue.echec ? 'Attention' : 'Fait'}</span>
            {' '}{vue.message}
          </p>
        )}
        {defauts.length > 0 && (
          <ul style={{ ...Aide, padding: '0 var(--rig-esp-3) var(--rig-esp-2) var(--rig-esp-5)' }}>
            {defauts.slice(0, 8).map((d) => <li key={d}>{d}</li>)}
          </ul>
        )}

        {etat?.ouverte && (
          <div style={Bandeau} aria-label="Les 7 chaînes signées">
            {etat.chaines.map((c) => (
              <span key={c.code} className="rig-etat" title={c.dernierMaillonLe ? `Dernier maillon le ${quand(c.dernierMaillonLe)}` : 'Aucun maillon'}>
                {c.libelle} : {c.maillons}
              </span>
            ))}
            {etat.enAttente > 0 && <span className="rig-etat rig-etat--attente">{etat.enAttente} événement(s) en attente de signature</span>}
          </div>
        )}

        {onglet === 'journal' && (
          <div className="rig-table-cadre">
            <table className="rig-table" style={GrilleRepartie}>
              <colgroup>
                <col style={{ width: '128px' }} /><col style={{ width: '56px' }} /><col style={{ width: '220px' }} /><col />
                <col style={{ width: '118px' }} /><col style={{ width: '150px' }} />
              </colgroup>
              <thead>
                <tr>{['Date', 'Code', 'Événement', 'Détail', 'Maillon', 'Auteur'].map((t) => <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
              </thead>
              <tbody>
                {vue.journal.map((e) => (
                  <tr key={e.id}>
                    <td>{quand(e.instant)}</td>
                    <td className="rig-table__mono">{e.code}</td>
                    <td style={Coupe} title={e.libelle}>{e.libelle}</td>
                    <td style={Coupe} title={e.detail}>{e.detail}</td>
                    <td title={e.purgeable ? 'Journal purgeable' : 'Journal non purgeable'}>
                      {e.seq === null
                        ? <span className="rig-etat rig-etat--attente">En attente</span>
                        : <span className="rig-etat">{e.purgeable ? `P ${e.exercice ?? ''} n° ${e.seq}` : `NP n° ${e.seq}`}</span>}
                    </td>
                    <td style={Coupe} title={e.auteur}>{e.auteur}</td>
                  </tr>
                ))}
                {vue.journal.length === 0 && (
                  <tr><td colSpan={6} className="rig-table__vide">Aucun événement ne répond à ces critères.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {onglet !== 'journal' && (
          <div className="rig-table-cadre">
            <table className="rig-table" style={GrilleRepartie}>
              <colgroup>
                <col style={{ width: '82px' }} /><col style={{ width: '120px' }} /><col style={{ width: '112px' }} /><col style={{ width: '128px' }} />
                <col style={{ width: '128px' }} /><col style={{ width: '64px' }} /><col /><col style={{ width: '72px' }} />
              </colgroup>
              <thead>
                <tr>{['Clôture', 'Période', 'Total', 'Cumul perpétuel', 'Cumul absolu', 'Pièces', onglet === 'GTF' ? 'Par taux de TVA' : 'Calculé le', 'Maillon']
                  .map((t) => <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
              </thead>
              <tbody>
                {totaux.map((t) => (
                  <tr key={`${t.chaine}-${t.type}-${t.periode}`}>
                    <td>{NomType[t.type] ?? t.type}</td>
                    <td title={t.information ?? ''}>{periodeLisible(t.type, t.periode)}</td>
                    <td className="rig-table__mono">{euros(t.total)}</td>
                    <td className="rig-table__mono">{euros(t.cumulReel)}</td>
                    <td className="rig-table__mono">{euros(t.cumulAbsolu)}</td>
                    <td>{t.nbPieces}</td>
                    <td style={Coupe}>
                      {onglet === 'GTF'
                        ? (t.parTaux.length === 0 ? '—' : t.parTaux.map((x) => `${x.taux.toLocaleString('fr-FR')} % : ${euros(x.montantTtc)}`).join(' · '))
                        : quand(t.calculeLe)}
                    </td>
                    <td className="rig-table__mono">{t.seq}</td>
                  </tr>
                ))}
                {totaux.length === 0 && (
                  <tr><td colSpan={8} className="rig-table__vide">Aucune période close pour l'instant.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
