// Ecran de la procedure PROC_REDA — redaction d'une decision judiciaire.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_REDA) :
//   - ETP_REDA/OPE_DECISION_JUDICIAIRE.Designer.cs : tout le formulaire.
//     Listes deroulantes : Type de decision (ultCBTypeDecision), Sens
//     general (ultCBSensGeneral), Ressort (ultCBRessort).
//     Dates : Date (ultDJDate), Date prevue (ultDJDatePrevue), « Mise en
//     delibere le » et « Prononce le ».
//     Cases a cocher : Avant dire droit (ultCBAvantDireDroit),
//     Contradictoire (ultCBContradictoire), Execution provisoire
//     (ultCBExecutionProvisoire), Debats publics, Decision publique,
//     Mesure d'instruction.
//     Boutons de la barre : « Rechercher Delibere » (ultBtnDelibere),
//     « Rechercher Prononce » (ultBtnPrononce), « Gestion Document GED »
//     (OperationGestionDoc).
//     Les deux messages d'absence de resultat sont repris tels quels :
//     « Aucun appel d'affaire delibere » / « Aucun appel d'affaire
//     prononce ».
//   - Le compteur « Decision judiciaire liee a N documents » vient du meme
//     fichier : c'est la GED rattachee, pas un decompte d'affaires.
//
// Porte depuis Components/Ecrans/EcranRedaction.razor (lot 5) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses decisions de
// demonstration sont ici, sans pont.

import { useState } from 'react'

interface Decision {
  numero: string
  affaire: string
  delibere: boolean
  typeDecision: string
  sensGeneral: string
  ressort: string
  date: string
  datePrevue: string
  miseEnDelibere: string
  prononce: string
  avantDireDroit: boolean
  contradictoire: boolean
  executionProvisoire: boolean
  debatsPublics: boolean
  decisionPublique: boolean
  mesureInstruction: boolean
  documents: number
  mesures: string[]
}

const Appels: Decision[] = [
  {
    numero: '2026J00121', affaire: 'AMITEL SAS - ouverture', delibere: true,
    typeDecision: 'Jugement d\'ouverture', sensGeneral: 'Favorable', ressort: 'Premier ressort',
    date: '06/03/2026', datePrevue: '05/03/2026', miseEnDelibere: '20/02/2026', prononce: '',
    avantDireDroit: false, contradictoire: true, executionProvisoire: true, debatsPublics: true,
    decisionPublique: true, mesureInstruction: true, documents: 4,
    mesures: ['MES-2026-0114 — expertise comptable', 'MES-2026-0121 — inventaire'],
  },
  {
    numero: '2026J00134', affaire: 'SCI DU PARC - designation', delibere: true,
    typeDecision: 'Ordonnance de designation', sensGeneral: 'Favorable', ressort: 'Premier ressort',
    date: '02/06/2026', datePrevue: '01/06/2026', miseEnDelibere: '18/05/2026', prononce: '',
    avantDireDroit: true, contradictoire: true, executionProvisoire: false, debatsPublics: false,
    decisionPublique: true, mesureInstruction: true, documents: 2,
    mesures: ['MES-2026-0128 — evaluation immobiliere'],
  },
  {
    numero: '2025J00088', affaire: 'MARTIN EURL - cloture', delibere: false,
    typeDecision: 'Jugement de cloture', sensGeneral: 'Defavorable', ressort: 'Dernier ressort',
    date: '20/09/2026', datePrevue: '20/09/2026', miseEnDelibere: '04/09/2026', prononce: '20/09/2026',
    avantDireDroit: false, contradictoire: true, executionProvisoire: false, debatsPublics: true,
    decisionPublique: true, mesureInstruction: false, documents: 0,
    mesures: [],
  },
  {
    numero: '2024J00507', affaire: 'DUPONT SARL - copie conforme', delibere: false,
    typeDecision: 'Copie conforme', sensGeneral: 'Sans objet', ressort: 'Premier ressort',
    date: '08/11/2024', datePrevue: '08/11/2024', miseEnDelibere: '', prononce: '08/11/2024',
    avantDireDroit: false, contradictoire: false, executionProvisoire: false, debatsPublics: false,
    decisionPublique: false, mesureInstruction: false, documents: 1,
    mesures: [],
  },
]

