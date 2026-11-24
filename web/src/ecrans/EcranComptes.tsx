// Ecran « Comptes des agents » : la procedure PROC_UTILISATEUR de RIG v2 (GESTUTIL, « gestion des
// donnees et parametres utilisateurs »), et ses roles (PROC_ROLE), refaits pour la V3 -- ecran NEUF,
// « a maquetter » dans le carnet (SOC-002 T03, SOC-004 T03, SOC-005 T03, SOC-016 T03).
//
// Le responsable du greffe y cree, corrige, desactive (ou supprime, s'il n'a jamais agi) le compte
// Windows d'un agent, le rattache a un service et lui donne ou retire ses roles. Deux journaux en
// lecture : les gestes (qui a fait quoi, depuis quel ecran, avant/apres) et les droits (qui a donne
// ou retire quel role). Tout passe par Pont/EcranComptes.cs, qui appelle le serveur V3 ; les regles
// (doublon sans majuscules, abrege, code greffe 9999, roles reserves, suppression refusee, journaux)
// sont tenues la-bas. Sans le role « Gestion des habilitations », l'ecran ne montre que le refus.
//
// LISTE MODERNE (24/11/2026, plan des listes modernes, lot 7) : la colonne prend les briques de
// composants/ListeModerne.tsx. Tete « Comptes des agents » (synthese de l'onglet montre, « Aller a… »
// Ctrl K : sur l'onglet des comptes il remplace la recherche de gauche, meme etat), les trois onglets en
// puces de vues avec leur compte, l'agent en avatar d'initiales, le service en pastille, l'etat et le
// changement de droit en point colore, un pied de liste. Le journal des gestes garde son encadre
// « Detail du geste » sous le tableau. Les gestes (creer, modifier, roles, desactiver, supprimer), la
// fiche et les comptes rendus ne changent pas.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'
import {
  CelluleIdentite, EnteteListe, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe, type ProprietesEnteteListe, type Teinte,
} from '../composants/ListeModerne'

export interface CompteAgent {
  id: number
  compteWindows: string
  nom: string
  prenom: string
  civilite: string
  abrege: string
  codeGreffe: string | null
  idService: number | null
  service: string
  desactive: boolean
  estAmitel: boolean
  estAdministrateur: boolean
  roles: string[]
}

export interface ServiceGreffe {
  id: number
  abrege: string
  nom: string | null
}

export interface RoleVisible {
  nom: string
  libelle: string
  reserveAmitel: boolean
  reserveAdministrateur: boolean
  comptabilite: boolean
}

export interface LigneJournal {
  id: number
  instant: string
  phrase: string
  qui: string
  verbe: string
  famille: string
  objet: string
  ecran: string | null
  avant: string | null
  apres: string | null
  source: string
}

export interface LigneJournalDroits {
  id: number
  instant: string
  compteVise: string
  role: string
  sens: string
  auteur: string
  ecran: string | null
}

export interface VueEcranComptes {
  mode: 'serveur' | 'demo' | 'refuse' | 'erreur'
  message: string | null
  echec: boolean
  agent: string
  comptes: CompteAgent[]
  services: ServiceGreffe[]
  roles: RoleVisible[]
  journal: LigneJournal[]
  journalDroits: LigneJournalDroits[]
  toutLeGreffe: boolean
  selection: number | null
  proposerDesactivation: boolean
}

interface Fiche {
  id: number | null
  compteWindows: string
  civilite: string
  nom: string
  prenom: string
  idService: number | null
  roles: string[]
}

const FicheVide: Fiche = { id: null, compteWindows: '', civilite: 'M.', nom: '', prenom: '', idService: null, roles: [] }
const Civilites = ['M.', 'Mme']
/** Le compte rendu d'un geste, sous l'entete : la marge des blocs de l'ecran. */
const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const

