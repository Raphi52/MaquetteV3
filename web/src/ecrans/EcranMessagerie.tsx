// Ecran « Messagerie » (SOC-006 T04, CA05). Procedure RIG v2 : PROC_GESTEMAIL (« Modele de mail », code GESTEMAIL) :
// la recherche des modeles (OPE_RECHERCHE_EMAILMODEL : colonnes nom, libelle ecran, objet ; filtre sur le nom au fil de
// la saisie ; les modeles desactives caches, « Afficher les éléments inactifs »), la fiche d'un modele (OPE_EDIT_EMAILMODEL
// et ses sous-operations : nom, libelle ecran, objet, texte, expediteur, « Répondre à », « Copie cachée », destinataire
// par defaut et ses listes, cases « Utilisable … », envoi automatique pour un modele RCS) et les listes de distribution
// (OPE_RECHERCHE_LISTEDISTRIBUTIONMAIL et OPE_EDIT_LISTEDISTRIBUTIONMAIL : code, libelle, adresses, RCS, judiciaire,
// publique ou privee, actif).
//
// Tout vient de la base DU GREFFE, par Pont/EcranMessagerie.cs et le serveur (RoutesCourriels). CA05 : un agent du greffe
// ne recoit aucun modele reserve a Amitel, ni par la liste ni par son nom, et n'a ni la bascule « Amitel », ni la case
// « Modèle modifiable uniquement par Amitel », ni le dictionnaire des tags (la V2 ne les montre qu'a un agent d'Amitel).
// L'apercu est le courriel tel qu'il partirait de ce greffe (habillage, jetons, logo, adresses du greffe), avec deux
// documents d'exemple comme l'apercu de la V2 ; il s'affiche dans un cadre sans script et se recalcule apres chaque
// enregistrement (le serveur le rend avec le resultat).
//
// L'ECRITURE (lot 15) : un agent qui a la fonction GESTEMAIL (vue.peutModifier, dit par le serveur) cree, modifie,
// desactive et reactive un modele et une liste : Nouveau, Enregistrer, Annuler, Désactiver / Réactiver, comme les barres
// de la V2. Le serveur controle chaque saisie ; son motif s'affiche tel quel et la saisie reste a l'ecran. Sans GESTEMAIL :
// la lecture, sans aucun bouton d'ecriture. Sans serveur : la demonstration du pont, avec son bandeau ; chaque
// enregistrement y est refuse et le dit.
//
// LES GESTES DE LA V2 (lot 16) :
//  - le texte d'un modele HTML s'edite dans un editeur mis en forme (les boutons de RigHtmlEditor) quand le greffe a un
//    habillage regle, la condition de la V2 (OPE_EDIT_EMAILMODEL.LinkDonnees : EMAILMOD_CORPS_HTML && _TemplateActif) ;
//    le serveur retire le contenu actif du HTML a chaque enregistrement (script, attributs on…, liens javascript:), le reste
//    est garde tel que saisi, comme la V2 ;
//  - expediteur, « Répondre à » et « Copie cachée » se choisissent parmi les boites du greffe, montrees par leur ADRESSE
//    (INFO_GREFFE, lue par le serveur), ou en saisie libre ; la base garde l'etiquette [ADRESSE_MAIL_X]
//    (OPE_EDIT_EMAILMODEL_ONE_CHOICE, OPE_EDIT_EMAILMODEL_MANY_CHOICES « RA » et « CC ») ;
//  - « Ajouter le logo » ajoute $LOGO$ au texte, le modele passe en HTML ; refuse sous l'habillage du greffe, comme le serveur ;
//  - ouvrir un autre modele avec une saisie en cours pose la question de la V2 (OPE_RECHERCHE_EMAILMODEL.LoadEmailModel) :
//    « Oui » enregistre puis ouvre l'autre (un refus du serveur garde la saisie et n'ouvre rien), « Non » abandonne la saisie.
// fix-ok: ecarts V2 releves par le chef et lus dans ce fichier avant le lot 16 -- texte HTML en <textarea> brut, adresses tapees
// en etiquettes (datalist [ADRESSE_MAIL_…]), aucun bouton « Ajouter le logo », changement de modele refuse par un message.

import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'

export interface ModeleMessage {
  type: string
  libelleEcran: string | null
  sujet: string
  corpsHtml: boolean
  expediteur: string | null
  reponse: string | null
  copie: string | null
  destinataireDefaut: string | null
  reserveAmitel: boolean
  envoiAutomatique: boolean
  utilisableEnSaisie: boolean | null
  lot1: boolean
  lot2: boolean
  inscription: boolean | null
  comptabilite: boolean | null
}

export interface ListeDiffusion {
  id: number
  code: string
  libelle: string
  adresses: string
  rcs: boolean
  judiciaire: boolean
  proprietaire: string | null
  actif: boolean
}

export interface ApercuModele {
  modele: ModeleMessage
  corps: string
  expediteur: string
  reponse: string
  copie: string
  destinataireDefaut: string
  rendu: string
  habille: boolean
  valeursManquantes: string[]
  listesParDefaut: ListeDiffusion[]
  signatureActive: boolean
  signature: string | null
}

/** Une boite du greffe (Contrats/Socle/Courriels.cs, BoiteCourriel) : son etiquette et son adresse dans INFO_GREFFE. */
export interface BoiteCourriel {
  etiquette: string
  /** "" : le greffe n'a pas d'adresse pour cette boite. */
  adresse: string
}

/** Ce que le formulaire d'un modele propose pour CE greffe (BoitesDuGreffe). */
export interface BoitesDuGreffe {
  boites: BoiteCourriel[]
  /** SEND_EMAIL_TEMPLATE_PATH est regle : un modele HTML s'edite dans l'editeur mis en forme (_TemplateActif de la V2). */
  habillageRegle: boolean
  /** Le greffe a un habillage (en base ou en fichier) : $LOGO$ est refuse. */
  habillagePresent: boolean
}

export interface VueEcranMessagerie {
  mode: 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur'
  message: string | null
  greffe: string | null
  voitReservesAmitel: boolean
  modeles: ModeleMessage[]
  listes: ListeDiffusion[]
  /** La fonction GESTEMAIL dans ce greffe : les boutons d'ecriture. */
  peutModifier: boolean
  /** Un agent d'Amitel : la case « réservé à Amitel » et le dictionnaire des tags. */
  estAmitel: boolean
  /** Le compte de l'agent, proprietaire d'une liste privee qu'il cree. */
  compte: string | null
  /** Le dictionnaire des tags de la V2, pour un agent d'Amitel seulement. */
  dictionnaire: string | null
  /** Les boites du greffe et son habillage ; null quand rien n'est lu (refus, panne, aucun greffe). */
  boites: BoitesDuGreffe | null
}

export interface VueModeleMessagerie {
  mode: string
  message: string | null
  apercu: ApercuModele | null
}

/** Ce que l'ecran envoie pour enregistrer un modele (Contrats/Socle/Courriels.cs, SaisieModele). */
export interface SaisieModele {
  type: string
  libelleEcran: string
  sujet: string
  corps: string
  expediteur: string
  reponse: string
  copie: string
  destinataireDefaut: string
  lot1: boolean
  lot2: boolean
  inscription: boolean
  comptabilite: boolean
  envoiAutomatique: boolean
  reserveAmitel: boolean
  signatureActive: boolean
  signature: string
  listesParDefaut: number[]
  ecran: string | null
}

/** Ce que l'ecran envoie pour enregistrer une liste (SaisieListe). */
export interface SaisieListe {
  code: string
  libelle: string
  adresses: string
  rcs: boolean
  judiciaire: boolean
  publique: boolean
  ecran: string | null
}

/** Ce qu'est devenu un geste d'ecriture (Pont/EcranMessagerie.cs, ResultatMessagerie). */
export interface ResultatMessagerie {
  ok: boolean
  message: string
  vue: VueEcranMessagerie | null
  fiche: VueModeleMessagerie | null
}

