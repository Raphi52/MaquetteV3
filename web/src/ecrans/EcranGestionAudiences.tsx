// Ecran de la procedure PROC_GESTAUDS — gestion des audiences et cabinets.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_GESTAUDS) :
//   - Operations/OPE_RECHERCHE_GESTAUDS.Designer.cs : les criteres de
//     recherche — Date (OPE_DATE_AUDIENCE, puis OPE_DATE_AUDCAB1 et
//     OPE_DATE_AUDCAB2 pour l'encadrement), Type d'audience, Chambre,
//     Section, Juge, Type de cabinet, Code de cabinet, Type de Classement,
//     Nombre de resultats maximum.
//   - Operations/OPE_RESULTAT_GESTAUDS.cs : la liste de resultats. Ses
//     colonnes sont montees dans Columns.AddRange() et leurs libelles poses
//     plus bas (_jour.Text, _date.Text, ...) — d'ou les TREIZE colonnes
//     reprises ici. La colonne « Greffier » n'existe QUE lorsque la
//     recherche porte sur les cabinets (test ParamTypeRecherche ==
//     EnumTypeRecherche.Cabinet) : la bascule « Cabinets » ci-dessous
//     reproduit cette condition, elle n'est pas un confort d'affichage.
//     De meme, « jugeOuSection » est une seule colonne qui change de titre
//     selon le mode — d'ou l'en-tete calcule.
//
// Porte depuis Components/Ecrans/EcranGestionAudiences.razor (lot 5) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// audiences de demonstration et ses filtres sont ici, sans pont.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 2) : la liste prend les briques de
// composants/ListeModerne.tsx. Tete (synthese, « Aller a une audience… » Ctrl K qui filtre la liste affichee),
// vues = le critere « Type d'audience » preregle, une ligne par audience (Jour, Date, Heure, Type en pastille,
// Juge ou Section, Type de classement en point d'etat). Les sept autres colonnes de la v2 (Code de cabinet,
// Greffier, Public, suppression, Web) sont dans la ligne depliee ; Greffier n'y figure qu'en recherche par
// cabinets, comme la colonne v2. Ni menu ni action de ligne : la liste n'en avait pas. Criteres inchanges.
//
// LA BASE DU GREFFE (plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026, marche du lot 17) : les lignes viennent du pont
// (Pont/EcranGestionAudiencesBase.cs → Serveur/Socle/EcransMain/LectureGestionAudiences.cs) : les audiences et cabinets
// SUPPRIMES (AUDIENCE_CABINET_SUPPRIMEE), comme PROC_GESTAUDS ; la bascule « Recherche par cabinets » choisit le mode de RIG v2
// (AUDNCS_AUDIENCE). Sans serveur, les cinq audiences de demonstration du pont (sans mode : la bascule ne les filtre pas),
// marquees « exemple » (ProvenanceEcran) ; en panne, le motif et aucune ligne. Sans serveur, les criteres filtrent la demonstration a l'ecran.
//
// EN BASE, LA RECHERCHE DE RIG v2 EST FAITE PAR LE SERVEUR (plan « RIG v3 complet, mesure par le banc du parcours complet », lot 9,
// 28/11/2026) : « Rechercher » envoie les criteres (codes de la chambre, du code cabinet et des classements, numero du juge, dates
// d'audience ET de suppression, mode, nombre maximum) au pont (EcranGestionAudiences.Rechercher), et le serveur les applique comme
// OPE_RECHERCHE_GESTAUDS._Rechercher (Serveur/Socle/EcransMain/LectureGestionAudiences.cs, RechercherAsync) ; sans date de debut,
// RIG v2 ne cherche rien et l'ecran le dit. A l'ouverture, la liste montre les suppressions du mode choisi (au plus 500). Aucun
// geste d'ecriture : PROC_GESTAUDS n'en a pas dans RIG v2 (FORM_GESTAUDS.cs:17-18, OPE_RESULTAT_GESTAUDS.cs:167-168).

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { distinctsTries, entierLisible } from '../criteres'
import {
  CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe, normaliser,
  type Teinte,
} from '../composants/ListeModerne'

