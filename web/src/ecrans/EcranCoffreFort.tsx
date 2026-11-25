// Ecran de la procedure PROC_COFFRE_FORT.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_COFFRE_FORT) :
//   - OPE_RECHERCHE_COFFRE   : ult_DateDebut, ult_DateFin (bornes), et les
//     filtres de nature ult_Archive, ult_Audience, ult_Erreur, ult_GedJud,
//     ult_Message, ult_Reda
//   - OPE_FILTRE_DESTINATAIRE : cboDest, CBChoixDestinataire, txtNbLigneHisto
//   - OPE_DESTINATAIRE        : txtNomCoffre, cboType, cboMand, dtDateDemarrage
//   - OPE_PARAMETRAGE_COFFRE  : ult_GroupRadioButton1 (coffre par defaut)
//   - OPE_TREEVIEW_TRANSMETTRE / OPE_TREEVIEW_RETRANSMETTRE : les deux listes
// Colonnes des grilles : Code, Abrege, Destinataire, Etat, Date envoi
// infogreffe, Date AR Infogreffe, Date AR destinataire, Date lecture
// destinataire, Date traitement RIG, Date avis de depot coffre,
// Code de depot dans le coffre, Erreur.
//
// Porte depuis Components/Ecrans/EcranCoffreFort.razor (lot 4) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// depots de demonstration l'ont suivi ici.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 4) : la liste prend les briques de
// composants/ListeModerne.tsx. Tete (compte, depots en erreur, « Aller a un depot… » Ctrl K -- ce champ EST le
// critere « Code ou destinataire » de gauche, meme etat : un seul champ pour un seul filtre), les onglets
// « A transmettre / Deja transmis » deviennent des puces comptees (sans « + Nouvelle vue » : ce sont deux listes,
// pas un jeu de criteres), une ligne par depot (avatar et destinataire, code · abrege ; etat en point colore),
// les six dates de suivi, le coffre et le type de decision dans la ligne depliee. Aucun montant : pas de carte.
// Le clic qui montre l'historique, la ligne en alerte et les autres criteres ne changent pas.

import { Fragment, useState } from 'react'
import { contientSansCasse, dateCourte, distinctsTries, entierLisible, estBlanc } from '../criteres'
import {
  CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, PiedListe, PointEtat, VuesListe, teinteStatut,
} from '../composants/ListeModerne'

interface DepotCoffre {
  code: string
  abrege: string
  destinataire: string
  coffre: string
  typeDecision: string
  etat: string
  statut: string
  /** aaaa-mm-jj, ou null */
  envoiInfogreffe: string | null
  arInfogreffe: string | null
  arDestinataire: string | null
  lectureDestinataire: string | null
  traitementRig: string | null
  avisDepotCoffre: string | null
  codeDepot: string
  erreur: string
  historique: string[]
}

const Depots: DepotCoffre[] = [
  {
    code: 'CF-2026-0412', abrege: 'DUVAL', destinataire: 'Me LEROY', coffre: 'COFFRE_LEROY', typeDecision: 'ACAD', etat: 'Transmis', statut: 'ok',
    envoiInfogreffe: '2026-03-07', arInfogreffe: '2026-03-07', arDestinataire: '2026-03-08', lectureDestinataire: '2026-03-09', traitementRig: '2026-03-07', avisDepotCoffre: '2026-03-08',
    codeDepot: 'DEP-77120', erreur: '', historique: ['07/03/2026 — depose', '08/03/2026 — accuse de reception', '09/03/2026 — lu'],
  },
  {
    code: 'CF-2026-0518', abrege: 'MARNE', destinataire: 'Me BLANC', coffre: 'COFFRE_BLANC', typeDecision: 'ACAD', etat: 'Transmis', statut: 'ok',
    envoiInfogreffe: '2026-06-03', arInfogreffe: '2026-06-03', arDestinataire: '2026-06-04', lectureDestinataire: null, traitementRig: '2026-06-03', avisDepotCoffre: '2026-06-04',
    codeDepot: 'DEP-77455', erreur: '', historique: ['03/06/2026 — depose', '04/06/2026 — accuse de reception'],
  },
  {
    code: 'CF-2026-0603', abrege: 'MARIE', destinataire: 'Me LEROY', coffre: 'COFFRE_LEROY', typeDecision: 'CD', etat: 'A envoyer', statut: 'attente',
    envoiInfogreffe: null, arInfogreffe: null, arDestinataire: null, lectureDestinataire: null, traitementRig: '2026-09-01', avisDepotCoffre: null,
    codeDepot: '', erreur: '', historique: ['01/09/2026 — prepare'],
  },
  {
    code: 'CF-2026-0610', abrege: 'MOULIN', destinataire: 'Me PETIT', coffre: 'COFFRE_PETIT', typeDecision: 'CD', etat: 'En erreur', statut: 'alerte',
    envoiInfogreffe: '2026-09-04', arInfogreffe: null, arDestinataire: null, lectureDestinataire: null, traitementRig: '2026-09-04', avisDepotCoffre: null,
    codeDepot: '', erreur: 'Destinataire inconnu', historique: ['04/09/2026 — envoi refuse par le coffre'],
  },
]

