// Ecran « Historique de l'ancien RIG » : « a maquetter » dans le carnet (REC-010 T04, REC-011 T04, REC-009 T04, REC-018 T04).
// Sans origine RIG v2 : l'ancien logiciel ne montre pas les affaires de l'autre ; sa recherche par ancien numero (Procedure RIG v2 :
// PROC_CORRESPMIG) est reprise ici comme un critere.
//
// Pendant que les deux logiciels tournent, une affaire se traite dans UN seul : creee avant le passage, dans l'ancien RIG ;
// apres, dans la V3. L'agent voit ici, en LECTURE, tout l'historique d'une entreprise, d'un dossier, d'un client ou d'une
// arrivee : les affaires des deux logiciels, les anciennes marquees « traitée dans l'ancien RIG », leurs demandes et leur etat,
// les liens entre demandes (bloquee par, nee de) avec le logiciel de chacune, et les echanges avec les partenaires (REC-010,
// REC-011). Une arrivee liste ses demandes des deux logiciels (REC-018). Les anciens numeros d'avant RIG (Dedale, Alinea,
// GAGI, Oracle) menent a la fiche RIG, et les fiches creees par les anciennes reprises sont listees a verifier (REC-009).
// Rien ne s'y modifie : une ancienne affaire se traite dans l'ancien RIG, la base de la V3 le refuse (REC-010 CA03).
// Les regles sont au serveur (Serveur/Recopie, prouvees par tests/RecopieRetourCheck) ; tout passe par Pont/EcranHistoriqueV2.cs.
// fix-ok: deux causes mesurees apres la premiere ecriture : (1) capture claire du 21/11 (1 920 px) -- la liste des affaires prenait
// toute la hauteur et le detail sortait coupe en bas : liste plafonnee, detail qui defile ; (2) SoclePreferencesCheck rouge
// (SOC-027 CA04) -- la touche de recherche appelait « chercher » et la regle des ecrans de recherche attend « rechercher ».
// fix-ok: capture du 22/11 en fenetre de 1 080 px -- les colonnes « Affaire » et « Libelle et liens » rendues a 0 px : en
// table-layout fixed, la seule colonne sans largeur prend le reste, nul quand les colonnes fixes remplissent la place ; d'ou grille().
//
// LISTE MODERNE (24/11/2026, plan des listes modernes, lot 5) : la colonne des resultats prend les briques de
// composants/ListeModerne.tsx -- tete (synthese, « Aller à… » Ctrl K qui filtre la liste affichee -- affaires, anciens numeros
// ou fiches a verifier --, Entree choisit l'affaire restee seule), et dans ces trois listes : client en avatar + nom + code,
// etat et « Dans la V3 » en point colore, logiciel et sorte de fiche en pastille, pied. Les demandes et les echanges d'une
// affaire (son detail) et les demandes d'une arrivee ne sont pas des listes de resultats : ils gardent le restyle commun.
// Aucune vue : l'ecran n'a pas de critere d'etat ni de type sur ses lignes (« Rechercher par » choisit QUOI chercher).
// fix-ok: ajout du socle des listes en plusieurs pas (lot 5), pas un correctif.

import { useState, type CSSProperties } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'
import { toucheDeRecherche } from '../plan/clavier'
import { CelluleIdentite, EnteteListe, ListeModerne, Pastille, PiedListe, PointEtat, teinteStatut } from '../composants/ListeModerne'
import { quand } from './EcranComptes'

export interface LienDemandeVue {
  lien: string
  idDemande: number
  numero: string | null
  logiciel: string
  etat: string
  etatLibelle: string
  leve: boolean
  leveLe: string | null
}

export interface DemandeHistorique {
  id: number
  idV2: string | null
  logiciel: string
  numero: string | null
  libelle: string | null
  etat: string
  etatLibelle: string
  creeeLe: string | null
  valideeLe: string | null
  type: string | null
  numeroArrivee: string | null
  bloquee: boolean
  texteBlocage: string | null
  liens: LienDemandeVue[]
}

