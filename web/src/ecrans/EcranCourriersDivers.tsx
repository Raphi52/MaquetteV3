// Ecran de la procedure PROC_COURRIER_DIVERS (RIG v2 : « Courrier divers »,
// OPE_RECHERCHE_COURRIER_DIVERS + OPE_EDITION_COURRIER_DIVERS). C'est le
// CATALOGUE des courriers divers, pas un courrier a envoyer. Colonnes lues
// dans OPE_RECHERCHE_COURRIER_DIVERS.cs : Code (COUJU_CODE), Libelle
// (COUJU_LIBELLE), Type entite (COUJU_TYPE_ENTITE), Parametre edition
// (COUJU_CODE_PRMED), Avec destinataire (COUJU_WITH_DEST), Actif
// (COUJU_ACTIF). Valeurs de type entite : eTypeEntiteJudiciaire
// (INSTANCE, INSTANCE_PREVENTION, PROCEDURE_COLLECTIVE, PREVENTION, MESURE).
//
// Porte depuis Components/Ecrans/EcranCourriersDivers.razor (lot 5) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : son
// catalogue de demonstration est ici, sans pont.

import { useState } from 'react'
import { contientSansCasse, estBlanc } from '../criteres'
import { PourLeDossier } from '../composants/PourLeDossier'
import type { ContexteDossierVue } from '../plan/types'

// v2 : table COURRIER_JUDICIAIRE (colonnes COUJU_*).
interface Courrier {
  code: string
  libelle: string
  typeEntite: string
  paramEdition: string
  avecDestinataire: boolean
  actif: boolean
}

const Types = ['INSTANCE', 'INSTANCE_PREVENTION', 'PROCEDURE_COLLECTIVE', 'PREVENTION', 'MESURE']

const Courriers: Courrier[] = [
  { code: 'CD_CONV', libelle: 'Convocation simple', typeEntite: 'INSTANCE', paramEdition: 'MAQ_CONV_SIMPLE', avecDestinataire: true, actif: true },
  { code: 'CD_RELAN', libelle: 'Relance de piece manquante', typeEntite: 'INSTANCE', paramEdition: 'MAQ_RELANCE', avecDestinataire: true, actif: true },
  { code: 'CD_NOTIF', libelle: 'Notification au mandataire', typeEntite: 'PROCEDURE_COLLECTIVE', paramEdition: 'MAQ_NOTIF_MAND', avecDestinataire: true, actif: true },
  { code: 'CD_INFO', libelle: 'Information sans destinataire', typeEntite: 'PREVENTION', paramEdition: 'MAQ_INFO', avecDestinataire: false, actif: true },
  { code: 'CD_MESURE', libelle: 'Courrier de suivi de mesure', typeEntite: 'MESURE', paramEdition: 'MAQ_SUIVI_MES', avecDestinataire: true, actif: false },
]

export interface ProprietesEcranCourriersDivers {
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) : le courrier est pour elle,
   *  deja choisie (bloc « Pour le dossier »). Absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranCourriersDivers({ dossier = null }: ProprietesEcranCourriersDivers) {
  const [recherche, setRecherche] = useState('')
  const [typeFiltre, setTypeFiltre] = useState('')
  const [actifsSeuls, setActifsSeuls] = useState(true)
  const [selection, setSelection] = useState<Courrier | null>(Courriers[0]!)

  let q = Courriers
  if (actifsSeuls) q = q.filter((c) => c.actif)
  if (typeFiltre !== '') q = q.filter((c) => c.typeEntite === typeFiltre)
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    q = q.filter((c) => contientSansCasse(c.code, t) || contientSansCasse(c.libelle, t))
  }
  const trouves = q

  const s = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        {dossier && <PourLeDossier dossier={dossier} />}

        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Courriers divers</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Code ou libelle</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type d'entite</span>
          <select className="rig-saisie-champ__saisie" value={typeFiltre} onChange={(e) => setTypeFiltre(e.target.value)}>
            <option value="">Tous</option>
            {Types.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Affichage</div>
          <button className={`rig-bascule${actifsSeuls ? ' rig-bascule--actif' : ''}`} onClick={() => setActifsSeuls(!actifsSeuls)}>
            <span className="rig-bascule__temoin"></span>
            <span>Actifs seulement</span>
          </button>
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton">Ajouter un courrier divers</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Catalogue</div>
          <span className="rig-resultats-proc__compte">{trouves.length} courrier(s)</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Code</th>
                <th>Libelle</th>
                <th>Type entite</th>
                <th>Parametre edition</th>
                <th className="rig-table__centre">Avec destinataire</th>
                <th className="rig-table__centre">Actif</th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((courrier) => (
                <tr key={courrier.code} onClick={() => setSelection(courrier)}>
                  <td className="rig-table__mono">{courrier.code}</td>
                  <td>{courrier.libelle}</td>
                  <td className="rig-table__mono">{courrier.typeEntite}</td>
                  <td className="rig-table__mono">{courrier.paramEdition}</td>
                  <td className="rig-table__centre">{courrier.avecDestinataire ? 'oui' : 'non'}</td>
                  <td className="rig-table__centre">{courrier.actif ? 'oui' : 'non'}</td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr><td colSpan={6} className="rig-table__vide">Aucun courrier divers ne repond a cette recherche.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {s && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-kicker">Modification du courrier divers {s.code}</div>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Code du courrier divers</span>
                <input className="rig-saisie-champ__saisie" value={s.code} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Libelle</span>
                <input className="rig-saisie-champ__saisie" value={s.libelle} readOnly />
              </label>
            </div>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Type entite judiciaire</span>
                <input className="rig-saisie-champ__saisie" value={s.typeEntite} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Parametre edition (maquette)</span>
                <input className="rig-saisie-champ__saisie" value={s.paramEdition} readOnly />
              </label>
            </div>
            <div className="rig-criteres__bloc">
              <span className="rig-chip">{s.avecDestinataire ? 'Avec destinataire' : 'Sans destinataire'}</span>
              <span className="rig-chip">{s.actif ? 'Actif' : 'Inactif'}</span>
            </div>
            <div className="rig-criteres__actions">
              <button className="rig-bouton rig-bouton--plat">Acceder au parametre edition</button>
            </div>
          </div>
        )}
      </section>
    </div>
  )
}
