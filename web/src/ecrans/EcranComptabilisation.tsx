// Ecran de la procedure PROC_COMPTABILISATION (RIG v2 : « Comptabilisation »,
// OPE_COMPTABILISATION + TreeView/TreeViewJoursCompta). Le v2 affiche un arbre
// des JOURNEES non comptabilisees ; sous chaque journee, l'agregat des factures
// (Classes/FactureJour : NbFactures, MontantTotalTTC, NbAffairesEnCours,
// NbSansDemande) et les encaissements groupes par remis
// (Classes/EncaissementRemis : Remis, ModePaiement, NbEncaissement,
// SommeEncaissement, DateEncaissement ; un remis absent = « non remis »).
// Traitements lus dans OPE_COMPTABILISATION.cs : « Comptabilisation des
// factures », « Comptabilisation des encaissements », « Remettre en banque »,
// « Lettrage des PV et REG du ... ».
//
// Porte depuis Components/Ecrans/EcranComptabilisation.razor (lot 4) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// journees de demonstration l'ont suivi ici. Montants en centimes entiers
// (le C# calculait en decimal, exact).

import { useState } from 'react'
import { dateCourte } from '../criteres'
import { euros } from '../format'

// v2 : Classes/EncaissementRemis.cs
interface Remis {
  numeroRemis: number
  modePaiement: string
  nbEncaissement: number
  /** centimes */
  sommeEncaissement: number
  /** aaaa-mm-jj */
  dateEncaissement: string
}

// v2 : Classes/FactureJour.cs + Classes/DateCompta.cs
interface Journee {
  /** aaaa-mm-jj */
  date: string
  nbFactures: number
  /** centimes */
  montantTotalTTC: number
  nbAffairesEnCours: number
  nbSansDemande: number
  remis: Remis[]
}

const Journees: Journee[] = [
  {
    date: '2026-09-09', nbFactures: 14, montantTotalTTC: 381240, nbAffairesEnCours: 11, nbSansDemande: 1,
    remis: [
      { numeroRemis: 4821, modePaiement: 'Cheque', nbEncaissement: 9, sommeEncaissement: 214000, dateEncaissement: '2026-09-09' },
      { numeroRemis: 4822, modePaiement: 'Virement', nbEncaissement: 4, sommeEncaissement: 145040, dateEncaissement: '2026-09-09' },
      { numeroRemis: 0, modePaiement: 'Carte bancaire', nbEncaissement: 2, sommeEncaissement: 22200, dateEncaissement: '2026-09-09' },
    ],
  },
  {
    date: '2026-09-08', nbFactures: 6, montantTotalTTC: 120480, nbAffairesEnCours: 6, nbSansDemande: 0,
    remis: [
      { numeroRemis: 4818, modePaiement: 'Cheque', nbEncaissement: 5, sommeEncaissement: 98080, dateEncaissement: '2026-09-08' },
      { numeroRemis: 4819, modePaiement: 'Especes', nbEncaissement: 1, sommeEncaissement: 22400, dateEncaissement: '2026-09-08' },
    ],
  },
  { date: '2026-09-07', nbFactures: 3, montantTotalTTC: 45600, nbAffairesEnCours: 2, nbSansDemande: 1, remis: [] },
]

const somme = (valeurs: number[]) => valeurs.reduce((a, b) => a + b, 0)
const nbEncaissements = (j: Journee) => somme(j.remis.map((r) => r.nbEncaissement))

export function EcranComptabilisation() {
  const [selection, setSelection] = useState<Journee>(Journees[0]!)

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Journees a comptabiliser</div>

        {Journees.map((jour) => (
          <button key={jour.date} className={`rig-bascule${selection.date === jour.date ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setSelection(jour)}>
            <span className="rig-bascule__temoin"></span>
            <span>{dateCourte(jour.date)} — {jour.nbFactures} facture(s), {nbEncaissements(jour)} encaissement(s)</span>
          </button>
        ))}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Ce qui reste</div>
          <span className="rig-chip">{Journees.length} jour(s) a comptabiliser</span>
          <span className="rig-chip">{somme(Journees.map((j) => j.nbFactures))} facture(s)</span>
          <span className="rig-chip">{euros(somme(Journees.flatMap((j) => j.remis.map((r) => r.sommeEncaissement))))} € encaisses</span>
        </div>

        {/* v2 : ult_LblParam, affiche quand EXPRESSO_FIGERFACTAUTO est actif. */}
        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Parametre du greffe</div>
          <span>Votre greffe est parametre pour ne cocher que les encaissements lorsque vous cochez une journee.</span>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Journee du {dateCourte(selection.date)}</div>
          <span className="rig-resultats-proc__compte">{selection.nbFactures} facture(s) — {euros(selection.montantTotalTTC)} € TTC</span>
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton">Comptabiliser les factures</button>{' '}
          <button className="rig-bouton">Comptabiliser les encaissements</button>{' '}
          <button className="rig-bouton rig-bouton--plat">Remettre en banque</button>{' '}
          <button className="rig-bouton rig-bouton--plat">Lettrage des PV et REG</button>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Factures de la journee</div>
          <span className="rig-chip">{selection.nbFactures} facture(s)</span>
          <span className="rig-chip">{euros(selection.montantTotalTTC)} € TTC</span>
          <span className="rig-chip">{selection.nbAffairesEnCours} affaire(s) en cours</span>
          <span className="rig-chip">{selection.nbSansDemande} sans demande</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Remis</th>
                <th>Mode de paiement</th>
                <th className="rig-table__centre">Nb</th>
                <th>Montant</th>
                <th>Date d'encaissement</th>
              </tr>
            </thead>
            <tbody>
              {selection.remis.map((r, i) => (
                <tr key={i}>
                  <td className="rig-table__mono">{r.numeroRemis === 0 ? 'non remis' : r.numeroRemis}</td>
                  <td>{r.modePaiement}</td>
                  <td className="rig-table__centre">{r.nbEncaissement}</td>
                  <td className="rig-table__mono">{euros(r.sommeEncaissement)} €</td>
                  <td className="rig-table__mono">{dateCourte(r.dateEncaissement)}</td>
                </tr>
              ))}
              {selection.remis.length === 0 && (
                <tr><td colSpan={5} className="rig-table__vide">Aucun encaissement pour cette journee.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
