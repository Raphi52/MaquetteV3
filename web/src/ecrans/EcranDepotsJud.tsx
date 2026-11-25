// Ecran de la procedure PROC_DEPOTJUD.
//
// Etapes lues dans Processus/PROC_DEPOTJUD.Designer.cs (RIG v2), avec le
// libelle que v2 leur donne :
//   ETP_INFO_DEMANDE               « Demande »
//   ETP_INSTANCE_RECHERCHE         « Etape de recherche d'instance »
//   ETP_ENTITE_JUDICIAIRE_READONLY « Entite judiciaire (lecture seule) »
//   ETP_INSTANCE_SUMMARY           « Informations sur l'instance »
//   ETP_EVENEMENT_SURVENU          « Nouvel evenement survenu »
//   ETP_FACTU_GESTCOM              « Facturation et gestion commerciale »
//   ETP_POST_VAL_INSTANCE          « Recapitulatif instance »
// La maquette garde cet enchainement.
//
// Porte depuis Components/Ecrans/EcranDepotsJud.razor (lot 5) : meme balisage,
// memes classes CSS. La date du jour (valeur de depart de l'evenement) vient
// de Pont/EcranDepotsJud.cs ; le reste etait declare dans l'ecran et l'a suivi.
// La coquille .razor monte cet ecran et recoit ouvrirDossier.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 2) : la colonne des resultats prend
// les briques de composants/ListeModerne.tsx. Tete (le titre reste celui de l'etape, synthese, « Aller a une
// instance… » Ctrl K, Entree ouvre le dossier de l'instance restee seule), tableau a une ligne par instance
// (Instance, Nature, Date d'ouverture, Dernier evenement, Etat) ; chambre, « A facturer » et dossier sont dans la
// ligne depliee, avec « Ouvrir le dossier ». Ni vues ni cartes : l'ecran n'a pas de critere d'etat (les etapes
// sont l'enchainement de la v2, pas un filtre d'etat).
// « Aller a une instance… » EST l'ancien critere « N d'instance » (meme etat, numero d'instance cherche) : la
// liste est filtree ici, sans pont, deux champs auraient fait le meme filtre (decision du plan, lot 7).
// « Denomination » reste un critere a part, comme en v2. Etapes, evenement et clic qui ouvre le dossier : inchanges.

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, dateCourte, estBlanc } from '../criteres'
import {
  CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, MenuLigne, Pastille, PiedListe, PointEtat,
  type ActionLigne, type Teinte,
} from '../composants/ListeModerne'
import type { ContexteDossierVue } from '../plan/types'

interface Etape {
  code: string
  libelle: string
}

interface Instance {
  numero: string
  denomination: string
  nature: string
  chambre: string
  /** aaaa-mm-jj */
  ouverture: string
  dernierEvenement: string
  etat: string
  statut: string
  aFacturer: boolean
  dossier: string
}

const Etapes: Etape[] = [
  { code: 'ETP_INFO_DEMANDE', libelle: 'Demande' },
  { code: 'ETP_INSTANCE_RECHERCHE', libelle: 'Etape de recherche d\'instance' },
  { code: 'ETP_ENTITE_JUDICIAIRE_READONLY', libelle: 'Entite judiciaire (lecture seule)' },
  { code: 'ETP_INSTANCE_SUMMARY', libelle: 'Informations sur l\'instance' },
  { code: 'ETP_EVENEMENT_SURVENU', libelle: 'Nouvel evenement survenu' },
  { code: 'ETP_FACTU_GESTCOM', libelle: 'Facturation et gestion commerciale' },
  { code: 'ETP_POST_VAL_INSTANCE', libelle: 'Recapitulatif instance' },
]

const Lignes: Instance[] = [
  { numero: '2026J00121', denomination: 'SARL BOULANGERIE DUVAL', nature: 'Redressement judiciaire', chambre: '1re', ouverture: '2026-03-04', dernierEvenement: 'Jugement d\'ouverture', etat: 'En cours', statut: 'action', aFacturer: true, dossier: '2024B00318' },
  { numero: '2026J00134', denomination: 'SAS TRANSPORTS MARNE', nature: 'Liquidation judiciaire', chambre: '2e', ouverture: '2026-05-19', dernierEvenement: 'Designation d\'expert', etat: 'En cours', statut: 'attente', aFacturer: false, dossier: '2024B00407' },
  { numero: '2025J00088', denomination: 'EURL COIFFURE MARIE', nature: 'Sauvegarde', chambre: '1re', ouverture: '2025-11-02', dernierEvenement: 'Ordonnance', etat: 'Suspendue', statut: 'alerte', aFacturer: true, dossier: '2023B01120' },
  { numero: '2024J00507', denomination: 'SCI DU MOULIN', nature: 'Redressement judiciaire', chambre: '3e', ouverture: '2024-06-12', dernierEvenement: 'Cloture', etat: 'Close', statut: 'ok', aFacturer: false, dossier: '2025B00044' },
]

/** Le point d'etat suit le statut de l'instance ; la pastille, la nature de la procedure. */
const TeinteStatut: Record<string, Teinte> = { action: 'bleu', attente: 'orange', alerte: 'rouge', ok: 'vert' }
const TeinteNature: Record<string, Teinte> = { 'Redressement judiciaire': 'orange', 'Liquidation judiciaire': 'rouge', Sauvegarde: 'vert' }

/** Les colonnes du tableau a une ligne : chevron, Instance, Nature, Date d'ouverture, Dernier evenement, Etat, menu. */
const NbColonnes = 7

