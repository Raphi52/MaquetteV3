// Les gestes NOMMES par la fiche RIG v2 sur une Operation et sur un Referentiel (plan « RIG v3 100 % fonctionnel », lot 11,
// 27/11/2026) : une Operation offre un bouton par geste au temps Confirmer (« Générer » ou « Supprimer le bordereau » d'un
// envoi JAL), un Referentiel ajoute les siens au menu de la ligne (« Dupliquer » un document océrisé). Chacun part a
// l'ecrivain sous SON nom ; sans gestes nommes, rien ne change (le bouton valider seul, Modifier et Supprimer seuls).

import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { EcranGenere, type VueLignesEcran } from '../EcranGenere'
import type { DescripteurEcranVue, LigneEcranVue } from '../../plan/types'
import { installerFauxPont, retirerFauxPont } from '../../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

function descripteur(partiel: Partial<DescripteurEcranVue>): DescripteurEcranVue {
  return {
    code: 'ECRAN_TEST', titre: 'Ecran test', forme: 'Operation', sujet: 'annonces', recherche: 'Rechercher…',
    criteres: [], valider: 'Générer le bordereau', rang: 1,
    etats: [
      { code: 'AFaire', libelle: 'À envoyer', icone: 'a-traiter' },
      { code: 'EnCours', libelle: 'Bordereau généré', icone: 'saisie-manuelle' },
      { code: 'Validee', libelle: 'Envoyée', icone: 'validee' },
    ],
    groupes: ['Fond'], marches: [], sections: [], colonnes: [], parametres: [], gestes: [], lignes: [], membres: [], enOnglets: false,
    ...partiel,
  }
}

const ligne: LigneEcranVue = { nom: 'PC 2001P00001', precisions: 'bordereau 2001/0001', groupe: 'Fond', etat: 'EnCours', faites: [], id: '2110401' }
const base = (lignes: LigneEcranVue[]): VueLignesEcran => ({ mode: 'base', message: null, greffe: '3801', lignes, exemples: [] })
const fait = (message: string) => () => ({ fait: true, message, sansEcrivain: false, ids: ['2110401'] })

/** Coche la ligne, puis va au temps « Confirmer » ; rend les boutons du pied. */
async function auTempsConfirmer(container: HTMLElement) {
  await waitFor(() => expect(container.querySelector('tbody input[type="checkbox"]')).not.toBeNull())
  fireEvent.click(container.querySelector('tbody input[type="checkbox"]')!)
  const suivant = () => [...container.querySelectorAll('button')].find((b) => b.textContent === 'Suivant')!
  fireEvent.click(suivant())
  fireEvent.click(suivant())
  return [...container.querySelectorAll('button')].map((b) => b.textContent)
}

describe('Operation : un bouton par geste nomme', () => {
  it('deux gestes nommes : deux boutons, et Supprimer part a l’ecrivain sous son nom', async () => {
    const pont = installerFauxPont({
      'EcranGenere.Lignes': () => base([ligne]),
      'EcranGenere.Geste': fait('Bordereau 2001/0001 supprimé.'),
    })
    const d = descripteur({ gestes: ['Générer le bordereau', 'Supprimer le bordereau'] })
    const { container } = render(<EcranGenere descripteur={d} />)
    const boutons = await auTempsConfirmer(container)
    expect(boutons).toContain('Générer le bordereau')
    expect(boutons).toContain('Supprimer le bordereau')
    expect(container.querySelector('.rig-eg-recap')!.textContent).toContain('Générer le bordereau ou Supprimer le bordereau')
    fireEvent.click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Supprimer le bordereau')!)
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', 'Fond', 'Confirmer',
      'Supprimer le bordereau', ['2110401'], {}))
    await waitFor(() => expect(container.querySelector('.rig-eg-annonce')?.textContent).toContain('Bordereau 2001/0001 supprimé.'))
  })

  it('sans geste nomme : le bouton valider seul, envoye sous son nom (jumeau)', async () => {
    const pont = installerFauxPont({ 'EcranGenere.Lignes': () => base([ligne]), 'EcranGenere.Geste': fait('Bordereau généré.') })
    const { container } = render(<EcranGenere descripteur={descripteur({})} />)
    const boutons = await auTempsConfirmer(container)
    expect(boutons).toContain('Générer le bordereau')
    expect(boutons).not.toContain('Supprimer le bordereau')
    fireEvent.click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Générer le bordereau')!)
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', 'Fond', 'Confirmer',
      'Générer le bordereau', ['2110401'], {}))
  })
})

describe('Referentiel : les gestes nommes au menu de la ligne', () => {
  const doc: LigneEcranVue = { nom: 'courrier.pdf', precisions: 'PDF', groupe: 'PDF', etat: 'AFaire', faites: [], id: '2110301', valeurs: ['courrier', 'PDF'] }
  const grille = (gestes: string[]) => descripteur({
    forme: 'Referentiel', valider: 'Enregistrer les noms de fichiers', groupes: ['PDF'], gestes,
    colonnes: [{ libelle: 'Nom', type: 'Texte', options: [] }, { libelle: 'Format', type: 'Texte', options: [] }],
  })
  const menu = async (container: HTMLElement) => {
    await waitFor(() => expect(container.querySelector('button[aria-label="Actions — courrier.pdf"]')).not.toBeNull())
    fireEvent.click(container.querySelector('button[aria-label="Actions — courrier.pdf"]')!)
    return [...document.body.querySelectorAll('[role="menu"] button')]
  }

  it('« Dupliquer » s’ajoute a Modifier et Supprimer, et part a l’ecrivain (type Agir, la ligne)', async () => {
    const pont = installerFauxPont({
      'EcranGenere.Lignes': () => base([doc]),
      'EcranGenere.Geste': fait('Document « courrier.pdf » dupliqué en « courrier-copie.pdf ».'),
    })
    const { container } = render(<EcranGenere descripteur={grille(['Dupliquer'])} />)
    const actions = await menu(container)
    expect(actions.map((b) => b.textContent)).toEqual(['Modifier', 'Supprimer', 'Dupliquer'])
    fireEvent.click(actions[2]!)
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', 'PDF', 'Agir', 'Dupliquer', ['2110301'], {}))
    await waitFor(() => expect(container.querySelector('.rig-eg-annonce')?.textContent).toContain('dupliqué en « courrier-copie.pdf »'))
  })

  it('sans geste nomme : Modifier et Supprimer seuls (jumeau)', async () => {
    installerFauxPont({ 'EcranGenere.Lignes': () => base([doc]) })
    const { container } = render(<EcranGenere descripteur={grille([])} />)
    expect((await menu(container)).map((b) => b.textContent)).toEqual(['Modifier', 'Supprimer'])
  })
})