/** Les cases « Utilisable … » de la V2 (OPE_EDIT_EMAILMODEL.Designer.cs : CheckBoxRCS, Judiciaire, INS, CPT) et ce qu'elles lisent. */
const Domaines: { nom: string; libelle: string; vrai: (m: ModeleMessage) => boolean }[] = [
  { nom: 'RCS', libelle: 'Utilisable RCS', vrai: (m) => m.lot1 },
  { nom: 'Judiciaire', libelle: 'Utilisable judiciaire', vrai: (m) => m.lot2 },
  { nom: 'Privilège', libelle: 'Utilisable privilège', vrai: (m) => m.inscription === true },
  { nom: 'Comptabilité', libelle: 'Utilisable comptabilité', vrai: (m) => m.comptabilite === true },
]
/** CheckBoxAmitel de la V2, visible pour un agent d'Amitel seulement (OPE_EDIT_EMAILMODEL.cs:333). */
const DomaineAmitel = { nom: 'Amitel', libelle: 'Modèle modifiable uniquement par Amitel', vrai: (m: ModeleMessage) => m.reserveAmitel }
const LibelleEnvoiAuto = 'Mail envoyé automatiquement (sans affichage de l\'écran de mail)'

// Les boites du greffe (colonnes d'INFO_GREFFE) et leur nom court, montre a cote de leur adresse : deux boites d'un greffe
// ont souvent la meme adresse (RIG_DEV : RCS et nantissements, comptabilite et « ne pas répondre »).
const NoReply = 'ADRESSE_MAIL_NO_REPLY'
const NomsDesBoites: Record<string, string> = {
  ADRESSE_MAIL_RCS: 'RCS', ADRESSE_MAIL_JUDICIAIRE: 'judiciaire', ADRESSE_MAIL_NANTISSEMENTS: 'nantissements',
  ADRESSE_MAIL_COMPTABILITE: 'comptabilité', [NoReply]: 'ne pas répondre',
  ADRESSE_MAIL_ACCUSE_ENVOI_JUD: 'accusé d\'envoi judiciaire', ADRESSE_MAIL_ACCUSE_ENVOI_RCS: 'accusé d\'envoi RCS',
  ADRESSE_MAIL_ACCUSE_ENVOI_NANT: 'accusé d\'envoi nantissements', ADRESSE_MAIL_ACCUSE_ENVOI_CPT: 'accusé d\'envoi comptabilité',
}
/** Expediteur et « Répondre à » : OPE_EDIT_EMAILMODEL_ONE_CHOICE.ConstructGroupRadioButton et MANY_CHOICES « RA ». */
const BoitesExpediteur = ['ADRESSE_MAIL_RCS', 'ADRESSE_MAIL_JUDICIAIRE', 'ADRESSE_MAIL_NANTISSEMENTS', 'ADRESSE_MAIL_COMPTABILITE', NoReply]
/** « Copie cachée » : MANY_CHOICES « CC », sans « ne pas répondre » (interdit en copie), avec les accuses d'envoi. */
const BoitesCopie = ['ADRESSE_MAIL_RCS', 'ADRESSE_MAIL_JUDICIAIRE', 'ADRESSE_MAIL_NANTISSEMENTS', 'ADRESSE_MAIL_COMPTABILITE',
  'ADRESSE_MAIL_ACCUSE_ENVOI_JUD', 'ADRESSE_MAIL_ACCUSE_ENVOI_RCS', 'ADRESSE_MAIL_ACCUSE_ENVOI_NANT', 'ADRESSE_MAIL_ACCUSE_ENVOI_CPT']
/** V2 CheckUtilisableEn : cocher « Utilisable X » coche l'accuse d'envoi X en copie cachee. */
const AccuseDe = {
  lot1: 'ADRESSE_MAIL_ACCUSE_ENVOI_RCS', lot2: 'ADRESSE_MAIL_ACCUSE_ENVOI_JUD', inscription: 'ADRESSE_MAIL_ACCUSE_ENVOI_NANT',
  comptabilite: 'ADRESSE_MAIL_ACCUSE_ENVOI_CPT',
} as const
type CaseUtilisable = keyof typeof AccuseDe

