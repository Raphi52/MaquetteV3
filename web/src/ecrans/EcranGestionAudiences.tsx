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

import { Fragment, useState } from 'react'
import { distinctsTries, entierLisible } from '../criteres'
import {
  CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe,
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
}

const a = (jour: string, date: string, heure: string, type: string, codeCabinet: string, typeCabinet: string,
           juge: string, section: string, chambre: string, greffier: string, typeClassement: string,
           pub: boolean, auteurSuppression: string, dateSuppression: string, envoiSuppression: string): Audience =>
  ({ jour, date, heure, type, codeCabinet, typeCabinet, juge, section, chambre, greffier, typeClassement,
     public: pub, auteurSuppression, dateSuppression, envoiSuppression })

const Audiences: Audience[] = [
  a('Lundi',    '14/09/2026', '09h00', 'Audience de procedure', 'CAB-01', 'Cabinet du president',  'Mme LEROY', '1re section', '1re chambre', 'M. PETIT',  'Renvoi',           true,  '',        '',           ''),
  a('Lundi',    '14/09/2026', '10h30', 'Audience publique',     'CAB-01', 'Cabinet du president',  'Mme LEROY', '1re section', '1re chambre', 'M. PETIT',  'Mise en delibere', true,  '',        '',           ''),
  a('Mardi',    '15/09/2026', '14h00', 'Cabinet du juge',       'CAB-03', 'Cabinet du rapporteur', 'M. DUVAL',  '2e section',  '2e chambre',  'Mme FAURE', 'Renvoi',           false, '',        '',           ''),
  a('Jeudi',    '17/09/2026', '09h30', 'Audience publique',     '',       '',                      'M. DUVAL',  '2e section',  '2e chambre',  '',          'Radiation',        true,  'rvilain', '02/09/2026', 'Envoye'),
  a('Vendredi', '18/09/2026', '11h00', 'Audience de procedure', 'CAB-02', 'Cabinet du president',  'Mme LEROY', '3e section',  '1re chambre', 'M. PETIT',  'Clos',             true,  '',        '',           ''),
]

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

/** Le filtre de OPE_RECHERCHE_GESTAUDS, borne au nombre maximum : la liste, et le compte de chaque vue. */
function filtrer(c: CriteresGestion): Audience[] {
  let q = Audiences
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

/** Les vues : « Toutes », puis chaque type d'audience (le critere « Type d'audience » preregle). */
const Types = distinctsTries(Audiences.map((x) => x.type))
export const VuesAudiences: ReadonlyArray<{ cle: string; libelle: string; critType: string }> = [
  { cle: 'toutes', libelle: 'Toutes', critType: '' },
  ...Types.map((t) => ({ cle: t, libelle: t, critType: t })),
]

const TeinteType: Record<string, Teinte> = { 'Audience de procedure': 'orange', 'Audience publique': 'bleu', 'Cabinet du juge': 'violet' }
const TeinteClassement: Record<string, Teinte> = { Renvoi: 'orange', 'Mise en delibere': 'bleu', Radiation: 'rouge', Clos: 'gris' }

/** Les colonnes du tableau a une ligne : chevron, Jour, Date, Heure, Type, Juge ou Section, Type de classement. */
const NbColonnes = 7

/**
 * « Aller a… » compare sans casse ni accents, comme la Recherche d'affaires : « procédure » trouve « Audience de procedure ».
 * Le normaliser() de RechAffaires n'est pas exporte du socle (manque note au plan, lot 6) : recopie locale.
 */
function normaliser(texte: string): string {
  return texte.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '')
}

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

  const criteres: CriteresGestion = {
    dateDebut, dateFin, critType, critChambre, critSection, critJuge, critTypeCabinet, critCodeCabinet, critClassement, maxResultats,
  }
  const trouvees = filtrer(criteres)

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
                onClick={() => setModeCabinet(!modeCabinet)}>
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
              {distinctsTries(Audiences.map((x) => x.chambre)).map((c) => <option key={c} value={c}>{c}</option>)}
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
            {distinctsTries(Audiences.map((x) => x.juge)).map((j) => <option key={j} value={j}>{j}</option>)}
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
              {distinctsTries(Audiences.map((x) => x.codeCabinet).filter((c) => c.length > 0))
                .map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
          </label>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type de classement</span>
          <select className="rig-saisie-champ__saisie" value={critClassement} onChange={(e) => setCritClassement(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(Audiences.map((x) => x.typeClassement)).map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nombre de resultats maximum</span>
          <input className="rig-saisie-champ__saisie" type="number" min="1" max="999" value={maxTexte}
                 onChange={(e) => saisirMax(e.target.value)} />
        </label>
      </aside>

      <ListeModerne etiquette="Audiences trouvees">
        <EnteteListe titre="Audiences trouvees"
                     synthese={<>{affichees.length}{' sur '}{maxResultats}{' maximum · '}<b>{publiques}</b>
                       {publiques > 1 ? ' publiques · ' : ' publique · '}<b>{supprimees}</b>{supprimees > 1 ? ' supprimees' : ' supprimee'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une audience…' }} />

        <VuesListe vues={VuesAudiences.map((v) => ({ cle: v.cle, libelle: v.libelle, compte: filtrer({ ...criteres, critType: v.critType }).length }))}
                   active={VuesAudiences.find((v) => v.critType === critType)?.cle ?? null}
                   choisir={(cle) => { const v = VuesAudiences.find((x) => x.cle === cle); if (v) setCritType(v.critType) }} />

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
