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
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 6) : les cinq listes de droite prennent les
// briques de composants/ListeModerne.tsx -- tete (titre de la liste, synthese, filtre Ctrl K sur la liste montree ; le
// champ « Greffe ou serveur » des greffes en est devenu le filtre), les cinq onglets devenus des puces de vues (meme
// geste : changer de liste, decision du lot 7), etats en point colore, regles et niveaux en pastille, comptes en
// avatar + nom, pied. Les traitements ont huit colonnes : « En cours » et le dernier message passent dans la ligne
// depliee par le chevron, avec « Relancer ». Le detail d'une erreur ou d'un defaut reste sous sa liste.
// fix-ok: application du socle des listes en plusieurs editions (lot 6), pas un correctif a l'aveugle.

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'
import { quand } from './EcranComptes'
import {
  CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe,
  teinteStatut, type Teinte,
} from '../composants/ListeModerne'

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

const TeinteRegle: Record<string, Teinte> = {
  'traitement-en-erreur': 'rouge', 'file-bloquee': 'orange', 'base-injoignable': 'violet', 'migrations-en-retard': 'ardoise',
  'echec-numerotation': 'bleu',
}
const TeinteNiveau: Record<ErreurTechnique['niveau'], Teinte> = { grave: 'rouge', erreur: 'orange', avertissement: 'ardoise' }

/** Le point d'etat d'une classe de la charte (rig-etat--*), celle que rendent pastilleSante et pastilleTraitement. */
function Etat({ p }: { p: { classe: string; libelle: string } }) {
  return <PointEtat teinte={teinteStatut(p.classe)}>{p.libelle}</PointEtat>
}

/** Un compte (login) en avatar + nom ; sans compte, un tiret. */
function CelluleCompte({ compte }: { compte: string | null }) {
  return compte ? <CelluleIdentite nom={compte} titre={compte} /> : <td>—</td>
}

/** Les titres des cinq listes, et l'indication de leur filtre rapide. */
const TitreOnglet: Record<Onglet, string> = {
  greffes: 'Greffes', traitements: 'Traitements', alertes: 'Alertes', erreurs: 'Erreurs', defauts: 'Défauts signalés',
}
const IndicationOnglet: Record<Onglet, string> = {
  greffes: 'Greffe ou serveur…', traitements: 'Aller à un traitement…', alertes: 'Aller à une alerte…',
  erreurs: 'Aller à une erreur…', defauts: 'Aller à un défaut…',
}

/** Ce que compte le pied de chaque liste. fix-ok: le pied lisait « 40 affiché(s) » sans dire quoi (capture du bureau caché, lot 6). */
const NomLigne: Record<Onglet, string> = {
  greffes: 'greffe(s)', traitements: 'traitement(s)', alertes: 'alerte(s)', erreurs: 'erreur(s)', defauts: 'défaut(s)',
}

