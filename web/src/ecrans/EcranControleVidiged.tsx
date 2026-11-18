// Ecran de la procedure PROC_CONTROLE_VIDIGED (RIG v2 : trois blocs
// OPE_CRITERES, OPE_PAGINATION et OPE_RESULTATS). Colonnes lues dans les
// grilles v2 : Guid, Type, Date, DateArchivage, Taille, Info, CD, Count.
//
// Porte depuis Components/Ecrans/EcranControleVidiged.razor (lot 5) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// archives de demonstration, ses filtres et sa pagination sont ici, sans pont.

import { useState } from 'react'
import { contientSansCasse, dateCourte, distinctsTries, entierLisible, estBlanc } from '../criteres'

interface ArchiveVidi {
  guid: string
  type: string
  /** aaaa-mm-jj */
  date: string
  /** aaaa-mm-jj */
  archivage: string
  taille: string
  info: string
  cd: string
  nombre: number
}

const Archives: ArchiveVidi[] = [
  { guid: 'a41f-2026-0091', type: 'Lot de numerisation', date: '2026-08-03', archivage: '2026-08-04', taille: '412 Mo', info: 'Actes RCS aout', cd: 'CD-2026-014', nombre: 318 },
  { guid: 'a41f-2026-0092', type: 'Lot de numerisation', date: '2026-08-10', archivage: '2026-08-11', taille: '377 Mo', info: 'Actes RCS aout', cd: 'CD-2026-014', nombre: 291 },
  { guid: 'b77c-2026-0103', type: 'Comptes annuels', date: '2026-08-20', archivage: '2026-08-21', taille: '1,2 Go', info: 'Depots comptes 2025', cd: '', nombre: 904 },
  { guid: 'b77c-2026-0104', type: 'Comptes annuels', date: '2026-08-27', archivage: '2026-08-28', taille: '980 Mo', info: 'Depots comptes 2025', cd: 'CD-2026-015', nombre: 733 },
  { guid: 'c12d-2026-0110', type: 'Documents judiciaires', date: '2026-09-01', archivage: '2026-09-02', taille: '204 Mo', info: 'GED judiciaire', cd: '', nombre: 126 },
]

export function EcranControleVidiged() {
  const [recherche, setRecherche] = useState('')
  const [critType, setCritType] = useState('')
  const [du, setDu] = useState('')
  const [au, setAu] = useState('')
  const [sansCd, setSansCd] = useState(false)
  // Le texte saisi et la derniere valeur LISIBLE (le @bind d'un int).
  const [parPageTexte, setParPageTexte] = useState('3')
  const [parPage, setParPage] = useState(3)
  const [page, setPage] = useState(1)

  function saisirParPage(texte: string) {
    setParPageTexte(texte)
    const n = entierLisible(texte)
    if (n !== null) setParPage(n)
  }

  let q = Archives
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    q = q.filter((a) => contientSansCasse(a.guid, t) || contientSansCasse(a.info, t))
  }
  if (critType !== '') q = q.filter((a) => a.type === critType)
  if (du !== '') q = q.filter((a) => a.archivage >= du)
  if (au !== '') q = q.filter((a) => a.archivage <= au)
  if (sansCd) q = q.filter((a) => a.cd === '')
  const filtrees = q

  const taille = Math.max(parPage, 1)
  const nbPages = Math.max(1, Math.ceil(filtrees.length / taille))
  const pageAffichee = Math.min(Math.max(page, 1), nbPages)
  const pageCourante = filtrees.slice((pageAffichee - 1) * taille, pageAffichee * taille)

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Guid ou info</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type d'archive</span>
          <select className="rig-saisie-champ__saisie" value={critType} onChange={(e) => setCritType(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(Archives.map((a) => a.type)).map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date d'archivage</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date" value={du} onChange={(e) => setDu(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date" value={au} onChange={(e) => setAu(e.target.value)} />
            </label>
          </div>
        </div>

        <button className={`rig-bascule${sansCd ? ' rig-bascule--actif' : ''}`}
                onClick={() => setSansCd(!sansCd)}>
          <span className="rig-bascule__temoin"></span>
          <span>Sans support CD uniquement</span>
        </button>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Pagination</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Lignes par page</span>
            <input className="rig-saisie-champ__saisie" type="number" min="1" max="200" value={parPageTexte}
                   onChange={(e) => saisirParPage(e.target.value)} />
          </label>
          <div className="rig-criteres__actions">
            <button className="rig-bouton rig-bouton--plat" onClick={() => setPage(Math.max(1, page - 1))}>Precedent</button>{' '}
            <button className="rig-bouton rig-bouton--plat" onClick={() => setPage(Math.min(nbPages, page + 1))}>Suivant</button>
          </div>
          {/* Le numero AFFICHE est celui tenu par les boutons, comme dans Blazor ;
              la page montree, elle, est ramenee dans les bornes. */}
          <span className="rig-resultats-proc__compte">Page {page} / {nbPages}</span>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Resultats</div>
          <span className="rig-resultats-proc__compte">{filtrees.length} archive(s) — {pageCourante.length} affichee(s)</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Guid</th>
                <th>Type</th>
                <th>Date</th>
                <th>DateArchivage</th>
                <th>Taille</th>
                <th>Info</th>
                <th>CD</th>
                <th className="rig-table__centre">Count</th>
              </tr>
            </thead>
            <tbody>
              {pageCourante.map((a) => (
                <tr key={a.guid} className={a.cd === '' ? 'rig-table__ligne--alerte' : ''}>
                  <td className="rig-table__mono">{a.guid}</td>
                  <td>{a.type}</td>
                  <td className="rig-table__mono">{dateCourte(a.date)}</td>
                  <td className="rig-table__mono">{dateCourte(a.archivage)}</td>
                  <td className="rig-table__mono">{a.taille}</td>
                  <td>{a.info}</td>
                  <td className="rig-table__mono">{a.cd}</td>
                  <td className="rig-table__centre">{a.nombre}</td>
                </tr>
              ))}
              {pageCourante.length === 0 && (
                <tr><td colSpan={8} className="rig-table__vide">Aucune archive ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
