import { afterEach, describe, expect, it } from 'vitest'
import { act } from '@testing-library/react'
import { demarrer, type EtatRacine, type PageMontee } from './racine'
import { installerFauxPont, retirerFauxPont } from './test/faux-pont'

let pages: PageMontee[] = []
afterEach(() => {
  act(() => { for (const p of pages) p.arreter() })
  pages = []
  retirerFauxPont()
  document.body.innerHTML = ''
})

function app(): HTMLDivElement {
  const div = document.createElement('div')
  div.id = 'app'
  div.textContent = 'Chargement...'
  document.body.appendChild(div)
  return div
}

const verrous = () => ({
  lignes: [{ dossier: '004151', nom: 'Depot', statut: 'alerte', etat: 'En controle', utilisateur: 'C. BENIER', age: 11 }],
  detenteurs: [{ utilisateur: 'C. BENIER', nombre: 1 }],
})

function etat(partiel: Partial<EtatRacine>): EtatRacine {
  return { composant: 'EcranVerrous', rappels: [], bords: false, proprietes: {}, ...partiel }
}

async function monter(div: HTMLElement): Promise<void> {
  await act(async () => { pages.push(await demarrer(div)) })
}

describe('racine (montage par la fenetre, ex-Main.razor / IleReact.razor)', () => {
  it('monte le composant que donne Racine.Ouvrir, dans un div[data-ile] sans boite, et remplace « Chargement... »', async () => {
    const pont = installerFauxPont({ 'Racine.Ouvrir': () => etat({}), 'EcranVerrous.Verrouilles': verrous })
    const div = app()
    await monter(div)
    expect(pont.appels.mock.calls[0]).toEqual(['Racine.Ouvrir'])
    expect(div.textContent).not.toContain('Chargement')
    const ile = div.firstElementChild as HTMLElement
    expect(ile.dataset.ile).toBe('EcranVerrous')
    expect(ile.style.display).toBe('contents')
    expect(div.querySelector('tbody tr')?.textContent).toContain('004151')
  })

  it('remonte un rappel a la racine C# (Racine.Rappeler), puis demonte proprement', async () => {
    const pont = installerFauxPont({
      'Racine.Ouvrir': () => etat({ rappels: ['ouvrirDossier'] }),
      'EcranVerrous.Verrouilles': verrous,
      'Racine.Rappeler': () => null,
    })
    const div = app()
    await monter(div)
    const ligne = div.querySelector('tbody tr')
    ligne!.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    expect(pont.appels).toHaveBeenLastCalledWith('Racine.Rappeler', 'ouvrirDossier', '004151')

    act(() => { pages.pop()?.arreter() })
    expect(div.innerHTML).toBe('')
  })

  it('une racine envoyee par le C# re-rend avec les nouvelles proprietes sans perdre l\'etat React', async () => {
    const pont = installerFauxPont({
      'Racine.Ouvrir': () => etat({ composant: 'EcranFactures' }),
      'EcranFactures.DateDuJour': () => '2026-09-09',
    })
    const div = app()
    await monter(div)
    const tarifer = [...div.querySelectorAll('button')].find((b) => b.textContent === 'Tarifer')!
    act(() => { tarifer.dispatchEvent(new MouseEvent('click', { bubbles: true })) })
    expect(tarifer.className).toBe('rig-onglet rig-onglet--actif')

    await act(async () => { pont.pousser('racine', etat({ composant: 'EcranFactures', proprietes: { inutile: 1 } })) })
    expect(tarifer.isConnected).toBe(true)
    expect(tarifer.className).toBe('rig-onglet rig-onglet--actif')
  })

  it('pose les 8 bandes de bord quand la racine le demande, et les retire quand la fenetre est agrandie', async () => {
    const pont = installerFauxPont({
      'Racine.Ouvrir': () => etat({ bords: true }),
      'EcranVerrous.Verrouilles': verrous,
      'Racine.Rappeler': () => null,
    })
    const div = app()
    await monter(div)
    const bandes = [...div.querySelectorAll<HTMLElement>(':scope > .rig-bord')]
    expect(bandes.map((b) => b.className)).toEqual([
      'rig-bord rig-bord--h', 'rig-bord rig-bord--b', 'rig-bord rig-bord--g', 'rig-bord rig-bord--d',
      'rig-bord rig-bord--hg', 'rig-bord rig-bord--hd', 'rig-bord rig-bord--bg', 'rig-bord rig-bord--bd',
    ])
    expect(bandes.every((b) => b.getAttribute('aria-hidden') === 'true')).toBe(true)

    bandes[5]!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))
    expect(pont.appels).toHaveBeenLastCalledWith('Racine.Rappeler', 'bord', 'HautDroite')

    await act(async () => { pont.pousser('racine', etat({ bords: false })) })
    expect(div.querySelectorAll('.rig-bord')).toHaveLength(0)
  })

  it('ne reste jamais blanche : une racine en echec s\'ecrit dans la page', async () => {
    installerFauxPont({ 'Racine.Ouvrir': () => { throw new Error('session introuvable') } })
    const div = app()
    await monter(div)
    expect(div.textContent).toContain('RIG V3 n\'a pas pu afficher sa page')
    expect(div.textContent).toContain('Racine.Ouvrir : session introuvable')
  })

  it('un composant inconnu s\'ecrit lui aussi dans la page, avec les noms montables', async () => {
    installerFauxPont({ 'Racine.Ouvrir': () => etat({ composant: 'EcranInconnu' }) })
    const div = app()
    await monter(div)
    expect(div.textContent).toMatch(/pas pu afficher sa page.*EcranInconnu.*EcranVerrous/)
  })

  it('hors d\'une fenetre RIG (pont absent), la page le dit au lieu de rester sur « Chargement... »', async () => {
    const div = app()
    await monter(div)
    expect(div.textContent).toContain('chrome.webview est absent')
  })
})
