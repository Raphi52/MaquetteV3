// Ecran « Compteurs de numéros » : « a maquetter » dans le carnet (NUM-004 T04, « écran de
// réattribution d'un numéro »). Procedure RIG v2 : XNUMERO (« Reattribution de numero metier »,
// procedure VB6 du menu, roles RCSNIV03 et RCSNIV05 sur RIG_DEV ; sans source ni fiche dans le depot).
//
// A gauche, rendre un numero donne par erreur : serie, numero, explication (obligatoire ici, NUM-004
// T04). A droite, les compteurs de la base V3 du greffe : une ligne par serie, annee et jour (NUM-001
// CA01), le dernier chrono donne, si la serie redonne ses numeros rendus et combien attendent. Les
// regles (format lu dans le numero, jamais donne refuse, deja rendu, serie sans restitution) sont
// tenues par le serveur (Serveur/Numerotation/Compteur, prouvees par tests/NumerotationCompteurCheck) :
// l'ecran montre sa reponse telle quelle. Tout passe par Pont/EcranCompteurs.cs.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'
import { quand } from './EcranComptes'

export interface CompteurNumeros {
  type: string
  annee: number
  jour: number
  libelle: string | null
  structure: string
  codageAnnee: number
  lettre: string | null
  separateur: string | null
  tailleChrono: number
  codeGreffe: string | null
  debut: number
  chrono: number | null
  gestionRendus: boolean
  redirection: string | null
  tableCible: string | null
  colonneCible: string | null
  derniereAttribution: string | null
  numerosRendus: number
}

export interface VueEcranCompteurs {
  mode: 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur'
  message: string | null
  echec: boolean
  greffe: string | null
  compteurs: CompteurNumeros[]
}

/** La ligne de l'ancien logiciel ne garde que ces caracteres de l'explication (NUM-004 CA08). */
export const LongueurExplicationV2 = 50

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const GrilleRepartie = { tableLayout: 'fixed' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis' } as const
/** Sous un champ : la note dense de l'ecran des comptes (DetailGeste). */
const Aide = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)' } as const

/** Une serie sans « A » dans son format ne porte pas l'annee : on la demande (NUM-004 CA07). */
export function porteAnnee(structure: string): boolean {
  return structure.toUpperCase().includes('A')
}