interface Audience {
  jour: string
  /** jj/mm/aaaa, tel que la grille v2 l'affiche */
  date: string
  heure: string
  type: string
  codeCabinet: string
  typeCabinet: string
  juge: string
  section: string
  chambre: string
  greffier: string
  typeClassement: string
  public: boolean
  auteurSuppression: string
  dateSuppression: string
  envoiSuppression: string
  /** Le mode de RIG v2 (cabinet ou audience) d'une ligne lue en base ; absent dans la demonstration. */
  cabinet?: boolean | null
  /** Lus en base : les codes que la recherche du serveur recoit (chambre, code cabinet, numero du juge). */
  codeChambre?: string
  codeCab?: string
  idJuge?: number | null
}

/** Ce que la recherche du serveur recoit (Contrats/EcransMain/LectureGestionAudiences.cs, RechercheAudiencesSupprimees). */
interface RechercheAudiencesSupprimees {
  cabinets: boolean
  audienceDu: string | null
  audienceAu: string | null
  suppressionDu: string | null
  suppressionAu: string | null
  type: string | null
  chambre: string | null
  section: string | null
  juge: number | null
  codeCabinet: string | null
  typeClassement: string | null
  max: number
}

/** Une valeur proposee par un critere : celle que la recherche recoit (le code en base) et son libelle. */
interface Choix { valeur: string; libelle: string }

/** Les valeurs distinctes d'un critere, triees par libelle ; sans code (demonstration), la valeur est le libelle. */
function choixDe(paires: Array<[valeur: string, libelle: string]>): Choix[] {
  const vus = new Map<string, string>()
  for (const [v, l] of paires) if (v !== '' && !vus.has(v)) vus.set(v, l)
  return distinctsTries(vus.values()).flatMap((l) => [...vus].filter(([, x]) => x === l).map(([v]) => ({ valeur: v, libelle: l })))
}

/** Ce que le pont rend (Pont/EcranGestionAudiences.cs, VueGestionAudiences). */
interface VueGestionAudiences {
  lignes: Audience[]
  mode?: ModeLecture
  message?: string | null
  total: number
}

const vide = (valeur: string): string => (valeur === '' ? '—' : valeur)

/** « jj/mm/aaaa » -> « aaaa-mm-jj », comparable a la valeur d'un <input type="date">. */
const jourIso = (texte: string): string => {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(texte)
  return m ? `${m[3]}-${m[2]}-${m[1]}` : '0001-01-01'
}

/** Les criteres de recherche, tels que saisis (la borne maximum, derniere valeur lisible). */
interface CriteresGestion {
  dateDebut: string
  dateFin: string
  critType: string
  critChambre: string
  critSection: string
  critJuge: string
  critTypeCabinet: string
  critCodeCabinet: string
  critClassement: string
  maxResultats: number
}

/** Le filtre de OPE_RECHERCHE_GESTAUDS, borne au nombre maximum : la liste, et le compte de chaque vue. Le mode (cabinet ou
 *  audience, AUDNCS_AUDIENCE) ne filtre que les lignes lues en base, qui le portent. */
function filtrer(source: Audience[], modeCabinet: boolean, c: CriteresGestion): Audience[] {
  let q = source.filter((x) => x.cabinet === undefined || x.cabinet === null || x.cabinet === modeCabinet)
  if (c.critType !== '') q = q.filter((x) => x.type === c.critType)
  if (c.critChambre !== '') q = q.filter((x) => x.chambre === c.critChambre)
  if (c.critSection !== '') q = q.filter((x) => x.section === c.critSection)
  if (c.critJuge !== '') q = q.filter((x) => x.juge === c.critJuge)
  if (c.critTypeCabinet !== '') q = q.filter((x) => x.typeCabinet === c.critTypeCabinet)
  if (c.critCodeCabinet !== '') q = q.filter((x) => x.codeCabinet === c.critCodeCabinet)
  if (c.critClassement !== '') q = q.filter((x) => x.typeClassement === c.critClassement)
  if (c.dateDebut !== '') q = q.filter((x) => jourIso(x.date) >= c.dateDebut)
  if (c.dateFin !== '') q = q.filter((x) => jourIso(x.date) <= c.dateFin)
  return q.slice(0, Math.max(1, c.maxResultats))
}

/** Les vues : « Toutes », puis chaque type d'audience des lignes (le critere « Type d'audience » preregle). */
const vuesDe = (source: Audience[]): ReadonlyArray<{ cle: string; libelle: string; critType: string }> => [
  { cle: 'toutes', libelle: 'Toutes', critType: '' },
  ...distinctsTries(source.map((x) => x.type)).map((t) => ({ cle: t, libelle: t, critType: t })),
]

