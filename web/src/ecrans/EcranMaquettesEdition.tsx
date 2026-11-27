// Ecran de la procedure PROC_EDIT_MAQUETTE (RIG v2 : « Edition des
// maquettes », etapes etP_MAQUETTE et etP_PARAM_EDT, plus le bloc
// « Tables liees »). Champs lus dans les formulaires v2 :
// ComboBoxCodeProcess, ComboBoxTypeDoc, ComboBoxMaquette, ComboBoxTypeMaquette,
// ComboBoxImprimante, ComboBoxLot / LotLRAR / LotILRAR / LotSubstitution,
// ComboBoxTypePapier, CheckBoxRectoVerso, CheckBoxAgrafage,
// CheckBoxAffAvtImp, CheckBoxImpression, CheckBoxDocNonModifiable,
// CheckBoxEnteteFormatPlieuse, DataListviewTableLiee.
//
// Porte depuis Components/Ecrans/EcranMaquettesEdition.razor (lot 5) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// maquettes de demonstration et ses parametres sont ici, sans pont.
//
// LISTE MODERNE (24/11/2026, plan des listes modernes, lot 5) : la liste des maquettes prend les briques de
// composants/ListeModerne.tsx -- tete (synthese, « Aller a une maquette… » Ctrl K qui filtre la liste affichee,
// Entree choisit la maquette restee seule), vues = le critere Type de document existant, type de document en
// pastille, pied. Les criteres, le clic sur une ligne et les tables liees ne changent pas.
// fix-ok: ajout du socle des listes en plusieurs pas (lot 5), pas un correctif.
//
// BRANCHE SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026 ; marche du lot 17) : les maquettes viennent de la base
// du greffe (Pont/EcranMaquettesEdition.cs, Serveur/Socle/EcransMain/LectureMaquettesEdition.cs : MAQUETTE, ses parametres d'edition
// et leurs tables liees, les types que RIG v2 montre a l'agent). Sans serveur, la demonstration ci-dessous reste, marquee « exemple » ;
// en panne, le motif et aucune maquette. Pour une maquette de la base : « Type de document » = le format de son document
// (MQTT_EXTENSION), « Type de maquette » = la colonne Type de RIG v2 (RIG, CRYSTAL…), aucune date (MAQUETTE n'en porte pas) ; la choisir
// remplit le panneau avec son PREMIER parametre d'edition (RIG v2 en ouvre un par etape), et la liste « Parametre » montre les autres.
//
// LES GESTES D'UN PARAMETRE (lot 15, 27/11/2026) : « Enregistrer » et « Supprimer » d'OPE_PARAM_EDT_MAJ (Valider, Supprimer de RIG v2)
// partent au pont (EcranMaquettesEdition.Parametre) puis a Serveur/Socle/EcransMain/EcritureParametresEdition.cs, qui applique les
// controles de RIG v2 et ecrit la base ; ecrit, l'ecran relit les maquettes. Enregistrer envoie les champs CHANGES parmi ceux que la V3
// ecrit (libelle, nombre d'exemplaires, cases ; « Garder edition lot 1 » pour Amitel et une fusion RIG seulement) ; les autres champs
// d'un parametre de la base (listes de RIG v2 non reprises : imprimante, papier, lots, nature ; code, requete, autres parametres,
// entete de la maquette) restent EN LECTURE. Supprimer n'est montre qu'a un agent d'Amitel, comme dans RIG v2. Une maquette REDA
// (Amitel) : formulaire en lecture seule. Sans serveur : la demonstration, aucun bouton de geste.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { EnteteListe, ListeModerne, Pastille, PiedListe, VuesListe, type Teinte } from '../composants/ListeModerne'
import { Annonce, type CompteRendu, type ResultatGesteVue } from './formes/commun'

interface Maquette {
  code: string
  libelle: string
  process: string
  typeDoc: string
  typeMaquette: string
  /** aaaa-mm-jj */
  maj: string
  tables: string[]
  /** Une maquette de la base : l'entete au format plieuse et ses parametres d'edition (Pont/EcranMaquettesEdition.cs). */
  entetePlieuse?: boolean
  parametres?: ParametreEdition[]
}