/**
 * L'entete d'une grille a colonnes REPARTIES. La grille commune (.rig-table : une ligne par cellule,
 * largeur automatique, defilement a droite) poussait hors du cadre « Rôles », « État », « Écran » et
 * « Avant → après » des que la fenetre etait etroite (captures du 21/11/2026, ~540 px). Ici la table
 * est en disposition fixe : chaque colonne garde sa part de la largeur et reste visible ; un texte
 * trop long finit par « … » et se lit entier au survol (title des cellules). Largeur absente = le reste.
 * Les largeurs fixes sont mesurees sur ces captures (texte dense ~6,4 px par lettre) pour qu'a 540 px
 * les colonnes courtes tiennent entieres : date (97 px), « Comptes des agents » (115 px), un role.
 * fix-ok: table-layout auto + cellules nowrap = largeur minimale > fenetre, colonnes de droite hors cadre (captures SOC-004-05 et SOC-016-06 du 21/11).
 */
function Colonnes({ colonnes }: { colonnes: [titre: string, largeur?: string][] }) {
  return (
    <>
      <colgroup>{colonnes.map(([t, l]) => <col key={t} style={l ? { width: l } : undefined} />)}</colgroup>
      <thead>
        <tr>{colonnes.map(([t]) => <th key={t} title={t} style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{t}</th>)}</tr>
      </thead>
    </>
  )
}
const GrilleRepartie = { tableLayout: 'fixed' } as const
/** Le geste choisi se repere au filet, comme une demande en alerte (.rig-table__ligne--alerte). */
const FiletChoisi = { boxShadow: 'inset 3px 0 0 0 var(--rig-focus)' } as const
/**
 * Sous le journal des gestes : le geste choisi (le dernier, d'office), en entier. La grille garde
 * une ligne par geste (regle du 17/09, .rig-table tbody td) : a ~540 px, la phrase et l'avant/apres
 * y finissaient par « … » et ne se lisaient qu'au survol (capture SOC-016-06 du 21/11). Ici le
 * texte replie ses lignes.
 */
const DetailGeste = {
  flex: 'none', margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)', borderTop: '1px solid var(--rig-bord)',
  fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)', display: 'grid', gap: 'var(--rig-esp-1)',
} as const
const TexteEntier = { margin: 0, whiteSpace: 'normal', overflowWrap: 'anywhere' } as const

function ficheDe(c: CompteAgent): Fiche {
  return { id: c.id, compteWindows: c.compteWindows, civilite: c.civilite, nom: c.nom, prenom: c.prenom, idService: c.idService, roles: c.roles }
}

/** « 21/11/2026 09:42 », a l'heure du poste (le serveur rend l'instant en UTC). */
export function quand(instant: string): string {
  const d = new Date(instant)
  if (Number.isNaN(d.getTime())) return instant
  const deux = (n: number) => String(n).padStart(2, '0')
  return `${deux(d.getDate())}/${deux(d.getMonth() + 1)}/${d.getFullYear()} ${deux(d.getHours())}:${deux(d.getMinutes())}`
}

/** Les valeurs qui ont change entre l'avant et l'apres d'un geste : « nom : Martin -> Martin-Durand ». */
export function changements(avant: string | null, apres: string | null): string {
  const lire = (j: string | null): Record<string, unknown> => {
    if (!j) return {}
    try { return JSON.parse(j) as Record<string, unknown> } catch { return {} }
  }
  const a = lire(avant)
  const b = lire(apres)
  // Une liste vide (compte sans rôle) est une valeur absente, comme null : « — », pas une chaîne vide.
  const texte = (v: unknown) =>
    (v === null || v === undefined || v === '' || (Array.isArray(v) && v.length === 0) ? '—' : Array.isArray(v) ? v.join(', ') : String(v))
  const cles = [...new Set([...Object.keys(a), ...Object.keys(b)])].filter((k) => JSON.stringify(a[k]) !== JSON.stringify(b[k]))
  if (!avant && apres) return 'création'
  if (avant && !apres) return 'suppression'
  return cles.map((k) => `${k} : ${texte(a[k])} → ${texte(b[k])}`).join(' ; ')
}

type Onglet = 'comptes' | 'gestes' | 'droits'

