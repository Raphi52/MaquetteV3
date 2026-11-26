// Ecran de la procedure PROC_RECHAFF — recherche d'affaires.
//
// Reference RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_RECHAFF) :
//   - Operations/OPE_CRITERES.cs : n° affaire, n° demande, n° facture, dates
//   - Ults/ULT_GRIDVIEW_RECHAFF.cs : colonnes N° Affaire, Libelle,
//     Date creation, Solde, Demandes, Factures.
//
// Les criteres d'INSTANCE (id instance, n° de role, code affaire, nom
// instance ou partie, etat, fenetre de saisine) et le panneau de suppression
// XXAFF viennent de l'ecran « Instances judiciaires » : la recherche
// d'affaire doit couvrir ces champs sans obliger a changer d'ecran.
//
// Deux differences ASSUMEES avec v2, parce que ce n'est pas un portage :
//   - aucun rattachement de demande a l'affaire. L'ecran ne fait que LIRE.
//   - noAffaire pre-remplit le critere et lance la recherche (tuile « Mes
//     affaires recentes »).
//
// Porte depuis Components/Ecrans/EcranRechAffaires.razor (lot 3). Le fonds d'affaires, le filtre et le TRI
// (ordre des chaines .NET) sont faits par Pont/EcranRechAffaires.cs ; ici restent les criteres saisis, la
// colonne de tri choisie, la selection et la suppression simulee.
//
// L'ANCIEN RIG (SOC-014, SOC-015 ; lot 3 de la phase 2, Pont/AncienRig.cs) : une affaire creee avant le
// passage du greffe a la V3 porte la mention « traitee dans la V2 » ; choisie, elle propose « Ouvrir dans
// l'ancien RIG » (la V2 du greffe, lancee ou ramenee, sur sa recherche d'affaires, numero copie), et sa
// suppression est refusee ici (elle se fait dans l'ancien RIG). Le double-clic l'ouvre toujours dans la
// visionneuse : la consulter dans la V3 reste permis. L'aiguillage est demande au pont quand la LISTE des
// numeros change (pas a chaque tri) ; s'il ne repond pas, l'ecran reste tel qu'avant ce lot.
// Lot 11 (REC-010 ; SOC-015 CA01 laisse ouvert par le lot 3, decision du chef) : le bloc propose aussi « Voir son historique
// (lecture seule) » -- ses affaires et demandes lues par le pont de l'historique (Pont/EcranHistoriqueV2.cs).
//
// LISTE MODERNE « Tout reuni » (24/11/2026, /draft conv-2132, plan des listes modernes, lot 1) : la colonne
// des resultats prend les briques de composants/ListeModerne.tsx. Tete (synthese, « Aller a une affaire… »
// Ctrl K qui filtre la liste affichee, Entree ouvre l'affaire si une seule reste), trois cartes qui POSENT les
// criteres existants (nature, etat « En cours », domaine), des vues = jeux de criteres (dont « Solde > 100 € »,
// critere critSoldeMin ajoute au pont), « Trier » sur les 13 cles du pont, export CSV. Le tableau passe a UNE
// ligne par affaire (Affaire, Nature, Etat, Saisine, Solde) ; les six autres colonnes sont dans la ligne
// depliee par le chevron, avec Ouvrir et Historique. Les criteres de gauche, la selection, le double-clic
// (visionneuse), le bloc « Ancien RIG » et la suppression XXAFF ne changent pas.
// « + Nouvelle vue » garde ses vues pour la SESSION (module, pas de reglage) : accueil.json n'a aucun magasin
// de reglages d'ecran ou les poser (AccueilEtat.cs, 24/11/2026).
//
// LA BASE DU GREFFE (plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026, marche du lot 17) : « EcranRechAffaires.Trouver » et
// « .Compter » sont servis par Pont/EcranRechAffairesBase.cs → Serveur/Socle/EcransMain/LectureAffaires.cs : la base cherche
// les affaires (AFFAIRE) par numero (d'affaire, de demande, de facture), libelle ou SIREN, et par leur instance ; solde de la V2
// (ventilations moins articles utilises), demandes et factures dans la ligne depliee. Type, nature, etat, solde minimal et tri
// restent ceux du pont. Sans critere, RIG v2 ne cherche rien : aucune ligne ; une note dit ce que la liste montre. Sans serveur,
// la demonstration, marquee « exemple » ; en panne, le motif et aucune ligne.

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { EnteteTriable } from '../composants/EnteteTriable'
import {
  CartesChiffrees, CelluleChevron, CelluleIdentite, ChoixTri, EnteteListe, LigneDetail, ListeModerne, MenuLigne, MontantBarre,
  Pastille, PiedListe, PointEtat, VuesListe, exporterCsv, montant, normaliser, useVuesDeSession, type ActionLigne, type Teinte, type VueEnregistree,
} from '../composants/ListeModerne'
import { dateSaisie } from '../saisie'
import { toucheDeRecherche } from '../plan/clavier'
import type { VueEcranHistorique } from './EcranHistoriqueV2'

