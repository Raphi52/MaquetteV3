// Ecran de la procedure PROC_GEDJUD.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_GEDJUD) :
//   - ETP_RECHERCHE_DOC/OPE_RECHERCHE_DOC : ulT_txt_numero_metier,
//     ult_cbb_type, ult_date_reception_debut, ult_date_reception_fin,
//     ult_txt_audience_cabinet, ult_txt_commentaire,
//     ult_chb_afficher_minute_non_num, ult_chb_afficher_tout,
//     ult_chb_expandAll, ult_chb_selectAll
//   - ETP_RECHERCHE_DJ/OPE_RECHERCHE_DJ : ult_txt_numero, ult_cbb_type
//   - PROC_GED_LIER_DJ, PROC_GED_HISTORIQUE, PROC_GEDJUD_EDITION :
//     les actions Lier a une decision, Historique, Imprimer, Envoyer.
//
// Porte depuis Components/Ecrans/EcranGedJud.razor (lot 5) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses documents de
// demonstration et ses filtres sont ici, sans pont. La coquille .razor monte
// cet ecran et recoit ouvrirDossier.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 2) : la colonne des resultats prend
// les briques de composants/ListeModerne.tsx. Tete (synthese suivie de l'action demandee, « Aller a une
// decision… » Ctrl K, Entree choisit le document reste seul), trois cartes qui POSENT les criteres existants
// (etat, nature, « Non lies a une decision »), des vues = jeux des criteres d'etat. Le tableau passe a une ligne
// par document (Decision, Instance, Nature, Date Decision, Docs lies, Etat) ; numero de minute, date prevue et
// dossier sont dans la ligne depliee, avec les actions du document. Une decision n'est ni une entreprise, ni une
// personne, ni un dossier : pas d'avatar, une cellule simple (comme l'article d'une facture, lot 4).
// « Aller a une decision… » EST l'ancien critere « Instance / decision » (meme etat, memes colonnes cherchees) :
// la liste est filtree ici, sans pont, deux champs auraient fait le meme filtre (decision du plan, lot 7). Les
// autres criteres, le clic qui montre les actions et l'historique : inchanges.
//
// BRANCHE SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026, marche du lot 17) : le pont
// Pont/EcranGedJud.cs rend les documents de la GED judiciaire du greffe (VIDIGED_INDEX, Serveur/Socle/EcransMain/
// LectureGedJud.cs, la requete de OPE_RECHERCHE_DOC). Une ligne de la base : le libelle du document, le numero de l'entite
// qui le porte (instance, PC, prevention, mesure), sa nature, la date de la premiere decision liee, le nombre de decisions
// liees (VIDIGED_DJ) et l'etat de RIG v2 (Valide, Valide avec reserve, En attente, Rejete). Les criteres de RIG v2 s'y
// appliquent ICI, sur les lignes lues : un rejete est cache sauf « Afficher tout » (OPE_RECHERCHE_DOC.cs:376), la date de
// reception du jour seul ou de la plage, 0 h a 23:59:59 (:358-373), le commentaire contenu (:342-344). « Non lies a une
// decision » = aucune decision liee (GetNbDecisionsJudiciairesLiees). Sans serveur, les documents de demonstration
// ci-dessous, marques « exemple » (provenance.tsx) ; en panne, le motif et aucune ligne. Non repris : « Audience /
// cabinet » (un ecran de recherche OCX de RIG v2) et les minutes non numerisees (elles viennent des decisions).

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { Annonce, type CompteRendu, type ResultatGesteVue } from './formes/commun'
import {
  CartesChiffrees, CelluleChevron, EnteteListe, LigneDetail, ListeModerne, MenuLigne, Pastille, PiedListe,
  PointEtat, VuesListe, teinteStatut, type ActionLigne, type Teinte,
} from '../composants/ListeModerne'
import type { ContexteDossierVue } from '../plan/types'

