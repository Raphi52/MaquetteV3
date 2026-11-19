// Ecran de la procedure PROC_FCLIENT.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_FCLIENT) :
//   - OPE_IDENTIFICATION : _ultAbrege, _ultNomClient, _ultNumeroCompte,
//     _ultNumeroSiren, _ultDateNaissance, _ultEmailClient, _ultPassword,
//     _ultComboBoxEtatDuCompte, _ultComboBoxGroupeClient,
//     _ultComboBoxTypeDeCompte, _ultTextNumPCouPrevention, et les boutons
//     _ultButtonCompleterNumeroCompte, _ultButtonUpdateFromSiren,
//     _ultButtonGeneratePassword
//   - OPE_ENTREPRISE     : _ultTextTelephonePrincipal, _ultTextTelephonePortable
//   - OPE_PARAMETRAGE    : _ultComboModePaiement, _ultMontantProvision,
//     _ultCheckBoxReleveFacture
//   - OPE_INFORMATIONS   : _ultDateDerniereModif, _ultDateFactuFirst /
//     _ultDateFactuLast, _ultDateEncaissFirst / _ultDateEncaissLast,
//     _ultTextNbFactures, _ultTextNbEncaissements, _ultTextSynchroInfogreffe,
//     _ultTextMultiLineNotesClient, et la grille _ultDataGridViewLatestBalance
//     (colonnes Date et Solde)
//
// NOTE : les colonnes Mouvement, Debit et Credit de la grille ci-dessous
// sont un ajout de la maquette ; la grille v2 n a que Date et Solde.
//
// Porte depuis Components/Ecrans/EcranFicheClient.razor (lot 4) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// fiches, mouvements et contacts de demonstration l'ont suivi ici. Montants
// en centimes entiers (le C# calculait en decimal, exact).

import { useState } from 'react'
import { dateCourte } from '../criteres'
import { euros } from '../format'

interface FicheCli {
  abrege: string
  nom: string
  compte: string
  siren: string
  adresse: string
  type: string
  etatCompte: string
  groupe: string
  naissance: string
  numPc: string
  mail: string
  telPrincipal: string
  telPortable: string
  modePaiement: string
  /** centimes */
  provision: number
  releveFacture: boolean
  factuPremiere: string
  factuDerniere: string
  encaissPremier: string
  encaissDernier: string
  nbFactures: number
  nbEncaissements: number
  derniereModif: string
  synchroInfogreffe: string
  notes: string
}

interface Mouvement {
  abrege: string
  /** aaaa-mm-jj */
  date: string
  libelle: string
  /** centimes */
  debit: number
  /** centimes */
  credit: number
  /** centimes */
  solde: number
}

interface Contact {
  abrege: string
  nom: string
  fonction: string
  tel: string
  mail: string
  principal: boolean
}

const Fiches: FicheCli[] = [
  { abrege: 'LEROY', nom: 'SELARL LEROY & ASSOCIES', compte: 'C-000142', siren: '412 875 330', adresse: '12 rue du Palais\n51100 Reims', type: 'En compte', etatCompte: 'Ouvert', groupe: 'Mandataires', naissance: '', numPc: '', mail: 'contact@leroy-associes.fr', telPrincipal: '03 26 40 11 22', telPortable: '06 12 45 78 90', modePaiement: 'Prelevement', provision: 150000, releveFacture: true, factuPremiere: '04/01/2019', factuDerniere: '02/09/2026', encaissPremier: '11/01/2019', encaissDernier: '28/08/2026', nbFactures: 412, nbEncaissements: 388, derniereModif: '02/09/2026', synchroInfogreffe: '08/09/2026', notes: 'Releve mensuel, envoi par courriel.' },
  { abrege: 'BLANC', nom: 'CABINET BLANC', compte: 'C-000188', siren: '509 221 004', adresse: '3 place Drouet\n51100 Reims', type: 'En compte', etatCompte: 'Ouvert', groupe: 'Mandataires', naissance: '', numPc: 'PC2026-042', mail: 's.blanc@cabinet-blanc.fr', telPrincipal: '03 26 40 55 01', telPortable: '', modePaiement: 'Virement', provision: 80000, releveFacture: false, factuPremiere: '12/03/2020', factuDerniere: '19/08/2026', encaissPremier: '20/03/2020', encaissDernier: '19/08/2026', nbFactures: 226, nbEncaissements: 210, derniereModif: '19/08/2026', synchroInfogreffe: '08/09/2026', notes: '' },
  { abrege: 'DUPONT', nom: 'SCP DUPONT-MARTIN', compte: 'C-000231', siren: '788 410 226', adresse: '22 rue Chanzy\n51100 Reims', type: 'En compte', etatCompte: 'Bloque', groupe: 'Avocats', naissance: '14/05/1971', numPc: '', mail: 'greffe@dupont-martin.fr', telPrincipal: '03 26 77 43 09', telPortable: '06 88 21 04 55', modePaiement: 'Cheque', provision: 0, releveFacture: false, factuPremiere: '08/06/2021', factuDerniere: '14/07/2026', encaissPremier: '15/06/2021', encaissDernier: '14/07/2026', nbFactures: 97, nbEncaissements: 95, derniereModif: '14/07/2026', synchroInfogreffe: '07/09/2026', notes: 'Compte bloque : relance impayes en cours.' },
]

