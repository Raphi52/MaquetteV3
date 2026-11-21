// Ecran « Numéros manquants » (NUM-013 T04, « a maquetter »). Procedure RIG v2 : LNUMCM (« Liste des numeros chrono
// manquants », procedure VB6 du menu, roles RCSNIV01 et RCSNIV05 sur RIG_DEV ; la V2 remplissait une table sans motif).
// La V3 liste chaque numero du registre donne puis jamais utilise, avec son motif : ceux repris de la V2 portent
// « manquant constaté par la V2, motif inconnu » (CA02), ceux declares dans la V3 le leur (CA03) ; un numero rendu
// depuis n'y est plus. La liste vient du serveur (route reservee a LNUMCM) par Pont/EcranManquants.cs.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { quand } from './EcranComptes'

export interface NumeroManquant {
  serie: string
  annee: number
  jour: number
  chrono: number
  numero: string
  motif: string | null
  logiciel: string
  declareLe: string
}

export interface VueEcranManquants {
  mode: 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur'
  message: string | null
  greffe: string | null
  manquants: NumeroManquant[]
}

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const GrilleRepartie = { tableLayout: 'fixed' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Toutes = ''

export function EcranManquants() {
  const [vue, setVue] = useState<VueEcranManquants | null>(null)
  const [serie, setSerie] = useState(Toutes)
  const [annee, setAnnee] = useState(Toutes)

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranManquants>('EcranManquants.Ouvrir').then((v) => { if (vivant) setVue(v) })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement de la liste…</p></div>

  if (vue.mode !== 'serveur' && vue.mode !== 'demo') {
    const etat = vue.mode === 'refuse' ? 'Accès refusé' : vue.mode === 'sans-greffe' ? 'Aucun greffe ouvert' : 'Serveur injoignable'
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete"><div className="rig-kicker">Numéros manquants</div></div>
          <p style={Message} role="alert"><span className="rig-etat rig-etat--alerte">{etat}</span>{' '}{vue.message}</p>
        </section>
      </div>
    )
  }

  const series = [...new Set(vue.manquants.map((m) => m.serie))].sort()
  const annees = [...new Set(vue.manquants.map((m) => m.annee))].sort((a, b) => b - a)
  const trouves = vue.manquants.filter((m) => (serie === Toutes || m.serie === serie) && (annee === Toutes || String(m.annee) === annee))

  return (
    <div className="rig-proc__corps">
      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Registre chronologique</div>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Série</span>
          <select className="rig-saisie-champ__saisie" value={serie} onChange={(e) => setSerie(e.target.value)}>
            <option value={Toutes}>Toutes</option>
            {series.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </label>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Année</span>
          <select className="rig-saisie-champ__saisie" value={annee} onChange={(e) => setAnnee(e.target.value)}>
            <option value={Toutes}>Toutes</option>
            {annees.map((a) => <option key={a} value={String(a)}>{a}</option>)}
          </select>
        </label>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Numéros manquants</div>
          <span className="rig-resultats-proc__compte">
            {vue.mode === 'demo' ? 'Démonstration' : `${trouves.length} numéro(s) — greffe ${vue.greffe ?? ''}`}
          </span>
        </div>
        {vue.mode === 'demo' && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Démonstration</span>
            {' '}Sans serveur, les numéros manquants relevés sur la base de développement s'affichent.
          </p>
        )}
        <div className="rig-table-cadre">
          <table className="rig-table" style={GrilleRepartie}>
            <colgroup>
              <col style={{ width: '120px' }} /><col style={{ width: '64px' }} /><col style={{ width: '60px' }} /><col />
              <col style={{ width: '64px' }} /><col style={{ width: '120px' }} />
            </colgroup>
            <thead>
              <tr>{['Numéro', 'Série', 'Année', 'Motif', 'Origine', 'Déclaré le'].map((t) => <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
            </thead>
            <tbody>
              {trouves.map((m) => (
                <tr key={`${m.serie}-${m.numero}`}>
                  <td className="rig-table__mono">{m.numero}</td>
                  <td className="rig-table__mono">{m.serie}</td>
                  <td>{m.annee}</td>
                  <td title={m.motif ?? ''} style={Coupe}>{m.motif ?? '—'}</td>
                  <td>{m.logiciel}</td>
                  <td>{quand(m.declareLe)}</td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr><td colSpan={6} className="rig-table__vide">Aucun numéro manquant pour ce choix : le registre est continu.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
