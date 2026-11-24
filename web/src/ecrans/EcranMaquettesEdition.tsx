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

import { useState } from 'react'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'
import { EnteteListe, ListeModerne, Pastille, PiedListe, VuesListe, type Teinte } from '../composants/ListeModerne'

interface Maquette {
  code: string
  libelle: string
  process: string
  typeDoc: string
  typeMaquette: string
  /** aaaa-mm-jj */
  maj: string
  tables: string[]
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
function Bascule({ actif, basculer, libelle }: { actif: boolean; basculer: () => void; libelle: string }) {
  return (
    <button className={`rig-bascule${actif ? ' rig-bascule--actif' : ''}`} onClick={basculer}>
      <span className="rig-bascule__temoin"></span><span>{libelle}</span>
    </button>
  )
}

/** Une des quatre listes de lot : meme catalogue, canal different. */
function ListeLots({ libelle, valeur, choisir }: { libelle: string; valeur: string; choisir: (v: string) => void }) {
  return (
    <label className="rig-saisie-champ">
      <span className="rig-saisie-champ__libelle">{libelle}</span>
      <select className="rig-saisie-champ__saisie" value={valeur} onChange={(e) => choisir(e.target.value)}>
        {LotsDisponibles.map((l) => <option key={l} value={l}>{l}</option>)}
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

  /** Les maquettes de l'ecran d'origine choisi, pour un type de document (une vue compte les siennes). */
  function trouver(type: string): Maquette[] {
    let q = Maquettes
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
            {distinctsTries(Maquettes.map((m) => m.process)).map((p) => <option key={p} value={p}>{p}</option>)}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type de document</span>
          <select className="rig-saisie-champ__saisie" value={typeDoc} onChange={(e) => setTypeDoc(e.target.value)}>
            <option value="">Tous</option>
            {distinctsTries(Maquettes.map((m) => m.typeDoc)).map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Parametres d'edition</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Imprimante</span>
            <select className="rig-saisie-champ__saisie" value={imprimante} onChange={(e) => setImprimante(e.target.value)}>
              <option>Greffe - Accueil</option>
              <option>Greffe - Etage 1</option>
              <option>PDF (fichier)</option>
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Type de papier</span>
            <select className="rig-saisie-champ__saisie" value={typePapier} onChange={(e) => setTypePapier(e.target.value)}>
              <option>A4 blanc</option>
              <option>A4 en-tete</option>
              <option>Enveloppe plieuse</option>
            </select>
          </label>
          {/* v2 (OPE_PARAM_EDT_MAJ) a QUATRE listes de lot distinctes, pas une
              seule liste a quatre options : ComboBoxLot, ComboBoxLotLRAR,
              ComboBoxLotILRAR et ComboBoxLotSubstitution. Chacune choisit le
              lot a utiliser pour un canal d'envoi different. */}
          <ListeLots libelle="Lot standard" valeur={lot} choisir={setLot} />
          <ListeLots libelle="Lot LRAR" valeur={lotLrar} choisir={setLotLrar} />
          <ListeLots libelle="Lot iLRAR" valeur={lotIlrar} choisir={setLotIlrar} />
          <ListeLots libelle="Lot de substitution" valeur={lotSubstitution} choisir={setLotSubstitution} />

          {/* v2 : TxtNbExemplaires. */}
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nombre d'exemplaires</span>
            <input className="rig-saisie-champ__saisie" type="number" min="1" value={nbExemplaires}
                   onChange={(e) => setNbExemplaires(e.target.value)} />
          </label>

          <Bascule actif={rectoVerso} basculer={() => setRectoVerso(!rectoVerso)} libelle="Recto-verso" />
          <Bascule actif={agrafage} basculer={() => setAgrafage(!agrafage)} libelle="Agrafage" />
          <Bascule actif={apercuAvantImpression} basculer={() => setApercuAvantImpression(!apercuAvantImpression)}
                   libelle="Afficher avant impression" />
          <Bascule actif={docNonModifiable} basculer={() => setDocNonModifiable(!docNonModifiable)}
                   libelle="Document non modifiable" />
          <Bascule actif={entetePlieuse} basculer={() => setEntetePlieuse(!entetePlieuse)}
                   libelle="Entete au format plieuse" />

          {/* v2 : CheckBoxImpression et ult_chkGarderEditionLot1. */}
          <Bascule actif={impression} basculer={() => setImpression(!impression)} libelle="Impression" />
          <Bascule actif={garderEditionLot1} basculer={() => setGarderEditionLot1(!garderEditionLot1)}
                   libelle="Garder l'edition en lot 1" />

          {/* v2 : TxtBoxCodeParamEdt, TxtBoxLibParamEdt, ultComboNatureEdition,
              ult_txtRequeteSql, TextMultiLineAutresParam. */}
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Code parametre d'edition</span>
              <input className="rig-saisie-champ__saisie" value={codeParamEdt} onChange={(e) => setCodeParamEdt(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Nature d'edition</span>
              <select className="rig-saisie-champ__saisie" value={natureEdition} onChange={(e) => setNatureEdition(e.target.value)}>
                <option>Courrier</option>
                <option>Acte</option>
                <option>Bordereau</option>
                <option>Etat</option>
              </select>
            </label>
          </div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Libelle du parametre</span>
            <input className="rig-saisie-champ__saisie" value={libParamEdt} onChange={(e) => setLibParamEdt(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Requete SQL</span>
            <textarea className="rig-saisie-champ__saisie" rows={2} value={requeteSql} onChange={(e) => setRequeteSql(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Autres parametres</span>
            <textarea className="rig-saisie-champ__saisie" rows={2} value={autresParam} onChange={(e) => setAutresParam(e.target.value)} />
          </label>
        </div>
      </aside>

      <ListeModerne etiquette="Maquettes">
        <EnteteListe titre="Maquettes"
                     synthese={<>{affichees.length}{affichees.length > 1 ? ' maquettes' : ' maquette'}
                       {parFormat.map(([format, n]) => <span key={format}>{' · '}<b>{n}</b>{' '}{format}</span>)}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une maquette…',
                               valider: () => { if (affichees.length === 1) setSelection(affichees[0]!) } }} />

        <VuesListe vues={[{ cle: '', libelle: 'Toutes', compte: trouver('').length },
                          ...distinctsTries(Maquettes.map((m) => m.typeDoc)).map((t) => ({ cle: t, libelle: t, compte: trouver(t).length }))]}
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
                <tr key={maq.code} onClick={() => setSelection(maq)}>
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
          </div>
        )}

        <PiedListe>
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' maquettes affichees' : ' maquette affichee'}</span>
          <span>Tables liees <b>{tablesLiees}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
