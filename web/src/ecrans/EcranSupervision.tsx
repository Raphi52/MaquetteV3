// Ecran « Supervision » : « a maquetter » dans le carnet (SOC-011 T04, SOC-017 T04), reserve a l'exploitation et a
// l'assistance d'Amitel. Sans origine RIG v2 : la V2 n'a pas d'ecran ; on y lit a la main les tables COMMUN_RIG
// ETAT_SERVICE et LOG_RIG_ERREUR, les fichiers de journal du serveur de l'agent (C:\rig\data\log, et le partage du
// serveur du territoire pour l'outre-mer), et l'on relance un service par l'outil MaintenanceServices.
//
// A gauche, retrouver une erreur : par l'identifiant de suivi que l'agent a lu dans son message (tout le parcours,
// SOC-017 CA02), ou par greffe, compte et periode. A droite, quatre onglets : les 40 greffes (base V3, migrations,
// echecs de numerotation de l'ancien RIG, SOC-011 CA03), les traitements publies par chaque serveur, avec la relance
// d'un traitement arrete (CA04), les alertes envoyees a l'exploitation (CA02) et le journal technique (detail et pile
// d'une erreur au clic). Un cinquieme (lot 17, SOC-022 CA06) : les defauts signales par les agents depuis la V3, avec
// leur identifiant de suivi, l'ecran, le greffe, la version et la phrase de l'agent ; la derniere erreur suivie qu'un
// defaut emporte ramene son parcours au clic. L'identifiant d'un defaut se cherche comme celui d'une erreur. Les regles vivent dans le serveur (Serveur/Socle/Supervision, prouvees par
// tests/SocleSupervisionCheck) ; tout passe par Pont/EcranSupervision.cs.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'
import { quand } from './EcranComptes'

export interface EtatGreffe {
  code: string
  nom: string
  estOutreMer: boolean
  serveur: string
  sante: 'ok' | 'pas creee' | 'injoignable' | 'migrations en retard' | 'non verifiee'
  detail: string | null
  migrationsJouees: number | null
  derniereMigration: string | null
  echecs: number | null
  premierEchec: string | null
  dernierEchec: string | null
  echecsDetail: string | null
  verifieLe: string | null
}

export interface EtatTraitement {
  serveur: string
  traitement: string
  etat: 'en marche' | 'travaux en cours' | 'en attente' | 'en erreur' | 'arrete'
  enCours: number
  enAttente: number
  enErreur: number
  codeGreffe: string | null
  message: string | null
  publieLe: string
  fileBaisseLe: string | null
}

export interface AlerteExploitation {
  id: number
  regle: string
  codeGreffe: string | null
  traitement: string
  erreur: string
  ouverteLe: string
  envoyeeLe: string | null
  canal: string | null
  fermeeLe: string | null
}

export interface ErreurTechnique {
  id: number
  suivi: string
  instant: string
  niveau: 'grave' | 'erreur' | 'avertissement'
  origine: 'ecran' | 'serveur' | 'traitement'
  codeGreffe: string | null
  compte: string | null
  serveur: string
  programme: string
  type: string | null
  message: string
  pile: string | null
}

export interface DefautSignale {
  id: number
  suivi: string
  instant: string
  codeGreffe: string | null
  compte: string | null
  poste: string
  ecran: string
  codeEcran: string | null
  version: string | null
  heurePoste: string | null
  derniereErreur: string | null
  phrase: string
}

export interface EtatSupervision {
  dernierPassage: string | null
  greffes: EtatGreffe[]
  traitements: EtatTraitement[]
  alertes: AlerteExploitation[]
}

export interface VueEcranSupervision {
  mode: 'serveur' | 'demo' | 'refuse' | 'erreur'
  message: string | null
  echec: boolean
  etat: EtatSupervision | null
  erreurs: ErreurTechnique[]
  /** Les defauts signales depuis la V3 (SOC-022 CA06) ; absent d'une vue de refus. */
  defauts?: DefautSignale[] | null
}