export interface EchangeHistorique {
  sens: string
  id: number
  codeEdi: string
  etat: number
  /** Le libelle de la base des echanges (« Erreur d'intégration ») ; le numero seul si elle n'en a pas. */
  etatLibelle: string
  /** Le flux a echoue (6, 7) : l'ecran le marque, avec son message d'erreur. */
  enErreur: boolean
  le: string
  reference: string | null
  erreur: string | null
  demande: string | null
}

export interface AffaireHistorique {
  id: number
  idV2: string | null
  logiciel: string
  marque: string
  etat: string
  libelle: string | null
  creeeLe: string | null
  client: string | null
  clientCode: string | null
  dossiers: string[]
  demandes: DemandeHistorique[]
  echanges: EchangeHistorique[]
  echangesNote: string | null
}

export interface VueHistorique {
  critere: string
  valeur: string
  titre: string | null
  affaires: AffaireHistorique[]
  anciennes: number
  nouvelles: number
  note: string | null
}

export interface ArriveeDeuxLogiciels {
  id: number
  idV2: string | null
  numero: string | null
  mode: string
  le: string | null
  nom: string | null
  references: string | null
  demandes: DemandeHistorique[]
  dansAncienRig: number
}

export interface AncienNumeroVue {
  logiciel: string
  tableRig: string
  sorteFiche: string
  ancienId: string | null
  ancienNumero: string | null
  idRig: number | null
  numeroRig: string | null
  numeroInfogreffe: string | null
  tableV3: string | null
  idV3: string | null
}

export interface FicheAncienneRepriseVue {
  tableV2: string
  idV2: string
  tableV3: string | null
  idV3: string | null
  motif: string
  libelle: string | null
}

export interface VueEcranHistorique {
  mode: 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur' | 'vide'
  message: string | null
  greffe: string | null
  critere: string
  valeur: string | null
  historique: VueHistorique | null
  arrivee: ArriveeDeuxLogiciels | null
  anciens: AncienNumeroVue[]
  reprises: FicheAncienneRepriseVue[]
}

/** Les criteres, tels que l'agent les lit (l'ordre de la liste). */
export const CRITERES: { code: string; libelle: string; aide: string }[] = [
  { code: 'siren', libelle: 'Entreprise (SIREN)', aide: '512345678' },
  { code: 'gestion', libelle: 'Dossier (numéro de gestion)', aide: '2004B00117' },
  { code: 'client', libelle: 'Client (code abrégé)', aide: 'DUPONT01' },
  { code: 'arrivee', libelle: 'Arrivée de courrier (numéro)', aide: '2026A004512' },
  { code: 'ancien', libelle: "Numéro d'un ancien logiciel", aide: 'DED-2004-007' },
  { code: 'rig', libelle: 'Numéro RIG (ses anciens numéros)', aide: '2004B00117' },
  { code: 'reprises', libelle: 'Fiches des anciennes reprises', aide: '' },
]

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Aide = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)' } as const
// Le detail prend la place qui reste sous la liste et defile dedans ; la liste des affaires suit son contenu, plafonnee : la
// premiere capture (clair, 1 920 px) montrait trois affaires en haut, 500 px vides, et le detail coupe en bas de la fenetre.
const Detail = { padding: 'var(--rig-esp-2) var(--rig-esp-3)', borderTop: '1px solid var(--rig-bord)', overflow: 'auto', flex: '1 1 auto', minHeight: 0 } as const
const ListeCourte = { flex: '0 1 auto', maxHeight: '40%' } as const
const Lien = { padding: '0 var(--rig-esp-1)', fontSize: 'var(--rig-fs-dense)' } as const

