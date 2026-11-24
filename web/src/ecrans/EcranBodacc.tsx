// Ecran de la procedure BODACC.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/BODACC/PROC_ENV_BOD) :
// v2 a DEUX grilles distinctes, pas une, avec des colonnes differentes
// (HeaderText releves dans Controls/) :
//
//   - Controls/BodaccToSendRigDataGridView.cs (« a envoyer ») :
//     Num PC ou prevention, Code eve pro, Date eve pro, Texte Bodacc,
//     Date prevue d envoi, Type annonce
//   - Controls/BodaccAlreadySentRigDataGridView.cs (« deja envoye ») :
//     Num PC ou prevention, Code eve pro, Date eve pro, Date envoi,
//     Date trans., Num bord., Num fact., Date parution, Num parution,
//     Num ann., Type annonce, Texte Bodacc
//
// Criteres : Operations/OPE_RECH_BOD.Designer.cs -> TextRechByNum,
// TextNumBordereau, TextNumFacture, DateDu, DateAu.
//
// Porte depuis Components/Ecrans/EcranBodacc.razor (lot 5) : meme balisage,
// memes classes CSS. La date du jour, qui dit si une annonce a envoyer est en
// retard, vient de Pont/EcranBodacc.cs ; le reste etait declare dans l'ecran
// et l'a suivi. La coquille .razor monte cet ecran et recoit ouvrirDossier.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 3) : les annonces prennent les briques
// de composants/ListeModerne.tsx. Les deux onglets v2 deviennent les deux puces de vues (« BODACC a envoyer »,
// « BODACC deja envoye », avec leur compte) ; la tete fait la synthese (retards, parutions), le type d'annonce
// passe en pastille. Le texte de l'annonce reste une cellule simple, sans avatar : ce n'est pas un nom, et une
// copie des classes de CelluleIdentite n'est pas permise (decision du plan, lot 4). « A envoyer » garde ses six
// colonnes v2 ; « deja envoye » en avait douze : sa ligne garde Texte, Type, Date envoi, Date parution, Num
// parution, et le reste (Num ann., Num PC, code et date eve pro, date de transmission, bordereau, facture, et
// le dossier que le clic ouvre) est dans la ligne depliee par le chevron. Le critere « Num annonce / dossier », filtre sur place sans le pont, devient
// le champ « Aller a une annonce… » (Ctrl K) de la tete : meme etat, memes colonnes comparees,
// retire du panneau de gauche (decision du plan, lot 2). « Effacer » le vide toujours. Criteres, retard surligne
// et clic qui ouvre le dossier ne changent pas.

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'
import type { ContexteDossierVue } from '../plan/types'
import {
  CelluleChevron, EnteteListe, LigneDetail, ListeModerne, MenuLigne, Pastille, PiedListe, PointEtat,
  VuesListe, type ActionLigne, type Teinte,
} from '../composants/ListeModerne'

interface Annonce {
  numAnnonce: string
  numPc: string
  typeAnnonce: string
  codeEvePro: string
  /** Les dates sont au format aaaa-mm-jj ; null = pas encore arrive. */
  dateEvePro: string
  prevueEnvoi: string
  envoi: string | null
  numParution: string
  parution: string | null
  numBord: string
  numFact: string
  texte: string
  dossier: string
  transmission: string | null
}

