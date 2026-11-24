import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranArchivage } from './EcranArchivage'

afterEach(cleanup)

// La tete de la liste moderne : « 4 retenues sur 5 · 58 pieces · bordereau non prepare ».
const synthese = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent!.trim()
const vue = (nom: string) => screen.getByRole('button', { name: new RegExp(`^${nom} \\d+$`) })

describe('EcranArchivage', () => {
  it('retient les radiations de 2012 a 2016, bordereau non prepare', () => {
    const { container } = render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    expect(synthese(container)).toBe('4 retenues sur 5 · 58 pieces · bordereau non prepare')
    const premiere = container.querySelectorAll('tbody tr')[0]!
    const cellules = premiere.querySelectorAll('td')
    expect(cellules).toHaveLength(6)
    // Dossier : avatar d'initiales, denomination en gras, numero de dossier en gris.
    expect(cellules[0]!.querySelector('.rig-lm-avatar')!.textContent).toBe('MC')
    expect(cellules[0]!.querySelector('.rig-lm-identite__nom')!.textContent).toBe('SARL MENUISERIE COLLIN')
    expect(cellules[0]!.querySelector('.rig-lm-identite__objet')!.textContent).toBe('1998B00214')
    expect(cellules[1]!.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-bleu')
    expect([...cellules].slice(1).map((td) => td.textContent)).toEqual(['PM', '12/03/2014', '04/11/2013', '18', 'A archiver'])
    expect(premiere.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('4 formalites affichees' + 'Pieces 58')
  })

  it('« Preparer le bordereau » passe les formalites sur bordereau', () => {
    const { container } = render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Preparer le bordereau' }))
    expect(synthese(container)).toBe('4 retenues sur 5 · 58 pieces · bordereau prepare')
    const etats = [...container.querySelectorAll('.rig-lm-etat')]
    expect(etats).toHaveLength(4)
    expect(etats.every((e) => e.className === 'rig-lm-etat rig-lm-t-vert' && e.textContent === 'Sur bordereau')).toBe(true)
  })

  it('filtre par type RCS, par dates, et borne au nombre de dossiers', () => {
    const { container } = render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    fireEvent.change(screen.getByLabelText('Type RCS'), { target: { value: 'PP' } })
    expect(synthese(container)).toBe('2 retenues sur 5 · 16 pieces · bordereau non prepare')
    fireEvent.change(screen.getByLabelText('Type RCS'), { target: { value: '' } })

    fireEvent.change(screen.getByLabelText('Date maxi.'), { target: { value: '' } })
    expect(synthese(container)).toBe('5 retenues sur 5 · 69 pieces · bordereau non prepare')

    fireEvent.change(screen.getByLabelText('Nombre de dossiers'), { target: { value: '2' } })
    expect(synthese(container)).toBe('2 retenues sur 5 · 42 pieces · bordereau non prepare')

    fireEvent.change(screen.getByLabelText('Date mini.'), { target: { value: '2030-01-01' } })
    expect(screen.getByText('Aucune formalite dans cette plage de radiation.')).toBeTruthy()
  })

  it('les vues sont le critere Type RCS, avec le compte de chacune', () => {
    const { container } = render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    expect([...container.querySelectorAll('.rig-lm-vue')].map((v) => v.textContent))
      .toEqual(['Toutes 4', 'Personnes morales 2', 'Personnes physiques 2'])
    expect(vue('Toutes').getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(vue('Personnes morales'))
    expect((screen.getByLabelText('Type RCS') as HTMLSelectElement).value).toBe('PM')
    expect(vue('Personnes morales').getAttribute('aria-pressed')).toBe('true')
    expect(synthese(container)).toBe('2 retenues sur 5 · 42 pieces · bordereau non prepare')
    // Le critere pose a la main allume la vue qui lui correspond.
    fireEvent.change(screen.getByLabelText('Type RCS'), { target: { value: 'PP' } })
    expect(vue('Personnes physiques').getAttribute('aria-pressed')).toBe('true')
  })

  it('« Aller a une formalite… » filtre la liste affichee ; Entree ouvre le dossier reste seul', () => {
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranArchivage ouvrirDossier={ouvrirDossier} />)
    const champ = screen.getByLabelText('Aller a une formalite…')
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)

    fireEvent.change(champ, { target: { value: 'martin' } })
    expect([...container.querySelectorAll('tbody tr b')].map((b) => b.textContent)).toEqual(['MARTIN Claire'])
    expect(synthese(container)).toBe('1 retenue sur 5 · 9 pieces · bordereau non prepare')
    // Les criteres ne bougent pas.
    expect((screen.getByLabelText('Type RCS') as HTMLSelectElement).value).toBe('')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(ouvrirDossier).toHaveBeenCalledWith('1999A00456')

    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 4 formalites ne contient « zzz ».')).toBeTruthy()
  })

  it('une formalite cliquee remonte son dossier', () => {
    const ouvrirDossier = vi.fn()
    render(<EcranArchivage ouvrirDossier={ouvrirDossier} />)
    fireEvent.click(screen.getByText('DUPONT Jean'))
    expect(ouvrirDossier).toHaveBeenCalledWith('1995A00120')
  })
})
