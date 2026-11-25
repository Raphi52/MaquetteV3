// Ecran de la procedure PROC_CLOT.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_CLOT/PROC_CLOT) :
// v2 ne presente pas UNE grille mais DEUX, a deux etapes differentes, avec
// des colonnes distinctes (HeaderText releves dans les .Designer.cs) :
//
//   - « 1-4-5 Selection PC » : N PC, Nom de l Affaire, Chambre, Date
//     d audience, Heure d appel, Heure de debut, Type de classement,
//     A facturer, Limite Examen, Dernier courrier envoye, Dirigeants,
//     Liquidateurs, Forfait, Huissier, Code Postal - Ville
//   - « 2-3 Enrolement Requete » : N Role, N PC, Demandeur, Defendeur,
//     Acte de saisine, Audience, Depot, Facturation
//
// La maquette montrait une grille unique melangeant les deux. Elle presente
// desormais les deux etapes en onglets, chacune avec SES colonnes.
//
// Porte depuis Components/Ecrans/EcranCloture.razor (lot 4) : meme balisage,
// memes classes CSS. La date du jour (JeuDemo.Aujourdhui), a laquelle se
// compare la limite d'examen, vient de Pont/EcranCloture.cs ; les affaires de
// demonstration etaient declarees dans l'ecran et l'ont suivi. La coquille
// .razor recoit ouvrirDossier.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 4) : la liste prend les briques de
// composants/ListeModerne.tsx. Les deux etapes restent deux onglets (ce sont deux jeux de colonnes, pas des
// criteres). Tete (compte, a facturer, total des forfaits, « Aller a une affaire… » Ctrl K, Entree ouvre le dossier
// reste seul ; ce champ EST le critere « Affaire » de gauche -- nom ou n de role, meme etat, il pose aussi les vues
// et se remplit depuis une affaire : un seul champ pour un seul filtre), trois cartes qui POSENT les criteres
// existants (Forfaits par classement, A facturer, Limite d'examen depassee), des vues = jeux de criteres
// dont « + Nouvelle vue » pour la session. « Selection PC » : une ligne par affaire (avatar, nom, N PC · chambre ;
// classement en pastille ; limite en point rouge si depassee ; forfait avec sa mini-barre), heures, courrier,
// dirigeants, liquidateurs, huissier et ville dans la ligne depliee. « Enrolement / requete » : 6 colonnes, N Role
// et N PC sous le defendeur. Criteres, clic qui ouvre le dossier et ligne en alerte ne changent pas.

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'
import {
  CartesChiffrees, CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, MenuLigne, MontantBarre, Pastille,
  PiedListe, PointEtat, VuesListe, montant, type ActionLigne, type Teinte,
} from '../composants/ListeModerne'
import type { ContexteDossierVue } from '../plan/types'

interface LigneClot {
  role: string
  affaire: string
  demandeur: string
  defendeur: string
  chambre: string
  /** aaaa-mm-jj */
  audience: string
  heureAppel: string
  classement: string
  /** aaaa-mm-jj */
  limiteExamen: string
  aFacturer: boolean
  dernierCourrier: string
  dossier: string
  numPc: string
  heureDebut: string
  dirigeants: string
  liquidateurs: string
  forfait: string
  huissier: string
  codePostalVille: string
  acteSaisine: string
  /** aaaa-mm-jj */
  depot: string
  facturation: string
}

const Classements = ['Insuffisance d\'actif', 'Extinction du passif', 'Conversion en liquidation', 'Report']