const Lignes: Annonce[] = [
  { numAnnonce: 'A2026-1188', numPc: 'PC2026-041', typeAnnonce: 'Immatriculation', codeEvePro: 'IMM', dateEvePro: '2026-09-02', prevueEnvoi: '2026-09-08', envoi: null, numParution: '', parution: null, numBord: '', numFact: '', texte: 'Immatriculation SARL LES TILLEULS', dossier: '2026B00811', transmission: null },
  { numAnnonce: 'A2026-1189', numPc: '', typeAnnonce: 'Modification', codeEvePro: 'MOD', dateEvePro: '2026-09-03', prevueEnvoi: '2026-09-09', envoi: null, numParution: '', parution: null, numBord: '', numFact: '', texte: 'Changement de gerant', dossier: '2019B00233', transmission: null },
  { numAnnonce: 'A2026-1190', numPc: 'PC2026-042', typeAnnonce: 'Procedure collective', codeEvePro: 'PCO', dateEvePro: '2026-09-01', prevueEnvoi: '2026-09-04', envoi: null, numParution: '', parution: null, numBord: '', numFact: '', texte: 'Ouverture de redressement', dossier: '2026B00790', transmission: null },
  { numAnnonce: 'A2026-1150', numPc: '', typeAnnonce: 'Radiation', codeEvePro: 'RAD', dateEvePro: '2026-08-12', prevueEnvoi: '2026-08-18', envoi: '2026-08-18', numParution: '2026-171', parution: '2026-08-24', numBord: 'BD-2610', numFact: 'FA-45120', texte: 'Radiation EURL LE PETIT CAFE', dossier: '2004B01344', transmission: '2026-08-19' },
  { numAnnonce: 'A2026-1151', numPc: '', typeAnnonce: 'Depot des comptes', codeEvePro: 'DCA', dateEvePro: '2026-08-14', prevueEnvoi: '2026-08-20', envoi: '2026-08-20', numParution: '2026-172', parution: '2026-08-26', numBord: 'BD-2610', numFact: 'FA-45121', texte: 'Depot des comptes 2025', dossier: '2011B00512', transmission: '2026-08-21' },
]

/** Les annonces de l'ecran, que la palette Ctrl K cherche aussi (composants/Palette.tsx). */
export { Lignes as AnnoncesBodacc, type Annonce as AnnonceBodacc }

const date = (d: string | null): string => (d === null ? '' : dateCourte(d))

/** La teinte d'un type d'annonce ; un type inconnu reste gris. */
const TeinteType: Record<string, Teinte> = {
  Immatriculation: 'vert', Modification: 'bleu', 'Procedure collective': 'orange', Radiation: 'rouge', 'Depot des comptes': 'ardoise',
}

/** Les colonnes de « deja envoye » a une ligne : chevron, Annonce, Type, Date envoi, Date parution, Num parution, menu. */
const NbColonnesEnvoye = 7

export interface ProprietesEcranBodacc {
  /** Remonte le NUMERO de dossier de l'annonce cliquee : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranBodacc({ ouvrirDossier, dossier = null }: ProprietesEcranBodacc) {
  const [aEnvoyer, setAEnvoyer] = useState(true)
  const [critNumBord, setCritNumBord] = useState('')
  const [critNumFact, setCritNumFact] = useState('')

  // Ouvert depuis une affaire : ses annonces, par son numero dans « Num annonce / dossier ».
  const [critTexte, setCritTexte] = useState(dossier?.numero ?? '')
  const [critType, setCritType] = useState('')
  const [critDu, setCritDu] = useState('')
  const [critAu, setCritAu] = useState('')
  // null tant que le pont n'a pas repondu : sans elle, on ne sait pas quelle
  // annonce est en retard, et les lignes attendent plutot que de le taire.
  const [aujourdhui, setAujourdhui] = useState<string | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranBodacc.DateDuJour').then((d) => { if (!perime) setAujourdhui(d) })
    return () => { perime = true }
  }, [])

  function effacer() {
    setCritTexte(''); setCritType('')
    setCritDu(''); setCritAu('')
  }

  /** Les annonces d'un onglet (a envoyer ou deja envoye) qui repondent aux criteres de gauche. */
  function filtrees(onglet: boolean): Annonce[] {
    let q = Lignes.filter((l) => (l.envoi === null) === onglet)
    if (!estBlanc(critTexte)) {
      const t = critTexte.trim()
      q = q.filter((l) => contientSansCasse(l.numAnnonce, t) || contientSansCasse(l.dossier, t))
    }
    if (critType !== '') q = q.filter((l) => l.typeAnnonce === critType)
    if (critDu !== '') q = q.filter((l) => l.prevueEnvoi >= critDu)
    if (critAu !== '') q = q.filter((l) => l.prevueEnvoi <= critAu)
    return q
  }
  const trouvees = filtrees(aEnvoyer)
  // La ligne depliee de « deja envoye » (une a la fois).
  const [ouverte, setOuverte] = useState<string | null>(null)