type Onglet = 'greffes' | 'traitements' | 'alertes' | 'erreurs' | 'defauts'

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const GrilleRepartie = { tableLayout: 'fixed' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Aide = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)' } as const
const Pile = {
  margin: 'var(--rig-esp-2) var(--rig-esp-3)', padding: 'var(--rig-esp-2)', maxHeight: '220px', overflow: 'auto',
  fontSize: 'var(--rig-fs-dense)', whiteSpace: 'pre-wrap', background: 'var(--rig-fond-2)', borderRadius: '6px',
} as const

/** La pastille d'etat d'une base de greffe. */
export function pastilleSante(sante: EtatGreffe['sante']): { classe: string; libelle: string } {
  switch (sante) {
    case 'ok': return { classe: 'ok', libelle: 'Répond' }
    case 'injoignable': return { classe: 'alerte', libelle: 'Injoignable' }
    case 'migrations en retard': return { classe: 'alerte', libelle: 'Migrations en retard' }
    case 'pas creee': return { classe: '', libelle: 'Pas encore créée' }
    default: return { classe: 'attente', libelle: 'Non vérifiée' }
  }
}

/** La pastille d'etat d'un traitement (les etats de SOC-011 CA01). */
export function pastilleTraitement(etat: EtatTraitement['etat']): { classe: string; libelle: string } {
  switch (etat) {
    case 'en marche': return { classe: 'ok', libelle: 'En marche' }
    case 'travaux en cours': return { classe: 'action', libelle: 'Travaux en cours' }
    case 'en attente': return { classe: 'attente', libelle: 'En attente' }
    case 'en erreur': return { classe: 'alerte', libelle: 'En erreur' }
    default: return { classe: 'alerte', libelle: 'Arrêté' }
  }
}

const LibellesRegles: Record<string, string> = {
  'traitement-en-erreur': 'Traitement en erreur',
  'file-bloquee': 'File bloquée',
  'base-injoignable': 'Base injoignable',
  'migrations-en-retard': 'Migrations en retard',
  'echec-numerotation': 'Échec de numérotation',
}

function Pastille({ p }: { p: { classe: string; libelle: string } }) {
  return <span className={`rig-etat${p.classe ? ` rig-etat--${p.classe}` : ''}`}>{p.libelle}</span>
}

