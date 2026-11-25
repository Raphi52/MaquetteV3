// Ecran de la procedure PROC_ACTES_NUM.
// Criteres lus dans RIG v2 : OPE_CRITERES ("N Dossier / N Depot",
// "Abrege piece"), OPE_DATE ("Date de depot" Du/Au), OPE_RECHERCHE
// ("Type de depot", "Lister actes non numerisables").
// Colonnes : RESULTAT/DataGridviewActes.cs.
//
// Porte depuis Components/Ecrans/EcranActes.razor (lot 3) : meme balisage,
// memes classes CSS. Le filtre est fait par Pont/EcranActes.cs sur
// JeuDemo.Actes ; la coquille .razor monte cet ecran et recoit ouvrirDossier.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 2) : la colonne des resultats prend
// les briques de composants/ListeModerne.tsx. Tete (synthese, « Aller a un acte… » Ctrl K qui filtre la liste
// affichee), vues = les deux bascules existantes prereglees (« Avec anomalie seulement », « Lister actes non
// numerisables » ; sans compte : il viendrait d'un appel au pont par vue). Le tableau passe a une ligne par acte
// (Acte, Categorie en pastille, Date de depot, Demat. et Anomalies en point d'etat) ; les huit autres colonnes
// de DataGridviewActes sont dans la ligne depliee, avec « Ouvrir le dossier ». Criteres, clic qui ouvre le
// dossier, acte non numerisable inerte : inchanges.

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { dateSaisie, useNombreSaisi } from '../saisie'
import {
  CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, MenuLigne, Pastille, PiedListe, PointEtat, VuesListe,
  type ActionLigne, type Teinte,
} from '../composants/ListeModerne'

export interface OptionActes {
  code: string
  libelle: string
}

export interface LigneActe {
  rang: number
  dossier: string
  depot: string
  denomination: string
  categorieLisible: string
  abrege: string
  description: string
  depose: string
  acte: string
  cloture: string
  confidentiel: boolean
  demat: boolean
  anomalie: string
  numerisable: boolean
}

export interface VueActes {
  categories: OptionActes[]
  lignes: LigneActe[]
}

export interface ProprietesEcranActes {
  /** Remonte le numero de dossier a ouvrir. */
  ouvrirDossier: (numero: string) => void
}

/**
 * Les vues : les deux bascules de gauche, prereglees. Le type de depot et le texte saisis restent. La vue de
 * depart s'appelle « Numerises », pas « Tous » : le pont MASQUE les actes non numerisables tant que « Lister actes
 * non numerisables » n'est pas cochee (Pont/EcranActes.cs, regle v2).
 */
export const VuesActes: ReadonlyArray<{ cle: string; libelle: string; nonNumerisables: boolean; anomalies: boolean }> = [
  { cle: 'numerises', libelle: 'Numerises', nonNumerisables: false, anomalies: false },
  { cle: 'anomalies', libelle: 'Avec anomalie', nonNumerisables: false, anomalies: true },
  { cle: 'non-numerisables', libelle: 'Avec non numerisables', nonNumerisables: true, anomalies: false },
]

/** La teinte de la categorie de depot (dbo.CATEGORIE_DEPOT), par famille : actes, comptes, le reste. */
export function teinteCategorie(libelle: string): Teinte {
  const l = libelle.toLowerCase()
  if (l.includes('comptes')) return 'vert'
  if (l.includes('actes')) return 'bleu'
  if (l.includes('beneficiaire')) return 'violet'
  if (l.includes('ordonnance')) return 'ardoise'
  return 'gris'
}

/** Les colonnes du tableau a une ligne : chevron, Acte, Categorie, Date de depot, Demat., Anomalies, menu. */
const NbColonnes = 7

/**
 * « Aller a… » compare sans casse ni accents, comme la Recherche d'affaires : « procès » trouve « Proces-verbal ».
 * Le normaliser() de RechAffaires n'est pas exporte du socle (manque note au plan, lot 6) : recopie locale.
 */
function normaliser(texte: string): string {
  return texte.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '')
}

