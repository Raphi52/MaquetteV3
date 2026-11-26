import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranDepotsJud } from './EcranDepotsJud'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont() })

// La demonstration du pont (Pont/EcranDepotsJud.cs, Exemple), telle que le pont la serialise.
const exemple = {
  mode: 'demo', message: null, total: 4,
  lignes: [
    { numero: '2026J00121', denomination: 'SARL BOULANGERIE DUVAL', nature: 'Redressement judiciaire', chambre: '1re', ouverture: '2026-03-04', dernierEvenement: 'Jugement d\'ouverture', etat: 'En cours', statut: 'action', aFacturer: true, dossier: '2024B00318' },
    { numero: '2026J00134', denomination: 'SAS TRANSPORTS MARNE', nature: 'Liquidation judiciaire', chambre: '2e', ouverture: '2026-05-19', dernierEvenement: 'Designation d\'expert', etat: 'En cours', statut: 'attente', aFacturer: false, dossier: '2024B00407' },
    { numero: '2025J00088', denomination: 'EURL COIFFURE MARIE', nature: 'Sauvegarde', chambre: '1re', ouverture: '2025-11-02', dernierEvenement: 'Ordonnance', etat: 'Suspendue', statut: 'alerte', aFacturer: true, dossier: '2023B01120' },
    { numero: '2024J00507', denomination: 'SCI DU MOULIN', nature: 'Redressement judiciaire', chambre: '3e', ouverture: '2024-06-12', dernierEvenement: 'Cloture', etat: 'Close', statut: 'ok', aFacturer: false, dossier: '2025B00044' },
  ],
}

/** L'ecran monte, une fois les instances du pont arrivees. */
async function ouvrir(ouvrirDossier = vi.fn(), vue: unknown = exemple) {
  const pont = installerFauxPont({ 'EcranDepotsJud.DateDuJour': () => '2026-09-09', 'EcranDepotsJud.Vue': () => vue })
  const rendu = render(<EcranDepotsJud ouvrirDossier={ouvrirDossier} />)
  await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranDepotsJud.Vue'))
  return { pont, ouvrirDossier, ...rendu }
}

const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')
/** La phrase de synthese sous le titre : le compte, les en cours, les a facturer. */
const synthese = (c: HTMLElement) => espaces(c.querySelector('.rig-lm-synthese')!.textContent)
const titre = (c: HTMLElement) => c.querySelector('.rig-lm-titre')!.textContent
const rangees = (c: HTMLElement) => [...c.querySelectorAll<HTMLTableRowElement>('tbody tr:not(.rig-lm-detail)')]

