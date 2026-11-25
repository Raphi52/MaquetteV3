// Ecran de la procedure PROC_ARRIVEE.
// Blocs de criteres lus dans RIG v2 : OPE_ARRIVEE.Designer.cs ("Arrivee",
// "Date d'arrivee", "Mode d'arrivee") et OPE_DEMANDE.Designer.cs
// ("N Demande", "Type de demande", "Utilisateur designe", "Numerisation",
// "Attestation de depot"). Colonnes : table ARRIVEE (ARRV_*).
//
// Porte depuis Components/Ecrans/EcranArrivee.razor (lot 3) : meme balisage,
// memes classes CSS. Le filtre est fait par Pont/EcranArrivee.cs sur
// JeuDemo.Arrivees ; la coquille .razor monte cet ecran et recoit ouvrirDossier.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 3) : les resultats prennent les briques
// de composants/ListeModerne.tsx. Tete (synthese, « Aller a une arrivee… » Ctrl K qui filtre la liste affichee,
// Entree ouvre la demande si une seule arrivee reste), trois cartes qui POSENT les criteres existants (mode
// d'arrivee, « Reste a numeriser », « Paiement joint »), des vues = jeux de criteres de gauche, export CSV. Le
// tableau passe a UNE ligne par arrivee (Expediteur, Mode, N Demande, Numerisation, Date et heure) ; N Arrivee,
// References, Case, Paiement, Type de demande, Utilisateur designe et Attestation sont dans la ligne depliee.
// Le clic qui ouvre la demande, la ligne inerte sans demande et la ligne en alerte ne changent pas.

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { dateSaisie, useNombreSaisi } from '../saisie'
import {
  CartesChiffrees, CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, MenuLigne, Pastille,
  PiedListe, PointEtat, VuesListe, exporterCsv, type ActionLigne, type Teinte,
} from '../composants/ListeModerne'

export interface OptionArrivee {
  code: string
  libelle: string
}

export interface LigneArrivee {
  numero: string
  horodatage: string
  modeLisible: string
  nom: string
  references: string
  case: string
  paiementJoint: boolean
  numDemande: string
  typeDemande: string
  utilisateur: string
  numerise: boolean
  attestation: boolean
}

export interface VueArrivee {
  modes: OptionArrivee[]
  lignes: LigneArrivee[]
  /** Toutes les arrivees connues, filtre ou pas. */
  total: number
  aNumeriser: number
}

export interface ProprietesEcranArrivee {
  /** Remonte le numero de dossier a ouvrir. */
  ouvrirDossier: (numero: string) => void
}

/** Les criteres de gauche, tels que saisis. « Nombre maxi. » n'en est pas : c'est une limite d'affichage. */
export interface CriteresArrivee {
  critNumArrivee: string
  critNomArrivee: string
  critReferences: string
  critMode: string
  critCase: string
  critDu: string
  critAu: string
  critPaiement: boolean
  critNonNumerise: boolean
}

const SansCritere: CriteresArrivee = {
  critNumArrivee: '', critNomArrivee: '', critReferences: '', critMode: '', critCase: '', critDu: '', critAu: '',
  critPaiement: false, critNonNumerise: false,
}

/** Ce que le pont recoit (Pont/EcranArrivee.cs, CriteresArrivee) : les dates vides deviennent null. */
function pourLePont(c: CriteresArrivee, topMax: number) {
  return {
    critNumArrivee: c.critNumArrivee, critNomArrivee: c.critNomArrivee, critReferences: c.critReferences,
    critMode: c.critMode, critCase: c.critCase, critDu: dateSaisie(c.critDu), critAu: dateSaisie(c.critAu),
    critPaiement: c.critPaiement, critNonNumerise: c.critNonNumerise, critTopMax: topMax,
  }
}

function memesCriteres(a: CriteresArrivee, b: CriteresArrivee): boolean {
  return (Object.keys(SansCritere) as (keyof CriteresArrivee)[]).every((k) => a[k] === b[k])
}

