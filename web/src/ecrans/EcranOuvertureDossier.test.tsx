import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranOuvertureDossier, valeurCapital, type LigneDossierOuvert, type ResultatOuverture, type VueOuverture } from './EcranOuvertureDossier'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Les controles et l'ouverture elle-meme sont faits par
// Pont/EcranOuvertureDossier.cs, execute par tests/OuvertureCheck. Ici : le
// SIREN demande est pre-rempli, la fiche part au pont, un refus garde la fiche.
const ouvert: LigneDossierOuvert = { numero: '2026B00001', siren: '512345678', denomination: 'SOCIETE Z', forme: 'SAS',
                                     immatriculation: '09/09/2026', capital: '1 000', monnaie: 'EUR', origine: 'Guichet unique (INPI)' }

const synthese = () => document.querySelector('.rig-lm-synthese')!.textContent

describe('EcranOuvertureDossier', () => {
  it('pre-remplit le SIREN recu d\'une recherche restee vide, et la date du jour du pont', async () => {
    installerFauxPont({ 'EcranOuvertureDossier.Vue': (): VueOuverture => ({ aujourdhui: '2026-09-09', ouverts: [] }) })
    render(<EcranOuvertureDossier sirenDemande=" 512345678 " />)
    expect((screen.getByLabelText('N° SIREN') as HTMLInputElement).value).toBe('512345678')
    await waitFor(() => expect((screen.getByLabelText('Date d\'immatriculation') as HTMLInputElement).value).toBe('2026-09-09'))
    expect(synthese()).toBe('0 dossier(s)')
    expect(screen.getByText('Aucun dossier ouvert pour l\'instant : remplissez la fiche a gauche.')).toBeTruthy()
  })

  it('ouvre le dossier par le pont, puis vide la fiche ; un refus la garde', async () => {
    const reponses: ResultatOuverture[] = [
      { message: 'La denomination est obligatoire pour ouvrir un dossier.', bloquant: true, ouverts: [] },
      { message: 'Dossier 2026B00001 ouvert pour SOCIETE Z', bloquant: false, ouverts: [ouvert] },
    ]
    const pont = installerFauxPont({
      'EcranOuvertureDossier.Vue': (): VueOuverture => ({ aujourdhui: '2026-09-09', ouverts: [] }),
      'EcranOuvertureDossier.Ouvrir': () => reponses.shift(),
    })
    render(<EcranOuvertureDossier sirenDemande="512345678" />)
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranOuvertureDossier.Vue'))
    const bouton = screen.getByRole('button', { name: /Ouvrir le dossier/ })

    fireEvent.click(bouton)
    await screen.findByText('La denomination est obligatoire pour ouvrir un dossier.')
    expect((screen.getByLabelText('N° SIREN') as HTMLInputElement).value).toBe('512345678')

    fireEvent.click(bouton)
    await screen.findByText('Dossier 2026B00001 ouvert pour SOCIETE Z')
    expect(pont.appels.mock.lastCall![1]).toMatchObject({ siren: '512345678', dateImmat: '2026-09-09', capital: 1000 })
    expect((screen.getByLabelText('N° SIREN') as HTMLInputElement).value).toBe('')
    expect(screen.getByText('2026B00001')).toBeTruthy()
  })
})

