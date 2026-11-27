// Ecran de la procedure PROC_ENCAISS.
//
// Sources RIG v2 (D:\RigApplication\Source\RIG\DLL\Processus\PROC_ENCAISS) :
//   - Saisie/OPE_FORM_SAISIE : txtPayeur, rcbModePaiement, txtBanque,
//     txtNumTitre, rigMontant, _ultDateVirement, rigchkRecu, txtAdresseRecu,
//     txtCommentaire, lblEncaissRepris
//   - Saisie/OPE_VENTILATIONS + GridViewEncaissementVentilations : colonnes
//     « N° identifiant », « Affaire », « Client », « Abrégé client »,
//     « Montant », « Solde (affaire / client) »
//
// Porte depuis Components/Ecrans/EcranEncaissements.razor (lot 4) : meme
// balisage, memes classes CSS. La date de virement de depart
// (JeuDemo.Aujourdhui) vient de Pont/EcranEncaissements.cs ; les ventilations
// de demonstration etaient declarees dans l'ecran et l'ont suivi. Montants en
// centimes entiers (le C# calculait en decimal, exact).
//
// BRANCHE SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026 ; marche du lot 17) : avec un serveur, l'ecran part de
// l'encaissement que RIG v2 prepare (Pont/EcranEncaissements.cs, Depart ; Serveur/Socle/EcransMain/LectureEncaissements.cs) --
// ouvert pour un dossier, une ventilation par couple affaire et client de ses factures, au solde du, cochee ; depuis l'accueil,
// un encaissement vide, sans ventilation (PROC_ENCAISS.cs:87). Les champs de demonstration (banque, titre, adresse du recu,
// encaissement repris) restent vides. Sans serveur, la demonstration ci-dessous, marquee « exemple » ; en panne, le motif et rien.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 4) : les ventilations prennent les
// briques de composants/ListeModerne.tsx. Tete (choisies, ventile sur encaisse, reste, « Aller a une ventilation… »
// Ctrl K, Entree coche ou decoche la ventilation restee seule), trois cartes : Ventile
// (anneau sur le montant encaisse ; le clic montre les seules ventilations choisies), Montants par client (le clic
// sur un client filtre), A ventiler. Une ligne par ventilation (client en avatar et abrege, montant avec sa
// mini-barre, ventile en point vert), pied de totaux. Le clic qui coche ou decoche une ventilation, la saisie et
// le compte rendu de ventilation ne changent pas ; les filtres montrent, ils ne cochent rien.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { centimes, euros } from '../format'
import { PourLeDossier } from '../composants/PourLeDossier'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import {
  CartesChiffrees, CelluleIdentite, EnteteListe, ListeModerne, MontantBarre, PiedListe, PointEtat, montant as texteMontant, normaliser, type Teinte,
} from '../composants/ListeModerne'
import type { ContexteDossierVue } from '../plan/types'

interface Ventilation {
  identifiant: string
  affaire: string
  client: string
  abrege: string
  /** centimes */
  montant: number
  /** centimes */
  soldeAffaire: number
  /** centimes */
  soldeClient: number
}

const Lignes: Ventilation[] = [
  { identifiant: 'V-2026-8801', affaire: '2026B00811 — Immatriculation', client: 'SELARL LEROY & ASSOCIES', abrege: 'LEROY', montant: 19485, soldeAffaire: 19485, soldeClient: 81240 },
  { identifiant: 'V-2026-8802', affaire: '2019B00233 — Modification', client: 'SELARL LEROY & ASSOCIES', abrege: 'LEROY', montant: 16272, soldeAffaire: 16272, soldeClient: 81240 },
  { identifiant: 'V-2026-8803', affaire: '2011B00512 — Depot comptes', client: 'CABINET BLANC', abrege: 'BLANC', montant: 5352, soldeAffaire: 5352, soldeClient: 32010 },
  { identifiant: 'V-2026-8804', affaire: '2026B00790 — Procedure', client: 'SCP DUPONT-MARTIN', abrege: 'DUPONT', montant: 8891, soldeAffaire: 8891, soldeClient: 14500 },
]

