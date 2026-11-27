// Ecran de la procedure PROC_ARCHIRAD.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_ARCHIRAD) :
//   - Operations/OPE_ARCHRSEL : ult_DateMin, ult_DateMax (bornes de date de
//     radiation), ult_TypeRcs, ult_CBListe et ult_TextListe
//   - Operations/OPE_ARCHRTOT : ult_NbDossiers, ult_TypeBrd, ult_cboImprimante
//
// NOTE : v2 n a PAS de grille de resultat sur cet ecran, seulement ces
// criteres et une impression. Le tableau ci-dessous est un ajout de la
// maquette, pour que l ecran montre quelque chose en demonstration ; il ne
// correspond a aucune grille v2.
//
// Porte depuis Components/Ecrans/EcranArchivage.razor (lot 5) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses formalites de
// demonstration sont ici, sans pont. La coquille .razor monte cet ecran et
// recoit ouvrirDossier.
//
// LISTE MODERNE (24/11/2026, plan des listes modernes, lot 5) : les formalites retenues prennent les briques de
// composants/ListeModerne.tsx -- tete (synthese, « Aller a une formalite… » Ctrl K qui filtre la liste affichee,
// Entree ouvre le dossier reste seul), vues = le critere Type RCS existant, avatar + denomination + numero de
// dossier, type en pastille, etat en point colore, pied. Les criteres, le clic sur une ligne et le bordereau ne
// changent pas. fix-ok: ajout du socle des listes en plusieurs pas (lot 5), pas un correctif.
//
// BRANCHE SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 20, 27/11/2026) : la liste vient du pont
// (Pont/EcranArchivageBase.cs). En base, les dossiers radies dans la periode, du type RCS choisi (les types de RIG v2 :
// A, B, C, D, AC, I -- obligatoire, comme les dates), ou des numeros de gestion de la liste, sans « Historique des
// FORMALITES » en GED : ceux que compte la recherche de RIG v2 (Serveur/Socle/EcransMain/LectureArchivage.cs) ; RIG v2
// n'admet le bordereau que pour 1 a 149 dossiers. Sans serveur, les formalites d'exemple ci-dessous, marquees ; en panne,
// le motif et aucune ligne. « Preparer le bordereau » N'ECRIT RIEN et le dit (en base : la raison de RIG v2 ; sans serveur :
// « Demonstration : rien n'est enregistre ») -- il marquait avant les lignes « Sur bordereau » sans rien enregistrer.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { contientSansCasse, dateCourte, entierLisible, estBlanc } from '../criteres'
import { CelluleIdentite, EnteteListe, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe, type Teinte } from '../composants/ListeModerne'

interface LigneArchi {
  dossier: string
  denomination: string
  type: string
  /** aaaa-mm-jj */
  radiation: string
  /** aaaa-mm-jj ; absent en base (colonne de la maquette, sans equivalent RIG v2). */
  dernierDepot?: string
  /** Absent en base (colonne de la maquette, sans equivalent RIG v2). */
  pieces?: number
}

/** Ce que rend le pont (Pont/EcranArchivageBase.cs, VueArchivage). */
export interface VueArchivage {
  mode: ModeLecture
  message: string | null
  /** Les dossiers de la base (mode « serveur ») ; vide sinon. */
  lignes: LigneArchi[]
  total: number
  /** Pourquoi rien n'est cherche (un critere obligatoire de RIG v2 manque). */
  motif: string | null
  /** Les types RCS de RIG v2. */
  types: string[]
  /** Le bordereau n'est permis que pour moins de dossiers (150, OPE_ARCHRSEL). */
  maxBordereau: number
}

/** Ce que dit « Preparer le bordereau » sans serveur. */
export const Demonstration = "Démonstration : rien n'est enregistré."

/** Ce que dit « Preparer le bordereau » branche a la base : la raison de RIG v2 (PROC_ARCHIRAD.cs:44-61, :150-200). */
export const SansBordereau = "Rien n'est enregistré : dans RIG v2, le bordereau crée une demande par dossier, ses éditions et " +
  "l'annexe « Historique des FORMALITES » en GED (PROC_ARCHIRAD). La V3 n'édite pas encore ces bordereaux."

const Exemples: LigneArchi[] = [
  { dossier: '1998B00214', denomination: 'SARL MENUISERIE COLLIN', type: 'PM', radiation: '2014-03-12', dernierDepot: '2013-11-04', pieces: 18 },
  { dossier: '2001B00877', denomination: 'SAS ELEC DU NORD', type: 'PM', radiation: '2016-07-01', dernierDepot: '2016-02-20', pieces: 24 },
  { dossier: '1995A00120', denomination: 'DUPONT Jean', type: 'PP', radiation: '2012-10-09', dernierDepot: '2012-05-30', pieces: 7 },
  { dossier: '2004B01344', denomination: 'EURL LE PETIT CAFE', type: 'PM', radiation: '2018-01-25', dernierDepot: '2017-09-12', pieces: 11 },
  { dossier: '1999A00456', denomination: 'MARTIN Claire', type: 'PP', radiation: '2015-06-03', dernierDepot: '2015-01-15', pieces: 9 },
]

