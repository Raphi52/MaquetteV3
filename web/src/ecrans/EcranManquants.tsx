// Ecran « Numéros manquants » (NUM-013 T04, « a maquetter »). Procedure RIG v2 : LNUMCM (« Liste des numeros chrono
// manquants », procedure VB6 du menu, roles RCSNIV01 et RCSNIV05 sur RIG_DEV ; la V2 remplissait une table sans motif).
// La V3 liste chaque numero du registre donne puis jamais utilise, avec son motif : ceux repris de la V2 portent
// « manquant constaté par la V2, motif inconnu » (CA02), ceux declares dans la V3 le leur (CA03) ; un numero rendu
// depuis n'y est plus. La liste vient du serveur (route reservee a LNUMCM) par Pont/EcranManquants.cs.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 3) : la liste prend les briques de
// composants/ListeModerne.tsx. Tete (synthese : nombre, greffe, repris de la V2 ; « Aller à un numéro… » Ctrl K :
// un registre peut manquer de centaines de numeros), une vue par serie (le critere « Série » de gauche, annee
// remise a « Toutes »), serie et origine en pastilles, un pied. Six colonnes : pas de ligne depliee. Les libelles
// existants de l'ecran gardent leurs accents. Les criteres, les messages de refus et la demonstration ne changent pas.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { quand } from './EcranComptes'
import { EnteteListe, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe, type Teinte } from '../composants/ListeModerne'

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
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Toutes = ''

/** Une serie garde sa teinte : elle suit son rang dans la liste des series du registre. */
const TeintesSerie: Teinte[] = ['ardoise', 'bleu', 'violet', 'vert', 'orange']
/** L'origine du numero manquant : repris de la V2 ou declare dans la V3. */
const TeinteOrigine: Record<string, Teinte> = { V2: 'ardoise', V3: 'bleu' }

const NbColonnes = 6

/**
 * « Aller a… » compare sans casse ni accents, comme la Recherche d'affaires : « abandonnee » trouve « abandonnée ».
 * fix-ok: contientSansCasse (criteres.ts) garde les accents ; test « Aller à un numéro… » rouge sur « abandonnee », vert ici.
 */
function normaliser(texte: string): string {
  return texte.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '')
}

export function EcranManquants() {
  const [vue, setVue] = useState<VueEcranManquants | null>(null)
  const [serie, setSerie] = useState(Toutes)
  const [annee, setAnnee] = useState(Toutes)
  // « Aller à un numéro… » (Ctrl K) : filtre la liste AFFICHEE.
  const [filtreRapide, setFiltreRapide] = useState('')

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
        <ListeModerne etiquette="Numéros manquants">
          <EnteteListe titre="Numéros manquants" />
          <p style={Message} role="alert"><PointEtat teinte="rouge">{etat}</PointEtat>{' '}{vue.message}</p>
        </ListeModerne>
      </div>
    )
  }

  const series = [...new Set(vue.manquants.map((m) => m.serie))].sort()
  const annees = [...new Set(vue.manquants.map((m) => m.annee))].sort((a, b) => b - a)
  const trouves = vue.manquants.filter((m) => (serie === Toutes || m.serie === serie) && (annee === Toutes || String(m.annee) === annee))
  const cherche = filtreRapide.trim()
  const chercheNormal = normaliser(cherche)
  const affiches = cherche === '' ? trouves : trouves.filter((m) => normaliser(`${m.numero} ${m.motif ?? ''}`).includes(chercheNormal))
  const reprisV2 = affiches.filter((m) => m.logiciel === 'V2').length
  const teinteSerie = (s: string): Teinte => TeintesSerie[Math.max(0, series.indexOf(s)) % TeintesSerie.length]!

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

      <ListeModerne etiquette="Numéros manquants">
        <EnteteListe titre="Numéros manquants"
                     synthese={<>{vue.mode === 'demo' ? 'Démonstration · ' : ''}{affiches.length}{' numéro(s)'}
                       {vue.mode === 'serveur' ? ` — greffe ${vue.greffe ?? ''}` : ''}{' · '}<b>{reprisV2}</b>{' repris de la V2'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller à un numéro…' }} />

        {/* Une vue par serie : le critere « Série », annee remise a « Toutes ». */}
        <VuesListe vues={[Toutes, ...series].map((s) => ({
                     cle: s, libelle: s === Toutes ? 'Toutes' : s,
                     compte: vue.manquants.filter((m) => s === Toutes || m.serie === s).length,
                   }))}
                   active={annee === Toutes ? serie : null}
                   choisir={(s) => { setSerie(s); setAnnee(Toutes) }} />

        {vue.mode === 'demo' && (
          <p style={Message}>
            <PointEtat teinte="gris">Démonstration</PointEtat>
            {' '}Sans serveur, les numéros manquants relevés sur la base de développement s'affichent.
          </p>
        )}
        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: '120px' }} /><col style={{ width: '80px' }} /><col style={{ width: '60px' }} /><col />
              <col style={{ width: '76px' }} /><col style={{ width: '120px' }} />
            </colgroup>
            <thead>
              <tr>{['Numéro', 'Série', 'Année', 'Motif', 'Origine', 'Déclaré le'].map((t) => <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
            </thead>
            <tbody>
              {affiches.map((m) => (
                <tr key={`${m.serie}-${m.numero}`}>
                  <td className="rig-table__mono">{m.numero}</td>
                  <td><Pastille teinte={teinteSerie(m.serie)}>{m.serie}</Pastille></td>
                  <td>{m.annee}</td>
                  <td title={m.motif ?? ''}>{m.motif ?? '—'}</td>
                  <td><Pastille teinte={TeinteOrigine[m.logiciel] ?? 'gris'}>{m.logiciel}</Pastille></td>
                  <td>{quand(m.declareLe)}</td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">Aucun numéro manquant pour ce choix : le registre est continu.</td></tr>
              )}
              {trouves.length > 0 && affiches.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">{`Aucun des ${trouves.length} numéros ne contient « ${cherche} ».`}</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{affiches.length}</b>{affiches.length > 1 ? ' numéros affichés' : ' numéro affiché'}</span>
          <span>Repris de la V2 <b>{reprisV2}</b></span>
          <span>Déclarés dans la V3 <b>{affiches.filter((m) => m.logiciel === 'V3').length}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
