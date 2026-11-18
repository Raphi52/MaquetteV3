import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranVerrous, type LigneVerrou, type VueVerrous } from './EcranVerrous'
import { installerFauxDotNet, retirerFauxDotNet } from '../test/faux-dotnet'

afterEach(() => { cleanup(); retirerFauxDotNet() })

// Le calcul (filtre, tri, age, detenteurs) est fait par Pont/EcranVerrous.cs
// et execute par tests/AlertesCheck. Ici : l'ecran AFFICHE ce que le pont rend,
// et lui transmet le bon login et la bonne bascule.
const lignes: LigneVerrou[] = [
  { dossier: '004151', nom: 'Depot de bilan - TRANSNORD SA', statut: 'alerte', etat: 'En controle', utilisateur: 'C. BENIER', age: 11 },
  { dossier: '004176', nom: 'Modification - SCI DU PARC', statut: 'alerte', etat: 'En controle', utilisateur: 'M. DURAND', age: 9 },
  { dossier: '004179', nom: 'Radiation - MARTIN EURL', statut: 'attente', etat: 'En controle', utilisateur: 'R. VILAIN', age: 4 },
]

function vue(utilisateur: unknown, moiCompris: unknown): VueVerrous {
  const l = lignes.filter((x) => moiCompris === true || x.utilisateur !== utilisateur)
  const noms = [...new Set(l.map((x) => x.utilisateur))].sort()
  return { lignes: l, detenteurs: noms.map((u) => ({ utilisateur: u, nombre: l.filter((x) => x.utilisateur === u).length })) }
}

describe('EcranVerrous', () => {
  it('affiche les dossiers retenus par un autre poste, avec leur compte et leurs detenteurs', async () => {
    const dotnet = installerFauxDotNet({ 'EcranVerrous.Verrouilles': vue })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} />)

    await screen.findByText('2 dossier(s)')
    expect(dotnet.invokeMethodAsync).toHaveBeenCalledWith('RigV3Desktop', 'EcranVerrous.Verrouilles', 'R. VILAIN', false)
    const rangees = container.querySelectorAll('tbody tr')
    expect(rangees).toHaveLength(2)
    expect([...rangees[0]!.querySelectorAll('td')].map((td) => td.textContent))
      .toEqual(['004151', 'Depot de bilan - TRANSNORD SA', 'En controle', 'C. BENIER', '11 j'])
    expect(rangees[0]!.querySelector('.rig-etat')!.className).toBe('rig-etat rig-etat--alerte')
    expect(rangees[0]!.getAttribute('title')).toBe('Ouvrir l\'affaire 004151')
    expect([...container.querySelectorAll('.rig-alerte__titre')].map((e) => e.textContent)).toEqual(['C. BENIER', 'M. DURAND'])
  })

  it('« Inclure mes propres dossiers » redemande au pont avec la bascule allumee', async () => {
    const dotnet = installerFauxDotNet({ 'EcranVerrous.Verrouilles': vue })
    render(<EcranVerrous ouvrirDossier={vi.fn()} />)
    await screen.findByText('2 dossier(s)')

    const bascule = screen.getByRole('button', { name: 'Inclure mes propres dossiers' })
    expect(bascule.className).toBe('rig-bascule')
    fireEvent.click(bascule)

    await screen.findByText('3 dossier(s)')
    expect(bascule.className).toBe('rig-bascule rig-bascule--actif')
    expect(dotnet.invokeMethodAsync).toHaveBeenLastCalledWith('RigV3Desktop', 'EcranVerrous.Verrouilles', 'R. VILAIN', true)
  })

  it('« Mon login » change le poste de reference', async () => {
    const dotnet = installerFauxDotNet({ 'EcranVerrous.Verrouilles': vue })
    render(<EcranVerrous ouvrirDossier={vi.fn()} />)
    await screen.findByText('2 dossier(s)')

    fireEvent.change(screen.getByLabelText('Mon login'), { target: { value: 'C. BENIER' } })
    await waitFor(() => expect(dotnet.invokeMethodAsync)
      .toHaveBeenLastCalledWith('RigV3Desktop', 'EcranVerrous.Verrouilles', 'C. BENIER', false))
    await screen.findByText('2 dossier(s)')
    expect(screen.queryByText('Depot de bilan - TRANSNORD SA')).toBeNull()
  })

  it('remonte le NUMERO clique a la coquille Blazor', async () => {
    installerFauxDotNet({ 'EcranVerrous.Verrouilles': vue })
    const ouvrirDossier = vi.fn()
    render(<EcranVerrous ouvrirDossier={ouvrirDossier} />)

    fireEvent.click(await screen.findByText('004176'))
    expect(ouvrirDossier).toHaveBeenCalledWith('004176')
  })

  it('dit qu\'aucun dossier n\'est retenu, mais seulement une fois la reponse arrivee', async () => {
    let repondre: (v: VueVerrous) => void = () => {}
    installerFauxDotNet({ 'EcranVerrous.Verrouilles': () => new Promise<VueVerrous>((r) => { repondre = r }) })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} />)

    expect(screen.queryByText('Aucun dossier n\'est retenu par un autre poste.')).toBeNull()
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('')

    repondre({ lignes: [], detenteurs: [] })
    await screen.findByText('Aucun dossier n\'est retenu par un autre poste.')
    expect(screen.getByText('0 dossier(s)')).toBeTruthy()
  })

  it('ignore une reponse perimee arrivee apres une plus recente', async () => {
    const enAttente: Array<{ args: unknown[]; repondre: (v: VueVerrous) => void }> = []
    installerFauxDotNet({
      'EcranVerrous.Verrouilles': (...args) => new Promise<VueVerrous>((r) => { enAttente.push({ args, repondre: r }) }),
    })
    render(<EcranVerrous ouvrirDossier={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inclure mes propres dossiers' }))
    await waitFor(() => expect(enAttente).toHaveLength(2))

    enAttente[1]!.repondre(vue('R. VILAIN', true))
    await screen.findByText('3 dossier(s)')
    enAttente[0]!.repondre(vue('R. VILAIN', false))
    await new Promise((r) => setTimeout(r, 0))
    expect(screen.getByText('3 dossier(s)')).toBeTruthy()
  })
})