/** Une vue enregistree : un nom et un jeu complet de criteres. */
export interface VueArriveeEnregistree {
  cle: string
  libelle: string
  criteres: CriteresArrivee
}

/** Les vues toujours la : les criteres de l'ecran, preposes. */
export const VuesPrereglees: VueArriveeEnregistree[] = [
  { cle: 'toutes', libelle: 'Toutes', criteres: SansCritere },
  { cle: 'a-numeriser', libelle: 'Reste a numeriser', criteres: { ...SansCritere, critNonNumerise: true } },
  { cle: 'paiement', libelle: 'Paiement joint', criteres: { ...SansCritere, critPaiement: true } },
  { cle: 'edi', libelle: 'Teletransmises', criteres: { ...SansCritere, critMode: 'EDI' } },
]

/** Les vues ajoutees par « + Nouvelle vue » : elles vivent le temps de la session (aucun reglage ne les porte). */
const VuesDeLaSession: VueArriveeEnregistree[] = []

/** Pour les tests : repartir d'une session sans vue ajoutee. */
export function oublierVuesDeLaSession(): void {
  VuesDeLaSession.length = 0
}

/** La teinte d'un mode d'arrivee (libelles de dbo.MODE_ARRIVEE) ; l'ecran des teletransmissions la reprend. */
export function teinteModeArrivee(libelle: string): Teinte {
  switch (libelle) {
    case 'Accueil': return 'vert'
    case 'Poste': return 'bleu'
    case 'Recommande': return 'orange'
    case 'Edi': return 'violet'
    case 'e-Mail': return 'ardoise'
    default: return 'gris'
  }
}

/** Les colonnes du tableau a une ligne : chevron, Expediteur, Mode, N Demande, Numerisation, Date et heure, menu. */
const NbColonnes = 7

/**
 * « Aller a… » compare sans casse ni accents, comme la Recherche d'affaires : « réf » trouve « REF ».
 * fix-ok: contientSansCasse (criteres.ts) garde les accents ; test « Aller a une arrivee… » rouge sur « réf », vert ici.
 */
function normaliser(texte: string): string {
  return texte.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '')
}