const Mouvements: Mouvement[] = [
  { abrege: 'LEROY', date: '2026-07-04', libelle: 'Facture FA-44120', debit: 19485, credit: 0, solde: 19485 },
  { abrege: 'LEROY', date: '2026-07-28', libelle: 'Encaissement CH-4471', debit: 0, credit: 19485, solde: 0 },
  { abrege: 'LEROY', date: '2026-08-19', libelle: 'Facture FA-45120', debit: 81240, credit: 0, solde: 81240 },
  { abrege: 'BLANC', date: '2026-08-14', libelle: 'Facture FA-45121', debit: 32010, credit: 0, solde: 32010 },
  { abrege: 'DUPONT', date: '2026-06-02', libelle: 'Facture FA-43980', debit: 14500, credit: 0, solde: 14500 },
]

const Contacts: Contact[] = [
  { abrege: 'LEROY', nom: 'Antoine LEROY', fonction: 'Mandataire', tel: '03 26 40 11 22', mail: 'a.leroy@exemple.fr', principal: true },
  { abrege: 'LEROY', nom: 'Sarah HUET', fonction: 'Assistante', tel: '03 26 40 11 25', mail: 's.huet@exemple.fr', principal: false },
  { abrege: 'BLANC', nom: 'Sophie BLANC', fonction: 'Administratrice', tel: '03 26 40 55 01', mail: 's.blanc@exemple.fr', principal: true },
  { abrege: 'DUPONT', nom: 'Marc DUPONT', fonction: 'Avocat', tel: '03 26 77 43 09', mail: 'm.dupont@exemple.fr', principal: true },
]