export function EcranSupervision() {
  const [vue, setVue] = useState<VueEcranSupervision | null>(null)
  const [onglet, setOnglet] = useState<Onglet>('greffes')
  const [filtre, setFiltre] = useState('')
  const [suivi, setSuivi] = useState('')
  const [greffe, setGreffe] = useState('')
  const [compte, setCompte] = useState('')
  const [du, setDu] = useState('')
  const [au, setAu] = useState('')
  const [choisie, setChoisie] = useState<number | null>(null)
  const [defautChoisi, setDefautChoisi] = useState<number | null>(null)
  const [occupe, setOccupe] = useState(false)

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranSupervision>('EcranSupervision.Ouvrir').then((v) => { if (vivant) setVue(v) })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement de la supervision…</p></div>

  if (vue.mode === 'refuse' || vue.mode === 'erreur' || !vue.etat) {
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete"><div className="rig-kicker">Supervision</div></div>
          <p style={Message} role="alert">
            <span className="rig-etat rig-etat--alerte">{vue.mode === 'refuse' ? 'Accès refusé' : 'Serveur injoignable'}</span>{' '}{vue.message}
          </p>
        </section>
      </div>
    )
  }

  const etat = vue.etat
  const lecture = vue.mode === 'demo'
  const ouvertes = etat.alertes.filter((a) => a.fermeeLe === null)
  const outreMer = etat.greffes.filter((g) => g.estOutreMer).length
  const enPanne = etat.greffes.filter((g) => g.sante === 'injoignable' || g.sante === 'migrations en retard').length
  let greffes = etat.greffes
  if (!estBlanc(filtre)) {
    const t = filtre.trim()
    greffes = greffes.filter((g) => contientSansCasse(g.code, t) || contientSansCasse(g.nom, t) || contientSansCasse(g.serveur, t))
  }
  const erreur = vue.erreurs.find((e) => e.id === choisie) ?? null
  const defauts = vue.defauts ?? []
  const defaut = defauts.find((d) => d.id === defautChoisi) ?? null

  const agir = async (geste: () => Promise<VueEcranSupervision>, apres?: (v: VueEcranSupervision) => Onglet) => {
    setOccupe(true)
    try {
      const v = await geste()
      setVue(v)
      if (apres) setOnglet(apres(v))
    } finally { setOccupe(false) }
  }
  const chercher = (identifiant = suivi) => {
    setChoisie(null)
    setDefautChoisi(null)
    void agir(() => appeler<VueEcranSupervision>('EcranSupervision.Rechercher', {
      suivi: identifiant.trim() || null, greffe: greffe.trim() || null, compte: compte.trim() || null, du: du || null, au: au || null,
    }), (v) => (
      // L'identifiant d'un defaut signale (aucune erreur sous lui) mene a l'onglet des defauts.
      !estBlanc(identifiant) && v.erreurs.length === 0 && (v.defauts?.length ?? 0) > 0 ? 'defauts' : 'erreurs'))
  }
  /** La derniere erreur suivie qu'un defaut emporte : son parcours, dans l'onglet des erreurs. */
  const parcoursDe = (identifiant: string) => { setSuivi(identifiant); chercher(identifiant) }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Retrouver une erreur</div>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Identifiant de suivi</span>
          <input className="rig-saisie-champ__saisie rig-table__mono" placeholder="7KQ2-M9XP" value={suivi}
                 onChange={(e) => setSuivi(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') chercher() }} />
        </label>
        <p style={Aide}>Celui que l'agent a lu dans son message d'erreur : il ramène tout le parcours.</p>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Greffe</span>
          <input className="rig-saisie-champ__saisie" value={greffe} onChange={(e) => setGreffe(e.target.value)} />
        </label>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Compte</span>
          <input className="rig-saisie-champ__saisie" value={compte} onChange={(e) => setCompte(e.target.value)} />
        </label>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Du</span>
          <input className="rig-saisie-champ__saisie" type="date" value={du} onChange={(e) => setDu(e.target.value)} />
        </label>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Au</span>
          <input className="rig-saisie-champ__saisie" type="date" value={au} onChange={(e) => setAu(e.target.value)} />
        </label>
        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" disabled={occupe} onClick={() => chercher()}>Chercher</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-onglets">
            <button className={`rig-onglet${onglet === 'greffes' ? ' rig-onglet--actif' : ''}`} onClick={() => setOnglet('greffes')}>
              Greffes ({etat.greffes.length})
            </button>
            <button className={`rig-onglet${onglet === 'traitements' ? ' rig-onglet--actif' : ''}`} onClick={() => setOnglet('traitements')}>
              Traitements ({etat.traitements.length})
            </button>
            <button className={`rig-onglet${onglet === 'alertes' ? ' rig-onglet--actif' : ''}`} onClick={() => setOnglet('alertes')}>
              Alertes ({ouvertes.length})
            </button>
            <button className={`rig-onglet${onglet === 'erreurs' ? ' rig-onglet--actif' : ''}`} onClick={() => setOnglet('erreurs')}>
              Erreurs ({vue.erreurs.length})
            </button>
            <button className={`rig-onglet${onglet === 'defauts' ? ' rig-onglet--actif' : ''}`} onClick={() => setOnglet('defauts')}>
              Défauts ({defauts.length})
            </button>
          </div>
          <span className="rig-resultats-proc__compte">
            {lecture ? 'Démonstration' : etat.dernierPassage ? `Dernier passage ${quand(etat.dernierPassage)}` : 'Supervision pas encore passée'}
          </span>
        </div>

        {lecture && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Démonstration</span>
            {' '}Sans serveur : les 40 greffes réels, avec des états, des alertes et des erreurs inventés, en lecture seule.
          </p>
        )}
        {vue.message && (
          <p style={Message} role={vue.echec ? 'alert' : 'status'}>
            <span className={`rig-etat rig-etat--${vue.echec ? 'alerte' : 'ok'}`}>{vue.echec ? 'Refusé' : 'Fait'}</span>
            {' '}{vue.message}
          </p>
        )}
        {ouvertes.length > 0 && onglet !== 'alertes' && (
          <p style={Message}>
            <span className="rig-etat rig-etat--alerte">{ouvertes.length} alerte(s)</span>
            {' '}{ouvertes[0]!.erreur}
          </p>
        )}

        {onglet === 'greffes' && (
          <>
            <p style={Message}>
              {etat.greffes.length} greffe(s), dont {outreMer} d'outre-mer — {enPanne} à surveiller.
            </p>
            <label className="rig-saisie-champ" style={{ margin: 'var(--rig-esp-2) var(--rig-esp-3)' }}>
              <span className="rig-saisie-champ__libelle">Greffe ou serveur</span>
              <input className="rig-saisie-champ__saisie" value={filtre} onChange={(e) => setFiltre(e.target.value)} />
            </label>
            <div className="rig-table-cadre">
              <table className="rig-table" style={GrilleRepartie}>
                <colgroup>
                  <col style={{ width: '58px' }} /><col /><col style={{ width: '140px' }} /><col style={{ width: '170px' }} />
                  {/* « ÉCHECS NUMÉROTATION » finissait par « … » a 150 px (capture du 21/11, 1500 px de large). */}
                  <col style={{ width: '96px' }} /><col style={{ width: '186px' }} /><col style={{ width: '124px' }} />
                </colgroup>
                <thead>
                  <tr>{['Code', 'Greffe', 'Serveur', 'Base V3', 'Migrations', 'Échecs numérotation', 'Vérifié le'].map((t) =>
                    <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
                </thead>
                <tbody>
                  {greffes.map((g) => (
                    <tr key={g.code}>
                      <td className="rig-table__mono">{g.code}</td>
                      <td style={Coupe} title={g.nom}>{g.nom}{g.estOutreMer ? ' · outre-mer' : ''}</td>
                      <td className="rig-table__mono" style={Coupe} title={g.serveur}>{g.serveur}</td>
                      <td title={g.detail ?? ''}><Pastille p={pastilleSante(g.sante)} /></td>
                      <td className="rig-table__mono" title={g.derniereMigration ?? ''}>{g.migrationsJouees ?? '—'}</td>
                      <td title={g.echecsDetail ?? ''}>
                        {g.echecs === null ? <span className="rig-etat rig-etat--attente">Non relevés</span>
                          : g.echecs > 0 ? <span className="rig-etat rig-etat--alerte">{g.echecs} échec(s)</span>
                          : <span className="rig-etat rig-etat--ok">Aucun</span>}
                      </td>
                      <td>{g.verifieLe ? quand(g.verifieLe) : '—'}</td>
                    </tr>
                  ))}
                  {greffes.length === 0 && <tr><td colSpan={7} className="rig-table__vide">Aucun greffe ne répond à ce filtre.</td></tr>}
                </tbody>
              </table>
            </div>
          </>
        )}

        {onglet === 'traitements' && (
          <div className="rig-table-cadre">
            <table className="rig-table" style={GrilleRepartie}>
              <colgroup>
                <col style={{ width: '120px' }} /><col style={{ width: '160px' }} /><col style={{ width: '132px' }} />
                <col style={{ width: '70px' }} /><col style={{ width: '78px' }} /><col style={{ width: '70px' }} /><col /><col style={{ width: '96px' }} />
              </colgroup>
              <thead>
                <tr>{['Serveur', 'Traitement', 'État', 'En cours', 'En file', 'Échoués', 'Dernier message', ''].map((t, i) =>
                  <th key={i} title={t} style={Coupe}>{t}</th>)}</tr>
              </thead>
              <tbody>
                {etat.traitements.map((t) => (
                  <tr key={`${t.serveur}-${t.traitement}`}>
                    <td className="rig-table__mono" style={Coupe} title={t.serveur}>{t.serveur}</td>
                    <td style={Coupe} title={t.traitement}>{t.traitement}</td>
                    <td><Pastille p={pastilleTraitement(t.etat)} /></td>
                    <td className="rig-table__mono">{t.enCours}</td>
                    <td className="rig-table__mono">{t.enAttente}</td>
                    <td className="rig-table__mono">{t.enErreur}</td>
                    <td style={Coupe} title={t.message ?? ''}>{t.codeGreffe ? `Greffe ${t.codeGreffe} — ` : ''}{t.message ?? '—'}</td>
                    <td>
                      {(t.etat === 'en erreur' || t.etat === 'arrete') && (
                        <button className="rig-bouton" disabled={lecture || occupe}
                                onClick={() => void agir(() => appeler<VueEcranSupervision>('EcranSupervision.Relancer', t.traitement))}>
                          Relancer
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
                {etat.traitements.length === 0 && <tr><td colSpan={8} className="rig-table__vide">Aucun état publié.</td></tr>}
              </tbody>
            </table>
          </div>
        )}

        {onglet === 'alertes' && (
          <div className="rig-table-cadre">
            <table className="rig-table" style={GrilleRepartie}>
              <colgroup>
                <col style={{ width: '124px' }} /><col style={{ width: '160px' }} /><col style={{ width: '58px' }} />
                <col style={{ width: '170px' }} /><col /><col style={{ width: '124px' }} />
              </colgroup>
              <thead>
                <tr>{['Ouverte le', 'Règle', 'Greffe', 'Traitement', 'Erreur', 'État'].map((t) => <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
              </thead>
              <tbody>
                {etat.alertes.map((a) => (
                  <tr key={a.id}>
                    <td>{quand(a.ouverteLe)}</td>
                    <td style={Coupe}>{LibellesRegles[a.regle] ?? a.regle}</td>
                    <td className="rig-table__mono">{a.codeGreffe ?? '—'}</td>
                    <td style={Coupe} title={a.traitement}>{a.traitement}</td>
                    <td style={Coupe} title={a.erreur}>{a.erreur}</td>
                    <td title={a.envoyeeLe ? `Envoyée le ${quand(a.envoyeeLe)} (${a.canal ?? '?'})` : 'Pas encore envoyée'}>
                      {a.fermeeLe ? <span className="rig-etat rig-etat--ok">Fermée</span>
                        : a.envoyeeLe ? <span className="rig-etat rig-etat--alerte">Envoyée</span>
                        : <span className="rig-etat rig-etat--attente">À envoyer</span>}
                    </td>
                  </tr>
                ))}
                {etat.alertes.length === 0 && <tr><td colSpan={6} className="rig-table__vide">Aucune alerte.</td></tr>}
              </tbody>
            </table>
          </div>
        )}

        {onglet === 'erreurs' && (
          <>
            <div className="rig-table-cadre">
              <table className="rig-table" style={GrilleRepartie}>
                <colgroup>
                  <col style={{ width: '124px' }} /><col style={{ width: '96px' }} /><col style={{ width: '96px' }} />
                  <col style={{ width: '58px' }} /><col style={{ width: '90px' }} /><col style={{ width: '190px' }} /><col />
                </colgroup>
                <thead>
                  <tr>{['Le', 'Suivi', 'Niveau', 'Greffe', 'Compte', 'Programme', 'Message'].map((t) => <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
                </thead>
                <tbody>
                  {vue.erreurs.map((e) => (
                    <tr key={e.id} onClick={() => setChoisie(e.id)} aria-selected={e.id === choisie}>
                      <td>{quand(e.instant)}</td>
                      <td className="rig-table__mono">{e.suivi}</td>
                      <td><span className={`rig-etat rig-etat--${e.niveau === 'avertissement' ? 'attente' : 'alerte'}`}>{e.niveau}</span></td>
                      <td className="rig-table__mono">{e.codeGreffe ?? '—'}</td>
                      <td style={Coupe} title={e.compte ?? ''}>{e.compte ?? '—'}</td>
                      <td className="rig-table__mono" style={Coupe} title={e.programme}>{e.programme}</td>
                      <td style={Coupe} title={e.message}>{e.message}</td>
                    </tr>
                  ))}
                  {vue.erreurs.length === 0 && <tr><td colSpan={7} className="rig-table__vide">Aucune erreur à montrer.</td></tr>}
                </tbody>
              </table>
            </div>
            {erreur && (
              <div aria-label="Détail de l'erreur">
                <p style={Message}>
                  <span className="rig-etat">{erreur.origine}</span>{' '}
                  {erreur.serveur} — {erreur.type ?? 'type inconnu'} — {erreur.message}
                </p>
                <pre style={Pile}>{erreur.pile ?? 'Pas de pile d\'appels.'}</pre>
              </div>
            )}
          </>
        )}

        {onglet === 'defauts' && (
          <>
            <p style={Message}>
              Signalés par les agents depuis la V3 (SOC-022) ; « RigV3.Passage beta defauts » les classe par la règle écrite.
            </p>
            <div className="rig-table-cadre">
              <table className="rig-table" style={GrilleRepartie} aria-label="Défauts signalés">
                <colgroup>
                  <col style={{ width: '124px' }} /><col style={{ width: '96px' }} /><col style={{ width: '58px' }} />
                  <col style={{ width: '90px' }} /><col style={{ width: '150px' }} /><col style={{ width: '110px' }} /><col />
                </colgroup>
                <thead>
                  <tr>{['Le', 'Suivi', 'Greffe', 'Compte', 'Écran', 'Dernière erreur', 'Phrase de l\'agent'].map((t) =>
                    <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
                </thead>
                <tbody>
                  {defauts.map((d) => (
                    <tr key={d.id} onClick={() => setDefautChoisi(d.id)} aria-selected={d.id === defautChoisi}>
                      <td>{quand(d.instant)}</td>
                      <td className="rig-table__mono">{d.suivi}</td>
                      <td className="rig-table__mono">{d.codeGreffe ?? '—'}</td>
                      <td style={Coupe} title={d.compte ?? ''}>{d.compte ?? '—'}</td>
                      <td style={Coupe} title={d.ecran}>{d.ecran}</td>
                      <td className="rig-table__mono">
                        {d.derniereErreur
                          ? <button className="rig-bouton" title={`Le parcours de l'erreur ${d.derniereErreur}`}
                                    onClick={(e) => { e.stopPropagation(); parcoursDe(d.derniereErreur!) }}>{d.derniereErreur}</button>
                          : '—'}
                      </td>
                      <td style={Coupe} title={d.phrase}>{d.phrase}</td>
                    </tr>
                  ))}
                  {defauts.length === 0 && <tr><td colSpan={7} className="rig-table__vide">Aucun défaut signalé.</td></tr>}
                </tbody>
              </table>
            </div>
            {defaut && (
              <div aria-label="Détail du défaut">
                <p style={Message}>
                  <span className="rig-etat rig-etat--attente">{'Suivi '}{defaut.suivi}</span>{' '}{defaut.phrase}
                </p>
                <p style={Message}>
                  {'Écran « '}{defaut.ecran}{' »'}{defaut.codeEcran ? ` (${defaut.codeEcran})` : ''}
                  {' — poste '}{defaut.poste}{' — version '}{defaut.version ?? '?'}
                  {' — heure du poste '}{defaut.heurePoste ? quand(defaut.heurePoste) : '?'}
                </p>
              </div>
            )}
          </>
        )}
      </section>
    </div>
  )
}
