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
// de la V2. Le serveur controle chaque saisie ; son motif s'affiche tel quel et la saisie reste a l'ecran. Une saisie non
// enregistree n'est jamais perdue en silence : ouvrir un autre modele ou une autre liste est refuse tant qu'elle n'est ni
// enregistree ni annulee (la V2 posait la question, OPE_RECHERCHE_EMAILMODEL.LoadEmailModel). Sans GESTEMAIL : la
// lecture, sans aucun bouton d'ecriture. Sans serveur : la demonstration du pont, avec son bandeau ; chaque enregistrement
// y est refuse et le dit.

import { useEffect, useState } from 'react'
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
/** Les boites du greffe que la V2 propose pour l'expediteur (OPE_EDIT_EMAILMODEL_ONE_CHOICE.ConstructGroupRadioButton). */
const BoitesDuGreffe = ['ADRESSE_MAIL_RCS', 'ADRESSE_MAIL_JUDICIAIRE', 'ADRESSE_MAIL_NANTISSEMENTS', 'ADRESSE_MAIL_COMPTABILITE', 'ADRESSE_MAIL_NO_REPLY']

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const Fiche = { padding: 'var(--rig-esp-3)', display: 'flex', flexDirection: 'column', gap: 'var(--rig-esp-2)', overflow: 'auto' } as const
const Note = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)', overflowWrap: 'anywhere' } as const
const Cadre = { width: '100%', height: '260px', border: '1px solid var(--rig-bord)', borderRadius: 'var(--rig-rayon, 4px)', background: '#fff' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Liste = { display: 'flex', flexDirection: 'column', gap: '2px' } as const
const Pastilles = { display: 'flex', flexWrap: 'wrap', gap: 'var(--rig-esp-2)' } as const
const Bascules = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', columnGap: 'var(--rig-esp-3)' } as const
const Actions = { display: 'flex', flexWrap: 'wrap', gap: 'var(--rig-esp-2)', paddingTop: 'var(--rig-esp-2)' } as const
const Dictionnaire = { margin: 0, padding: 'var(--rig-esp-2)', fontSize: 'var(--rig-fs-dense)', whiteSpace: 'pre-wrap', border: '1px solid var(--rig-bord)', borderRadius: 'var(--rig-rayon, 4px)' } as const

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

/** Le formulaire d'un modele, tel que l'agent le saisit. */
interface FicheModele {
  /** null : un modele neuf (« Ajout d'un modèle de mail ») ; sinon le nom du modele en base. */
  origine: string | null
  type: string
  libelleEcran: string
  sujet: string
  texte: string
  barres: boolean
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
  listes: number[]
}

// Un modele neuf : les valeurs par defaut de soc.ModeleMessage (RCS et judiciaire coches, comme EMAIL_MODEL).
const ModeleNeuf: FicheModele = {
  origine: null, type: '', libelleEcran: '', sujet: '', texte: '', barres: true, expediteur: '', reponse: '', copie: '',
  destinataireDefaut: '', lot1: true, lot2: true, inscription: false, comptabilite: false, envoiAutomatique: false,
  reserveAmitel: false, signatureActive: false, signature: '', listes: [],
}

function ficheDe(a: ApercuModele): FicheModele {
  const m = a.modele
  return {
    origine: m.type, type: m.type, libelleEcran: m.libelleEcran ?? '', sujet: m.sujet, texte: versSaisie(a.corps), barres: enBarres(a.corps),
    expediteur: m.expediteur ?? '', reponse: m.reponse ?? '', copie: m.copie ?? '', destinataireDefaut: m.destinataireDefaut ?? '',
    lot1: m.lot1, lot2: m.lot2, inscription: m.inscription === true, comptabilite: m.comptabilite === true,
    envoiAutomatique: m.envoiAutomatique, reserveAmitel: m.reserveAmitel, signatureActive: a.signatureActive, signature: a.signature ?? '',
    listes: a.listesParDefaut.map((l) => l.id),
  }
}

function saisieDe(f: FicheModele): SaisieModele {
  return {
    type: f.type, libelleEcran: f.libelleEcran, sujet: f.sujet, corps: versBase(f.texte, f.barres), expediteur: f.expediteur,
    reponse: f.reponse, copie: f.copie, destinataireDefaut: f.destinataireDefaut, lot1: f.lot1, lot2: f.lot2, inscription: f.inscription,
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

function Saisie({ libelle, valeur, changer, requis, taille, lecture, liste, titre }: {
  libelle: string; valeur: string; changer: (v: string) => void; requis?: boolean; taille: number; lecture?: boolean; liste?: string; titre?: string
}) {
  return (
    <label className="rig-saisie-champ">
      <span className="rig-saisie-champ__libelle">{libelle}{requis && <span className="rig-saisie-champ__requis">requis</span>}</span>
      {/* Le nom du champ est son libelle seul : la marque « requis » n'en fait pas partie. */}
      <input className="rig-saisie-champ__saisie" aria-label={libelle} value={valeur} maxLength={taille} readOnly={lecture} list={liste}
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

  const modifie = saisie !== null && !memes(saisie, initiale)
  const listeModifiee = liste !== null && !memes(liste, listeInitiale)

  const poserFiche = (f: VueModeleMessagerie | null) => {
    setFiche(f)
    const formulaire = f?.apercu ? ficheDe(f.apercu) : null
    setSaisie(formulaire)
    setInitiale(formulaire)
  }

  /** Refuse de quitter une saisie non enregistree (le message le dit) ; vrai si l'agent peut changer de fiche. */
  const libre = () => {
    if (modifie || listeModifiee) {
      const quoi = modifie ? `Le modèle ${saisie?.origine ?? 'neuf'}` : `La liste ${liste?.code || 'neuve'}`
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
      setVue(v)
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
      if (r.vue) setVue(r.vue)
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
  // V2 _SetVisibilityChkEnvoiAuto : sans RCS, l'envoi automatique est remis a faux.
  const basculerRcs = () => setSaisie((f) => (f ? { ...f, lot1: !f.lot1, envoiAutomatique: f.lot1 ? false : f.envoiAutomatique } : f))
  const basculerListeDefaut = (id: number) =>
    setSaisie((f) => (f ? { ...f, listes: f.listes.includes(id) ? f.listes.filter((x) => x !== id) : [...f.listes, id] } : f))
  // Les listes a cocher : les actives que l'agent voit (OPE_EDIT_EMAILMODEL_LISTE_DISTRIB.DoInitZoneSaisie), et celles deja liees.
  const listesACocher = vue.listes.filter((l) => l.actif || saisie?.listes.includes(l.id))

  const nouveauModele = () => {
    if (!libre()) return
    setOnglet('modele')
    setChoisi(null)
    setFiche(null)
    setSaisie(ModeleNeuf)
    setInitiale(ModeleNeuf)
    setResultat(null)
  }
  const enregistrerModele = () => {
    if (!saisie) return
    void agir(() => appeler<ResultatMessagerie>('EcranMessagerie.EnregistrerModele', saisieDe(saisie), saisie.origine))
  }
  const annulerModele = () => {
    setResultat(null)
    if (saisie?.origine === null) { setSaisie(null); setInitiale(null); return }
    setSaisie(initiale)
  }
  const activerModele = (type: string, actifVoulu: boolean) =>
    void agir(() => appeler<ResultatMessagerie>('EcranMessagerie.ActiverModele', type, actifVoulu))

  const changerListe = <K extends keyof FicheListe>(cle: K, valeur: FicheListe[K]) => setListe((f) => (f ? { ...f, [cle]: valeur } : f))
  const ouvrirListe = (l: ListeDiffusion | null) => {
    if (!ecriture || !libre()) return
    const f = l ? ficheListeDe(l) : ListeNeuve
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
  const activerListe = (l: ListeDiffusion) =>
    void agir(() => appeler<ResultatMessagerie>('EcranMessagerie.ActiverListe', l.id, !l.actif), (r) => {
      const relue = r.vue?.listes.find((x) => x.id === l.id)
      const f = relue ? ficheListeDe(relue) : null
      setListe(f)
      setListeInitiale(f)
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
                        title={m.sujet} onClick={() => { if (!libre()) return; setOnglet('modele'); setResultat(null); void ouvrir(m.type) }}>
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
            <div className="rig-criteres__paire">
              <Saisie libelle="Nom du modèle" valeur={saisie.type} changer={(v) => changer('type', v)} requis taille={40} lecture={saisie.origine !== null} />
              <Saisie libelle="Libellé écran" valeur={saisie.libelleEcran} changer={(v) => changer('libelleEcran', v)} taille={100} />
            </div>
            <Saisie libelle="Sujet" valeur={saisie.sujet} changer={(v) => changer('sujet', v)} requis taille={200} />
            <datalist id="rig-messagerie-boites">
              {BoitesDuGreffe.map((b) => <option key={b} value={`[${b}]`} />)}
            </datalist>
            <div className="rig-criteres__paire">
              <Saisie libelle="Expéditeur" valeur={saisie.expediteur} changer={(v) => changer('expediteur', v)} requis taille={200} liste="rig-messagerie-boites"
                      titre="Une boîte du greffe, ex. « Greffe du tribunal de commerce <[ADRESSE_MAIL_JUDICIAIRE]> », ou une adresse" />
              <Saisie libelle="Répondre à" valeur={saisie.reponse} changer={(v) => changer('reponse', v)} taille={200} liste="rig-messagerie-boites" />
            </div>
            <div className="rig-criteres__paire">
              <Saisie libelle="Copie cachée" valeur={saisie.copie} changer={(v) => changer('copie', v)} taille={200} liste="rig-messagerie-boites" />
              <Saisie libelle="Destinataire par défaut" valeur={saisie.destinataireDefaut} changer={(v) => changer('destinataireDefaut', v)} taille={200} />
            </div>
            {a && saisie.origine !== null && (
              <p style={Note} aria-label="Adresses pour ce greffe">
                Pour ce greffe : de {a.expediteur || '— (à compléter)'}{a.reponse ? ` ; réponse à ${a.reponse}` : ''}{a.copie ? ` ; copie cachée ${a.copie}` : ''}
              </p>
            )}

            <div className="rig-kicker">Listes par défaut</div>
            <div style={Bascules} aria-label="Listes par défaut">
              {listesACocher.map((l) => (
                <Bascule key={l.id} libelle={`${l.code} — ${l.libelle}`} vrai={saisie.listes.includes(l.id)} basculer={() => basculerListeDefaut(l.id)} />
              ))}
              {listesACocher.length === 0 && <span style={Note}>Aucune liste de distribution active.</span>}
            </div>

            <div className="rig-kicker">Utilisation</div>
            <div style={Bascules} aria-label="Cases du modèle">
              <Bascule libelle="Utilisable RCS" vrai={saisie.lot1} basculer={basculerRcs} />
              <Bascule libelle="Utilisable judiciaire" vrai={saisie.lot2} basculer={() => changer('lot2', !saisie.lot2)} />
              <Bascule libelle="Utilisable privilège" vrai={saisie.inscription} basculer={() => changer('inscription', !saisie.inscription)} />
              <Bascule libelle="Utilisable comptabilité" vrai={saisie.comptabilite} basculer={() => changer('comptabilite', !saisie.comptabilite)} />
              {vue.estAmitel && (
                <Bascule libelle={DomaineAmitel.libelle} vrai={saisie.reserveAmitel} basculer={() => changer('reserveAmitel', !saisie.reserveAmitel)} />
              )}
              {/* V2 _SetVisibilityChkEnvoiAuto : l'envoi automatique n'existe que pour un modele RCS. */}
              {saisie.lot1 && (
                <Bascule libelle={LibelleEnvoiAuto} vrai={saisie.envoiAutomatique} basculer={() => changer('envoiAutomatique', !saisie.envoiAutomatique)} />
              )}
            </div>

            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Texte du modèle<span className="rig-saisie-champ__requis">requis</span></span>
              <textarea className="rig-saisie-champ__saisie" aria-label="Texte du modèle" rows={7} value={saisie.texte}
                        onChange={(e) => changer('texte', e.target.value)} />
            </label>
            <div style={Bascules}>
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
