// Ecran PROC_VERROUS : les dossiers actuellement OUVERTS PAR UN TIERS.
// Destination de l'alerte « dossier(s) ouvert(s) par un tiers » de l'accueil.
// Les lignes sont CALCULEES par Pont/EcranVerrous.cs sur les demandes connues
// (JeuDemo), jamais ecrites ici : une demande encore en cours portee par un
// autre login est un verrou.
//
// Porte depuis Components/Ecrans/EcranVerrous.razor (lot 1). La coquille .razor monte cet ecran et recoit ouvrirDossier.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 6) : la liste prend les briques de
// composants/ListeModerne.tsx -- tete (synthese, « Aller a un dossier… » Ctrl K qui filtre la liste affichee),
// societe en avatar + nom, objet et numero en gris, etat en point colore, pied. Cinq colonnes : pas de ligne
// depliee ; aucun critere d'etat ou de type : pas de vues. Le clic ouvre toujours l'affaire.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import type { ContexteDossierVue } from '../plan/types'
import { CelluleIdentite, EnteteListe, ListeModerne, PiedListe, PointEtat, normaliser, teinteStatut } from '../composants/ListeModerne'

export interface LigneVerrou {
  dossier: string
  nom: string
  statut: string
  etat: string
  utilisateur: string
  age: number
}

export interface DetenteurVerrous {
  utilisateur: string
  nombre: number
}

export interface VueVerrous {
  lignes: LigneVerrou[]
  detenteurs: DetenteurVerrous[]
}

export interface ProprietesEcranVerrous {
  /** Remonte le NUMERO clique : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

/** « Depot de bilan - TRANSNORD SA » : la societe (apres « - ») et l'objet (avant). */
export function societeEtObjet(nom: string): { societe: string; objet: string } {
  const tiret = nom.indexOf(' - ')
  return tiret < 0 ? { societe: nom, objet: '' } : { societe: nom.slice(tiret + 3), objet: nom.slice(0, tiret) }
}

export function EcranVerrous({ ouvrirDossier, dossier = null }: ProprietesEcranVerrous) {
  const [utilisateur, setUtilisateur] = useState('R. VILAIN')
  const [moiCompris, setMoiCompris] = useState(false)
  // Ouvert depuis une affaire : la liste se limite a elle (« qui la tient ? ») ;
  // la bascule, montree seulement alors, rend la liste entiere.
  const [surLeDossier, setSurLeDossier] = useState(dossier !== null)
  const filtre = surLeDossier ? dossier : null
  // null tant que le pont n'a pas repondu : on n'affiche alors ni ligne ni
  // « aucun dossier », qui serait faux pendant ce court instant.
  const [vue, setVue] = useState<VueVerrous | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueVerrous>('EcranVerrous.Verrouilles', utilisateur, moiCompris)
      .then((v) => { if (!perime) setVue(v) })
    // Une frappe rapide dans « Mon login » lance plusieurs appels : seule la
    // reponse du DERNIER etat compte, meme si une plus ancienne arrive apres.
    return () => { perime = true }
  }, [utilisateur, moiCompris])

  const tous = vue?.lignes ?? []
  const verrouilles = filtre ? tous.filter((v) => v.dossier === filtre.numero) : tous

  // « Aller a un dossier… » (Ctrl K) : filtre la liste AFFICHEE, sans redemander au pont.
  const [rapide, setRapide] = useState('')
  const cherche = normaliser(rapide.trim())
  const affiches = cherche === '' ? verrouilles
    : verrouilles.filter((v) => normaliser(`${v.dossier} ${v.nom} ${v.utilisateur}`).includes(cherche))
  const detenteurs = new Set(affiches.map((v) => v.utilisateur)).size
  const plusAncien = affiches.reduce((m, v) => (v.age > m ? v.age : m), 0)

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Verrous</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Mon login</span>
          <input className="rig-saisie-champ__saisie" value={utilisateur}
                 onChange={(e) => setUtilisateur(e.target.value)} />
        </label>

        <button className={`rig-bascule${moiCompris ? ' rig-bascule--actif' : ''}`}
                onClick={() => setMoiCompris(!moiCompris)}>
          <span className="rig-bascule__temoin"></span>
          <span>Inclure mes propres dossiers</span>
        </button>

        {dossier && (
          <button className={`rig-bascule${surLeDossier ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setSurLeDossier(!surLeDossier)}>
            <span className="rig-bascule__temoin"></span>
            <span>Dossier {dossier.numero} seulement</span>
          </button>
        )}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Detenteurs</div>
          {(vue?.detenteurs ?? []).map((d) => (
            <div className="rig-alerte" key={d.utilisateur}>
              <span className="rig-filet-v rig-filet-v--attente"></span>
              <span className="rig-alerte__corps">
                <span className="rig-alerte__titre">{d.utilisateur}</span><br />
                <span className="rig-alerte__detail">{d.nombre} dossier(s)</span>
              </span>
            </div>
          ))}
        </div>
      </aside>

      <ListeModerne etiquette="Dossiers verrouilles">
        <EnteteListe titre="Dossiers verrouilles"
                     synthese={vue && <>{affiches.length}{' dossier(s)'}{affiches.length > 0 && <>{' · '}<b>{detenteurs}</b>{' detenteur(s) · plus ancien '}<b>{plusAncien} j</b></>}</>}
                     filtre={{ valeur: rapide, changer: setRapide, indication: 'Aller a un dossier…',
                               valider: () => { if (affiches.length === 1) ouvrirDossier(affiches[0]!.dossier) } }} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col /><col style={{ width: '20%' }} /><col style={{ width: '22%' }} /><col style={{ width: '12%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>Dossier</th>
                <th>Etat</th>
                <th>Detenu par</th>
                <th className="rig-lm-droite">Depuis</th>
              </tr>
            </thead>
            <tbody>
              {/* Cle par rang : un dossier peut porter plusieurs demandes. */}
              {affiches.map((v, i) => {
                const { societe, objet } = societeEtObjet(v.nom)
                return (
                  <tr key={`${i}:${v.dossier}`} onClick={() => ouvrirDossier(v.dossier)}
                      title={`Ouvrir l'affaire ${v.dossier}`}>
                    <CelluleIdentite nom={societe} titre={`${v.nom} · ${v.dossier}`}
                                     objet={<>{objet}{objet !== '' ? ' · ' : ''}<span>{v.dossier}</span></>} />
                    <td title={v.etat}><PointEtat teinte={teinteStatut(v.statut)}>{v.etat}</PointEtat></td>
                    <td title={v.utilisateur}>{v.utilisateur}</td>
                    <td className="rig-lm-droite rig-table__mono">{v.age} j</td>
                  </tr>
                )
              })}
              {vue && verrouilles.length === 0 && (
                <tr><td colSpan={4} className="rig-table__vide">
                  {filtre ? `Le dossier ${filtre.numero} n'est retenu par aucun autre poste.` : "Aucun dossier n'est retenu par un autre poste."}
                </td></tr>
              )}
              {verrouilles.length > 0 && affiches.length === 0 && (
                <tr><td colSpan={4} className="rig-table__vide">{`Aucun des ${verrouilles.length} dossiers ne contient « ${rapide.trim()} ».`}</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe aide="Clic sur une ligne pour ouvrir l'affaire.">
          <span><b>{affiches.length}</b>{' dossier(s) affiche(s)'}</span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