export interface ProprietesEcranDepotsJud {
  /** Remonte le NUMERO de dossier de l'instance cliquee : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranDepotsJud({ ouvrirDossier, dossier = null }: ProprietesEcranDepotsJud) {
  const [etapeCourante, setEtapeCourante] = useState('ETP_INSTANCE_RECHERCHE')
  // « Aller a une instance… » de la tete (l'ancien critere « N d'instance ») : ouvert depuis une affaire, son
  // instance y est cherchee par son numero.
  const [numInstance, setNumInstance] = useState(dossier?.numero ?? '')
  const [denomination, setDenomination] = useState('')
  const [evenement, setEvenement] = useState('JUG')
  // null tant que le pont n'a pas repondu (ou une fois la date effacee).
  const [dateEvenement, setDateEvenement] = useState<string | null>(null)
  // La ligne depliee par son chevron (une a la fois).
  const [ouverte, setOuverte] = useState<string | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranDepotsJud.DateDuJour')
      .then((d) => { if (!perime) setDateEvenement((avant) => avant ?? d) })
    return () => { perime = true }
  }, [])

  let q = Lignes
  if (!estBlanc(numInstance)) q = q.filter((l) => contientSansCasse(l.numero, numInstance.trim()))
  if (!estBlanc(denomination)) q = q.filter((l) => contientSansCasse(l.denomination, denomination.trim()))
  if (etapeCourante === 'ETP_FACTU_GESTCOM') q = q.filter((l) => l.aFacturer)
  const trouvees = q

  // Les chiffres de la tete et du pied : sur la liste affichee.
  const enCours = trouvees.filter((l) => l.etat === 'En cours').length
  const aFacturer = trouvees.filter((l) => l.aFacturer).length

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Etapes du depot</div>

        {Etapes.map((etape) => (
          <button key={etape.code} className={`rig-bascule${etapeCourante === etape.code ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setEtapeCourante(etape.code)}>
            <span className="rig-bascule__temoin"></span>
            <span>{etape.libelle}</span>
          </button>
        ))}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Instance</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Denomination</span>
            <input className="rig-saisie-champ__saisie" value={denomination} onChange={(e) => setDenomination(e.target.value)} />
          </label>
        </div>
      </aside>

      <ListeModerne etiquette="Instances">
        <EnteteListe titre={Etapes.find((e) => e.code === etapeCourante)!.libelle}
                     synthese={<>{trouvees.length}{trouvees.length > 1 ? ' instances · ' : ' instance · '}<b>{enCours}</b>
                       {' en cours · '}<b>{aFacturer}</b>{' a facturer'}</>}
                     filtre={{ valeur: numInstance, changer: setNumInstance, indication: 'Aller a une instance…',
                               valider: () => { if (trouvees.length === 1) ouvrirDossier(trouvees[0]!.dossier) } }} />

        {etapeCourante === 'ETP_EVENEMENT_SURVENU' && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Evenement survenu</span>
              <select className="rig-saisie-champ__saisie" value={evenement} onChange={(e) => setEvenement(e.target.value)}>
                <option value="JUG">Jugement</option>
                <option value="ORD">Ordonnance</option>
                <option value="EXP">Designation d'expert</option>
                <option value="CLO">Cloture</option>
              </select>
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Date de l'evenement</span>
              <input className="rig-saisie-champ__saisie" type="date" value={dateEvenement ?? ''}
                     onChange={(e) => setDateEvenement(e.target.value || null)} />
            </label>
          </div>
        )}

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col /><col style={{ width: '19%' }} /><col style={{ width: '13%' }} />
              <col style={{ width: '18%' }} /><col style={{ width: '12%' }} /><col style={{ width: 34 }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Instance</th>
                <th>Nature</th>
                <th>Date d'ouverture</th>
                <th>Dernier evenement</th>
                <th>Etat</th>
                <th aria-label="Actions"></th>
              </tr>
            </thead>
            <tbody>
              {trouvees.map((inst) => {
                const estOuverte = ouverte === inst.numero
                const actions: ActionLigne[] = [{ libelle: 'Ouvrir le dossier', fort: true, agir: () => ouvrirDossier(inst.dossier) }]
                return (
                  <Fragment key={inst.numero}>
                  <tr className={estOuverte ? 'rig-lm-ouverte' : undefined} onClick={() => ouvrirDossier(inst.dossier)}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : inst.numero)} />
                    <CelluleIdentite nom={inst.denomination} objet={inst.numero} titre={`${inst.denomination} · ${inst.numero}`} />
                    <td title={inst.nature}><Pastille teinte={TeinteNature[inst.nature] ?? 'gris'}>{inst.nature}</Pastille></td>
                    <td className="rig-table__mono">{dateCourte(inst.ouverture)}</td>
                    <td title={inst.dernierEvenement}>{inst.dernierEvenement}</td>
                    <td title={inst.etat}><PointEtat teinte={TeinteStatut[inst.statut] ?? 'gris'}>{inst.etat}</PointEtat></td>
                    <td className="rig-lm-cellule-menu">
                      <MenuLigne libelle={`Actions de l'instance ${inst.numero}`} actions={[
                        ...actions,
                        { libelle: estOuverte ? 'Replier la ligne' : 'Deplier la ligne', agir: () => setOuverte(estOuverte ? null : inst.numero) },
                      ]} />
                    </td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes} actions={actions} champs={[
                      { libelle: 'Chambre', valeur: inst.chambre },
                      { libelle: 'A facturer', valeur: inst.aFacturer ? 'oui' : '' },
                      { libelle: 'Dossier', valeur: inst.dossier },
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {trouvees.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">Aucune instance ne repond a cette recherche.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{trouvees.length}</b>{trouvees.length > 1 ? ' instances affichees' : ' instance affichee'}</span>
          <span>En cours <b>{enCours}</b></span>
          <span>A facturer <b>{aFacturer}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
