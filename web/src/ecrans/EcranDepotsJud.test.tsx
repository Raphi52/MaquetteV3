import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranDepotsJud } from './EcranDepotsJud'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont() })

function ouvrir(ouvrirDossier = vi.fn()) {
  const pont = installerFauxPont({ 'EcranDepotsJud.DateDuJour': () => '2026-09-09' })
  return { pont, ouvrirDossier, ...render(<EcranDepotsJud ouvrirDossier={ouvrirDossier} />) }
}

const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')
/** La phrase de synthese sous le titre : le compte, les en cours, les a facturer. */
const synthese = (c: HTMLElement) => espaces(c.querySelector('.rig-lm-synthese')!.textContent)
const titre = (c: HTMLElement) => c.querySelector('.rig-lm-titre')!.textContent
const rangees = (c: HTMLElement) => [...c.querySelectorAll<HTMLTableRowElement>('tbody tr:not(.rig-lm-detail)')]

describe('EcranDepotsJud', () => {
  it('ouvre sur l\'etape de recherche d\'instance, avec les quatre instances, une ligne par instance', () => {
    const { container } = ouvrir()
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

  it('l\'etape Facturation ne garde que les instances a facturer', () => {
    const { container } = ouvrir()
    fireEvent.click(screen.getByRole('button', { name: 'Facturation et gestion commerciale' }))
    expect(titre(container)).toBe('Facturation et gestion commerciale')
    expect(synthese(container)).toBe('2 instances · 1 en cours · 2 a facturer')
  })

  it('l\'etape « Nouvel evenement survenu » montre l\'evenement, date du jour venue du pont', async () => {
    const { pont } = ouvrir()
    expect(screen.queryByLabelText('Evenement survenu')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Nouvel evenement survenu' }))
    expect((screen.getByLabelText('Evenement survenu') as HTMLSelectElement).value).toBe('JUG')
    expect(await screen.findByDisplayValue('2026-09-09')).toBe(screen.getByLabelText('Date de l\'evenement'))
    expect(pont.appels).toHaveBeenCalledWith('EcranDepotsJud.DateDuJour')
  })

  it('cherche par numero (« Aller a une instance… ») et par denomination, sans casse', () => {
    const { container } = ouvrir()
    fireEvent.change(screen.getByLabelText('Aller a une instance…'), { target: { value: '2026j' } })
    expect(synthese(container)).toBe('2 instances · 2 en cours · 1 a facturer')
    fireEvent.change(screen.getByLabelText('Denomination'), { target: { value: 'marne' } })
    expect(synthese(container)).toBe('1 instance · 1 en cours · 0 a facturer')
    fireEvent.change(screen.getByLabelText('Denomination'), { target: { value: 'inconnue' } })
    expect(screen.getByText('Aucune instance ne repond a cette recherche.')).toBeTruthy()
  })

  it('une instance cliquee remonte son dossier a la coquille ; le chevron ne l\'ouvre pas, la ligne depliee et le menu oui', () => {
    const { ouvrirDossier } = ouvrir()
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
  it('« Aller a une instance… » (Ctrl K) est le critere « N d\'instance » ; Entree ouvre le dossier de l\'instance restee seule ; le pied compte', () => {
    const { container, ouvrirDossier } = ouvrir()
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
  const rendre = (dossier: ReturnType<typeof contexteDossier> | null) => {
    installerFauxPont({ 'EcranDepotsJud.DateDuJour': () => '2026-09-09' })
    return render(<EcranDepotsJud ouvrirDossier={vi.fn()} dossier={dossier} />)
  }

  it('ne montre que son instance, le critere deja rempli ; sans affaire, les quatre', () => {
    const avec = rendre(contexteDossier({ numero: '2026J00134', type: 'JUD' }))
    expect(synthese(avec.container)).toBe('1 instance · 1 en cours · 0 a facturer')
    expect(screen.getByText('SAS TRANSPORTS MARNE')).toBeTruthy()
    expect((screen.getByLabelText('Aller a une instance…') as HTMLInputElement).value).toBe('2026J00134')
    avec.unmount()
    retirerFauxPont()

    const sans = rendre(null)
    expect(synthese(sans.container)).toBe('4 instances · 2 en cours · 2 a facturer')
    expect((screen.getByLabelText('Aller a une instance…') as HTMLInputElement).value).toBe('')
  })
})