export function EcranActes({ ouvrirDossier }: ProprietesEcranActes) {
  const [critActeCritere, setCritActeCritere] = useState('')
  const [critAbrege, setCritAbrege] = useState('')
  const [critTypeDepot, setCritTypeDepot] = useState('')
  const [critDu, setCritDu] = useState('')
  const [critAu, setCritAu] = useState('')
  const [critNonNumerisables, setCritNonNumerisables] = useState(false)
  const [critAnomalies, setCritAnomalies] = useState(false)
  const topMax = useNombreSaisi(50)
  // null tant que le pont n'a pas repondu : ni ligne ni « aucun acte ».
  const [vue, setVue] = useState<VueActes | null>(null)
  // La ligne depliee par son chevron (une a la fois), par son rang.
  const [ouverte, setOuverte] = useState<number | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueActes>('EcranActes.Trouver', {
      critActeCritere, critAbrege, critTypeDepot,
      critDu: dateSaisie(critDu), critAu: dateSaisie(critAu),
      critNonNumerisables, critAnomalies, critTopMax: topMax.valeur,
    }).then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [critActeCritere, critAbrege, critTypeDepot, critDu, critAu, critNonNumerisables, critAnomalies, topMax.valeur])

  const trouves = vue?.lignes ?? []

  // « Aller a un acte… » (Ctrl K) : filtre la liste AFFICHEE, sans redemander au pont.
  const [filtreRapide, setFiltreRapide] = useState('')
  const cherche = filtreRapide.trim()
  const affiches = cherche === '' ? trouves
    : trouves.filter((a) => normaliser([a.dossier, a.depot, a.denomination, a.abrege, a.description].join(' ')).includes(normaliser(cherche)))

  // Les chiffres de la tete et du pied : sur la liste AFFICHEE.
  const sansImage = affiches.filter((a) => !a.demat).length
  const anomalies = affiches.filter((a) => a.anomalie !== '').length

  /**
   * Un acte numerise mene au dossier qui le porte. Un acte NON
   * numerisable n a pas d image a montrer : la ligne reste inerte.
   */
  function ouvrir(a: LigneActe) {
    if (!a.numerisable) return
    ouvrirDossier(a.dossier)
  }

  function effacer() {
    setCritActeCritere('')
    setCritAbrege('')
    setCritTypeDepot('')
    setCritDu('')
    setCritAu('')
    setCritNonNumerisables(false)
    setCritAnomalies(false)
    topMax.poser(50)
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Critere</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">N Dossier / N Depot</span>
          <input className="rig-saisie-champ__saisie" placeholder="0000, D-2026-1188, denomination"
                 value={critActeCritere} onChange={(e) => setCritActeCritere(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Abrege piece</span>
          <input className="rig-saisie-champ__saisie" placeholder="STAT, BILAN, PVAG"
                 value={critAbrege} onChange={(e) => setCritAbrege(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type de depot</span>
          <select className="rig-saisie-champ__saisie" value={critTypeDepot} onChange={(e) => setCritTypeDepot(e.target.value)}>
            <option value="">Tous</option>
            {(vue?.categories ?? []).map((c) => (
              <option key={c.code} value={c.code}>{c.libelle}</option>
            ))}
          </select>
          <span className="rig-saisie-champ__colonne">DPTRC_CATEGORIE_DEPOT · dbo.CATEGORIE_DEPOT</span>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date de depot</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date" value={critDu} onChange={(e) => setCritDu(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date" value={critAu} onChange={(e) => setCritAu(e.target.value)} />
            </label>
          </div>
        </div>

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${critNonNumerisables ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritNonNumerisables(!critNonNumerisables)}>
            <span className="rig-bascule__temoin"></span>
            <span>Lister actes non numerisables</span>
          </button>
          <button className={`rig-bascule${critAnomalies ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritAnomalies(!critAnomalies)}>
            <span className="rig-bascule__temoin"></span>
            <span>Avec anomalie seulement</span>
          </button>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nombre maxi. d actes affiches</span>
          <input className="rig-saisie-champ__saisie" type="number" min="1" max="500"
                 value={topMax.texte} onChange={(e) => topMax.saisir(e.target.value)} />
        </label>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <ListeModerne etiquette="Actes numerises">
        {/* Un morceau de texte par nombre, puis son libelle, comme Razor : une seule chaine decalait le lissage
            des lettres (web/README.md). */}
        <EnteteListe titre="Actes numerises"
                     synthese={vue && <>{affiches.length}{affiches.length > 1 ? ' actes · ' : ' acte · '}<b>{sansImage}</b>
                       {' sans image · '}<b>{anomalies}</b>{anomalies > 1 ? ' anomalies' : ' anomalie'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a un acte…',
                               valider: () => { if (affiches.length === 1) ouvrir(affiches[0]!) } }} />

        <VuesListe vues={VuesActes.map((v) => ({ cle: v.cle, libelle: v.libelle }))}
                   active={VuesActes.find((v) => v.nonNumerisables === critNonNumerisables && v.anomalies === critAnomalies)?.cle ?? null}
                   choisir={(cle) => {
                     const v = VuesActes.find((x) => x.cle === cle)
                     if (v) { setCritNonNumerisables(v.nonNumerisables); setCritAnomalies(v.anomalies) }
                   }} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col /><col style={{ width: '20%' }} /><col style={{ width: '12%' }} />
              <col style={{ width: '9%' }} /><col style={{ width: '18%' }} /><col style={{ width: 34 }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Acte</th>
                <th>Categorie de depot</th>
                <th>Date de depot</th>
                <th>Demat.</th>
                <th>Anomalies</th>
                <th aria-label="Actions"></th>
              </tr>
            </thead>
            <tbody>
              {affiches.map((a, i) => {
                const estOuverte = ouverte === a.rang
                const classe = [a.anomalie === '' ? '' : 'rig-table__ligne--alerte', a.numerisable ? '' : 'rig-table__ligne--inerte',
                                estOuverte ? 'rig-lm-ouverte' : ''].filter((c) => c !== '').join(' ')
                const actions: ActionLigne[] = [
                  { libelle: 'Ouvrir le dossier', fort: true, desactive: !a.numerisable, agir: () => ouvrir(a) },
                ]
                return (
                  <Fragment key={`${i}:${a.rang}`}>
                  <tr className={classe || undefined}
                      title={a.numerisable ? `Ouvrir le dossier ${a.dossier}` : `Acte non numerisable : ${a.anomalie}`}
                      onClick={() => ouvrir(a)}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : a.rang)} />
                    <CelluleIdentite nom={a.denomination}
                                     objet={<>{a.abrege}{' · '}<span>{a.dossier}</span>{' · '}<span>{a.depot}</span></>} />
                    <td title={a.categorieLisible}><Pastille teinte={teinteCategorie(a.categorieLisible)}>{a.categorieLisible}</Pastille></td>
                    <td className="rig-table__mono">{a.depose}</td>
                    <td><PointEtat teinte={a.demat ? 'vert' : 'gris'}>{a.demat ? 'oui' : 'non'}</PointEtat></td>
                    <td title={a.anomalie}>
                      {a.anomalie === '' ? <span className="rig-table__sans">aucune</span>
                        : <PointEtat teinte="rouge">{a.anomalie}</PointEtat>}
                    </td>
                    <td className="rig-lm-cellule-menu">
                      <MenuLigne libelle={`Actions de l'acte ${a.depot}`} actions={[
                        ...actions,
                        { libelle: estOuverte ? 'Replier la ligne' : 'Deplier la ligne', agir: () => setOuverte(estOuverte ? null : a.rang) },
                      ]} />
                    </td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes} actions={actions} champs={[
                      { libelle: 'N', valeur: String(a.rang) },
                      { libelle: 'N Dossier', valeur: a.dossier },
                      { libelle: 'N Depot', valeur: a.depot },
                      { libelle: 'Abrege', valeur: a.abrege },
                      { libelle: 'Description', valeur: a.description },
                      { libelle: 'Date de l acte', valeur: a.acte },
                      { libelle: 'Date de cloture', valeur: a.cloture },
                      { libelle: 'Confid.', valeur: a.confidentiel ? 'oui' : '' },
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {vue && trouves.length === 0 && (
                <tr>
                  <td colSpan={NbColonnes} className="rig-table__vide">
                    Aucun acte ne repond a ces criteres.
                  </td>
                </tr>
              )}
              {trouves.length > 0 && affiches.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">
                  {`Aucun des ${trouves.length} actes ne contient « ${cherche} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{affiches.length}</b>{affiches.length > 1 ? ' actes affiches' : ' acte affiche'}</span>
          <span>Sans image <b>{sansImage}</b></span>
          <span>Anomalies <b>{anomalies}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
