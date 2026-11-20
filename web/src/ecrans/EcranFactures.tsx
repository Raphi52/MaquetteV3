// Ecran de la procedure PROC_FACTURE.
//
// Sources RIG v2 (D:\RigApplication\Source\RIG\DLL\Processus\PROC_FACTURE) :
//   - FormFacture/OPE_FACTURE_CLIENT   : ult_client_abrege, ult_client_nom,
//                                        ult_client_adresse, btn_Changer_client,
//                                        ult_lblSolde
//   - FormFacture/OPE_FACTURE_MODE     : ult_Facturer_Tarifer (valeur "F" ou "T")
//   - FormFacture/OPE_FACTURE_INFOS    : ult_combo_payeur, ult_References,
//                                        ult_Attention
//   - FormFacture/OPE_FACTURE_ARTICLES : grille ult_DataGridViewArticles,
//                                        colonnes colCodeArtut, colTypeArticle,
//                                        colQte, colPUHT, colTauxTVA, colBODACC,
//                                        colINPI, colAutre, colTTC
//   - FormEncaissement/OPE_FACTU_ENCAISS : cboModePaiement, cboPayeurs, txtBanque,
//                                        txtNumTitre, ultMontant, ultDateEncaissement,
//                                        txtCommentaire, TxtVentilationId
//
// Porte depuis Components/Ecrans/EcranFactures.razor (lot 1) : meme balisage,
// memes classes CSS. Les fiches, le tarif et les tiers payeurs etaient declares
// dans l'ecran lui-meme : ils l'ont suivi ici. La date du jour vient de Metier/,
// par Pont/EcranFactures.cs.
//
// DEUX FACTURES DE DEPART, UN SEUL TARIF (plan « Vue du dossier », lot 8, 20/11/2026) :
//   - depuis l'accueil, la facture de DEMONSTRATION, inchangee (458,63 EUR TTC) ;
//   - depuis la page d'une affaire, une facture DE CETTE AFFAIRE : les articles du
//     tarif qui facturent sa nature, sa demande et son client (Pont/EcranFactures.cs,
//     DemandeDe). Ce que l'affaire ne dit pas reste vide, rien n'est emprunte a la demo.
// fix-ok: constate sur 004182 le 20/11 -- l'etat de depart (client, n de demande,
// articles, encaissement) etait celui de la demo quel que soit le dossier recu.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { centimes, euros, nombre } from '../format'
import { PourLeDossier } from '../composants/PourLeDossier'
import type { ContexteDossierVue } from '../plan/types'

/** Un article du tarif : ses montants POUR UNE UNITE. */
interface ArticleTarif {
  code: string
  libelle: string
  prixUht: number
  tauxTva: number
  bodacc: number
  inpi: number
  autre: number
  ttc: number
  /** Les natures d'affaire RCS que l'article facture : le debut de leur objet
   *  (« Immatriculation - AMIENS BILLARDS »), comme Pont/PlanTravail.cs (SommaireDu) le lit. */
  natures: readonly string[]
}

/** Une ligne de facture : un article du tarif, en quelle quantite, avec quel commentaire. */
interface LigneFacture {
  code: string
  quantite: number
  commentaire: string
}

/** Une ligne telle que la grille la montre : les montants de la ligne (unite x quantite),
 *  sauf le prix UHT, qui reste celui d'une unite. */
interface ArticleFacture {
  code: string
  libelle: string
  quantite: number
  prixUht: number
  tauxTva: number
  bodacc: number
  inpi: number
  autre: number
  ttc: number
  commentaire: string
}

// v2 : ult_client_abrege (dont le titre indique le type de client),
// ult_client_nom, ult_client_adresse, ult_lblSolde.
interface FicheClient {
  nom: string
  abrege: string
  typeClient: string
  adresse: string
  /** null : solde inconnu (le client nomme par une affaire n'a pas de compte connu ici). */
  solde: number | null
}