const Natures = ['Archive', 'Audience', 'Erreur', 'GED judiciaire', 'Message', 'REDA']

const Coffres = distinctsTries(Depots.map((d) => d.coffre))

const jour = (d: string | null) => (d === null ? '' : dateCourte(d))

/** Colonnes : chevron, Destinataire, Etat, Date envoi infogreffe, Code de depot, Erreur. */
const NbColonnes = 6

/** Un depot est « a transmettre » s'il n'est jamais parti ou s'il est en erreur (l'onglet de l'ecran). */
const aTransmettreLe = (d: DepotCoffre) => d.envoiInfogreffe === null || d.erreur !== ''

export function EcranCoffreFort() {
  const [recherche, setRecherche] = useState('')
  const [coffre, setCoffre] = useState('')
  const [typeDecision, setTypeDecision] = useState('')
  // Criteres de OPE_RECHERCHE_COFFRE (RIG v2) : vides au depart.
  const [dateDebut, setDateDebut] = useState('')
  const [dateFin, setDateFin] = useState('')
  const [naturesActives, setNaturesActives] = useState<ReadonlySet<string>>(new Set())
  // Le texte saisi et la derniere valeur LISIBLE (le @bind d'un int).
  const [nbLignesTexte, setNbLignesTexte] = useState('20')
  const [nbLignesHisto, setNbLignesHisto] = useState(20)
  const [coffreDefaut, setCoffreDefaut] = useState('COFFRE_LEROY')
  const [aTransmettre, setATransmettre] = useState(true)
  const [selection, setSelection] = useState<DepotCoffre | null>(null)
  // La ligne depliee par son chevron.
  const [ouverte, setOuverte] = useState<string | null>(null)

  function basculerNature(n: string) {
    setNaturesActives((avant) => {
      const s = new Set(avant)
      if (!s.delete(n)) s.add(n)
      return s
    })
  }

  function saisirNbLignes(texte: string) {
    setNbLignesTexte(texte)
    const n = entierLisible(texte)
    if (n !== null) setNbLignesHisto(n)
  }

  /** Les depots d'un onglet qui repondent aux criteres (le filtre de l'ecran, inchange). */
  function filtrer(transmettre: boolean): DepotCoffre[] {
    let trouves = Depots.filter((d) => aTransmettreLe(d) === transmettre)
    if (!estBlanc(recherche)) {
      const t = recherche.trim()
      trouves = trouves.filter((d) => contientSansCasse(d.code, t) || contientSansCasse(d.destinataire, t))
    }
    if (coffre !== '') trouves = trouves.filter((d) => d.coffre === coffre)
    if (typeDecision !== '') trouves = trouves.filter((d) => d.typeDecision === typeDecision)
    return trouves
  }

  const trouves = filtrer(aTransmettre)
  const s = selection
  const enErreur = trouves.filter((d) => d.erreur !== '').length

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nom du coffre</span>
          <select className="rig-saisie-champ__saisie" value={coffre} onChange={(e) => setCoffre(e.target.value)}>
            <option value="">Tous</option>
            {Coffres.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type de decision</span>
          <select className="rig-saisie-champ__saisie" value={typeDecision} onChange={(e) => setTypeDecision(e.target.value)}>
            <option value="">Tous</option>
            <option value="ACAD">Avec code de depot</option>
            <option value="CD">Sans code de depot</option>
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Periode</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date" value={dateDebut} onChange={(e) => setDateDebut(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date" value={dateFin} onChange={(e) => setDateFin(e.target.value)} />
            </label>
          </div>
        </div>

        {/* v2 : ult_Archive, ult_Audience, ult_Erreur, ult_GedJud,
            ult_Message, ult_Reda -- les natures de contenu recherchees. */}
        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Nature du contenu</div>
          {Natures.map((nature) => (
            <button key={nature} className={`rig-bascule${naturesActives.has(nature) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerNature(nature)}>
              <span className="rig-bascule__temoin"></span>
              <span>{nature}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Historique</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nombre de lignes d'historique</span>
            <input className="rig-saisie-champ__saisie" type="number" min="1" max="200"
                   value={nbLignesTexte} onChange={(e) => saisirNbLignes(e.target.value)} />
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Parametrage du coffre</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Coffre par defaut</span>
            <input className="rig-saisie-champ__saisie" value={coffreDefaut} onChange={(e) => setCoffreDefaut(e.target.value)} />
          </label>
        </div>
      </aside>

      <ListeModerne etiquette="Depots">
        <EnteteListe titre="Depots"
                     synthese={<>{trouves.length}{trouves.length > 1 ? ' depots ' : ' depot '}
                       {aTransmettre ? 'a transmettre' : 'deja transmis'}{' · '}<b>{enErreur}</b>{' en erreur'}</>}
                     filtre={{ valeur: recherche, changer: setRecherche, indication: 'Aller a un depot…',
                               valider: () => { if (trouves.length === 1) setSelection(trouves[0]!) } }} />

        {/* Les deux onglets de l'ecran, en puces comptees (decision du lot 7 : onglets de listes differentes). */}
        <VuesListe vues={[
          { cle: 'a-transmettre', libelle: 'A transmettre', compte: filtrer(true).length },
          { cle: 'deja-transmis', libelle: 'Deja transmis', compte: filtrer(false).length },
        ]} active={aTransmettre ? 'a-transmettre' : 'deja-transmis'} choisir={(cle) => setATransmettre(cle === 'a-transmettre')} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col /><col style={{ width: '15%' }} /><col style={{ width: '17%' }} />
              <col style={{ width: '15%' }} /><col style={{ width: '20%' }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Destinataire</th>
                <th>Etat</th>
                <th>Date envoi infogreffe</th>
                <th>Code de depot</th>
                <th>Erreur</th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((dep) => {
                const estOuverte = ouverte === dep.code
                const classes = [dep.erreur === '' ? '' : 'rig-table__ligne--alerte', estOuverte ? 'rig-lm-ouverte' : ''].filter(Boolean).join(' ')
                return (
                  <Fragment key={dep.code}>
                  <tr className={classes} onClick={() => setSelection(dep)}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : dep.code)} />
                    <CelluleIdentite nom={dep.destinataire} objet={`${dep.code} · ${dep.abrege}`}
                                     titre={`${dep.destinataire} — ${dep.code} · ${dep.abrege}`} />
                    <td><PointEtat teinte={teinteStatut(dep.statut)}>{dep.etat}</PointEtat></td>
                    <td className="rig-table__mono">{jour(dep.envoiInfogreffe)}</td>
                    <td className="rig-table__mono">{dep.codeDepot}</td>
                    <td title={dep.erreur}>{dep.erreur}</td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes}
                                 actions={[{ libelle: 'Voir l\'historique', fort: true, agir: () => setSelection(dep) }]} champs={[
                      { libelle: 'Date AR Infogreffe', valeur: jour(dep.arInfogreffe) },
                      { libelle: 'Date AR destinataire', valeur: jour(dep.arDestinataire) },
                      { libelle: 'Date lecture destinataire', valeur: jour(dep.lectureDestinataire) },
                      { libelle: 'Date traitement RIG', valeur: jour(dep.traitementRig) },
                      { libelle: 'Date avis de depot coffre', valeur: jour(dep.avisDepotCoffre) },
                      { libelle: 'Coffre', valeur: dep.coffre },
                      { libelle: 'Type de decision', valeur: dep.typeDecision },
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {trouves.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">Aucun depot ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {s && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-kicker">Historique de {s.code} ({nbLignesHisto} ligne(s) au plus)</div>
            <ul className="rig-historique__liste">
              {s.historique.slice(0, Math.max(nbLignesHisto, 1)).map((h, i) => <li key={i}>{h}</li>)}
            </ul>
          </div>
        )}

        <PiedListe aide="Clic sur un depot pour voir son historique.">
          <span><b>{trouves.length}</b>{trouves.length > 1 ? ' depots affiches' : ' depot affiche'}</span>
          <span>En erreur <b>{enErreur}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
