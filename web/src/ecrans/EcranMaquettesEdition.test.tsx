import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranMaquettesEdition } from './EcranMaquettesEdition'

afterEach(cleanup)

// La tete de la liste moderne : « 5 maquettes · 1 Excel · 4 Word ».
const synthese = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent
const edition = (c: HTMLElement) => c.querySelector('.rig-nonmaq__texte--sourd')!.textContent!.replace(/\s+/g, ' ').trim()
const vue = (nom: string) => screen.getByRole('button', { name: new RegExp(`^${nom} \\d+$`) })

describe('EcranMaquettesEdition', () => {
  it('liste les cinq maquettes et les tables liees de la premiere', () => {
    const { container } = render(<EcranMaquettesEdition />)
    expect(synthese(container)).toBe('5 maquettes · 1 Excel · 4 Word')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('5 maquettes affichees' + 'Tables liees 12')
    expect(screen.getByText('Tables liees a MQ_KBIS')).toBeTruthy()
    expect([...container.querySelectorAll('.rig-chip')].map((c) => c.textContent)).toEqual(['DOSSIER', 'DEPOT', 'CLIENT'])
    expect(edition(container)).toBe('Edition : Greffe - Accueil — A4 en-tete — Lot 1 - courrier simple — recto-verso')
    // Le type de document en pastille.
    const pastille = container.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')[3]!.querySelector('.rig-lm-pastille')!
    expect([pastille.textContent, pastille.className]).toEqual(['Extrait', 'rig-lm-pastille rig-lm-t-bleu'])
  })

  it('les quatre listes de lot ont le meme catalogue et leur propre choix', () => {
    render(<EcranMaquettesEdition />)
    const lots = ['Lot standard', 'Lot LRAR', 'Lot iLRAR', 'Lot de substitution']
      .map((l) => screen.getByLabelText(l) as HTMLSelectElement)
    expect(lots.map((s) => s.value)).toEqual(['Lot 1 - courrier simple', 'Lot 3 - recommande', 'Lot 2 - courrier suivi', '(aucun)'])
    for (const s of lots) expect(s.options).toHaveLength(5)
  })

  it('les parametres d\'edition suivent les choix : lot, recto-verso, agrafage', () => {
    const { container } = render(<EcranMaquettesEdition />)
    fireEvent.change(screen.getByLabelText('Lot standard'), { target: { value: 'Lot 4 - depot guichet' } })
    fireEvent.click(screen.getByRole('button', { name: 'Recto-verso' }))
    fireEvent.click(screen.getByRole('button', { name: 'Agrafage' }))
    expect(edition(container)).toBe('Edition : Greffe - Accueil — A4 en-tete — Lot 4 - depot guichet — agrafe')
  })

  it('filtre par ecran d\'origine (liste triee) et ouvre une maquette', () => {
    const { container } = render(<EcranMaquettesEdition />)
    expect([...(screen.getByLabelText('Ecran d\'origine') as HTMLSelectElement).options].map((o) => o.textContent))
      .toEqual(['Tous', 'Archivage', 'Audiences', 'Courrier du jour', 'Demandes recues', 'Factures'])
    fireEvent.change(screen.getByLabelText('Type de document'), { target: { value: 'Courrier' } })
    expect(synthese(container)).toBe('2 maquettes · 2 Word')
    fireEvent.click(screen.getByText('MQ_CONVOC'))
    expect(screen.getByText('Tables liees a MQ_CONVOC')).toBeTruthy()
    expect(container.querySelectorAll('tbody tr')[1]!.querySelectorAll('td')[5]!.textContent).toBe('22/07/2026')
  })

  it('les vues sont le critere Type de document, avec le compte de chacune dans l\'ecran d\'origine choisi', () => {
    const { container } = render(<EcranMaquettesEdition />)
    expect([...container.querySelectorAll('.rig-lm-vue')].map((v) => v.textContent))
      .toEqual(['Toutes 5', 'Bordereau 1', 'Courrier 2', 'Extrait 1', 'Facture 1'])
    expect(vue('Toutes').getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(vue('Courrier'))
    expect((screen.getByLabelText('Type de document') as HTMLSelectElement).value).toBe('Courrier')
    expect(vue('Courrier').getAttribute('aria-pressed')).toBe('true')
    expect(synthese(container)).toBe('2 maquettes · 2 Word')
    fireEvent.change(screen.getByLabelText('Ecran d\'origine'), { target: { value: 'Audiences' } })
    expect(vue('Courrier').textContent).toBe('Courrier 1')
    expect(vue('Extrait').textContent).toBe('Extrait 0')
  })

  it('« Aller a une maquette… » filtre la liste affichee ; Entree choisit la maquette restee seule', () => {
    const { container } = render(<EcranMaquettesEdition />)
    const champ = screen.getByLabelText('Aller a une maquette…')
    fireEvent.change(champ, { target: { value: 'facture' } })
    expect([...container.querySelectorAll('tbody tr td:first-child')].map((td) => td.textContent)).toEqual(['MQ_FACT'])
    expect(synthese(container)).toBe('1 maquette · 1 Excel')
    expect((screen.getByLabelText('Type de document') as HTMLSelectElement).value).toBe('')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(screen.getByText('Tables liees a MQ_FACT')).toBeTruthy()
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 5 maquettes ne contient « zzz ».')).toBeTruthy()
  })
})