interface DocGed {
  decision: string
  minute: string
  instance: string
  nature: string
  /** aaaa-mm-jj, null pour un document que nulle decision ne porte encore */
  dateDecision: string | null
  /** aaaa-mm-jj */
  datePrevue: string
  nbLies: number
  etat: string
  statut: string
  dossier: string
  historiques: string[]
  /** Un document de la base (Pont/EcranGedJud.cs) : son identifiant, le type de son entite, son etat V2 (V, VR, A, R),
   *  sa date de reception (aaaa-mm-jj), son commentaire et son motif de rejet. */
  id?: string
  typeEntite?: string
  codeEtat?: string
  reception?: string | null
  commentaire?: string
  motifRejet?: string
}

/** Ce que rend Pont/EcranGedJud.cs : les documents, leur total et d'ou ils viennent. */
export interface VueGedJud {
  lignes: DocGed[]
  total: number
  mode?: ModeLecture
  message?: string | null
}

const Docs: DocGed[] = [
  { decision: 'Jugement d\'ouverture', minute: '2026/0412', instance: '2026J00121', nature: 'Jugement', dateDecision: '2026-03-06', datePrevue: '2026-03-05', nbLies: 4, etat: 'Notifie', statut: 'ok', dossier: '2024B00318', historiques: ['06/03/2026 — signe par le president', '07/03/2026 — notifie au mandataire'] },
  { decision: 'Ordonnance de designation', minute: '2026/0518', instance: '2026J00134', nature: 'Ordonnance', dateDecision: '2026-06-02', datePrevue: '2026-06-01', nbLies: 2, etat: 'Signe', statut: 'action', dossier: '2024B00407', historiques: ['02/06/2026 — signe par le juge-commissaire'] },
  { decision: 'Jugement de cloture', minute: '', instance: '2025J00088', nature: 'Jugement', dateDecision: null, datePrevue: '2026-09-20', nbLies: 0, etat: 'Brouillon', statut: 'attente', dossier: '2023B01120', historiques: ['01/09/2026 — projet cree'] },
  { decision: 'Copie conforme', minute: '2024/1188', instance: '2024J00507', nature: 'Copie', dateDecision: '2024-11-08', datePrevue: '2024-11-08', nbLies: 1, etat: 'Notifie', statut: 'ok', dossier: '2025B00044', historiques: ['08/11/2024 — copie delivree'] },
]