export function EcranFicheClient() {
  const [abrege, setAbrege] = useState('LEROY')
  const [ongletCompte, setOngletCompte] = useState(true)

  const fiche = Fiches.find((f) => f.abrege === abrege)!
  const mouvementsClient = Mouvements.filter((m) => m.abrege === abrege)
  const contactsClient = Contacts.filter((c) => c.abrege === abrege)
  const solde = mouvementsClient.at(-1)?.solde ?? 0

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Client</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Choisir un client</span>
          <select className="rig-saisie-champ__saisie" value={abrege} onChange={(e) => setAbrege(e.target.value)}>
            {Fiches.map((f) => <option key={f.abrege} value={f.abrege}>{f.abrege} — {f.nom}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Entreprise</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Raison sociale</span>
            <input className="rig-saisie-champ__saisie" value={fiche.nom} readOnly />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">N de compte</span>
            <input className="rig-saisie-champ__saisie" value={fiche.compte} readOnly />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Siren</span>
            <input className="rig-saisie-champ__saisie" value={fiche.siren} readOnly />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Adresse</span>
            <textarea className="rig-saisie-champ__saisie" rows={2} readOnly value={fiche.adresse} />
          </label>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Abrege</span>
              <input className="rig-saisie-champ__saisie" value={fiche.abrege} readOnly />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Type de compte</span>
              <input className="rig-saisie-champ__saisie" value={fiche.type} readOnly />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Etat du compte</span>
              <input className="rig-saisie-champ__saisie" value={fiche.etatCompte} readOnly />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Groupe client</span>
              <input className="rig-saisie-champ__saisie" value={fiche.groupe} readOnly />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Date de naissance</span>
              <input className="rig-saisie-champ__saisie" value={fiche.naissance} readOnly />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">N PC ou prevention</span>
              <input className="rig-saisie-champ__saisie" value={fiche.numPc} readOnly />
            </label>
          </div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">E-mail du client</span>
            <input className="rig-saisie-champ__saisie" value={fiche.mail} readOnly />
          </label>
        </div>

        {/* v2 : OPE_ENTREPRISE. */}
        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Telephones</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Principal</span>
              <input className="rig-saisie-champ__saisie" value={fiche.telPrincipal} readOnly />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Portable</span>
              <input className="rig-saisie-champ__saisie" value={fiche.telPortable} readOnly />
            </label>
          </div>
        </div>

        {/* v2 : OPE_PARAMETRAGE. */}
        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Parametrage</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Mode de paiement</span>
              <input className="rig-saisie-champ__saisie" value={fiche.modePaiement} readOnly />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Montant de provision</span>
              <input className="rig-saisie-champ__saisie" value={euros(fiche.provision)} readOnly />
            </label>
          </div>
          <span className={`rig-etat rig-etat--${fiche.releveFacture ? 'ok' : 'attente'}`}>
            {fiche.releveFacture ? 'Releve de facture actif' : 'Pas de releve de facture'}
          </span>
        </div>

        {/* v2 : OPE_INFORMATIONS. */}
        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Informations</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Premiere facture</span>
              <input className="rig-saisie-champ__saisie" value={fiche.factuPremiere} readOnly />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Derniere facture</span>
              <input className="rig-saisie-champ__saisie" value={fiche.factuDerniere} readOnly />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Premier encaissement</span>
              <input className="rig-saisie-champ__saisie" value={fiche.encaissPremier} readOnly />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Dernier encaissement</span>
              <input className="rig-saisie-champ__saisie" value={fiche.encaissDernier} readOnly />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Nb factures</span>
              <input className="rig-saisie-champ__saisie" value={fiche.nbFactures} readOnly />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Nb encaissements</span>
              <input className="rig-saisie-champ__saisie" value={fiche.nbEncaissements} readOnly />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Derniere modification</span>
              <input className="rig-saisie-champ__saisie" value={fiche.derniereModif} readOnly />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Synchro Infogreffe</span>
              <input className="rig-saisie-champ__saisie" value={fiche.synchroInfogreffe} readOnly />
            </label>
          </div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Notes client</span>
            <textarea className="rig-saisie-champ__saisie" rows={2} readOnly value={fiche.notes} />
          </label>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-onglets">
            <button className={`rig-onglet${ongletCompte ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setOngletCompte(true)}>Compte</button>
            <button className={`rig-onglet${!ongletCompte ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setOngletCompte(false)}>Contacts</button>
          </div>
          <span className="rig-resultats-proc__compte">Solde actuel {euros(solde)} EUR</span>
        </div>

        {ongletCompte ? (
          <div className="rig-table-cadre">
            <table className="rig-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Mouvement</th>
                  <th>Debit</th>
                  <th>Credit</th>
                  <th>Solde</th>
                </tr>
              </thead>
              <tbody>
                {mouvementsClient.map((m, i) => (
                  <tr key={i}>
                    <td className="rig-table__mono">{dateCourte(m.date)}</td>
                    <td>{m.libelle}</td>
                    <td className="rig-table__mono">{m.debit === 0 ? '' : euros(m.debit)}</td>
                    <td className="rig-table__mono">{m.credit === 0 ? '' : euros(m.credit)}</td>
                    <td className="rig-table__mono">{euros(m.solde)}</td>
                  </tr>
                ))}
                {mouvementsClient.length === 0 && (
                  <tr><td colSpan={5} className="rig-table__vide">Aucun mouvement sur ce compte.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="rig-table-cadre">
            <table className="rig-table">
              <thead>
                <tr>
                  <th>Nom</th>
                  <th>Fonction</th>
                  <th>Telephone</th>
                  <th>E-mail</th>
                  <th className="rig-table__centre">Principal</th>
                </tr>
              </thead>
              <tbody>
                {contactsClient.map((c) => (
                  <tr key={c.nom}>
                    <td>{c.nom}</td>
                    <td>{c.fonction}</td>
                    <td className="rig-table__mono">{c.tel}</td>
                    <td className="rig-table__mono">{c.mail}</td>
                    <td className="rig-table__centre">{c.principal ? 'oui' : ''}</td>
                  </tr>
                ))}
                {contactsClient.length === 0 && (
                  <tr><td colSpan={5} className="rig-table__vide">Aucun contact enregistre.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
