// Ouverture d'un dossier RCS pour une societe.
//
// Ecran qui manquait a la maquette : faire NAITRE l'affaire elle-meme, point
// d'entree d'une immatriculation arrivee du guichet unique. Sans origine RIG v2 :
// aucun ecran de l'ancienne application ne portait ce geste, il n'y a donc pas
// de procedure a citer.
//
// Les champs sont ceux des sections de Metier/InformationsDossier.cs (lecture
// du 15/09/2026 de RIG_DEV@SQL-DEV) : DOSSIER_RCS, INSCRIT_PM, CAPITAL,
// ACTIVITE, ETABLISSEMENT, DIRIGEANT.
//
// Porte depuis Components/Ecrans/EcranOuvertureDossier.razor (lot 3) : meme
// balisage, memes classes CSS. Les controles et l'ouverture elle-meme sont faits
// par Pont/EcranOuvertureDossier.cs, qui ecrit dans le registre partage
// (Metier/AffairesOuvertes.cs, conserve dans accueil.json). La coquille .razor
// passe sirenDemande, pose quand on arrive d'une recherche restee vide.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 3) : la liste des dossiers ouverts
// prend les briques de composants/ListeModerne.tsx. Tete (synthese : nombre, capital ; « Aller a un dossier… »
// Ctrl K, le registre grandit a chaque ouverture), une ligne par dossier : la denomination avec son avatar, le
// numero de gestion et le SIREN en gris, la forme et l'origine en pastilles, la date, le capital avec sa
// mini-barre (par monnaie), et un pied. Pas de vues ni de cartes : la colonne de gauche est une fiche a remplir,
// pas des criteres. La fiche, ses controles et l'ouverture ne changent pas.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { dateSaisie, decimalLisible, useNombreSaisi } from '../saisie'
import { CelluleIdentite, EnteteListe, ListeModerne, MontantBarre, Pastille, PiedListe, montant, normaliser, type Teinte } from '../composants/ListeModerne'

export interface LigneDossierOuvert {
  numero: string
  siren: string
  denomination: string
  forme: string
  immatriculation: string
  /** Le capital deja mis en forme (« 1 000 »), comme le ToString("N0") de Blazor. */
  capital: string
  monnaie: string
  origine: string
}

export interface VueOuverture {
  /** La date d'immatriculation proposee (aaaa-mm-jj). */
  aujourdhui: string
  ouverts: LigneDossierOuvert[]
}

export interface ResultatOuverture {
  message: string
  bloquant: boolean
  ouverts: LigneDossierOuvert[]
}

export interface ProprietesEcranOuvertureDossier {
  /**
   * SIREN pose a l'ouverture de l'ecran : l'agent arrive ici depuis une
   * recherche d'affaires restee vide, il ne doit pas retaper le numero.
   */
  sirenDemande?: string | null
}

/** Les trois portes reelles d'une immatriculation au greffe. */
const Origines = ['Guichet unique (INPI)', 'Demande recue au greffe', 'Depot au guichet']

const Formes = ['SAS', 'SASU', 'SARL', 'EURL', 'SA', 'SNC', 'SCI', 'Societe civile']

const Qualites = ['President', 'Directeur general', 'Gerant', 'Administrateur', 'Associe unique']

/** La teinte d'une forme juridique, par famille (actions, parts, civiles, personnes). */
const TeinteForme: Record<string, Teinte> = {
  SAS: 'bleu', SASU: 'bleu', SA: 'violet', SARL: 'vert', EURL: 'vert', SNC: 'orange', SCI: 'ardoise', 'Societe civile': 'ardoise',
}

/** La teinte de chaque porte d'entree (Origines). */
const TeinteOrigine: Record<string, Teinte> = {
  'Guichet unique (INPI)': 'violet', 'Demande recue au greffe': 'bleu', 'Depot au guichet': 'orange',
}

/** Le capital rendu par le pont en « N0 » (« 1 000 », separateur selon la culture) : ses seuls chiffres. */
export function valeurCapital(capital: string): number {
  const chiffres = capital.replace(/\D/g, '')
  return chiffres === '' ? 0 : Number(chiffres)
}

/** Les colonnes du tableau : Dossier, Forme, Origine, Immatriculation, Capital. */
const NbColonnes = 5

