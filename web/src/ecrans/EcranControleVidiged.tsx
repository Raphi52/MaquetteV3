// Ecran de la procedure PROC_CONTROLE_VIDIGED (RIG v2 : trois blocs
// OPE_CRITERES, OPE_PAGINATION et OPE_RESULTATS). Colonnes lues dans les
// grilles v2 : Guid, Type, Date, DateArchivage, Taille, Info, CD, Count.
//
// Porte depuis Components/Ecrans/EcranControleVidiged.razor (lot 5) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// archives de demonstration, ses filtres et sa pagination sont ici, sans pont.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 3) : les resultats prennent les briques
// de composants/ListeModerne.tsx. Tete (synthese : archives, page, sans CD), des vues = jeux de criteres de
// gauche (toutes, sans support CD, un type d'archive), un tableau a une ligne par archive (Guid, Type en pastille,
// DateArchivage, Info, CD en point d'etat, Count) ; Date et Taille sont dans la ligne depliee par le chevron.
// Le critere « Guid ou info », filtre sur place sans pont, devient le champ « Aller a une archive… » (Ctrl K) de
// la tete : meme etat, memes colonnes comparees, retire du panneau de gauche (decision du plan, lot 2). Comme le
// champ de la Recherche d'affaires, il n'entre pas dans les vues. Criteres, pagination et surlignage des archives
// sans CD ne changent pas. Les vues ajoutees vivent le temps de la SESSION (decision du lot 1).

import { Fragment, useState } from 'react'
import { contientSansCasse, dateCourte, distinctsTries, entierLisible, estBlanc } from '../criteres'
import {
  CelluleChevron, EnteteListe, LigneDetail, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe, type Teinte,
} from '../composants/ListeModerne'

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

/**
 * Les criteres de gauche, tels que saisis (la pagination n'en est pas). Une vue en est un jeu complet ; le champ
 * « Aller a une archive… » de la tete n'en fait pas partie.
 */
interface CriteresVidiged {
  critType: string
  du: string
  au: string
  sansCd: boolean
}

const SansCritere: CriteresVidiged = { critType: '', du: '', au: '', sansCd: false }

function memesCriteres(a: CriteresVidiged, b: CriteresVidiged): boolean {
  return (Object.keys(SansCritere) as (keyof CriteresVidiged)[]).every((k) => a[k] === b[k])
}

/** Les archives qui repondent a un jeu de criteres (et au texte « Aller a… ») : la liste, et le compte de chaque vue. */
function filtrer(c: CriteresVidiged, recherche = ''): ArchiveVidi[] {
  let q = Archives
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    q = q.filter((a) => contientSansCasse(a.guid, t) || contientSansCasse(a.info, t))
  }
  if (c.critType !== '') q = q.filter((a) => a.type === c.critType)
  if (c.du !== '') q = q.filter((a) => a.archivage >= c.du)
  if (c.au !== '') q = q.filter((a) => a.archivage <= c.au)
  if (c.sansCd) q = q.filter((a) => a.cd === '')
  return q
}

interface VueVidiged {
  cle: string
  libelle: string
  criteres: CriteresVidiged
}

const Types = distinctsTries(Archives.map((a) => a.type))

/** Les vues toujours la : toutes, sans support CD, puis une par type d'archive (les deux criteres de l'ecran). */
const VuesPrereglees: VueVidiged[] = [
  { cle: 'toutes', libelle: 'Toutes', criteres: SansCritere },
  { cle: 'sans-cd', libelle: 'Sans support CD', criteres: { ...SansCritere, sansCd: true } },
  ...Types.map((t) => ({ cle: `type:${t}`, libelle: t, criteres: { ...SansCritere, critType: t } })),
]

/** Les vues ajoutees par « + Nouvelle vue » : elles vivent le temps de la session (aucun reglage ne les porte). */
const VuesDeLaSession: VueVidiged[] = []

/** Pour les tests : repartir d'une session sans vue ajoutee. */
export function oublierVuesDeLaSession(): void {
  VuesDeLaSession.length = 0
}

/** Un type d'archive garde sa teinte : elle suit son rang dans la liste des types. */
const TeintesType: Teinte[] = ['ardoise', 'bleu', 'violet', 'vert', 'orange']
const teinteType = (type: string): Teinte => TeintesType[Math.max(0, Types.indexOf(type)) % TeintesType.length]!

