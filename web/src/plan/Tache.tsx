// LE MODE TACHE de la visionneuse (Components/Pages/Home.razor, porte au lot 2
// du plan « finir le passage a React », 19/11/2026). Une alerte n'est pas une
// ligne a lire : c'est l'INSTANCE d'une recette (Metier/RecetteTache.cs). Tant
// qu'une tache est ouverte, l'arbre se replie sur ce que la recette declare ;
// « Afficher tout le dossier » remet la vue entiere. Les comptes, la
// completude et ce qui coche une ligne sont calcules par le C#
// (SessionPlanTravail.VueTache) : la preuve est la trace du dossier ou la
// piece deposee dans l'arbre, jamais un clic.

import type { GestePlan, TacheVue } from './types'

/** Case desactivee : aucun geste (le .razor n'en branchait pas), sans attribut de plus dans le DOM. */
function rien(): void { /* la coche vient de la trace du dossier */ }

/** Le bandeau au-dessus de l'arbre, et les lignes d'une tache repetable. */
export function BandeauTache({ tache, plan }: { tache: TacheVue, plan: GestePlan }) {
  return (
    <>
      <div className="rig-tache__bandeau">
        <span className="rig-tache__code">{tache.code}</span>{' '}
        <span className="rig-tache__titre">{tache.titre}</span>{' '}
        <button className="rig-bouton rig-bouton--plat rig-tache__tout"
                aria-pressed={tache.toutLeDossier}
                title="Afficher tout le dossier sans quitter la tache"
                onClick={() => plan('basculerTout')}>{tache.texteTout}</button>
        {/* CONTROLE DE COMPLETUDE : la tache ne se termine que lorsque TOUS ses
            champs sont renseignes. Le compte porte sur les CASES exigees : un
            champ repetable compte une fois PAR LIGNE. */}
        <span className={'rig-tache__etat ' + (tache.complete ? 'rig-tache__etat--ok' : '')}>
          {tache.faits}{' / '}{tache.exiges}{' champs renseignes\n'}
          {tache.facultatifs > 0 && <span className="rig-tache__reste">{'+ '}{tache.facultatifs}{' facultatif(s)'}</span>}
          {tache.reste !== null && <span className="rig-tache__reste">{'reste : '}{tache.reste}</span>}
        </span>{' '}
        <button className="rig-bouton rig-tache__terminer" disabled={!tache.peutClore}
                title={tache.raison}
                onClick={() => plan('terminerTache')}>Terminer la tache</button>{' '}
        <button className="rig-bouton rig-bouton--plat" title="Fermer la tache sans la terminer"
                onClick={() => plan('fermerTache')}>Fermer la tache</button>
      </div>
      {/* PLUSIEURS LIGNES : trois dirigeants changes le meme jour font UNE
          tache, pas trois. Chaque ligne a ses propres champs, sa propre trace,
          et compte dans la completude. */}
      {tache.lignes && (
        <div className="rig-tache__lignes">
          {tache.lignes.lignes.map((l) => (
            <button key={l.rang}
                    className={'rig-tache__ligne ' + (l.courante ? 'rig-tache__ligne--courante' : '') + ' ' + (l.trous === 0 ? 'rig-tache__ligne--ok' : '')}
                    title={l.trous === 0 ? 'Ligne complete' : `${l.trous} champ(s) a renseigner`}
                    onClick={() => plan('ligneTache', l.rang)}>
              {tache.lignes!.libelle}{' '}{l.rang}
            </button>
          ))}
          <button className="rig-tache__ligne rig-tache__ligne--ajout"
                  title={tache.lignes.titreAjout}
                  onClick={() => plan('ajouterLigne')}>+</button>
          {tache.lignes.retrait && (
            <button className="rig-tache__ligne rig-tache__ligne--ajout"
                    title="Retirer la derniere ligne (ce qui y a ete ecrit reste dans la trace)"
                    onClick={() => plan('retirerLigne')}>{'−'}</button>
          )}
        </div>
      )}
    </>
  )
}

/**
 * La checklist en pied de colonne (17/09/2026 : au-dessus de l'arbre, elle le
 * poussait vers le bas). D'abord ce qui est A SAISIR -- aucune ligne n'est
 * cliquable, la preuve est la valeur ecrite dans la trace -- puis ce que le
 * changement ENTRAINE ailleurs : la ligne se coche par la piece deposee, et
 * son libelle filtre l'arbre sur cette piece.
 */
export function ChecklistsTache({ tache, plan }: { tache: TacheVue, plan: GestePlan }) {
  return (
    <>
      <div className="rig-tache__effets rig-tache__effets--pied">
        <div className="rig-tache__effets-titre">
          {' A saisir '}
          <span className="rig-tache__effets-compte">
            {tache.casesFaites}{'\n / '}{tache.casesExigees}{' renseignes\n'}
          </span>
        </div>
        {tache.cases.map((c, i) => (
          <div key={i}
               className={'rig-tache__effet ' + (c.exige ? '' : 'rig-tache__effet--facultatif') + ' ' + (c.rempli ? 'rig-tache__effet--prouve' : '')}
               title={c.titre}>
            <input type="checkbox" checked={c.rempli} disabled={true} onChange={rien} />{' '}
            <span className="rig-tache__effet-libelle">{c.libelle}</span>
            {c.rempli && <span className="rig-tache__effet-preuve">{'saisi : '}{c.valeur}</span>}
            {!c.exige && <span className="rig-tache__effet-note">non exige</span>}
          </div>
        ))}
      </div>{' '}
      <div className="rig-tache__effets rig-tache__effets--pied">
        <div className="rig-tache__effets-titre">
          {' Ce que ce changement entraine '}
          <span className="rig-tache__effets-compte">
            {tache.effetsTraites}{'\n / '}{tache.effetsObligatoires}{' traites\n'}
          </span>
        </div>
        {tache.effets.map((e, i) => (
          <div key={i}
               className={'rig-tache__effet ' + (e.obligatoire ? '' : 'rig-tache__effet--facultatif') + ' ' + (e.atteste ? 'rig-tache__effet--prouve' : '') + ' ' + (e.cible ? 'rig-tache__effet--cible' : '')}
               title={e.titre}>
            {/* Cochee par la PIECE deposee : une coche posee a la main ne prouvait rien. */}
            <input type="checkbox" checked={e.coche} disabled={e.atteste}
                   onChange={() => plan('basculerEffet', i)} />{' '}
            {/* Le libelle est un BOUTON : il filtre l'arbre sur la piece attendue par cet effet. */}
            <button className="rig-tache__effet-libelle"
                    disabled={e.sansPiece}
                    title={e.titreLibelle}
                    onClick={() => plan('viserEffet', i)}>{e.libelle}</button>{' '}
            <span className="rig-tache__effet-code">{e.code}</span>
            {e.atteste && <span className="rig-tache__effet-preuve">{'prouve : '}{e.piece}</span>}
            {!e.obligatoire && <span className="rig-tache__effet-note">non exige</span>}
          </div>
        ))}
      </div>
    </>
  )
}