export function EcranOuvertureDossier({ sirenDemande }: ProprietesEcranOuvertureDossier) {
  const [origine, setOrigine] = useState(Origines[0]!)
  const [siren, setSiren] = useState(() => (sirenDemande ?? '').trim())
  // null tant que le pont n'a pas donne la date du jour du jeu de demonstration.
  const [dateImmat, setDateImmat] = useState<string | null>(null)
  const [denomination, setDenomination] = useState('')
  const [formeJuridique, setFormeJuridique] = useState(Formes[0]!)
  const capital = useNombreSaisi(1000, decimalLisible)
  const [monnaie, setMonnaie] = useState('EUR')
  const [codeNaf, setCodeNaf] = useState('')
  const [activite, setActivite] = useState('')
  const [adresseSiege, setAdresseSiege] = useState('')
  const [dirigeant, setDirigeant] = useState('')
  const [qualiteDirigeant, setQualiteDirigeant] = useState(Qualites[0]!)

  const [message, setMessage] = useState('')
  const [bloquant, setBloquant] = useState(false)
  // null tant que le pont n'a pas repondu : ni ligne ni « aucun dossier ouvert ».
  const [ouverts, setOuverts] = useState<LigneDossierOuvert[] | null>(null)
  // « Aller a un dossier… » (Ctrl K) : filtre la liste AFFICHEE.
  const [filtreRapide, setFiltreRapide] = useState('')

  useEffect(() => {
    let perime = false
    void appeler<VueOuverture>('EcranOuvertureDossier.Vue').then((v) => {
      if (perime) return
      setDateImmat((avant) => avant ?? v.aujourdhui)
      setOuverts(v.ouverts)
    })
    return () => { perime = true }
  }, [])

  function reinitialiserChamps() {
    setSiren('')
    setDenomination('')
    setCodeNaf('')
    setActivite('')
    setAdresseSiege('')
    setDirigeant('')
    capital.poser(1000)
  }

  async function ouvrir() {
    const r = await appeler<ResultatOuverture>('EcranOuvertureDossier.Ouvrir', {
      origine, siren, dateImmat: dateSaisie(dateImmat ?? ''), denomination,
      formeJuridique, capital: capital.valeur, monnaie,
    })
    setMessage(r.message)
    setBloquant(r.bloquant)
    setOuverts(r.ouverts)
    if (!r.bloquant) reinitialiserChamps()
  }

  function viderLaFiche() {
    reinitialiserChamps()
    setMessage('')
    setBloquant(false)
  }

  const tous = ouverts ?? []
  const cherche = filtreRapide.trim()
  const chercheNormal = normaliser(cherche)
  const affiches = cherche === '' ? tous : tous.filter((d) => normaliser(
    [d.numero, d.siren, d.denomination, d.forme, d.origine].join(' ')).includes(chercheNormal))
  // Le capital se compare et s'additionne dans sa monnaie : une barre EUR ne se mesure pas a un capital CHF.
  const monnaies = [...new Set(affiches.map((d) => d.monnaie))]
  const capitalMax = (monnaie: string) => affiches.filter((d) => d.monnaie === monnaie).reduce((m, d) => Math.max(m, valeurCapital(d.capital)), 0)
  const capitalTotal = (monnaie: string) => affiches.filter((d) => d.monnaie === monnaie).reduce((s, d) => s + valeurCapital(d.capital), 0)
  // Un capital s'ecrit sans decimales, dans sa monnaie : « 50 000 CHF » (montant() du socle).
  const capitalEcrit = (m: string) => montant(capitalTotal(m), { devise: m, decimales: 0 })
  const totaux = monnaies.map(capitalEcrit).join(' · ')

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Ouverture d'un dossier</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">D'ou vient la demande</span>
          <select className="rig-saisie-champ__saisie" value={origine} onChange={(e) => setOrigine(e.target.value)}>
            {Origines.map((o) => (
              <option key={o} value={o}>{o}</option>
            ))}
          </select>
        </label>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">N° SIREN</span>
            <input className="rig-saisie-champ__saisie" value={siren} onChange={(e) => setSiren(e.target.value)} placeholder="9 chiffres" />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Date d'immatriculation</span>
            <input className="rig-saisie-champ__saisie" type="date"
                   value={dateImmat ?? ''} onChange={(e) => setDateImmat(e.target.value)} />
          </label>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Denomination</span>
          <input className="rig-saisie-champ__saisie" value={denomination} onChange={(e) => setDenomination(e.target.value)} placeholder="raison sociale" />
        </label>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Forme juridique utilisee</span>
            <select className="rig-saisie-champ__saisie" value={formeJuridique} onChange={(e) => setFormeJuridique(e.target.value)}>
              {Formes.map((f) => (
                <option key={f} value={f}>{f}</option>
              ))}
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Montant du capital</span>
            <input className="rig-saisie-champ__saisie" type="number"
                   value={capital.texte} onChange={(e) => capital.saisir(e.target.value)} />
          </label>
        </div>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Code NAF</span>
            <input className="rig-saisie-champ__saisie" value={codeNaf} onChange={(e) => setCodeNaf(e.target.value)} placeholder="ex. 6201Z" />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Monnaie</span>
            <select className="rig-saisie-champ__saisie" value={monnaie} onChange={(e) => setMonnaie(e.target.value)}>
              <option>EUR</option><option>CHF</option><option>USD</option>
            </select>
          </label>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Activites (indiquees sur le Kbis)</span>
          <input className="rig-saisie-champ__saisie" value={activite} onChange={(e) => setActivite(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Adresse du siege</span>
          <input className="rig-saisie-champ__saisie" value={adresseSiege} onChange={(e) => setAdresseSiege(e.target.value)} />
        </label>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Dirigeant</span>
            <input className="rig-saisie-champ__saisie" value={dirigeant} onChange={(e) => setDirigeant(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Qualite</span>
            <select className="rig-saisie-champ__saisie" value={qualiteDirigeant} onChange={(e) => setQualiteDirigeant(e.target.value)}>
              {Qualites.map((q) => (
                <option key={q} value={q}>{q}</option>
              ))}
            </select>
          </label>
        </div>

        {/* Le bloc d'actions n'est pas une colonne souple : l'espace entre
            les deux boutons est celui que Razor laissait entre eux. */}
        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" onClick={() => void ouvrir()}>Ouvrir le dossier</button>
          {' '}
          <button className="rig-bouton" onClick={viderLaFiche}>Vider</button>
        </div>

        {/* Une PHRASE, pas une pastille : « rig-etat » interdit le retour a
            la ligne (voulu pour les etats de tableau), ce qui faisait
            deborder ce message hors de la colonne. */}
        {message !== '' && (
          <div className={`rig-etat rig-etat--${bloquant ? 'attente' : 'ok'}`}
               style={{ display: 'block', whiteSpace: 'normal', lineHeight: 1.5, padding: '6px 10px' }}>
            {message}
          </div>
        )}
      </aside>

      <ListeModerne etiquette="Dossiers ouverts">
        <EnteteListe titre="Dossiers ouverts"
                     synthese={ouverts ? <>{affiches.length}{' dossier(s)'}{affiches.length > 0 ? ` · capital ${totaux}` : ''}</> : undefined}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a un dossier…' }} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col /><col style={{ width: '13%' }} /><col style={{ width: '22%' }} /><col style={{ width: '15%' }} />
              <col style={{ width: '20%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>Dossier</th>
                <th>Forme</th>
                <th>Origine</th>
                <th>Immatriculation</th>
                <th className="rig-lm-droite">Capital</th>
              </tr>
            </thead>
            <tbody>
              {affiches.map((d) => (
                <tr key={d.numero}>
                  <CelluleIdentite nom={d.denomination} titre={`${d.denomination} · ${d.numero} · SIREN ${d.siren}`}
                                   objet={<><span>{d.numero}</span>{' · SIREN '}{d.siren}</>} />
                  <td><Pastille teinte={TeinteForme[d.forme] ?? 'gris'}>{d.forme}</Pastille></td>
                  <td title={d.origine}><Pastille teinte={TeinteOrigine[d.origine] ?? 'gris'}>{d.origine}</Pastille></td>
                  <td className="rig-table__mono">{d.immatriculation}</td>
                  <td className="rig-lm-droite" title={`${d.capital} ${d.monnaie}`}>
                    <MontantBarre valeur={valeurCapital(d.capital)} max={capitalMax(d.monnaie)} texte={`${d.capital} ${d.monnaie}`} />
                  </td>
                </tr>
              ))}
              {ouverts && ouverts.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">
                  Aucun dossier ouvert pour l'instant : remplissez la fiche a gauche.
                </td></tr>
              )}
              {tous.length > 0 && affiches.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">
                  {`Aucun des ${tous.length} dossiers ne contient « ${cherche} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        {ouverts && (
          <PiedListe>
            <span><b>{affiches.length}</b>{affiches.length > 1 ? ' dossiers ouverts' : ' dossier ouvert'}</span>
            {monnaies.map((m) => <span key={m}>Capital <b>{capitalEcrit(m)}</b></span>)}
          </PiedListe>
        )}
      </ListeModerne>
    </div>
  )
}
