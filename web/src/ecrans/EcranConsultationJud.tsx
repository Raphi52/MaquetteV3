// Ecran de la procedure PROC_CJUD (RIG v2 : consultation judiciaire,
// « Enrolement de toute affaire » + bloc CJUDINFOG). Les champs sont les
// controles v2 : ULT_COMBO_ETAT_DEMAT, ult_ComboBoxEtatTraitement,
// ult_ComboBoxTypeRejet, ult_DateCreation, ult_DateTraitement,
// ult_LabelNbPiece, ult_TextMultiLineAnomalies, ult_TextMultiLineMotifRejet,
// et les boutons ult_ButtonVisuPiece / ult_ButtonvisuXML.
//
// Porte depuis Components/Ecrans/EcranConsultationJud.razor (lot 5) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// enrolements de demonstration et ses filtres sont ici, sans pont. La
// coquille .razor monte cet ecran et recoit ouvrirDossier.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 2) : la colonne des resultats prend
// les briques de composants/ListeModerne.tsx. Tete (synthese, « Aller a une affaire… » Ctrl K, Entree choisit
// l'affaire restee seule), trois cartes qui POSENT les criteres d'etat existants (traitement, « Rejets
// uniquement », dematerialisation), des vues = jeux de ces criteres d'etat. Le tableau passe a une ligne par
// affaire (Affaire, Etat demat., Etat traitement, Date creation, Pieces) ; Type de rejet, Date traitement,
// dossier et anomalies sont dans la ligne depliee, avec « Visualiser la piece » et « le XML ».
// « Aller a une affaire… » EST l'ancien critere « Affaire / dossier » (meme etat, memes colonnes cherchees :
// numero d'affaire, denomination) : la liste est filtree ici, sans pont, deux champs auraient fait le meme
// filtre (decision du plan, lot 7). Les autres criteres, le clic qui montre le detail, ses boutons et le XML
// ne changent pas.

import { Fragment, useState } from 'react'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'
import {
  CartesChiffrees, CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, MenuLigne, Pastille, PiedListe,
  PointEtat, VuesListe, type ActionLigne, type Teinte,
} from '../composants/ListeModerne'
import type { ContexteDossierVue } from '../plan/types'

interface DossierJud {
  affaire: string
  denomination: string
  etatDemat: string
  etatTraitement: string
  typeRejet: string
  /** aaaa-mm-jj */
  creation: string
  /** aaaa-mm-jj, null tant que l'affaire n'est pas traitee */
  traitement: string | null
  nbPieces: number
  anomalies: string
  motifRejet: string
  xml: string
  dossier: string
}

const Dossiers: DossierJud[] = [
  { affaire: '2026J00121', denomination: 'SARL BOULANGERIE DUVAL', etatDemat: 'Dematerialise', etatTraitement: 'Traite', typeRejet: '', creation: '2026-03-04', traitement: '2026-03-06', nbPieces: 12, anomalies: '', motifRejet: '', xml: '<enrolement num="2026J00121" />', dossier: '2024B00318' },
  { affaire: '2026J00134', denomination: 'SAS TRANSPORTS MARNE', etatDemat: 'Partiel', etatTraitement: 'En attente', typeRejet: '', creation: '2026-05-19', traitement: null, nbPieces: 5, anomalies: 'Piece 3 illisible', motifRejet: '', xml: '<enrolement num="2026J00134" />', dossier: '2024B00407' },
  { affaire: '2026J00140', denomination: 'EURL COIFFURE MARIE', etatDemat: 'Dematerialise', etatTraitement: 'Rejete', typeRejet: 'Piece manquante', creation: '2026-06-02', traitement: '2026-06-03', nbPieces: 3, anomalies: 'Acte de saisine absent du depot', motifRejet: 'Dossier incomplet', xml: '<enrolement num="2026J00140" />', dossier: '2023B01120' },
  { affaire: '2026J00155', denomination: 'SCI DU MOULIN', etatDemat: 'Non demat.', etatTraitement: 'En attente', typeRejet: '', creation: '2026-07-21', traitement: null, nbPieces: 0, anomalies: 'Aucun flux recu', motifRejet: '', xml: '', dossier: '2025B00044' },
]