/** Un parametre d'edition de la base (PARAMETRE_EDITION, les champs d'OPE_PARAM_EDT_MAJ de RIG v2). */
export interface ParametreEdition {
  code: string
  libelle: string
  processus: string
  nature: string
  imprimante: string
  typePapier: string
  lot: string
  lotLrar: string
  lotIlrar: string
  lotSubstitution: string
  nbExemplaires: string
  rectoVerso: boolean
  agrafage: boolean
  afficherAvantImpression: boolean
  docNonModifiable: boolean
  impression: boolean
  garderEditionLot1: boolean
  typeGeneration: string
  requeteSql: string
}

/** Ce que rend le pont (Pont/EcranMaquettesEdition.cs, VueMaquettes) : les maquettes de la base, et d'ou elles viennent. */
export interface VueMaquettes {
  maquettes: Maquette[]
  total: number
  mode?: ModeLecture
  message?: string | null
  /** L'agent est d'Amitel : RIG v2 ne lui ouvre qu'a lui Supprimer un parametre et la case « Garder edition lot 1 ». */
  agentAmitel?: boolean
}

/**
 * Les champs qu'Enregistrer ecrit, sous le titre de leur controle dans RIG v2 (les cles des valeurs du geste,
 * Serveur/Socle/EcransMain/EcritureParametresEdition.cs, Champ*).
 */
export const ChampsParametre = {
  libelle: 'Libellé paramètre édition', nbExemplaires: 'Nb exemplaires', rectoVerso: 'Recto/Verso', agrafage: 'Agrafage',
  afficherAvantImpression: 'Affichage avant impression', docNonModifiable: 'Doc non modifiable', impression: 'Impression',
  garderEditionLot1: 'Garder edition lot 1',
} as const satisfies Partial<Record<keyof ParametreEdition, string>>

/** Les options d'une liste, plus la valeur lue en base si elle n'y est pas (sinon la liste montrerait une autre option). */
function avecValeur(options: string[], valeur: string): string[] {
  return options.includes(valeur) ? options : [valeur, ...options]
}

const Maquettes: Maquette[] = [
  { code: 'MQ_KBIS', libelle: 'Extrait Kbis', process: 'Demandes recues', typeDoc: 'Extrait', typeMaquette: 'Word', maj: '2026-06-12', tables: ['DOSSIER', 'DEPOT', 'CLIENT'] },
  { code: 'MQ_ARCOU', libelle: 'Accuse de reception', process: 'Courrier du jour', typeDoc: 'Courrier', typeMaquette: 'Word', maj: '2026-04-03', tables: ['ARRIVEE', 'CLIENT'] },
  { code: 'MQ_CONVOC', libelle: 'Convocation d\'audience', process: 'Audiences', typeDoc: 'Courrier', typeMaquette: 'Word', maj: '2026-07-22', tables: ['AUDIENCE', 'ACTEUR', 'INSTANCE'] },
  { code: 'MQ_FACT', libelle: 'Facture client', process: 'Factures', typeDoc: 'Facture', typeMaquette: 'Excel', maj: '2026-08-30', tables: ['FACTURE', 'ARTICLE', 'CLIENT'] },
  { code: 'MQ_BORD', libelle: 'Bordereau d\'archivage', process: 'Archivage', typeDoc: 'Bordereau', typeMaquette: 'Word', maj: '2025-12-05', tables: ['DOSSIER'] },
]

// v2 (OPE_PARAM_EDT_MAJ) : les lots proposes sont les memes pour les quatre
// listes ; c'est le canal d'envoi qui change, pas le catalogue.
const LotsDisponibles = [
  '(aucun)', 'Lot 1 - courrier simple', 'Lot 2 - courrier suivi',
  'Lot 3 - recommande', 'Lot 4 - depot guichet',
]

