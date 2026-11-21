// Ecran « Fiches en conflit » : « a maquetter » dans le carnet (REC-004 T03). Sans origine RIG v2 : l'ancien logiciel
// n'a pas de recopie, donc pas d'ecarts a montrer.
//
// Une fiche partagee (client, dossier, inscription, adresse...) modifiee dans l'ancien RIG ET dans la V3 entre deux
// passages de la recopie : la V3 a gagne dans les deux bases (REC-004 CA01, fiche entiere, CA04). Pour chaque fiche,
// les champs que l'ancien RIG avait changes : sa valeur, perdue, et celle que la V3 a gardee, avec les deux dates et
// les deux auteurs. L'agent designe ressaisit dans la V3 ce qui doit l'etre, puis marque l'ecart « traité » : il sort
// de l'alerte de l'accueil et reste consultable avec son nom et la date (CA03). Un ecart qui attend depuis 5 jours
// ouvres passe en rouge. Le clic sur la fiche l'ouvre (son affaire, ou l'ecran de sa fiche) (CA02).
// Les regles (qui gagne, quand c'est un conflit, qui peut traiter) sont tenues par le serveur (Serveur/Recopie,
// prouvees par tests/RecopieDeuxSensCheck) : l'ecran montre ce qu'il rend. Tout passe par Pont/EcranConflits.cs.
// fix-ok: fichier NEUF du lot 9 ; la premiere ecriture mettait la fiche en lien par une classe « rig-lien » absente de la
// charte (grep de wwwroot/*.css : 0 occurrence) -> bouton « Ouvrir la fiche » (rig-bouton) dans le detail.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { quand } from './EcranComptes'

export interface ChampEcart {
  champ: string
  valeurV2: string | null
  valeurV3: string | null
}

export interface EcartRecopie {
  id: number
  tableV2: string
  idV2: string
  idV3: string
  fiche: string
  cible: string | null
  detecteLe: string
  dateV2: string
  auteurV2: string | null
  dateV3: string
  auteurV3: string
  champs: ChampEcart[]
  traiteLe: string | null
  traitePar: string | null
  enRouge: boolean
}

export interface AlerteEcarts {
  nonTraites: number
  enRouge: number
  plusAncien: string | null
}

export interface VueEcranConflits {
  mode: 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur'
  message: string | null
  echec: boolean
  greffe: string | null
  peutTraiter: boolean
  traites: boolean
  table: string | null
  ecarts: EcartRecopie[]
  tables: string[]
  alerte: AlerteEcarts | null
}

export interface ProprietesEcranConflits {
  /** Ouvre l'affaire d'un numero de dossier (cible « dossier:… »). */
  ouvrirDossier: (numero: string) => void
  /** Ouvre un ecran par son code (cible « ecran:… »). */
  ouvrir: (code: string) => void
}

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Aide = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)' } as const
const Detail = { padding: 'var(--rig-esp-2) var(--rig-esp-3)', borderTop: '1px solid var(--rig-bord)' } as const

/** Une valeur pour l'agent : vide se lit « (vide) », une date ISO se lit jj/mm/aaaa. */
export function lisible(valeur: string | null): string {
  if (valeur === null || valeur.trim() === '') return '(vide)'
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(valeur)) {
    const d = quand(valeur)
    return d.endsWith(' 00:00') ? d.slice(0, -6) : d
  }
  return valeur
}

/** Ou mene la fiche d'un ecart : « dossier:… » ouvre l'affaire, « ecran:… » un ecran ; null, rien. */
export function destination(cible: string | null): { genre: 'dossier' | 'ecran'; valeur: string } | null {
  if (!cible) return null
  const i = cible.indexOf(':')
  if (i <= 0 || i === cible.length - 1) return null
  const genre = cible.slice(0, i)
  return genre === 'dossier' || genre === 'ecran' ? { genre, valeur: cible.slice(i + 1) } : null
}