describe('EcranOuvertureDossier en liste moderne', () => {
  const autres: LigneDossierOuvert[] = [
    ouvert,
    { ...ouvert, numero: '2026B00002', siren: '612345678', denomination: 'BATIPRO SARL', forme: 'SARL', capital: '10 000',
      origine: 'Depot au guichet' },
    { ...ouvert, numero: '2026B00003', siren: '712345678', denomination: 'HELVETIA SA', forme: 'SA', capital: '50 000', monnaie: 'CHF' },
  ]

  it('une ligne par dossier : denomination et numero, forme et origine en pastilles, capital et sa barre dans sa monnaie', async () => {
    installerFauxPont({ 'EcranOuvertureDossier.Vue': (): VueOuverture => ({ aujourdhui: '2026-09-09', ouverts: autres }) })
    const { container } = render(<EcranOuvertureDossier />)
    await screen.findByText('BATIPRO SARL')
    expect(synthese()).toBe('3 dossier(s) · capital 11 000 EUR · 50 000 CHF')
    const [z, batipro, helvetia] = [...container.querySelectorAll('tbody tr')]
    expect(z!.querySelector('.rig-lm-identite__objet')!.textContent).toBe('2026B00001 · SIREN 512345678')
    expect([...z!.querySelectorAll('.rig-lm-pastille')].map((p) => [p.textContent, p.className])).toEqual([
      ['SAS', 'rig-lm-pastille rig-lm-t-bleu'], ['Guichet unique (INPI)', 'rig-lm-pastille rig-lm-t-violet']])
    // La barre se mesure au plus grand capital de la MEME monnaie : 1 000 sur 10 000 EUR, 50 000 sur 50 000 CHF.
    const barre = (tr: Element) => (tr.querySelector('.rig-lm-montant__barre b') as HTMLElement).style.width
    expect([barre(z!), barre(batipro!), barre(helvetia!)]).toEqual(['10%', '100%', '100%'])
    expect(helvetia!.querySelector('.rig-lm-montant__texte')!.textContent).toBe('50 000 CHF')
    expect(container.querySelector('.rig-lm-pied')!.textContent)
      .toBe('3 dossiers ouvertsCapital 11 000 EURCapital 50 000 CHF')
  })

  it('« Aller a un dossier… » filtre la liste affichee', async () => {
    installerFauxPont({ 'EcranOuvertureDossier.Vue': (): VueOuverture => ({ aujourdhui: '2026-09-09', ouverts: autres }) })
    const { container } = render(<EcranOuvertureDossier />)
    await screen.findByText('BATIPRO SARL')
    const champ = screen.getByRole('textbox', { name: 'Aller a un dossier…' })
    fireEvent.change(champ, { target: { value: '612345678' } })
    expect([...container.querySelectorAll('tbody tr')].map((tr) => tr.querySelector('.rig-lm-identite__nom')!.textContent))
      .toEqual(['BATIPRO SARL'])
    expect(synthese()).toBe('1 dossier(s) · capital 10 000 EUR')
    // Sans accents, comme l'ecran modele : « dépôt » trouve l'origine « Depot au guichet ».
    fireEvent.change(champ, { target: { value: 'dépôt' } })
    expect([...container.querySelectorAll('tbody tr')].map((tr) => tr.querySelector('.rig-lm-identite__nom')!.textContent))
      .toEqual(['BATIPRO SARL'])
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 3 dossiers ne contient « zzz ».')).toBeTruthy()
  })

  it('le capital « N0 » du pont se lit quel que soit son separateur', () => {
    expect([valeurCapital('1 000'), valeurCapital('1 000'), valeurCapital('1,000'), valeurCapital('0'), valeurCapital('')])
      .toEqual([1000, 1000, 1000, 0, 0])
  })
})

// Branche au serveur (plan « RIG v3 100 % fonctionnel », lot 17) : les dossiers de la base du greffe, sans capital ni
// origine lus (cellules vides, aucun faux zero), et « Ouvrir » qui n'ecrit pas et le dit ; sans serveur, « exemple ».
describe('EcranOuvertureDossier et la base du greffe', () => {
  const exemple = () => document.querySelector('.rig-tcw__exemple')
  const lu: LigneDossierOuvert = {
    numero: '2019B00411', siren: '412000411', denomination: 'ALPHA BASE SARL', forme: 'SARL', immatriculation: '12/03/2019',
    capital: '', monnaie: '', origine: '',
  }
  const refus = 'Rien n’est enregistré : dans RIG v2, un dossier RCS naît d’une formalité d’immatriculation, pas de cet écran.'

  it('des dossiers de la base : ni mention « exemple », ni capital invente ; « Ouvrir » refuse et garde la fiche', async () => {
    installerFauxPont({
      'EcranOuvertureDossier.Vue': (): VueOuverture => ({ aujourdhui: '2026-11-26', ouverts: [lu], mode: 'serveur', message: null }),
      'EcranOuvertureDossier.Ouvrir': (): ResultatOuverture => ({ message: refus, bloquant: true, ouverts: [lu] }),
    })
    render(<EcranOuvertureDossier sirenDemande="512345678" />)
    await screen.findByText('ALPHA BASE SARL')
    expect(exemple()).toBeNull()
    expect(synthese()).toBe('1 dossier(s)')
    const cellules = [...document.querySelectorAll('tbody tr td')]
    expect(cellules[2]!.textContent).toBe('')
    expect(cellules[4]!.textContent).toBe('')
    fireEvent.click(screen.getByRole('button', { name: /Ouvrir le dossier/ }))
    await screen.findByText(refus)
    expect((screen.getByLabelText('N° SIREN') as HTMLInputElement).value).toBe('512345678')
  })

  it('la demonstration (sans serveur) porte la mention « exemple »', async () => {
    installerFauxPont({ 'EcranOuvertureDossier.Vue': (): VueOuverture => ({ aujourdhui: '2026-09-09', ouverts: [ouvert], mode: 'demo', message: null }) })
    render(<EcranOuvertureDossier />)
    await waitFor(() => expect(exemple()?.textContent).toBe('exemple'))
  })
})
