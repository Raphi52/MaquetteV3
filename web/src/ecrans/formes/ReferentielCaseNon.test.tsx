// Une colonne CASE de la grille d'un Referentiel (et d'un Etat, meme fonction lisible) : une case non cochee s'affiche « Non »
// (plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026, ordre du chef msg-cab1a60f). Le serveur rend une case cochee « oui » et une
// case NON cochee « » (LecteurEcran.Case : "oui" ou ""), comme la case a cocher de l'ecran qui ecrit '' quand on la decoche
// (commun.tsx, Champ 'Case') : « » n'est pas une valeur absente. Avant : « — », comme une cellule vide.
// Jumeaux : une case cochee reste « Oui » ; une cellule TEXTE vide reste « — ».

import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, waitFor } from '@testing-library/react'
import { EcranGenere, type VueLignesEcran } from '../EcranGenere'
import type { DescripteurEcranVue, LigneEcranVue } from '../../plan/types'
import { installerFauxPont, retirerFauxPont } from '../../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

const grille: DescripteurEcranVue = {
  code: 'ECRAN_TEST', titre: 'Natures test', forme: 'Referentiel', sujet: 'natures', recherche: 'Rechercher…', criteres: [],
  valider: 'Valider', rang: 1,
  etats: [{ code: 'Validee', libelle: 'Utilisable en saisie', icone: 'validee' }],
  groupes: ['Natures'], marches: [], parametres: [], gestes: [], lignes: [], membres: [], enOnglets: false, sections: [],
  colonnes: [{ libelle: 'Code', type: 'Texte', options: [] }, { libelle: 'Commentaire', type: 'Texte', options: [] },
             { libelle: 'Active', type: 'Case', options: [] }],
}

const ligne = (code: string, commentaire: string, active: string): LigneEcranVue =>
  ({ nom: code, precisions: '', groupe: 'Natures', etat: 'Validee', faites: [], id: code, valeurs: [code, commentaire, active] })
const base = (lignes: LigneEcranVue[]): VueLignesEcran => ({ mode: 'base', message: null, greffe: '3801', lignes, exemples: [] })

/** Les cellules lues de la ligne <code>, sans sa colonne d'actions. */
function cellules(c: HTMLElement, code: string): string[] {
  const tr = [...c.querySelectorAll('tbody tr')].find((t) => t.querySelector('td')?.textContent?.startsWith(code))
  return [...(tr?.querySelectorAll('td:not(.rig-lm-cellule-menu)') ?? [])].map((td) => td.textContent?.trim() ?? '')
}

describe('Referentiel, colonne Case lue de la base', () => {
  it('une case non cochee (« » du serveur) se lit « Non », une cochee « Oui » ; un texte vide reste « — »', async () => {
    installerFauxPont({ 'EcranGenere.Lignes': () => base([ligne('NC', '', ''), ligne('CO', 'gardée', 'oui')]) })
    const { container } = render(<EcranGenere descripteur={grille} />)
    await waitFor(() => expect(cellules(container, 'NC')).toHaveLength(3))
    expect(cellules(container, 'NC')).toEqual(['NC', '—', 'Non'])
    expect(cellules(container, 'CO')).toEqual(['CO', 'gardée', 'Oui'])
  })
})