export function EcranConflits({ ouvrirDossier, ouvrir }: ProprietesEcranConflits) {
  const [vue, setVue] = useState<VueEcranConflits | null>(null)
  const [choisi, setChoisi] = useState<number | null>(null)
  const [occupe, setOccupe] = useState(false)

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranConflits>('EcranConflits.Ouvrir', false, null).then((v) => { if (vivant) setVue(v) })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement des fiches en conflit…</p></div>

  if (vue.mode === 'refuse' || vue.mode === 'erreur' || vue.mode === 'sans-greffe') {
    const etat = vue.mode === 'refuse' ? 'Accès refusé' : vue.mode === 'sans-greffe' ? 'Aucun greffe ouvert' : 'Serveur injoignable'
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete"><div className="rig-kicker">Fiches en conflit</div></div>
          <p style={Message} role="alert"><span className="rig-etat rig-etat--alerte">{etat}</span>{' '}{vue.message}</p>
        </section>
      </div>
    )
  }

  const relire = async (traites: boolean, table: string | null) => {
    setOccupe(true)
    try { setVue(await appeler<VueEcranConflits>('EcranConflits.Ouvrir', traites, table)); setChoisi(null) } finally { setOccupe(false) }
  }
  const traiter = async (id: number) => {
    setOccupe(true)
    try { setVue(await appeler<VueEcranConflits>('EcranConflits.Traiter', id, vue.traites, vue.table)); setChoisi(null) } finally { setOccupe(false) }
  }
  const ouvrirFiche = (e: EcartRecopie) => {
    const d = destination(e.cible)
    if (d?.genre === 'dossier') ouvrirDossier(d.valeur)
    else if (d?.genre === 'ecran') ouvrir(d.valeur)
  }

  const detail = vue.ecarts.find((e) => e.id === choisi) ?? vue.ecarts[0] ?? null
  const a = vue.alerte

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Fiches modifiées des deux côtés</div>
        {a && (
          <p style={{ ...Message, paddingLeft: 0 }} role="status">
            <span className={`rig-etat rig-etat--${a.enRouge > 0 ? 'alerte' : a.nonTraites > 0 ? 'attente' : 'ok'}`}>
              {a.nonTraites === 0 ? 'Rien à revoir' : `${a.nonTraites} à revoir`}
            </span>
            {a.enRouge > 0 && <>{' '}dont {a.enRouge} depuis 5 jours ouvrés ou plus</>}
          </p>
        )}
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Écarts</span>
          <select className="rig-saisie-champ__saisie" value={vue.traites ? 'traites' : 'attente'} disabled={occupe}
                  onChange={(e) => void relire(e.target.value === 'traites', vue.table)}>
            <option value="attente">À traiter</option>
            <option value="traites">Déjà traités</option>
          </select>
        </label>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Table</span>
          <select className="rig-saisie-champ__saisie" value={vue.table ?? ''} disabled={occupe}
                  onChange={(e) => void relire(vue.traites, e.target.value === '' ? null : e.target.value)}>
            <option value="">Toutes</option>
            {vue.tables.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>
        <p style={Aide}>
          La V3 a gardé sa version dans les deux bases. Ressaisissez dans la V3 ce que l'ancien RIG avait écrit, s'il le faut,
          puis marquez l'écart traité.
        </p>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Fiches en conflit</div>
          <span className="rig-resultats-proc__compte">
            {vue.mode === 'demo' ? 'Démonstration' : `${vue.ecarts.length} écart(s) — greffe ${vue.greffe ?? ''}`}
          </span>
        </div>

        {vue.mode === 'demo' && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Démonstration</span>
            {' '}Sans serveur, trois écarts inventés s'affichent en lecture seule.
          </p>
        )}
        {vue.message && (
          <p style={Message} role={vue.echec ? 'alert' : 'status'}>
            <span className={`rig-etat rig-etat--${vue.echec ? 'alerte' : 'ok'}`}>{vue.echec ? 'Refusé' : 'Enregistré'}</span>
            {' '}{vue.message}
          </p>
        )}

        <div className="rig-table-cadre">
          <table className="rig-table" style={{ tableLayout: 'fixed' }}>
            <colgroup>
              <col /><col style={{ width: '120px' }} /><col style={{ width: '124px' }} /><col style={{ width: '70px' }} />
              <col style={{ width: '150px' }} />
            </colgroup>
            <thead>
              <tr>{['Fiche', 'Table', 'Vu le', 'Champs', 'État'].map((t) => <th key={t} title={t} style={Coupe}>{t}</th>)}</tr>
            </thead>
            <tbody>
              {vue.ecarts.map((e) => (
                <tr key={e.id} onClick={() => setChoisi(e.id)} aria-selected={detail?.id === e.id}>
                  <td style={Coupe} title={e.fiche}>{e.fiche}</td>
                  <td className="rig-table__mono" style={Coupe}>{e.tableV2}</td>
                  <td>{quand(e.detecteLe)}</td>
                  <td>{e.champs.length}</td>
                  <td>
                    {e.traiteLe
                      ? <span className="rig-etat rig-etat--ok" title={`par ${e.traitePar ?? '?'} le ${quand(e.traiteLe)}`}>Traité</span>
                      : <span className={`rig-etat rig-etat--${e.enRouge ? 'alerte' : 'attente'}`}>{e.enRouge ? 'En retard' : 'À traiter'}</span>}
                  </td>
                </tr>
              ))}
              {vue.ecarts.length === 0 && (
                <tr><td colSpan={5} className="rig-table__vide">
                  {vue.traites ? 'Aucun écart traité pour ce filtre.' : 'Aucune fiche modifiée des deux côtés : rien à revoir.'}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        {detail && (
          <div style={Detail} aria-label="Détail de l'écart">
            <div className="rig-kicker">{detail.fiche}</div>
            <p style={Aide}>
              Ancien RIG : {quand(detail.dateV2)}{detail.auteurV2 ? ` par ${detail.auteurV2}` : ' (auteur non noté par l\'ancien RIG)'}
              {' · '}V3 : {quand(detail.dateV3)} par {detail.auteurV3}
              {detail.traiteLe && <>{' · '}traité par {detail.traitePar} le {quand(detail.traiteLe)}</>}
            </p>
            <table className="rig-table" style={{ tableLayout: 'fixed', marginTop: 'var(--rig-esp-2)' }}>
              <colgroup><col style={{ width: '34%' }} /><col style={{ width: '33%' }} /><col style={{ width: '33%' }} /></colgroup>
              <thead><tr><th>Champ</th><th>Écrit par l'ancien RIG (perdu)</th><th>Gardé par la V3</th></tr></thead>
              <tbody>
                {detail.champs.map((c) => (
                  <tr key={c.champ}>
                    <td className="rig-table__mono" style={Coupe} title={c.champ}>{c.champ}</td>
                    <td style={Coupe} title={lisible(c.valeurV2)}>{lisible(c.valeurV2)}</td>
                    <td style={Coupe} title={lisible(c.valeurV3)}>{lisible(c.valeurV3)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <div className="rig-criteres__actions" style={{ marginTop: 'var(--rig-esp-2)' }}>
              {destination(detail.cible) && (
                <button className="rig-bouton" onClick={() => ouvrirFiche(detail)}>Ouvrir la fiche</button>
              )}
              {!detail.traiteLe && vue.peutTraiter && (
                <button className="rig-bouton rig-bouton--fort" disabled={occupe} onClick={() => void traiter(detail.id)}>Traité</button>
              )}
            </div>
          </div>
        )}
      </section>
    </div>
  )
}