describe('EcranDepotsJud', () => {
  it('ouvre sur l\'etape de recherche d\'instance, avec les quatre instances, une ligne par instance', async () => {
    const { container } = await ouvrir()
    await screen.findByText('SARL BOULANGERIE DUVAL')
    expect(titre(container)).toBe('Etape de recherche d\'instance')
    expect(synthese(container)).toBe('4 instances · 2 en cours · 2 a facturer')
    expect(screen.getByRole('button', { name: 'Etape de recherche d\'instance' }).className).toBe('rig-bascule rig-bascule--actif')
    const premiere = rangees(container)[0]!
    expect(premiere.querySelector('.rig-lm-identite__nom')!.textContent).toBe('SARL BOULANGERIE DUVAL')
    expect(premiere.querySelector('.rig-lm-identite__objet')!.textContent).toBe('2026J00121')
    const nature = premiere.querySelector('.rig-lm-pastille')!
    expect([nature.textContent, nature.className]).toEqual(['Redressement judiciaire', 'rig-lm-pastille rig-lm-t-orange'])
    expect([...premiere.querySelectorAll('td')].slice(3, 5).map((td) => td.textContent)).toEqual(['04/03/2026', 'Jugement d\'ouverture'])
    const etat = premiere.querySelector('.rig-lm-etat')!
    expect([etat.textContent, etat.className]).toEqual(['En cours', 'rig-lm-etat rig-lm-t-bleu'])
    expect(rangees(container)[2]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')

    // Chambre et « A facturer » passent dans la ligne depliee ; le chevron n'ouvre pas le dossier.
    fireEvent.click(within(premiere).getByRole('button', { name: 'Deplier la ligne' }))
    const detail = premiere.nextElementSibling!
    expect(detail.querySelector('td')!.getAttribute('colspan')).toBe('7')
    expect([...detail.querySelectorAll('.rig-lm-champ')].map((c) => [
      c.querySelector('.rig-lm-champ__libelle')!.textContent, c.querySelector('.rig-lm-champ__valeur')!.textContent]))
      .toEqual([['Chambre', '1re'], ['A facturer', 'oui'], ['Dossier', '2024B00318']])
  })

  it('l\'etape Facturation ne garde que les instances a facturer', async () => {
    const { container } = await ouvrir()
    await screen.findByText('SARL BOULANGERIE DUVAL')
    fireEvent.click(screen.getByRole('button', { name: 'Facturation et gestion commerciale' }))
    expect(titre(container)).toBe('Facturation et gestion commerciale')
    expect(synthese(container)).toBe('2 instances · 1 en cours · 2 a facturer')
  })

  it('l\'etape « Nouvel evenement survenu » montre l\'evenement, date du jour venue du pont', async () => {
    const { pont } = await ouvrir()
    expect(screen.queryByLabelText('Evenement survenu')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Nouvel evenement survenu' }))
    expect((screen.getByLabelText('Evenement survenu') as HTMLSelectElement).value).toBe('JUG')
    expect(await screen.findByDisplayValue('2026-09-09')).toBe(screen.getByLabelText('Date de l\'evenement'))
    expect(pont.appels).toHaveBeenCalledWith('EcranDepotsJud.DateDuJour')
  })

  it('cherche par numero (« Aller a une instance… ») et par denomination, sans casse', async () => {
    const { container } = await ouvrir()
    await screen.findByText('SARL BOULANGERIE DUVAL')
    fireEvent.change(screen.getByLabelText('Aller a une instance…'), { target: { value: '2026j' } })
    expect(synthese(container)).toBe('2 instances · 2 en cours · 1 a facturer')
    fireEvent.change(screen.getByLabelText('Denomination'), { target: { value: 'marne' } })
    expect(synthese(container)).toBe('1 instance · 1 en cours · 0 a facturer')
    fireEvent.change(screen.getByLabelText('Denomination'), { target: { value: 'inconnue' } })
    expect(screen.getByText('Aucune instance ne repond a cette recherche.')).toBeTruthy()
  })

  it('une instance cliquee remonte son dossier a la coquille ; le chevron ne l\'ouvre pas, la ligne depliee et le menu oui', async () => {
    const { ouvrirDossier } = await ouvrir()
    await screen.findByText('SARL BOULANGERIE DUVAL')
    fireEvent.click(screen.getByText('2025J00088'))
    expect(ouvrirDossier).toHaveBeenCalledWith('2023B01120')

    const rangee = screen.getByText('SAS TRANSPORTS MARNE').closest('tr')!
    fireEvent.click(within(rangee).getByRole('button', { name: 'Deplier la ligne' }))
    expect(ouvrirDossier).toHaveBeenCalledTimes(1)
    fireEvent.click(within(rangee.nextElementSibling as HTMLElement).getByRole('button', { name: 'Ouvrir le dossier' }))
    expect(ouvrirDossier).toHaveBeenLastCalledWith('2024B00407')

    fireEvent.click(screen.getByRole('button', { name: 'Actions de l\'instance 2024J00507' }))
    expect(screen.getAllByRole('menuitem').map((m) => m.textContent)).toEqual(['Ouvrir le dossier', 'Deplier la ligne'])
    fireEvent.click(screen.getByRole('menuitem', { name: 'Ouvrir le dossier' }))
    expect(ouvrirDossier).toHaveBeenLastCalledWith('2025B00044')
    expect(ouvrirDossier).toHaveBeenCalledTimes(3)
  })

  // Un seul champ pour un seul filtre (decision du plan des listes, lot 7) : « Aller a une instance… » EST l'ancien
  // critere « N d'instance ». S'il redevenait un second filtre pose sur la liste affichee (numero, nom, nature,
  // dossier), « sauvegarde » trouverait la COIFFURE MARIE ; « Denomination » reste un critere a part, qui s'ajoute.
  it('« Aller a une instance… » (Ctrl K) est le critere « N d\'instance » ; Entree ouvre le dossier de l\'instance restee seule ; le pied compte', async () => {
    const { container, ouvrirDossier } = await ouvrir()
    await screen.findByText('SARL BOULANGERIE DUVAL')
    expect(screen.queryByLabelText('N d\'instance')).toBeNull()
    const champ = screen.getByLabelText('Aller a une instance…')
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: ' 2025j ' } })
    expect(rangees(container).map((r) => r.querySelector('b')!.textContent)).toEqual(['EURL COIFFURE MARIE'])
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(ouvrirDossier).toHaveBeenCalledWith('2023B01120')
    expect([...container.querySelector('.rig-lm-pied')!.children].map((e) => espaces(e.textContent)))
      .toEqual(['1 instance affichee', 'En cours 0', 'A facturer 1'])

    fireEvent.change(champ, { target: { value: '2026J' } })
    expect(rangees(container).map((r) => r.querySelector('b')!.textContent)).toEqual(['SARL BOULANGERIE DUVAL', 'SAS TRANSPORTS MARNE'])
    fireEvent.change(screen.getByLabelText('Denomination'), { target: { value: 'marne' } })
    expect(rangees(container).map((r) => r.querySelector('b')!.textContent)).toEqual(['SAS TRANSPORTS MARNE'])
    fireEvent.change(screen.getByLabelText('Denomination'), { target: { value: '' } })

    fireEvent.change(champ, { target: { value: 'sauvegarde' } })
    expect(screen.getByText('Aucune instance ne repond a cette recherche.')).toBeTruthy()
  })
})

