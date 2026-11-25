// TRAITEMENT EN LOT : la MEME recette appliquee a PLUSIEURS affaires.
//
// Au greffe, un evenement recurrent n'arrive pas dossier par dossier : quarante
// denominations a reprendre font QUARANTE alertes et UNE seule operation. Ici,
// une ligne par dossier, les champs de la recette en colonnes, et une cloture
// PAR DOSSIER -- jamais une cloture globale, qui fermerait des dossiers
// incomplets avec les autres. Le lot n'est propose QUE pour les recettes SANS
// repetition (RecetteTache.ARepetition).
//
// Origine : Sans origine RIG v2. L'ancienne application traitait les taches
// UNE PAR UNE, dossier par dossier ; aucun ecran v2 ne portait le lot. Il n'y a
// donc pas de procedure PROC_ a citer ici.
//
// Porte depuis Components/Ecrans/EcranTachesLot.razor (lot 3) : meme balisage,
// memes classes CSS. Les regles (lots, choix d'ouverture, saisie dans la trace
// DU dossier, cloture par dossier) vivent dans Pont/EcranTachesLot.cs, dans une
// session que la COQUILLE ouvre avec l'etat vivant de l'accueil : React recoit
// son identifiant (session) et « version », qui change quand la coquille
// rouvre l'ecran sur une autre recette.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 6) : la grille du lot prend les briques de
// composants/ListeModerne.tsx -- tete (titre de la recette, synthese, « Aller a un dossier… » Ctrl K qui filtre les
// lignes affichees : un lot compte parfois quarante dossiers), societe en avatar + nom et numero en gris, etat du
// dossier en point colore (ce que disait seulement l'infobulle de la ligne), pied. Les champs de la recette RESTENT en
// colonnes, meme au-dela de sept : c'est la saisie en lot elle-meme, les replier dans une ligne depliee la casserait.
// Aucun critere d'etat ou de type : pas de vues. Saisie, propagation et cloture par dossier ne changent pas.
// fix-ok: application du socle des listes en plusieurs editions (lot 6), pas un correctif a l'aveugle.

import { useCallback, useEffect, useRef, useState } from 'react'
import { appeler } from '../pont'
import { Saisie } from '../session'
import { CelluleIdentite, EnteteListe, ListeModerne, PiedListe, PointEtat, normaliser } from '../composants/ListeModerne'

export interface LotVue {
  code: string
  titre: string
  dossiers: number
  aRepetition: boolean
  actif: boolean
}

export interface ChampLotVue {
  libelle: string
  /** La colonne REELLE de la base (prefixe compris), montree en infobulle. */
  colonne: string
}

export interface RecetteLotVue {
  code: string
  titre: string
  /** Les codes evenement, joints par « / ». */
  evenements: string
  aRepetition: boolean
  /** « dirigeant » ou « ligne » : le refus de la saisie en lot le nomme. */
  libelleLigne: string
  champs: ChampLotVue[]
  /** Nombre de publications obligatoires de la recette. */
  effets: number
}

export interface DossierLotVue {
  dossier: string
  nom: string
  /** La derniere valeur saisie de chaque champ, dans l'ordre des colonnes. */
  valeurs: string[]
  restants: number
  effetsRestants: number
  pret: boolean
}

export interface VueTachesLot {
  session: string
  lots: LotVue[]
  recette: RecetteLotVue | null
  dossiers: DossierLotVue[]
  dernierMessage: string
}

export interface ProprietesEcranTachesLot {
  /** La session ouverte par la coquille (Pont/EcranTachesLot.cs). */
  session: string
  /** Change quand un nouveau parametre de la coquille a change la recette affichee. */
  version: number
}

/**
 * La vue de la session, relue a chaque changement de session ou de version.
 * Seule la reponse du DERNIER appel est appliquee (meme regle que useSession).
 */
function useVueDeLot(session: string, version: number): [VueTachesLot | null, (geste: string, valeur?: unknown) => void] {
  const [vue, setVue] = useState<VueTachesLot | null>(null)
  const dernier = useRef(0)

  useEffect(() => {
    const numero = ++dernier.current
    appeler<VueTachesLot>('EcranTachesLot.Vue', session).then((v) => {
      if (numero === dernier.current) setVue(v)
    }, (erreur: unknown) => console.error('[pont] appel en echec', erreur))
  }, [session, version])

  const geste = useCallback((nom: string, valeur?: unknown) => {
    const numero = ++dernier.current
    appeler<VueTachesLot>('EcranTachesLot.Agir', session, nom, valeur ?? null).then((v) => {
      if (numero === dernier.current) setVue(v)
    }, (erreur: unknown) => console.error('[pont] appel en echec', erreur))
  }, [session])

  return [vue, geste]
}