export interface NatureAffaire {
  code: string
  nom: string
}

export interface LigneAffaire {
  numero: string
  idInstance: string
  role: string
  codeAffaire: string
  nature: string
  nomNature: string
  libelle: string
  nom: string
  etat: string
  siren: string
  type: string
  saisine: string
  creation: string
  /** Le solde deja mis en forme (« 142,50 »), comme le ToString("N2") de Blazor. */
  solde: string
  /** Le meme solde en nombre : totaux, carte « Solde total », mini-barre. */
  soldeValeur: number
  /** Lues en base (lot 6) : les numeros des demandes et de leurs factures, joints par « ; » (grille de PROC_RECHAFF). */
  demandes?: string
  factures?: string
}

export interface AffaireChoisie {
  numero: string
  idInstance: string
  role: string
}

export interface VueAffaires {
  natures: NatureAffaire[]
  etats: string[]
  lignes: LigneAffaire[]
  /** « 7 instance, 3 procedure collective, … » */
  resume: string
  aucunCritere: boolean
  sirenCherche: string
  choisie: AffaireChoisie | null
  /** D'ou viennent les lignes (Pont/LectureEcranMain.cs) ; absent : la demonstration d'avant le lot 6. */
  mode?: ModeLecture
  /** En base : ce que la liste montre au juste ; en panne : le motif. */
  message?: string | null
}

/** Le logiciel d'une affaire, dit par l'aiguillage (Pont/AncienRig.cs, LigneAiguillee). */
export interface LigneAiguillee {
  numero: string
  /** « V2 » : creee avant le passage du greffe, traitee dans l'ancien RIG ; « V3 » sinon. */
  logiciel: string
  motif: string
}

export interface VueAiguillage {
  mode: string
  greffe: string | null
  passage: string | null
  message: string | null
  affaires: LigneAiguillee[]
}

/** Le compte rendu de « Ouvrir dans l'ancien RIG » (Pont/AncienRig.cs, ResultatAncienRig). */
export interface ResultatAncienRig {
  fait: boolean
  geste: string
  numero: string
  message: string
  ligneDeCommande: string | null
}

export interface ProprietesEcranRechAffaires {
  /** Numero d'affaire pre-rempli a l'ouverture (tuile de l'accueil). */
  noAffaire?: string | null
  /** Remonte le numero choisi ; la page ouvre la visionneuse. */
  ouvrir: (numero: string) => void
  /**
   * Remonte un SIREN a immatriculer : la recherche n'a rien trouve, c'est donc
   * ICI que l'agent constate qu'il faut ouvrir le dossier. Le renvoyer au menu
   * lui ferait retaper le numero.
   */
  ouvrirUnDossier: (siren: string) => void
}

/** Les cles de tri comprises par le pont, avec leur libelle : la liste « Trier » et l'export CSV. */
const Colonnes: ReadonlyArray<readonly [libelle: string, cle: string]> = [
  ['N° Affaire', 'Numero'],
  ['Id instance', 'IdInstance'],
  ['N° de role', 'Role'],
  ['Code affaire', 'CodeAffaire'],
  ['Nature', 'Nature'],
  ['Libelle', 'Libelle'],
  ['Nom', 'Nom'],
  ['Etat', 'Etat'],
  ['SIREN', 'Siren'],
  ['Domaine', 'Type'],
  ['Saisine', 'Saisine'],
  ['Date creation', 'Creation'],
  ['Solde', 'Solde'],
]

/** Les colonnes du tableau a une ligne : chevron, Affaire, Nature, Etat, Saisine, Solde, menu. */
const NbColonnes = 7

/** Les criteres de gauche, tels que saisis (dates au format du champ). Une vue en est un jeu complet. */
export interface CriteresSaisis {
  critNumero: string
  critLibelle: string
  critType: string
  critNature: string
  critIdInstance: string
  critRole: string
  critCodeAffaire: string
  critNom: string
  critEtat: string
  critSaisineDu: string
  critSaisineAu: string
  critSoldeMin: string
}

const SansCritere: CriteresSaisis = {
  critNumero: '', critLibelle: '', critType: '', critNature: '', critIdInstance: '', critRole: '',
  critCodeAffaire: '', critNom: '', critEtat: '', critSaisineDu: '', critSaisineAu: '', critSoldeMin: '',
}