/** Les colonnes des traitements : chevron, Serveur, Traitement, Etat, En file, Echoues, Relancer. */
const NbColonnesTraitements = 7

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
  // Le traitement deplie par son chevron (un a la fois).
  const [deplie, setDeplie] = useState<string | null>(null)

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
  // Le filtre rapide (Ctrl K) vaut pour la liste montree ; changer de liste le vide.
  const t = filtre.trim()
  const garde = (...champs: Array<string | null>) => estBlanc(t) || champs.some((c) => c !== null && contientSansCasse(c, t))
  const greffes = etat.greffes.filter((g) => garde(g.code, g.nom, g.serveur))
  const traitements = etat.traitements.filter((x) => garde(x.serveur, x.traitement, x.message, x.codeGreffe))
  const alertes = etat.alertes.filter((a) => garde(LibellesRegles[a.regle] ?? a.regle, a.codeGreffe, a.traitement, a.erreur))
  const erreurs = vue.erreurs.filter((e) => garde(e.suivi, e.codeGreffe, e.compte, e.programme, e.message))
  const outreMer = greffes.filter((g) => g.estOutreMer).length
  const enPanne = greffes.filter((g) => g.sante === 'injoignable' || g.sante === 'migrations en retard').length
  const erreur = vue.erreurs.find((e) => e.id === choisie) ?? null
  const tousDefauts = vue.defauts ?? []
  const defauts = tousDefauts.filter((d) => garde(d.suivi, d.codeGreffe, d.compte, d.ecran, d.phrase))
  const defaut = tousDefauts.find((d) => d.id === defautChoisi) ?? null
  const changerOnglet = (o: Onglet) => { setOnglet(o); setFiltre('') }
  const relancer = (traitement: string) => void agir(() => appeler<VueEcranSupervision>('EcranSupervision.Relancer', traitement))
  const relancable = (x: EtatTraitement) => x.etat === 'en erreur' || x.etat === 'arrete'

  // La synthese de chaque liste : son compte et un ou deux chiffres tires de ses lignes.
  const passage = lecture ? <b>Démonstration</b> : etat.dernierPassage ? `Dernier passage ${quand(etat.dernierPassage)}` : 'Supervision pas encore passée'
  const synthese: Record<Onglet, string> = {
    greffes: `${greffes.length} greffe(s), dont ${outreMer} d'outre-mer — ${enPanne} à surveiller`,
    traitements: `${traitements.length} traitement(s) · ${traitements.filter(relancable).length} en erreur ou arrêté(s) · `
      + `${traitements.reduce((n, x) => n + x.enAttente, 0)} en file`,
    alertes: `${alertes.length} alerte(s) · ${alertes.filter((a) => a.fermeeLe === null).length} ouverte(s)`,
    erreurs: `${erreurs.length} erreur(s) · ${erreurs.filter((e) => e.niveau === 'grave').length} grave(s)`,
    defauts: `${defauts.length} défaut(s) · ${defauts.filter((d) => d.derniereErreur !== null).length} avec une erreur suivie`,
  }
  const affiches: Record<Onglet, number> = {
    greffes: greffes.length, traitements: traitements.length, alertes: alertes.length, erreurs: erreurs.length, defauts: defauts.length,
  }

  const agir = async (geste: () => Promise<VueEcranSupervision>, apres?: (v: VueEcranSupervision) => Onglet) => {
    setOccupe(true)
    try {
      const v = await geste()
      setVue(v)
      if (apres) changerOnglet(apres(v))
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

      <ListeModerne etiquette={TitreOnglet[onglet]}>
        <EnteteListe titre={TitreOnglet[onglet]}
                     synthese={<>{passage}{' · '}{synthese[onglet]}</>}
                     filtre={{ valeur: filtre, changer: setFiltre, indication: IndicationOnglet[onglet] }} />

        <VuesListe vues={[
                     { cle: 'greffes', libelle: 'Greffes', compte: etat.greffes.length },
                     { cle: 'traitements', libelle: 'Traitements', compte: etat.traitements.length },
                     { cle: 'alertes', libelle: 'Alertes', compte: ouvertes.length },
                     { cle: 'erreurs', libelle: 'Erreurs', compte: vue.erreurs.length },
                     { cle: 'defauts', libelle: 'Défauts', compte: tousDefauts.length },
                   ]}
                   active={onglet} choisir={(cle) => changerOnglet(cle as Onglet)} />

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
          <div className="rig-table-cadre">
            <table className="rig-table rig-lm-table">
              <colgroup>
                <col style={{ width: '58px' }} /><col /><col style={{ width: '140px' }} /><col style={{ width: '170px' }} />
                {/* « ÉCHECS NUMÉROTATION » finissait par « … » a 150 px (capture du 21/11, 1500 px de large). */}
                <col style={{ width: '96px' }} /><col style={{ width: '186px' }} /><col style={{ width: '124px' }} />
              </colgroup>
              <thead>
                <tr>{['Code', 'Greffe', 'Serveur', 'Base V3', 'Migrations', 'Échecs numérotation', 'Vérifié le'].map((x) =>
                  <th key={x} title={x} style={Coupe} className={x === 'Migrations' ? 'rig-lm-droite' : undefined}>{x}</th>)}</tr>
              </thead>
              <tbody>
                {greffes.map((g) => (
                  <tr key={g.code}>
                    <td className="rig-table__mono">{g.code}</td>
                    <CelluleIdentite nom={g.nom} objet={g.estOutreMer ? 'outre-mer' : undefined} titre={`${g.nom}${g.estOutreMer ? ' · outre-mer' : ''}`} />
                    <td className="rig-table__mono" style={Coupe} title={g.serveur}>{g.serveur}</td>
                    <td title={g.detail ?? ''}><Etat p={pastilleSante(g.sante)} /></td>
                    <td className="rig-lm-droite rig-table__mono" title={g.derniereMigration ?? ''}>{g.migrationsJouees ?? '—'}</td>
                    <td title={g.echecsDetail ?? ''}>
                      {g.echecs === null ? <PointEtat teinte="orange">Non relevés</PointEtat>
                        : g.echecs > 0 ? <PointEtat teinte="rouge">{g.echecs} échec(s)</PointEtat>
                        : <PointEtat teinte="vert">Aucun</PointEtat>}
                    </td>
                    <td>{g.verifieLe ? quand(g.verifieLe) : '—'}</td>
                  </tr>
                ))}
                {greffes.length === 0 && <tr><td colSpan={7} className="rig-table__vide">Aucun greffe ne répond à ce filtre.</td></tr>}
              </tbody>
            </table>
          </div>
        )}

        {onglet === 'traitements' && (
          <div className="rig-table-cadre">
            <table className="rig-table rig-lm-table">
              <colgroup>
                <col style={{ width: '30px' }} /><col style={{ width: '150px' }} /><col /><col style={{ width: '150px' }} />
                <col style={{ width: '78px' }} /><col style={{ width: '78px' }} /><col style={{ width: '104px' }} />
              </colgroup>
              <thead>
                <tr>
                  <th aria-label="Deplier"></th>
                  {['Serveur', 'Traitement', 'État', 'En file', 'Échoués', ''].map((x, i) =>
                    <th key={i} title={x} style={Coupe} className={x === 'En file' || x === 'Échoués' ? 'rig-lm-droite' : undefined}>{x}</th>)}
                </tr>
              </thead>
              <tbody>
                {traitements.map((x) => {
                  const cle = `${x.serveur}-${x.traitement}`
                  const ouvert = deplie === cle
                  return (
                    <Fragment key={cle}>
                      <tr className={ouvert ? 'rig-lm-ouverte' : undefined}>
                        <CelluleChevron ouverte={ouvert} basculer={() => setDeplie(ouvert ? null : cle)} />
                        <td className="rig-table__mono" style={Coupe} title={x.serveur}>{x.serveur}</td>
                        <td style={Coupe} title={x.traitement}>{x.traitement}</td>
                        <td><Etat p={pastilleTraitement(x.etat)} /></td>
                        <td className="rig-lm-droite rig-table__mono">{x.enAttente}</td>
                        <td className="rig-lm-droite rig-table__mono">{x.enErreur}</td>
                        <td>
                          {/* Depliee, la ligne porte « Relancer » dans son detail : un seul bouton a l'ecran. */}
                          {relancable(x) && !ouvert && (
                            <button className="rig-bouton" disabled={lecture || occupe} onClick={() => relancer(x.traitement)}>
                              Relancer
                            </button>
                          )}
                        </td>
                      </tr>
                      {ouvert && (
                        <LigneDetail colonnes={NbColonnesTraitements} champs={[
                          { libelle: 'En cours', valeur: String(x.enCours) },
                          { libelle: 'Dernier message', valeur: `${x.codeGreffe ? `Greffe ${x.codeGreffe} — ` : ''}${x.message ?? '—'}` },
                          { libelle: 'Publié le', valeur: quand(x.publieLe) },
                          { libelle: 'File baissée le', valeur: x.fileBaisseLe ? quand(x.fileBaisseLe) : '' },
                        ]} actions={relancable(x)
                          ? [{ libelle: 'Relancer', fort: true, desactive: lecture || occupe, agir: () => relancer(x.traitement) }] : []} />
                      )}
                    </Fragment>
                  )
                })}
                {traitements.length === 0 && (
                  <tr><td colSpan={NbColonnesTraitements} className="rig-table__vide">
                    {etat.traitements.length === 0 ? 'Aucun état publié.' : 'Aucun traitement ne répond à ce filtre.'}
                  </td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {onglet === 'alertes' && (
          <div className="rig-table-cadre">
            <table className="rig-table rig-lm-table">
              <colgroup>
                <col style={{ width: '124px' }} /><col style={{ width: '180px' }} /><col style={{ width: '58px' }} />
                <col style={{ width: '170px' }} /><col /><col style={{ width: '124px' }} />
              </colgroup>
              <thead>
                <tr>{['Ouverte le', 'Règle', 'Greffe', 'Traitement', 'Erreur', 'État'].map((x) => <th key={x} title={x} style={Coupe}>{x}</th>)}</tr>
              </thead>
              <tbody>
                {alertes.map((a) => (
                  <tr key={a.id}>
                    <td>{quand(a.ouverteLe)}</td>
                    <td title={LibellesRegles[a.regle] ?? a.regle}>
                      <Pastille teinte={TeinteRegle[a.regle] ?? 'gris'}>{LibellesRegles[a.regle] ?? a.regle}</Pastille>
                    </td>
                    <td className="rig-table__mono">{a.codeGreffe ?? '—'}</td>
                    <td style={Coupe} title={a.traitement}>{a.traitement}</td>
                    <td style={Coupe} title={a.erreur}>{a.erreur}</td>
                    <td title={a.envoyeeLe ? `Envoyée le ${quand(a.envoyeeLe)} (${a.canal ?? '?'})` : 'Pas encore envoyée'}>
                      {a.fermeeLe ? <PointEtat teinte="vert">Fermée</PointEtat>
                        : a.envoyeeLe ? <PointEtat teinte="rouge">Envoyée</PointEtat>
                        : <PointEtat teinte="orange">À envoyer</PointEtat>}
                    </td>
                  </tr>
                ))}
                {alertes.length === 0 && (
                  <tr><td colSpan={6} className="rig-table__vide">
                    {etat.alertes.length === 0 ? 'Aucune alerte.' : 'Aucune alerte ne répond à ce filtre.'}
                  </td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {onglet === 'erreurs' && (
          <>
            <div className="rig-table-cadre">
              <table className="rig-table rig-lm-table">
                <colgroup>
                  <col style={{ width: '124px' }} /><col style={{ width: '96px' }} /><col style={{ width: '112px' }} />
                  <col style={{ width: '58px' }} /><col style={{ width: '130px' }} /><col style={{ width: '190px' }} /><col />
                </colgroup>
                <thead>
                  <tr>{['Le', 'Suivi', 'Niveau', 'Greffe', 'Compte', 'Programme', 'Message'].map((x) => <th key={x} title={x} style={Coupe}>{x}</th>)}</tr>
                </thead>
                <tbody>
                  {erreurs.map((e) => (
                    <tr key={e.id} onClick={() => setChoisie(e.id)} aria-selected={e.id === choisie}
                        className={e.id === choisie ? 'rig-ligne--valide' : undefined}>
                      <td>{quand(e.instant)}</td>
                      <td className="rig-table__mono">{e.suivi}</td>
                      <td><Pastille teinte={TeinteNiveau[e.niveau] ?? 'gris'}>{e.niveau}</Pastille></td>
                      <td className="rig-table__mono">{e.codeGreffe ?? '—'}</td>
                      <CelluleCompte compte={e.compte} />
                      <td className="rig-table__mono" style={Coupe} title={e.programme}>{e.programme}</td>
                      <td style={Coupe} title={e.message}>{e.message}</td>
                    </tr>
                  ))}
                  {erreurs.length === 0 && (
                    <tr><td colSpan={7} className="rig-table__vide">
                      {vue.erreurs.length === 0 ? 'Aucune erreur à montrer.' : 'Aucune erreur ne répond à ce filtre.'}
                    </td></tr>
                  )}
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
              <table className="rig-table rig-lm-table" aria-label="Défauts signalés">
                <colgroup>
                  <col style={{ width: '124px' }} /><col style={{ width: '96px' }} /><col style={{ width: '58px' }} />
                  <col style={{ width: '130px' }} /><col style={{ width: '150px' }} /><col style={{ width: '110px' }} /><col />
                </colgroup>
                <thead>
                  <tr>{['Le', 'Suivi', 'Greffe', 'Compte', 'Écran', 'Dernière erreur', 'Phrase de l\'agent'].map((x) =>
                    <th key={x} title={x} style={Coupe}>{x}</th>)}</tr>
                </thead>
                <tbody>
                  {defauts.map((d) => (
                    <tr key={d.id} onClick={() => setDefautChoisi(d.id)} aria-selected={d.id === defautChoisi}
                        className={d.id === defautChoisi ? 'rig-ligne--valide' : undefined}>
                      <td>{quand(d.instant)}</td>
                      <td className="rig-table__mono">{d.suivi}</td>
                      <td className="rig-table__mono">{d.codeGreffe ?? '—'}</td>
                      <CelluleCompte compte={d.compte} />
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
                  {defauts.length === 0 && (
                    <tr><td colSpan={7} className="rig-table__vide">
                      {tousDefauts.length === 0 ? 'Aucun défaut signalé.' : 'Aucun défaut ne répond à ce filtre.'}
                    </td></tr>
                  )}
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

        <PiedListe aide={{
          greffes: 'Survol d\'un état : son détail (base, migrations, échecs).',
          traitements: 'Le chevron déplie le dernier message ; Relancer reprend un traitement en erreur ou arrêté.',
          alertes: 'Survol d\'un état : quand et par quel canal l\'alerte est partie.',
          erreurs: 'Clic sur une erreur : son détail et sa pile d\'appels.',
          defauts: 'Clic sur un défaut : son détail ; sa dernière erreur ramène son parcours.',
        }[onglet]}>
          <span><b>{affiches[onglet]}</b>{` ${NomLigne[onglet]} affiché(s)`}</span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
