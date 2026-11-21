// Ecran « Courriels envoyés » (SOC-006, « a maquetter »). Procedure RIG v2 : PROC_MAIL_LOT1 (« Mails envoyés en Lot 1 »,
// fonction MAIL_LOT1) : la liste des courriels partis, filtree par periode (aujourd'hui a l'ouverture), destinataire et
// modele, 500 lignes au plus, le texte d'un courriel a la demande. La V2 n'y montrait que les courriels partis (« le Lot 1
// ne stocke pas d'erreur ») ; la V3 trace chaque envoi (CA03) et montre aussi ceux qui ont echoue, et pourquoi, et hors
// production ceux qui n'ont pas ete remis au serveur de messagerie mais ranges dans la boite de depot (CA06). Lecture
// seule. Tout passe par Pont/EcranCourriels.cs ; la trace est tenue par le serveur (Serveur/Socle/Courriels, prouvee par
// tests/SocleCourrielsCheck).
// fix-ok: relecture avant tout lancement -- la 1re version montrait un serveur en erreur (404, 503) sous forme de bandeau
// au-dessus d'un formulaire inutilisable ; comme EcranManquants et EcranReglages, toute vue hors « serveur » et « demo »
// est desormais une page d'alerte (un 400 ne peut pas venir de cet ecran : dates et issue viennent de champs fermes).
// fix-ok: capture claire du 2026-11-21 sur bureau cache -- « Non remis (hors production) » coupe dans une colonne de
// 150 px (200 px desormais) ; « Tout afficher » en rig-bouton--plat (11 px) a cote de « Chercher » : bouton simple.

import { useEffect, useState, type FormEvent } from 'react'
import { appeler } from '../pont'
import { quand } from './EcranComptes'

export interface CourrielSuivi {
  suivi: string
  debut: string
  etat: 'envoye' | 'ignore' | 'echec' | 'en-cours'
  typeModele: string | null
  expediteur: string | null
  destinataires: string | null
  copie: string | null
  sujet: string | null
  nbPieces: number | null
  idCourriel: number | null
  erreur: string | null
  agent: string | null
  fichierDepot: string | null
}

export interface EtapeCourriel {
  instant: string
  etape: string
  mode: string | null
  succes: boolean | null
  smtpHote: string | null
  smtpPort: number | null
  erreurType: string | null
  erreurMessage: string | null
  fichierDepot: string | null
}

export interface DetailCourriel {
  envoi: CourrielSuivi
  corps: string | null
  corpsHtml: boolean | null
  etapes: EtapeCourriel[]
}

export interface FiltreCourriels {
  du: string | null
  au: string | null
  destinataire: string | null
  modele: string | null
  etat: string | null
}

export interface VueEcranCourriels {
  mode: 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur'
  message: string | null
  greffe: string | null
  filtre: FiltreCourriels
  envois: CourrielSuivi[]
}

export interface VueDetailCourriel {
  mode: string
  message: string | null
  detail: DetailCourriel | null
}

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const GrilleRepartie = { tableLayout: 'fixed' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Note = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)', overflowWrap: 'anywhere' } as const
const FiletChoisi = { boxShadow: 'inset 3px 0 0 0 var(--rig-focus)' } as const
const Detail = { padding: 'var(--rig-esp-2) var(--rig-esp-3)', borderTop: '1px solid var(--rig-bord)' } as const
const Texte = { margin: 'var(--rig-esp-2) 0 0', padding: 'var(--rig-esp-2)', whiteSpace: 'pre-wrap', fontSize: 'var(--rig-fs-dense)',
  border: '1px solid var(--rig-bord)', borderRadius: 'var(--rig-rayon, 4px)', maxHeight: '180px', overflow: 'auto' } as const

/** L'issue d'un envoi, en clair, et la couleur de sa pastille. */
export const Etats: Record<CourrielSuivi['etat'], { libelle: string; teinte: string }> = {
  envoye: { libelle: 'Envoyé', teinte: 'ok' },
  ignore: { libelle: 'Non remis (hors production)', teinte: 'attente' },
  echec: { libelle: 'Échec', teinte: 'alerte' },
  'en-cours': { libelle: 'Sans issue', teinte: 'action' },
}