const TeinteTypeDoc: Record<string, Teinte> = { Extrait: 'bleu', Courrier: 'orange', Facture: 'vert', Bordereau: 'ardoise' }

/** Une case de parametre d'edition : la bascule v2 (CheckBox...). */
function Bascule({ actif, basculer, libelle, lecture = false }: { actif: boolean; basculer: () => void; libelle: string; lecture?: boolean }) {
  return (
    <button className={`rig-bascule${actif ? ' rig-bascule--actif' : ''}`} onClick={basculer} disabled={lecture} aria-pressed={actif}>
      <span className="rig-bascule__temoin"></span><span>{libelle}</span>
    </button>
  )
}

/** Une des quatre listes de lot : meme catalogue, canal different. */
function ListeLots({ libelle, valeur, choisir, lecture = false }: { libelle: string; valeur: string; choisir: (v: string) => void; lecture?: boolean }) {
  return (
    <label className="rig-saisie-champ">
      <span className="rig-saisie-champ__libelle">{libelle}</span>
      <select className="rig-saisie-champ__saisie" value={valeur} onChange={(e) => choisir(e.target.value)} disabled={lecture}>
        {avecValeur(LotsDisponibles, valeur).map((l) => <option key={l} value={l}>{l === '' ? '(non renseigné)' : l}</option>)}
      </select>
    </label>
  )
}