/** Le service en pastille : les deux domaines du greffe ont la teinte du domaine de la Recherche d'affaires. */
const TeinteService: Record<string, Teinte> = { RCS: 'bleu', JUD: 'ardoise' }

/** « 1 compte », « 3 comptes » : le nombre en gras, comme la synthese de la Recherche d'affaires. */
function Nombre({ n, un, plusieurs }: { n: number; un: string; plusieurs: string }) {
  return <><b>{n}</b>{n > 1 ? ` ${plusieurs}` : ` ${un}`}</>
}

export function EcranComptes() {
  const [vue, setVue] = useState<VueEcranComptes | null>(null)
  const [fiche, setFiche] = useState<Fiche>(FicheVide)
  const [recherche, setRecherche] = useState('')
  const [onglet, setOnglet] = useState<Onglet>('comptes')
  const [occupe, setOccupe] = useState(false)
  const [gesteChoisi, setGesteChoisi] = useState<number | null>(null)
  // « Aller à… » (Ctrl K) des deux journaux : filtre la liste affichee. Celui des comptes est la recherche de gauche.
  const [filtreGestes, setFiltreGestes] = useState('')
  const [filtreDroits, setFiltreDroits] = useState('')

  const recevoir = (v: VueEcranComptes) => {
    setVue(v)
    const choisi = v.selection !== null ? v.comptes.find((c) => c.id === v.selection) : undefined
    if (choisi) setFiche(ficheDe(choisi))
    else if (v.selection === null && !v.echec && v.message) setFiche(FicheVide)
  }

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranComptes>('EcranComptes.Ouvrir').then((v) => { if (vivant) recevoir(v) })
    return () => { vivant = false }
  }, [])

  const agir = async (geste: () => Promise<VueEcranComptes>) => {
    setOccupe(true)
    try { recevoir(await geste()) } finally { setOccupe(false) }
  }

  // Le compte rendu (et le bouton « Désactiver ce compte » d'un refus) parle du geste qui vient
  // d'avoir lieu, sur le compte choisi a ce moment. Il s'efface des qu'on change d'onglet ou d'agent.
  // fix-ok: vue.message n'etait remplace que par la reponse suivante du serveur : il restait affiche
  // sur les journaux, et son bouton agissait sur l'agent choisi ensuite (test rouge « le compte rendu
  // d'un geste s'efface », 21/11).
  const oublierCompteRendu = () =>
    setVue((v) => (v && v.message ? { ...v, message: null, echec: false, proposerDesactivation: false } : v))
  const changerOnglet = (o: Onglet) => { oublierCompteRendu(); setOnglet(o) }
  const choisir = (f: Fiche) => { oublierCompteRendu(); setFiche(f) }

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement des comptes…</p></div>

  if (vue.mode === 'refuse' || vue.mode === 'erreur') {
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete">
            <div className="rig-kicker">Comptes des agents</div>
          </div>
          <p className="rig-comptes__message" style={Message} role="alert">
            <span className="rig-etat rig-etat--alerte">{vue.mode === 'refuse' ? 'Accès refusé' : 'Serveur injoignable'}</span>
            {' '}{vue.message}
          </p>
        </section>
      </div>
    )
  }

  const lecture = vue.mode === 'demo'
  const courant = fiche.id !== null ? vue.comptes.find((c) => c.id === fiche.id) : undefined
  let trouves = vue.comptes
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouves = trouves.filter((c) => contientSansCasse(c.compteWindows, t) || contientSansCasse(c.nom, t) || contientSansCasse(c.prenom, t))
  }
  const contient = (filtre: string, textes: (string | null)[]) => estBlanc(filtre) || textes.some((t) => contientSansCasse(t ?? '', filtre.trim()))
  const gestes = vue.journal.filter((l) => contient(filtreGestes, [l.phrase, l.ecran, changements(l.avant, l.apres)]))
  const droits = vue.journalDroits.filter((l) => contient(filtreDroits, [l.compteVise, l.role, l.auteur]))
  // Le geste detaille : celui qu'on a choisi, sinon le premier affiche (le dernier geste, d'office).
  const geste = vue.journal.find((l) => l.id === gesteChoisi) ?? gestes[0]
  const actifs = trouves.filter((c) => !c.desactive).length
  const ajouts = droits.filter((l) => l.sens === 'ajout').length
  const champ = (cle: keyof Fiche, valeur: string | number | null) => setFiche({ ...fiche, [cle]: valeur })
  const enregistrer = () => {
    const demande = {
      id: fiche.id, compteWindows: fiche.compteWindows, nom: fiche.nom, prenom: fiche.prenom,
      civilite: fiche.civilite, idService: fiche.idService, roles: fiche.id === null ? fiche.roles : null, ecran: null,
    }
    void agir(() => appeler<VueEcranComptes>(fiche.id === null ? 'EcranComptes.Creer' : 'EcranComptes.Modifier', demande))
  }
  // Dans la grille, le service par son abrege (RCS, JUD) : le nom complet ne tenait pas a cote des
  // roles en fenetre etroite. Il reste au survol et dans la fiche.
  const abregeService = (c: CompteAgent) => vue.services.find((s) => s.id === c.idService)?.abrege ?? c.service
  const basculerRole = (role: string) => {
    if (fiche.id === null) {
      setFiche({ ...fiche, roles: fiche.roles.includes(role) ? fiche.roles.filter((r) => r !== role) : [...fiche.roles, role] })
      return
    }
    const ajouter = !(courant?.roles ?? []).includes(role)
    void agir(() => appeler<VueEcranComptes>('EcranComptes.Role', fiche.id, role, ajouter))
  }
  const rolesFiche = fiche.id === null ? fiche.roles : courant?.roles ?? []

  // La tete de l'onglet montre : sa synthese et son « Aller à… » (Ctrl K).
  const tete: Omit<ProprietesEnteteListe, 'titre'> =
    onglet === 'comptes' ? {
      synthese: <><Nombre n={trouves.length} un="compte" plusieurs="comptes" />{' · '}
        <Nombre n={actifs} un="actif" plusieurs="actifs" />{!lecture && vue.agent !== '' ? ` — ${vue.agent}` : ''}</>,
      // Le meme etat que la recherche de gauche, qui se cache sur cet onglet : un seul filtre, un seul champ a la fois.
      filtre: { valeur: recherche, changer: setRecherche, indication: 'Aller à un compte…',
                valider: () => { if (trouves.length === 1) choisir(ficheDe(trouves[0]!)) } },
    } : onglet === 'gestes' ? {
      synthese: <><Nombre n={gestes.length} un="geste" plusieurs="gestes" />{' · '}
        <Nombre n={new Set(gestes.map((l) => l.qui)).size} un="agent" plusieurs="agents" />{' · '}
        <Nombre n={new Set(gestes.flatMap((l) => (l.ecran ? [l.ecran] : []))).size} un="écran" plusieurs="écrans" /></>,
      filtre: { valeur: filtreGestes, changer: setFiltreGestes, indication: 'Aller à un geste…' },
    } : {
      synthese: <><Nombre n={droits.length} un="changement" plusieurs="changements" />{' · '}
        <Nombre n={ajouts} un="ajout" plusieurs="ajouts" />{' · '}
        <Nombre n={droits.length - ajouts} un="retrait" plusieurs="retraits" /></>,
      filtre: { valeur: filtreDroits, changer: setFiltreDroits, indication: 'Aller à un compte ou un rôle…' },
    }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Comptes des agents</div>

        {/* Sur l'onglet des comptes, cette recherche est le champ « Aller à un compte… » de la tete : la
            montrer aussi ici ferait deux champs pour un seul filtre. Sur les journaux, la tete filtre le
            journal ; la recherche revient ici, ou elle filtre encore la liste des comptes ci-dessous. */}
        {onglet !== 'comptes' && (
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Compte, nom ou prénom</span>
            <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
          </label>
        )}

        {!lecture && (
          <div className="rig-criteres__actions">
            <button className="rig-bouton rig-bouton--fort" disabled={occupe}
                    onClick={() => { choisir(FicheVide); changerOnglet('comptes') }}>Nouveau compte</button>
          </div>
        )}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Comptes ({trouves.length})</div>
          {trouves.map((c) => (
            <button key={c.id} className={`rig-bascule${fiche.id === c.id ? ' rig-bascule--actif' : ''}`}
                    onClick={() => { choisir(ficheDe(c)); changerOnglet('comptes') }}>
              <span className="rig-bascule__temoin"></span>
              <span>{c.compteWindows} — {c.prenom} {c.nom}{c.desactive ? ' (désactivé)' : ''}</span>
            </button>
          ))}
        </div>
      </aside>

      {/* fix-ok: application du socle des listes modernes (lot 7) en plusieurs pas, pas un correctif. */}
      <ListeModerne etiquette="Comptes des agents">
        <EnteteListe titre="Comptes des agents" {...tete} />

        {/* Les trois onglets, en puces de vues (maquette « Tout reuni ») : chacune montre une autre liste. */}
        <VuesListe active={onglet} choisir={(cle) => changerOnglet(cle as Onglet)} vues={[
          { cle: 'comptes', libelle: 'Comptes', compte: vue.comptes.length },
          { cle: 'gestes', libelle: 'Journal des gestes', compte: vue.journal.length },
          { cle: 'droits', libelle: 'Journal des droits', compte: vue.journalDroits.length },
        ]} />

        {lecture && (
          <p className="rig-comptes__message" style={Message}>
            <span className="rig-etat rig-etat--attente">Démonstration</span>
            {' '}Sans serveur, les comptes de la maquette s'affichent en lecture seule.
          </p>
        )}
        {vue.message && (
          <p className="rig-comptes__message" style={Message} role={vue.echec ? 'alert' : 'status'}>
            <span className={`rig-etat rig-etat--${vue.echec ? 'alerte' : 'ok'}`}>{vue.echec ? 'Refusé' : 'Enregistré'}</span>
            {' '}{vue.message}
            {vue.proposerDesactivation && courant && !courant.desactive && (
              <>
                {' '}
                <button className="rig-bouton" disabled={occupe}
                        onClick={() => void agir(() => appeler<VueEcranComptes>('EcranComptes.Desactiver', courant.id))}>Désactiver ce compte</button>
              </>
            )}
          </p>
        )}

        {onglet === 'comptes' && (
          <>
            <div className="rig-table-cadre">
              <table className="rig-table rig-lm-table" style={GrilleRepartie}>
                {/* L'agent en avatar d'initiales, son compte Windows en gris : l'ancienne colonne « Compte » y est entree. */}
                <Colonnes colonnes={[['Agent', '40%'], ['Service', '96px'], ['Rôles'], ['État', '108px']]} />
                <tbody>
                  {trouves.map((c) => (
                    <tr key={c.id} onClick={() => choisir(ficheDe(c))}>
                      <CelluleIdentite nom={`${c.prenom} ${c.nom}`.trim() || c.compteWindows} objet={c.compteWindows}
                                       titre={`${[c.civilite, c.prenom, c.nom].filter((m) => m !== '').join(' ')} — ${c.compteWindows}`} />
                      <td title={c.service}><Pastille teinte={TeinteService[abregeService(c)] ?? 'gris'}>{abregeService(c)}</Pastille></td>
                      <td title={c.roles.join(', ')}>{c.roles.join(', ')}</td>
                      <td><PointEtat teinte={c.desactive ? 'gris' : 'vert'}>{c.desactive ? 'Désactivé' : 'Actif'}</PointEtat></td>
                    </tr>
                  ))}
                  {trouves.length === 0 && (
                    <tr><td colSpan={4} className="rig-table__vide">Aucun compte ne répond à cette recherche.</td></tr>
                  )}
                </tbody>
              </table>
            </div>

            {!lecture && (
              <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
                <div className="rig-kicker">{fiche.id === null ? 'Nouveau compte' : `Compte ${courant?.compteWindows ?? ''}`}</div>
                <div className="rig-criteres__paire">
                  <label className="rig-saisie-champ">
                    <span className="rig-saisie-champ__libelle">Compte Windows</span>
                    <input className="rig-saisie-champ__saisie" value={fiche.compteWindows} maxLength={35}
                           onChange={(e) => champ('compteWindows', e.target.value)} />
                  </label>
                  <label className="rig-saisie-champ">
                    <span className="rig-saisie-champ__libelle">Civilité</span>
                    <select className="rig-saisie-champ__saisie" value={fiche.civilite} onChange={(e) => champ('civilite', e.target.value)}>
                      {Civilites.map((c) => <option key={c} value={c}>{c}</option>)}
                    </select>
                  </label>
                </div>
                <div className="rig-criteres__paire">
                  <label className="rig-saisie-champ">
                    <span className="rig-saisie-champ__libelle">Nom</span>
                    <input className="rig-saisie-champ__saisie" value={fiche.nom} maxLength={35} onChange={(e) => champ('nom', e.target.value)} />
                  </label>
                  <label className="rig-saisie-champ">
                    <span className="rig-saisie-champ__libelle">Prénom</span>
                    <input className="rig-saisie-champ__saisie" value={fiche.prenom} maxLength={35} onChange={(e) => champ('prenom', e.target.value)} />
                  </label>
                </div>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Service</span>
                  <select className="rig-saisie-champ__saisie" value={fiche.idService ?? ''}
                          onChange={(e) => champ('idService', e.target.value === '' ? null : Number(e.target.value))}>
                    <option value="">— choisir —</option>
                    {vue.services.map((s) => <option key={s.id} value={s.id}>{s.nom ?? s.abrege}</option>)}
                  </select>
                </label>
                {courant && (
                  <div className="rig-criteres__paire">
                    <label className="rig-saisie-champ">
                      <span className="rig-saisie-champ__libelle">Abrégé</span>
                      <input className="rig-saisie-champ__saisie" value={courant.abrege} readOnly />
                    </label>
                    <label className="rig-saisie-champ">
                      <span className="rig-saisie-champ__libelle">Code greffe</span>
                      <input className="rig-saisie-champ__saisie" value={courant.codeGreffe ?? ''} readOnly />
                    </label>
                  </div>
                )}

                <div className="rig-kicker">Rôles</div>
                <div>
                  {vue.roles.map((r) => (
                    <button key={r.nom} className={`rig-bascule${rolesFiche.includes(r.nom) ? ' rig-bascule--actif' : ''}`}
                            disabled={occupe || courant?.desactive} title={r.comptabilite ? 'Ouvre la facturation ou l\'encaissement' : r.libelle}
                            onClick={() => basculerRole(r.nom)}>
                      <span className="rig-bascule__temoin"></span>
                      <span>{r.libelle}{r.comptabilite ? ' (facturation)' : ''}</span>
                    </button>
                  ))}
                </div>

                <div className="rig-criteres__actions">
                  <button className="rig-bouton rig-bouton--fort" disabled={occupe || courant?.desactive} onClick={enregistrer}>
                    {fiche.id === null ? 'Créer le compte' : 'Enregistrer'}
                  </button>
                  {courant && !courant.desactive && (
                    <button className="rig-bouton" disabled={occupe}
                            onClick={() => void agir(() => appeler<VueEcranComptes>('EcranComptes.Desactiver', courant.id))}>Désactiver</button>
                  )}
                  {courant && (
                    <button className="rig-bouton rig-bouton--plat" disabled={occupe}
                            onClick={() => void agir(() => appeler<VueEcranComptes>('EcranComptes.Supprimer', courant.id))}>Supprimer</button>
                  )}
                </div>
              </div>
            )}
          </>
        )}

        {onglet === 'gestes' && (
          <>
          <div className="rig-table-cadre">
            <table className="rig-table rig-lm-table" style={GrilleRepartie}>
              <Colonnes colonnes={[['Quand', '116px'], ['Geste', '40%'], ['Écran', '140px'], ['Avant → après']]} />
              <tbody>
                {gestes.map((l) => (
                  <tr key={l.id} aria-selected={l === geste} onClick={() => setGesteChoisi(l.id)}>
                    <td className="rig-table__mono" style={l === geste ? FiletChoisi : undefined}>{quand(l.instant)}</td>
                    <td title={l.phrase}>{l.phrase}</td>
                    <td title={l.ecran ?? undefined}>{l.ecran ?? '—'}</td>
                    <td title={changements(l.avant, l.apres)}>{changements(l.avant, l.apres)}</td>
                  </tr>
                ))}
                {vue.journal.length === 0 && (
                  <tr><td colSpan={4} className="rig-table__vide">Aucun geste à montrer.</td></tr>
                )}
                {vue.journal.length > 0 && gestes.length === 0 && (
                  <tr><td colSpan={4} className="rig-table__vide">{`Aucun des ${vue.journal.length} gestes ne contient « ${filtreGestes.trim()} ».`}</td></tr>
                )}
              </tbody>
            </table>
            {!vue.toutLeGreffe && !lecture && (
              <p className="rig-comptes__message" style={Message}>Seuls vos propres gestes s'affichent : le journal du greffe est réservé au chef de greffe.</p>
            )}
          </div>
          {geste && (
            <section aria-label="Détail du geste" style={DetailGeste}>
              <div className="rig-kicker">Détail du geste · {quand(geste.instant)}{geste.ecran && <> · <span>{geste.ecran}</span></>}</div>
              <p style={{ ...TexteEntier, color: 'var(--rig-texte)' }}>{geste.phrase}</p>
              <p style={TexteEntier}>{changements(geste.avant, geste.apres)}</p>
            </section>
          )}
          </>
        )}

        {onglet === 'droits' && (
          <div className="rig-table-cadre">
            <table className="rig-table rig-lm-table" style={GrilleRepartie}>
              <Colonnes colonnes={[['Quand', '116px'], ['Compte', '15%'], ['Rôle'], ['Changement', '108px'], ['Par', '25%']]} />
              <tbody>
                {droits.map((l) => (
                  <tr key={l.id}>
                    <td className="rig-table__mono">{quand(l.instant)}</td>
                    <td className="rig-table__mono" title={l.compteVise}>{l.compteVise}</td>
                    <td title={l.role}>{l.role}</td>
                    <td><PointEtat teinte={l.sens === 'ajout' ? 'vert' : 'orange'}>{l.sens === 'ajout' ? 'Ajouté' : 'Retiré'}</PointEtat></td>
                    <CelluleIdentite nom={l.auteur} titre={l.auteur} />
                  </tr>
                ))}
                {vue.journalDroits.length === 0 && (
                  <tr><td colSpan={5} className="rig-table__vide">Aucun changement de droits.</td></tr>
                )}
                {vue.journalDroits.length > 0 && droits.length === 0 && (
                  <tr><td colSpan={5} className="rig-table__vide">{`Aucun des ${vue.journalDroits.length} changements ne contient « ${filtreDroits.trim()} ».`}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        <PiedListe>
          {onglet === 'comptes' && <>
            <span><Nombre n={trouves.length} un="compte affiché" plusieurs="comptes affichés" /></span>
            <span>Actifs <b>{actifs}</b></span>
            {trouves.length > actifs && <span>Désactivés <b>{trouves.length - actifs}</b></span>}
          </>}
          {onglet === 'gestes' && <span><Nombre n={gestes.length} un="geste affiché" plusieurs="gestes affichés" /></span>}
          {onglet === 'droits' && <>
            <span><Nombre n={droits.length} un="changement affiché" plusieurs="changements affichés" /></span>
            <span>Ajouts <b>{ajouts}</b></span>
            <span>Retraits <b>{droits.length - ajouts}</b></span>
          </>}
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
