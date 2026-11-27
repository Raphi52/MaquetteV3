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
// balisage, memes classes CSS. Montants en centimes entiers (le C# calcule en
// decimal, exact, et les convertit : Pont/EcranComptabilisationBase.cs).
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 4) : les remis de la journee prennent
// les briques de composants/ListeModerne.tsx. Tete (factures et TTC, encaissements et somme), trois cartes :
// Factures de la journee (l'agregat FactureJour, qui remplace ses quatre puces), Encaissements par mode (le clic
// sur un mode filtre les remis), Non remis en banque (anneau, le clic filtre). Une ligne par remis : remis en
// point (vert, ou orange « non remis »), mode en pastille, montant avec sa mini-barre ; pied de totaux. Choisir
// une autre journee enleve le filtre. Les journees, « Ce qui reste » et les quatre traitements ne changent pas.
//
// LA BASE (plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026) : les journees viennent du pont
// (Pont/EcranComptabilisationBase.cs) -- la base du greffe (Serveur/Socle/EcransMain/LectureComptabilisation.cs,
// regles de RIG v2 citees), ou, sans serveur, la demonstration, marquee « exemple ». Comme RIG v2 : de la plus
// ancienne journee a la plus recente ; une facture sans demande bloque l'arbre (le message d'anomalie de RIG v2,
// aucune journee) ; le libelle du parametre EXPRESSO_FIGERFACTAUTO ne s'affiche que si le greffe l'a a « 0 ».
// Les quatre traitements ne sont pas branches : leurs boutons ne font rien.

import { useEffect, useState } from 'react'
import { dateCourte } from '../criteres'
import { euros } from '../format'
import { appeler } from '../pont'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import {
  CartesChiffrees, EnteteListe, ListeModerne, MontantBarre, Pastille, PiedListe, PointEtat, montant, type Teinte,
} from '../composants/ListeModerne'

// v2 : Classes/EncaissementRemis.cs
export interface Remis {
  /** 0 : les encaissements non remis du mode */
  numeroRemis: number
  /** le libelle du mode (MODE_PAIEMENT) */
  modePaiement: string
  /** le type du mode (R_MODEPAIEME_TYPE : CH, VI, CB, ES...) */
  typeMode: string
  nbEncaissement: number
  /** centimes */
  sommeEncaissement: number
  /** aaaa-mm-jj */
  dateEncaissement: string
  /** « Attention des encaissements remis du meme type sont deja presents » (RIG v2), vide sinon */
  observation?: string
}

// v2 : Classes/FactureJour.cs + Classes/DateCompta.cs
export interface Journee {
  /** aaaa-mm-jj */
  date: string
  nbFactures: number
  /** centimes */
  montantTotalTTC: number
  nbAffairesEnCours: number
  nbSansDemande: number
  remis: Remis[]
}

export interface VueComptabilisation {
  journees: Journee[]
  /** Le nombre de journees de la base (le pont en rend au plus son plafond). */
  total: number
  /** Le message bloquant de RIG v2 (factures sans demande) : aucune journee alors. */
  anomalie: string | null
  /** EXPRESSO_FIGERFACTAUTO a « 0 » : cocher une journee ne coche que ses encaissements. */
  encaissementsSeuls: boolean
  mode?: ModeLecture
  message?: string | null
}

const somme = (valeurs: number[]) => valeurs.reduce((a, b) => a + b, 0)
const nbEncaissements = (j: Journee) => somme(j.remis.map((r) => r.nbEncaissement))

/** Les modes de paiement, dans l'ordre de la saisie d'un encaissement, et leur teinte. */
const Modes = ['Cheque', 'Virement', 'Carte bancaire', 'Especes']
const TeinteMode: Record<string, Teinte> = { Cheque: 'bleu', Virement: 'violet', 'Carte bancaire': 'orange', Especes: 'vert' }
/** La teinte par le TYPE du mode (MODE_PAIEMENT.R_MODEPAIEME_TYPE), qui ne depend pas du libelle du greffe. */
const TeinteType: Record<string, Teinte> = { CH: 'bleu', VI: 'violet', CB: 'orange', ES: 'vert' }
const teinte = (r: Remis): Teinte => TeinteType[r.typeMode] ?? TeinteMode[r.modePaiement] ?? 'gris'
/** Le filtre de la carte « Non remis en banque ». */
const NonRemis = 'non-remis'

