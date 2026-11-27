import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranClients, type VueClients } from './EcranClients'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { oublierVuesDeSession } from '../composants/ListeModerne'

afterEach(() => { cleanup(); oublierVuesDeSession(); retirerFauxPont() })

// Sans serveur, le pont rend le mode « demo » : l'ecran montre ses clients d'exemple, marques (Pont/EcranClientsBase.cs).
const Demo: VueClients = { mode: 'demo', message: null, clients: [], total: 0 }
async function ouvrir(selectionner: (abrege: string) => void, v: VueClients = Demo) {
  const pont = installerFauxPont({ 'EcranClients.Clients': () => v })
  const r = render(<EcranClients selectionner={selectionner} />)
  await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranClients.Clients'))
  await vi.waitFor(() => expect(r.container.querySelectorAll('tbody .rig-lm-identite__objet').length).toBeGreaterThan(0))
  return r
}

// Valeurs attendues : celles de l'ecran Blazor (EcranClients.razor, 6 clients de demonstration).
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
// L'abrege de chaque client affiche : le debut de l'objet de sa cellule (« LEROY · C-000142 »).
const abreges = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody .rig-lm-identite__objet')].map((o) => o.textContent!.split(' · ')[0])
const synthese = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent

describe('EcranClients', () => {
  it('affiche les six clients : avatar et nom, statut en point colore, type en pastille', async () => {
    const { container } = await ouvrir(vi.fn())
    expect(synthese(container)).toBe('6 clients sur 6 · 4 actifs')
    // Avatar « LA » (ni SELARL ni & ne donnent d'initiale), nom, abrege · compte, puis Id, Groupe, Statut, Type.
    expect(lignes(container)[0]).toEqual(['LASELARL LEROY & ASSOCIESLEROY · C-000142', '1042', 'Mandataires', 'Actif', 'En compte'])
    expect(screen.getByText('Bloque', { selector: 'span.rig-lm-etat' }).className).toBe('rig-lm-etat rig-lm-t-rouge')
    expect(screen.getByText('Actif', { selector: 'tbody tr:first-child span.rig-lm-etat' }).className).toBe('rig-lm-etat rig-lm-t-vert')
    expect(container.querySelector('tbody tr:nth-child(4) .rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-vert')
    // Une pastille trop longue est coupee sans « … » (decision du plan, lot 3) : le type entier est au survol de sa cellule.
    expect(container.querySelector('tbody tr:first-child .rig-lm-pastille')!.closest('td')!.getAttribute('title')).toBe('En compte')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('6 clients affiches')
  })

  it('filtre par mot cle (sans casse) et par statut, et « Effacer » remet tout', async () => {
    const { container } = await ouvrir(vi.fn())
    fireEvent.change(screen.getByLabelText('Aller a un client…'), { target: { value: 'dup' } })
    expect(abreges(container)).toEqual(['DUPONT'])

    fireEvent.change(screen.getByLabelText('Aller a un client…'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'En sommeil' }))
    expect(screen.getByRole('button', { name: 'En sommeil' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(abreges(container)).toEqual(['PREV51'])

    fireEvent.click(screen.getByRole('button', { name: 'Greffe' }))
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucun client ne repond a ces criteres.')

    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    expect(synthese(container)).toBe('6 clients sur 6 · 4 actifs')
  })

  it('un clic sur une ligne remonte l\'abrege du client a Blazor', async () => {
    const selectionner = vi.fn()
    const { container } = await ouvrir(selectionner)
    fireEvent.click(container.querySelectorAll('tbody tr')[2]!)
    expect(selectionner).toHaveBeenCalledWith('DUPONT')
  })

  it('les cartes POSENT les criteres existants : Actifs, et un type par segment de la legende', async () => {
    const { container } = await ouvrir(vi.fn())
    const carteActifs = container.querySelector('.rig-lm-carte')!
    expect(carteActifs.textContent).toBe('Actifs4 sur 6')
    fireEvent.click(carteActifs)
    expect(screen.getByRole('button', { name: 'Actif' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(abreges(container)).toEqual(['LEROY', 'BLANC', 'WEBPRO', 'GRFCPY'])
    fireEvent.click(screen.getByRole('button', { name: 'Web 1', pressed: false }))
    expect(screen.getByRole('button', { name: 'Web' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(abreges(container)).toEqual(['WEBPRO'])
    // La carte « Type de client » enleve les types ; « Actifs » un second clic enleve le statut.
    fireEvent.click(screen.getByRole('button', { name: /^Type de client/ }))
    expect(screen.getByRole('button', { name: 'Web' }).className).toBe('rig-bascule')
    fireEvent.click(carteActifs)
    expect(abreges(container)).toHaveLength(6)
  })

  it('les vues POSENT leurs criteres et la puce active suit les criteres ; « + Nouvelle vue » garde les criteres pour la session', async () => {
    const premier = await ouvrir(vi.fn())
    const vue = (nom: RegExp) => screen.getByRole('button', { name: nom })
    expect(vue(/^Tous/).getAttribute('aria-pressed')).toBe('true')
    expect(vue(/^Bloques ou en sommeil/).textContent).toBe('Bloques ou en sommeil 2')
    fireEvent.click(vue(/^Bloques ou en sommeil/))
    expect(abreges(premier.container)).toEqual(['DUPONT', 'PREV51'])
    expect(screen.getByRole('button', { name: 'Bloque' }).className).toBe('rig-bascule rig-bascule--actif')
    // Un critere de plus : plus aucune vue n'est celle de l'ecran.
    fireEvent.click(screen.getByRole('button', { name: 'En compte' }))
    expect(premier.container.querySelector('.rig-lm-vue--active')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    fireEvent.change(screen.getByLabelText('Nom de la nouvelle vue'), { target: { value: 'Bloques en compte' } })
    fireEvent.keyDown(screen.getByLabelText('Nom de la nouvelle vue'), { key: 'Enter' })
    expect(vue(/^Bloques en compte/).getAttribute('aria-pressed')).toBe('true')
    premier.unmount()

    // L'ecran rouvert la garde ; la choisir repose ses criteres.
    const second = await ouvrir(vi.fn())
    fireEvent.click(vue(/^Bloques en compte/))
    expect(abreges(second.container)).toEqual(['DUPONT'])
  })

  it('« Aller a un client… » EST le critere « Mot cle » (nom, abrege ou compte) : un seul champ, il pose les vues ; Ctrl K y pose le curseur ; Entree choisit le client reste seul', async () => {
    const selectionner = vi.fn()
    const { container } = await ouvrir(selectionner)
    const champ = screen.getByLabelText('Aller a un client…') as HTMLInputElement
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)

    // Un seul champ pour un seul filtre : plus de champ « Mot cle » a gauche.
    expect(screen.queryByLabelText('Mot cle')).toBeNull()
    fireEvent.change(champ, { target: { value: 'compiegne' } })
    expect(abreges(container)).toEqual(['GRFCPY'])
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('1 client affiche')
    // C'est un critere : la vue « Tous » n'est plus la vue posee.
    expect(container.querySelector('.rig-lm-vue--active')).toBeNull()
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(selectionner).toHaveBeenCalledWith('GRFCPY')
    // Le compte, comme le critere d'avant.
    fireEvent.change(champ, { target: { value: 'g-000012' } })
    expect(abreges(container)).toEqual(['GRFCPY'])

    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucun client ne repond a ces criteres.')
    // « Effacer » le vide comme les autres criteres, et la vue « Tous » revient.
    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    expect(champ.value).toBe('')
    expect(screen.getByRole('button', { name: /^Tous/ }).getAttribute('aria-pressed')).toBe('true')
  })

  // Branche sur la base (lot 14) : les clients du pont, filtres par les memes criteres ; aucun exemple, aucune note.
  it('en base, les clients lus et eux seuls, filtres comme RIG v2', async () => {
    const client = (id: number, abrege: string, statut: string, type: string) => ({ id, compte: 'C' + id, abrege, nom: abrege + ' SARL', groupe: 'Avocats', statut, type })
    const { container } = await ouvrir(vi.fn(), { mode: 'serveur', message: null, total: 2,
      clients: [client(7, 'CLA', 'Actif', 'En compte'), client(8, 'CLB', 'Bloque', 'Web')] })
    expect(abreges(container)).toEqual(['CLA', 'CLB'])
    expect(screen.queryByText(/LEROY/)).toBeNull()
    expect(screen.queryByRole('note')).toBeNull()
    fireEvent.click(screen.getAllByRole('button', { name: /^Actifs/ })[0]!)
    expect(abreges(container)).toEqual(['CLA'])
  })
})