export function EcranTachesLot({ session, version }: ProprietesEcranTachesLot) {
  const [vue, geste] = useVueDeLot(session, version)
  // « Aller a un dossier… » (Ctrl K) : filtre les lignes AFFICHEES, sans rien demander a la session. Il ne vaut que
  // pour la recette ou il a ete tape : une autre tache (clic a gauche, ou la coquille) repart sans filtre.
  const [filtreDe, setFiltreDe] = useState({ recette: '', texte: '' })
  if (vue === null) return null

  const recette = vue.recette
  const rapide = filtreDe.recette === (recette?.code ?? '') ? filtreDe.texte : ''
  const setRapide = (texte: string) => setFiltreDe({ recette: recette?.code ?? '', texte })
  const cherche = normaliser(rapide.trim())
  const affiches = cherche === '' ? vue.dossiers : vue.dossiers.filter((d) => normaliser(`${d.dossier} ${d.nom}`).includes(cherche))
  const prets = affiches.filter((d) => d.pret).length
  // Societe, champs de la recette, etat, propagation, cloture.
  const nbColonnes = (recette?.champs.length ?? 0) + 4

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Taches en attente</div>

        {vue.lots.map((lot) => (
          <button key={lot.code}
                  className={`rig-alerte rig-alerte--lien ${lot.actif ? 'rig-alerte--actif' : ''}`}
                  type="button"
                  title={lot.aRepetition
                    ? 'Cette tache se saisit dossier par dossier'
                    : `Traiter les ${lot.dossiers} dossiers en lot`}
                  onClick={() => geste('Choisir', lot.code)}>
            <span className="rig-filet-v rig-filet-v--action"></span>
            <span className="rig-alerte__corps">
              <span className="rig-alerte__titre">{lot.titre}</span><br />
              <span className="rig-alerte__detail">
                {/* Memes morceaux de texte que Razor : le nombre, puis « dossier(s) »
                    qui garde le blanc d'avant le @if, puis la puce sans blanc. */}
                {lot.dossiers}{' dossier(s) '}{lot.aRepetition && '· saisie ligne par ligne'}
              </span>
            </span>
          </button>
        ))}
        {vue.lots.length === 0 && (
          <div className="rig-vide">Aucune tache en attente.</div>
        )}
      </aside>

      <ListeModerne etiquette="Traitement en lot">
        <EnteteListe titre={recette?.titre ?? 'Traitement en lot'}
                     synthese={<>
                       {recette !== null && <>{recette.evenements}{' · '}</>}
                       {affiches.length}{' dossier(s)'}
                       {prets > 0 && <>{' · '}<b>{prets}</b>{' pret(s) a terminer'}</>}
                     </>}
                     filtre={recette !== null && !recette.aRepetition
                       ? { valeur: rapide, changer: setRapide, indication: 'Aller a un dossier…' } : undefined} />

        {recette === null ? (
          <div className="rig-vide">Choisir une tache a gauche pour traiter ses dossiers ensemble.</div>
        ) : recette.aRepetition ? (
          // Refus EXPLIQUE, pas un tableau vide : cette tache porte plusieurs
          // lignes par dossier, elle se traite une par une.
          <div className="rig-vide">
            {'« '}{recette.titre}{' » se saisit '}<b>ligne par ligne</b>
            {' (plusieurs '}{recette.libelleLigne}{"s par dossier). Ouvrir chaque affaire depuis la file d'alertes."}
          </div>
        ) : (
          <>
            <div className="rig-table-cadre">
              <table className="rig-table rig-lm-table">
                <colgroup>
                  <col style={{ width: '24%' }} />
                  {recette.champs.map((_, j) => <col key={j} />)}
                  <col style={{ width: '120px' }} /><col style={{ width: '104px' }} /><col style={{ width: '104px' }} />
                </colgroup>
                <thead>
                  <tr>
                    <th>Dossier</th>
                    {recette.champs.map((c, j) => (
                      <th key={j} title={c.colonne}>{c.libelle}</th>
                    ))}
                    <th>Etat</th>
                    <th>Propagation</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {affiches.map((d) => (
                    <tr key={d.dossier}
                        title={d.pret ? 'Pret a terminer' : `${d.restants} champ(s) et ${d.effetsRestants} publication(s) a traiter`}>
                      <CelluleIdentite nom={d.nom} objet={<span>{d.dossier}</span>} titre={`${d.nom} · ${d.dossier}`} />
                      {recette.champs.map((_, j) => (
                        <td key={j}>
                          <Saisie className="rig-saisie-champ__saisie" quand="change"
                                  valeur={d.valeurs[j] ?? ''}
                                  changer={(v) => geste('Ecrire', { dossier: d.dossier, champ: j, valeur: v })} />
                        </td>
                      ))}
                      <td>
                        {d.pret ? <PointEtat teinte="vert">Pret</PointEtat>
                          : <PointEtat teinte="orange">{d.restants + d.effetsRestants}{' a traiter'}</PointEtat>}
                      </td>
                      <td className="rig-table__mono">
                        {recette.effets - d.effetsRestants}{' / '}{recette.effets}{' '}
                        <button className="rig-bouton rig-bouton--plat"
                                title="Marquer toutes les publications de ce dossier comme traitees"
                                onClick={() => geste('ToutPropager', d.dossier)}>tout</button>
                      </td>
                      <td>
                        <button className="rig-bouton rig-tache__terminer" disabled={!d.pret}
                                title={d.pret
                                  ? 'Terminer la tache sur ce dossier'
                                  : 'Renseigner les champs et les publications d\'abord'}
                                onClick={() => geste('Terminer', d.dossier)}>Terminer</button>
                      </td>
                    </tr>
                  ))}
                  {vue.dossiers.length === 0 && (
                    <tr><td colSpan={nbColonnes} className="rig-table__vide">Plus aucun dossier en attente pour cette tache.</td></tr>
                  )}
                  {vue.dossiers.length > 0 && affiches.length === 0 && (
                    <tr><td colSpan={nbColonnes} className="rig-table__vide">
                      {`Aucun des ${vue.dossiers.length} dossiers ne contient « ${rapide.trim()} ».`}
                    </td></tr>
                  )}
                </tbody>
              </table>
            </div>
            {vue.dernierMessage.length > 0 && (
              <div className="rig-fiche__note">{vue.dernierMessage}</div>
            )}
            <PiedListe aide="Terminer clot la tache sur ce seul dossier, quand ses champs et ses publications sont faits.">
              <span><b>{affiches.length}</b>{' dossier(s) affiche(s)'}</span>
              <span>Prets <b>{prets}</b></span>
            </PiedListe>
          </>
        )}
      </ListeModerne>
    </div>
  )
}