// Un tableau en largeur fixe donne 0 px a sa seule colonne libre (le libelle) des que ses colonnes fixes remplissent la place :
// capture du 22/11/2026 en fenetre etroite (1 080 px, criteres au-dessus des resultats), le nom des affaires et les liens entre
// demandes avaient disparu. Chaque grille tire donc ses colonnes ET sa largeur minimale des memes nombres (null = la colonne
// libre) : la colonne libre garde PlaceLibre px, et en deca c'est le cadre du tableau qui defile.
const PlaceLibre = 200
const MargesCellule = 16   // var(--rig-esp-2) de chaque cote de chaque cellule (rig-accueil.css, .rig-table th/td)
function grille(largeurs: (number | null)[], style: CSSProperties = {}) {
  const fixes = largeurs.reduce<number>((s, l) => s + (l ?? 0), 0)
  return {
    style: { tableLayout: 'fixed', minWidth: fixes + PlaceLibre + largeurs.length * MargesCellule, ...style } as CSSProperties,
    colonnes: <colgroup>{largeurs.map((l, i) => <col key={i} style={l === null ? undefined : { width: `${l}px` }} />)}</colgroup>,
  }
}
const GrilleAffaires = grille([null, 120, 170, 96, 190])
const GrilleDemandes = grille([118, null, 112, 120, 104, 92], { marginTop: 'var(--rig-esp-2)' })
// L'etat est un libelle (« Erreur d'intégration », 20 lettres), pas un numero : sa colonne le tient en entier.
const GrilleEchanges = grille([96, 150, 170, 124, null], { marginTop: 'var(--rig-esp-2)' })
const MessageErreur = { ...Coupe, color: 'var(--rig-alerte)', fontSize: 'var(--rig-fs-dense)' } as const
const GrilleAnciens = grille([96, 170, null, 130, 150, 110])
const GrilleReprises = grille([110, null, 280, 110])

/** Une date pour l'agent : jj/mm/aaaa hh:mm, ou un tiret. */
export function date(instant: string | null): string {
  return instant ? quand(instant) : '—'
}

/** La marque d'un logiciel, en pastille comme dans la liste des affaires : l'ancien RIG en attente (on y va pour agir),
 *  la V3 en vert (teinteStatut du socle). */
export function Logiciel({ logiciel, texte }: { logiciel: string; texte?: string }) {
  const ancien = logiciel !== 'V3'
  return <Pastille teinte={teinteStatut(ancien ? 'attente' : 'ok')}>{texte ?? (ancien ? 'Ancien RIG' : 'V3')}</Pastille>
}

/** Un lien entre demandes : « bloquée par D2607903046 (ancien RIG, qualifiée) ». */
export function texteLien(l: LienDemandeVue): string {
  return `${l.lien} ${l.numero ?? l.idDemande} (${l.logiciel}, ${l.etatLibelle}${l.leve ? `, levé${l.leveLe ? ` le ${quand(l.leveLe)}` : ''}` : ''})`
}

function TableDemandes({ demandes, ouvrir }: { demandes: DemandeHistorique[]; ouvrir: (id: number) => void }) {
  return (
    <table className="rig-table" style={GrilleDemandes.style} aria-label="Demandes">
      {GrilleDemandes.colonnes}
      <thead><tr>{['Demande', 'Libellé et liens', 'État', 'Validée le', 'Arrivée', 'Logiciel'].map((t) => <th key={t} style={Coupe}>{t}</th>)}</tr></thead>
      <tbody>
        {demandes.map((d) => (
          <tr key={d.id}>
            <td style={Coupe} title={d.type ?? undefined}>{d.numero ?? d.id}</td>
            <td>
              <div style={Coupe} title={d.libelle ?? undefined}>{d.libelle ?? '—'}</div>
              {d.bloquee && <div style={Aide} role="note"><PointEtat teinte={teinteStatut('alerte')}>Bloquée</PointEtat> {d.texteBlocage}</div>}
              {d.liens.map((l, i) => (
                <div key={i} style={Aide}>
                  <button className="rig-bouton" style={Lien} onClick={() => ouvrir(l.idDemande)}
                          title="Ouvrir la demande liée, en lecture">{texteLien(l)}</button>
                </div>
              ))}
            </td>
            <td style={Coupe}>{d.etatLibelle}</td>
            <td>{date(d.valideeLe)}</td>
            <td style={Coupe}>{d.numeroArrivee ?? '—'}</td>
            <td><Logiciel logiciel={d.logiciel} /></td>
          </tr>
        ))}
        {demandes.length === 0 && <tr><td colSpan={6} className="rig-table__vide">Aucune demande.</td></tr>}
      </tbody>
    </table>
  )
}