const Lignes: LigneClot[] = [
  { role: '2024/00318', affaire: 'SARL BOULANGERIE DUVAL', demandeur: 'Me LEROY', defendeur: 'SARL BOULANGERIE DUVAL', chambre: '1re', audience: '2026-09-15', heureAppel: '09:00', classement: 'Insuffisance d\'actif', limiteExamen: '2026-09-12', aFacturer: true, dernierCourrier: '28/08/2026', dossier: '2024B00318', numPc: 'PC-2024-0118', heureDebut: '08:30', dirigeants: 'DUVAL Marc', liquidateurs: 'Me LEROY', forfait: '450,00', huissier: 'SCP HUISSIERS REIMS', codePostalVille: '51100 Reims', acteSaisine: 'Assignation', depot: '2026-03-04', facturation: 'Facturee' },
  { role: '2024/00407', affaire: 'SAS TRANSPORTS MARNE', demandeur: 'Me BLANC', defendeur: 'SAS TRANSPORTS MARNE', chambre: '2e', audience: '2026-09-15', heureAppel: '09:30', classement: 'Extinction du passif', limiteExamen: '2026-09-05', aFacturer: false, dernierCourrier: '12/08/2026', dossier: '2024B00407', numPc: 'PC-2024-0207', heureDebut: '09:00', dirigeants: 'MARTIN Luc', liquidateurs: 'Me BLANC', forfait: '450,00', huissier: '', codePostalVille: '51000 Chalons', acteSaisine: 'Requete', depot: '2026-05-19', facturation: 'A facturer' },
  { role: '2023/01120', affaire: 'EURL COIFFURE MARIE', demandeur: 'Me LEROY', defendeur: 'EURL COIFFURE MARIE', chambre: '1re', audience: '2026-09-22', heureAppel: '10:15', classement: 'Conversion en liquidation', limiteExamen: '2026-09-20', aFacturer: true, dernierCourrier: '02/09/2026', dossier: '2023B01120', numPc: 'PC-2023-0920', heureDebut: '10:00', dirigeants: 'MARIE Sophie', liquidateurs: 'Me LEROY', forfait: '620,00', huissier: 'SCP HUISSIERS REIMS', codePostalVille: '51100 Reims', acteSaisine: 'Assignation', depot: '2025-11-02', facturation: 'Facturee' },
  { role: '2025/00044', affaire: 'SCI DU MOULIN', demandeur: 'Me PETIT', defendeur: 'SCI DU MOULIN', chambre: '3e', audience: '2026-09-29', heureAppel: '11:00', classement: 'Report', limiteExamen: '2026-10-02', aFacturer: false, dernierCourrier: '', dossier: '2025B00044', numPc: 'PC-2025-0044', heureDebut: '10:45', dirigeants: 'BERTIN Andre', liquidateurs: 'Me PETIT', forfait: '380,00', huissier: '', codePostalVille: '51200 Epernay', acteSaisine: 'Requete', depot: '2024-06-12', facturation: 'A facturer' },
  { role: '2022/00876', affaire: 'SARL PLOMBERIE NORD', demandeur: 'Me BLANC', defendeur: 'SARL PLOMBERIE NORD', chambre: '2e', audience: '2026-10-06', heureAppel: '14:00', classement: 'Insuffisance d\'actif', limiteExamen: '2026-09-30', aFacturer: true, dernierCourrier: '19/07/2026', dossier: '2022B00876', numPc: 'PC-2022-0876', heureDebut: '13:30', dirigeants: 'NOEL Patrick', liquidateurs: 'Me BLANC', forfait: '450,00', huissier: 'SCP HUISSIERS NORD', codePostalVille: '51100 Reims', acteSaisine: 'Assignation', depot: '2022-09-08', facturation: 'Facturee' },
]

const Chambres = distinctsTries(Lignes.map((l) => l.chambre))

const TeinteClassement: Record<string, Teinte> = {
  'Insuffisance d\'actif': 'orange', 'Extinction du passif': 'vert', 'Conversion en liquidation': 'rouge', Report: 'ardoise',
}
const TeinteSaisine: Record<string, Teinte> = { Assignation: 'orange', Requete: 'bleu' }
const TeinteFacturation: Record<string, Teinte> = { Facturee: 'vert', 'A facturer': 'orange' }

/** Le forfait en euros (« 450,00 » -> 450). */
const forfaitDe = (l: LigneClot): number => Number(l.forfait.replace(/\s/g, '').replace(',', '.')) || 0

/** Les criteres de gauche, tels que saisis. Une vue en est un jeu complet. */
export interface CriteresCloture {
  critAffaire: string
  critChambre: string
  critDu: string
  critAu: string
  critAFacturer: boolean
  critExamenDepasse: boolean
  critTypes: ReadonlySet<string>
}

