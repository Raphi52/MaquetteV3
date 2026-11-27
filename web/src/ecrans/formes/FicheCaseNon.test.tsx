// Un champ CASE d'une fiche lue, affiche en lecture (champ que l'ecrivain n'enregistre pas, ou fiche en lecture seule) : une case non
// cochee se lit « Non », comme dans la grille d'un Referentiel (ReferentielCaseNon.test.tsx). Plan « RIG v3 100 % fonctionnel », lot 14,
// 27/11/2026, refus du chef : le serveur rend une case cochee « oui » et une case NON cochee « » (LecteurEcran.Case : "oui" ou ""), et
// lue() (commun.tsx) prenait « » pour une valeur absente : « — ».
// Jumeaux : une case cochee reste « Oui » ; un champ TEXTE vide reste « — » (la meme borne « », un autre type).

import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { EcranGenere } from '../EcranGenere'
import type { DescripteurEcranVue, LigneEcranVue } from '../../plan/types'
import { installerFauxPont, retirerFauxPont } from '../../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

const fiche: DescripteurEcranVue = {
  code: 'ECRAN_TEST', titre: 'Mandataires test', forme: 'Fiche', sujet: 'mandataires', recherche: 'Rechercher…', criteres: [],
  valider: 'Enregistrer', rang: 1,
  etats: [{ code: 'Validee', libelle: 'Actif', icone: 'validee' }],
  groupes: ['Mandataires'], marches: [], parametres: [], gestes: [], lignes: [], membres: [], enOnglets: false, colonnes: [],
  sections: [{ titre: 'Mandataire', consigne: '', obligatoire: false,
    champs: [{ libelle: 'Désignation', type: 'Texte', options: [] }, { libelle: 'Inactif', type: 'Case', options: [] },
             { libelle: 'Administrateur', type: 'Case', options: [] }, { libelle: 'Commentaire', type: 'Texte', options: [] }] }],
}

const ligne: LigneEcranVue = { nom: 'Me DURAND', precisions: '', groupe: 'Mandataires', etat: 'Validee', faites: [], id: '1400001' }
const lu = (libelle: string, valeur: string) => ({ libelle, valeur, modifiable: false, colonnes: [], lignes: [] })

describe('Fiche lue, champ Case affiche en lecture', () => {
  it('une case non cochee (« » du serveur) se lit « Non », une cochee « Oui » ; un texte vide reste « — »', async () => {
    installerFauxPont({
      'EcranGenere.Lignes': () => ({ mode: 'base', message: null, greffe: '3801', lignes: [ligne], exemples: [] }),
      'EcranGenere.Fiche': () => ({
        mode: 'base', message: null,
        fiche: { procedure: 'ECRAN_TEST', id: '1400001', nom: 'Me DURAND', lectureSeule: false, motif: null,
          sections: [{ titre: 'Mandataire',
            champs: [lu('Désignation', 'Me DURAND'), lu('Inactif', ''), lu('Administrateur', 'oui'), lu('Commentaire', '')] }] },
      }),
    })
    const { container } = render(<EcranGenere descripteur={fiche} />)
    await waitFor(() => expect(container.querySelector('.rig-lm-identite__nom')?.textContent).toBe('Me DURAND'))
    fireEvent.click([...container.querySelectorAll('tr')].find((tr) => tr.querySelector('.rig-lm-identite__nom')?.textContent === 'Me DURAND')!)
    await waitFor(() => expect(container.textContent).toContain('Fiche lue dans la base du greffe 3801.'))
    const valeur = (libelle: string) => container.querySelector(`.rig-eg-lu__valeur[aria-label="${libelle}"]`)?.textContent
    expect(valeur('Inactif')).toBe('Non')
    expect(valeur('Administrateur')).toBe('Oui')
    expect(valeur('Commentaire')).toBe('—')
  })
})