// v2 : lblEncaissRepris, affiche quand la saisie reprend un encaissement deja
// enregistre.
const EncaissementRepris: string | null = 'E-2026-0447 du 02/09/2026'

/** Le numero de l'affaire d'une ventilation (« 2026B00811 — Immatriculation » -> « 2026B00811 »). */
const numeroAffaire = (l: Ventilation): string => l.affaire.split(' — ')[0] ?? ''

/** Les teintes de la carte « Montants par client », dans l'ordre d'apparition des clients. */
const TeintesClients: readonly Teinte[] = ['bleu', 'violet', 'orange', 'vert', 'ardoise', 'rouge']

/** Ce que rend le pont (Pont/EcranEncaissements.cs, VueEncaissement) : l'encaissement de depart lu dans la base. */
export interface VueEncaissement {
  payeur: string
  /** centimes */
  montant: number
  ventilations: Ventilation[]
  mode?: ModeLecture
  message?: string | null
}

export interface ProprietesEcranEncaissements {
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) : le reglement est pour elle
   *  (bloc « Pour le dossier », ses ventilations cochees). Absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranEncaissements({ dossier = null }: ProprietesEcranEncaissements) {
  const [payeur, setPayeur] = useState('SELARL LEROY & ASSOCIES')
  const [modePaiement, setModePaiement] = useState('Cheque')
  // null tant que le pont n'a pas repondu (ou une fois la date effacee).
  const [dateVirement, setDateVirement] = useState<string | null>(null)
  const [recuDemande, setRecuDemande] = useState(true)
  const [banque, setBanque] = useState('CREDIT AGRICOLE')
  const [numTitre, setNumTitre] = useState('CH-4471902')
  const [commentaire, setCommentaire] = useState('')
  const [adresseRecu, setAdresseRecu] = useState('12 rue du Palais, 51100 Reims')
  // Le texte saisi et la derniere valeur LISIBLE : comme le @bind d'un
  // decimal, une saisie illisible ne remet pas le montant a zero.
  const [montantTexte, setMontantTexte] = useState('357.57')
  const [montant, setMontant] = useState(centimes(357.57))
  // Ouvert depuis une affaire : les ventilations cochees au depart sont les SIENNES
  // (aucune si elle n'en a pas), jamais celles d'autres affaires.
  // null : la demonstration (pas encore de reponse du pont, ou mode « demo »).
  const [vue, setVue] = useState<VueEncaissement | null>(null)
  const lignes = vue ? vue.ventilations : Lignes
  const repris = vue ? null : EncaissementRepris
  const [choisies, setChoisies] = useState<ReadonlySet<string>>(() => new Set(dossier
    ? Lignes.filter((l) => numeroAffaire(l) === dossier.numero).map((l) => l.identifiant)
    : ['V-2026-8801', 'V-2026-8802']))

  const numeroDossier = dossier?.numero ?? null
  useEffect(() => {
    let perime = false
    void appeler<VueEncaissement>('EcranEncaissements.Depart', numeroDossier)
      .then((v) => {
        if (perime || v.mode === 'demo' || v.mode === undefined) return
        // La base remplace la demonstration : l'encaissement de depart de RIG v2, ses ventilations cochees.
        setVue(v)
        setPayeur(v.payeur)
        setMontant(v.montant)
        setMontantTexte((v.montant / 100).toFixed(2))
        setBanque('')
        setNumTitre('')
        setAdresseRecu('')
        setChoisies(new Set(v.ventilations.map((l) => l.identifiant)))
      })
      .catch(() => { /* pas de pont (tests, page hors WebView2) : la demonstration reste */ })
    return () => { perime = true }
  }, [numeroDossier])

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranEncaissements.DateDuJour')
      .then((d) => { if (!perime) setDateVirement((avant) => avant ?? d) })
    return () => { perime = true }
  }, [])

  function saisirMontant(texte: string) {
    setMontantTexte(texte)
    const valeur = Number.parseFloat(texte)
    if (Number.isFinite(valeur)) setMontant(centimes(valeur))
  }

  function basculer(id: string) {
    setChoisies((avant) => {
      const s = new Set(avant)
      if (!s.delete(id)) s.add(id)
      return s
    })
  }

  const ventile = lignes.filter((l) => choisies.has(l.identifiant)).reduce((s, l) => s + l.montant, 0)
  const nonVentile = montant - ventile

  // Les filtres des cartes : les ventilations choisies seulement, un client. Ils montrent, ils ne cochent rien.
  const [seulementChoisies, setSeulementChoisies] = useState(false)
  const [filtreClient, setFiltreClient] = useState<string | null>(null)
  // « Aller a une ventilation… » (Ctrl K) : un payeur peut solder beaucoup d'affaires d'un coup.
  const [filtreRapide, setFiltreRapide] = useState('')
  const cherche = normaliser(filtreRapide.trim())
  const affichees = lignes.filter((l) => (!seulementChoisies || choisies.has(l.identifiant))
                                        && (filtreClient === null || l.abrege === filtreClient)
                                        && (cherche === '' || normaliser([l.identifiant, l.affaire, l.client, l.abrege].join(' ')).includes(cherche)))
  const montantMax = affichees.reduce((m, l) => Math.max(m, l.montant), 0)
  const clients = [...new Set(lignes.map((l) => l.abrege))]
  const parClient = (abrege: string) => lignes.filter((l) => l.abrege === abrege).reduce((s, l) => s + l.montant, 0)
  const euro = (c: number) => texteMontant(c / 100)

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        {dossier && <PourLeDossier dossier={dossier} />}

        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Saisie d'encaissement</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Payeur</span>
          <input className="rig-saisie-champ__saisie" value={payeur} onChange={(e) => setPayeur(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Mode</span>
          <select className="rig-saisie-champ__saisie" value={modePaiement} onChange={(e) => setModePaiement(e.target.value)}>
            <option>Cheque</option>
            <option>Virement</option>
            <option>Carte bancaire</option>
            <option>Especes</option>
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Date de virement</span>
          <input className="rig-saisie-champ__saisie" type="date"
                 value={dateVirement ?? ''} onChange={(e) => setDateVirement(e.target.value || null)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Banque</span>
          <input className="rig-saisie-champ__saisie" value={banque} onChange={(e) => setBanque(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">N de titre</span>
          <input className="rig-saisie-champ__saisie" value={numTitre} onChange={(e) => setNumTitre(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Montant encaisse</span>
          <input className="rig-saisie-champ__saisie" type="number" step="0.01"
                 value={montantTexte} onChange={(e) => saisirMontant(e.target.value)} />
        </label>

        <button className={`rig-bascule${recuDemande ? ' rig-bascule--actif' : ''}`}
                onClick={() => setRecuDemande(!recuDemande)}>
          <span className="rig-bascule__temoin"></span>
          <span>Emettre un recu</span>
        </button>

        {recuDemande && (
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Adresse du recu</span>
            <textarea className="rig-saisie-champ__saisie" rows={2}
                      value={adresseRecu} onChange={(e) => setAdresseRecu(e.target.value)}></textarea>
          </label>
        )}

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Commentaire</span>
          <textarea className="rig-saisie-champ__saisie" rows={2}
                    value={commentaire} onChange={(e) => setCommentaire(e.target.value)}></textarea>
        </label>

        {repris !== null && (
          <span className="rig-etat rig-etat--attente">Encaissement repris : {repris}</span>
        )}
      </aside>

      <ListeModerne etiquette="Ventilations">
        <EnteteListe titre="Ventilations"
                     synthese={<>{choisies.size}{` choisie(s) sur ${lignes.length} · `}<b>{euro(ventile)}</b>{' ventile(s) sur '}<b>{euro(montant)}</b>
                       {' · '}{nonVentile === 0 ? 'solde nul' : <><b>{euro(nonVentile)}</b>{' a ventiler'}</>}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une ventilation…',
                               valider: () => { if (affichees.length === 1) basculer(affichees[0]!.identifiant) } }} />
        {vue ? <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Ventilations" /> : <ProvenanceEcran mode="demo" quoi="Ventilations" />}

        <CartesChiffrees cartes={[
          { cle: 'ventile', titre: 'Ventile', valeur: euro(ventile), complement: `sur ${euro(montant)}`,
            anneau: { part: Math.min(ventile, montant), total: montant, teinte: 'vert' },
            actif: seulementChoisies, choisir: () => setSeulementChoisies(!seulementChoisies),
            indication: seulementChoisies ? 'Toutes les ventilations' : 'Seulement les ventilations choisies' },
          { cle: 'clients', titre: 'Montants par client', valeur: euro(lignes.reduce((s, l) => s + l.montant, 0)), legende: true,
            ...(filtreClient !== null ? { choisir: () => setFiltreClient(null), indication: 'Tous les clients' } : {}),
            segments: clients.map((abrege, i) => ({
              cle: abrege, libelle: abrege, valeur: parClient(abrege), texte: euro(parClient(abrege)),
              teinte: TeintesClients[i % TeintesClients.length]!, actif: filtreClient === abrege,
              choisir: () => setFiltreClient(filtreClient === abrege ? null : abrege),
            })) },
          { cle: 'reste', titre: 'A ventiler', valeur: nonVentile === 0 ? 'solde nul' : euro(nonVentile) },
        ]} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: '15%' }} /><col /><col style={{ width: '24%' }} /><col style={{ width: '16%' }} />
              <col style={{ width: '17%' }} /><col style={{ width: '10%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>N identifiant</th>
                <th>Client</th>
                <th>Affaire</th>
                <th className="rig-lm-droite">Montant</th>
                <th className="rig-lm-droite">Solde (affaire / client)</th>
                <th>Ventile</th>
              </tr>
            </thead>
            <tbody>
              {affichees.map((ligne) => {
                const choisi = choisies.has(ligne.identifiant)
                return (
                  <tr key={ligne.identifiant} className={choisi ? 'rig-ligne--valide' : ''}
                      onClick={() => basculer(ligne.identifiant)}>
                    <td className="rig-table__mono">{ligne.identifiant}</td>
                    <CelluleIdentite nom={ligne.client} objet={ligne.abrege} titre={`${ligne.client} — ${ligne.abrege}`} />
                    <td title={ligne.affaire}>{ligne.affaire}</td>
                    <td className="rig-lm-droite" title={euro(ligne.montant)}>
                      <MontantBarre valeur={ligne.montant} max={montantMax} texte={euro(ligne.montant)} />
                    </td>
                    <td className="rig-lm-droite rig-table__mono">{euros(ligne.soldeAffaire)} / {euros(ligne.soldeClient)}</td>
                    <td>{choisi && <PointEtat teinte="vert">oui</PointEtat>}</td>
                  </tr>
                )
              })}
              {affichees.length === 0 && (
                <tr><td colSpan={6} className="rig-table__vide">Aucune ventilation ne repond a ce filtre.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
          <span className={`rig-etat rig-etat--${nonVentile === 0 && choisies.size > 0 ? 'ok' : 'attente'}`}>
            {choisies.size === 0
              ? 'Choisissez les affaires a solder'
              : nonVentile === 0
                ? 'Encaissement entierement ventile'
                : 'Ventilation incomplete'}
          </span>
        </div>

        <PiedListe aide="Clic sur une ligne pour la ventiler ou la retirer.">
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' ventilations affichees' : ' ventilation affichee'}</span>
          <span>Ventile <b>{euro(ventile)}</b></span>
          <span>Encaisse <b>{euro(montant)}</b></span>
          <span>A ventiler <b>{euro(nonVentile)}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