const TeinteType: Record<string, Teinte> = { 'Audience de procedure': 'orange', 'Audience publique': 'bleu', 'Cabinet du juge': 'violet' }
const TeinteClassement: Record<string, Teinte> = { Renvoi: 'orange', 'Mise en delibere': 'bleu', Radiation: 'rouge', Clos: 'gris' }

/** Les colonnes du tableau a une ligne : chevron, Jour, Date, Heure, Type, Juge ou Section, Type de classement. */
const NbColonnes = 7

export function EcranGestionAudiences() {
  const [modeCabinet, setModeCabinet] = useState(true)
  const [dateDebut, setDateDebut] = useState('')
  const [dateFin, setDateFin] = useState('')
  const [critType, setCritType] = useState('')
  const [critChambre, setCritChambre] = useState('')
  const [critSection, setCritSection] = useState('')
  const [critJuge, setCritJuge] = useState('')
  const [critTypeCabinet, setCritTypeCabinet] = useState('')
  const [critCodeCabinet, setCritCodeCabinet] = useState('')
  const [critClassement, setCritClassement] = useState('')
  // Le texte saisi et la derniere valeur LISIBLE (le @bind d'un int).
  const [maxTexte, setMaxTexte] = useState('100')
  const [maxResultats, setMaxResultats] = useState(100)

  function saisirMax(texte: string) {
    setMaxTexte(texte)
    const n = entierLisible(texte)
    if (n !== null) setMaxResultats(n)
  }

  // null tant que le pont n'a pas repondu : aucune ligne, jamais la demonstration a la place de la base.
  const [vue, setVue] = useState<VueGestionAudiences | null>(null)
  useEffect(() => {
    let perime = false
    void appeler<VueGestionAudiences>('EcranGestionAudiences.Vue').then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [])
  const Audiences = vue?.lignes ?? []
  const VuesAudiences = vuesDe(Audiences)
  const enBase = vue?.mode === 'serveur'
  // Les dates de suppression (OPE_DATE_AUDCAB2 de RIG v2) ; le resultat de la derniere recherche du serveur (null : aucune).
  const [suppressionDu, setSuppressionDu] = useState('')
  const [suppressionAu, setSuppressionAu] = useState('')
  const [resultat, setResultat] = useState<VueGestionAudiences | null>(null)
  const [enRecherche, setEnRecherche] = useState(false)

  const criteres: CriteresGestion = {
    dateDebut, dateFin, critType, critChambre, critSection, critJuge, critTypeCabinet, critCodeCabinet, critClassement, maxResultats,
  }
  const filtrerIci = (c: CriteresGestion) => filtrer(Audiences, modeCabinet, c)
  // En base : le resultat du serveur, sinon les suppressions du mode choisi ; sans serveur : la demonstration filtree a l'ecran.
  const trouvees = !enBase ? filtrerIci(criteres) : resultat !== null ? resultat.lignes : Audiences.filter((x) => x.cabinet === modeCabinet)

  // Les criteres en base : leurs CODES (la chambre, le code cabinet, le juge par son numero, chaque classement admis).
  const parMode = enBase ? Audiences.filter((x) => x.cabinet === modeCabinet) : Audiences
  const choixChambres = choixDe(parMode.map((x) => [enBase ? x.codeChambre ?? '' : x.chambre, x.chambre]))
  const choixJuges = choixDe(parMode.map((x) => [enBase ? (x.idJuge == null ? '' : String(x.idJuge)) : x.juge, x.juge]))
  const choixCodes = choixDe(parMode.map((x) => [enBase ? x.codeCab ?? '' : x.codeCabinet, x.codeCabinet]))
  const choixClassements = enBase
    ? choixDe(parMode.flatMap((x) => x.typeClassement.split(';').map((c): [string, string] => [c.trim(), c.trim()])))
    : choixDe(Audiences.map((x) => [x.typeClassement, x.typeClassement]))

  /** « Rechercher » : les criteres de RIG v2, appliques par le serveur. */
  async function rechercher(type = modeCabinet ? critTypeCabinet : critType) {
    const vide = (t: string) => (t.trim() === '' ? null : t.trim())
    const r: RechercheAudiencesSupprimees = {
      cabinets: modeCabinet, audienceDu: vide(dateDebut), audienceAu: vide(dateFin), suppressionDu: vide(suppressionDu), suppressionAu: vide(suppressionAu),
      type: vide(type), chambre: modeCabinet ? null : vide(critChambre), section: modeCabinet ? null : vide(critSection),
      juge: modeCabinet && critJuge !== '' ? Number(critJuge) : null, codeCabinet: modeCabinet ? vide(critCodeCabinet) : null,
      typeClassement: vide(critClassement), max: maxResultats,
    }
    setEnRecherche(true)
    try {
      setResultat(await appeler<VueGestionAudiences>('EcranGestionAudiences.Rechercher', r))
    } finally {
      setEnRecherche(false)
    }
  }

  // La ligne depliee par son chevron (une a la fois) : l'audience elle-meme.
  const [ouverte, setOuverte] = useState<Audience | null>(null)

  // « Aller a une audience… » (Ctrl K) : filtre la liste AFFICHEE, sans toucher aux criteres.
  const [filtreRapide, setFiltreRapide] = useState('')
  const cherche = filtreRapide.trim()
  const affichees = cherche === '' ? trouvees : trouvees.filter((x) => normaliser(
    [x.jour, x.date, x.heure, x.type, x.codeCabinet, x.juge, x.section, x.greffier, x.typeClassement].join(' ')).includes(normaliser(cherche)))

  // Les chiffres de la tete et du pied : sur la liste AFFICHEE.
  const publiques = affichees.filter((x) => x.public).length
  const supprimees = affichees.filter((x) => x.auteurSuppression !== '').length

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Recherche d'une audience</div>

        <button className={`rig-bascule${modeCabinet ? ' rig-bascule--actif' : ''}`}
                onClick={() => { setModeCabinet(!modeCabinet); setResultat(null) }}>
          <span className="rig-bascule__temoin"></span>
          <span>Recherche par cabinets</span>
        </button>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Du</span>
            <input className="rig-saisie-champ__saisie" type="date" value={dateDebut}
                   onChange={(e) => setDateDebut(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Au</span>
            <input className="rig-saisie-champ__saisie" type="date" value={dateFin}
                   onChange={(e) => setDateFin(e.target.value)} />
          </label>
        </div>

        {enBase && (
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Supprimee du</span>
              <input className="rig-saisie-champ__saisie" type="date" value={suppressionDu} onChange={(e) => setSuppressionDu(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Supprimee au</span>
              <input className="rig-saisie-champ__saisie" type="date" value={suppressionAu} onChange={(e) => setSuppressionAu(e.target.value)} />
            </label>
          </div>
        )}

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type d'audience</span>
          <select className="rig-saisie-champ__saisie" value={critType} onChange={(e) => setCritType(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(Audiences.map((x) => x.type)).map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Chambre</span>
            <select className="rig-saisie-champ__saisie" value={critChambre} onChange={(e) => setCritChambre(e.target.value)}>
              <option value="">Toutes</option>
              {choixChambres.map((c) => <option key={c.valeur} value={c.valeur}>{c.libelle}</option>)}
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Section</span>
            <select className="rig-saisie-champ__saisie" value={critSection} onChange={(e) => setCritSection(e.target.value)}>
              <option value="">Toutes</option>
              {distinctsTries(Audiences.map((x) => x.section)).map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </label>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Juge</span>
          <select className="rig-saisie-champ__saisie" value={critJuge} onChange={(e) => setCritJuge(e.target.value)}>
            <option value="">Tous</option>
            {choixJuges.map((j) => <option key={j.valeur} value={j.valeur}>{j.libelle}</option>)}
          </select>
        </label>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Type de cabinet</span>
            <select className="rig-saisie-champ__saisie" value={critTypeCabinet} onChange={(e) => setCritTypeCabinet(e.target.value)}>
              <option value="">Tous</option>
              {distinctsTries(Audiences.map((x) => x.typeCabinet).filter((t) => t.length > 0))
                .map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Code de cabinet</span>
            <select className="rig-saisie-champ__saisie" value={critCodeCabinet} onChange={(e) => setCritCodeCabinet(e.target.value)}>
              <option value="">Tous</option>
              {choixCodes.map((c) => <option key={c.valeur} value={c.valeur}>{c.libelle}</option>)}
            </select>
          </label>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type de classement</span>
          <select className="rig-saisie-champ__saisie" value={critClassement} onChange={(e) => setCritClassement(e.target.value)}>
            <option value="">Tous</option>
            {choixClassements.map((c) => <option key={c.valeur} value={c.valeur}>{c.libelle}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nombre de resultats maximum</span>
          <input className="rig-saisie-champ__saisie" type="number" min="1" max="999" value={maxTexte}
                 onChange={(e) => saisirMax(e.target.value)} />
        </label>

        {enBase && (
          <div className="rig-criteres__actions">
            <button className="rig-bouton rig-bouton--fort" onClick={() => void rechercher()} disabled={enRecherche}>Rechercher</button>
          </div>
        )}
        {resultat?.message && <p className="rig-eg-annonce" role="status">{resultat.message}</p>}
      </aside>

      <ListeModerne etiquette="Audiences trouvees">
        <EnteteListe titre="Audiences trouvees"
                     synthese={<>{affichees.length}{' sur '}{maxResultats}{' maximum · '}<b>{publiques}</b>
                       {publiques > 1 ? ' publiques · ' : ' publique · '}<b>{supprimees}</b>{supprimees > 1 ? ' supprimees' : ' supprimee'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une audience…' }} />

        {vue && <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Audiences" />}

        {/* Les vues : le critere « Type d'audience » preregle ; en base, choisir une vue relance la recherche du serveur. */}
        <VuesListe vues={VuesAudiences.map((v) => ({ cle: v.cle, libelle: v.libelle,
                     compte: enBase ? trouvees.filter((x) => v.critType === '' || x.type === v.critType).length
                                    : filtrerIci({ ...criteres, critType: v.critType }).length }))}
                   active={VuesAudiences.find((v) => v.critType === critType)?.cle ?? null}
                   choisir={(cle) => {
                     const v = VuesAudiences.find((x) => x.cle === cle)
                     if (!v) return
                     setCritType(v.critType)
                     if (enBase && resultat !== null) void rechercher(v.critType)
                   }} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col style={{ width: '10%' }} /><col style={{ width: '12%' }} /><col style={{ width: '8%' }} />
              <col /><col style={{ width: '20%' }} /><col style={{ width: '18%' }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Jour</th>
                <th>Date</th>
                <th>Heure</th>
                <th>Type</th>
                {/* Une seule colonne en v2, dont le titre change avec le mode. */}
                <th>{modeCabinet ? 'Juge' : 'Section'}</th>
                <th>Type de classement</th>
              </tr>
            </thead>
            <tbody>
              {affichees.map((x, i) => {
                const estOuverte = ouverte === x
                return (
                  <Fragment key={i}>
                  <tr className={estOuverte ? 'rig-lm-ouverte' : undefined}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : x)} />
                    <td>{x.jour}</td>
                    <td className="rig-table__mono">{x.date}</td>
                    <td className="rig-table__mono">{x.heure}</td>
                    <td title={x.type}><Pastille teinte={TeinteType[x.type] ?? 'gris'}>{x.type}</Pastille></td>
                    {modeCabinet ? <CelluleIdentite nom={x.juge} titre={x.juge} /> : <td>{x.section}</td>}
                    <td title={x.typeClassement}>
                      <PointEtat teinte={TeinteClassement[x.typeClassement] ?? 'gris'}>{x.typeClassement}</PointEtat>
                    </td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes} champs={[
                      { libelle: 'Code de cabinet', valeur: vide(x.codeCabinet) },
                      // La colonne « Greffier » n'existe en v2 qu'en recherche par cabinets.
                      ...(modeCabinet ? [{ libelle: 'Greffier', valeur: vide(x.greffier) }] : []),
                      { libelle: 'Public', valeur: x.public ? 'Oui' : 'Non' },
                      { libelle: 'Auteur suppression', valeur: vide(x.auteurSuppression) },
                      { libelle: 'Date suppression', valeur: vide(x.dateSuppression) },
                      { libelle: 'Envoi suppression Infogreffe', valeur: vide(x.envoiSuppression) },
                      { libelle: 'Web (non dispo.)', valeur: <span className="rig-table__sans">non dispo.</span> },
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {trouvees.length === 0 && (
                <tr className="rig-table__ligne--inerte">
                  <td colSpan={NbColonnes} className="rig-table__vide">Aucune audience ne repond a ces criteres.</td>
                </tr>
              )}
              {trouvees.length > 0 && affichees.length === 0 && (
                <tr className="rig-table__ligne--inerte"><td colSpan={NbColonnes} className="rig-table__vide">
                  {`Aucune des ${trouvees.length} audiences ne contient « ${cherche} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' audiences affichees' : ' audience affichee'}</span>
          <span>Publiques <b>{publiques}</b></span>
          <span>Supprimees <b>{supprimees}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