const Fiches: readonly FicheClient[] = [
  { nom: 'SELARL LEROY & ASSOCIES', abrege: 'LEROY',  typeClient: 'Client en compte', adresse: '12 rue de la Paix, 51100 Reims',      solde: 812.40 },
  { nom: 'CABINET BLANC',           abrege: 'BLANC',  typeClient: 'Client WEB',       adresse: '4 avenue Jean Jaures, 51000 Chalons', solde: 326.15 },
  { nom: 'SCP DUPONT-MARTIN',       abrege: 'DUPONT', typeClient: 'Compte PC',        adresse: '9 place d Erlon, 51100 Reims',        solde: 149.00 },
  { nom: 'Client de passage',       abrege: 'DIVERS', typeClient: 'Client divers',    adresse: '',                                    solde: 0 },
]

// Le tarif de l'ecran, celui des formalites RCS. Un « Depot de bilan » n'y a AUCUN
// article : c'est la declaration de cessation des paiements (procedure collective ;
// glossaire du Brain, 50-metier-judiciaire), pas le depot des comptes annuels (DEP03).
// Les « Comptes annuels 2025 » du depot D-2026-1170 de 004151 (Metier/JeuDemo.cs) sont
// les pieces jointes a cette declaration (art. R631-1 du Code de commerce), pas un depot
// des comptes. Aucune affaire de la demo n'est un depot des comptes : DEP03 ne facture
// aucune nature, il ne sert qu'a la facture de demonstration.
const Tarif: readonly ArticleTarif[] = [
  { code: 'IMM01', libelle: 'Immatriculation societe',   prixUht: 37.45, tauxTva: 20, bodacc: 105.90, inpi: 0,     autre: 0,    ttc: 194.85, natures: ['Immatriculation'] },
  { code: 'MOD02', libelle: 'Modification statutaire',   prixUht: 45.60, tauxTva: 20, bodacc: 108.00, inpi: 0,     autre: 0,    ttc: 162.72, natures: ['Modification'] },
  { code: 'DEP03', libelle: 'Depot des comptes annuels', prixUht: 22.30, tauxTva: 20, bodacc: 0,      inpi: 0,     autre: 0,    ttc: 26.76,  natures: [] },
  { code: 'INP04', libelle: 'Transmission INPI',         prixUht: 12.00, tauxTva: 20, bodacc: 0,      inpi: 18.50, autre: 0,    ttc: 32.90,  natures: [] },
  { code: 'DIV05', libelle: 'Frais divers',              prixUht: 8.00,  tauxTva: 20, bodacc: 0,      inpi: 0,     autre: 4.20, ttc: 14.64,  natures: [] },
]

// La facture de demonstration, celle de l'ecran ouvert depuis l'accueil (capture
// de l'ecran Blazor du 18/11/2026 : 458,63 EUR TTC).
const FactureDemo: readonly LigneFacture[] = [
  { code: 'IMM01', quantite: 1, commentaire: '' },
  { code: 'MOD02', quantite: 1, commentaire: 'Changement de gerant' },
  { code: 'DEP03', quantite: 2, commentaire: '' },
  { code: 'INP04', quantite: 1, commentaire: 'Refacturation INPI' },
  { code: 'DIV05', quantite: 1, commentaire: '' },
]

/** La nature d'une affaire, debut de son objet (« Depot de bilan - TRANSNORD SA » -> « Depot de bilan »). */
const natureDe = (dossier: ContexteDossierVue): string =>
  dossier.objet.includes(' - ') ? dossier.objet.slice(0, dossier.objet.indexOf(' - ')) : ''

/** La facture de depart d'une affaire : une fois chaque article du tarif qui facture sa
 *  nature, aucun si le tarif n'en a pas (depot de bilan, liquidation, toute affaire judiciaire). */
function factureDe(dossier: ContexteDossierVue): LigneFacture[] {
  const nature = natureDe(dossier)
  return Tarif.filter((t) => t.natures.includes(nature))
              .map((t) => ({ code: t.code, quantite: 1, commentaire: '' }))
}