export function EcranHistoriqueV2() {
  const [critere, setCritere] = useState('siren')
  const [valeur, setValeur] = useState('')
  const [vue, setVue] = useState<VueEcranHistorique | null>(null)
  const [choisie, setChoisie] = useState<number | null>(null)
  const [liee, setLiee] = useState<DemandeHistorique | null>(null)
  const [occupe, setOccupe] = useState(false)

  const lancer = async (c: string, v: string) => {
    setOccupe(true)
    try {
      setVue(await appeler<VueEcranHistorique>('EcranHistoriqueV2.Chercher', c, c === 'reprises' ? null : v))
      setChoisie(null)
      setLiee(null)
    } finally { setOccupe(false) }
  }
  // « rechercher » : le nom que tient la regle des touches de recherche (SoclePreferencesCheck, SOC-027 CA04 : Entree, F5 et F7
  // passent par toucheDeRecherche puis rechercher, comme sur les autres ecrans de recherche).
  const rechercher = (c = critere, v = valeur) => { void lancer(c, v) }
  const ouvrirLiee = async (id: number) => setLiee(await appeler<DemandeHistorique | null>('EcranHistoriqueV2.Demande', id))

  const h = vue?.historique ?? null
  const detail = h?.affaires.find((a) => a.id === choisie) ?? h?.affaires[0] ?? null
  const decrit = CRITERES.find((c) => c.code === critere)!
  const enErreur = vue !== null && (vue.mode === 'refuse' || vue.mode === 'erreur' || vue.mode === 'sans-greffe')

  // « Aller à… » (Ctrl K) : filtre la liste AFFICHEE (affaires, anciens numeros ou fiches a verifier), sans relancer la recherche.
  const [filtreRapide, setFiltreRapide] = useState('')
  const garde = (textes: (string | number | null)[]) => estBlanc(filtreRapide) || contientSansCasse(textes.join(' '), filtreRapide.trim())
  const affaires = (h?.affaires ?? []).filter((a) => garde([a.libelle, a.id, a.idV2, ...a.dossiers, a.client, a.clientCode]))
  const listeAnciens = vue !== null && (vue.critere === 'ancien' || vue.critere === 'rig') && vue.mode !== 'vide' && !enErreur
  const anciens = listeAnciens ? vue.anciens.filter((a) => garde([a.ancienNumero, a.ancienId, a.numeroRig, a.idRig, a.numeroInfogreffe, a.sorteFiche])) : []
  const listeReprises = vue !== null && vue.critere === 'reprises' && !enErreur
  const reprises = listeReprises ? vue.reprises.filter((f) => garde([f.libelle, f.idV2, f.motif, f.tableV2])) : []
  // La liste que montre l'ecran, s'il en montre une : ce qu'en disent la tete, le filtre et le pied.
  const liste = h ? { mot: ['affaire', 'affaires'], affichees: affaires.length, total: h.affaires.length, aller: 'Aller à une affaire…' }
    : listeAnciens ? { mot: ['correspondance', 'correspondances'], affichees: anciens.length, total: vue.anciens.length, aller: 'Aller à un numéro…' }
    : listeReprises ? { mot: ['fiche à vérifier', 'fiches à vérifier'], affichees: reprises.length, total: vue.reprises.length, aller: 'Aller à une fiche…' }
    : null
  const nombre = (n: number, mot: string[]) => `${n} ${n > 1 ? mot[1] : mot[0]}`
  const anciennes = affaires.filter((a) => a.logiciel !== 'V3').length

  return (
    <div className="rig-proc__corps">
      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Chercher dans les deux logiciels</div>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Rechercher par</span>
          <select className="rig-saisie-champ__saisie" value={critere} disabled={occupe}
                  onChange={(e) => { setCritere(e.target.value); if (e.target.value === 'reprises') rechercher('reprises', '') }}>
            {CRITERES.map((c) => <option key={c.code} value={c.code}>{c.libelle}</option>)}
          </select>
        </label>
        {critere !== 'reprises' && (
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">{decrit.libelle}</span>
            <input className="rig-saisie-champ__saisie" value={valeur} placeholder={decrit.aide} disabled={occupe}
                   onChange={(e) => setValeur(e.target.value)}
                   onKeyDown={(e) => { if (toucheDeRecherche(e)) rechercher() }} />
          </label>
        )}
        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" disabled={occupe} onClick={() => rechercher()}>Rechercher</button>
        </div>
        <p style={{ ...Aide, marginTop: 'var(--rig-esp-3)' }}>
          Les affaires créées avant le passage du greffe se traitent dans l'ancien RIG : elles sont montrées ici en lecture,
          à jour de ce que l'ancien RIG y fait.
        </p>
      </aside>

      <ListeModerne etiquette="Historique de l'ancien RIG">
        <EnteteListe titre="Historique de l'ancien RIG"
                     synthese={vue === null ? 'Saisissez un critère'
                       : <>{vue.mode === 'demo' && 'Démonstration · '}
                           {liste && (liste.affichees === liste.total ? nombre(liste.total, liste.mot) : `${liste.affichees} sur ${nombre(liste.total, liste.mot)}`)}
                           {h && <>{' · '}<b>{anciennes}</b>{' dans l\'ancien RIG · '}<b>{affaires.length - anciennes}</b>{' dans la V3'}</>}</>}
                     filtre={liste ? {
                       valeur: filtreRapide, changer: setFiltreRapide, indication: liste.aller,
                       valider: () => { if (affaires.length === 1) { setChoisie(affaires[0]!.id); setLiee(null) } },
                     } : undefined} />

        {vue?.message && (
          <p style={Message} role={enErreur ? 'alert' : 'status'}>
            <span className={`rig-etat rig-etat--${enErreur ? 'alerte' : 'attente'}`}>
              {vue.mode === 'refuse' ? 'Accès refusé' : vue.mode === 'sans-greffe' ? 'Aucun greffe ouvert' : vue.mode === 'erreur' ? 'Indisponible'
                : vue.mode === 'demo' ? 'Démonstration' : 'Information'}
            </span>{' '}{vue.message}
          </p>
        )}
        {vue === null && <p className="rig-vide" style={Message}>Choisissez un critère, saisissez le numéro, puis Entrée.</p>}

        {vue?.arrivee && (
          <div style={Detail} aria-label="Arrivée">
            <div className="rig-kicker">Arrivée {vue.arrivee.numero} — {date(vue.arrivee.le)} — mode {vue.arrivee.mode}</div>
            <p style={Aide}>
              {vue.arrivee.nom ?? ''}{vue.arrivee.references ? ` · réf. ${vue.arrivee.references}` : ''} · enregistrée une fois pour les deux logiciels
              {vue.arrivee.idV2 ? '' : ' (pas encore dans l\'ancien RIG)'} · {vue.arrivee.demandes.length} demande(s), dont {vue.arrivee.dansAncienRig} dans l'ancien RIG
            </p>
            <TableDemandes demandes={vue.arrivee.demandes} ouvrir={(id) => void ouvrirLiee(id)} />
          </div>
        )}

        {h && (
          <>
            {h.titre && <p style={{ ...Message, fontWeight: 600 }}>{h.titre}</p>}
            {h.note && <p style={{ ...Message, ...Aide }}>{h.note}</p>}
            <div className="rig-table-cadre" style={ListeCourte}>
              <table className="rig-table rig-lm-table" style={GrilleAffaires.style} aria-label="Affaires">
                {GrilleAffaires.colonnes}
                <thead><tr>{['Affaire', 'Créée le', 'Client', 'État', 'Logiciel'].map((t) => <th key={t} style={Coupe}>{t}</th>)}</tr></thead>
                <tbody>
                  {affaires.map((a) => (
                    <tr key={a.id} onClick={() => { setChoisie(a.id); setLiee(null) }} aria-selected={detail?.id === a.id}>
                      <td style={Coupe} title={a.libelle ?? undefined}>{a.libelle ?? a.id}{a.dossiers.length > 0 ? ` · ${a.dossiers.join(', ')}` : ''}</td>
                      <td>{date(a.creeeLe)}</td>
                      {a.client
                        ? <CelluleIdentite nom={a.client} objet={a.clientCode ?? undefined} titre={`${a.client}${a.clientCode ? ` · ${a.clientCode}` : ''}`} />
                        : <td>—</td>}
                      <td title={a.etat}><PointEtat teinte={a.etat === 'en cours' ? 'bleu' : 'gris'}>{a.etat}</PointEtat></td>
                      <td title={a.marque}><Pastille teinte={a.logiciel === 'V3' ? 'vert' : 'orange'}>{a.marque}</Pastille></td>
                    </tr>
                  ))}
                  {h.affaires.length === 0 && <tr><td colSpan={5} className="rig-table__vide">Aucune affaire pour ce critère.</td></tr>}
                  {h.affaires.length > 0 && affaires.length === 0 && (
                    <tr><td colSpan={5} className="rig-table__vide">{`Aucune des ${h.affaires.length} affaires ne contient « ${filtreRapide.trim()} ».`}</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </>
        )}

        {detail && (
          <div style={Detail} aria-label="Détail de l'affaire">
            <div className="rig-kicker">{detail.libelle ?? detail.id} — {detail.marque}</div>
            {detail.logiciel !== 'V3' && (
              <p style={Aide} role="note">Cette affaire se traite dans l'ancien RIG : la V3 la montre en lecture seule. Ouvrez-la dans l'ancien RIG pour la modifier
                ou y ajouter une demande.</p>
            )}
            <TableDemandes demandes={detail.demandes} ouvrir={(id) => void ouvrirLiee(id)} />
            {(detail.echanges.length > 0 || detail.echangesNote) && (
              <>
                <div className="rig-kicker" style={{ marginTop: 'var(--rig-esp-3)' }}>Échanges avec les partenaires</div>
                {detail.echangesNote && <p style={Aide}>{detail.echangesNote}</p>}
                {detail.echanges.length > 0 && (
                  <table className="rig-table" style={GrilleEchanges.style} aria-label="Échanges">
                    {GrilleEchanges.colonnes}
                    <thead><tr>{['Sens', 'Flux', 'État', 'Le', 'Référence'].map((t) => <th key={t}>{t}</th>)}</tr></thead>
                    <tbody>
                      {detail.echanges.map((e) => (
                        <tr key={`${e.sens}${e.id}`}>
                          <td>{e.sens}</td><td style={Coupe}>{e.codeEdi}</td>
                          <td style={Coupe} title={`État ${e.etat} de la base des échanges`}>
                            {e.enErreur ? <PointEtat teinte={teinteStatut('alerte')}>{e.etatLibelle}</PointEtat> : e.etatLibelle}
                          </td>
                          <td>{date(e.le)}</td>
                          <td>
                            <div style={Coupe} title={e.reference ?? undefined}>{e.reference ?? '—'}{e.demande ? ` · ${e.demande}` : ''}</div>
                            {e.enErreur && e.erreur && <div style={MessageErreur} title={e.erreur}>{e.erreur}</div>}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </>
            )}
          </div>
        )}

        {liee && (
          <div style={Detail} aria-label="Demande liée">
            <div className="rig-kicker">Demande {liee.numero ?? liee.id} — lecture seule — <Logiciel logiciel={liee.logiciel} /></div>
            <p style={Aide}>{liee.libelle ?? ''} · {liee.type ?? 'type ?'} · {liee.etatLibelle}{liee.valideeLe ? ` le ${date(liee.valideeLe)}` : ''}
              {liee.numeroArrivee ? ` · arrivée ${liee.numeroArrivee}` : ''}</p>
            {liee.liens.length > 0 && <p style={Aide}>{liee.liens.map(texteLien).join(' · ')}</p>}
          </div>
        )}

        {vue && listeAnciens && (
          <div className="rig-table-cadre">
            <table className="rig-table rig-lm-table" style={GrilleAnciens.style} aria-label="Anciens numéros">
              {GrilleAnciens.colonnes}
              <thead><tr>{['Logiciel', 'Fiche', 'Ancien numéro', 'Numéro RIG', 'Numéro Infogreffe', 'Dans la V3'].map((t) => <th key={t} style={Coupe}>{t}</th>)}</tr></thead>
              <tbody>
                {anciens.map((a, i) => (
                  <tr key={i}>
                    <td title={a.logiciel}><Pastille teinte="ardoise">{a.logiciel}</Pastille></td><td style={Coupe}>{a.sorteFiche}</td>
                    <td style={Coupe} title={a.ancienId ?? undefined}>{a.ancienNumero ?? a.ancienId ?? '—'}</td>
                    <td>{a.numeroRig ?? a.idRig ?? '—'}</td><td style={Coupe}>{a.numeroInfogreffe ?? '—'}</td>
                    <td>{a.idV3 ? <PointEtat teinte="vert">Reprise</PointEtat> : <PointEtat teinte="orange">Pas encore</PointEtat>}</td>
                  </tr>
                ))}
                {vue.anciens.length === 0 && <tr><td colSpan={6} className="rig-table__vide">Le numéro n'a pas de correspondance.</td></tr>}
                {vue.anciens.length > 0 && anciens.length === 0 && (
                  <tr><td colSpan={6} className="rig-table__vide">{`Aucune des ${vue.anciens.length} correspondances ne contient « ${filtreRapide.trim()} ».`}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {vue && listeReprises && (
          <div className="rig-table-cadre">
            <p style={{ ...Message, ...Aide }}>Fiches créées par une ancienne reprise pour combler un lien cassé : transférées telles quelles, à vérifier par le greffe.</p>
            <table className="rig-table rig-lm-table" style={GrilleReprises.style} aria-label="Fiches à vérifier">
              {GrilleReprises.colonnes}
              <thead><tr>{['Fiche', 'Libellé', 'Pourquoi', 'Dans la V3'].map((t) => <th key={t}>{t}</th>)}</tr></thead>
              <tbody>
                {reprises.map((f) => (
                  <tr key={`${f.tableV2}${f.idV2}`}>
                    <td title={`${f.tableV2} ${f.idV2}`}>
                      <Pastille teinte={f.tableV2 === 'CLIENT' ? 'bleu' : f.tableV2 === 'ADRESSE_BASE' ? 'violet' : 'gris'}>
                        {f.tableV2 === 'CLIENT' ? 'Client' : f.tableV2 === 'ADRESSE_BASE' ? 'Adresse' : f.tableV2}
                      </Pastille> {f.idV2}
                    </td>
                    {f.libelle ? <CelluleIdentite nom={f.libelle} titre={f.libelle} /> : <td>—</td>}
                    <td style={Coupe} title={f.motif}>{f.motif}</td>
                    <td>{f.idV3 ?? '—'}</td>
                  </tr>
                ))}
                {vue.reprises.length === 0 && <tr><td colSpan={4} className="rig-table__vide">Aucune fiche d'ancienne reprise dans ce greffe.</td></tr>}
                {vue.reprises.length > 0 && reprises.length === 0 && (
                  <tr><td colSpan={4} className="rig-table__vide">{`Aucune des ${vue.reprises.length} fiches ne contient « ${filtreRapide.trim()} ».`}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {liste && (
          <PiedListe>
            <span><b>{liste.affichees}</b>{` ${liste.affichees > 1 ? liste.mot[1] : liste.mot[0]} ${liste.affichees > 1 ? 'affichées' : 'affichée'}`}</span>
          </PiedListe>
        )}
      </ListeModerne>
    </div>
  )
}