const SansCritere: CriteresCloture = {
  critAffaire: '', critChambre: '', critDu: '', critAu: '', critAFacturer: false, critExamenDepasse: false, critTypes: new Set(),
}

/** La limite d'examen est-elle depassee ? Jamais tant que la date du jour n'est pas connue. */
const enRetardLe = (aujourdhui: string | null) => (l: LigneClot) => aujourdhui !== null && l.limiteExamen < aujourdhui

/** Les affaires qui repondent aux criteres (le filtre de l'ecran, inchange). */
function filtrer(c: CriteresCloture, aujourdhui: string | null): LigneClot[] {
  let trouvees = Lignes
  if (!estBlanc(c.critAffaire)) {
    const t = c.critAffaire.trim()
    trouvees = trouvees.filter((l) => contientSansCasse(l.affaire, t) || contientSansCasse(l.role, t))
  }
  if (!estBlanc(c.critChambre)) trouvees = trouvees.filter((l) => l.chambre === c.critChambre)
  if (c.critDu !== '') trouvees = trouvees.filter((l) => l.audience >= c.critDu)
  if (c.critAu !== '') trouvees = trouvees.filter((l) => l.audience <= c.critAu)
  if (c.critTypes.size > 0) trouvees = trouvees.filter((l) => c.critTypes.has(l.classement))
  if (c.critAFacturer) trouvees = trouvees.filter((l) => l.aFacturer)
  if (c.critExamenDepasse) trouvees = trouvees.filter(enRetardLe(aujourdhui))
  return trouvees
}

const memeEnsemble = (a: ReadonlySet<string>, b: ReadonlySet<string>) => a.size === b.size && [...a].every((v) => b.has(v))
const memesCriteres = (a: CriteresCloture, b: CriteresCloture) =>
  a.critAffaire === b.critAffaire && a.critChambre === b.critChambre && a.critDu === b.critDu && a.critAu === b.critAu
  && a.critAFacturer === b.critAFacturer && a.critExamenDepasse === b.critExamenDepasse && memeEnsemble(a.critTypes, b.critTypes)

/** Une vue enregistree : un nom et un jeu complet de criteres. */
export interface VueCloture {
  cle: string
  libelle: string
  criteres: CriteresCloture
}

/** Les vues toujours la : les criteres de facturation, d'examen et de classement de l'ecran. */
export const VuesPrereglees: VueCloture[] = [
  { cle: 'toutes', libelle: 'Toutes', criteres: SansCritere },
  { cle: 'a-facturer', libelle: 'A facturer', criteres: { ...SansCritere, critAFacturer: true } },
  { cle: 'examen-depasse', libelle: 'Limite d\'examen depassee', criteres: { ...SansCritere, critExamenDepasse: true } },
  { cle: 'insuffisance', libelle: 'Insuffisance d\'actif', criteres: { ...SansCritere, critTypes: new Set(['Insuffisance d\'actif']) } },
]

/** Les vues ajoutees par « + Nouvelle vue » pendant la session (aucun reglage ne les porte, decision du lot 1). */
const VuesDeLaSession: VueCloture[] = []

/** Pour les tests : repartir d'une session sans vue ajoutee. */
export function oublierVuesDeLaSession(): void {
  VuesDeLaSession.length = 0
}

/** Colonnes de « Selection PC » : chevron, Affaire, Audience, Classement, Limite, A facturer, Forfait, menu. */
const NbColonnesSelection = 8
/** Colonnes de « Enrolement / requete » : Defendeur, Demandeur, Acte de saisine, Audience, Depot, Facturation. */
const NbColonnesEnrolement = 6