/** Le refus du serveur sous habillage (ReglesCourriels.ControlerModele), dit des le clic sur « Ajouter le logo ». */
const RefusLogo = 'Le tag $LOGO$ est interdit quand un template est actif (le logo est positionné dans le template).'
/** La question de la V2 avant d'ouvrir un autre modele (OPE_RECHERCHE_EMAILMODEL.LoadEmailModel, :107). */
const QuestionChangement = 'Des changements ont été saisis, voulez-vous les enregistrer ?'

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const Fiche = { padding: 'var(--rig-esp-3)', display: 'flex', flexDirection: 'column', gap: 'var(--rig-esp-2)', overflow: 'auto' } as const
const Note = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)', overflowWrap: 'anywhere' } as const
const Cadre = { width: '100%', height: '260px', border: '1px solid var(--rig-bord)', borderRadius: 'var(--rig-rayon, 4px)', background: '#fff' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Liste = { display: 'flex', flexDirection: 'column', gap: '2px' } as const
const Pastilles = { display: 'flex', flexWrap: 'wrap', gap: 'var(--rig-esp-2)' } as const
const Bascules = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', columnGap: 'var(--rig-esp-3)' } as const
const Actions = { display: 'flex', flexWrap: 'wrap', gap: 'var(--rig-esp-2)', paddingTop: 'var(--rig-esp-2)' } as const
const Demande = { display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--rig-esp-2)' } as const
const Dictionnaire = { margin: 0, padding: 'var(--rig-esp-2)', fontSize: 'var(--rig-fs-dense)', whiteSpace: 'pre-wrap', border: '1px solid var(--rig-bord)', borderRadius: 'var(--rig-rayon, 4px)' } as const
const BarreEditeur = { display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--rig-esp-1)' } as const
const TitreRequis = { display: 'flex', alignItems: 'center', gap: 'var(--rig-esp-2)' } as const
const Separateur = { width: '1px', alignSelf: 'stretch', background: 'var(--rig-bord)', margin: '0 var(--rig-esp-1)' } as const
// La zone editable : le style d'une zone de saisie de la maquette, hauteur de la zone de texte qu'elle remplace (7 lignes).
const ZoneEditeur = { minHeight: '9.5em', maxHeight: '24em', overflow: 'auto', whiteSpace: 'normal', lineHeight: 1.45, cursor: 'text' } as const

const ouiNon = (b: boolean) => (b ? 'Oui' : 'Non')
const proprietaire = (p: string | null) => (p === '*' ? 'Tous les agents' : p ?? '—')
const adresses = (a: string) => a.split(';').map((x) => x.trim()).filter((x) => x.length > 0).join(' ; ')
const actif = (m: ModeleMessage) => m.utilisableEnSaisie !== false

// Le texte d'un modele marque ses sauts de ligne par « | » (le rendu les change en <br/>, HabillageCourriel.Rendre). La zone de
// saisie les montre en vrais sauts de ligne et les remet en « | » a l'enregistrement. Un texte qui porte deja de vrais sauts de
// ligne en base est montre et renvoye TEL QUEL : les changer en « | » modifierait son rendu sans que l'agent l'ait demande.
const enBarres = (corps: string) => !/[\r\n]/.test(corps)
const versSaisie = (corps: string) => (enBarres(corps) ? corps.split('|').join('\n') : corps)
const versBase = (texte: string, barres: boolean) => (barres ? texte.split('\n').join('|') : texte)

// L'editeur mis en forme recoit un texte deja HTML tel quel ; un texte sans balise (25 des 37 modeles HTML de RIG_DEV, ecrits
// en lignes) y entre avec ses sauts de ligne en <br />, comme l'editeur de la V2 (Ult_HtmlEditor._CorpsToHtml).
const echapper = (t: string) => t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
const versEditeur = (corps: string) => (corps.includes('<') ? corps : echapper(corps).replace(/\r\n|\n|\|/g, '<br />'))

/** L'expediteur tel que la V2 le decoupe (RegEx.DecoupeMailInfoGreffe) : libelle, et une boite du greffe OU une adresse libre. */
interface Expediteur {
  libelle: string
  /** L'etiquette choisie (« ADRESSE_MAIL_RCS ») ; null : « Saisie libre ». */
  boite: string | null
  adresse: string
}

function expediteurDe(valeur: string): Expediteur {
  const m = /([^;<]*)<([^;>]*)>/.exec(valeur)
  const libelle = (m ? m[1]! : '').trim()
  const adresse = (m ? m[2]! : valeur).trim()
  const etiquette = /^\[([A-Za-z_]+)\]$/.exec(adresse)?.[1]
  // Une etiquette que le formulaire ne propose pas reste en saisie libre, telle quelle : rien ne se perd.
  return etiquette && BoitesExpediteur.includes(etiquette) ? { libelle, boite: etiquette, adresse: '' } : { libelle, boite: null, adresse }
}

/** « Libellé <[ADRESSE_MAIL_X]> », comme OPE_EDIT_EMAILMODEL_ONE_CHOICE (DoPreparerSauvegarde) ; "" sans adresse (le serveur
 *  le refuse : l'expediteur est obligatoire). */
function expediteurEnBase(e: Expediteur): string {
  const adresse = e.boite ? `[${e.boite}]` : e.adresse.trim()
  if (adresse.length === 0) return ''
  return e.libelle.trim().length > 0 ? `${e.libelle.trim()} <${adresse}>` : adresse
}

/** « Répondre à » ou « Copie cachée » : les boites cochees et la saisie libre (OPE_EDIT_EMAILMODEL_MANY_CHOICES). */
interface Adresses { boites: string[]; libre: string }

function adressesDe(valeur: string, proposees: string[]): Adresses {
  const boites: string[] = []
  const libres: string[] = []
  for (const morceau of valeur.split(';').map((x) => x.trim()).filter((x) => x.length > 0)) {
    const etiquette = /^\[([A-Za-z_]+)\]$/.exec(morceau)?.[1]
    // La V2 laissait tomber une etiquette qu'elle ne proposait pas (GetValueOfPropertyForCheckbox) : ici elle reste en saisie libre.
    if (etiquette && proposees.includes(etiquette)) { if (!boites.includes(etiquette)) boites.push(etiquette) } else libres.push(morceau)
  }
  return { boites, libre: libres.join(';') }
}

/** La saisie libre d'abord, puis les etiquettes cochees dans l'ordre du formulaire, comme MANY_CHOICES.Valider. */
const adressesEnBase = (a: Adresses, proposees: string[]) =>
  [a.libre.trim(), ...proposees.filter((b) => a.boites.includes(b)).map((b) => `[${b}]`)].filter((x) => x.length > 0).join(';')

/** Le formulaire d'un modele, tel que l'agent le saisit. */
interface FicheModele {
  /** null : un modele neuf (« Ajout d'un modèle de mail ») ; sinon le nom du modele en base. */
  origine: string | null
  type: string
  libelleEcran: string
  sujet: string
  /** Le texte : en lignes dans la zone de texte, en HTML dans l'editeur mis en forme. */
  texte: string
  barres: boolean
  /** Ouvert dans l'editeur mis en forme : modele HTML sous un habillage regle, decide a l'ouverture comme la V2 (LinkDonnees). */
  editeur: boolean
  /** Le texte tel qu'en base : renvoye tel quel tant que l'agent n'a pas touche au texte de l'editeur. */
  corpsOrigine: string
  /** Le modele est HTML, ou le devient (« Ajouter le logo »). */
  html: boolean
  expediteur: Expediteur
  reponse: Adresses
  copie: Adresses
  destinataireDefaut: string
  lot1: boolean
  lot2: boolean
  inscription: boolean
  comptabilite: boolean
  envoiAutomatique: boolean
  reserveAmitel: boolean
  signatureActive: boolean
  signature: string
  listes: number[]
}

// Un modele neuf : les valeurs par defaut de soc.ModeleMessage (RCS et judiciaire coches, comme EMAIL_MODEL), un texte
// (la V2 cree un modele texte), l'expediteur en saisie libre, et l'accuse d'envoi judiciaire en copie cachee comme
// l'ajout de la V2 (MANY_CHOICES.LinkDonnees : « en mode ajout, je coche CheckBoxAdrMailAccuseEnvoiJud »).
const ModeleNeuf: FicheModele = {
  origine: null, type: '', libelleEcran: '', sujet: '', texte: '', barres: true, editeur: false, corpsOrigine: '', html: false,
  expediteur: { libelle: '', boite: null, adresse: '' }, reponse: { boites: [], libre: '' },
  copie: { boites: ['ADRESSE_MAIL_ACCUSE_ENVOI_JUD'], libre: '' }, destinataireDefaut: '', lot1: true, lot2: true,
  inscription: false, comptabilite: false, envoiAutomatique: false, reserveAmitel: false, signatureActive: false, signature: '', listes: [],
}

function ficheDe(a: ApercuModele, habillageRegle: boolean): FicheModele {
  const m = a.modele
  const editeur = m.corpsHtml && habillageRegle
  return {
    origine: m.type, type: m.type, libelleEcran: m.libelleEcran ?? '', sujet: m.sujet,
    texte: editeur ? versEditeur(a.corps) : versSaisie(a.corps), barres: enBarres(a.corps), editeur, corpsOrigine: a.corps, html: m.corpsHtml,
    expediteur: expediteurDe(m.expediteur ?? ''), reponse: adressesDe(m.reponse ?? '', BoitesExpediteur),
    copie: adressesDe(m.copie ?? '', BoitesCopie), destinataireDefaut: m.destinataireDefaut ?? '',
    lot1: m.lot1, lot2: m.lot2, inscription: m.inscription === true, comptabilite: m.comptabilite === true,
    envoiAutomatique: m.envoiAutomatique, reserveAmitel: m.reserveAmitel, signatureActive: a.signatureActive, signature: a.signature ?? '',
    // Cochees : les listes liees que l'agent peut cocher, les actives (OPE_EDIT_EMAILMODEL_LISTE_DISTRIB.LinkDonnees) ; a
    // l'enregistrement, le modele ne garde QUE les cochees (:58-61), comme la V2.
    listes: a.listesParDefaut.filter((l) => l.actif).map((l) => l.id),
  }
}

function saisieDe(f: FicheModele): SaisieModele {
  // L'editeur : le texte de la base tant que l'agent n'y a pas touche (la V2 : _userEdited), sinon le HTML saisi.
  const corps = f.editeur ? (f.texte === versEditeur(f.corpsOrigine) ? f.corpsOrigine : f.texte) : versBase(f.texte, f.barres)
  return {
    type: f.type, libelleEcran: f.libelleEcran, sujet: f.sujet, corps, expediteur: expediteurEnBase(f.expediteur),
    reponse: adressesEnBase(f.reponse, BoitesExpediteur), copie: adressesEnBase(f.copie, BoitesCopie),
    destinataireDefaut: f.destinataireDefaut, lot1: f.lot1, lot2: f.lot2, inscription: f.inscription,
    comptabilite: f.comptabilite, envoiAutomatique: f.lot1 && f.envoiAutomatique, reserveAmitel: f.reserveAmitel,
    signatureActive: f.signatureActive, signature: f.signature, listesParDefaut: f.listes, ecran: null,
  }
}

/** Le formulaire d'une liste ; <c>id</c> null : une liste neuve. */
interface FicheListe {
  id: number | null
  code: string
  libelle: string
  adresses: string
  rcs: boolean
  judiciaire: boolean
  publique: boolean
}

const ListeNeuve: FicheListe = { id: null, code: '', libelle: '', adresses: '', rcs: false, judiciaire: false, publique: false }

const ficheListeDe = (l: ListeDiffusion): FicheListe => ({
  id: l.id, code: l.code, libelle: l.libelle, adresses: l.adresses.split(';').map((x) => x.trim()).filter((x) => x.length > 0).join('\n'),
  rcs: l.rcs, judiciaire: l.judiciaire, publique: l.proprietaire === '*',
})

const memes = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b)

function Champ({ libelle, valeur, titre }: { libelle: string; valeur: string; titre?: string }) {
  return (
    <label className="rig-saisie-champ">
      <span className="rig-saisie-champ__libelle">{libelle}</span>
      <input className="rig-saisie-champ__saisie" value={valeur} title={titre} readOnly />
    </label>
  )
}

function Saisie({ libelle, valeur, changer, requis, taille, lecture, titre }: {
  libelle: string; valeur: string; changer: (v: string) => void; requis?: boolean; taille: number; lecture?: boolean; titre?: string | undefined
}) {
  return (
    <label className="rig-saisie-champ">
      <span className="rig-saisie-champ__libelle">{libelle}{requis && <span className="rig-saisie-champ__requis">requis</span>}</span>
      {/* Le nom du champ est son libelle seul : la marque « requis » n'en fait pas partie. */}
      <input className="rig-saisie-champ__saisie" aria-label={libelle} value={valeur} maxLength={taille} readOnly={lecture}
             title={titre} onChange={(e) => changer(e.target.value)} />
    </label>
  )
}

function Bascule({ libelle, vrai, basculer, inactif, titre }: {
  libelle: string; vrai: boolean; basculer: () => void; inactif?: boolean | undefined; titre?: string | undefined
}) {
  return (
    <button type="button" className={`rig-bascule${vrai ? ' rig-bascule--actif' : ''}`} aria-pressed={vrai} disabled={inactif} title={titre}
            onClick={basculer}>
      <span className="rig-bascule__temoin"></span><span>{libelle}</span>
    </button>
  )
}