  const enRetard = aujourdhui === null ? 0 : trouvees.filter((l) => aEnvoyer && l.prevueEnvoi < aujourdhui).length
  const parues = trouvees.filter((l) => l.parution !== null).length

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Recherche BODACC</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type d'annonce</span>
          <select className="rig-saisie-champ__saisie" value={critType} onChange={(e) => setCritType(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(Lignes.map((l) => l.typeAnnonce)).map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Date prevue d'envoi</div>
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

        {/* v2 : TextNumBordereau et TextNumFacture (OPE_RECH_BOD). */}
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Num bordereau</span>
          <input className="rig-saisie-champ__saisie" value={critNumBord} onChange={(e) => setCritNumBord(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Num facture</span>
          <input className="rig-saisie-champ__saisie" value={critNumFact} onChange={(e) => setCritNumFact(e.target.value)} />
        </label>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <ListeModerne etiquette="Annonces BODACC">
        <EnteteListe titre="Annonces BODACC"
                     synthese={aujourdhui === null ? undefined : (aEnvoyer
                       ? <>{trouvees.length}{' annonce(s) a envoyer · '}<b>{enRetard}</b>{' en retard'}</>
                       : <>{trouvees.length}{' annonce(s) envoyee(s) · '}<b>{parues}</b>{' parue(s)'}</>)}
                     filtre={{ valeur: critTexte, changer: setCritTexte, indication: 'Aller a une annonce…' }} />

        {/* Les deux onglets v2 : une puce par grille, avec le nombre d'annonces qu'elle montre pour ces criteres. */}
        <VuesListe vues={[
                     { cle: 'a-envoyer', libelle: 'BODACC a envoyer', compte: filtrees(true).length },
                     { cle: 'envoye', libelle: 'BODACC deja envoye', compte: filtrees(false).length },
                   ]}
                   active={aEnvoyer ? 'a-envoyer' : 'envoye'}
                   choisir={(cle) => { setAEnvoyer(cle === 'a-envoyer'); setOuverte(null) }} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            {aEnvoyer ? (
              <colgroup>
                <col style={{ width: '14%' }} /><col style={{ width: '9%' }} /><col style={{ width: '11%' }} /><col />
                <col style={{ width: '15%' }} /><col style={{ width: '19%' }} />
              </colgroup>
            ) : (
              <colgroup>
                <col style={{ width: 30 }} /><col /><col style={{ width: '19%' }} /><col style={{ width: '12%' }} />
                <col style={{ width: '13%' }} /><col style={{ width: '12%' }} /><col style={{ width: 34 }} />
              </colgroup>
            )}
            <thead>
              {aEnvoyer ? (
                // Colonnes de BodaccToSendRigDataGridView.cs (RIG v2).
                <tr>
                  <th>Num PC ou prevention</th>
                  <th>Code eve pro</th>
                  <th>Date eve pro</th>
                  <th>Texte Bodacc</th>
                  <th>Date prevue d'envoi</th>
                  <th>Type annonce</th>
                </tr>
              ) : (
                // Colonnes de BodaccAlreadySentRigDataGridView.cs (RIG v2) : les secondaires sont dans la ligne depliee.
                <tr>
                  <th aria-label="Deplier"></th>
                  <th>Texte Bodacc</th>
                  <th>Type annonce</th>
                  <th>Date envoi</th>
                  <th>Date parution</th>
                  <th>Num parution</th>
                  <th aria-label="Actions"></th>
                </tr>
              )}
            </thead>
            <tbody>
              {aujourdhui !== null && trouvees.map((ligne) => {
                const retard = aEnvoyer && ligne.prevueEnvoi < aujourdhui
                const type = <Pastille teinte={TeinteType[ligne.typeAnnonce] ?? 'gris'}>{ligne.typeAnnonce}</Pastille>
                if (aEnvoyer) {
                  return (
                    <tr key={ligne.numAnnonce} className={retard ? 'rig-table__ligne--alerte' : ''}
                        onClick={() => ouvrirDossier(ligne.dossier)}>
                      <td className="rig-table__mono">{ligne.numPc}</td>
                      <td className="rig-table__mono">{ligne.codeEvePro}</td>
                      <td className="rig-table__mono">{dateCourte(ligne.dateEvePro)}</td>
                      <td title={ligne.texte}>{ligne.texte}</td>
                      <td title={retard ? 'Envoi en retard' : undefined}>
                        <PointEtat teinte={retard ? 'rouge' : 'bleu'}>{dateCourte(ligne.prevueEnvoi)}</PointEtat>
                      </td>
                      <td title={ligne.typeAnnonce}>{type}</td>
                    </tr>
                  )
                }
                const estOuverte = ouverte === ligne.numAnnonce
                const ouvrir: ActionLigne = { libelle: 'Ouvrir le dossier', fort: true, agir: () => ouvrirDossier(ligne.dossier) }
                return (
                  <Fragment key={ligne.numAnnonce}>
                    <tr className={estOuverte ? 'rig-lm-ouverte' : ''} onClick={() => ouvrirDossier(ligne.dossier)}>
                      <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : ligne.numAnnonce)} />
                      <td title={ligne.texte}>{ligne.texte}</td>
                      <td title={ligne.typeAnnonce}>{type}</td>
                      <td className="rig-table__mono">{date(ligne.envoi)}</td>
                      <td className="rig-table__mono">{date(ligne.parution)}</td>
                      <td className="rig-table__mono">{ligne.numParution}</td>
                      <td className="rig-lm-cellule-menu">
                        <MenuLigne libelle={`Actions de l'annonce ${ligne.numAnnonce}`} actions={[
                          ouvrir,
                          { libelle: estOuverte ? 'Replier la ligne' : 'Deplier la ligne', agir: () => setOuverte(estOuverte ? null : ligne.numAnnonce) },
                        ]} />
                      </td>
                    </tr>
                    {estOuverte && (
                      <LigneDetail colonnes={NbColonnesEnvoye} actions={[ouvrir]} champs={[
                        { libelle: 'Num ann.', valeur: ligne.numAnnonce },
                        { libelle: 'Dossier', valeur: ligne.dossier },
                        { libelle: 'Num PC ou prevention', valeur: ligne.numPc },
                        { libelle: 'Code eve pro', valeur: ligne.codeEvePro },
                        { libelle: 'Date eve pro', valeur: dateCourte(ligne.dateEvePro) },
                        { libelle: 'Date trans.', valeur: date(ligne.transmission) },
                        { libelle: 'Num bord.', valeur: ligne.numBord },
                        { libelle: 'Num fact.', valeur: ligne.numFact },
                      ]} />
                    )}
                  </Fragment>
                )
              })}
              {aujourdhui !== null && trouvees.length === 0 && (
                <tr><td colSpan={aEnvoyer ? 6 : NbColonnesEnvoye} className="rig-table__vide">Aucune annonce ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe aide="Clic sur une annonce pour ouvrir son dossier.">
          <span><b>{trouvees.length}</b>{trouvees.length > 1 ? ' annonces affichees' : ' annonce affichee'}</span>
          {aEnvoyer ? <span>En retard <b>{enRetard}</b></span> : <span>Parues <b>{parues}</b></span>}
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