/** Les colonnes du tableau a une ligne : chevron, Guid, Type, DateArchivage, Info, CD, Count. */
const NbColonnes = 7

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
  // La ligne depliee par son chevron (une a la fois).
  const [ouverte, setOuverte] = useState<string | null>(null)

  function saisirParPage(texte: string) {
    setParPageTexte(texte)
    const n = entierLisible(texte)
    if (n !== null) setParPage(n)
  }

  const criteres: CriteresVidiged = { critType, du, au, sansCd }
  function poserCriteres(c: CriteresVidiged) {
    setCritType(c.critType)
    setDu(c.du)
    setAu(c.au)
    setSansCd(c.sansCd)
  }
  const [vuesAjoutees, setVuesAjoutees] = useState<VueVidiged[]>(() => [...VuesDeLaSession])
  const vues = [...VuesPrereglees, ...vuesAjoutees]
  const vueActive = vues.find((v) => memesCriteres(v.criteres, criteres))?.cle ?? null
  function enregistrerVue(nom: string) {
    VuesDeLaSession.push({ cle: `session-${VuesDeLaSession.length + 1}`, libelle: nom, criteres })
    setVuesAjoutees([...VuesDeLaSession])
  }

  const filtrees = filtrer(criteres, recherche)

  const taille = Math.max(parPage, 1)
  const nbPages = Math.max(1, Math.ceil(filtrees.length / taille))
  const pageAffichee = Math.min(Math.max(page, 1), nbPages)
  const pageCourante = filtrees.slice((pageAffichee - 1) * taille, pageAffichee * taille)

  // La tete compte toutes les archives filtrees ; le pied, la page montree.
  const sansSupport = filtrees.filter((a) => a.cd === '').length
  const sansSupportPage = pageCourante.filter((a) => a.cd === '').length
  const documents = pageCourante.reduce((s, a) => s + a.nombre, 0)

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type d'archive</span>
          <select className="rig-saisie-champ__saisie" value={critType} onChange={(e) => setCritType(e.target.value)}>
            <option value="">Tous</option>
            {Types.map((t) => <option key={t} value={t}>{t}</option>)}
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

      <ListeModerne etiquette="Archives">
        <EnteteListe titre="Resultats"
                     synthese={<>{filtrees.length}{' archive(s) — '}{pageCourante.length}{' affichee(s) · '}<b>{sansSupport}</b>{' sans CD'}</>}
                     filtre={{ valeur: recherche, changer: setRecherche, indication: 'Aller a une archive…' }} />

        <VuesListe vues={vues.map((v) => ({ cle: v.cle, libelle: v.libelle, compte: filtrer(v.criteres).length }))} active={vueActive}
                   choisir={(cle) => { const v = vues.find((x) => x.cle === cle); if (v) poserCriteres(v.criteres) }}
                   enregistrer={enregistrerVue} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col style={{ width: '18%' }} /><col style={{ width: '22%' }} /><col style={{ width: '13%' }} />
              <col /><col style={{ width: '17%' }} /><col style={{ width: '10%' }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Guid</th>
                <th>Type</th>
                <th>DateArchivage</th>
                <th>Info</th>
                <th>CD</th>
                <th className="rig-lm-droite">Count</th>
              </tr>
            </thead>
            <tbody>
              {pageCourante.map((a) => {
                const estOuverte = ouverte === a.guid
                const classes = [a.cd === '' ? 'rig-table__ligne--alerte' : '', estOuverte ? 'rig-lm-ouverte' : ''].filter(Boolean).join(' ')
                return (
                  <Fragment key={a.guid}>
                    <tr className={classes}>
                      <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : a.guid)} />
                      <td className="rig-table__mono">{a.guid}</td>
                      <td title={a.type}><Pastille teinte={teinteType(a.type)}>{a.type}</Pastille></td>
                      <td className="rig-table__mono">{dateCourte(a.archivage)}</td>
                      <td title={a.info}>{a.info}</td>
                      <td>{a.cd === '' ? <PointEtat teinte="rouge">Sans CD</PointEtat> : <PointEtat teinte="vert">{a.cd}</PointEtat>}</td>
                      <td className="rig-lm-droite">{a.nombre}</td>
                    </tr>
                    {estOuverte && (
                      <LigneDetail colonnes={NbColonnes} champs={[
                        { libelle: 'Date', valeur: dateCourte(a.date) },
                        { libelle: 'Taille', valeur: a.taille },
                      ]} />
                    )}
                  </Fragment>
                )
              })}
              {pageCourante.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">Aucune archive ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{pageCourante.length}</b>{pageCourante.length > 1 ? ' archives affichees' : ' archive affichee'}</span>
          <span>Sans CD <b>{sansSupportPage}</b></span>
          <span>Documents <b>{documents}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
