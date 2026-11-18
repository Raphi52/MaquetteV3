import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render } from '@testing-library/react'
import { EcranNonMaquette } from './EcranNonMaquette'

afterEach(cleanup)

describe('EcranNonMaquette', () => {
  it('nomme la procedure et donne le motif etabli de l\'absence d\'ecran', () => {
    const { container } = render(
      <EcranNonMaquette code="PROC_CREANCE" libelle="Creances" motif="L'ancienne application ne contenait aucun ecran." />)
    expect(container.querySelector('.rig-nonmaq__kicker')!.textContent).toBe('Ecran a venir')
    expect(container.querySelector('.rig-nonmaq__titre')!.textContent).toBe('Creances')
    expect([...container.querySelectorAll('.rig-nonmaq__texte')].map((p) => p.textContent))
      .toEqual(['L\'ancienne application ne contenait aucun ecran.'])
  })

  it('sans motif (absent, nul ou blanc), dit que l\'ecran n\'est pas encore disponible', () => {
    for (const sansMotif of [{}, { motif: null }, { motif: '   ' }]) {
      const { container, unmount } = render(<EcranNonMaquette code="PROC_X" libelle="X" {...sansMotif} />)
      expect(container.querySelector('.rig-nonmaq__texte')!.textContent!.trim()).toBe('Cet ecran n\'est pas encore disponible.')
      unmount()
    }
  })
})