/** Un choix parmi plusieurs (l'expediteur : une boite OU la saisie libre), dans le style des bascules de la maquette. */
function Choix({ libelle, vrai, choisir, titre }: { libelle: string; vrai: boolean; choisir: () => void; titre?: string | undefined }) {
  return (
    <button type="button" role="radio" aria-checked={vrai} className={`rig-bascule${vrai ? ' rig-bascule--actif' : ''}`} title={titre}
            onClick={choisir}>
      <span className="rig-bascule__temoin"></span><span>{libelle}</span>
    </button>
  )
}

/** Une boite du greffe telle que l'agent la choisit : son ADRESSE dans ce greffe, son nom court ; l'etiquette en infobulle. */
const libelleBoite = (etiquette: string, adresse: string) =>
  `${adresse.length > 0 ? adresse : 'aucune adresse dans ce greffe'} (${NomsDesBoites[etiquette] ?? etiquette})`

/**
 * Les boutons de RigHtmlEditor (D:\RigApplication\Source\Outils\DLL\RigHtmlEditor\RigHtmlEditor.cs, AjouterBouton), dans son
 * ordre et avec ses textes ; leur nom est l'infobulle de la V2 (« Effacer mise en forme » : son texte). Chacun est la commande
 * d'edition du navigateur que la V2 passait a ExecCommand (WebBrowser), sans paquet de plus.
 */
const BoutonsEditeur: ({ texte: string; nom: string; titre: string; commande: string; style?: CSSProperties } | null)[] = [
  { texte: 'G', nom: 'Gras', titre: 'Gras', commande: 'bold', style: { fontWeight: 700 } },
  { texte: 'I', nom: 'Italique', titre: 'Italique', commande: 'italic', style: { fontStyle: 'italic' } },
  { texte: 'S', nom: 'Souligné', titre: 'Souligné', commande: 'underline', style: { textDecoration: 'underline' } },
  null,
  { texte: '• Liste', nom: 'Liste à puces', titre: 'Liste à puces', commande: 'insertUnorderedList' },
  { texte: '1. Liste', nom: 'Liste numérotée', titre: 'Liste numérotée', commande: 'insertOrderedList' },
  null,
  { texte: 'Lien', nom: 'Insérer un lien', titre: 'Insérer un lien', commande: 'createLink' },
  { texte: 'Effacer mise en forme', nom: 'Effacer mise en forme', titre: 'Retirer la mise en forme', commande: 'removeFormat' },
]

/**
 * L'editeur mis en forme du texte d'un modele HTML : une zone editable et la barre de RigHtmlEditor. <c>valeur</c> est son HTML ;
 * chaque saisie, collage ou mise en forme le rend par <c>changer</c>. Le HTML n'est pas nettoye ici : le serveur en retire le
 * contenu actif a chaque enregistrement (script, attributs on…, liens javascript:), seul juge de ce qui est garde.
 */
function EditeurHtml({ libelle, valeur, changer }: { libelle: string; valeur: string; changer: (html: string) => void }) {
  const zone = useRef<HTMLDivElement>(null)
  // La selection de l'agent dans la zone : un clic sur la barre ou la saisie de l'adresse d'un lien la lui ferait perdre (la
  // V2 agissait au MouseDown pour la garder, Bouton_MouseDown).
  const selection = useRef<Range | null>(null)
  const [lien, setLien] = useState<string | null>(null)

  // Le HTML ne se recopie dans la zone que s'il vient d'ailleurs (ouverture, Annuler, logo) : sinon le curseur sauterait.
  // Pose AVANT l'affichage (effet de mise en page) : par un effet differe, la zone restait vide un instant, et un test qui la
  // lisait juste apres l'ouverture la trouvait vide sous charge (vitest complet du 2026-11-21 : '' au lieu du texte).
  useLayoutEffect(() => {
    const z = zone.current
    if (z && z.innerHTML !== valeur) z.innerHTML = valeur
  }, [valeur])

  const retenir = () => {
    const s = window.getSelection()
    if (zone.current && s && s.rangeCount > 0 && zone.current.contains(s.anchorNode)) selection.current = s.getRangeAt(0).cloneRange()
  }
  const lire = () => {
    retenir()
    if (zone.current) changer(zone.current.innerHTML)
  }
  const executer = (commande: string, argument?: string) => {
    const z = zone.current
    if (!z) return
    z.focus()
    const s = window.getSelection()
    if (s && selection.current) { s.removeAllRanges(); s.addRange(selection.current) }
    document.execCommand(commande, false, argument)
    lire()
  }
  const poserLien = () => {
    const adresse = (lien ?? '').trim()
    setLien(null)
    // V2 DemanderUrl : une adresse vide ne cree rien.
    if (adresse.length > 0 && adresse !== 'https://') executer('createLink', adresse)
  }

  return (
    <div className="rig-saisie-champ">
      <span className="rig-saisie-champ__libelle">{libelle}<span className="rig-saisie-champ__requis">requis</span></span>
      <div role="toolbar" aria-label="Mise en forme du texte" style={BarreEditeur}>
        {BoutonsEditeur.map((b, i) => b === null ? <span key={`sep${i}`} style={Separateur} aria-hidden="true"></span> : (
          <button key={b.commande} type="button" className="rig-bouton rig-bouton--plat" aria-label={b.nom} title={b.titre} style={b.style}
                  // Le clic ne prend pas le focus a la zone : la selection a mettre en forme reste celle de l'agent.
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => (b.commande === 'createLink' ? setLien('https://') : executer(b.commande))}>
            {b.texte}
          </button>
        ))}
      </div>
      {lien !== null && (
        // DemanderUrl de la V2 : l'adresse, pre-remplie « https:// », OK ou Annuler.
        <div role="dialog" aria-label="Insérer un lien" style={BarreEditeur}>
          <input className="rig-saisie-champ__saisie" aria-label="Adresse du lien" value={lien} autoFocus style={{ flex: '1 1 18em' }}
                 onChange={(e) => setLien(e.target.value)}
                 onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); poserLien() } else if (e.key === 'Escape') setLien(null) }} />
          <button type="button" className="rig-bouton rig-bouton--fort" onClick={poserLien}>OK</button>
          <button type="button" className="rig-bouton" onClick={() => setLien(null)}>Annuler</button>
        </div>
      )}
      <div ref={zone} className="rig-saisie-champ__saisie" role="textbox" aria-multiline="true" aria-label={libelle}
           contentEditable suppressContentEditableWarning style={ZoneEditeur}
           // La saisie seule change la valeur (comme _userEdited de la V2) : entrer dans la zone et en sortir ne « modifie » rien,
           // meme si le navigateur ecrit autrement le HTML de la base (<BR> devient <br>).
           onInput={lire} onKeyUp={retenir} onMouseUp={retenir} />
    </div>
  )
}

/** Une question attend la reponse de l'agent : Desactiver / activer (son objet : « modele:CA », « liste:7 », la question ne se
 *  montre que sur la fiche qui l'a posee), ou ouvrir un autre modele avec une saisie en cours (« changement »). */
interface QuestionEnCours { objet: string; texte: string; oui: () => void; non?: () => void }

/** La question de la V2 avant Désactiver / Activer (DialogBox.Show(..., WarningYesNo), OPE_EDIT_EMAILMODEL.cs:63, :68 ;
 *  OPE_EDIT_LISTEDISTRIBUTIONMAIL.cs:39, :44) : « Oui » fait le geste, « Non » ne fait rien. */
function Question({ texte, oui, non }: { texte: string; oui: () => void; non: () => void }) {
  return (
    <div role="alertdialog" aria-label={texte} style={Demande}>
      {/* fix-ok: capture claire du 2026-11-21 lue -- la question entiere dans la pastille sortait en petits caracteres ;
          la pastille dit l'avertissement (WarningYesNo de la V2), la question est en texte normal. */}
      <span className="rig-etat rig-etat--attente">Attention</span><span>{texte}</span>
      <button type="button" className="rig-bouton rig-bouton--fort" onClick={oui}>Oui</button>
      <button type="button" className="rig-bouton" onClick={non}>Non</button>
    </div>
  )
}