export interface ProprietesEcranCloture {
  /** Remonte le dossier a ouvrir dans la visionneuse. */
  ouvrirDossier: (dossier: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranCloture({ ouvrirDossier, dossier = null }: ProprietesEcranCloture) {
  // v2 presente ces affaires a deux etapes distinctes, avec des colonnes
  // differentes : « Selection PC » puis « Enrolement / requete ».
  const [etapeSelection, setEtapeSelection] = useState(true)
  // Ouvert depuis une affaire : cherchee par son NOM, ce que prend le critere
  // « Affaire » (nom ou n de role) -- son numero de dossier n'est pas un n de role.
  const [critAffaire, setCritAffaire] = useState(dossier?.nom ?? '')
  const [critChambre, setCritChambre] = useState('')
  const [critDu, setCritDu] = useState('')
  const [critAu, setCritAu] = useState('')
  const [critAFacturer, setCritAFacturer] = useState(false)
  const [critExamenDepasse, setCritExamenDepasse] = useState(false)
  const [critTypes, setCritTypes] = useState<ReadonlySet<string>>(new Set())
  // JeuDemo.Aujourdhui : null tant que le pont n'a pas repondu (aucune ligne
  // n'est alors marquee en retard).
  const [aujourdhui, setAujourdhui] = useState<string | null>(null)
  // La ligne depliee par son chevron (etape « Selection PC »).
  const [ouverte, setOuverte] = useState<string | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranCloture.DateDuJour').then((d) => { if (!perime) setAujourdhui(d) })
    return () => { perime = true }
  }, [])

  const enRetard = enRetardLe(aujourdhui)
  const criteres: CriteresCloture = { critAffaire, critChambre, critDu, critAu, critAFacturer, critExamenDepasse, critTypes }

  function basculer(t: string) {
    setCritTypes((avant) => {
      const s = new Set(avant)
      if (!s.delete(t)) s.add(t)
      return s
    })
  }

  /** Pose un jeu complet de criteres : une vue choisie, ou « Effacer ». */
  function poserCriteres(c: CriteresCloture) {
    setCritAffaire(c.critAffaire); setCritChambre(c.critChambre)
    setCritDu(c.critDu); setCritAu(c.critAu)
    setCritAFacturer(c.critAFacturer); setCritExamenDepasse(c.critExamenDepasse)
    setCritTypes(new Set(c.critTypes))
  }

  function effacer() {
    poserCriteres(SansCritere)
  }

  const trouvees = filtrer(criteres, aujourdhui)

  const [vuesAjoutees, setVuesAjoutees] = useState<VueCloture[]>(() => [...VuesDeLaSession])
  const vues = [...VuesPrereglees, ...vuesAjoutees]
  const vueActive = vues.find((v) => memesCriteres(v.criteres, criteres))?.cle ?? null
  function enregistrerVue(nom: string) {
    VuesDeLaSession.push({ cle: `session-${VuesDeLaSession.length + 1}`, libelle: nom, criteres: { ...criteres, critTypes: new Set(critTypes) } })
    setVuesAjoutees([...VuesDeLaSession])
  }