/** Les etapes de la trace (EMAIL_ENVOI_TRACE de la V2), en clair. */
export const Etapes: Record<string, string> = {
  PREPARATION_EMAIL: 'Courriel préparé',
  PREPARATION_ERREUR: 'Préparation impossible',
  EMAIL_SEND_DEBUT: 'Envoi commencé',
  SMTP_APPEL: 'Remis au serveur de messagerie',
  SMTP_IGNORE_DEV: 'Non remis : rangé dans la boîte de dépôt (hors production)',
  EMAIL_SEND_SUCCES: 'Envoi terminé',
  EMAIL_SEND_ERREUR: 'Envoi refusé',
  EMAIL_ENVOYE: 'Courriel enregistré',
}

const sansVide = (s: string) => (s.trim() === '' ? null : s.trim())

export function EcranCourriels() {
  const [vue, setVue] = useState<VueEcranCourriels | null>(null)
  const [du, setDu] = useState('')
  const [au, setAu] = useState('')
  const [destinataire, setDestinataire] = useState('')
  const [modele, setModele] = useState('')
  const [etat, setEtat] = useState('')
  const [choisi, setChoisi] = useState<string | null>(null)
  const [detail, setDetail] = useState<VueDetailCourriel | null>(null)
  const [occupe, setOccupe] = useState(false)

  const recevoir = (v: VueEcranCourriels) => {
    setVue(v)
    setDu(v.filtre.du ?? '')
    setAu(v.filtre.au ?? '')
    setDestinataire(v.filtre.destinataire ?? '')
    setModele(v.filtre.modele ?? '')
    setEtat(v.filtre.etat ?? '')
    setChoisi(null)
    setDetail(null)
  }

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranCourriels>('EcranCourriels.Ouvrir').then((v) => { if (vivant) recevoir(v) })
    return () => { vivant = false }
  }, [])

  const chercher = async (f: FiltreCourriels) => {
    setOccupe(true)
    try { recevoir(await appeler<VueEcranCourriels>('EcranCourriels.Chercher', f)) }
    finally { setOccupe(false) }
  }
  const filtre = (change: Partial<FiltreCourriels> = {}): FiltreCourriels =>
    ({ du: sansVide(du), au: sansVide(au), destinataire: sansVide(destinataire), modele: sansVide(modele), etat: sansVide(etat), ...change })
  const valider = (e: FormEvent) => { e.preventDefault(); void chercher(filtre()) }
  const ouvrir = async (envoi: CourrielSuivi) => {
    setChoisi(envoi.suivi)
    setDetail(await appeler<VueDetailCourriel>('EcranCourriels.Detail', envoi.suivi))
  }

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement des courriels…</p></div>

  if (vue.mode !== 'serveur' && vue.mode !== 'demo') {
    const libelle = vue.mode === 'refuse' ? 'Accès refusé' : vue.mode === 'sans-greffe' ? 'Aucun greffe ouvert' : 'Serveur injoignable'
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete"><div className="rig-kicker">Courriels envoyés</div></div>
          <p style={Message} role="alert"><span className="rig-etat rig-etat--alerte">{libelle}</span>{' '}{vue.message}</p>
        </section>
      </div>
    )
  }

  const demo = vue.mode === 'demo'
  const d = detail?.detail

  return (
    <div className="rig-proc__corps">
      <aside className="rig-criteres">
        <form onSubmit={valider}>
          <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Rechercher</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Du</span>
            <input className="rig-saisie-champ__saisie" type="date" value={du}
                   onChange={(e) => { setDu(e.target.value); void chercher(filtre({ du: sansVide(e.target.value) })) }} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Au</span>
            <input className="rig-saisie-champ__saisie" type="date" value={au}
                   onChange={(e) => { setAu(e.target.value); void chercher(filtre({ au: sansVide(e.target.value) })) }} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Destinataire</span>
            <input className="rig-saisie-champ__saisie" value={destinataire} onChange={(e) => setDestinataire(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Modèle</span>
            <input className="rig-saisie-champ__saisie" value={modele} onChange={(e) => setModele(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Issue</span>
            <select className="rig-saisie-champ__saisie" value={etat}
                    onChange={(e) => { setEtat(e.target.value); void chercher(filtre({ etat: sansVide(e.target.value) })) }}>
              <option value="">Toutes</option>
              {(Object.keys(Etats) as CourrielSuivi['etat'][]).map((k) => <option key={k} value={k}>{Etats[k].libelle}</option>)}
            </select>
          </label>
          <div className="rig-criteres__actions">
            <button type="submit" className="rig-bouton rig-bouton--fort" disabled={occupe}>Chercher</button>
            <button type="button" className="rig-bouton" disabled={occupe}
                    onClick={() => void chercher({ du: null, au: null, destinataire: null, modele: null, etat: null })}>
              Tout afficher
            </button>
          </div>
        </form>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Courriels envoyés</div>
          <span className="rig-resultats-proc__compte">
            {demo ? 'Démonstration' : `${vue.envois.length} courriel(s) — greffe ${vue.greffe ?? ''}`}
          </span>
        </div>
        {demo && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Démonstration</span>
            {' '}Sans serveur, trois envois d'exemple s'affichent : un parti, un non remis hors production, un refusé.
          </p>
        )}
        <div className="rig-table-cadre">
          <table className="rig-table" style={GrilleRepartie}>
            <colgroup>
              <col style={{ width: '120px' }} /><col style={{ width: '200px' }} /><col style={{ width: '110px' }} />
              <col style={{ width: '24%' }} /><col /><col style={{ width: '60px' }} />
            </colgroup>
            <thead>
              <tr>{['Le', 'Issue', 'Modèle', 'Destinataires', 'Objet', 'Pièces'].map((t) => <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
            </thead>
            <tbody>
              {vue.envois.map((e) => (
                <tr key={e.suivi} onClick={() => void ouvrir(e)} style={e.suivi === choisi ? FiletChoisi : undefined}>
                  <td>{quand(e.debut)}</td>
                  <td style={Coupe}><span className={`rig-etat rig-etat--${Etats[e.etat].teinte}`}>{Etats[e.etat].libelle}</span></td>
                  <td className="rig-table__mono" title={e.typeModele ?? ''} style={Coupe}>{e.typeModele ?? '—'}</td>
                  <td title={e.destinataires ?? ''} style={Coupe}>{e.destinataires ?? '—'}</td>
                  <td title={e.erreur ?? e.sujet ?? ''} style={Coupe}>{e.sujet ?? '—'}</td>
                  <td>{e.nbPieces ?? 0}</td>
                </tr>
              ))}
              {vue.envois.length === 0 && (
                <tr><td colSpan={6} className="rig-table__vide">Aucun courriel pour cette recherche.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {detail && !d && <p style={Message} role="alert"><span className="rig-etat rig-etat--alerte">Détail</span>{' '}{detail.message}</p>}
        {d && (
          <div style={Detail} aria-label="Détail du courriel">
            <div className="rig-kicker">{d.envoi.sujet ?? 'Courriel'}</div>
            <p style={Note}>De {d.envoi.expediteur ?? '—'} à {d.envoi.destinataires ?? '—'}{d.envoi.copie ? `, copie cachée ${d.envoi.copie}` : ''}</p>
            <p style={Note}>Suivi <span className="rig-table__mono">{d.envoi.suivi}</span>{d.envoi.agent ? ` — demandé par ${d.envoi.agent}` : ''}</p>
            {d.envoi.erreur && <p style={Note} role="alert"><span className="rig-etat rig-etat--alerte">Refus</span>{' '}{d.envoi.erreur}</p>}
            {d.envoi.fichierDepot && <p style={Note}>Boîte de dépôt : <span className="rig-table__mono">{d.envoi.fichierDepot}</span></p>}
            <table className="rig-table" style={{ ...GrilleRepartie, marginTop: 'var(--rig-esp-2)' }}>
              <colgroup><col style={{ width: '120px' }} /><col /><col style={{ width: '35%' }} /></colgroup>
              <thead><tr>{['Le', 'Étape', 'Serveur ou erreur'].map((t) => <th key={t}>{t}</th>)}</tr></thead>
              <tbody>
                {d.etapes.map((x, i) => (
                  <tr key={i}>
                    <td>{quand(x.instant)}</td>
                    <td title={x.etape} style={Coupe}>{Etapes[x.etape] ?? x.etape}</td>
                    <td title={x.erreurMessage ?? x.smtpHote ?? ''} style={Coupe}>
                      {x.erreurMessage ?? (x.smtpHote ? `${x.smtpHote}:${x.smtpPort ?? ''}` : '')}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {d.corps !== null && (
              d.corpsHtml
                ? <iframe title="Texte du courriel" sandbox="" srcDoc={d.corps} style={{ ...Texte, width: '100%', height: '180px' }} />
                : <pre style={Texte} aria-label="Texte du courriel">{d.corps.split('|').join('\n')}</pre>
            )}
          </div>
        )}
      </section>
    </div>
  )
}