export interface ProprietesEcranGedJud {
  /** Remonte le NUMERO de dossier du document : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

/** Les criteres qui filtrent la liste, tels que saisis. */
interface CriteresGed {
  recherche: string
  critNature: string
  critEtat: string
  sansDecision: boolean
}

/** Les criteres de RIG v2 qui ne valent que pour un document de la base (sa reception, son commentaire, son etat V2). */
interface CriteresBase {
  receptionDebut: string
  receptionFin: string
  commentaire: string
  afficherTout: boolean
}
const SansCriteresBase: CriteresBase = { receptionDebut: '', receptionFin: '', commentaire: '', afficherTout: false }

/**
 * Le filtre des criteres : la liste, et le compte de chaque vue. « Non lies a une decision » : aucune decision liee. Les
 * criteres de RIG v2 (CriteresBase) ne filtrent que les documents de la base : la demonstration n'a ni reception ni etat V2.
 */
function filtrer(docs: DocGed[], c: CriteresGed, b: CriteresBase = SansCriteresBase): DocGed[] {
  let q = docs
  if (!estBlanc(c.recherche)) {
    const t = c.recherche.trim()
    q = q.filter((d) => contientSansCasse(d.instance, t) || contientSansCasse(d.decision, t))
  }
  if (c.critNature !== '') q = q.filter((d) => d.nature === c.critNature)
  if (c.critEtat !== '') q = q.filter((d) => d.etat === c.critEtat)
  if (c.sansDecision) q = q.filter((d) => d.nbLies === 0)
  // OPE_RECHERCHE_DOC.cs:376 : « VGIDX_ETAT <> 'R' » sauf « Afficher tout ».
  if (!b.afficherTout) q = q.filter((d) => d.codeEtat !== 'R')
  // :358-373 : la date de debut seule vaut pour ce jour ; la fin, a 23:59:59 (une date aaaa-mm-jj se compare telle quelle).
  if (b.receptionDebut !== '') {
    const fin = b.receptionFin !== '' ? b.receptionFin : b.receptionDebut
    q = q.filter((d) => d.reception === undefined || (d.reception !== null && d.reception >= b.receptionDebut && d.reception <= fin))
  }
  // :342-344 : « VGIDX_COMMENTAIRE like %...% ».
  if (!estBlanc(b.commentaire)) q = q.filter((d) => d.commentaire === undefined || contientSansCasse(d.commentaire, b.commentaire.trim()))
  return q
}

/** Les etats de RIG v2, dans l'ordre de VIDIGED_PARAMETRAGE (ULT_COMBO_ETAT) : ceux des documents lus, dans cet ordre. */
const OrdreEtatsV2 = ['V', 'VR', 'A', 'R']
function etatsDeLaBase(docs: DocGed[]): string[] {
  const rang = (d: DocGed) => { const i = OrdreEtatsV2.indexOf(d.codeEtat ?? ''); return i < 0 ? OrdreEtatsV2.length : i }
  return [...new Map([...docs].sort((a, b) => rang(a) - rang(b)).map((d) => [d.etat, d.etat])).keys()]
}

/** Une vue pose les criteres d'ETAT (etat, « Non lies a une decision ») ; le texte et la nature restent. */
type CriteresEtat = Pick<CriteresGed, 'critEtat' | 'sansDecision'>
const SansEtat: CriteresEtat = { critEtat: '', sansDecision: false }

type VueDocuments = { cle: string; libelle: string; criteres: CriteresEtat }
export const VuesDocuments: ReadonlyArray<VueDocuments> = [
  { cle: 'tous', libelle: 'Tous', criteres: SansEtat },
  { cle: 'brouillons', libelle: 'Brouillons', criteres: { ...SansEtat, critEtat: 'Brouillon' } },
  { cle: 'signes', libelle: 'Signes', criteres: { ...SansEtat, critEtat: 'Signe' } },
  { cle: 'notifies', libelle: 'Notifies', criteres: { ...SansEtat, critEtat: 'Notifie' } },
  { cle: 'sans-decision', libelle: 'Non lies a une decision', criteres: { ...SansEtat, sansDecision: true } },
]

/** Les vues d'un document de la base : une par etat de RIG v2 lu, et « Non lies a une decision ». */
const vuesDeLaBase = (etats: string[]): VueDocuments[] => [
  { cle: 'tous', libelle: 'Tous', criteres: SansEtat },
  ...etats.map((e) => ({ cle: 'etat-' + e, libelle: e, criteres: { ...SansEtat, critEtat: e } })),
  { cle: 'sans-decision', libelle: 'Non lies a une decision', criteres: { ...SansEtat, sansDecision: true } },
]

/** Les etats du critere, dans son ordre. Le point d'etat d'une ligne suit le statut du document (teinteStatut du socle). */
const Etats = ['Brouillon', 'Signe', 'Notifie']
const TeinteEtat: Record<string, Teinte> = {
  Brouillon: 'orange', Signe: 'bleu', Notifie: 'vert',
  // Les etats de RIG v2 : un rejete en rouge (OPE_RESULTAT_TREEVIEW.cs:60-61), en attente en orange, valide en vert.
  'Validé': 'vert', 'Validé avec réserve': 'bleu', 'En attente': 'orange', 'Rejeté': 'rouge',
}
const TeinteNature: Record<string, Teinte> = { Jugement: 'bleu', Ordonnance: 'violet', Copie: 'gris' }

/** Les colonnes du tableau a une ligne : chevron, Decision, Instance, Nature, Date Decision, Docs lies, Etat, menu. */
const NbColonnes = 8

export function EcranGedJud({ ouvrirDossier, dossier = null }: ProprietesEcranGedJud) {
  // Les documents du pont ; sans reponse (pas de pont) ou sans serveur, la demonstration reste.
  const [vue, setVue] = useState<VueGedJud | null>(null)
  // Relue apres un geste (« Modifier le document », lot 8 du plan « parcours complet »).
  const [relecture, setRelecture] = useState(0)
  useEffect(() => {
    let perime = false
    void appeler<VueGedJud>('EcranGedJud.Documents')
      .then((v) => {
        if (perime || v.mode === undefined || v.mode === 'demo') return
        setVue(v)
        setSelection((avant) => avant === null ? null : v.lignes.find((l) => l.id !== undefined && l.id === avant.id) ?? avant)
      })
      .catch(() => { /* pas de pont (tests, page hors WebView2) : la demonstration reste */ })
    return () => { perime = true }
  }, [relecture])
  // « Modifier le document » de RIG v2 (PROC_GED_MODIF) : la liste « Etat » de la base, le motif d'un rejet, le commentaire.
  const [etatsGed, setEtatsGed] = useState<{ code: string; libelle: string }[]>([])
  useEffect(() => {
    let perime = false
    void appeler<{ etats: { code: string; libelle: string }[] }>('EcranGedJud.Etats')
      .then((v) => { if (!perime) setEtatsGed(v.etats ?? []) })
      .catch(() => { /* pas de pont : pas de geste */ })
    return () => { perime = true }
  }, [])
  const [modif, setModif] = useState<{ id: string; etat: string; motif: string; commentaire: string } | null>(null)
  const [annonce, setAnnonce] = useState<CompteRendu>(null)
  const [envoi, setEnvoi] = useState(false)

  async function enregistrerDocument() {
    if (!modif) return
    setEnvoi(true)
    try {
      const r = await appeler<ResultatGesteVue>('EcranGedJud.Modifier', modif.id, modif.etat, modif.motif, modif.commentaire)
      setAnnonce(r.fait || r.sansEcrivain ? r.message : { refus: r.message })
      if (r.fait) { setModif(null); setRelecture((n) => n + 1) }
    } catch {
      setAnnonce({ refus: "Le pont n'a pas répondu : rien n'est enregistré." })
    } finally {
      setEnvoi(false)
    }
  }
  const docs = vue ? vue.lignes : Docs
  const etats = vue ? etatsDeLaBase(docs) : Etats
  const vues = vue ? vuesDeLaBase(etats) : VuesDocuments

  // Criteres de OPE_RECHERCHE_DOC (RIG v2).
  const [receptionDebut, setReceptionDebut] = useState('')
  const [receptionFin, setReceptionFin] = useState('')
  const [audienceCabinet, setAudienceCabinet] = useState('')
  const [critCommentaire, setCritCommentaire] = useState('')
  const [minutesNonNumerisees, setMinutesNonNumerisees] = useState(false)
  const [afficherTout, setAfficherTout] = useState(false)

  // « Aller a une decision… » de la tete (l'ancien critere « Instance / decision ») : ouvert depuis une affaire,
  // ses documents y sont cherches par son numero.
  const [recherche, setRecherche] = useState(dossier?.numero ?? '')
  const [critNature, setCritNature] = useState('')
  const [critEtat, setCritEtat] = useState('')
  const [sansDecision, setSansDecision] = useState(false)
  const [historique, setHistorique] = useState(false)
  const [action, setAction] = useState<string | null>(null)
  const [selection, setSelection] = useState<DocGed | null>(null)
  // La ligne depliee par son chevron (une a la fois).
  const [ouverte, setOuverte] = useState<string | null>(null)

  const criteres: CriteresGed = { recherche, critNature, critEtat, sansDecision }
  const criteresBase: CriteresBase = { receptionDebut, receptionFin, commentaire: critCommentaire, afficherTout }
  const trouves = filtrer(docs, criteres, criteresBase)

  const vueActive = vues.find((v) => v.criteres.critEtat === critEtat && v.criteres.sansDecision === sansDecision)?.cle ?? null

  // Les chiffres de la tete, des cartes et du pied : sur la liste affichee.
  const parEtat = (e: string) => trouves.filter((d) => d.etat === e).length
  const parNature = (n: string) => trouves.filter((d) => d.nature === n).length
  const brouillons = parEtat('Brouillon')
  const nonLies = trouves.filter((d) => d.nbLies === 0).length
  const lies = trouves.reduce((n, d) => n + d.nbLies, 0)
  const basculer = (actuel: string, valeur: string, poser: (v: string) => void) => () => poser(actuel === valeur ? '' : valeur)

  /** « Historique » d'une ligne : la choisit et deplie son historique dans le bloc du document. */
  function voirHistorique(doc: DocGed) {
    setSelection(doc)
    setHistorique(true)
  }

  /** Une action de ligne (Imprimer, Envoyer…) vise SON document : il devient le document choisi, comme au clic. */
  function agirSur(doc: DocGed, libelle: string) {
    setSelection(doc)
    setAction(libelle)
  }

  const s = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Documents de la GED</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nature du document</span>
          <select className="rig-saisie-champ__saisie" value={critNature} onChange={(e) => setCritNature(e.target.value)}>
            <option value="">Toutes</option>
            {distinctsTries(docs.map((d) => d.nature)).map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Etat</span>
          <select className="rig-saisie-champ__saisie" value={critEtat} onChange={(e) => setCritEtat(e.target.value)}>
            <option value="">Tous</option>
            {etats.map((e) => <option key={e} value={e}>{e}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date de reception</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date" value={receptionDebut}
                     onChange={(e) => setReceptionDebut(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date" value={receptionFin}
                     onChange={(e) => setReceptionFin(e.target.value)} />
            </label>
          </div>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Audience / cabinet</span>
          <input className="rig-saisie-champ__saisie" value={audienceCabinet} onChange={(e) => setAudienceCabinet(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Commentaire</span>
          <input className="rig-saisie-champ__saisie" value={critCommentaire} onChange={(e) => setCritCommentaire(e.target.value)} />
        </label>

        {/* v2 : ult_chb_afficher_minute_non_num et ult_chb_afficher_tout. */}
        <button className={`rig-bascule${minutesNonNumerisees ? ' rig-bascule--actif' : ''}`}
                onClick={() => setMinutesNonNumerisees(!minutesNonNumerisees)}>
          <span className="rig-bascule__temoin"></span>
          <span>Afficher les minutes non numerisees</span>
        </button>

        <button className={`rig-bascule${afficherTout ? ' rig-bascule--actif' : ''}`}
                onClick={() => setAfficherTout(!afficherTout)}>
          <span className="rig-bascule__temoin"></span>
          <span>Afficher tout</span>
        </button>

        <button className={`rig-bascule${sansDecision ? ' rig-bascule--actif' : ''}`}
                onClick={() => setSansDecision(!sansDecision)}>
          <span className="rig-bascule__temoin"></span>
          <span>Non lies a une decision</span>
        </button>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={() => setAction('Inserer un document')}>Inserer un document</button>{' '}
          <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Lier a une decision judiciaire')}>Lier a une decision</button>
        </div>
      </aside>

      <ListeModerne etiquette="Decisions judiciaires et documents lies">
        <EnteteListe titre="Decisions judiciaires et documents lies"
                     synthese={<>{trouves.length}{trouves.length > 1 ? ' documents · ' : ' document · '}
                       {!vue && <><b>{brouillons}</b>{brouillons > 1 ? ' brouillons · ' : ' brouillon · '}</>}<b>{lies}</b>{' docs lies'}
                       {action === null ? '' : ' — ' + action}</>}
                     filtre={{ valeur: recherche, changer: setRecherche, indication: 'Aller a une decision…',
                               valider: () => { if (trouves.length === 1) setSelection(trouves[0]!) } }} />
        {vue ? <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Documents" /> : <ProvenanceEcran mode="demo" quoi="Documents" />}

        <CartesChiffrees cartes={[
          { cle: 'etat', titre: 'Etat', valeur: trouves.length, complement: trouves.length > 1 ? 'documents' : 'document', legende: true,
            ...(critEtat !== '' ? { choisir: () => setCritEtat(''), indication: 'Tous les etats' } : {}),
            segments: etats.map((e) => ({
              cle: e, libelle: e, valeur: parEtat(e), texte: String(parEtat(e)), teinte: TeinteEtat[e] ?? 'gris',
              actif: critEtat === e, choisir: basculer(critEtat, e, setCritEtat),
            })) },
          { cle: 'nature', titre: 'Nature du document', valeur: distinctsTries(trouves.map((d) => d.nature)).length,
            complement: 'natures', legende: true,
            ...(critNature !== '' ? { choisir: () => setCritNature(''), indication: 'Toutes les natures' } : {}),
            segments: distinctsTries(docs.map((d) => d.nature)).map((n) => ({
              cle: n, libelle: n, valeur: parNature(n), texte: String(parNature(n)), teinte: TeinteNature[n] ?? 'gris',
              actif: critNature === n, choisir: basculer(critNature, n, setCritNature),
            })) },
          { cle: 'sans-decision', titre: 'Non lies a une decision', valeur: nonLies, complement: `sur ${trouves.length}`,
            anneau: { part: nonLies, total: trouves.length, teinte: 'orange' },
            actif: sansDecision, choisir: () => setSansDecision(!sansDecision),
            indication: sansDecision ? 'Tous les documents' : 'Seulement les documents non lies a une decision' },
        ]} />

        <VuesListe vues={vues.map((v) => ({ cle: v.cle, libelle: v.libelle, compte: filtrer(docs, { ...criteres, ...v.criteres }, criteresBase).length }))}
                   active={vueActive}
                   choisir={(cle) => {
                     const v = vues.find((x) => x.cle === cle)
                     if (v) { setCritEtat(v.criteres.critEtat); setSansDecision(v.criteres.sansDecision) }
                   }} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col /><col style={{ width: '13%' }} /><col style={{ width: '15%' }} />
              <col style={{ width: '13%' }} /><col style={{ width: '10%' }} /><col style={{ width: '13%' }} /><col style={{ width: 34 }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Decision judiciaire</th>
                <th>Instance</th>
                <th>Nature du document</th>
                <th>Date Decision</th>
                <th className="rig-lm-droite">Docs lies</th>
                <th>Etat</th>
                <th aria-label="Actions"></th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((doc) => {
                // Un document de la base a son identifiant : plusieurs documents d'une meme entite se distinguent.
                const cle = doc.id ?? doc.instance
                const estOuverte = ouverte === cle
                const actions: ActionLigne[] = [
                  { libelle: 'Afficher le document', fort: true, agir: () => ouvrirDossier(doc.dossier) },
                  { libelle: 'Imprimer', agir: () => agirSur(doc, 'Imprimer') },
                  { libelle: 'Envoyer par e-mail', agir: () => agirSur(doc, 'Envoyer par e-mail') },
                  { libelle: 'Envoyer par courrier', agir: () => agirSur(doc, 'Envoyer par courrier') },
                  { libelle: 'Historique', agir: () => voirHistorique(doc) },
                ]
                return (
                  <Fragment key={cle}>
                  <tr className={estOuverte ? 'rig-lm-ouverte' : undefined} onClick={() => setSelection(doc)}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : cle)} />
                    <td title={doc.decision}>{doc.decision}</td>
                    <td className="rig-table__mono">{doc.instance}</td>
                    <td title={doc.nature}><Pastille teinte={TeinteNature[doc.nature] ?? 'gris'}>{doc.nature}</Pastille></td>
                    <td className="rig-table__mono">{doc.dateDecision ? dateCourte(doc.dateDecision) : ''}</td>
                    <td className="rig-lm-droite">{doc.nbLies}</td>
                    <td title={doc.etat}><PointEtat teinte={teinteStatut(doc.statut)}>{doc.etat}</PointEtat></td>
                    <td className="rig-lm-cellule-menu">
                      <MenuLigne libelle={`Actions du document ${doc.instance}`} actions={[
                        ...actions,
                        { libelle: estOuverte ? 'Replier la ligne' : 'Deplier la ligne', agir: () => setOuverte(estOuverte ? null : cle) },
                      ]} />
                    </td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes} actions={actions} champs={doc.id === undefined ? [
                      { libelle: 'Numero minute', valeur: doc.minute },
                      { libelle: 'Date prevue de la decision', valeur: dateCourte(doc.datePrevue) },
                      { libelle: 'Dossier', valeur: doc.dossier },
                    ] : [
                      { libelle: 'Entite', valeur: `${doc.typeEntite ?? ''} ${doc.instance}`.trim() },
                      { libelle: 'Date de reception', valeur: doc.reception ? dateCourte(doc.reception) : '' },
                      { libelle: 'Date prevue de la decision', valeur: doc.datePrevue ? dateCourte(doc.datePrevue) : '' },
                      { libelle: 'Commentaire', valeur: doc.commentaire ?? '' },
                      ...(doc.motifRejet ? [{ libelle: 'Motif du rejet', valeur: doc.motifRejet }] : []),
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {trouves.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">Aucun document ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {s && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-kicker">{s.decision} — {s.id === undefined ? `minute ${s.minute}` : `${s.typeEntite ?? ''} ${s.instance}`.trim()}</div>
            <div className="rig-criteres__actions">
              <button className="rig-bouton" onClick={() => ouvrirDossier(s.dossier)}>Afficher le document</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Imprimer')}>Imprimer</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Envoyer par e-mail')}>Envoyer par e-mail</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setAction('Envoyer par courrier')}>Envoyer par courrier</button>{' '}
              <button className="rig-bouton rig-bouton--plat" onClick={() => setHistorique(!historique)}>Historique</button>
              {s.id !== undefined && etatsGed.length > 0 && (
                <>{' '}<button className="rig-bouton rig-bouton--plat"
                  onClick={() => { setAnnonce(null); setModif({ id: s.id!, etat: s.codeEtat ?? '', motif: s.motifRejet ?? '', commentaire: s.commentaire ?? '' }) }}>
                  Modifier le document</button></>
              )}
            </div>
            {modif !== null && modif.id === s.id && (
              <div className="rig-criteres__bloc">
                <div className="rig-kicker">Modifier le document</div>
                <div className="rig-criteres__paire">
                  <label className="rig-saisie-champ">
                    <span className="rig-saisie-champ__libelle">Etat</span>
                    <select className="rig-saisie-champ__saisie" value={modif.etat} onChange={(e) => setModif({ ...modif, etat: e.target.value })}>
                      {etatsGed.map((e) => <option key={e.code} value={e.code}>{e.libelle || e.code}</option>)}
                    </select>
                  </label>
                  {modif.etat === 'R' && (
                    <label className="rig-saisie-champ">
                      <span className="rig-saisie-champ__libelle">Motif rejet</span>
                      <input className="rig-saisie-champ__saisie" maxLength={1000} value={modif.motif}
                             onChange={(e) => setModif({ ...modif, motif: e.target.value })} />
                    </label>
                  )}
                </div>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Libellé</span>
                  <input className="rig-saisie-champ__saisie" maxLength={1000} value={modif.commentaire}
                         onChange={(e) => setModif({ ...modif, commentaire: e.target.value })} />
                </label>
                <div className="rig-criteres__actions">
                  <button className="rig-bouton rig-bouton--fort" disabled={envoi} onClick={() => void enregistrerDocument()}>Enregistrer</button>{' '}
                  <button className="rig-bouton rig-bouton--plat" onClick={() => setModif(null)}>Annuler</button>
                </div>
              </div>
            )}
            <Annonce texte={annonce} />
            {historique && s.historiques.length > 0 && (
              <ul className="rig-historique__liste">
                {s.historiques.map((h, i) => <li key={i}>{h}</li>)}
              </ul>
            )}
            {historique && s.historiques.length === 0 && (
              <p className="rig-nonmaq__texte">L'historique du document (PROC_GED_HISTORIQUE de RIG v2) n'est pas relu dans la base.</p>
            )}
          </div>
        )}

        <PiedListe>
          <span><b>{trouves.length}</b>{trouves.length > 1 ? ' documents affiches' : ' document affiche'}</span>
          <span>Docs lies <b>{lies}</b></span>
          <span>Non lies a une decision <b>{nonLies}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
