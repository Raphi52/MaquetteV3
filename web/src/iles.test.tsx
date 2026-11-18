import { afterEach, describe, expect, it, vi } from 'vitest'
import { act } from '@testing-library/react'
import { composantsMontables, demonter, mettreAJour, monter } from './iles'
import { installerFauxDotNet, retirerFauxDotNet } from './test/faux-dotnet'

afterEach(retirerFauxDotNet)

function hote(): HTMLDivElement {
  const div = document.createElement('div')
  document.body.appendChild(div)
  return div
}

describe('iles (montage depuis Components/IleReact.razor)', () => {
  it('chaque ecran et composant porte est montable sous le nom de son fichier', () => {
    expect(composantsMontables()).toEqual(['EcranFactures', 'EcranVerrous', 'EnteteTriable'])
  })

  it('un nom inconnu echoue en listant les noms montables', () => {
    const reference = { invokeMethodAsync: vi.fn() }
    expect(() => monter(hote(), 'EcranInconnu', {}, [], reference)).toThrow(/EcranInconnu.*EcranFactures, EcranVerrous/)
  })

  it('monte, remonte un rappel a Blazor, puis demonte proprement', async () => {
    installerFauxDotNet({
      'EcranVerrous.Verrouilles': () => ({
        lignes: [{ dossier: '004151', nom: 'Depot', statut: 'alerte', etat: 'En controle', utilisateur: 'C. BENIER', age: 11 }],
        detenteurs: [{ utilisateur: 'C. BENIER', nombre: 1 }],
      }),
    })
    const reference = { invokeMethodAsync: vi.fn(async () => undefined) }
    const div = hote()

    await act(async () => { monter(div, 'EcranVerrous', {}, ['ouvrirDossier'], reference) })
    const ligne = div.querySelector('tbody tr')
    expect(ligne?.textContent).toContain('004151')

    ligne!.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    expect(reference.invokeMethodAsync).toHaveBeenCalledWith('Rappeler', 'ouvrirDossier', '004151')

    act(() => { demonter(div) })
    expect(div.innerHTML).toBe('')
    // Un second demontage (fermeture apres rechargement) ne casse rien.
    expect(() => demonter(div)).not.toThrow()
  })

  it('mettreAJour re-rend avec les nouvelles proprietes sans perdre l\'etat React', async () => {
    installerFauxDotNet({ 'EcranFactures.DateDuJour': () => '2026-09-09' })
    const reference = { invokeMethodAsync: vi.fn() }
    const div = hote()
    await act(async () => { monter(div, 'EcranFactures', {}, [], reference) })

    const tarifer = [...div.querySelectorAll('button')].find((b) => b.textContent === 'Tarifer')!
    act(() => { tarifer.dispatchEvent(new MouseEvent('click', { bubbles: true })) })
    expect(tarifer.className).toBe('rig-onglet rig-onglet--actif')

    await act(async () => { mettreAJour(div, { inutile: 1 }) })
    expect(tarifer.className).toBe('rig-onglet rig-onglet--actif')
    act(() => { demonter(div) })
  })
})
