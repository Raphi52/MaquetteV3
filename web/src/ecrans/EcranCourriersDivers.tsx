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
// balisage, memes classes CSS. La liste des modeles vient du C#
// (Pont/EcranCourriersDivers.cs, Metier/CourriersDivers.cs) : c'est la MEME que
// celle d'ou la page du dossier tire les courriers qu'elle ecrit pour une
// affaire (plan « Vue du dossier », lot 9). Gardee ici, elle aurait derive.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'

// v2 : table COURRIER_JUDICIAIRE (colonnes COUJU_*).
export interface CourrierDiversVue {
  code: string
  libelle: string
  typeEntite: string
  paramEdition: string
  avecDestinataire: boolean
  actif: boolean
}

/** Ce que Pont/EcranCourriersDivers.cs (Catalogue) rend : les types d'entite du filtre et tous les modeles. */
export interface CatalogueCourriersVue {
  types: string[]
  courriers: CourrierDiversVue[]
}

export function EcranCourriersDivers() {
  const [recherche, setRecherche] = useState('')
  const [typeFiltre, setTypeFiltre] = useState('')
  const [actifsSeuls, setActifsSeuls] = useState(true)
  // null tant que le pont n'a pas repondu : ni ligne ni « aucun courrier », qui serait faux.
  const [catalogue, setCatalogue] = useState<CatalogueCourriersVue | null>(null)
  // Le code du modele clique ; null = le premier du catalogue, comme a l'ouverture.
  const [selection, setSelection] = useState<string | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<CatalogueCourriersVue>('EcranCourriersDivers.Catalogue').then((c) => { if (!perime) setCatalogue(c) })
    return () => { perime = true }
  }, [])

  const courriers = catalogue?.courriers ?? []
  let q = courriers
  if (actifsSeuls) q = q.filter((c) => c.actif)
  if (typeFiltre !== '') q = q.filter((c) => c.typeEntite === typeFiltre)
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    q = q.filter((c) => contientSansCasse(c.code, t) || contientSansCasse(c.libelle, t))
  }
  const trouves = q

  const s = courriers.find((c) => c.code === selection) ?? courriers[0] ?? null

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Courriers divers</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Code ou libelle</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type d'entite</span>
          <select className="rig-saisie-champ__saisie" value={typeFiltre} onChange={(e) => setTypeFiltre(e.target.value)}>
            <option value="">Tous</option>
            {(catalogue?.types ?? []).map((t) => <option key={t} value={t}>{t}</option>)}
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
          <span className="rig-resultats-proc__compte">{catalogue ? `${trouves.length} courrier(s)` : ''}</span>
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
                <tr key={courrier.code} onClick={() => setSelection(courrier.code)}>
                  <td className="rig-table__mono">{courrier.code}</td>
                  <td>{courrier.libelle}</td>
                  <td className="rig-table__mono">{courrier.typeEntite}</td>
                  <td className="rig-table__mono">{courrier.paramEdition}</td>
                  <td className="rig-table__centre">{courrier.avecDestinataire ? 'oui' : 'non'}</td>
                  <td className="rig-table__centre">{courrier.actif ? 'oui' : 'non'}</td>
                </tr>
              ))}
              {catalogue && trouves.length === 0 && (
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
