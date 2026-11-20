import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCloture } from './EcranCloture'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont() })

// Valeurs attendues : celles de l'ecran Blazor (EcranCloture.razor), JeuDemo.Aujourdhui = 09/09/2026 :
// seule la limite d'examen du 05/09/2026 (SAS TRANSPORTS MARNE) est depassee.
function ouvrir(ouvrirDossier = vi.fn()) {
  const pont = installerFauxPont({ 'EcranCloture.DateDuJour': () => '2026-09-09' })
  return { pont, ouvrirDossier, ...render(<EcranCloture ouvrirDossier={ouvrirDossier} />) }
}
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
const alertes = (c: HTMLElement) => c.querySelectorAll('tbody tr.rig-table__ligne--alerte')

describe('EcranCloture', () => {
  it('affiche les cinq affaires de « Selection PC » et marque la limite depassee', async () => {
    const { container, pont } = ouvrir()
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent!.trim()).toBe('5 affichee(s) sur 5')
    expect(container.querySelectorAll('thead th')).toHaveLength(15)
    expect(lignes(container)[0]!.slice(0, 4)).toEqual(['PC-2024-0118', 'SARL BOULANGERIE DUVAL', '1re', '15/09/2026'])
    await vi.waitFor(() => expect(alertes(container)).toHaveLength(1))
    expect(pont.appels).toHaveBeenCalledWith('EcranCloture.DateDuJour')
    expect(alertes(container)[0]!.textContent).toContain('SAS TRANSPORTS MARNE')
  })

  it('un clic sur une ligne remonte le dossier a Blazor', () => {
    const { container, ouvrirDossier } = ouvrir()
    fireEvent.click(container.querySelectorAll('tbody tr')[0]!)
    expect(ouvrirDossier).toHaveBeenCalledWith('2024B00318')
  })

  it('les criteres filtrent ; l\'onglet « Enrolement / requete » a ses propres colonnes', async () => {
    const { container } = ouvrir()
    await vi.waitFor(() => expect(alertes(container)).toHaveLength(1))
    fireEvent.click(screen.getByRole('button', { name: 'Limite d\'examen depassee' }))
    expect(lignes(container).map((l) => l[0])).toEqual(['PC-2024-0207'])
    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    fireEvent.change(screen.getByLabelText('Chambre'), { target: { value: '1re' } })
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent!.trim()).toBe('2 affichee(s) sur 5')

    fireEvent.click(screen.getByRole('button', { name: 'Enrolement / requete' }))
    expect(screen.getByText('Enrolement et requete')).toBeTruthy()
    expect(container.querySelectorAll('thead th')).toHaveLength(8)
    expect(lignes(container)[0]).toEqual(['2024/00318', 'PC-2024-0118', 'Me LEROY', 'SARL BOULANGERIE DUVAL', 'Assignation', '15/09/2026', '04/03/2026', 'Facturee'])
    fireEvent.change(screen.getByLabelText('Affaire'), { target: { value: 'zzz' } })
    expect(container.querySelector('.rig-table__vide')!.getAttribute('colspan')).toBe('8')
  })
})

// Ouvert depuis la page d'une affaire : cherchee par son NOM dans « Affaire »
// (nom ou n de role ; plan « Vue du dossier », lot 2).
describe('EcranCloture ouvert depuis une affaire', () => {
  const rendre = (dossier: ReturnType<typeof contexteDossier> | null) => {
    installerFauxPont({ 'EcranCloture.DateDuJour': () => '2026-09-09' })
    return render(<EcranCloture ouvrirDossier={vi.fn()} dossier={dossier} />)
  }
  const resultat = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent!.trim()

  it('ne montre qu\'elle, le critere deja rempli', () => {
    const { container } = rendre(contexteDossier({ numero: '2025B00044', nom: 'SCI DU MOULIN' }))
    expect(resultat(container)).toBe('1 affichee(s) sur 5')
    expect(lignes(container)[0]!.slice(0, 2)).toEqual(['PC-2025-0044', 'SCI DU MOULIN'])
    expect((screen.getByLabelText('Affaire') as HTMLInputElement).value).toBe('SCI DU MOULIN')
  })

  it('une affaire sans procedure le dit ; sans affaire, les cinq', () => {
    const avec = rendre(contexteDossier())
    expect(screen.getByText('Aucune affaire ne repond a ces criteres.')).toBeTruthy()
    avec.unmount()
    retirerFauxPont()

    const sans = rendre(null)
    expect(resultat(sans.container)).toBe('5 affichee(s) sur 5')
    expect((screen.getByLabelText('Affaire') as HTMLInputElement).value).toBe('')
  })
})