/** Une ligne de facture lue dans le tarif, ses montants calcules en centimes (exacts). */
function article(ligne: LigneFacture): ArticleFacture {
  const t = Tarif.find((a) => a.code === ligne.code)
  if (!t) throw new Error(`Article ${ligne.code} absent du tarif`)
  const fois = (montant: number) => centimes(montant) * ligne.quantite / 100
  return {
    code: t.code, libelle: t.libelle, quantite: ligne.quantite, prixUht: t.prixUht, tauxTva: t.tauxTva,
    bodacc: fois(t.bodacc), inpi: fois(t.inpi), autre: fois(t.autre), ttc: fois(t.ttc), commentaire: ligne.commentaire,
  }
}

const Payeurs: readonly string[] = ['', 'Debiteur', 'Tiers payeur principal', 'Avocat mandataire']

const FicheVide: FicheClient = { nom: '', abrege: '', typeClient: '', adresse: '', solde: null }

/** La demande d'une affaire, telle que Pont/EcranFactures.cs (DemandeDe) la rend. */
interface DemandeFacturee {
  numero: string
  /** L'abrege du client de la demande, vide si elle n'en nomme pas. */
  client: string
}

export interface ProprietesEcranFactures {
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) : la facture est pour elle,
   *  deja choisie (bloc « Pour le dossier »). Absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranFactures({ dossier = null }: ProprietesEcranFactures) {
  // Ouvert pour une affaire, la facture part d'elle seule : son client et sa demande
  // arrivent du pont (DemandeDe) ; d'ici la, et si elle n'en a pas, ils restent vides.
  const [client, setClient] = useState(dossier ? FicheVide.nom : (Fiches[0] ?? FicheVide).nom)
  const [numDemande, setNumDemande] = useState(dossier ? '' : 'D2026-04118')
  const [demande, setDemande] = useState<DemandeFacturee | null>(null)
  // Une affaire est montee avec sa cle (Aiguillage.tsx) : sa facture est lue une fois.
  const [lignes] = useState<readonly LigneFacture[]>(() => dossier ? factureDe(dossier) : FactureDemo)

  // v2 : ult_Facturer_Tarifer, "F" par defaut.
  const [tarifer, setTarifer] = useState(false)

  const [payeur, setPayeur] = useState('')
  const [references, setReferences] = useState('')
  const [attention, setAttention] = useState('')
  const ventilationId = dossier ? '' : 'V-2026-8801'
  const [mode, setMode] = useState('Compte')
  const [banque, setBanque] = useState('')
  const [numTitre, setNumTitre] = useState('')
  const [commentaire, setCommentaire] = useState('')
  // Le texte saisi et la derniere valeur LISIBLE : comme le @bind d'un
  // decimal, une saisie illisible ne remet pas le montant a zero. Pour une
  // affaire, rien n'est encaisse au depart.
  const [encaisseTexte, setEncaisseTexte] = useState(dossier ? '' : '200')
  const [encaisse, setEncaisse] = useState(dossier ? 0 : 200)
  const [dateEncaissement, setDateEncaissement] = useState<string | null>(null)
  const [retires, setRetires] = useState<ReadonlySet<string>>(new Set())

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranFactures.DateDuJour')
      .then((d) => { if (!perime) setDateEncaissement((avant) => avant ?? d) })
    return () => { perime = true }
  }, [])

  const numeroAffaire = dossier?.numero
  const nomAffaire = dossier?.nom ?? ''
  useEffect(() => {
    if (numeroAffaire === undefined) return
    let perime = false
    void appeler<DemandeFacturee | null>('EcranFactures.DemandeDe', numeroAffaire).then((d) => {
      if (perime || !d) return
      setDemande(d)
      // Ce que l'agent a deja saisi ou choisi entre-temps n'est pas ecrase.
      setNumDemande((avant) => avant || d.numero)
      if (d.client) setClient((avant) => avant || nomAffaire)
    })
    return () => { perime = true }
  }, [numeroAffaire, nomAffaire])

  // Le client nomme par l'affaire : l'abrege de sa demande, la societe de l'affaire
  // (toutes les demandes du jeu de demonstration abregent la denomination de leur
  // dossier). Ni type, ni adresse, ni solde : l'affaire ne les dit pas.
  const ficheAffaire: FicheClient | null = dossier
    ? (demande?.client ? { ...FicheVide, nom: nomAffaire, abrege: demande.client } : FicheVide)
    : null
  const fiches: readonly FicheClient[] = ficheAffaire ? [ficheAffaire, ...Fiches] : Fiches
  const fiche = fiches.find((f) => f.nom === client) ?? fiches[0] ?? FicheVide

  // v2 : btn_Changer_client fait defiler les clients rattachables a la facture.
  function changerClient() {
    const i = fiches.findIndex((f) => f.nom === client)
    setClient((fiches[(i + 1) % fiches.length] ?? FicheVide).nom)
  }

  function basculer(code: string) {
    setRetires((avant) => {
      const apres = new Set(avant)
      if (!apres.delete(code)) apres.add(code)
      return apres
    })
  }

  function saisirEncaisse(texte: string) {
    setEncaisseTexte(texte)
    const valeur = Number.parseFloat(texte)
    if (Number.isFinite(valeur)) setEncaisse(valeur)
  }

  const articles = lignes.map(article)
  const total = articles.filter((a) => !retires.has(a.code)).reduce((s, a) => s + centimes(a.ttc), 0)
  const reste = Math.max(0, total - centimes(encaisse))

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        {dossier && <PourLeDossier dossier={dossier} />}

        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Informations client</div>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Abrege</span>
            <input className="rig-saisie-champ__saisie" value={fiche.abrege} readOnly />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Type de client</span>
            <input className="rig-saisie-champ__saisie" value={fiche.typeClient} readOnly />
          </label>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nom du client</span>
          <select className="rig-saisie-champ__saisie" value={client} onChange={(e) => setClient(e.target.value)}>
            {fiches.map((c) => (
              <option key={c.nom} value={c.nom}>{c.nom}</option>
            ))}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Adresse</span>
          <input className="rig-saisie-champ__saisie" value={fiche.adresse} readOnly />
        </label>

        {/* Bloc NON flex : bouton et pastille sont en ligne, l'espace qui les
            separe se voit. Razor le gardait ; JSX supprime un blanc qui contient
            un retour a la ligne, d'ou le {' '} explicite (capture du 18/11 :
            pastille decalee de la largeur d'une espace sans lui). */}
        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={changerClient}>Changer de client</button>{' '}
          {fiche.solde !== null && (
            <span className={`rig-etat rig-etat--${fiche.solde > 0 ? 'attente' : 'ok'}`}>
              Solde client {nombre(fiche.solde, 2)} EUR
            </span>
          )}
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">N de demande</span>
          <input className="rig-saisie-champ__saisie" value={numDemande} onChange={(e) => setNumDemande(e.target.value)} />
        </label>

        {/* v2 : ult_Facturer_Tarifer, un choix exclusif dont la valeur vaut
            « F » (facturer) ou « T » (tarifer). */}
        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Mode</div>
          <div className="rig-onglets">
            <button className={`rig-onglet${tarifer ? '' : ' rig-onglet--actif'}`}
                    onClick={() => setTarifer(false)}>Facturer</button>
            <button className={`rig-onglet${tarifer ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setTarifer(true)}>Tarifer</button>
          </div>
          <p className="rig-nonmaq__texte rig-nonmaq__texte--sourd">
            {tarifer
              ? 'Tarifer : le montant est calcule sans emettre de facture.'
              : 'Facturer : une facture numerotee sera emise.'}
          </p>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Infos complementaires</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Tiers payeur</span>
            <select className="rig-saisie-champ__saisie" value={payeur} onChange={(e) => setPayeur(e.target.value)}>
              {Payeurs.map((p) => (
                <option key={p} value={p}>{p}</option>
              ))}
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">References client</span>
            <input className="rig-saisie-champ__saisie" value={references} onChange={(e) => setReferences(e.target.value)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">A l'attention de</span>
            <input className="rig-saisie-champ__saisie" value={attention} onChange={(e) => setAttention(e.target.value)} />
          </label>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Articles de la facture</div>
          <span className="rig-resultats-proc__compte">
            Total TTC {euros(total)} EUR — reste a encaisser {euros(reste)} EUR
          </span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Article</th>
                <th>Libelle</th>
                <th className="rig-table__centre">Quantite</th>
                <th>Prix UHT</th>
                <th>Taux TVA</th>
                <th>BODACC</th>
                <th>INPI</th>
                <th>Autre</th>
                <th>Montant TTC</th>
                <th>Commentaire</th>
              </tr>
            </thead>
            <tbody>
              {articles.length === 0 && dossier && (
                <tr>
                  <td colSpan={10} className="rig-table__vide">
                    Aucun article du tarif pour cette affaire{natureDe(dossier) ? ` (${natureDe(dossier)})` : ''}.
                  </td>
                </tr>
              )}
              {articles.map((art) => (
                <tr key={art.code}
                    className={retires.has(art.code) ? 'rig-table__ligne--inerte' : undefined}
                    onClick={() => basculer(art.code)}>
                  <td className="rig-table__mono">{art.code}</td>
                  <td>{art.libelle}</td>
                  <td className="rig-table__centre">{art.quantite}</td>
                  <td className="rig-table__mono">{nombre(art.prixUht, 2)}</td>
                  <td className="rig-table__mono">{nombre(art.tauxTva, 1)} %</td>
                  <td className="rig-table__mono">{nombre(art.bodacc, 2)}</td>
                  <td className="rig-table__mono">{nombre(art.inpi, 2)}</td>
                  <td className="rig-table__mono">{nombre(art.autre, 2)}</td>
                  <td className="rig-table__mono">{nombre(art.ttc, 2)}</td>
                  <td>{art.commentaire}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
          <div className="rig-kicker">Encaissement</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Montant encaisse</span>
              <input className="rig-saisie-champ__saisie" type="number" step="0.01"
                     value={encaisseTexte} onChange={(e) => saisirEncaisse(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Date d'encaissement</span>
              <input className="rig-saisie-champ__saisie" type="date"
                     value={dateEncaissement ?? ''} onChange={(e) => setDateEncaissement(e.target.value)} />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Mode de paiement</span>
              <select className="rig-saisie-champ__saisie" value={mode} onChange={(e) => setMode(e.target.value)}>
                <option>Compte</option>
                <option>Cheque</option>
                <option>Carte bancaire</option>
                <option>Virement</option>
                <option>Especes</option>
              </select>
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Banque</span>
              <input className="rig-saisie-champ__saisie" value={banque} onChange={(e) => setBanque(e.target.value)} />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">N de titre</span>
              <input className="rig-saisie-champ__saisie" value={numTitre} onChange={(e) => setNumTitre(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Identifiant de ventilation</span>
              <input className="rig-saisie-champ__saisie" value={ventilationId} readOnly />
            </label>
          </div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Commentaire</span>
            <textarea className="rig-saisie-champ__saisie" rows={2} value={commentaire}
                      onChange={(e) => setCommentaire(e.target.value)}></textarea>
          </label>
          <span className={`rig-etat rig-etat--${reste <= 0 ? 'ok' : 'attente'}`}>
            {reste <= 0 ? 'Facture soldee' : `Solde restant ${euros(reste)} EUR`}
          </span>
        </div>
      </section>
    </div>
  )
}