/** Les vues : le critere Type RCS de l'ecran, preregle. */
const VuesType: { cle: string; libelle: string }[] = [
  { cle: '', libelle: 'Toutes' },
  { cle: 'PM', libelle: 'Personnes morales' },
  { cle: 'PP', libelle: 'Personnes physiques' },
]
const TeinteType: Record<string, Teinte> = { PM: 'bleu', PP: 'orange' }
const NomType: Record<string, string> = { PM: 'Personne morale', PP: 'Personne physique' }

export interface ProprietesEcranArchivage {
  /** Remonte le NUMERO de dossier de la formalite cliquee : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
}

export function EcranArchivage({ ouvrirDossier }: ProprietesEcranArchivage) {
  const [dateMin, setDateMin] = useState('2012-01-01')
  const [dateMax, setDateMax] = useState('2016-12-31')
  // v2 : ult_TextListe, le libelle associe a la case ult_CBListe.
  const [libelleListe, setLibelleListe] = useState('')

  const [typeRcs, setTypeRcs] = useState('')
  const [typeBrd, setTypeBrd] = useState('ARCH')
  const [liste, setListe] = useState('COMPLETE')
  // Le texte saisi et la derniere valeur LISIBLE (le @bind d'un int).
  const [nbDossiersTexte, setNbDossiersTexte] = useState('50')
  const [nbDossiers, setNbDossiers] = useState(50)
  const [imprimante, setImprimante] = useState('Greffe - Etage 1')
  // Ce que « Preparer le bordereau » a repondu : il n'ecrit rien, il dit pourquoi.
  const [compteRendu, setCompteRendu] = useState<string | null>(null)

  // La vue du pont, relue a chaque critere de RIG v2 (dates, type, liste) ; null tant qu'il n'a pas repondu.
  const [vue, setVue] = useState<VueArchivage | null>(null)
  useEffect(() => {
    let perime = false
    void appeler<VueArchivage>('EcranArchivage.Dossiers', dateMin, dateMax, typeRcs, libelleListe)
      .then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [dateMin, dateMax, typeRcs, libelleListe])
  const enBase = vue?.mode === 'serveur'
  const Lignes: LigneArchi[] = vue === null ? [] : enBase ? vue.lignes : vue.mode === 'demo' ? Exemples : []
  const vuesType = vue !== null && enBase ? vue.types.map((t) => ({ cle: t, libelle: t })) : VuesType

  function preparer() {
    setCompteRendu(enBase ? SansBordereau : Demonstration)
  }

  function saisirNbDossiers(texte: string) {
    setNbDossiersTexte(texte)
    const n = entierLisible(texte)
    if (n !== null) setNbDossiers(n)
  }

  /** Les formalites retenues par les criteres, pour un type RCS donne (une vue compte les siennes). */
  function retenir(type: string): LigneArchi[] {
    // En base, le serveur a deja retenu les dossiers du type choisi ; un autre type n'est pas lu (pas de compte).
    if (enBase) return type === typeRcs ? Lignes : []
    let q = Lignes
    if (dateMin !== '') q = q.filter((l) => l.radiation >= dateMin)
    if (dateMax !== '') q = q.filter((l) => l.radiation <= dateMax)
    if (type !== '') q = q.filter((l) => l.type === type)
    return q.slice(0, Math.min(Math.max(nbDossiers, 1), 999))
  }
  const retenues = retenir(typeRcs)