export function EcranCompteurs() {
  const [vue, setVue] = useState<VueEcranCompteurs | null>(null)
  const [recherche, setRecherche] = useState('')
  const [type, setType] = useState('')
  const [numero, setNumero] = useState('')
  const [explication, setExplication] = useState('')
  const [annee, setAnnee] = useState(String(new Date().getFullYear()))
  const [occupe, setOccupe] = useState(false)

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranCompteurs>('EcranCompteurs.Ouvrir').then((v) => { if (vivant) setVue(v) })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement des compteurs…</p></div>

  if (vue.mode === 'refuse' || vue.mode === 'erreur' || vue.mode === 'sans-greffe') {
    const etat = vue.mode === 'refuse' ? 'Accès refusé' : vue.mode === 'sans-greffe' ? 'Aucun greffe ouvert' : 'Serveur injoignable'
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete">
            <div className="rig-kicker">Compteurs de numéros</div>
          </div>
          <p style={Message} role="alert">
            <span className="rig-etat rig-etat--alerte">{etat}</span>{' '}{vue.message}
          </p>
        </section>
      </div>
    )
  }

  const lecture = vue.mode === 'demo'
  const series = [...new Set(vue.compteurs.map((c) => c.type))].sort()
  const choisie = vue.compteurs.find((c) => c.type === type)
  const demandeAnnee = choisie !== undefined && !porteAnnee(choisie.structure)
  let trouves = vue.compteurs
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouves = trouves.filter((c) => contientSansCasse(c.type, t) || contientSansCasse(c.libelle ?? '', t))
  }
  const complet = !estBlanc(type) && !estBlanc(numero) && !estBlanc(explication)

  const rendre = async () => {
    setOccupe(true)
    try {
      const v = await appeler<VueEcranCompteurs>('EcranCompteurs.Rendre', {
        type, numero: numero.trim(), explication: explication.trim(), annee: demandeAnnee ? Number(annee) || 0 : 0,
      })
      setVue(v)
      if (!v.echec) { setNumero(''); setExplication('') }
    } finally { setOccupe(false) }
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Rendre un numéro</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Série</span>
          <select className="rig-saisie-champ__saisie" value={type} disabled={lecture} onChange={(e) => setType(e.target.value)}>
            <option value="">—</option>
            {series.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </label>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Numéro donné par erreur</span>
          <input className="rig-saisie-champ__saisie" value={numero} disabled={lecture} onChange={(e) => setNumero(e.target.value)} />
        </label>
        {demandeAnnee && (
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Année (le numéro ne la porte pas)</span>
            <input className="rig-saisie-champ__saisie" inputMode="numeric" value={annee} disabled={lecture} onChange={(e) => setAnnee(e.target.value)} />
          </label>
        )}
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Explication</span>
          <textarea className="rig-saisie-champ__saisie" rows={3} value={explication} disabled={lecture} onChange={(e) => setExplication(e.target.value)} />
        </label>
        {explication.trim().length > LongueurExplicationV2 && (
          <p style={Aide}>
            Gardée en entier dans le journal de la V3 ; l'ancien logiciel n'en recevra que les {LongueurExplicationV2} premiers caractères.
          </p>
        )}
        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" disabled={lecture || occupe || !complet} onClick={() => void rendre()}>
            Rendre le numéro
          </button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Compteurs de numéros</div>
          <span className="rig-resultats-proc__compte">
            {lecture ? 'Démonstration' : `${vue.compteurs.length} compteur(s) — greffe ${vue.greffe ?? ''}`}
          </span>
        </div>

        {lecture && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Démonstration</span>
            {' '}Sans serveur, des compteurs relevés sur la base de développement s'affichent en lecture seule.
          </p>
        )}
        {vue.message && (
          <p style={Message} role={vue.echec ? 'alert' : 'status'}>
            <span className={`rig-etat rig-etat--${vue.echec ? 'alerte' : 'ok'}`}>{vue.echec ? 'Refusé' : 'Enregistré'}</span>
            {' '}{vue.message}
          </p>
        )}

        <label className="rig-saisie-champ" style={{ margin: 'var(--rig-esp-2) var(--rig-esp-3)' }}>
          <span className="rig-saisie-champ__libelle">Série ou libellé</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <div className="rig-table-cadre">
          <table className="rig-table" style={GrilleRepartie}>
            <colgroup>
              {/* Largeurs mesurees sur la capture du 21/11 (1516 px) : a 44 et 92 px, « JOUR » et la puce
                  « 2 en attente » finissaient par « … » ; « Non redonnés » demande 118 px. */}
              <col style={{ width: '64px' }} /><col /><col style={{ width: '56px' }} /><col style={{ width: '56px' }} />
              <col style={{ width: '84px' }} /><col style={{ width: '118px' }} /><col style={{ width: '120px' }} />
            </colgroup>
            <thead>
              <tr>
                {['Série', 'Libellé', 'Année', 'Jour', 'Dernier n°', 'Rendus', 'Donné le'].map((t) =>
                  <th key={t} title={t} style={Coupe}>{t}</th>)}
              </tr>
            </thead>
            <tbody>
              {trouves.map((c) => (
                <tr key={`${c.type}-${c.annee}-${c.jour}`} onClick={() => setType(c.type)}>
                  <td className="rig-table__mono">{c.type}</td>
                  <td title={c.libelle ?? ''} style={Coupe}>{c.libelle}</td>
                  <td>{c.annee}</td>
                  <td>{c.jour > 0 ? c.jour : '—'}</td>
                  <td className="rig-table__mono">{c.chrono ?? '—'}</td>
                  <td title={c.gestionRendus ? 'Un numéro rendu est redonné au prochain acte' : 'Série sans restitution : un numéro rendu n\'est pas redonné'}>
                    {c.gestionRendus
                      ? <span className={`rig-etat rig-etat--${c.numerosRendus > 0 ? 'attente' : 'ok'}`}>{c.numerosRendus > 0 ? `${c.numerosRendus} en attente` : 'Redonnés'}</span>
                      : <span className="rig-etat">Non redonnés</span>}
                  </td>
                  <td>{c.derniereAttribution ? quand(c.derniereAttribution) : '—'}</td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr><td colSpan={7} className="rig-table__vide">Aucun compteur ne répond à cette recherche.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
