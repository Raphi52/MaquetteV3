import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCoffreFort } from './EcranCoffreFort'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranCoffreFort.razor) : 4 depots, dont 2
// « A transmettre » (jamais envoye, ou en erreur) et 2 « Deja transmis ».
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr:not(.rig-lm-detail)')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
// Le code de chaque depot affiche : le debut de l'objet de sa cellule (« CF-2026-0603 · MARIE »).
const codes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody .rig-lm-identite__objet')].map((o) => o.textContent!.split(' · ')[0])
const compte = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent
// Les onglets de l'ecran, en puces comptees.
const puce = (nom: RegExp) => screen.getByRole('button', { name: nom })

describe('EcranCoffreFort', () => {
  it('affiche les depots a transmettre, le depot en erreur en alerte', () => {
    const { container } = render(<EcranCoffreFort />)
    expect(compte(container)).toBe('2 depots a transmettre · 1 en erreur')
    expect([...container.querySelectorAll('thead th')].map((th) => th.textContent))
      .toEqual(['', 'Destinataire', 'Etat', 'Date envoi infogreffe', 'Code de depot', 'Erreur'])
    expect(codes(container)).toEqual(['CF-2026-0603', 'CF-2026-0610'])
    expect(container.querySelectorAll('tbody tr')[1]!.className).toBe('rig-table__ligne--alerte')
    expect(lignes(container)[1]!.slice(2, 4)).toEqual(['En erreur', '04/09/2026'])
    expect(container.querySelectorAll('tbody tr')[1]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')
    expect(container.querySelectorAll('tbody tr')[0]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
    expect(puce(/^A transmettre/).textContent).toBe('A transmettre 2')
    expect(puce(/^A transmettre/).getAttribute('aria-pressed')).toBe('true')
    // Distinct().Order() des coffres, apres « Tous ».
    expect([...(screen.getByLabelText('Nom du coffre') as HTMLSelectElement).options].map((o) => o.text))
      .toEqual(['Tous', 'COFFRE_BLANC', 'COFFRE_LEROY', 'COFFRE_PETIT'])
  })

  it('la puce « Deja transmis » et les criteres filtrent la liste', () => {
    const { container } = render(<EcranCoffreFort />)
    fireEvent.click(puce(/^Deja transmis/))
    expect(puce(/^Deja transmis/).getAttribute('aria-pressed')).toBe('true')
    expect(puce(/^A transmettre/).getAttribute('aria-pressed')).toBe('false')
    expect(codes(container)).toEqual(['CF-2026-0412', 'CF-2026-0518'])
    fireEvent.change(screen.getByLabelText('Nom du coffre'), { target: { value: 'COFFRE_BLANC' } })
    expect(codes(container)).toEqual(['CF-2026-0518'])
    // Chaque puce compte sa liste sous les criteres.
    expect(puce(/^Deja transmis/).textContent).toBe('Deja transmis 1')
    expect(puce(/^A transmettre/).textContent).toBe('A transmettre 0')
    fireEvent.change(screen.getByLabelText('Code ou destinataire'), { target: { value: 'leroy' } })
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucun depot ne repond a ces criteres.')
  })

  it('un clic sur un depot montre son historique, borne par le nombre de lignes', () => {
    const { container } = render(<EcranCoffreFort />)
    fireEvent.click(puce(/^Deja transmis/))
    fireEvent.click(container.querySelectorAll('tbody tr')[0]!)
    expect(screen.getByText('Historique de CF-2026-0412 (20 ligne(s) au plus)')).toBeTruthy()
    expect(container.querySelectorAll('.rig-historique__liste li')).toHaveLength(3)
    fireEvent.change(screen.getByLabelText('Nombre de lignes d\'historique'), { target: { value: '2' } })
    expect(container.querySelectorAll('.rig-historique__liste li')).toHaveLength(2)
  })

  it('le chevron deplie les dates de suivi sans choisir le depot ; « Voir l\'historique » le choisit', () => {
    const { container } = render(<EcranCoffreFort />)
    fireEvent.click(puce(/^Deja transmis/))
    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[0]!)
    expect(screen.queryByText(/^Historique de/)).toBeNull()
    expect([...container.querySelectorAll('.rig-lm-champ')].map((c) => c.textContent)).toEqual([
      'Date AR Infogreffe07/03/2026', 'Date AR destinataire08/03/2026', 'Date lecture destinataire09/03/2026',
      'Date traitement RIG07/03/2026', 'Date avis de depot coffre08/03/2026', 'CoffreCOFFRE_LEROY', 'Type de decisionACAD'])
    fireEvent.click(screen.getByRole('button', { name: 'Voir l\'historique' }))
    expect(screen.getByText('Historique de CF-2026-0412 (20 ligne(s) au plus)')).toBeTruthy()
  })

  it('« Aller a un depot… » filtre la liste affichee ; Entree choisit le depot reste seul', () => {
    const { container } = render(<EcranCoffreFort />)
    const champ = screen.getByLabelText('Aller a un depot…')
    fireEvent.change(champ, { target: { value: 'petit' } })
    expect(codes(container)).toEqual(['CF-2026-0610'])
    expect((screen.getByLabelText('Code ou destinataire') as HTMLInputElement).value).toBe('')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(screen.getByText('Historique de CF-2026-0610 (20 ligne(s) au plus)')).toBeTruthy()
  })
})