export function EcranMessagerie() {
  const [vue, setVue] = useState<VueEcranMessagerie | null>(null)
  const [recherche, setRecherche] = useState('')
  const [critDomaines, setCritDomaines] = useState<ReadonlySet<string>>(new Set())
  const [inactifs, setInactifs] = useState(false)
  const [onglet, setOnglet] = useState<'modele' | 'listes'>('modele')
  const [choisi, setChoisi] = useState<string | null>(null)
  const [fiche, setFiche] = useState<VueModeleMessagerie | null>(null)
  // Le formulaire (agent GESTEMAIL) et ce qu'il etait a l'ouverture : la difference, c'est la saisie non enregistree.
  const [saisie, setSaisie] = useState<FicheModele | null>(null)
  const [initiale, setInitiale] = useState<FicheModele | null>(null)
  const [liste, setListe] = useState<FicheListe | null>(null)
  const [listeInitiale, setListeInitiale] = useState<FicheListe | null>(null)
  const [resultat, setResultat] = useState<{ ok: boolean; texte: string } | null>(null)
  const [occupe, setOccupe] = useState(false)
  const [voirTags, setVoirTags] = useState(false)
  const [question, setQuestion] = useState<QuestionEnCours | null>(null)

  const modifie = saisie !== null && !memes(saisie, initiale)
  const listeModifiee = liste !== null && !memes(liste, listeInitiale)

  // L'habillage regle du greffe, lu a l'ouverture : il decide de l'editeur d'un modele qu'on ouvre (une fiche peut s'ouvrir dans
  // le meme tour que la vue est posee, avant que son etat soit relu).
  const habillageRegle = useRef(false)
  const poserVue = (v: VueEcranMessagerie) => {
    habillageRegle.current = v.boites?.habillageRegle === true
    setVue(v)
  }

  const poserFiche = (f: VueModeleMessagerie | null) => {
    setQuestion(null)
    setFiche(f)
    const formulaire = f?.apercu ? ficheDe(f.apercu, habillageRegle.current) : null
    setSaisie(formulaire)
    setInitiale(formulaire)
  }

  /** Refuse d'ouvrir une liste tant qu'une saisie est en cours, et un modele tant qu'une saisie de LISTE l'est (le message dit
   *  laquelle ; un modele en cours pose la question de la V2, voir quitterModele) ; vrai si l'agent peut changer de fiche. */
  const libre = () => {
    if (modifie || listeModifiee) {
      const quoi = listeModifiee ? `La liste ${liste?.code || 'neuve'}` : `Le modèle ${saisie?.origine ?? 'neuf'}`
      setResultat({ ok: false, texte: `${quoi} a des modifications non enregistrées : enregistrez-les ou annulez-les avant d'ouvrir autre chose.` })
      return false
    }
    return true
  }

  const ouvrir = async (type: string) => {
    setChoisi(type)
    poserFiche(null)
    poserFiche(await appeler<VueModeleMessagerie>('EcranMessagerie.Modele', type))
  }

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranMessagerie>('EcranMessagerie.Ouvrir').then((v) => {
      if (!vivant) return
      poserVue(v)
      const premier = v.modeles.find(actif)
      if (premier && (v.mode === 'serveur' || v.mode === 'demo')) void ouvrir(premier.type)
    })
    return () => { vivant = false }
  }, [])

  /** Un geste d'ecriture : le motif d'un refus s'affiche et la saisie reste ; reussi, l'ecran et l'apercu sont relus. */
  const agir = async (geste: () => Promise<ResultatMessagerie>, apres?: (r: ResultatMessagerie) => void) => {
    setOccupe(true)
    try {
      const r = await geste()
      setResultat({ ok: r.ok, texte: r.message })
      if (!r.ok) return
      if (r.vue) poserVue(r.vue)
      if (r.fiche) {
        setChoisi(r.fiche.apercu?.modele.type ?? choisi)
        poserFiche(r.fiche)
      }
      apres?.(r)
    } finally {
      setOccupe(false)
    }
  }

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement des modèles de courriel…</p></div>

  if (vue.mode !== 'serveur' && vue.mode !== 'demo') {
    const libelle = vue.mode === 'refuse' ? 'Accès refusé' : vue.mode === 'sans-greffe' ? 'Aucun greffe ouvert' : 'Serveur injoignable'
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete"><div className="rig-kicker">Messagerie</div></div>
          <p style={Message} role="alert"><span className="rig-etat rig-etat--alerte">{libelle}</span>{' '}{vue.message}</p>
        </section>
      </div>
    )
  }

  const demo = vue.mode === 'demo'
  const ecriture = vue.peutModifier
  const domaines = vue.voitReservesAmitel ? [...Domaines, DomaineAmitel] : Domaines
  const basculerDomaine = (d: string) => setCritDomaines((avant) => {
    const s = new Set(avant)
    if (!s.delete(d)) s.add(d)
    return s
  })

  const presents = vue.modeles.filter((m) => inactifs || actif(m))
  let trouves = presents
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouves = trouves.filter((m) => contientSansCasse(m.type, t) || contientSansCasse(m.libelleEcran ?? '', t))
  }
  if (critDomaines.size > 0) trouves = trouves.filter((m) => domaines.some((d) => critDomaines.has(d.nom) && d.vrai(m)))
  const listes = vue.listes.filter((l) => inactifs || l.actif)
  const a = fiche?.apercu
  const courant = saisie?.origine ? vue.modeles.find((m) => m.type === saisie.origine) : undefined
  const listeCourante = liste?.id != null ? vue.listes.find((l) => l.id === liste.id) : undefined

  const changer = <K extends keyof FicheModele>(cle: K, valeur: FicheModele[K]) => setSaisie((f) => (f ? { ...f, [cle]: valeur } : f))
  /** Les cases « Utilisable … » : sans RCS, l'envoi automatique est remis a faux (_SetVisibilityChkEnvoiAuto) ; cocher une case
   *  coche son accuse d'envoi en copie cachee (CheckBoxUtilisableEn_ChangementValeur -> CheckUtilisableEn). */
  const basculerUtilisable = (cle: CaseUtilisable) => setSaisie((f) => {
    if (!f) return f
    const suite: FicheModele = { ...f, [cle]: !f[cle] }
    if (cle === 'lot1' && f.lot1) suite.envoiAutomatique = false
    if (!f[cle] && !f.copie.boites.includes(AccuseDe[cle])) suite.copie = { ...f.copie, boites: [...f.copie.boites, AccuseDe[cle]] }
    return suite
  })
  const basculerListeDefaut = (id: number) =>
    setSaisie((f) => (f ? { ...f, listes: f.listes.includes(id) ? f.listes.filter((x) => x !== id) : [...f.listes, id] } : f))
  // Les listes a cocher : les actives que l'agent voit (OPE_EDIT_EMAILMODEL_LISTE_DISTRIB.DoInitZoneSaisie, :99).
  const listesACocher = vue.listes.filter((l) => l.actif)

  /** L'adresse d'une boite dans CE greffe (INFO_GREFFE, rendue par le serveur) ; "" s'il n'en a pas. */
  const adresseDe = (etiquette: string) => vue.boites?.boites.find((b) => b.etiquette === etiquette)?.adresse ?? ''
  const changerExpediteur = (e: Partial<Expediteur>) => setSaisie((f) => (f ? { ...f, expediteur: { ...f.expediteur, ...e } } : f))
  /** « Répondre à » : « ne pas répondre » exclut les autres boites et la saisie libre, et inversement (MANY_CHOICES « RA »,
   *  CheckBox_ChangementValeur). */
  const basculerReponse = (b: string) => setSaisie((f) => {
    if (!f) return f
    const coche = !f.reponse.boites.includes(b)
    const boites = coche ? [...f.reponse.boites, b] : f.reponse.boites.filter((x) => x !== b)
    return { ...f, reponse: coche && b === NoReply ? { boites: [NoReply], libre: '' } : { ...f.reponse, boites } }
  })
  const basculerCopie = (b: string) => setSaisie((f) => (f ? {
    ...f, copie: { ...f.copie, boites: f.copie.boites.includes(b) ? f.copie.boites.filter((x) => x !== b) : [...f.copie.boites, b] },
  } : f))

  /** « Ajouter le logo » (OPE_EDIT_EMAILMODEL.Toolbar_ButtonClick, APERCU) : $LOGO$ a la fin du texte, le modele passe en HTML.
   *  Sous l'habillage du greffe, le serveur le refuserait : l'ecran le dit tout de suite, avec la meme phrase, sans rien ajouter. */
  const ajouterLogo = () => {
    if (vue.boites?.habillagePresent) { setResultat({ ok: false, texte: RefusLogo }); return }
    setResultat(null)
    setSaisie((f) => (f ? { ...f, html: true, texte: f.editeur ? `${f.texte}<br />$LOGO$` : `${f.texte}\n$LOGO$` } : f))
  }

  /**
   * Quitter le modele ouvert pour <c>suite</c> (ouvrir un autre modele, en creer un). Avec une saisie en cours, la question de la
   * V2 (OPE_RECHERCHE_EMAILMODEL.LoadEmailModel) : « Oui » l'enregistre puis fait la suite -- refusee par le serveur, la saisie
   * reste et rien d'autre ne s'ouvre ; « Non » l'abandonne et fait la suite. Une saisie de LISTE en cours reste refusee (libre).
   */
  const quitterModele = (suite: () => void) => {
    if (listeModifiee) { libre(); return }
    setResultat(null)
    if (!modifie || !saisie) { suite(); return }
    const enCours = saisie
    setQuestion({
      objet: 'changement', texte: QuestionChangement,
      oui: () => void agir(() => appeler<ResultatMessagerie>('EcranMessagerie.EnregistrerModele', saisieDe(enCours), enCours.origine), suite),
      non: suite,
    })
  }

  const nouveauModele = () => quitterModele(() => {
    setOnglet('modele')
    setChoisi(null)
    setFiche(null)
    setSaisie(ModeleNeuf)
    setInitiale(ModeleNeuf)
  })
  const enregistrerModele = () => {
    if (!saisie) return
    void agir(() => appeler<ResultatMessagerie>('EcranMessagerie.EnregistrerModele', saisieDe(saisie), saisie.origine))
  }
  const annulerModele = () => {
    setResultat(null)
    if (saisie?.origine === null) { setSaisie(null); setInitiale(null); return }
    setSaisie(initiale)
  }
  const activerModele = (type: string, actifVoulu: boolean) => setQuestion({
    objet: `modele:${type}`, texte: `Voulez-vous vraiment ${actifVoulu ? 'activer' : 'désactiver'} ce modèle ?`,
    oui: () => void agir(() => appeler<ResultatMessagerie>('EcranMessagerie.ActiverModele', type, actifVoulu)),
  })
  /** La question posee sur la fiche <c>objet</c>, s'il y en a une : sa reponse la referme. */
  const questionSur = (objet: string) => question?.objet === objet && (
    <Question texte={question.texte} oui={() => { setQuestion(null); question.oui() }} non={() => { setQuestion(null); question.non?.() }} />
  )

  const changerListe = <K extends keyof FicheListe>(cle: K, valeur: FicheListe[K]) => setListe((f) => (f ? { ...f, [cle]: valeur } : f))
  const ouvrirListe = (l: ListeDiffusion | null) => {
    if (!ecriture || !libre()) return
    const f = l ? ficheListeDe(l) : ListeNeuve
    setQuestion(null)
    setListe(f)
    setListeInitiale(f)
    setResultat(null)
  }
  const enregistrerListe = () => {
    if (!liste) return
    const demande: SaisieListe = {
      code: liste.code, libelle: liste.libelle, adresses: liste.adresses, rcs: liste.rcs, judiciaire: liste.judiciaire,
      publique: liste.publique, ecran: null,
    }
    void agir(() => appeler<ResultatMessagerie>('EcranMessagerie.EnregistrerListe', demande, liste.id), (r) => {
      // La liste enregistree reste ouverte, relue du serveur (une liste neuve recoit son numero).
      const relue = r.vue?.listes.find((x) => x.code.toUpperCase() === liste.code.trim().toUpperCase())
      const f = relue ? ficheListeDe(relue) : null
      setListe(f)
      setListeInitiale(f)
    })
  }
  const annulerListe = () => {
    setResultat(null)
    if (liste?.id === null) { setListe(null); setListeInitiale(null); return }
    setListe(listeInitiale)
  }
  const activerListe = (l: ListeDiffusion) => setQuestion({
    objet: `liste:${l.id}`, texte: `Voulez-vous vraiment ${l.actif ? 'désactiver' : 'activer'} cette liste ?`,
    oui: () => void agir(() => appeler<ResultatMessagerie>('EcranMessagerie.ActiverListe', l.id, !l.actif), (r) => {
      const relue = r.vue?.listes.find((x) => x.id === l.id)
      const f = relue ? ficheListeDe(relue) : null
      setListe(f)
      setListeInitiale(f)
    }),
  })

  return (
    <div className="rig-proc__corps">
      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Modèles de mail</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nom ou libellé</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Domaine</div>
          {domaines.map((d) => (
            <button key={d.nom} type="button" className={`rig-bascule${critDomaines.has(d.nom) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerDomaine(d.nom)}>
              <span className="rig-bascule__temoin"></span><span>{d.nom}</span>
            </button>
          ))}
        </div>

        {/* Le bouton de la V2 (OPE_RECHERCHE_EMAILMODEL, ACTUALISER) : il vaut pour les modeles et pour les listes. Pas dans
            rig-criteres__actions, qui pousse son contenu au bas du volet et la liste des resultats avec lui. Le bouton
            d'ajout (« Ajouter un modèle » de la V2) est a cote, pour un agent GESTEMAIL seulement. */}
        <div className="rig-criteres__bloc" style={Actions}>
          {ecriture && (
            <button type="button" className="rig-bouton rig-bouton--fort" disabled={occupe} onClick={nouveauModele}>Nouveau modèle</button>
          )}
          <button type="button" className="rig-bouton" onClick={() => setInactifs((x) => !x)}>
            {inactifs ? 'Masquer les éléments inactifs' : 'Afficher les éléments inactifs'}
          </button>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Résultats ({trouves.length})</div>
          <div role="list" aria-label="Modèles" style={Liste}>
            {trouves.map((m) => (
              <div role="listitem" key={m.type}>
                <button type="button" className={`rig-bascule${choisi === m.type ? ' rig-bascule--actif' : ''}`}
                        title={m.sujet} onClick={() => quitterModele(() => { setOnglet('modele'); void ouvrir(m.type) })}>
                  <span className="rig-bascule__temoin"></span>
                  <span>{m.type} — {m.libelleEcran ?? m.sujet}{actif(m) ? '' : ' (désactivé)'}</span>
                </button>
              </div>
            ))}
          </div>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-onglets">
            <button type="button" className={`rig-onglet${onglet === 'modele' ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setOnglet('modele')}>Modèle</button>
            <button type="button" className={`rig-onglet${onglet === 'listes' ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setOnglet('listes')}>Listes de distribution</button>
          </div>
          <span className="rig-resultats-proc__compte">
            {demo ? 'Démonstration' : `${presents.length} modèle(s) — greffe ${vue.greffe ?? ''}`}
          </span>
        </div>
        {!ecriture && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Lecture seule</span>
            {' '}Sans la fonction GESTEMAIL (« Gestion modèles emails ») dans ce greffe, vous lisez les modèles et les listes sans les modifier.
          </p>
        )}
        {demo && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Démonstration</span>
            {' '}Sans serveur, des modèles d'exemple s'affichent : noms, objets et cases de la base de développement, textes et adresses fictifs ; rien ne s'enregistre.
          </p>
        )}
        {resultat && (
          <p style={Message} role={resultat.ok ? 'status' : 'alert'}>
            <span className={`rig-etat rig-etat--${resultat.ok ? 'ok' : 'alerte'}`}>{resultat.ok ? 'Enregistré' : 'Refusé'}</span>
            {' '}{resultat.texte}
          </p>
        )}
        {question?.objet === 'changement' && <div style={Message}>{questionSur('changement')}</div>}

        {onglet === 'listes' ? (
          <div style={Fiche}>
            {ecriture && (
              <div style={Actions}>
                <button type="button" className="rig-bouton rig-bouton--fort" disabled={occupe} onClick={() => ouvrirListe(null)}>Nouvelle liste</button>
              </div>
            )}
            <div className="rig-table-cadre">
              <table className="rig-table">
                <thead>
                  <tr>{['Code', 'Libellé', 'RCS', 'Judiciaire', 'Propriétaire', 'Actif', 'Adresses'].map((t) => <th key={t}>{t}</th>)}</tr>
                </thead>
                <tbody>
                  {listes.map((l) => (
                    <tr key={l.id} aria-selected={liste?.id === l.id} onClick={() => ouvrirListe(l)}>
                      <td className="rig-table__mono">{l.code}</td>
                      <td>{l.libelle}</td>
                      <td>{ouiNon(l.rcs)}</td>
                      <td>{ouiNon(l.judiciaire)}</td>
                      <td>{proprietaire(l.proprietaire)}</td>
                      <td>{ouiNon(l.actif)}</td>
                      <td className="rig-table__mono">{adresses(l.adresses)}</td>
                    </tr>
                  ))}
                  {listes.length === 0 && (
                    <tr><td colSpan={7} className="rig-table__vide">Aucune liste de distribution dans ce greffe.</td></tr>
                  )}
                </tbody>
              </table>
            </div>

            {ecriture && liste && (
              <div className="rig-criteres__bloc" aria-label="Liste de distribution">
                <div className="rig-kicker">{liste.id === null ? 'Ajout d\'une liste de distribution' : `Liste ${listeInitiale?.code ?? ''}`}</div>
                <div style={Actions}>
                  <button type="button" className="rig-bouton rig-bouton--fort" disabled={occupe || !listeModifiee} onClick={enregistrerListe}>Enregistrer</button>
                  <button type="button" className="rig-bouton" disabled={occupe || (!listeModifiee && liste.id !== null)} onClick={annulerListe}>Annuler</button>
                  {listeCourante && (
                    <button type="button" className="rig-bouton" disabled={occupe || listeModifiee} onClick={() => activerListe(listeCourante)}>
                      {listeCourante.actif ? 'Désactiver' : 'Réactiver'}
                    </button>
                  )}
                </div>
                {listeCourante && !listeModifiee && questionSur(`liste:${listeCourante.id}`)}
                <div className="rig-criteres__paire">
                  <Saisie libelle="Code" valeur={liste.code} changer={(v) => changerListe('code', v)} requis taille={35} lecture={liste.id !== null} />
                  <Saisie libelle="Libellé" valeur={liste.libelle} changer={(v) => changerListe('libelle', v)} requis taille={200} />
                </div>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Adresses (une par ligne)<span className="rig-saisie-champ__requis">requis</span></span>
                  <textarea className="rig-saisie-champ__saisie" aria-label="Adresses (une par ligne)" rows={4} value={liste.adresses}
                            onChange={(e) => changerListe('adresses', e.target.value)} />
                </label>
                <div style={Bascules}>
                  <Bascule libelle="Liste RCS" vrai={liste.rcs} basculer={() => changerListe('rcs', !liste.rcs)} />
                  <Bascule libelle="Liste judiciaire" vrai={liste.judiciaire} basculer={() => changerListe('judiciaire', !liste.judiciaire)} />
                </div>
                {/* GroupRadioButtonPrive de la V2 : une liste deja publique ne repasse pas en privee. */}
                <div style={Bascules} role="group" aria-label="Visibilité"
                     title="Publique : tout le monde aura accès à cette liste de distribution. Privée : vous seul aurez accès à cette liste de distribution.">
                  <Bascule libelle="Publique (tous les agents)" vrai={liste.publique} basculer={() => changerListe('publique', true)} />
                  <Bascule libelle={`Privée (${vue.compte ?? 'vous'})`} vrai={!liste.publique} basculer={() => changerListe('publique', false)}
                           inactif={liste.id !== null && listeInitiale?.publique === true}
                           titre={liste.id !== null && listeInitiale?.publique ? 'Une liste publique ne redevient pas privée.' : undefined} />
                </div>
              </div>
            )}
          </div>
        ) : ecriture && saisie ? (
          <div style={Fiche} aria-label="Modèle de mail">
            <div className="rig-kicker">
              {saisie.origine === null ? 'Ajout d\'un modèle de mail' : `Modèle ${saisie.origine}`}
              {courant && !actif(courant) && <> — <span className="rig-etat rig-etat--attente">Désactivé</span></>}
            </div>
            {/* Les boutons en tete, comme les barres de la V2 (Valider / Annuler du formulaire, Désactiver / Activer de
                l'edition) : visibles sans faire defiler la fiche (capture claire du 2026-11-21 : ils etaient sous le pli). */}
            <div style={Actions}>
              <button type="button" className="rig-bouton rig-bouton--fort" disabled={occupe || !modifie} onClick={enregistrerModele}>
                {saisie.origine === null ? 'Créer le modèle' : 'Enregistrer'}
              </button>
              <button type="button" className="rig-bouton" disabled={occupe || (!modifie && saisie.origine !== null)} onClick={annulerModele}>Annuler</button>
              {courant && (
                <button type="button" className="rig-bouton" disabled={occupe || modifie} onClick={() => activerModele(courant.type, !actif(courant))}>
                  {actif(courant) ? 'Désactiver' : 'Réactiver'}
                </button>
              )}
            </div>
            {courant && !modifie && questionSur(`modele:${courant.type}`)}
            <div className="rig-criteres__paire">
              <Saisie libelle="Nom du modèle" valeur={saisie.type} changer={(v) => changer('type', v)} requis taille={40} lecture={saisie.origine !== null} />
              <Saisie libelle="Libellé écran" valeur={saisie.libelleEcran} changer={(v) => changer('libelleEcran', v)} requis taille={100} />
            </div>
            <Saisie libelle="Sujet" valeur={saisie.sujet} changer={(v) => changer('sujet', v)} requis taille={200} />
            {/* OPE_EDIT_EMAILMODEL_ONE_CHOICE : « Saisie libre » ou une boite du greffe, montree par son ADRESSE ; la base garde
                l'etiquette (« Libellé <[ADRESSE_MAIL_RCS]> »). L'adresse d'une boite se lit, celle de la saisie libre se tape. */}
            {/* La marque « requis » espacee comme dans le libelle d'un champ (.rig-saisie-champ__libelle : flex, gap esp-2) ;
                collee au titre sur la capture claire du 2026-11-21 (« EXPÉDITEURREQUIS »). */}
            <div className="rig-kicker" style={TitreRequis}>Expéditeur<span className="rig-saisie-champ__requis">requis</span></div>
            <div style={Bascules} role="radiogroup" aria-label="Expéditeur">
              <Choix libelle="Saisie libre" vrai={saisie.expediteur.boite === null} choisir={() => changerExpediteur({ boite: null })} />
              {BoitesExpediteur.map((b) => (
                <Choix key={b} libelle={libelleBoite(b, adresseDe(b))} titre={`[${b}]`} vrai={saisie.expediteur.boite === b}
                       choisir={() => changerExpediteur({ boite: b })} />
              ))}
            </div>
            <div className="rig-criteres__paire">
              <Saisie libelle="Expéditeur du courrier (libellé du mail)" valeur={saisie.expediteur.libelle} taille={150}
                      changer={(v) => changerExpediteur({ libelle: v })} />
              <Saisie libelle="Expéditeur du courrier (adresse du mail)" taille={200} lecture={saisie.expediteur.boite !== null}
                      valeur={saisie.expediteur.boite === null ? saisie.expediteur.adresse : adresseDe(saisie.expediteur.boite)}
                      titre={saisie.expediteur.boite === null ? undefined : `[${saisie.expediteur.boite}]`}
                      changer={(v) => changerExpediteur({ adresse: v })} />
            </div>

            {/* MANY_CHOICES « RA » : les memes boites, a cocher ; « ne pas répondre » seule ou rien. */}
            <div className="rig-kicker">Répondre à</div>
            <div style={Bascules} role="group" aria-label="Répondre à">
              {BoitesExpediteur.map((b) => {
                const autres = saisie.reponse.boites.some((x) => x !== NoReply)
                const seuleNoReply = saisie.reponse.boites.includes(NoReply) && !autres
                return (
                  <Bascule key={b} libelle={libelleBoite(b, adresseDe(b))} titre={`[${b}]`} vrai={saisie.reponse.boites.includes(b)}
                           inactif={b === NoReply ? autres && !saisie.reponse.boites.includes(NoReply) : seuleNoReply}
                           basculer={() => basculerReponse(b)} />
                )
              })}
            </div>
            <Saisie libelle="Répondre à (saisie libre)" valeur={saisie.reponse.libre} taille={200}
                    lecture={saisie.reponse.boites.includes(NoReply) && saisie.reponse.boites.length === 1}
                    changer={(v) => changer('reponse', { ...saisie.reponse, libre: v })} />

            {/* MANY_CHOICES « CC » : sans « ne pas répondre », avec les accuses d'envoi du greffe. */}
            <div className="rig-kicker">Copie cachée</div>
            <div style={Bascules} role="group" aria-label="Copie cachée">
              {BoitesCopie.map((b) => (
                <Bascule key={b} libelle={libelleBoite(b, adresseDe(b))} titre={`[${b}]`} vrai={saisie.copie.boites.includes(b)}
                         basculer={() => basculerCopie(b)} />
              ))}
            </div>
            <div className="rig-criteres__paire">
              <Saisie libelle="Copie cachée à (saisie libre)" valeur={saisie.copie.libre} taille={200}
                      changer={(v) => changer('copie', { ...saisie.copie, libre: v })} />
              <Saisie libelle="Destinataire par défaut" valeur={saisie.destinataireDefaut} changer={(v) => changer('destinataireDefaut', v)} taille={200} />
            </div>
            {a && saisie.origine !== null && (
              <p style={Note} aria-label="Adresses pour ce greffe">
                Pour ce greffe : de {a.expediteur || '— (à compléter)'}{a.reponse ? ` ; réponse à ${a.reponse}` : ''}{a.copie ? ` ; copie cachée ${a.copie}` : ''}
              </p>
            )}

            <div className="rig-kicker">Listes par défaut</div>
            <div style={Bascules} role="group" aria-label="Listes par défaut">
              {listesACocher.map((l) => (
                <Bascule key={l.id} libelle={`${l.code} — ${l.libelle}`} vrai={saisie.listes.includes(l.id)} basculer={() => basculerListeDefaut(l.id)} />
              ))}
              {listesACocher.length === 0 && <span style={Note}>Aucune liste de distribution active.</span>}
            </div>

            <div className="rig-kicker">Utilisation</div>
            <div style={Bascules} aria-label="Cases du modèle">
              <Bascule libelle="Utilisable RCS" vrai={saisie.lot1} basculer={() => basculerUtilisable('lot1')} />
              <Bascule libelle="Utilisable judiciaire" vrai={saisie.lot2} basculer={() => basculerUtilisable('lot2')} />
              <Bascule libelle="Utilisable privilège" vrai={saisie.inscription} basculer={() => basculerUtilisable('inscription')} />
              <Bascule libelle="Utilisable comptabilité" vrai={saisie.comptabilite} basculer={() => basculerUtilisable('comptabilite')} />
              {vue.estAmitel && (
                <Bascule libelle={DomaineAmitel.libelle} vrai={saisie.reserveAmitel} basculer={() => changer('reserveAmitel', !saisie.reserveAmitel)} />
              )}
              {/* V2 _SetVisibilityChkEnvoiAuto : l'envoi automatique n'existe que pour un modele RCS. */}
              {saisie.lot1 && (
                <Bascule libelle={LibelleEnvoiAuto} vrai={saisie.envoiAutomatique} basculer={() => changer('envoiAutomatique', !saisie.envoiAutomatique)} />
              )}
            </div>

            {/* La condition de la V2 (LinkDonnees) : l'editeur mis en forme pour un modele HTML quand le greffe a un habillage
                regle ; sinon la zone de texte, HTML compris. */}
            {saisie.editeur ? (
              <EditeurHtml libelle="Texte du modèle" valeur={saisie.texte} changer={(v) => changer('texte', v)} />
            ) : (
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Texte du modèle<span className="rig-saisie-champ__requis">requis</span></span>
                <textarea className="rig-saisie-champ__saisie" aria-label="Texte du modèle" rows={7} value={saisie.texte}
                          onChange={(e) => changer('texte', e.target.value)} />
              </label>
            )}
            <p style={Note} aria-label="Format du texte">
              Format : {saisie.html ? 'HTML' : 'texte'}{saisie.editeur ? ' — éditeur mis en forme (habillage du greffe réglé)' : ''}
            </p>
            {/* La barre sous le texte, comme la V2 (ToolbarAddSignature) : le logo, la signature, le dictionnaire. */}
            <div style={Bascules}>
              {/* A sa largeur, pas a celle d'une case de la grille (etire sur la capture claire du 2026-11-21). */}
              <button type="button" className="rig-bouton rig-bouton--plat" style={{ justifySelf: 'start' }} disabled={occupe} onClick={ajouterLogo}>
                Ajouter le logo
              </button>
              <Bascule libelle="Signature active" vrai={saisie.signatureActive} basculer={() => changer('signatureActive', !saisie.signatureActive)} />
              {vue.dictionnaire && (
                <button type="button" className="rig-bouton rig-bouton--plat" aria-expanded={voirTags} onClick={() => setVoirTags((x) => !x)}>
                  Dictionnaire de tags
                </button>
              )}
            </div>
            {saisie.signatureActive && (
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Signature</span>
                <textarea className="rig-saisie-champ__saisie" rows={3} maxLength={2000} value={saisie.signature}
                          onChange={(e) => changer('signature', e.target.value)} />
              </label>
            )}
            {voirTags && vue.dictionnaire && <pre style={Dictionnaire} aria-label="Dictionnaire de tags">{vue.dictionnaire}</pre>}

            {a && saisie.origine !== null && (
              <>
                {a.valeursManquantes.length > 0 && (
                  <div style={Note}>
                    <span className="rig-etat rig-etat--attente">À compléter</span>
                    <ul style={{ margin: 'var(--rig-esp-1) 0 0', paddingLeft: '1.2em' }}>{a.valeursManquantes.map((v) => <li key={v}>{v}</li>)}</ul>
                  </div>
                )}
                <div className="rig-kicker">Aperçu du courriel enregistré{a.habille ? ' — habillage du greffe' : ''}</div>
                <iframe title="Aperçu du courriel" sandbox="" srcDoc={a.rendu} style={Cadre} />
              </>
            )}
          </div>
        ) : !choisi ? (
          <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>Choisissez un modèle dans la liste de gauche.</div>
        ) : !fiche ? (
          <p className="rig-vide">Chargement du modèle {choisi}…</p>
        ) : !a ? (
          <p style={Message} role="alert"><span className="rig-etat rig-etat--alerte">Modèle</span>{' '}{fiche.message}</p>
        ) : (
          <div style={Fiche}>
            <div className="rig-criteres__paire">
              <Champ libelle="Nom du modèle" valeur={a.modele.type} />
              <Champ libelle="Libellé écran" valeur={a.modele.libelleEcran ?? ''} />
            </div>
            <Champ libelle="Sujet" valeur={a.modele.sujet} />
            <div className="rig-criteres__paire">
              <Champ libelle="Expéditeur" valeur={a.expediteur} titre={a.modele.expediteur ?? ''} />
              <Champ libelle="Répondre à" valeur={a.reponse} titre={a.modele.reponse ?? ''} />
            </div>
            <div className="rig-criteres__paire">
              <Champ libelle="Copie cachée" valeur={a.copie} titre={a.modele.copie ?? ''} />
              <Champ libelle="Destinataire par défaut" valeur={a.destinataireDefaut} titre={a.modele.destinataireDefaut ?? ''} />
            </div>
            <p style={Note} aria-label="Listes par défaut">
              Listes par défaut : {a.listesParDefaut.length === 0 ? 'aucune' : a.listesParDefaut.map((l) => `${l.code} (${l.libelle})`).join(', ')}
            </p>
            <div style={Pastilles} aria-label="Cases du modèle">
              {domaines.filter((d) => d.vrai(a.modele)).map((d) => <span key={d.nom} className="rig-etat rig-etat--action">{d.libelle}</span>)}
              {a.modele.utilisableEnSaisie === false && <span className="rig-etat rig-etat--attente">Désactivé</span>}
            </div>
            {/* V2 _SetVisibilityChkEnvoiAuto : l'envoi automatique n'existe que pour un modele RCS. */}
            {a.modele.lot1 && (
              <span className={`rig-etat rig-etat--${a.modele.envoiAutomatique ? 'ok' : 'attente'}`}>
                {a.modele.envoiAutomatique ? LibelleEnvoiAuto : 'Envoi manuel : l\'écran de mail s\'affiche'}
              </span>
            )}
            {a.valeursManquantes.length > 0 && (
              <div style={Note}>
                <span className="rig-etat rig-etat--attente">À compléter</span>
                <ul style={{ margin: 'var(--rig-esp-1) 0 0', paddingLeft: '1.2em' }}>{a.valeursManquantes.map((v) => <li key={v}>{v}</li>)}</ul>
              </div>
            )}
            <div className="rig-kicker">Aperçu du courriel{a.habille ? ' — habillage du greffe' : ''}</div>
            <iframe title="Aperçu du courriel" sandbox="" srcDoc={a.rendu} style={Cadre} />
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Texte du modèle</span>
              <textarea className="rig-saisie-champ__saisie" rows={6} readOnly value={versSaisie(a.corps)} />
            </label>
            {a.signatureActive && <p style={Note}>Signature : {a.signature ?? ''}</p>}
            <p style={{ ...Note, ...Coupe }} title={a.modele.type}>Format : {a.modele.corpsHtml ? 'HTML' : 'texte'}</p>
          </div>
        )}
      </section>
    </div>
  )
}
