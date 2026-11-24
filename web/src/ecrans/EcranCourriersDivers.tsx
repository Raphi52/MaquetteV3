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
//
// LISTE MODERNE (24/11/2026, plan des listes modernes, lot 5) : le catalogue prend les briques de
// composants/ListeModerne.tsx -- tete (synthese, « Aller a un courrier… » Ctrl K qui filtre la liste affichee,
// Entree choisit le courrier reste seul), vues = les criteres existants (actifs seulement ou non, type d'entite),
// type d'entite en pastille, actif en point colore, pied. Les criteres, le clic sur une ligne et la fiche du
// courrier choisi ne changent pas.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'
import { EnteteListe, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe, type Teinte } from '../composants/ListeModerne'

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

/** La teinte d'un type d'entite : celle de la nature d'affaire correspondante (Recherche d'affaires). */
const TeinteType: Record<string, Teinte> = {
  INSTANCE: 'bleu', INSTANCE_PREVENTION: 'violet', PROCEDURE_COLLECTIVE: 'orange', PREVENTION: 'ardoise', MESURE: 'vert',
}

/** « PROCEDURE_COLLECTIVE » -> « Procedure collective » : le libelle d'une vue. */
function nomDuType(type: string): string {
  return type.charAt(0) + type.slice(1).toLowerCase().replace(/_/g, ' ')
}

/** Une vue : un jeu des deux criteres d'etat et de type de l'ecran. */
interface VueCatalogue {
  cle: string
  libelle: string
  actifsSeuls: boolean
  type: string
}

export function EcranCourriersDivers() {
  const [recherche, setRecherche] = useState('')
  const [typeFiltre, setTypeFiltre] = useState('')
  const [actifsSeuls, setActifsSeuls] = useState(true)
  // null tant que le pont n'a pas repondu : ni ligne ni « aucun courrier », qui serait faux.
  const [catalogue, setCatalogue] = useState<CatalogueCourriersVue | null>(null)
  // Le code du modele clique ; null = le premier du catalogue, comme a l'ouverture.
  const [selection, setSelection] = useState<string | null>(null)
  // « Aller a un courrier… » (Ctrl K) : filtre la liste AFFICHEE, sans toucher aux criteres.
  const [filtreRapide, setFiltreRapide] = useState('')

  useEffect(() => {
    let perime = false
    void appeler<CatalogueCourriersVue>('EcranCourriersDivers.Catalogue').then((c) => { if (!perime) setCatalogue(c) })
    return () => { perime = true }
  }, [])

  const courriers = catalogue?.courriers ?? []
  /** Les courriers que montrent ces criteres (la recherche saisie comprise). */
  function trouver(actifs: boolean, type: string): CourrierDiversVue[] {
    let q = courriers
    if (actifs) q = q.filter((c) => c.actif)
    if (type !== '') q = q.filter((c) => c.typeEntite === type)
    if (!estBlanc(recherche)) {
      const t = recherche.trim()
      q = q.filter((c) => contientSansCasse(c.code, t) || contientSansCasse(c.libelle, t))
    }
    return q
  }
  const trouves = trouver(actifsSeuls, typeFiltre)
  const affiches = estBlanc(filtreRapide) ? trouves
    : trouves.filter((c) => contientSansCasse(`${c.code} ${c.libelle}`, filtreRapide.trim()))

  const s = courriers.find((c) => c.code === selection) ?? courriers[0] ?? null

  const vues: VueCatalogue[] = [
    { cle: 'actifs', libelle: 'Actifs', actifsSeuls: true, type: '' },
    { cle: 'tous', libelle: 'Tous', actifsSeuls: false, type: '' },
    ...(catalogue?.types ?? []).map((t) => ({ cle: t, libelle: nomDuType(t), actifsSeuls: true, type: t })),
  ]
  const vueActive = vues.find((v) => v.actifsSeuls === actifsSeuls && v.type === typeFiltre)?.cle ?? null
  const avecDestinataire = affiches.filter((c) => c.avecDestinataire).length
  const inactifs = affiches.filter((c) => !c.actif).length

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

      <ListeModerne etiquette="Catalogue">
        <EnteteListe titre="Catalogue"
                     synthese={catalogue ? <>{affiches.length}{affiches.length > 1 ? ' courriers' : ' courrier'}
                       {' · '}<b>{avecDestinataire}</b>{' avec destinataire'}
                       {inactifs > 0 && <>{' · '}<b>{inactifs}</b>{inactifs > 1 ? ' inactifs' : ' inactif'}</>}</> : undefined}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a un courrier…',
                               valider: () => { if (affiches.length === 1) setSelection(affiches[0]!.code) } }} />

        {catalogue && (
          <VuesListe vues={vues.map((v) => ({ cle: v.cle, libelle: v.libelle, compte: trouver(v.actifsSeuls, v.type).length }))}
                     active={vueActive}
                     choisir={(cle) => {
                       const v = vues.find((x) => x.cle === cle)
                       if (v) { setActifsSeuls(v.actifsSeuls); setTypeFiltre(v.type) }
                     }} />
        )}

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: '15%' }} /><col /><col style={{ width: '20%' }} /><col style={{ width: '17%' }} />
              <col style={{ width: '13%' }} /><col style={{ width: '10%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>Code</th>
                <th>Libelle</th>
                <th>Type entite</th>
                <th>Parametre edition</th>
                <th className="rig-table__centre">Avec destinataire</th>
                <th>Etat</th>
              </tr>
            </thead>
            <tbody>
              {affiches.map((courrier) => (
                <tr key={courrier.code} onClick={() => setSelection(courrier.code)}>
                  <td className="rig-table__mono">{courrier.code}</td>
                  <td title={courrier.libelle}>{courrier.libelle}</td>
                  <td title={courrier.typeEntite}>
                    <Pastille teinte={TeinteType[courrier.typeEntite] ?? 'gris'}>{nomDuType(courrier.typeEntite)}</Pastille>
                  </td>
                  <td className="rig-table__mono">{courrier.paramEdition}</td>
                  <td className="rig-table__centre">{courrier.avecDestinataire ? 'oui' : 'non'}</td>
                  <td><PointEtat teinte={courrier.actif ? 'vert' : 'gris'}>{courrier.actif ? 'Actif' : 'Inactif'}</PointEtat></td>
                </tr>
              ))}
              {catalogue && trouves.length === 0 && (
                <tr><td colSpan={6} className="rig-table__vide">Aucun courrier divers ne repond a cette recherche.</td></tr>
              )}
              {trouves.length > 0 && affiches.length === 0 && (
                <tr><td colSpan={6} className="rig-table__vide">
                  {`Aucun des ${trouves.length} courriers ne contient « ${filtreRapide.trim()} ».`}
                </td></tr>
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

        {catalogue && (
          <PiedListe>
            <span><b>{affiches.length}</b>{affiches.length > 1 ? ' courriers affiches' : ' courrier affiche'}</span>
          </PiedListe>
        )}
      </ListeModerne>
    </div>
  )
}
