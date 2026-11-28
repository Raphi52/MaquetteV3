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
//
// LA BASE DU GREFFE (plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026, marche du lot 17) : les enrolements viennent du
// pont (Pont/EcranConsultationJudBase.cs → Serveur/Socle/EcransMain/LectureConsultationJud.cs), DEMAT_RPJC_ENR de la base :
// etat du flux et traitement aux libelles de RIG v2 (eDematEtat ; Accepté / Refusé / En attente), type et motif de rejet,
// dates, role de l'instance creee a l'acceptation. « Visualiser le XML » lit le flux RPJCE_FLUX de l'enrolement A LA DEMANDE
// (EcranConsultationJud.Xml, par son id), comme la V2 qui l'ecrit dans un fichier et l'ouvre (PROC_CJUD.cs:437-455). Le nombre
// de pieces (flux entrant de la base EDI du greffe) et les anomalies (que la V2 calcule en lisant le XML pour creer l'instance,
// PROC_CJUD.cs:461) ne sont pas lus : l'ecran le dit au lieu d'afficher 0. Sans serveur, les quatre enrolements de demonstration du pont,
// marques « exemple » (ProvenanceEcran) ; en panne, le motif et aucune ligne. La vue « Non dematerialisees » est retiree :
// aucun etat de RIG v2 ne la porte (DematEtat.cs:13-49).

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'
import {
  CartesChiffrees, CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, MenuLigne, Pastille, PiedListe,
  PointEtat, VuesListe, type ActionLigne, type Teinte,
} from '../composants/ListeModerne'
import type { ContexteDossierVue } from '../plan/types'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { Annonce, type CompteRendu, type ResultatGesteVue } from './formes/commun'

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
  /** null : non lu par RIG v3 (flux entrant de la base EDI du greffe). */
  nbPieces: number | null
  /** null : non lues par RIG v3 (la V2 les calcule en lisant le XML pour creer l'instance). */
  anomalies: string | null
  motifRejet: string
  /** null : un enrolement de la base, dont le XML se lit a la demande par son id. */
  xml: string | null
  dossier: string
  /** L'enrolement dans la base du greffe (RPJCE_ID_RPJCE) ; absent pour la demonstration, qui porte son XML. */
  id?: string | null
}

/** Le XML d'un enrolement lu a la demande (Pont/EcranConsultationJud.cs, XmlConsultationJud). */
interface XmlConsultationJud { mode: string; xml: string | null; message: string | null }

/** Ce que le pont rend (Pont/EcranConsultationJud.cs, VueConsultationJud). */
interface VueConsultationJud {
  lignes: DossierJud[]
  mode?: ModeLecture
  message?: string | null
  total: number
}

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
function filtrer(source: DossierJud[], c: CriteresConsultation): DossierJud[] {
  let q = source
  if (!estBlanc(c.critTexte)) {
    const t = c.critTexte.trim()
    q = q.filter((d) => contientSansCasse(d.affaire, t) || contientSansCasse(d.denomination, t))
  }
  if (c.critDemat !== '') q = q.filter((d) => d.etatDemat === c.critDemat)
  if (c.critTraitement !== '') q = q.filter((d) => d.etatTraitement === c.critTraitement)
  if (c.critCreationDu !== '') q = q.filter((d) => d.creation >= c.critCreationDu)
  if (c.critTraitementAu !== '') q = q.filter((d) => d.traitement !== null && d.traitement <= c.critTraitementAu)
  if (c.rejetsSeuls) q = q.filter((d) => d.etatTraitement === 'Refusé')
  return q
}

/** Une vue pose les criteres d'ETAT de l'ecran ; le texte et les dates saisis restent. */
type CriteresEtat = Pick<CriteresConsultation, 'critDemat' | 'critTraitement' | 'rejetsSeuls'>
const SansEtat: CriteresEtat = { critDemat: '', critTraitement: '', rejetsSeuls: false }

export const VuesEnrolements: ReadonlyArray<{ cle: string; libelle: string; criteres: CriteresEtat }> = [
  { cle: 'toutes', libelle: 'Toutes', criteres: SansEtat },
  { cle: 'en-attente', libelle: 'En attente', criteres: { ...SansEtat, critTraitement: 'En attente' } },
  { cle: 'rejets', libelle: 'Rejets', criteres: { ...SansEtat, rejetsSeuls: true } },
]

/** Les etats du flux de RIG v2 (eDematEtat) : l'aller et le retour avec Docapost. */
const TeinteDemat: Record<string, Teinte> = {
  'Intégré avec succès': 'bleu', 'Traité par le greffe': 'bleu', Envoyé: 'bleu', Validé: 'vert',
  "Erreur d'envoi": 'orange', Rejeté: 'rouge', 'Ne pas envoyer': 'gris',
}
const TeinteTraitement: Record<string, Teinte> = { Accepté: 'vert', 'En attente': 'orange', Refusé: 'rouge' }