export interface ProprietesEcranConsultationJud {
  /** Remonte le NUMERO de dossier de la piece : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

/** Les criteres de gauche, tels que saisis. */
interface CriteresConsultation {
  critTexte: string
  critDemat: string
  critTraitement: string
  critCreationDu: string
  critTraitementAu: string
  rejetsSeuls: boolean
}

/** Le filtre des criteres de gauche : la liste, et le compte de chaque vue. */
function filtrer(c: CriteresConsultation): DossierJud[] {
  let q = Dossiers
  if (!estBlanc(c.critTexte)) {
    const t = c.critTexte.trim()
    q = q.filter((d) => contientSansCasse(d.affaire, t) || contientSansCasse(d.denomination, t))
  }
  if (c.critDemat !== '') q = q.filter((d) => d.etatDemat === c.critDemat)
  if (c.critTraitement !== '') q = q.filter((d) => d.etatTraitement === c.critTraitement)
  if (c.critCreationDu !== '') q = q.filter((d) => d.creation >= c.critCreationDu)
  if (c.critTraitementAu !== '') q = q.filter((d) => d.traitement !== null && d.traitement <= c.critTraitementAu)
  if (c.rejetsSeuls) q = q.filter((d) => d.etatTraitement === 'Rejete')
  return q
}

/** Une vue pose les criteres d'ETAT de l'ecran ; le texte et les dates saisis restent. */
type CriteresEtat = Pick<CriteresConsultation, 'critDemat' | 'critTraitement' | 'rejetsSeuls'>
const SansEtat: CriteresEtat = { critDemat: '', critTraitement: '', rejetsSeuls: false }

export const VuesEnrolements: ReadonlyArray<{ cle: string; libelle: string; criteres: CriteresEtat }> = [
  { cle: 'toutes', libelle: 'Toutes', criteres: SansEtat },
  { cle: 'en-attente', libelle: 'En attente', criteres: { ...SansEtat, critTraitement: 'En attente' } },
  { cle: 'rejets', libelle: 'Rejets', criteres: { ...SansEtat, rejetsSeuls: true } },
  { cle: 'non-demat', libelle: 'Non dematerialisees', criteres: { ...SansEtat, critDemat: 'Non demat.' } },
]

const TeinteDemat: Record<string, Teinte> = { Dematerialise: 'bleu', Partiel: 'orange', 'Non demat.': 'gris' }
const TeinteTraitement: Record<string, Teinte> = { Traite: 'vert', 'En attente': 'orange', Rejete: 'rouge' }

/** Les colonnes du tableau a une ligne : chevron, Affaire, Etat demat., Etat traitement, Date creation, Pieces, menu. */
const NbColonnes = 7

export function EcranConsultationJud({ ouvrirDossier, dossier = null }: ProprietesEcranConsultationJud) {
  // « Aller a une affaire… » de la tete (l'ancien critere « Affaire / dossier ») : ouvert depuis une affaire,
  // elle y est cherchee par son numero.
  const [critTexte, setCritTexte] = useState(dossier?.numero ?? '')
  const [critDemat, setCritDemat] = useState('')
  const [critTraitement, setCritTraitement] = useState('')
  const [critCreationDu, setCritCreationDu] = useState('')
  const [critTraitementAu, setCritTraitementAu] = useState('')
  const [rejetsSeuls, setRejetsSeuls] = useState(false)
  const [xmlOuvert, setXmlOuvert] = useState(false)
  const [selection, setSelection] = useState<DossierJud | null>(null)
  // La ligne depliee par son chevron (une a la fois).
  const [ouverte, setOuverte] = useState<string | null>(null)

  const criteres: CriteresConsultation = { critTexte, critDemat, critTraitement, critCreationDu, critTraitementAu, rejetsSeuls }
  const trouves = filtrer(criteres)

  function poserEtat(c: CriteresEtat) {
    setCritDemat(c.critDemat)
    setCritTraitement(c.critTraitement)
    setRejetsSeuls(c.rejetsSeuls)
  }
  const vueActive = VuesEnrolements.find((v) => v.criteres.critDemat === critDemat
    && v.criteres.critTraitement === critTraitement && v.criteres.rejetsSeuls === rejetsSeuls)?.cle ?? null

  // Les chiffres de la tete, des cartes et du pied : sur la liste affichee.
  const parTraitement = (e: string) => trouves.filter((d) => d.etatTraitement === e).length
  const parDemat = (e: string) => trouves.filter((d) => d.etatDemat === e).length
  const enAttente = parTraitement('En attente')
  const rejets = parTraitement('Rejete')
  const pieces = trouves.reduce((n, d) => n + d.nbPieces, 0)
  const basculer = (actuel: string, valeur: string, poser: (v: string) => void) => () => poser(actuel === valeur ? '' : valeur)

  /** « Visualiser le XML » d'une ligne : la choisit et montre son XML dans le detail. */
  function voirXml(dos: DossierJud) {
    setSelection(dos)
    setXmlOuvert(true)
  }

  const s = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de consultation</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Etat de dematerialisation</span>
          <select className="rig-saisie-champ__saisie" value={critDemat} onChange={(e) => setCritDemat(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(Dossiers.map((d) => d.etatDemat)).map((e) => <option key={e} value={e}>{e}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Etat de traitement</span>
          <select className="rig-saisie-champ__saisie" value={critTraitement} onChange={(e) => setCritTraitement(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(Dossiers.map((d) => d.etatTraitement)).map((e) => <option key={e} value={e}>{e}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Dates</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Creation a partir du</span>
            <input className="rig-saisie-champ__saisie" type="date" value={critCreationDu}
                   onChange={(e) => setCritCreationDu(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Traitement jusqu'au</span>
            <input className="rig-saisie-champ__saisie" type="date" value={critTraitementAu}
                   onChange={(e) => setCritTraitementAu(e.target.value)} />
          </label>
        </div>

        <button className={`rig-bascule${rejetsSeuls ? ' rig-bascule--actif' : ''}`}
                onClick={() => setRejetsSeuls(!rejetsSeuls)}>
          <span className="rig-bascule__temoin"></span>
          <span>Rejets uniquement</span>
        </button>
      </aside>

      <ListeModerne etiquette="Enrolements">
        <EnteteListe titre="Enrolements"
                     synthese={<>{trouves.length}{trouves.length > 1 ? ' affaires · ' : ' affaire · '}<b>{enAttente}</b>
                       {' en attente · '}<b>{rejets}</b>{rejets > 1 ? ' rejets' : ' rejet'}</>}
                     filtre={{ valeur: critTexte, changer: setCritTexte, indication: 'Aller a une affaire…',
                               valider: () => { if (trouves.length === 1) setSelection(trouves[0]!) } }} />

        <CartesChiffrees cartes={[
          { cle: 'traitement', titre: 'Etat de traitement', valeur: trouves.length,
            complement: trouves.length > 1 ? 'affaires' : 'affaire', legende: true,
            ...(critTraitement !== '' ? { choisir: () => setCritTraitement(''), indication: 'Tous les etats de traitement' } : {}),
            segments: distinctsTries(Dossiers.map((d) => d.etatTraitement)).map((e) => ({
              cle: e, libelle: e, valeur: parTraitement(e), texte: String(parTraitement(e)), teinte: TeinteTraitement[e] ?? 'gris',
              actif: critTraitement === e, choisir: basculer(critTraitement, e, setCritTraitement),
            })) },
          { cle: 'rejets', titre: 'Rejets', valeur: rejets, complement: `sur ${trouves.length}`,
            anneau: { part: rejets, total: trouves.length, teinte: 'rouge' },
            actif: rejetsSeuls, choisir: () => setRejetsSeuls(!rejetsSeuls),
            indication: rejetsSeuls ? 'Toutes les affaires' : 'Seulement les rejets' },
          { cle: 'demat', titre: 'Dematerialisation', valeur: parDemat('Dematerialise'), complement: `sur ${trouves.length}`,
            legende: true,
            ...(critDemat !== '' ? { choisir: () => setCritDemat(''), indication: 'Tous les etats de dematerialisation' } : {}),
            segments: distinctsTries(Dossiers.map((d) => d.etatDemat)).map((e) => ({
              cle: e, libelle: e, valeur: parDemat(e), texte: String(parDemat(e)), teinte: TeinteDemat[e] ?? 'gris',
              actif: critDemat === e, choisir: basculer(critDemat, e, setCritDemat),
            })) },
        ]} />

        <VuesListe vues={VuesEnrolements.map((v) => ({ cle: v.cle, libelle: v.libelle, compte: filtrer({ ...criteres, ...v.criteres }).length }))}
                   active={vueActive}
                   choisir={(cle) => { const v = VuesEnrolements.find((x) => x.cle === cle); if (v) poserEtat(v.criteres) }} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col /><col style={{ width: '17%' }} /><col style={{ width: '17%' }} />
              <col style={{ width: '14%' }} /><col style={{ width: '10%' }} /><col style={{ width: 34 }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Affaire</th>
                <th>Etat demat.</th>
                <th>Etat traitement</th>
                <th>Date creation</th>
                <th className="rig-lm-droite">Pieces</th>
                <th aria-label="Actions"></th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((dos) => {
                const estOuverte = ouverte === dos.affaire
                const actions: ActionLigne[] = [
                  { libelle: 'Visualiser la piece', fort: true, agir: () => ouvrirDossier(dos.dossier) },
                  { libelle: 'Visualiser le XML', agir: () => voirXml(dos) },
                ]
                const classes = [dos.etatTraitement === 'Rejete' ? 'rig-table__ligne--alerte' : '', estOuverte ? 'rig-lm-ouverte' : '']
                  .filter(Boolean).join(' ')
                return (
                  <Fragment key={dos.affaire}>
                  <tr className={classes || undefined} onClick={() => setSelection(dos)}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : dos.affaire)} />
                    <CelluleIdentite nom={dos.denomination} objet={dos.affaire} titre={`${dos.denomination} · ${dos.affaire}`} />
                    <td title={dos.etatDemat}><Pastille teinte={TeinteDemat[dos.etatDemat] ?? 'gris'}>{dos.etatDemat}</Pastille></td>
                    <td title={dos.etatTraitement}>
                      <PointEtat teinte={TeinteTraitement[dos.etatTraitement] ?? 'gris'}>{dos.etatTraitement}</PointEtat>
                    </td>
                    <td className="rig-table__mono">{dateCourte(dos.creation)}</td>
                    <td className="rig-lm-droite">{dos.nbPieces}</td>
                    <td className="rig-lm-cellule-menu">
                      <MenuLigne libelle={`Actions de l'affaire ${dos.affaire}`} actions={[
                        ...actions,
                        { libelle: estOuverte ? 'Replier la ligne' : 'Deplier la ligne', agir: () => setOuverte(estOuverte ? null : dos.affaire) },
                      ]} />
                    </td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes} actions={actions} champs={[
                      { libelle: 'Type de rejet', valeur: dos.typeRejet },
                      { libelle: 'Date traitement', valeur: dos.traitement ? dateCourte(dos.traitement) : '' },
                      { libelle: 'Dossier', valeur: dos.dossier },
                      { libelle: 'Anomalies', valeur: dos.anomalies },
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {trouves.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">Aucune affaire ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {s && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-kicker">Detail de {s.affaire}</div>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Anomalies</span>
              <textarea className="rig-saisie-champ__saisie" rows={3} readOnly value={s.anomalies} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Motif de rejet</span>
              <textarea className="rig-saisie-champ__saisie" rows={2} readOnly value={s.motifRejet} />
            </label>
            <div className="rig-criteres__actions">
              <button className="rig-bouton" onClick={() => ouvrirDossier(s.dossier)}>Visualiser la piece</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setXmlOuvert(!xmlOuvert)}>Visualiser le XML</button>
            </div>
            {xmlOuvert && <pre className="rig-code">{s.xml}</pre>}
          </div>
        )}

        <PiedListe>
          <span><b>{trouves.length}</b>{trouves.length > 1 ? ' affaires affichees' : ' affaire affichee'}</span>
          <span>Pieces <b>{pieces}</b></span>
          <span>En attente <b>{enAttente}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