/** Ce que le pont recoit : les dates vides deviennent null (Pont/EcranRechAffaires.cs, CriteresAffaires). */
function pourLePont(c: CriteresSaisis) {
  return {
    critNumero: c.critNumero, critLibelle: c.critLibelle, critType: c.critType, critNature: c.critNature,
    critIdInstance: c.critIdInstance, critRole: c.critRole, critCodeAffaire: c.critCodeAffaire,
    critNom: c.critNom, critEtat: c.critEtat, critSaisineDu: dateSaisie(c.critSaisineDu),
    critSaisineAu: dateSaisie(c.critSaisineAu), critSoldeMin: c.critSoldeMin,
  }
}

function memesCriteres(a: CriteresSaisis, b: CriteresSaisis): boolean {
  return (Object.keys(SansCritere) as (keyof CriteresSaisis)[]).every((k) => a[k] === b[k])
}

/** Les vues de la maquette, toujours la. */
export const VuesPrereglees: VueEnregistree<CriteresSaisis>[] = [
  { cle: 'toutes', libelle: 'Toutes', criteres: SansCritere },
  { cle: 'collectives-en-cours', libelle: 'Proc. collectives en cours',
    criteres: { ...SansCritere, critNature: 'collective', critEtat: 'En cours' } },
  { cle: 'solde-100', libelle: 'Solde > 100 €', criteres: { ...SansCritere, critSoldeMin: '100' } },
  { cle: 'cloturees', libelle: 'Cloturees', criteres: { ...SansCritere, critEtat: 'Cloturee' } },
]

const TeinteNature: Record<string, Teinte> = { instance: 'bleu', collective: 'orange', prevention: 'ardoise', mesure: 'vert' }
const TeinteEtat: Record<string, Teinte> = { 'En cours': 'bleu', Radiee: 'rouge', Cloturee: 'gris' }

/** L'objet de l'affaire : son libelle avant « - » (« Modification - SCI DU PARC » -> « Modification »). */
export function objetDe(affaire: Pick<LigneAffaire, 'libelle' | 'nom'>): string {
  const tiret = affaire.libelle.indexOf(' - ')
  const objet = tiret >= 0 ? affaire.libelle.slice(0, tiret) : affaire.libelle
  return objet === affaire.nom ? '' : objet
}

/** L'historique d'une affaire, en une phrase (pont de l'historique de l'ancien RIG). */
function texteHistorique(h: VueEcranHistorique, numero: string): string {
  return h.historique
    ? `${h.historique.titre ?? numero} : ` + (h.historique.affaires.length === 0 ? 'aucune affaire trouvée'
        : h.historique.affaires.map((a) => `${a.libelle ?? a.id} (${a.marque}, ${a.etat}, ${a.demandes.length} demande(s) : `
            + a.demandes.map((d) => `${d.numero ?? d.id} ${d.etatLibelle}`).join(', ') + ')').join(' ; '))
    : h.message ?? ''
}

