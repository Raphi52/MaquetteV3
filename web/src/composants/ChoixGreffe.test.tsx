import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { ChoixGreffe, type GreffeNonPropose, type GreffePropose } from './ChoixGreffe'

afterEach(() => { cleanup(); document.documentElement.removeAttribute('data-rig-theme') })

// Le filtre des greffes (catalogue, base en ligne, compte present et actif) est fait par le serveur et
// prouve par tests/OuvertureGreffeCheck. Ici : l'ecran PROPOSE ce que le C# lui donne, laisse choisir
// d'un clic, du clavier ou d'un double clic, n'ouvre qu'un greffe a la fois, et nomme les autres.
const greffes: GreffePropose[] = [
  { code: '9201', nom: 'Greffe de Grenoble' },
  { code: '9202', nom: 'Greffe de Vienne' },
]
const nonProposes: GreffeNonPropose[] = [{ code: '9203', raison: 'sa base ne répond pas' }]

function monter(proprietes: Partial<Parameters<typeof ChoixGreffe>[0]> = {}) {
  const gestes = { choisir: vi.fn(() => new Promise<void>(() => {})), fermer: vi.fn(), deplacer: vi.fn() }
  const vue = render(
    <ChoixGreffe compte="raphael.vilain" greffes={greffes} nonProposes={nonProposes} theme="sombre"
                 {...gestes} {...proprietes} />)
  return { ...vue, ...gestes }
}

const options = () => screen.getAllByRole('option')
const choisies = () => options().filter((o) => o.getAttribute('aria-selected') === 'true').map((o) => o.textContent)
const bouton = (nom: string) => screen.getByRole('button', { name: nom }) as HTMLButtonElement

describe('ChoixGreffe', () => {
  it('liste les greffes proposes, code et nom, dans l\'ordre du poste ; le premier est preselectionne', () => {
    const { container } = monter()
    expect(screen.getByRole('heading').textContent).toBe('Choisissez votre greffe')
    expect(options().map((o) => o.textContent)).toEqual(['9201Greffe de Grenoble', '9202Greffe de Vienne'])
    expect(choisies()).toEqual(['9201Greffe de Grenoble'])
    expect(container.querySelector('.rig-choix-greffe__compte b')!.textContent).toBe('raphael.vilain')
    expect(document.documentElement.getAttribute('data-rig-theme')).toBe('sombre')
  })

  it('un clic choisit, « Ouvrir le greffe » ouvre CE greffe, une seule fois', () => {
    const { choisir } = monter()
    fireEvent.click(options()[1]!)
    expect(choisies()).toEqual(['9202Greffe de Vienne'])
    expect(choisir).not.toHaveBeenCalled()
    fireEvent.click(bouton('Ouvrir le greffe'))
    expect(choisir).toHaveBeenCalledExactlyOnceWith('9202')
    // Pendant l'ouverture : l'ecran le dit, et rien ne part une seconde fois.
    expect(screen.getByRole('status').textContent).toBe('Ouverture du greffe Greffe de Vienne…')
    expect(bouton('Ouvrir le greffe').disabled && bouton('Fermer RIG').disabled).toBe(true)
    fireEvent.click(bouton('Ouvrir le greffe'))
    fireEvent.doubleClick(options()[0]!)
    expect(choisir).toHaveBeenCalledOnce()
  })

  it('au clavier : les fleches choisissent (sans sortir de la liste), Entree ouvre', () => {
    const { choisir } = monter()
    const liste = screen.getByRole('listbox')
    fireEvent.keyDown(liste, { key: 'ArrowUp' })
    expect(choisies()).toEqual(['9201Greffe de Grenoble'])
    fireEvent.keyDown(liste, { key: 'ArrowDown' })
    fireEvent.keyDown(liste, { key: 'ArrowDown' })
    expect(choisies()).toEqual(['9202Greffe de Vienne'])
    expect(liste.getAttribute('aria-activedescendant')).toBe('rig-greffe-9202')
    fireEvent.keyDown(liste, { key: 'Enter' })
    expect(choisir).toHaveBeenCalledExactlyOnceWith('9202')
  })

  it('un double clic ouvre le greffe de la ligne', () => {
    const { choisir } = monter()
    fireEvent.doubleClick(options()[1]!)
    expect(choisir).toHaveBeenCalledExactlyOnceWith('9202')
  })

  it('« Fermer RIG » (l\'Annuler de la V2) ferme sans rien ouvrir', () => {
    const { choisir, fermer } = monter()
    fireEvent.click(bouton('Fermer RIG'))
    expect(fermer).toHaveBeenCalledOnce()
    expect(choisir).not.toHaveBeenCalled()
  })

  it('nomme les greffes du poste non proposes et leur raison ; rien quand il n\'y en a pas', () => {
    const { container, unmount } = monter()
    expect(container.querySelector('.rig-choix-greffe__non-proposes')!.textContent)
      .toBe('Non proposés9203 · sa base ne répond pas')
    unmount()
    const sans = monter({ nonProposes: [] })
    expect(sans.container.querySelector('.rig-choix-greffe__non-proposes')).toBeNull()
  })

  it('une panne du pont revient a l\'ecran : elle est dite, et l\'agent peut choisir de nouveau', async () => {
    const choisir = vi.fn().mockRejectedValueOnce(new Error('pont coupe')).mockReturnValue(new Promise<void>(() => {}))
    monter({ choisir })
    await act(async () => { fireEvent.click(bouton('Ouvrir le greffe')) })
    expect(screen.getByRole('alert').textContent).toBe('pont coupe')
    expect(bouton('Ouvrir le greffe').disabled).toBe(false)
    fireEvent.click(bouton('Ouvrir le greffe'))
    expect(choisir).toHaveBeenCalledTimes(2)
  })

  it('SOC-001 CA08 : aucune saisie, ni identifiant ni mot de passe', () => {
    const { container } = monter({ theme: 'clair' })
    expect(container.querySelectorAll('input, select, textarea')).toHaveLength(0)
    expect(document.documentElement.getAttribute('data-rig-theme')).toBe('clair')
  })

  it('le haut de la carte deplace la fenetre, au bouton gauche seulement', () => {
    const { container, deplacer } = monter()
    const tete = container.querySelector('.rig-choix-greffe__tete')!
    fireEvent.mouseDown(tete, { button: 2 })
    expect(deplacer).not.toHaveBeenCalled()
    fireEvent.mouseDown(tete, { button: 0 })
    expect(deplacer).toHaveBeenCalledOnce()
  })
})
