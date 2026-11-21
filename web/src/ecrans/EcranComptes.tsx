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

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'

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

export function EcranComptes() {
  const [vue, setVue] = useState<VueEcranComptes | null>(null)
  const [fiche, setFiche] = useState<Fiche>(FicheVide)
  const [recherche, setRecherche] = useState('')
  const [onglet, setOnglet] = useState<Onglet>('comptes')
  const [occupe, setOccupe] = useState(false)
  const [gesteChoisi, setGesteChoisi] = useState<number | null>(null)

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
  const geste = vue.journal.find((l) => l.id === gesteChoisi) ?? vue.journal[0]
  let trouves = vue.comptes
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouves = trouves.filter((c) => contientSansCasse(c.compteWindows, t) || contientSansCasse(c.nom, t) || contientSansCasse(c.prenom, t))
  }
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

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Comptes des agents</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Compte, nom ou prénom</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

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

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-onglets">
            <button className={`rig-onglet${onglet === 'comptes' ? ' rig-onglet--actif' : ''}`} onClick={() => changerOnglet('comptes')}>Comptes</button>
            <button className={`rig-onglet${onglet === 'gestes' ? ' rig-onglet--actif' : ''}`} onClick={() => changerOnglet('gestes')}>Journal des gestes</button>
            <button className={`rig-onglet${onglet === 'droits' ? ' rig-onglet--actif' : ''}`} onClick={() => changerOnglet('droits')}>Journal des droits</button>
          </div>
          <span className="rig-resultats-proc__compte">
            {lecture ? 'Démonstration' : `${vue.comptes.length} compte(s) — ${vue.agent}`}
          </span>
        </div>

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
              <table className="rig-table" style={GrilleRepartie}>
                <Colonnes colonnes={[['Compte', '15%'], ['Agent', '30%'], ['Service', '72px'], ['Rôles'], ['État', '96px']]} />
                <tbody>
                  {trouves.map((c) => (
                    <tr key={c.id} onClick={() => choisir(ficheDe(c))}>
                      <td className="rig-table__mono" title={c.compteWindows}>{c.compteWindows}</td>
                      <td title={`${c.civilite} ${c.prenom} ${c.nom}`}>{c.civilite} {c.prenom} {c.nom}</td>
                      <td title={c.service}>{abregeService(c)}</td>
                      <td title={c.roles.join(', ')}>{c.roles.join(', ')}</td>
                      <td><span className={`rig-etat rig-etat--${c.desactive ? 'attente' : 'ok'}`}>{c.desactive ? 'Désactivé' : 'Actif'}</span></td>
                    </tr>
                  ))}
                  {trouves.length === 0 && (
                    <tr><td colSpan={5} className="rig-table__vide">Aucun compte ne répond à cette recherche.</td></tr>
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
            <table className="rig-table" style={GrilleRepartie}>
              <Colonnes colonnes={[['Quand', '116px'], ['Geste', '40%'], ['Écran', '140px'], ['Avant → après']]} />
              <tbody>
                {vue.journal.map((l) => (
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
            <table className="rig-table" style={GrilleRepartie}>
              <Colonnes colonnes={[['Quand', '116px'], ['Compte', '15%'], ['Rôle'], ['Changement', '96px'], ['Par', '23%']]} />
              <tbody>
                {vue.journalDroits.map((l) => (
                  <tr key={l.id}>
                    <td className="rig-table__mono">{quand(l.instant)}</td>
                    <td className="rig-table__mono" title={l.compteVise}>{l.compteVise}</td>
                    <td title={l.role}>{l.role}</td>
                    <td><span className={`rig-etat rig-etat--${l.sens === 'ajout' ? 'ok' : 'attente'}`}>{l.sens === 'ajout' ? 'Ajouté' : 'Retiré'}</span></td>
                    <td title={l.auteur}>{l.auteur}</td>
                  </tr>
                ))}
                {vue.journalDroits.length === 0 && (
                  <tr><td colSpan={5} className="rig-table__vide">Aucun changement de droits.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
