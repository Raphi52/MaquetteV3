// Ecran « Réglages du greffe » (SOC-003 T03, « a maquetter »). Sans origine RIG v2 : la V2 n'a pas d'ecran
// generique de ses reglages (table PARAMETRAGE) -- chacun de ses ecrans ecrit les siens sous sa propre fonction
// (PARAMJUD, PARAMEXPRESSO...), et PROC_GREFFE edite la fiche du greffe, pas ces reglages (socle-greffes.md).
// L'ecran montre donc ce que la V2 garde de chaque reglage : code, libelle, valeur EN TEXTE (jamais normalisee,
// decision Q2), utilisation, et qui l'a modifie et quand (CA07). La modification passe par la route du lot 9,
// reservee a la regle V2 de la fiche du greffe ; un refus s'affiche avec le motif du serveur. Tout passe par
// Pont/EcranReglages.cs.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'
import { quand } from './EcranComptes'

export interface ReglageGreffe {
  code: string
  libelle: string
  valeur: string | null
  utilisation: string | null
  auteurCreation: string | null
  dateCreation: string | null
  auteurModif: string | null
  dateModif: string | null
}

export interface VueEcranReglages {
  mode: 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur'
  message: string | null
  echec: boolean
  greffe: string | null
  reglages: ReglageGreffe[]
  selection: string | null
}

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const GrilleRepartie = { tableLayout: 'fixed' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Note = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)', overflowWrap: 'anywhere' } as const
/** Le reglage choisi se repere au filet, comme le geste choisi de l'ecran des comptes. */
const FiletChoisi = { boxShadow: 'inset 3px 0 0 0 var(--rig-focus)' } as const

/** « par X le 20/11/2026 09:42 », ou « jamais » sans trace. */
export function trace(auteur: string | null, date: string | null): string {
  if (!auteur && !date) return 'jamais'
  return `${auteur ? `par ${auteur}` : ''}${auteur && date ? ' ' : ''}${date ? `le ${quand(date)}` : ''}`
}

export function EcranReglages() {
  const [vue, setVue] = useState<VueEcranReglages | null>(null)
  const [recherche, setRecherche] = useState('')
  const [choisi, setChoisi] = useState<string | null>(null)
  const [valeur, setValeur] = useState('')
  const [occupe, setOccupe] = useState(false)

  const recevoir = (v: VueEcranReglages) => {
    setVue(v)
    const r = v.selection ? v.reglages.find((x) => x.code === v.selection) : undefined
    if (r) { setChoisi(r.code); if (!v.echec) setValeur(r.valeur ?? '') }
  }

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranReglages>('EcranReglages.Ouvrir').then((v) => { if (vivant) recevoir(v) })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement des réglages…</p></div>

  if (vue.mode === 'refuse' || vue.mode === 'erreur' || vue.mode === 'sans-greffe') {
    const etat = vue.mode === 'refuse' ? 'Accès refusé' : vue.mode === 'sans-greffe' ? 'Aucun greffe ouvert' : 'Serveur injoignable'
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete"><div className="rig-kicker">Réglages du greffe</div></div>
          <p style={Message} role="alert"><span className="rig-etat rig-etat--alerte">{etat}</span>{' '}{vue.message}</p>
        </section>
      </div>
    )
  }

  const lecture = vue.mode === 'demo'
  const courant = choisi ? vue.reglages.find((r) => r.code === choisi) : undefined
  let trouves = vue.reglages
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouves = trouves.filter((r) => contientSansCasse(r.code, t) || contientSansCasse(r.libelle, t) || contientSansCasse(r.valeur ?? '', t))
  }
  const choisir = (r: ReglageGreffe) => {
    setChoisi(r.code)
    setValeur(r.valeur ?? '')
    setVue((v) => (v && v.message ? { ...v, message: null, echec: false } : v))
  }
  const change = courant !== undefined && valeur !== (courant.valeur ?? '')

  const enregistrer = async () => {
    if (!courant) return
    setOccupe(true)
    try { recevoir(await appeler<VueEcranReglages>('EcranReglages.Modifier', courant.code, valeur === '' ? null : valeur)) }
    finally { setOccupe(false) }
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Modifier un réglage</div>
        {!courant && <p style={Note}>Choisissez un réglage dans la liste.</p>}
        {courant && (
          <>
            <p className="rig-table__mono" style={{ ...Note, color: 'var(--rig-texte)' }}>{courant.code}</p>
            <p style={Note}>{courant.libelle}</p>
            {courant.utilisation && <p style={Note}>Utilisation : {courant.utilisation}</p>}
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Valeur</span>
              <textarea className="rig-saisie-champ__saisie" rows={3} maxLength={1000} value={valeur} disabled={lecture}
                        onChange={(e) => setValeur(e.target.value)} />
            </label>
            <p style={Note}>Dernière modification : {trace(courant.auteurModif, courant.dateModif)}</p>
            <p style={Note}>Création : {trace(courant.auteurCreation, courant.dateCreation)}</p>
          </>
        )}
        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" disabled={lecture || occupe || !change} onClick={() => void enregistrer()}>
            Enregistrer
          </button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Réglages du greffe</div>
          <span className="rig-resultats-proc__compte">
            {lecture ? 'Démonstration' : `${vue.reglages.length} réglage(s) — greffe ${vue.greffe ?? ''}`}
          </span>
        </div>

        {lecture && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Démonstration</span>
            {' '}Sans serveur, des réglages relevés sur la base de développement s'affichent en lecture seule.
          </p>
        )}
        {vue.message && (
          <p style={Message} role={vue.echec ? 'alert' : 'status'}>
            <span className={`rig-etat rig-etat--${vue.echec ? 'alerte' : 'ok'}`}>{vue.echec ? 'Refusé' : 'Enregistré'}</span>
            {' '}{vue.message}
          </p>
        )}

        <label className="rig-saisie-champ" style={{ margin: 'var(--rig-esp-2) var(--rig-esp-3)' }}>
          <span className="rig-saisie-champ__libelle">Code, libellé ou valeur</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <div className="rig-table-cadre">
          <table className="rig-table" style={GrilleRepartie}>
            <colgroup>
              <col style={{ width: '30%' }} /><col /><col style={{ width: '120px' }} /><col style={{ width: '150px' }} /><col style={{ width: '120px' }} />
            </colgroup>
            <thead>
              <tr>{['Code', 'Libellé', 'Valeur', 'Modifié par', 'Le'].map((t) => <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
            </thead>
            <tbody>
              {trouves.map((r) => (
                <tr key={r.code} onClick={() => choisir(r)} style={r.code === choisi ? FiletChoisi : undefined}>
                  <td className="rig-table__mono" title={r.code} style={Coupe}>{r.code}</td>
                  <td title={r.libelle} style={Coupe}>{r.libelle}</td>
                  <td className="rig-table__mono" title={r.valeur ?? ''} style={Coupe}>{r.valeur ?? '—'}</td>
                  <td title={r.auteurModif ?? ''} style={Coupe}>{r.auteurModif ?? '—'}</td>
                  <td>{r.dateModif ? quand(r.dateModif) : '—'}</td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr><td colSpan={5} className="rig-table__vide">Aucun réglage ne répond à cette recherche.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