  // Les chiffres de la tete, des cartes et du pied : sur la liste affichee.
  const forfaits = trouvees.reduce((s, l) => s + forfaitDe(l), 0)
  const forfaitMax = trouvees.reduce((m, l) => Math.max(m, forfaitDe(l)), 0)
  const aFacturer = trouvees.filter((l) => l.aFacturer).length
  const depassees = trouvees.filter(enRetard).length
  const parClassement = (c: string) => trouvees.filter((l) => l.classement === c)
  const nbColonnes = etapeSelection ? NbColonnesSelection : NbColonnesEnrolement

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres d'examen</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Chambre</span>
          <select className="rig-saisie-champ__saisie" value={critChambre} onChange={(e) => setCritChambre(e.target.value)}>
            <option value="">Toutes</option>
            {Chambres.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date d'audience</div>
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

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Type de classement</div>
          {Classements.map((v) => (
            <button key={v} className={`rig-bascule${critTypes.has(v) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculer(v)}>
              <span className="rig-bascule__temoin"></span>
              <span>{v}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${critAFacturer ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritAFacturer(!critAFacturer)}>
            <span className="rig-bascule__temoin"></span>
            <span>A facturer uniquement</span>
          </button>
          <button className={`rig-bascule${critExamenDepasse ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritExamenDepasse(!critExamenDepasse)}>
            <span className="rig-bascule__temoin"></span>
            <span>Limite d'examen depassee</span>
          </button>
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <ListeModerne etiquette="Procedures collectives a cloturer">
        <div className="rig-onglets" style={{ margin: 'var(--rig-esp-3) var(--rig-esp-4) 0' }}>
          <button className={`rig-onglet${etapeSelection ? ' rig-onglet--actif' : ''}`}
                  onClick={() => setEtapeSelection(true)}>Selection PC</button>
          <button className={`rig-onglet${etapeSelection ? '' : ' rig-onglet--actif'}`}
                  onClick={() => setEtapeSelection(false)}>Enrolement / requete</button>
        </div>

        <EnteteListe titre={etapeSelection ? 'Selection des procedures collectives' : 'Enrolement et requete'}
                     synthese={<>{trouvees.length}{trouvees.length > 1 ? ' affaires' : ' affaire'}{` sur ${Lignes.length} · `}
                       <b>{aFacturer}</b>{' a facturer · forfaits '}<b>{montant(forfaits)}</b></>}
                     filtre={{ valeur: critAffaire, changer: setCritAffaire, indication: 'Aller a une affaire…',
                               valider: () => { if (trouvees.length === 1) ouvrirDossier(trouvees[0]!.dossier) } }} />

        <CartesChiffrees cartes={[
          { cle: 'forfaits', titre: 'Forfaits', valeur: montant(forfaits), legende: true,
            ...(critTypes.size > 0 ? { choisir: () => setCritTypes(new Set()), indication: 'Tous les classements' } : {}),
            segments: Classements.map((c) => {
              const somme = parClassement(c).reduce((s, l) => s + forfaitDe(l), 0)
              return { cle: c, libelle: c, valeur: somme, texte: montant(somme), teinte: TeinteClassement[c] ?? 'gris',
                       actif: critTypes.has(c), choisir: () => basculer(c) }
            }) },
          { cle: 'a-facturer', titre: 'A facturer', valeur: aFacturer, complement: `sur ${trouvees.length}`,
            anneau: { part: aFacturer, total: trouvees.length, teinte: 'orange' },
            actif: critAFacturer, choisir: () => setCritAFacturer(!critAFacturer),
            indication: critAFacturer ? 'Toutes les affaires' : 'Seulement les affaires a facturer' },
          { cle: 'examen', titre: 'Limite d\'examen depassee', valeur: depassees, complement: `sur ${trouvees.length}`,
            anneau: { part: depassees, total: trouvees.length, teinte: 'rouge' },
            actif: critExamenDepasse, choisir: () => setCritExamenDepasse(!critExamenDepasse),
            indication: critExamenDepasse ? 'Toutes les affaires' : 'Seulement les limites depassees' },
        ]} />

        <VuesListe vues={vues.map((v) => ({ cle: v.cle, libelle: v.libelle, compte: filtrer(v.criteres, aujourdhui).length }))}
                   active={vueActive} enregistrer={enregistrerVue}
                   choisir={(cle) => { const v = vues.find((x) => x.cle === cle); if (v) poserCriteres(v.criteres) }} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            {etapeSelection ? (
              <colgroup>
                <col style={{ width: 30 }} /><col /><col style={{ width: '15%' }} /><col style={{ width: '19%' }} />
                <col style={{ width: '13%' }} /><col style={{ width: '10%' }} /><col style={{ width: '14%' }} /><col style={{ width: 34 }} />
              </colgroup>
            ) : (
              <colgroup>
                <col /><col style={{ width: '13%' }} /><col style={{ width: '15%' }} /><col style={{ width: '12%' }} />
                <col style={{ width: '12%' }} /><col style={{ width: '14%' }} />
              </colgroup>
            )}
            <thead>
              {etapeSelection ? (
                // Colonnes de « 1-4-5 Selection PC » (RIG v2) : les secondaires sont dans la ligne depliee.
                <tr>
                  <th aria-label="Deplier"></th>
                  <th>Nom de l'Affaire</th>
                  <th>Date d'audience</th>
                  <th>Type de classement</th>
                  <th>Limite Examen</th>
                  <th>A facturer</th>
                  <th className="rig-lm-droite">Forfait</th>
                  <th aria-label="Actions"></th>
                </tr>
              ) : (
                // Colonnes de « 2-3 Enrolement Requete » (RIG v2) : N Role et N PC sous le defendeur.
                <tr>
                  <th>Defendeur</th>
                  <th>Demandeur</th>
                  <th>Acte de saisine</th>
                  <th>Audience</th>
                  <th>Depot</th>
                  <th>Facturation</th>
                </tr>
              )}
            </thead>
            <tbody>
              {trouvees.map((ligne) => {
                const retard = enRetard(ligne)
                const estOuverte = etapeSelection && ouverte === ligne.role
                const ouvrir: ActionLigne = { libelle: 'Ouvrir le dossier', agir: () => ouvrirDossier(ligne.dossier) }
                const classes = [retard ? 'rig-table__ligne--alerte' : '', estOuverte ? 'rig-lm-ouverte' : ''].filter(Boolean).join(' ')
                return (
                  <Fragment key={ligne.role}>
                  <tr className={classes} onClick={() => ouvrirDossier(ligne.dossier)}>
                    {etapeSelection ? (
                      <>
                        <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : ligne.role)} />
                        <CelluleIdentite nom={ligne.affaire} objet={`${ligne.numPc} · ${ligne.chambre}`}
                                         titre={`${ligne.affaire} — ${ligne.numPc} · chambre ${ligne.chambre}`} />
                        <td className="rig-table__mono">{dateCourte(ligne.audience)} {ligne.heureAppel}</td>
                        <td title={ligne.classement}><Pastille teinte={TeinteClassement[ligne.classement] ?? 'gris'}>{ligne.classement}</Pastille></td>
                        <td>
                          <PointEtat teinte={aujourdhui === null ? 'gris' : retard ? 'rouge' : 'vert'}>{dateCourte(ligne.limiteExamen)}</PointEtat>
                        </td>
                        <td>{ligne.aFacturer && <PointEtat teinte="orange">oui</PointEtat>}</td>
                        <td className="rig-lm-droite" title={`${ligne.forfait} €`}>
                          <MontantBarre valeur={forfaitDe(ligne)} max={forfaitMax} texte={montant(forfaitDe(ligne))} />
                        </td>
                        <td className="rig-lm-cellule-menu">
                          <MenuLigne libelle={`Actions de l'affaire ${ligne.numPc}`} actions={[
                            ouvrir,
                            { libelle: estOuverte ? 'Replier la ligne' : 'Deplier la ligne', agir: () => setOuverte(estOuverte ? null : ligne.role) },
                          ]} />
                        </td>
                      </>
                    ) : (
                      <>
                        <CelluleIdentite nom={ligne.defendeur} objet={`${ligne.role} · ${ligne.numPc}`}
                                         titre={`${ligne.defendeur} — role ${ligne.role} · ${ligne.numPc}`} />
                        <td title={ligne.demandeur}>{ligne.demandeur}</td>
                        <td><Pastille teinte={TeinteSaisine[ligne.acteSaisine] ?? 'gris'}>{ligne.acteSaisine}</Pastille></td>
                        <td className="rig-table__mono">{dateCourte(ligne.audience)}</td>
                        <td className="rig-table__mono">{dateCourte(ligne.depot)}</td>
                        <td><PointEtat teinte={TeinteFacturation[ligne.facturation] ?? 'gris'}>{ligne.facturation}</PointEtat></td>
                      </>
                    )}
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnesSelection} actions={[{ ...ouvrir, fort: true }]} champs={[
                      { libelle: 'Heure de debut', valeur: ligne.heureDebut },
                      { libelle: 'Dernier courrier envoye', valeur: ligne.dernierCourrier },
                      { libelle: 'Dirigeants', valeur: ligne.dirigeants },
                      { libelle: 'Liquidateurs', valeur: ligne.liquidateurs },
                      { libelle: 'Huissier', valeur: ligne.huissier },
                      { libelle: 'Code Postal - Ville', valeur: ligne.codePostalVille },
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {trouvees.length === 0 && (
                <tr><td colSpan={nbColonnes} className="rig-table__vide">Aucune affaire ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe aide="Clic sur une ligne pour ouvrir le dossier.">
          <span><b>{trouvees.length}</b>{trouvees.length > 1 ? ' affaires affichees' : ' affaire affichee'}</span>
          <span>Forfaits <b>{montant(forfaits)}</b></span>
          <span>A facturer <b>{aFacturer}</b></span>
          <span>Limite depassee <b>{depassees}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
