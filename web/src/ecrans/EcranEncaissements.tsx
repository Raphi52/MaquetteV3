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

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { centimes, euros } from '../format'

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

export function EcranEncaissements() {
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
  const [choisies, setChoisies] = useState<ReadonlySet<string>>(new Set(['V-2026-8801', 'V-2026-8802']))

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

  const ventile = Lignes.filter((l) => choisies.has(l.identifiant)).reduce((s, l) => s + l.montant, 0)
  const nonVentile = montant - ventile

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
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

        {EncaissementRepris !== null && (
          <span className="rig-etat rig-etat--attente">Encaissement repris : {EncaissementRepris}</span>
        )}
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Ventilations</div>
          <span className="rig-resultats-proc__compte">
            {euros(ventile)} EUR ventile(s) sur {euros(montant)} EUR — {nonVentile === 0 ? 'solde nul' : euros(nonVentile) + ' EUR a ventiler'}
          </span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>N identifiant</th>
                <th>Affaire</th>
                <th>Client</th>
                <th>Abrege client</th>
                <th>Montant</th>
                <th>Solde (affaire / client)</th>
                <th className="rig-table__centre">Ventile</th>
              </tr>
            </thead>
            <tbody>
              {Lignes.map((ligne) => {
                const choisi = choisies.has(ligne.identifiant)
                return (
                  <tr key={ligne.identifiant} className={choisi ? 'rig-ligne--valide' : ''}
                      onClick={() => basculer(ligne.identifiant)}>
                    <td className="rig-table__mono">{ligne.identifiant}</td>
                    <td>{ligne.affaire}</td>
                    <td>{ligne.client}</td>
                    <td className="rig-table__mono">{ligne.abrege}</td>
                    <td className="rig-table__mono">{euros(ligne.montant)}</td>
                    <td className="rig-table__mono">{euros(ligne.soldeAffaire)} / {euros(ligne.soldeClient)}</td>
                    <td className="rig-table__centre">{choisi ? 'oui' : ''}</td>
                  </tr>
                )
              })}
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
      </section>
    </div>
  )
}
