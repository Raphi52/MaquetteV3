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
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 4) : les remis de la journee prennent
// les briques de composants/ListeModerne.tsx. Tete (factures et TTC, encaissements et somme), trois cartes :
// Factures de la journee (l'agregat FactureJour, qui remplace ses quatre puces), Encaissements par mode (le clic
// sur un mode filtre les remis), Non remis en banque (anneau, le clic filtre). Une ligne par remis : remis en
// point (vert, ou orange « non remis »), mode en pastille, montant avec sa mini-barre ; pied de totaux. Choisir
// une autre journee enleve le filtre. Les journees, « Ce qui reste » et les quatre traitements ne changent pas.

import { useState } from 'react'
import { dateCourte } from '../criteres'
import { euros } from '../format'
import {
  CartesChiffrees, EnteteListe, ListeModerne, MontantBarre, Pastille, PiedListe, PointEtat, montant, type Teinte,
} from '../composants/ListeModerne'

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

/** Les modes de paiement, dans l'ordre de la saisie d'un encaissement, et leur teinte. */
const Modes = ['Cheque', 'Virement', 'Carte bancaire', 'Especes']
const TeinteMode: Record<string, Teinte> = { Cheque: 'bleu', Virement: 'violet', 'Carte bancaire': 'orange', Especes: 'vert' }
/** Le filtre de la carte « Non remis en banque ». */
const NonRemis = 'non-remis'

export function EcranComptabilisation() {
  const [selection, setSelection] = useState<Journee>(Journees[0]!)
  // Le filtre pose par une carte : un mode de paiement, ou les encaissements non remis.
  const [filtre, setFiltre] = useState<string | null>(null)

  function choisirJournee(jour: Journee) {
    setSelection(jour)
    setFiltre(null)
  }

  // Les chiffres de la tete, des cartes et du pied, en centimes : sur les remis de la journee.
  const remis = selection.remis
  const encaisse = somme(remis.map((r) => r.sommeEncaissement))
  const nonRemis = remis.filter((r) => r.numeroRemis === 0)
  const sommeNonRemis = somme(nonRemis.map((r) => r.sommeEncaissement))
  // Les modes de la journee, dans l'ordre de Modes ; un mode inconnu de Modes vient a la suite, il n'est pas perdu.
  const modes = [...new Set([...Modes, ...remis.map((r) => r.modePaiement)])].filter((m) => remis.some((r) => r.modePaiement === m))
  const affiches = remis.filter((r) => filtre === null || (filtre === NonRemis ? r.numeroRemis === 0 : r.modePaiement === filtre))
  const montantMax = affiches.reduce((m, r) => Math.max(m, r.sommeEncaissement), 0)
  const basculerFiltre = (f: string) => () => setFiltre(filtre === f ? null : f)
  const parMode = (m: string) => somme(remis.filter((r) => r.modePaiement === m).map((r) => r.sommeEncaissement))

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Journees a comptabiliser</div>

        {Journees.map((jour) => (
          <button key={jour.date} className={`rig-bascule${selection.date === jour.date ? ' rig-bascule--actif' : ''}`}
                  onClick={() => choisirJournee(jour)}>
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

      <ListeModerne etiquette={`Journee du ${dateCourte(selection.date)}`}>
        <EnteteListe titre={`Journee du ${dateCourte(selection.date)}`}
                     synthese={<>{selection.nbFactures}{' facture(s) · '}<b>{montant(selection.montantTotalTTC / 100)}</b>{' TTC · '}
                       {nbEncaissements(selection)}{' encaissement(s) · '}<b>{montant(encaisse / 100)}</b>{' encaisses'}</>} />

        {/* Les factures de la journee (v2 : Classes/FactureJour), puis la repartition des encaissements. */}
        <CartesChiffrees cartes={[
          { cle: 'factures', titre: 'Factures de la journee', valeur: montant(selection.montantTotalTTC / 100),
            complement: `TTC · ${selection.nbFactures} facture(s) · ${selection.nbAffairesEnCours} affaire(s) en cours · ${selection.nbSansDemande} sans demande` },
          { cle: 'encaissements', titre: 'Encaissements par mode', valeur: montant(encaisse / 100), legende: true,
            ...(filtre !== null && filtre !== NonRemis ? { choisir: () => setFiltre(null), indication: 'Tous les modes' } : {}),
            segments: modes.map((m) => ({
              cle: m, libelle: m, valeur: parMode(m), texte: montant(parMode(m) / 100), teinte: TeinteMode[m] ?? 'gris',
              actif: filtre === m, choisir: basculerFiltre(m),
            })) },
          { cle: 'non-remis', titre: 'Non remis en banque', valeur: montant(sommeNonRemis / 100),
            complement: `sur ${montant(encaisse / 100)}`,
            anneau: { part: sommeNonRemis, total: encaisse, teinte: 'orange' },
            actif: filtre === NonRemis, choisir: basculerFiltre(NonRemis),
            indication: filtre === NonRemis ? 'Tous les encaissements' : 'Seulement les encaissements non remis' },
        ]} />

        <div className="rig-criteres__actions">
          <button className="rig-bouton">Comptabiliser les factures</button>{' '}
          <button className="rig-bouton">Comptabiliser les encaissements</button>{' '}
          <button className="rig-bouton rig-bouton--plat">Remettre en banque</button>{' '}
          <button className="rig-bouton rig-bouton--plat">Lettrage des PV et REG</button>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: '18%' }} /><col /><col style={{ width: '10%' }} /><col style={{ width: '24%' }} /><col style={{ width: '20%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>Remis</th>
                <th>Mode de paiement</th>
                <th className="rig-table__centre">Nb</th>
                <th className="rig-lm-droite">Montant</th>
                <th>Date d'encaissement</th>
              </tr>
            </thead>
            <tbody>
              {affiches.map((r, i) => (
                <tr key={i}>
                  <td>
                    <PointEtat teinte={r.numeroRemis === 0 ? 'orange' : 'vert'}>{r.numeroRemis === 0 ? 'non remis' : r.numeroRemis}</PointEtat>
                  </td>
                  <td><Pastille teinte={TeinteMode[r.modePaiement] ?? 'gris'}>{r.modePaiement}</Pastille></td>
                  <td className="rig-table__centre">{r.nbEncaissement}</td>
                  <td className="rig-lm-droite" title={montant(r.sommeEncaissement / 100)}>
                    <MontantBarre valeur={r.sommeEncaissement} max={montantMax} texte={montant(r.sommeEncaissement / 100)} />
                  </td>
                  <td className="rig-table__mono">{dateCourte(r.dateEncaissement)}</td>
                </tr>
              ))}
              {remis.length === 0 && (
                <tr><td colSpan={5} className="rig-table__vide">Aucun encaissement pour cette journee.</td></tr>
              )}
              {remis.length > 0 && affiches.length === 0 && (
                <tr><td colSpan={5} className="rig-table__vide">Aucun encaissement ne repond a ce filtre.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{affiches.length}</b>{' remis affiche(s)'}</span>
          <span>Encaissements <b>{somme(affiches.map((r) => r.nbEncaissement))}</b></span>
          <span>Total <b>{montant(somme(affiches.map((r) => r.sommeEncaissement)) / 100)}</b></span>
          <span>Non remis <b>{montant(sommeNonRemis / 100)}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
