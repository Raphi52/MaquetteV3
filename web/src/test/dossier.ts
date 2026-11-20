// Le contexte d'une affaire, tel qu'un ecran ouvert depuis sa page le recoit
// (ContexteDossierVue, Pont/PlanTravailActions.cs) : l'affaire 004176 du jeu de
// demonstration (Pont/PlanTravail.cs, FondsDemo et SommairesDemo), sauf ce que
// le test change.

import type { ContexteDossierVue } from '../plan/types'

export function contexteDossier(partiel: Partial<ContexteDossierVue> = {}): ContexteDossierVue {
  return { numero: '004176', siren: '812 445 901', objet: 'Modification - SCI DU PARC', type: 'RCS', nom: 'SCI DU PARC', ...partiel }
}