export function EcranArrivee({ ouvrirDossier }: ProprietesEcranArrivee) {
  const [critNumArrivee, setCritNumArrivee] = useState('')
  const [critNomArrivee, setCritNomArrivee] = useState('')
  const [critReferences, setCritReferences] = useState('')
  const [critMode, setCritMode] = useState('')
  const [critCase, setCritCase] = useState('')
  const [critDu, setCritDu] = useState('')
  const [critAu, setCritAu] = useState('')
  const [critPaiement, setCritPaiement] = useState(false)
  const [critNonNumerise, setCritNonNumerise] = useState(false)
  const topMax = useNombreSaisi(50)
  // null tant que le pont n'a pas repondu : ni ligne ni « aucune arrivee ».
  const [vue, setVue] = useState<VueArrivee | null>(null)
  // La ligne depliee par son chevron (une a la fois).
  const [ouverte, setOuverte] = useState<string | null>(null)

  const criteres: CriteresArrivee = {
    critNumArrivee, critNomArrivee, critReferences, critMode, critCase, critDu, critAu, critPaiement, critNonNumerise,
  }
  function poserCriteres(c: CriteresArrivee) {
    setCritNumArrivee(c.critNumArrivee)
    setCritNomArrivee(c.critNomArrivee)
    setCritReferences(c.critReferences)
    setCritMode(c.critMode)
    setCritCase(c.critCase)
    setCritDu(c.critDu)
    setCritAu(c.critAu)
    setCritPaiement(c.critPaiement)
    setCritNonNumerise(c.critNonNumerise)
  }

  useEffect(() => {
    let perime = false
    void appeler<VueArrivee>('EcranArrivee.Trouver', pourLePont({
      critNumArrivee, critNomArrivee, critReferences, critMode, critCase, critDu, critAu, critPaiement, critNonNumerise,
    }, topMax.valeur)).then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [critNumArrivee, critNomArrivee, critReferences, critMode, critCase, critDu, critAu,
      critPaiement, critNonNumerise, topMax.valeur])

  const trouvees = vue?.lignes ?? []

  // « Aller a une arrivee… » (Ctrl K) : filtre la liste AFFICHEE, sans toucher aux criteres du pont.
  const [filtreRapide, setFiltreRapide] = useState('')
  const cherche = filtreRapide.trim()
  const chercheNormal = normaliser(cherche)
  const affichees = cherche === '' ? trouvees : trouvees.filter((a) => normaliser(
    [a.numero, a.nom, a.references, a.numDemande, a.typeDemande, a.case, a.utilisateur].join(' ')).includes(chercheNormal))

  // Les vues : les preposees, puis celles de la session. Leur compte est demande au pont, critere par critere,
  // avec la meme limite d'affichage : c'est le nombre de lignes que la vue MONTRERAIT.
  const [vuesAjoutees, setVuesAjoutees] = useState<VueArriveeEnregistree[]>(() => [...VuesDeLaSession])
  const vues = [...VuesPrereglees, ...vuesAjoutees]
  const [comptes, setComptes] = useState<number[] | null>(null)
  const cleVues = JSON.stringify(vues.map((v) => v.criteres))
  useEffect(() => {
    let perime = false
    Promise.all((JSON.parse(cleVues) as CriteresArrivee[]).map((c) =>
      appeler<VueArrivee>('EcranArrivee.Trouver', pourLePont(c, topMax.valeur)).then((v) => v.lignes.length))).then(
      (c) => { if (!perime) setComptes(c) },
      // Sans reponse, les puces restent sans compte.
      () => { if (!perime) setComptes(null) })
    return () => { perime = true }
  }, [cleVues, topMax.valeur])
  const vueActive = vues.find((v) => memesCriteres(v.criteres, criteres))?.cle ?? null
  function enregistrerVue(nom: string) {
    VuesDeLaSession.push({ cle: `session-${VuesDeLaSession.length + 1}`, libelle: nom, criteres })
    setVuesAjoutees([...VuesDeLaSession])
  }

  /**
   * Une arrivee mene a SA demande. Toutes n en ont pas : un pli peut
   * arriver avant qu une demande soit creee (colis, navette interne).
   */
  function ouvrir(a: LigneArrivee) {
    if (a.numDemande.trim() === '') return
    ouvrirDossier(a.numDemande)
  }

  function effacer() {
    setCritNumArrivee('')
    setCritNomArrivee('')
    setCritReferences('')
    setCritMode('')
    setCritCase('')
    setCritDu('')
    setCritAu('')
    setCritPaiement(false)
    setCritNonNumerise(false)
    topMax.poser(50)
  }

  // Les chiffres de la tete, des cartes et du pied : sur la liste AFFICHEE.
  const aNumeriser = affichees.filter((a) => !a.numerise).length
  const avecPaiement = affichees.filter((a) => a.paiementJoint).length
  const modes = vue?.modes ?? []
  const parMode = (libelle: string) => affichees.filter((a) => a.modeLisible === libelle).length

  function exporter() {
    exporterCsv('arrivees.csv',
      ['N Arrivee', 'Date et heure', 'Mode', 'Nom', 'References', 'Case', 'Paiement', 'N Demande', 'Type de demande',
       'Utilisateur designe', 'Numerisation', 'Attestation'],
      affichees.map((a) => [a.numero, a.horodatage, a.modeLisible, a.nom, a.references, a.case, a.paiementJoint ? 'oui' : '',
                            a.numDemande, a.typeDemande, a.utilisateur, a.numerise ? 'faite' : 'a faire', a.attestation ? 'oui' : '']))
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Arrivee</span>
          <input className="rig-saisie-champ__saisie" placeholder="n arrivee ou n demande"
                 value={critNumArrivee} onChange={(e) => setCritNumArrivee(e.target.value)} />
          <span className="rig-saisie-champ__colonne">ARRV_NUM_ARRIVEE</span>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nom</span>
          <input className="rig-saisie-champ__saisie" placeholder="expediteur"
                 value={critNomArrivee} onChange={(e) => setCritNomArrivee(e.target.value)} />
          <span className="rig-saisie-champ__colonne">ARRV_NOM</span>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">References</span>
          <input className="rig-saisie-champ__saisie" placeholder="n de suivi, reference"
                 value={critReferences} onChange={(e) => setCritReferences(e.target.value)} />
          <span className="rig-saisie-champ__colonne">ARRV_REFERENCES</span>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Mode d arrivee</span>
          <select className="rig-saisie-champ__saisie" value={critMode} onChange={(e) => setCritMode(e.target.value)}>
            <option value="">Tous</option>
            {(vue?.modes ?? []).map((m) => (
              <option key={m.code} value={m.code}>{m.libelle}</option>
            ))}
          </select>
          <span className="rig-saisie-champ__colonne">ARRV_MODE_ARRIVEE · dbo.MODE_ARRIVEE</span>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date d arrivee</div>
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

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Case courrier</span>
          <input className="rig-saisie-champ__saisie" placeholder="C-01"
                 value={critCase} onChange={(e) => setCritCase(e.target.value)} />
          <span className="rig-saisie-champ__colonne">ARRV_CASE_COURRIER</span>
        </label>

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${critPaiement ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritPaiement(!critPaiement)}>
            <span className="rig-bascule__temoin"></span>
            <span>Paiement joint</span>
          </button>
          <button className={`rig-bascule${critNonNumerise ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritNonNumerise(!critNonNumerise)}>
            <span className="rig-bascule__temoin"></span>
            <span>Reste a numeriser</span>
          </button>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nombre maxi. d arrivees affichees</span>
          <input className="rig-saisie-champ__saisie" type="number" min="1" max="500"
                 value={topMax.texte} onChange={(e) => topMax.saisir(e.target.value)} />
        </label>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <ListeModerne etiquette="Courrier arrive">
        <EnteteListe titre="Courrier arrive"
                     synthese={vue && <>{affichees.length}{' affichee(s) sur '}{vue.total}{' · '}<b>{aNumeriser}</b>{' a numeriser · '}
                       <b>{avecPaiement}</b>{' avec paiement'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une arrivee…',
                               valider: () => { if (affichees.length === 1) ouvrir(affichees[0]!) } }} />

        {vue && (
          <CartesChiffrees cartes={[
            { cle: 'modes', titre: 'Modes d arrivee', valeur: affichees.length, complement: affichees.length > 1 ? 'arrivees' : 'arrivee',
              legende: true,
              ...(critMode !== '' ? { choisir: () => setCritMode(''), indication: 'Tous les modes' } : {}),
              // Seuls les modes presents (ou le mode choisi) : la legende des huit modes noierait la carte.
              segments: modes.filter((m) => parMode(m.libelle) > 0 || m.code === critMode).map((m) => ({
                cle: m.code, libelle: m.libelle, teinte: teinteModeArrivee(m.libelle),
                valeur: parMode(m.libelle), texte: String(parMode(m.libelle)),
                actif: critMode === m.code, choisir: () => setCritMode(critMode === m.code ? '' : m.code),
              })) },
            { cle: 'a-numeriser', titre: 'Reste a numeriser', valeur: aNumeriser, complement: `sur ${affichees.length}`,
              anneau: { part: aNumeriser, total: affichees.length, teinte: 'orange' },
              actif: critNonNumerise, choisir: () => setCritNonNumerise(!critNonNumerise),
              indication: critNonNumerise ? 'Toutes les arrivees' : 'Seulement les arrivees a numeriser' },
            { cle: 'paiement', titre: 'Paiement joint', valeur: avecPaiement, complement: `sur ${affichees.length}`,
              anneau: { part: avecPaiement, total: affichees.length, teinte: 'vert' },
              actif: critPaiement, choisir: () => setCritPaiement(!critPaiement),
              indication: critPaiement ? 'Toutes les arrivees' : 'Seulement les arrivees avec paiement joint' },
          ]} />
        )}

        <VuesListe vues={vues.map((v, i) => ({ cle: v.cle, libelle: v.libelle, compte: comptes?.[i] }))} active={vueActive}
                   choisir={(cle) => { const v = vues.find((x) => x.cle === cle); if (v) poserCriteres(v.criteres) }}
                   enregistrer={enregistrerVue}>
          <button type="button" className="rig-lm-outil" disabled={affichees.length === 0} onClick={exporter}>Exporter</button>
        </VuesListe>

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col /><col style={{ width: '13%' }} /><col style={{ width: '15%' }} />
              <col style={{ width: '14%' }} /><col style={{ width: '17%' }} /><col style={{ width: 34 }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Expediteur</th>
                <th>Mode</th>
                <th>N Demande</th>
                <th>Numerisation</th>
                <th>Date et heure</th>
                <th aria-label="Actions"></th>
              </tr>
            </thead>
            <tbody>
              {affichees.map((a, i) => {
                const cle = `${i}:${a.numero}`
                const liee = a.numDemande.trim() !== ''
                const estOuverte = ouverte === cle
                const classe = [a.numerise ? '' : 'rig-table__ligne--alerte', liee ? '' : 'rig-table__ligne--inerte',
                                estOuverte ? 'rig-lm-ouverte' : ''].filter((c) => c !== '').join(' ')
                const ouvrirDemande: ActionLigne = { libelle: 'Ouvrir la demande', fort: true, desactive: !liee, agir: () => ouvrir(a) }
                return (
                  <Fragment key={cle}>
                  <tr className={classe || undefined}
                      title={liee ? `Ouvrir la demande ${a.numDemande}` : 'Aucune demande rattachee a cette arrivee'}
                      onClick={() => ouvrir(a)}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : cle)} />
                    <CelluleIdentite nom={a.nom}
                                     objet={<>{a.typeDemande}{a.typeDemande !== '' ? ' · ' : ''}<span>{a.numero}</span></>} />
                    <td><Pastille teinte={teinteModeArrivee(a.modeLisible)}>{a.modeLisible}</Pastille></td>
                    <td className="rig-table__mono">
                      {liee ? a.numDemande : <span className="rig-table__sans">non rattachee</span>}
                    </td>
                    <td>{a.numerise ? <PointEtat teinte="vert">faite</PointEtat> : <PointEtat teinte="orange">a faire</PointEtat>}</td>
                    <td className="rig-table__mono">{a.horodatage}</td>
                    <td className="rig-lm-cellule-menu">
                      <MenuLigne libelle={`Actions de l'arrivee ${a.numero}`} actions={[
                        ouvrirDemande,
                        { libelle: estOuverte ? 'Replier la ligne' : 'Deplier la ligne', agir: () => setOuverte(estOuverte ? null : cle) },
                      ]} />
                    </td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes} actions={[ouvrirDemande]} champs={[
                      { libelle: 'N Arrivee', valeur: a.numero },
                      { libelle: 'References', valeur: a.references },
                      { libelle: 'Case', valeur: a.case },
                      { libelle: 'Paiement joint', valeur: a.paiementJoint ? 'oui' : 'non' },
                      { libelle: 'Type de demande', valeur: a.typeDemande },
                      { libelle: 'Utilisateur designe', valeur: a.utilisateur },
                      { libelle: 'Attestation', valeur: a.attestation ? 'oui' : 'non' },
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {vue && trouvees.length === 0 && (
                <tr>
                  <td colSpan={NbColonnes} className="rig-table__vide">
                    Aucune arrivee ne repond a ces criteres.
                  </td>
                </tr>
              )}
              {trouvees.length > 0 && affichees.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">
                  {`Aucune des ${trouvees.length} arrivees ne contient « ${cherche} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe aide="Clic sur une arrivee pour ouvrir sa demande.">
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' arrivees affichees' : ' arrivee affichee'}</span>
          <span>A numeriser <b>{aNumeriser}</b></span>
          <span>Paiement joint <b>{avecPaiement}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