/** Ce que RIG v3 ne lit pas : les pieces (flux entrant de la base EDI du greffe, OPE_CJUDINFOG.cs:106-113) et les anomalies
 *  (Instance.CreateInstanceFromXmlRPJCEnr, PROC_CJUD.cs:461). */
const NonLu = 'Non lu par RIG v3 : la V2 le compte dans le flux entrant de la base EDI du greffe'
const AnomaliesNonLues = 'Non lu par RIG v3 : la V2 les calcule en lisant le XML pour créer l’instance'

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
  // Le XML lu a la demande pour l'enrolement choisi (id), ou ce qui dit pourquoi il n'y en a pas.
  const [xmlLu, setXmlLu] = useState<{ id: string; texte: string } | null>(null)
  const [selection, setSelection] = useState<DossierJud | null>(null)
  // La ligne depliee par son chevron (une a la fois).
  const [ouverte, setOuverte] = useState<string | null>(null)
  // null tant que le pont n'a pas repondu : aucune ligne, jamais la demonstration a la place de la base.
  const [vue, setVue] = useState<VueConsultationJud | null>(null)
  // « Saisie de la demande » de RIG v2 (OPE_CJUDINFOG, lot 8 du plan « parcours complet ») : l'etat de traitement, et pour un refus
  // son type et son motif ; le serveur l'ecrit (Pont/EcranConsultationJudBase.cs, Traiter), la liste se relit ensuite.
  const [traitement, setTraitement] = useState({ etat: 'R', type: 'MQPIECE', motif: '' })
  const [annonce, setAnnonce] = useState<CompteRendu>(null)
  const [envoi, setEnvoi] = useState(false)
  const [relecture, setRelecture] = useState(0)

  useEffect(() => {
    let perime = false
    void appeler<VueConsultationJud>('EcranConsultationJud.Vue').then((v) => {
      if (perime) return
      setVue(v)
      // La ligne choisie est relue avec la liste : son etat de traitement suit la base.
      setSelection((avant) => avant === null ? null : v.lignes.find((l) => l.id === avant.id && l.affaire === avant.affaire) ?? avant)
    })
    return () => { perime = true }
  }, [relecture])

  async function traiter(dos: DossierJud) {
    setEnvoi(true)
    try {
      const r = await appeler<ResultatGesteVue>('EcranConsultationJud.Traiter', dos.id ?? '', traitement.etat, traitement.type, traitement.motif)
      setAnnonce(r.fait || r.sansEcrivain ? r.message : { refus: r.message })
      if (r.fait) setRelecture((n) => n + 1)
    } catch {
      setAnnonce({ refus: "Le pont n'a pas répondu : rien n'est enregistré." })
    } finally {
      setEnvoi(false)
    }
  }

  const source = vue?.lignes ?? []
  const criteres: CriteresConsultation = { critTexte, critDemat, critTraitement, critCreationDu, critTraitementAu, rejetsSeuls }
  const trouves = filtrer(source, criteres)

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
  const rejets = parTraitement('Refusé')
  // Les pieces ne se comptent que si elles sont lues (null : base EDI du greffe, non lue par RIG v3).
  const piecesLues = trouves.every((d) => d.nbPieces !== null)
  const pieces = trouves.reduce((n, d) => n + (d.nbPieces ?? 0), 0)
  const basculer = (actuel: string, valeur: string, poser: (v: string) => void) => () => poser(actuel === valeur ? '' : valeur)

  /** « Visualiser le XML » d'une ligne : la choisit et montre son XML dans le detail. */
  function voirXml(dos: DossierJud) {
    setSelection(dos)
    setXmlOuvert(true)
  }

  const s = selection

  // « Visualiser le XML » sur un enrolement de la base : son flux se lit au pont, une fois par enrolement choisi.
  const idXml = xmlOuvert && s && s.xml === null && s.id ? s.id : null
  useEffect(() => {
    if (idXml === null) return
    let perime = false
    void appeler<XmlConsultationJud>('EcranConsultationJud.Xml', idXml)
      .then((r) => { if (!perime) setXmlLu({ id: idXml, texte: r.xml ?? r.message ?? '' }) })
    return () => { perime = true }
  }, [idXml])

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de consultation</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Etat de dematerialisation</span>
          <select className="rig-saisie-champ__saisie" value={critDemat} onChange={(e) => setCritDemat(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(source.map((d) => d.etatDemat)).map((e) => <option key={e} value={e}>{e}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Etat de traitement</span>
          <select className="rig-saisie-champ__saisie" value={critTraitement} onChange={(e) => setCritTraitement(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(source.map((d) => d.etatTraitement)).map((e) => <option key={e} value={e}>{e}</option>)}
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

        {vue && <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Enrôlements" />}

        <CartesChiffrees cartes={[
          { cle: 'traitement', titre: 'Etat de traitement', valeur: trouves.length,
            complement: trouves.length > 1 ? 'affaires' : 'affaire', legende: true,
            ...(critTraitement !== '' ? { choisir: () => setCritTraitement(''), indication: 'Tous les etats de traitement' } : {}),
            segments: distinctsTries(source.map((d) => d.etatTraitement)).map((e) => ({
              cle: e, libelle: e, valeur: parTraitement(e), texte: String(parTraitement(e)), teinte: TeinteTraitement[e] ?? 'gris',
              actif: critTraitement === e, choisir: basculer(critTraitement, e, setCritTraitement),
            })) },
          { cle: 'rejets', titre: 'Rejets', valeur: rejets, complement: `sur ${trouves.length}`,
            anneau: { part: rejets, total: trouves.length, teinte: 'rouge' },
            actif: rejetsSeuls, choisir: () => setRejetsSeuls(!rejetsSeuls),
            indication: rejetsSeuls ? 'Toutes les affaires' : 'Seulement les rejets' },
          { cle: 'demat', titre: 'Dematerialisation', valeur: parDemat('Validé'), complement: `${parDemat('Validé') > 1 ? 'validés' : 'validé'} sur ${trouves.length}`,
            legende: true,
            ...(critDemat !== '' ? { choisir: () => setCritDemat(''), indication: 'Tous les etats de dematerialisation' } : {}),
            segments: distinctsTries(source.map((d) => d.etatDemat)).map((e) => ({
              cle: e, libelle: e, valeur: parDemat(e), texte: String(parDemat(e)), teinte: TeinteDemat[e] ?? 'gris',
              actif: critDemat === e, choisir: basculer(critDemat, e, setCritDemat),
            })) },
        ]} />

        <VuesListe vues={VuesEnrolements.map((v) => ({ cle: v.cle, libelle: v.libelle, compte: filtrer(source, { ...criteres, ...v.criteres }).length }))}
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
                const classes = [dos.etatTraitement === 'Refusé' ? 'rig-table__ligne--alerte' : '', estOuverte ? 'rig-lm-ouverte' : '']
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
                    <td className="rig-lm-droite" title={dos.nbPieces === null ? NonLu : undefined}>{dos.nbPieces ?? '—'}</td>
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
                      { libelle: 'Anomalies', valeur: dos.anomalies ?? AnomaliesNonLues },
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
              <textarea className="rig-saisie-champ__saisie" rows={3} readOnly value={s.anomalies ?? AnomaliesNonLues} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Motif de rejet</span>
              <textarea className="rig-saisie-champ__saisie" rows={2} readOnly value={s.motifRejet} />
            </label>
            <div className="rig-criteres__actions">
              <button className="rig-bouton" onClick={() => ouvrirDossier(s.dossier)}>Visualiser la piece</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setXmlOuvert(!xmlOuvert)}>Visualiser le XML</button>
            </div>
            {s.etatTraitement === 'En attente' && (
              <div className="rig-criteres__bloc">
                <div className="rig-kicker">Saisie de la demande</div>
                <div className="rig-criteres__paire">
                  <label className="rig-saisie-champ">
                    <span className="rig-saisie-champ__libelle">Etat traitement</span>
                    <select className="rig-saisie-champ__saisie" value={traitement.etat}
                            onChange={(e) => setTraitement({ ...traitement, etat: e.target.value })}>
                      <option value="A">Accepté</option>
                      <option value="R">Refusé</option>
                    </select>
                  </label>
                  {traitement.etat === 'R' && (
                    <label className="rig-saisie-champ">
                      <span className="rig-saisie-champ__libelle">Type rejet</span>
                      <select className="rig-saisie-champ__saisie" value={traitement.type}
                              onChange={(e) => setTraitement({ ...traitement, type: e.target.value })}>
                        <option value="MQPIECE">Manque pièce</option>
                        <option value="PAIEMENT">Paiement incorrect</option>
                      </select>
                    </label>
                  )}
                </div>
                {traitement.etat === 'R' && (
                  <label className="rig-saisie-champ">
                    <span className="rig-saisie-champ__libelle">Motif rejet</span>
                    <textarea className="rig-saisie-champ__saisie" rows={2} maxLength={300} value={traitement.motif}
                              onChange={(e) => setTraitement({ ...traitement, motif: e.target.value })} />
                  </label>
                )}
                <div className="rig-criteres__actions">
                  <button className="rig-bouton rig-bouton--fort" disabled={envoi} onClick={() => void traiter(s)}>Valider</button>
                </div>
              </div>
            )}
            <Annonce texte={annonce} />
            {xmlOuvert && <pre className="rig-code">{s.xml ?? (xmlLu !== null && xmlLu.id === s.id ? xmlLu.texte : 'Lecture du XML…')}</pre>}
          </div>
        )}

        <PiedListe>
          <span><b>{trouves.length}</b>{trouves.length > 1 ? ' affaires affichees' : ' affaire affichee'}</span>
          <span>Pieces <b>{piecesLues ? pieces : '—'}</b></span>
          <span>En attente <b>{enAttente}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