export function EcranMaquettesEdition() {
  const [codeProcess, setCodeProcess] = useState('')
  const [typeDoc, setTypeDoc] = useState('')
  const [imprimante, setImprimante] = useState('Greffe - Accueil')
  const [typePapier, setTypePapier] = useState('A4 en-tete')

  const [lot, setLot] = useState('Lot 1 - courrier simple')
  const [lotLrar, setLotLrar] = useState('Lot 3 - recommande')
  const [lotIlrar, setLotIlrar] = useState('Lot 2 - courrier suivi')
  const [lotSubstitution, setLotSubstitution] = useState('(aucun)')
  const [nbExemplaires, setNbExemplaires] = useState('1')

  const [rectoVerso, setRectoVerso] = useState(true)
  const [agrafage, setAgrafage] = useState(false)
  const [apercuAvantImpression, setApercuAvantImpression] = useState(true)
  const [docNonModifiable, setDocNonModifiable] = useState(false)
  const [entetePlieuse, setEntetePlieuse] = useState(false)
  const [impression, setImpression] = useState(true)
  const [garderEditionLot1, setGarderEditionLot1] = useState(false)

  const [codeParamEdt, setCodeParamEdt] = useState('PE-RCS-IMM')
  const [libParamEdt, setLibParamEdt] = useState('Immatriculation - courrier d\'accuse')
  const [natureEdition, setNatureEdition] = useState('Courrier')
  const [requeteSql, setRequeteSql] = useState('SELECT * FROM DEMANDE WHERE DEMAN_ID = @id')
  const [autresParam, setAutresParam] = useState('')
  const [selection, setSelection] = useState<Maquette | null>(Maquettes[0]!)
  // null : la demonstration (pas encore de reponse du pont, ou mode « demo »).
  const [vueBase, setVueBase] = useState<VueMaquettes | null>(null)
  // Le parametre de la base montre dans le panneau, tel que lu : Enregistrer n'envoie que ce qui en differe.
  const [parametre, setParametre] = useState<ParametreEdition | null>(null)
  const [annonce, annoncer] = useState<CompteRendu>(null)
  const [envoi, setEnvoi] = useState(false)

  /** Choisir une maquette ; une maquette de la base remplit le panneau avec un parametre (le premier, ou celui de ce code). */
  function choisir(m: Maquette | null, codeParametre?: string) {
    setSelection(m)
    if (!m || m.parametres === undefined) return
    setEntetePlieuse(m.entetePlieuse === true)
    remplir(m.parametres.find((x) => x.code === codeParametre) ?? m.parametres[0])
  }

  /** Le panneau prend les champs du parametre <p> (aucun : vide). */
  function remplir(p: ParametreEdition | undefined) {
    setParametre(p ?? null)
    setCodeParamEdt(p?.code ?? '')
    setLibParamEdt(p?.libelle ?? '')
    setNatureEdition(p?.nature ?? '')
    setImprimante(p?.imprimante ?? '')
    setTypePapier(p?.typePapier ?? '')
    setLot(p?.lot ?? '')
    setLotLrar(p?.lotLrar ?? '')
    setLotIlrar(p?.lotIlrar ?? '')
    setLotSubstitution(p?.lotSubstitution ?? '')
    setNbExemplaires(p?.nbExemplaires ?? '')
    setRectoVerso(p?.rectoVerso === true)
    setAgrafage(p?.agrafage === true)
    setApercuAvantImpression(p?.afficherAvantImpression === true)
    setDocNonModifiable(p?.docNonModifiable === true)
    setImpression(p?.impression === true)
    setGarderEditionLot1(p?.garderEditionLot1 === true)
    setRequeteSql(p?.requeteSql ?? '')
    setAutresParam('')
  }

  useEffect(() => {
    let perime = false
    void appeler<VueMaquettes>('EcranMaquettesEdition.Maquettes')
      .then((v) => {
        if (perime || v.mode === 'demo' || v.mode === undefined) return
        // La base remplace la demonstration : sa premiere maquette choisie (aucune en panne).
        setVueBase(v)
        choisir(v.maquettes[0] ?? null)
      })
      .catch(() => { /* pas de pont (tests, page hors WebView2) : la demonstration reste */ })
    return () => { perime = true }
  }, [])

  // Un parametre de la base : les champs que la V3 n'ecrit pas sont en lecture ; une maquette REDA l'est entierement (RIG v2,
  // OPE_PARAM_EDT_MAJ.cs:210) ; « Garder edition lot 1 » ne se saisit que pour Amitel et une fusion RIG (:140-141).
  const enBase = vueBase?.mode === 'serveur' && parametre !== null
  const lectureSeule = enBase && selection?.typeMaquette === 'REDA'
  const garderSaisissable = !enBase || (vueBase?.agentAmitel === true && parametre?.typeGeneration === 'FUSION_RIG')

  /** Les champs changes, sous leurs titres de RIG v2 ; vide : rien a enregistrer. */
  function changes(): Record<string, string> {
    if (!parametre) return {}
    const oui = (b: boolean) => (b ? 'Oui' : 'Non')
    const saisis: Record<keyof typeof ChampsParametre, string> = {
      libelle: libParamEdt, nbExemplaires, rectoVerso: oui(rectoVerso), agrafage: oui(agrafage),
      afficherAvantImpression: oui(apercuAvantImpression), docNonModifiable: oui(docNonModifiable), impression: oui(impression),
      garderEditionLot1: oui(garderEditionLot1),
    }
    const lus: Record<keyof typeof ChampsParametre, string> = {
      libelle: parametre.libelle, nbExemplaires: parametre.nbExemplaires, rectoVerso: oui(parametre.rectoVerso),
      agrafage: oui(parametre.agrafage), afficherAvantImpression: oui(parametre.afficherAvantImpression),
      docNonModifiable: oui(parametre.docNonModifiable), impression: oui(parametre.impression),
      garderEditionLot1: oui(parametre.garderEditionLot1),
    }
    const valeurs: Record<string, string> = {}
    for (const cle of Object.keys(ChampsParametre) as (keyof typeof ChampsParametre)[])
      if (saisis[cle] !== lus[cle]) valeurs[ChampsParametre[cle]] = saisis[cle]
    return valeurs
  }
  const aEnregistrer = enBase && Object.keys(changes()).length > 0

  /** Enregistrer ou Supprimer le parametre du panneau ; ecrit, la liste est relue et le meme parametre reste choisi (s'il existe). */
  async function jouer(type: 'Enregistrer' | 'Supprimer') {
    if (!parametre || !selection) return
    const code = parametre.code, maquette = selection.code
    setEnvoi(true)
    try {
      const r = await appeler<ResultatGesteVue>('EcranMaquettesEdition.Parametre', type, code, type === 'Enregistrer' ? changes() : {})
      annoncer(r.fait || r.sansEcrivain ? r.message : { refus: r.message })
      if (r.fait) {
        const v = await appeler<VueMaquettes>('EcranMaquettesEdition.Maquettes')
        setVueBase(v)
        choisir(v.maquettes.find((m) => m.code === maquette) ?? v.maquettes[0] ?? null, code)
      }
    } catch {
      annoncer({ refus: "Le pont n'a pas répondu : rien n'est enregistré." })
    } finally {
      setEnvoi(false)
    }
  }

  const source = vueBase ? vueBase.maquettes : Maquettes

  /** Les maquettes de l'ecran d'origine choisi, pour un type de document (une vue compte les siennes). */
  function trouver(type: string): Maquette[] {
    let q = source
    if (codeProcess !== '') q = q.filter((m) => m.process === codeProcess)
    if (type !== '') q = q.filter((m) => m.typeDoc === type)
    return q
  }
  const trouvees = trouver(typeDoc)

  // « Aller a une maquette… » (Ctrl K) : filtre la liste AFFICHEE, sans toucher aux criteres.
  const [filtreRapide, setFiltreRapide] = useState('')
  const affichees = estBlanc(filtreRapide) ? trouvees
    : trouvees.filter((m) => contientSansCasse(`${m.code} ${m.libelle}`, filtreRapide.trim()))
  const parFormat = distinctsTries(affichees.map((m) => m.typeMaquette))
    .map((f) => [f, affichees.filter((m) => m.typeMaquette === f).length] as const)
  const tablesLiees = affichees.reduce((n, m) => n + m.tables.length, 0)

  const s = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Maquette</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Ecran d'origine</span>
          <select className="rig-saisie-champ__saisie" value={codeProcess} onChange={(e) => setCodeProcess(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(source.map((m) => m.process)).map((p) => <option key={p} value={p}>{p}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type de document</span>
          <select className="rig-saisie-champ__saisie" value={typeDoc} onChange={(e) => setTypeDoc(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(source.map((m) => m.typeDoc)).map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Parametres d'edition</div>
          {s?.parametres !== undefined && s.parametres.length > 1 && (
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Parametre</span>
              <select className="rig-saisie-champ__saisie" value={parametre?.code ?? ''}
                      onChange={(e) => { annoncer(null); remplir(s.parametres!.find((x) => x.code === e.target.value)) }}>
                {s.parametres.map((x) => <option key={x.code} value={x.code}>{x.code}</option>)}
              </select>
            </label>
          )}
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Imprimante</span>
            <select className="rig-saisie-champ__saisie" value={imprimante} onChange={(e) => setImprimante(e.target.value)} disabled={enBase}>
              {avecValeur(['Greffe - Accueil', 'Greffe - Etage 1', 'PDF (fichier)'], imprimante)
                .map((o) => <option key={o} value={o}>{o === '' ? '(non renseignée)' : o}</option>)}
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Type de papier</span>
            <select className="rig-saisie-champ__saisie" value={typePapier} onChange={(e) => setTypePapier(e.target.value)} disabled={enBase}>
              {avecValeur(['A4 blanc', 'A4 en-tete', 'Enveloppe plieuse'], typePapier)
                .map((o) => <option key={o} value={o}>{o === '' ? '(non renseigné)' : o}</option>)}
            </select>
          </label>
          {/* v2 (OPE_PARAM_EDT_MAJ) a QUATRE listes de lot distinctes, pas une
              seule liste a quatre options : ComboBoxLot, ComboBoxLotLRAR,
              ComboBoxLotILRAR et ComboBoxLotSubstitution. Chacune choisit le
              lot a utiliser pour un canal d'envoi different. */}
          <ListeLots libelle="Lot standard" valeur={lot} choisir={setLot} lecture={enBase} />
          <ListeLots libelle="Lot LRAR" valeur={lotLrar} choisir={setLotLrar} lecture={enBase} />
          <ListeLots libelle="Lot iLRAR" valeur={lotIlrar} choisir={setLotIlrar} lecture={enBase} />
          <ListeLots libelle="Lot de substitution" valeur={lotSubstitution} choisir={setLotSubstitution} lecture={enBase} />

          {/* v2 : TxtNbExemplaires. */}
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nombre d'exemplaires</span>
            <input className="rig-saisie-champ__saisie" type="number" min="1" value={nbExemplaires}
                   onChange={(e) => setNbExemplaires(e.target.value)} disabled={lectureSeule} />
          </label>

          <Bascule actif={rectoVerso} basculer={() => setRectoVerso(!rectoVerso)} libelle="Recto-verso" lecture={lectureSeule} />
          <Bascule actif={agrafage} basculer={() => setAgrafage(!agrafage)} libelle="Agrafage" lecture={lectureSeule} />
          <Bascule actif={apercuAvantImpression} basculer={() => setApercuAvantImpression(!apercuAvantImpression)}
                   libelle="Afficher avant impression" lecture={lectureSeule} />
          <Bascule actif={docNonModifiable} basculer={() => setDocNonModifiable(!docNonModifiable)}
                   libelle="Document non modifiable" lecture={lectureSeule} />
          <Bascule actif={entetePlieuse} basculer={() => setEntetePlieuse(!entetePlieuse)}
                   libelle="Entete au format plieuse" lecture={enBase} />

          {/* v2 : CheckBoxImpression et ult_chkGarderEditionLot1. */}
          <Bascule actif={impression} basculer={() => setImpression(!impression)} libelle="Impression" lecture={lectureSeule} />
          <Bascule actif={garderEditionLot1} basculer={() => setGarderEditionLot1(!garderEditionLot1)}
                   libelle="Garder l'edition en lot 1" lecture={lectureSeule || !garderSaisissable} />

          {/* v2 : TxtBoxCodeParamEdt, TxtBoxLibParamEdt, ultComboNatureEdition,
              ult_txtRequeteSql, TextMultiLineAutresParam. */}
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Code parametre d'edition</span>
              <input className="rig-saisie-champ__saisie" value={codeParamEdt} onChange={(e) => setCodeParamEdt(e.target.value)} disabled={enBase} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Nature d'edition</span>
              <select className="rig-saisie-champ__saisie" value={natureEdition} onChange={(e) => setNatureEdition(e.target.value)} disabled={enBase}>
                {avecValeur(['Courrier', 'Acte', 'Bordereau', 'Etat'], natureEdition)
                  .map((o) => <option key={o} value={o}>{o === '' ? '(non renseignée)' : o}</option>)}
              </select>
            </label>
          </div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Libelle du parametre</span>
            <input className="rig-saisie-champ__saisie" value={libParamEdt} onChange={(e) => setLibParamEdt(e.target.value)} disabled={lectureSeule} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Requete SQL</span>
            <textarea className="rig-saisie-champ__saisie" rows={2} value={requeteSql} onChange={(e) => setRequeteSql(e.target.value)} disabled={enBase} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Autres parametres</span>
            <textarea className="rig-saisie-champ__saisie" rows={2} value={autresParam} onChange={(e) => setAutresParam(e.target.value)} disabled={enBase} />
          </label>
          {enBase && (
            <div className="rig-maq-gestes">
              <p className="rig-eg-note">
                {lectureSeule ? 'Maquette REDA : ses paramètres se lisent sans se modifier, comme dans RIG v2.'
                  : 'Enregistrer écrit le libellé, le nombre d’exemplaires et les cases. Imprimante, papier, lots, nature, code, requête '
                    + 'et autres paramètres restent en lecture : leurs listes de RIG v2 ne sont pas encore reprises.'}
              </p>
              <button className="rig-bouton rig-bouton--fort" onClick={() => void jouer('Enregistrer')}
                      disabled={envoi || lectureSeule || !aEnregistrer}
                      title={aEnregistrer ? undefined : 'Aucun champ n’a changé.'}>Enregistrer</button>
              {vueBase?.agentAmitel === true && (
                <button className="rig-bouton" onClick={() => void jouer('Supprimer')} disabled={envoi}>Supprimer</button>
              )}
              <Annonce texte={annonce} />
            </div>
          )}
        </div>
      </aside>

      <ListeModerne etiquette="Maquettes">
        <EnteteListe titre="Maquettes"
                     synthese={<>{affichees.length}{affichees.length > 1 ? ' maquettes' : ' maquette'}
                       {parFormat.map(([format, n]) => <span key={format}>{' · '}<b>{n}</b>{' '}{format}</span>)}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une maquette…',
                               valider: () => { if (affichees.length === 1) choisir(affichees[0]!) } }} />
        {vueBase ? <ProvenanceEcran mode={vueBase.mode} message={vueBase.message} quoi="Maquettes" />
          : <ProvenanceEcran mode="demo" quoi="Maquettes" />}

        <VuesListe vues={[{ cle: '', libelle: 'Toutes', compte: trouver('').length },
                          ...distinctsTries(source.map((m) => m.typeDoc)).map((t) => ({ cle: t, libelle: t, compte: trouver(t).length }))]}
                   active={typeDoc} choisir={setTypeDoc} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: '13%' }} /><col /><col style={{ width: '16%' }} /><col style={{ width: '15%' }} />
              <col style={{ width: '12%' }} /><col style={{ width: '13%' }} /><col style={{ width: '9%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>Maquette</th>
                <th>Libelle</th>
                <th>Ecran d'origine</th>
                <th>Type de document</th>
                <th>Type de maquette</th>
                <th>Derniere mise a jour</th>
                <th className="rig-table__centre">Tables liees</th>
              </tr>
            </thead>
            <tbody>
              {affichees.map((maq) => (
                <tr key={maq.code} onClick={() => choisir(maq)}>
                  <td className="rig-table__mono">{maq.code}</td>
                  <td title={maq.libelle}>{maq.libelle}</td>
                  <td title={maq.process}>{maq.process}</td>
                  <td><Pastille teinte={TeinteTypeDoc[maq.typeDoc] ?? 'gris'}>{maq.typeDoc}</Pastille></td>
                  <td>{maq.typeMaquette}</td>
                  <td className="rig-table__mono">{dateCourte(maq.maj)}</td>
                  <td className="rig-table__centre">{maq.tables.length}</td>
                </tr>
              ))}
              {trouvees.length === 0 && (
                <tr><td colSpan={7} className="rig-table__vide">Aucune maquette pour ces criteres.</td></tr>
              )}
              {trouvees.length > 0 && affichees.length === 0 && (
                <tr><td colSpan={7} className="rig-table__vide">
                  {`Aucune des ${trouvees.length} maquettes ne contient « ${filtreRapide.trim()} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        {s && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-kicker">Tables liees a {s.code}</div>
            <div>
              {s.tables.map((t) => <span key={t} className="rig-chip">{t}</span>)}
            </div>
            {/* Le blanc entre le lot et les options vient du saut de ligne du
                .razor : sans lui, « recto-verso » collerait au lot. */}
            <p className="rig-nonmaq__texte rig-nonmaq__texte--sourd">
              Edition : {imprimante} — {typePapier} — {lot}{' '}{rectoVerso ? ' — recto-verso' : ''}{agrafage ? ' — agrafe' : ''}
            </p>
            {s.parametres !== undefined && (
              <p className="rig-nonmaq__texte rig-nonmaq__texte--sourd rig-maq-parametres">
                {s.parametres.length === 0 ? "Aucun parametre d'edition"
                  : "Parametres d'edition : " + s.parametres.map((p) => p.code).join(', ')}
              </p>
            )}
          </div>
        )}

        <PiedListe>
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' maquettes affichees' : ' maquette affichee'}</span>
          <span>Tables liees <b>{tablesLiees}</b></span>
          {vueBase && <span>Fichier <b>{vueBase.total}</b></span>}
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
