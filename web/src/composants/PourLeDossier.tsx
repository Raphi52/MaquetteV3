// « POUR LE DOSSIER » : l'affaire d'ou un ecran de CREATION a ete ouvert, depuis
// la barre d'actions de sa page (plan « Vue du dossier = coeur des actions »,
// lot 2, 20/11/2026). Facture, encaissement, courrier : ce qu'on y cree est pour
// cette affaire, deja choisie -- l'agent n'a rien a ressaisir. Lecture seule :
// changer d'affaire, c'est repartir de la page d'une autre.
//
// Ouvert depuis l'accueil, l'ecran ne recoit aucun contexte et ne montre pas ce
// bloc : il est exactement celui d'avant.
//
// fix-ok: capture du 20/11/2026 (Courriers divers depuis 004176) -- rendu en
// fragment, le bloc n'avait pas de separation : le titre des criteres de l'ecran
// collait a « Denomination ». Il est donc un bloc a part (.rig-criteres__dossier,
// filet en bas), comme les autres blocs de criteres.

import type { ContexteDossierVue } from '../plan/types'

export interface ProprietesPourLeDossier {
  dossier: ContexteDossierVue
}

export function PourLeDossier({ dossier }: ProprietesPourLeDossier) {
  return (
    <div className="rig-criteres__dossier">
      <div className="rig-kicker">Pour le dossier</div>
      <div className="rig-criteres__paire">
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">N de dossier</span>
          <input className="rig-saisie-champ__saisie" value={dossier.numero} readOnly />
        </label>
        {/* Une affaire purement judiciaire n'a pas de SIREN : pas de champ vide. */}
        {dossier.siren.length > 0 && (
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Siren</span>
            <input className="rig-saisie-champ__saisie" value={dossier.siren} readOnly />
          </label>
        )}
      </div>
      <label className="rig-saisie-champ">
        <span className="rig-saisie-champ__libelle">Denomination</span>
        <input className="rig-saisie-champ__saisie" value={dossier.nom} readOnly />
      </label>
    </div>
  )
}