// Ouvert depuis la page d'une affaire judiciaire : son instance, cherchee par son numero
// dans « Aller a une instance… », l'ancien critere « N d'instance » (plan « Vue du dossier », lot 2).
describe('EcranDepotsJud ouvert depuis une affaire', () => {
  const rendre = async (dossier: ReturnType<typeof contexteDossier> | null) => {
    installerFauxPont({ 'EcranDepotsJud.DateDuJour': () => '2026-09-09', 'EcranDepotsJud.Vue': () => exemple })
    const rendu = render(<EcranDepotsJud ouvrirDossier={vi.fn()} dossier={dossier} />)
    await screen.findByText('SAS TRANSPORTS MARNE')
    return rendu
  }

  it('ne montre que son instance, le critere deja rempli ; sans affaire, les quatre', async () => {
    const avec = await rendre(contexteDossier({ numero: '2026J00134', type: 'JUD' }))
    expect(synthese(avec.container)).toBe('1 instance · 1 en cours · 0 a facturer')
    expect(screen.getByText('SAS TRANSPORTS MARNE')).toBeTruthy()
    expect((screen.getByLabelText('Aller a une instance…') as HTMLInputElement).value).toBe('2026J00134')
    avec.unmount()
    retirerFauxPont()

    const sans = await rendre(null)
    expect(synthese(sans.container)).toBe('4 instances · 2 en cours · 2 a facturer')
    expect((screen.getByLabelText('Aller a une instance…') as HTMLInputElement).value).toBe('')
  })
})

// D'ou viennent les instances (plan « RIG v3 100 % fonctionnel », lot 5) : la demonstration est marquee « exemple », la
// base ne l'est pas, une panne montre son motif et AUCUNE ligne.
describe('EcranDepotsJud : provenance des instances', () => {
  it('sans serveur, les instances de demonstration sont marquees « exemple »', async () => {
    const { container } = await ouvrir()
    await screen.findByText('SARL BOULANGERIE DUVAL')
    expect(container.querySelector('[role="note"]')!.textContent).toContain('Instances de démonstration')
  })

  it('branche au serveur, les instances de la base, sans mention ; « Libre » et « A facturer » comme le pont les dit', async () => {
    const base = { mode: 'serveur', message: null, total: 1, lignes: [
      { numero: '2026J00042', denomination: 'BASE ALPHA # BASE BETA', nature: 'Opposition', chambre: '', ouverture: '2026-02-03',
        dernierEvenement: 'Jugement (05/02/2026)', etat: 'Libre', statut: 'ok', aFacturer: true, dossier: '2026J00042' }] }
    const { container, ouvrirDossier } = await ouvrir(vi.fn(), base)
    await screen.findByText('BASE ALPHA # BASE BETA')
    expect(container.querySelector('[role="note"]')).toBeNull()
    expect(synthese(container)).toBe('1 instance · 0 en cours · 1 a facturer')
    fireEvent.click(screen.getByText('2026J00042'))
    expect(ouvrirDossier).toHaveBeenCalledWith('2026J00042')
  })

  it('en panne, le motif et aucune instance (jamais la demonstration a la place)', async () => {
    const panne = { mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.', total: 0, lignes: [] }
    const { container } = await ouvrir(vi.fn(), panne)
    expect((await screen.findByRole('alert')).textContent).toContain('La base du greffe 3801 ne répond pas.')
    expect(rangees(container).map((r) => r.textContent)).toEqual(['Aucune instance ne repond a cette recherche.'])
  })
})