  // « Aller a une formalite… » (Ctrl K) : filtre la liste AFFICHEE, sans toucher aux criteres.
  const [filtreRapide, setFiltreRapide] = useState('')
  const affichees = estBlanc(filtreRapide) ? retenues
    : retenues.filter((l) => contientSansCasse(`${l.dossier} ${l.denomination}`, filtreRapide.trim()))
  const pieces = affichees.reduce((s, l) => s + (l.pieces ?? 0), 0)

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Dates de radiation</div>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Date mini.</span>
            <input className="rig-saisie-champ__saisie" type="date" value={dateMin} onChange={(e) => setDateMin(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Date maxi.</span>
            <input className="rig-saisie-champ__saisie" type="date" value={dateMax} onChange={(e) => setDateMax(e.target.value)} />
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Selection</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Type RCS</span>
            <select className="rig-saisie-champ__saisie" value={typeRcs} onChange={(e) => setTypeRcs(e.target.value)}>
              {vue !== null && enBase ? (
                <>
                  <option value="">Choisir…</option>
                  {vue.types.map((t) => <option key={t} value={t}>{t}</option>)}
                </>
              ) : (
                <>
                  <option value="">Tous</option>
                  <option value="PM">Personne morale</option>
                  <option value="PP">Personne physique</option>
                </>
              )}
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Type de bordereau</span>
            <select className="rig-saisie-champ__saisie" value={typeBrd} onChange={(e) => setTypeBrd(e.target.value)}>
              <option value="ARCH">Archivage</option>
              <option value="RAD">Radiation</option>
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Liste</span>
            <select className="rig-saisie-champ__saisie" value={liste} onChange={(e) => setListe(e.target.value)}>
              <option value="COMPLETE">Liste complete</option>
              <option value="CONTROLE">Liste de controle</option>
            </select>
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Libelle de la liste</span>
            <input className="rig-saisie-champ__saisie" value={libelleListe} onChange={(e) => setLibelleListe(e.target.value)} />
          </label>

          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nombre de dossiers</span>
            {vue !== null && enBase ? (
              // En base, le compte de RIG v2 (ult_NbDossiers, « Nombre de dossiers (max 150) »), en lecture.
              <input className="rig-saisie-champ__saisie" readOnly value={String(vue.total)}
                     title="Le nombre de dossiers de la recherche de RIG v2 (max 150 pour un bordereau)" />
            ) : (
              <input className="rig-saisie-champ__saisie" type="number" min="1" max="999" value={nbDossiersTexte}
                     onChange={(e) => saisirNbDossiers(e.target.value)} />
            )}
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Imprimante</span>
            <select className="rig-saisie-champ__saisie" value={imprimante} onChange={(e) => setImprimante(e.target.value)}>
              <option>Greffe - Etage 1</option>
              <option>Greffe - Accueil</option>
              <option>PDF (fichier)</option>
            </select>
          </label>
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" onClick={preparer}>Preparer le bordereau</button>
        </div>
        {compteRendu && <p className="rig-eg-note" role="status">{compteRendu}</p>}
      </aside>

      <ListeModerne etiquette="Formalites retenues">
        <ProvenanceEcran mode={vue?.mode} message={vue?.message} quoi="Formalites" />
        {vue !== null && enBase && vue.motif && <p className="rig-eg-note" role="note">{vue.motif}</p>}
        {vue !== null && enBase && vue.total >= vue.maxBordereau && (
          <p className="rig-eg-note" role="note">{`${vue.total} dossiers : RIG v2 n'admet le bordereau que pour moins de ${vue.maxBordereau} dossiers.`}</p>
        )}
        <EnteteListe titre="Formalites retenues"
                     synthese={<>{affichees.length}{affichees.length > 1 ? ' retenues' : ' retenue'}{' sur '}{vue !== null && enBase ? vue.total : Lignes.length}
                       {enBase ? '' : <>{' · '}<b>{pieces}</b>{pieces > 1 ? ' pieces' : ' piece'}</>}{' · bordereau non prepare'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une formalite…',
                               valider: () => { if (affichees.length === 1) ouvrirDossier(affichees[0]!.dossier) } }} />

        <VuesListe vues={vuesType.map((v) => ({ cle: v.cle, libelle: v.libelle,
                                                ...(enBase && v.cle !== typeRcs ? {} : { compte: retenir(v.cle).length }) }))}
                   active={typeRcs} choisir={setTypeRcs} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col /><col style={{ width: '14%' }} /><col style={{ width: '15%' }} /><col style={{ width: '13%' }} />
              <col style={{ width: '8%' }} /><col style={{ width: '16%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>Dossier</th>
                <th>Type RCS</th>
                <th>Date de radiation</th>
                <th>Dernier depot</th>
                <th className="rig-table__centre">Pieces</th>
                <th>Etat</th>
              </tr>
            </thead>
            <tbody>
              {affichees.map((ligne) => (
                <tr key={ligne.dossier} onClick={() => ouvrirDossier(ligne.dossier)}>
                  <CelluleIdentite nom={ligne.denomination} objet={ligne.dossier} titre={`${ligne.denomination} · ${ligne.dossier}`} />
                  <td><Pastille teinte={TeinteType[ligne.type] ?? 'gris'} titre={NomType[ligne.type]}>{ligne.type}</Pastille></td>
                  <td className="rig-table__mono">{dateCourte(ligne.radiation)}</td>
                  <td className="rig-table__mono">{ligne.dernierDepot ? dateCourte(ligne.dernierDepot) : ''}</td>
                  <td className="rig-table__centre">{ligne.pieces ?? ''}</td>
                  <td><PointEtat teinte="orange">A archiver</PointEtat></td>
                </tr>
              ))}
              {retenues.length === 0 && (
                <tr><td colSpan={6} className="rig-table__vide">Aucune formalite dans cette plage de radiation.</td></tr>
              )}
              {retenues.length > 0 && affichees.length === 0 && (
                <tr><td colSpan={6} className="rig-table__vide">
                  {`Aucune des ${retenues.length} formalites ne contient « ${filtreRapide.trim()} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' formalites affichees' : ' formalite affichee'}</span>
          {!enBase && <span>Pieces <b>{pieces}</b></span>}
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