export function EcranRechAffaires({ noAffaire, ouvrir, ouvrirUnDossier }: ProprietesEcranRechAffaires) {
  // Le critere n'est pose qu'a l'OUVERTURE : ensuite l'utilisateur est maitre
  // de ses criteres, un nouveau rendu ne doit pas les ecraser.
  const [critNumero, setCritNumero] = useState(() => (noAffaire ?? '').trim())
  const [critLibelle, setCritLibelle] = useState('')
  const [critNature, setCritNature] = useState('')
  const [critType, setCritType] = useState('')
  const [critIdInstance, setCritIdInstance] = useState('')
  const [critRole, setCritRole] = useState('')
  const [critCodeAffaire, setCritCodeAffaire] = useState('')
  const [critEtat, setCritEtat] = useState('')
  const [critNom, setCritNom] = useState('')
  const [critSaisineDu, setCritSaisineDu] = useState('')
  const [critSaisineAu, setCritSaisineAu] = useState('')
  const [critSoldeMin, setCritSoldeMin] = useState('')

  const criteres: CriteresSaisis = {
    critNumero, critLibelle, critType, critNature, critIdInstance, critRole, critCodeAffaire,
    critNom, critEtat, critSaisineDu, critSaisineAu, critSoldeMin,
  }

  /** Pose un jeu complet de criteres : une vue choisie. */
  function poserCriteres(c: CriteresSaisis) {
    setCritNumero(c.critNumero)
    setCritLibelle(c.critLibelle)
    setCritType(c.critType)
    setCritNature(c.critNature)
    setCritIdInstance(c.critIdInstance)
    setCritRole(c.critRole)
    setCritCodeAffaire(c.critCodeAffaire)
    setCritNom(c.critNom)
    setCritEtat(c.critEtat)
    setCritSaisineDu(c.critSaisineDu)
    setCritSaisineAu(c.critSaisineAu)
    setCritSoldeMin(c.critSoldeMin)
  }

  const [selection, setSelection] = useState<string | null>(null)
  // La ligne depliee par son chevron (une a la fois).
  const [ouverte, setOuverte] = useState<string | null>(null)
  const [libererNumeros, setLibererNumeros] = useState(true)
  const [message, setMessage] = useState<string | null>(null)

  // La colonne sur laquelle la grille est rangee ; null = ordre d'origine
  // (du plus recent au plus ancien).
  const [cleTri, setCleTri] = useState<string | null>(null)
  const [triAscendant, setTriAscendant] = useState(true)

  // null tant que le pont n'a pas repondu : ni ligne ni « aucune affaire ».
  const [vue, setVue] = useState<VueAffaires | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueAffaires>('EcranRechAffaires.Trouver', pourLePont({
      critNumero, critLibelle, critType, critNature, critIdInstance, critRole, critCodeAffaire,
      critNom, critEtat, critSaisineDu, critSaisineAu, critSoldeMin,
    }), cleTri, triAscendant, selection).then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [critNumero, critLibelle, critType, critNature, critIdInstance, critRole, critCodeAffaire,
      critNom, critEtat, critSaisineDu, critSaisineAu, critSoldeMin, cleTri, triAscendant, selection])

  const trouvees = vue?.lignes ?? []
  const choisie = vue?.choisie ?? null

  // « Aller a une affaire… » (Ctrl K) : filtre la liste AFFICHEE, sans toucher aux criteres du pont.
  const [filtreRapide, setFiltreRapide] = useState('')
  const cherche = normaliser(filtreRapide.trim())
  const affichees = cherche === '' ? trouvees : trouvees.filter((a) => normaliser(
    [a.numero, a.nom, a.libelle, a.siren, a.siren.replace(/\s/g, ''), a.idInstance, a.role, a.codeAffaire].join(' ')).includes(cherche))

  // Les vues : celles de la maquette, puis celles de la session ; leur compte vient du pont.
  const { vues, active: vueActive, enregistrer: enregistrerVue } = useVuesDeSession('RechAffaires', VuesPrereglees, criteres, memesCriteres)
  // Un compte null (lu en base, vue sans critere cherche) : la puce n'en montre aucun.
  const [comptes, setComptes] = useState<(number | null)[] | null>(null)
  const cleVues = JSON.stringify(vues.map((v) => v.criteres))
  useEffect(() => {
    let perime = false
    const jeux = (JSON.parse(cleVues) as CriteresSaisis[]).map(pourLePont)
    appeler<(number | null)[]>('EcranRechAffaires.Compter', jeux).then(
      (c) => { if (!perime) setComptes(c) },
      // Sans reponse, les puces restent sans compte.
      () => { if (!perime) setComptes(null) })
    return () => { perime = true }
  }, [cleVues])

  // Le logiciel de chaque affaire affichee, par numero. La cle est l'ENSEMBLE des numeros : un tri ne change
  // rien, donc ne redemande rien.
  const [aiguillage, setAiguillage] = useState<Record<string, LigneAiguillee>>({})
  const [ancienRig, setAncienRig] = useState<ResultatAncienRig | null>(null)
  // SOC-015 CA01 (lot 11, REC-010) : l'historique d'une ancienne affaire, en lecture, par le pont de l'historique de l'ancien RIG.
  const [historique, setHistorique] = useState<VueEcranHistorique | null>(null)
  function voirHistorique(numero: string) {
    void appeler<VueEcranHistorique>('EcranRechAffaires.HistoriqueAncienne', numero).then((v) => setHistorique(v), () => setHistorique(null))
  }
  // « Historique » de la ligne depliee : le meme pont, pour toute affaire (il lit les deux logiciels), dit dans la ligne.
  const [historiqueLigne, setHistoriqueLigne] = useState<{ numero: string; texte: string } | null>(null)
  function voirHistoriqueLigne(numero: string) {
    setOuverte(numero)
    setHistoriqueLigne({ numero, texte: 'Lecture de l\'historique…' })
    void appeler<VueEcranHistorique>('EcranRechAffaires.HistoriqueAncienne', numero).then(
      (v) => setHistoriqueLigne({ numero, texte: texteHistorique(v, numero) }),
      (e: unknown) => setHistoriqueLigne({ numero, texte: `L'historique n'a pas pu etre lu : ${e instanceof Error ? e.message : String(e)}` }))
  }
  const cleNumeros = [...new Set(trouvees.map((a) => a.numero))].sort().join('\n')
  useEffect(() => {
    let perime = false
    if (cleNumeros === '') { setAiguillage({}); return }
    appeler<VueAiguillage>('EcranRechAffaires.Aiguiller', cleNumeros.split('\n')).then(
      (v) => { if (!perime) setAiguillage(Object.fromEntries(v.affaires.map((a) => [a.numero, a]))) },
      // Sans reponse, aucune mention : l'ecran reste celui d'avant l'aiguillage.
      () => { if (!perime) setAiguillage({}) })
    return () => { perime = true }
  }, [cleNumeros])

  /** L'affaire choisie se traite-t-elle dans l'ancien RIG ? Alors son aiguillage, sinon null. */
  const choisieV2 = choisie !== null && aiguillage[choisie.numero]?.logiciel === 'V2' ? aiguillage[choisie.numero]! : null

  /** SOC-015 CA02, CA03 : la V2 du greffe, lancee ou ramenee, numero copie. */
  function ouvrirDansAncienRig(numero: string) {
    void appeler<ResultatAncienRig>('EcranRechAffaires.OuvrirDansAncienRig', numero).then(
      (r) => setAncienRig(r),
      (e: unknown) => setAncienRig({ fait: false, geste: 'refuse', numero, ligneDeCommande: null,
                                     message: `L'ancien RIG n'a pas pu etre ouvert : ${e instanceof Error ? e.message : String(e)}` }))
  }

  /** Premier clic : ordre croissant. Second clic sur la MEME colonne : ordre inverse. C'est le geste de RIG v2. */
  function basculerTri(cle: string) {
    if (cleTri === cle) setTriAscendant(!triAscendant)
    else { setCleTri(cle); setTriAscendant(true) }
  }

  /** Le filtrage est continu ; le bouton efface seulement le compte rendu. */
  function rechercher() {
    setMessage(null)
  }

  function vider() {
    poserCriteres(SansCritere)
    setSelection(null)
    setMessage(null)
  }

  /**
   * Ecran de demonstration : la suppression XXAFF n'ecrit rien, elle
   * rend compte de ce qui serait supprime et du sort des numeros metiers.
   */
  function supprimer() {
    if (choisie === null) return
    // SOC-015 CA04 : une ancienne affaire ne se modifie pas dans la V3 ; le bloc « Ancien RIG » propose de l'y ouvrir.
    if (choisieV2 !== null) {
      setMessage(`Instance ${choisie.idInstance} (${choisie.numero}) : affaire traitee dans la V2, elle ne se supprime pas ici.`
        + ' Ouvrez-la dans l\'ancien RIG.')
      return
    }
    setMessage(`Instance ${choisie.idInstance} (${choisie.numero}) : suppression simulee`
      + (libererNumeros ? ' — numeros metiers liberes.' : ' — numeros metiers conserves.'))
  }

  function choisirLigne(numero: string) {
    setSelection(numero); setAncienRig(null); setHistorique(null)
  }

  // Les chiffres de la tete, des cartes et du pied : sur la liste AFFICHEE.
  const enCours = affichees.filter((a) => a.etat === 'En cours').length
  const soldeTotal = affichees.reduce((s, a) => s + a.soldeValeur, 0)
  const soldeMax = affichees.reduce((m, a) => Math.max(m, a.soldeValeur), 0)
  const parType = (type: string) => affichees.filter((a) => a.type === type).length
  const natures = vue?.natures ?? []
  const basculer = (actuel: string, valeur: string, poser: (v: string) => void) => () => poser(actuel === valeur ? '' : valeur)

  function exporter() {
    exporterCsv('affaires.csv', Colonnes.map(([libelle]) => libelle), affichees.map((a) => [
      a.numero, a.idInstance, a.role, a.codeAffaire, a.nomNature, a.libelle, a.nom, a.etat, a.siren, a.type,
      a.saisine, a.creation, a.solde]))
  }

  return (
    <div className="rig-proc__corps">

      {/* Entree (ou F5, F7) dans le bloc des criteres = clic sur Rechercher. C'est le
          geste de RIG v2 : on tape, on valide, sans lacher le clavier. */}
      <aside className="rig-criteres" onKeyDown={(e) => { if (toucheDeRecherche(e)) rechercher() }}>
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">N° Affaire</span>
          <input className="rig-saisie-champ__saisie" value={critNumero} onChange={(e) => setCritNumero(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Libelle, SIREN</span>
          <input className="rig-saisie-champ__saisie" value={critLibelle} onChange={(e) => setCritLibelle(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nature</span>
          <select className="rig-saisie-champ__saisie" value={critNature} onChange={(e) => setCritNature(e.target.value)}>
            <option value="">Toutes</option>
            {natures.map((n) => (
              <option key={n.code} value={n.code}>{n.nom}</option>
            ))}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Domaine</span>
          <select className="rig-saisie-champ__saisie" value={critType} onChange={(e) => setCritType(e.target.value)}>
            <option value="">Tous</option>
            <option value="RCS">RCS</option>
            <option value="JUD">Judiciaire</option>
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Solde superieur a (€)</span>
          <input className="rig-saisie-champ__saisie" inputMode="decimal" placeholder="ex. 100"
                 value={critSoldeMin} onChange={(e) => setCritSoldeMin(e.target.value)} />
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Instance judiciaire</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Id instance</span>
              <input className="rig-saisie-champ__saisie" placeholder="ex. 55339"
                     value={critIdInstance} onChange={(e) => setCritIdInstance(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Numero de role</span>
              <input className="rig-saisie-champ__saisie" placeholder="ex. 2024F00123"
                     value={critRole} onChange={(e) => setCritRole(e.target.value)} />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Code affaire</span>
              <input className="rig-saisie-champ__saisie" placeholder="ex. 010"
                     value={critCodeAffaire} onChange={(e) => setCritCodeAffaire(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Etat</span>
              <select className="rig-saisie-champ__saisie" value={critEtat} onChange={(e) => setCritEtat(e.target.value)}>
                <option value="">Tous</option>
                {(vue?.etats ?? []).map((e) => (
                  <option key={e} value={e}>{e}</option>
                ))}
              </select>
            </label>
          </div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nom (instance ou partie)</span>
            <input className="rig-saisie-champ__saisie" placeholder="ex. DUPONT"
                   value={critNom} onChange={(e) => setCritNom(e.target.value)} />
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Saisine</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date"
                     value={critSaisineDu} onChange={(e) => setCritSaisineDu(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date"
                     value={critSaisineAu} onChange={(e) => setCritSaisineAu(e.target.value)} />
            </label>
          </div>
        </div>

        <div className="rig-criteres__actions">
          <div className="rig-criteres__paire">
            <button className="rig-bouton rig-bouton--fort" onClick={rechercher}>Rechercher</button>
            <button className="rig-bouton" onClick={vider}>Reinitialiser</button>
          </div>
        </div>
      </aside>

      <ListeModerne etiquette="Affaires">
        <EnteteListe titre="Affaires" detail={vue?.resume}
                     synthese={vue && <>{affichees.length}{affichees.length > 1 ? ' resultats · ' : ' resultat · '}<b>{enCours}</b>
                       {' en cours · solde '}<b>{montant(soldeTotal)}</b></>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une affaire…',
                               valider: () => { if (affichees.length === 1) ouvrir(affichees[0]!.numero) } }} />

        {vue && <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Affaires" />}
        {vue?.mode === 'serveur' && vue.message && <p className="rig-eg-note" role="note">{vue.message}</p>}

        {vue && (
          <CartesChiffrees cartes={[
            { cle: 'solde', titre: 'Solde total', valeur: montant(soldeTotal), legende: true,
              ...(critNature !== '' ? { choisir: () => setCritNature(''), indication: 'Toutes les natures' } : {}),
              segments: natures.map((n) => ({
                cle: n.code, libelle: n.nom.split(' ')[0] ?? n.nom, teinte: TeinteNature[n.code] ?? 'gris',
                valeur: affichees.filter((a) => a.nature === n.code).reduce((s, a) => s + a.soldeValeur, 0),
                texte: montant(affichees.filter((a) => a.nature === n.code).reduce((s, a) => s + a.soldeValeur, 0)),
                actif: critNature === n.code, choisir: basculer(critNature, n.code, setCritNature),
              })) },
            { cle: 'en-cours', titre: 'En cours', valeur: enCours, complement: `sur ${affichees.length}`,
              anneau: { part: enCours, total: affichees.length, teinte: 'bleu' },
              actif: critEtat === 'En cours', choisir: basculer(critEtat, 'En cours', setCritEtat),
              indication: critEtat === 'En cours' ? 'Tous les etats' : 'Seulement les affaires en cours' },
            { cle: 'domaine', titre: 'Domaine', valeur: <>RCS {parType('RCS')} <small>·</small> JUD {parType('JUD')}</>,
              ...(critType !== '' ? { choisir: () => setCritType(''), indication: 'Tous les domaines' } : {}),
              segments: [
                { cle: 'RCS', libelle: 'RCS', valeur: parType('RCS'), teinte: 'bleu', actif: critType === 'RCS',
                  choisir: basculer(critType, 'RCS', setCritType) },
                { cle: 'JUD', libelle: 'JUD', valeur: parType('JUD'), teinte: 'ardoise', actif: critType === 'JUD',
                  choisir: basculer(critType, 'JUD', setCritType) },
              ] },
          ]} />
        )}

        <VuesListe vues={vues.map((v, i) => ({ cle: v.cle, libelle: v.libelle, compte: comptes?.[i] ?? undefined }))} active={vueActive}
                   choisir={(cle) => { const v = vues.find((x) => x.cle === cle); if (v) poserCriteres(v.criteres) }}
                   enregistrer={enregistrerVue}>
          <ChoixTri cles={Colonnes} cleActive={cleTri} ascendant={triAscendant} origine="Plus recentes"
                    choisir={(cle) => { setCleTri(cle); setTriAscendant(true) }} inverser={() => setTriAscendant(!triAscendant)} />
          <button type="button" className="rig-lm-outil" disabled={affichees.length === 0} onClick={exporter}>Exporter</button>
        </VuesListe>

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col /><col style={{ width: '21%' }} /><col style={{ width: '13%' }} />
              <col style={{ width: '13%' }} /><col style={{ width: '18%' }} /><col style={{ width: 34 }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <EnteteTriable libelle="Affaire" cle="Nom" cleActive={cleTri} ascendant={triAscendant} trier={basculerTri} />
                <EnteteTriable libelle="Nature" cle="Nature" cleActive={cleTri} ascendant={triAscendant} trier={basculerTri} />
                <EnteteTriable libelle="Etat" cle="Etat" cleActive={cleTri} ascendant={triAscendant} trier={basculerTri} />
                <EnteteTriable libelle="Saisine" cle="Saisine" cleActive={cleTri} ascendant={triAscendant} trier={basculerTri} />
                <EnteteTriable libelle="Solde" cle="Solde" classe="rig-lm-droite" cleActive={cleTri} ascendant={triAscendant} trier={basculerTri} />
                <th aria-label="Actions"></th>
              </tr>
            </thead>
            <tbody>
              {affichees.map((affaire, i) => {
                const v2 = aiguillage[affaire.numero]?.logiciel === 'V2' ? aiguillage[affaire.numero]! : null
                const estOuverte = ouverte === affaire.numero
                const objet = objetDe(affaire)
                const actions: ActionLigne[] = [
                  { libelle: 'Ouvrir', fort: true, agir: () => ouvrir(affaire.numero) },
                  { libelle: 'Historique', agir: () => voirHistoriqueLigne(affaire.numero) },
                ]
                const classes = [affaire.numero === selection ? 'rig-ligne--valide' : '', estOuverte ? 'rig-lm-ouverte' : ''].filter(Boolean).join(' ')
                return (
                  <Fragment key={`${i}:${affaire.numero}`}>
                  <tr className={classes || undefined}
                      onClick={() => choisirLigne(affaire.numero)}
                      onDoubleClick={() => ouvrir(affaire.numero)}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : affaire.numero)} />
                    <CelluleIdentite nom={affaire.nom} titre={`${affaire.nom} — ${affaire.libelle} · ${affaire.numero}`}
                                     objet={<>{objet}{objet !== '' ? ' · ' : ''}<span>{affaire.numero}</span></>}
                                     apres={v2 && <span className="rig-badge" title={v2.motif} style={{ whiteSpace: 'nowrap' }}>traitee dans la V2</span>} />
                    <td title={affaire.nomNature}><Pastille teinte={TeinteNature[affaire.nature] ?? 'gris'}>{affaire.nomNature}</Pastille></td>
                    <td title={affaire.etat}><PointEtat teinte={TeinteEtat[affaire.etat] ?? 'gris'}>{affaire.etat}</PointEtat></td>
                    <td>{affaire.saisine}</td>
                    <td className="rig-lm-droite" title={`${affaire.solde} €`}>
                      <MontantBarre valeur={affaire.soldeValeur} max={soldeMax} texte={`${affaire.solde} €`} />
                    </td>
                    <td className="rig-lm-cellule-menu">
                      <MenuLigne libelle={`Actions de l'affaire ${affaire.numero}`} actions={[
                        ...actions,
                        { libelle: estOuverte ? 'Replier la ligne' : 'Deplier la ligne', agir: () => setOuverte(estOuverte ? null : affaire.numero) },
                        ...(v2 ? [{ libelle: 'Ouvrir dans l\'ancien RIG', agir: () => { choisirLigne(affaire.numero); ouvrirDansAncienRig(affaire.numero) } }] : []),
                      ]} />
                    </td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes} actions={actions} champs={[
                      { libelle: 'Id instance', valeur: affaire.idInstance },
                      { libelle: 'N° de role', valeur: affaire.role },
                      { libelle: 'Code affaire', valeur: affaire.codeAffaire },
                      { libelle: 'SIREN', valeur: affaire.siren },
                      { libelle: 'Domaine', valeur: affaire.type },
                      { libelle: 'Date creation', valeur: affaire.creation },
                      ...(affaire.demandes ? [{ libelle: 'Demandes', valeur: affaire.demandes }] : []),
                      ...(affaire.factures ? [{ libelle: 'Factures', valeur: affaire.factures }] : []),
                    ]}>
                      {historiqueLigne?.numero === affaire.numero && (
                        <div className="rig-lm-detail__compte" role="status" aria-label="Historique de l'affaire">{historiqueLigne.texte}</div>
                      )}
                    </LigneDetail>
                  )}
                  </Fragment>
                )
              })}
              {vue && trouvees.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">
                  {vue.aucunCritere ? (
                    'Saisissez au moins un critere, puis lancez la recherche.'
                  ) : (
                    <>
                      <div>Aucune affaire ne repond a ces criteres.</div>
                      <div style={{ marginTop: 'var(--rig-esp-3)' }}>
                        <button className="rig-bouton rig-bouton--fort"
                                onClick={() => ouvrirUnDossier(vue.sirenCherche)}>
                          Ouvrir un dossier
                        </button>
                        {/* Aucun blanc entre le bouton et le span (Razor retire celui qui
                            precede un @if) ; le blanc est DANS le span, a sa police. */}
                        {vue.sirenCherche.length > 0 && (
                          <span className="rig-resultats-proc__compte"
                                style={{ marginLeft: 'var(--rig-esp-3)' }}>
                            {' pour le SIREN '}{vue.sirenCherche}
                          </span>
                        )}
                      </div>
                    </>
                  )}
                </td></tr>
              )}
              {trouvees.length > 0 && affichees.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">
                  {`Aucune des ${trouvees.length} affaires ne contient « ${filtreRapide.trim()} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        {/* Le bloc n'apparait que pour une ancienne affaire choisie (ou le compte rendu du geste) : pour une
            affaire de la V3, l'ecran est celui d'avant. fix-ok: ajout SOC-015 T01 en plusieurs pas, pas un correctif. */}
        {(choisieV2 !== null || ancienRig !== null) && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3) var(--rig-esp-4)' }}>
            <div className="rig-kicker">Ancien RIG</div>
            {choisieV2 !== null && (
              <span className="rig-resultats-proc__compte">
                {choisieV2.numero}{' · '}{choisieV2.motif}
              </span>
            )}
            {choisieV2 !== null && (
              <button className="rig-bouton rig-bouton--fort" onClick={() => ouvrirDansAncienRig(choisieV2.numero)}>
                Ouvrir dans l'ancien RIG
              </button>
            )}
            {choisieV2 !== null && (
              <button className="rig-bouton" onClick={() => voirHistorique(choisieV2.numero)}>
                Voir son historique (lecture seule)
              </button>
            )}
            {historique !== null && (
              <span className="rig-resultats-proc__compte" role="status" aria-label="Historique de l'ancienne affaire">
                {texteHistorique(historique, choisieV2?.numero ?? '')}
              </span>
            )}
            {ancienRig !== null && (
              <span className="rig-resultats-proc__compte" role="status">{ancienRig.message}</span>
            )}
          </div>
        )}

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3) var(--rig-esp-4)' }}>
          <div className="rig-kicker">Suppression (XXAFF)</div>
          {choisie === null ? (
            <span className="rig-resultats-proc__compte">
              Selectionnez une instance dans les resultats.
            </span>
          ) : (
            <span className="rig-resultats-proc__compte">
              {choisie.numero}{' · instance '}{choisie.idInstance}{' · role '}{choisie.role}
            </span>
          )}
          <button className={`rig-bascule${libererNumeros ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setLibererNumeros(!libererNumeros)}>
            <span className="rig-bascule__temoin"></span>
            <span>Liberer les numeros metiers</span>
          </button>
          <button className="rig-bouton rig-bouton--fort" disabled={choisie === null}
                  onClick={supprimer}>Supprimer l'instance</button>
          {message !== null && message !== '' && (
            <span className="rig-resultats-proc__compte">{message}</span>
          )}
        </div>

        <PiedListe aide="Double-clic sur une ligne pour ouvrir l'affaire dans la visionneuse.">
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' affaires affichees' : ' affaire affichee'}</span>
          <span>Solde <b>{montant(soldeTotal)}</b></span>
          <span>En cours <b>{enCours}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