export function EcranComptabilisation() {
  // null tant que le pont n'a pas repondu : ni journee ni « aucune journee », qui serait faux pendant ce court instant.
  const [vue, setVue] = useState<VueComptabilisation | null>(null)
  const [selection, setSelection] = useState<string | null>(null)
  // Le filtre pose par une carte : un mode de paiement, ou les encaissements non remis.
  const [filtre, setFiltre] = useState<string | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueComptabilisation>('EcranComptabilisation.Journees').then((v) => {
      if (perime) return
      setVue(v)
      setSelection(v.journees[0]?.date ?? null)
    })
    return () => { perime = true }
  }, [])

  const journees = vue?.journees ?? []
  const jour = journees.find((j) => j.date === selection) ?? null

  function choisirJournee(j: Journee) {
    setSelection(j.date)
    setFiltre(null)
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Journees a comptabiliser</div>
        {vue && <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Journées" />}

        {journees.map((j) => (
          <button key={j.date} className={`rig-bascule${selection === j.date ? ' rig-bascule--actif' : ''}`}
                  onClick={() => choisirJournee(j)}>
            <span className="rig-bascule__temoin"></span>
            <span>{dateCourte(j.date)} — {j.nbFactures} facture(s), {nbEncaissements(j)} encaissement(s)</span>
          </button>
        ))}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Ce qui reste</div>
          <span className="rig-chip">{vue?.total ?? 0} jour(s) a comptabiliser</span>
          <span className="rig-chip">{somme(journees.map((j) => j.nbFactures))} facture(s)</span>
          <span className="rig-chip">{euros(somme(journees.flatMap((j) => j.remis.map((r) => r.sommeEncaissement))))} € encaisses</span>
        </div>

        {/* v2 : ult_LblParam, affiche quand EXPRESSO_FIGERFACTAUTO est a « 0 » (OPE_COMPTABILISATION.cs:148). */}
        {vue?.encaissementsSeuls && (
          <div className="rig-criteres__bloc">
            <div className="rig-kicker">Parametre du greffe</div>
            <span>Votre greffe est parametre pour ne cocher que les encaissements lorsque vous cochez une journee.</span>
          </div>
        )}
      </aside>

      {jour ? <JourneeChoisie jour={jour} filtre={filtre} setFiltre={setFiltre} /> : (
        <ListeModerne etiquette="Journees a comptabiliser">
          {/* La tete porte le bouton « Criteres » : sans elle, le panneau de gauche, replie au depart (ListeModerne), ne se
              rouvrirait plus (mesure le 27/11/2026 sur la copie de dev : anomalie seule, panneau invisible). */}
          <EnteteListe titre="Journees a comptabiliser" synthese={<>{vue?.total ?? 0}{' jour(s) a comptabiliser'}</>} />
          {vue?.anomalie ? (
            // RIG v2 : DialogBox.Show(« ANOMALIE : ... »), puis l'arbre vide (OPE_COMPTABILISATION.cs:186-193).
            <p className="rig-eg-note" role="alert" style={{ whiteSpace: 'pre-line' }}>
              <PointEtat teinte="rouge">Anomalie</PointEtat>{' '}{vue.anomalie}
            </p>
          ) : vue && (vue.mode === undefined || vue.mode === 'serveur' || vue.mode === 'demo') && (
            <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>Aucune journee a comptabiliser.</div>
          )}
        </ListeModerne>
      )}
    </div>
  )
}

function JourneeChoisie({ jour, filtre, setFiltre }: {
  jour: Journee
  filtre: string | null
  setFiltre: (f: string | null) => void
}) {
  // Les chiffres de la tete, des cartes et du pied, en centimes : sur les remis de la journee.
  const remis = jour.remis
  const encaisse = somme(remis.map((r) => r.sommeEncaissement))
  const nonRemis = remis.filter((r) => r.numeroRemis === 0)
  const sommeNonRemis = somme(nonRemis.map((r) => r.sommeEncaissement))
  // Les modes de la journee, dans l'ordre de Modes ; un mode inconnu de Modes vient a la suite, il n'est pas perdu.
  const modes = [...new Set([...Modes, ...remis.map((r) => r.modePaiement)])].filter((m) => remis.some((r) => r.modePaiement === m))
  const affiches = remis.filter((r) => filtre === null || (filtre === NonRemis ? r.numeroRemis === 0 : r.modePaiement === filtre))
  const montantMax = affiches.reduce((m, r) => Math.max(m, r.sommeEncaissement), 0)
  const basculerFiltre = (f: string) => () => setFiltre(filtre === f ? null : f)
  const parMode = (m: string) => somme(remis.filter((r) => r.modePaiement === m).map((r) => r.sommeEncaissement))
  const teinteDe = (m: string) => { const r = remis.find((x) => x.modePaiement === m); return r ? teinte(r) : 'gris' }

  return (
    <ListeModerne etiquette={`Journee du ${dateCourte(jour.date)}`}>
      <EnteteListe titre={`Journee du ${dateCourte(jour.date)}`}
                   synthese={<>{jour.nbFactures}{' facture(s) · '}<b>{montant(jour.montantTotalTTC / 100)}</b>{' TTC · '}
                     {nbEncaissements(jour)}{' encaissement(s) · '}<b>{montant(encaisse / 100)}</b>{' encaisses'}</>} />

      {/* Les factures de la journee (v2 : Classes/FactureJour), puis la repartition des encaissements. */}
      <CartesChiffrees cartes={[
        { cle: 'factures', titre: 'Factures de la journee', valeur: montant(jour.montantTotalTTC / 100),
          complement: `TTC · ${jour.nbFactures} facture(s) · ${jour.nbAffairesEnCours} affaire(s) en cours · ${jour.nbSansDemande} sans demande` },
        { cle: 'encaissements', titre: 'Encaissements par mode', valeur: montant(encaisse / 100), legende: true,
          ...(filtre !== null && filtre !== NonRemis ? { choisir: () => setFiltre(null), indication: 'Tous les modes' } : {}),
          segments: modes.map((m) => ({
            cle: m, libelle: m, valeur: parMode(m), texte: montant(parMode(m) / 100), teinte: teinteDe(m),
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
                <td title={r.observation ? `${r.modePaiement} — ${r.observation}` : r.modePaiement}>
                  <Pastille teinte={teinte(r)}>{r.modePaiement}</Pastille>
                  {r.observation && <div className="rig-eg-note">{r.observation}</div>}
                </td>
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
  )
}