const ou = (valeur: string): string => (valeur === '' ? '—' : valeur)

const caracteres = (d: Decision): Array<[string, boolean]> => [
  ['Avant dire droit', d.avantDireDroit],
  ['Contradictoire', d.contradictoire],
  ['Execution provisoire', d.executionProvisoire],
  ['Debats publics', d.debatsPublics],
  ['Decision publique', d.decisionPublique],
  ['Mesure d\'instruction', d.mesureInstruction],
]

export function EcranRedaction() {
  const [surDelibere, setSurDelibere] = useState(true)
  const [selection, setSelection] = useState<Decision | null>(Appels[0]!)

  const appels = Appels.filter((a) => a.delibere === surDelibere)
  const d = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Appels d'affaires</div>

        <div className="rig-onglets">
          <button className={`rig-onglet${surDelibere ? ' rig-onglet--actif' : ''}`}
                  onClick={() => setSurDelibere(true)}>Delibere</button>
          <button className={`rig-onglet${!surDelibere ? ' rig-onglet--actif' : ''}`}
                  onClick={() => setSurDelibere(false)}>Prononce</button>
        </div>

        <div className="rig-criteres__bloc">
          {appels.map((ap) => (
            <button key={ap.numero} className={`rig-bascule${selection?.numero === ap.numero ? ' rig-bascule--actif' : ''}`}
                    onClick={() => setSelection(ap)}>
              <span className="rig-bascule__temoin"></span>
              <span>{ap.numero} — {ap.affaire}</span>
            </button>
          ))}
          {appels.length === 0 && (
            // Messages repris mot pour mot de la v2.
            <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-3)' }}>
              {surDelibere ? 'Aucun appel d\'affaire delibere' : 'Aucun appel d\'affaire prononce'}
            </div>
          )}
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Decision judiciaire</div>
          <span className="rig-resultats-proc__compte">
            {d ? `Decision judiciaire liee a ${d.documents} documents` : 'aucune decision selectionnee'}
          </span>
        </div>

        {d ? (
          <>
            <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Type de decision</span>
                  <input className="rig-saisie-champ__saisie" value={d.typeDecision} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Sens general</span>
                  <input className="rig-saisie-champ__saisie" value={d.sensGeneral} readOnly />
                </label>
              </div>

              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Ressort</span>
                <input className="rig-saisie-champ__saisie" value={d.ressort} readOnly />
              </label>

              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Date</span>
                  <input className="rig-saisie-champ__saisie" value={d.date} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Date prevue</span>
                  <input className="rig-saisie-champ__saisie" value={d.datePrevue} readOnly />
                </label>
              </div>

              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Mise en delibere le</span>
                  <input className="rig-saisie-champ__saisie" value={ou(d.miseEnDelibere)} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Prononce le</span>
                  <input className="rig-saisie-champ__saisie" value={ou(d.prononce)} readOnly />
                </label>
              </div>
            </div>

            <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
              <div className="rig-kicker">Caracteres de la decision</div>
              {/* Le bloc parent est une colonne flexible : sans ce conteneur,
                  chaque pastille serait etiree sur toute la largeur. */}
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--rig-esp-2)' }}>
                {caracteres(d).map(([libelle, oui]) => (
                  <span key={libelle} className={`rig-etat rig-etat--${oui ? 'ok' : 'attente'}`}>{libelle}</span>
                ))}
              </div>
            </div>

            <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
              <div className="rig-kicker">Mesures d'instruction liees</div>
              {d.mesures.length === 0
                ? <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-3)' }}>Aucune mesure liee a cette decision.</div>
                : d.mesures.map((m) => <div key={m} className="rig-table__mono">{m}</div>)}
            </div>
          </>
        ) : (
          <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>
            Choisissez un appel d'affaire dans la liste de gauche.
          </div>
        )}
      </section>
    </div>
  )
}
