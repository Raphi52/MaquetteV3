import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { Onglets } from './Onglets'
import { morceaux } from '../test/morceaux'
import type { GestePlan, OngletBarreVue } from './types'

afterEach(cleanup)

// La pastille du verbe (« Onglet fondu » n°3, 19/11/2026) : chaque onglet porte
// la pastille du verbe du menu qui range sa vue (MenuAccueil.VerbeDe, C#).
const onglets: OngletBarreVue[] = [
  { code: 'PROC_ARRIVEE', libelle: 'Courrier du jour', actif: true, detache: false, glisse: false,
    verbe: { titre: 'Ouvrir', teinte: 'rcs', signe: '+' } },
  { code: 'PROC_AUDIENCE', libelle: 'Audiences', actif: false, detache: false, glisse: false,
    verbe: { titre: 'Surveiller', teinte: 'surv', signe: '!' } },
  { code: 'PROC_TACHES_LOT', libelle: 'Taches en lot', actif: false, detache: false, glisse: false, verbe: null },
]

function rendre() {
  const plan = vi.fn<GestePlan>()
  const r = render(<Onglets onglets={onglets} finsDeGlisse={0} plan={plan} />)
  return { ...r, plans: () => plan.mock.calls.map(([nom, valeur]) => [nom, valeur ?? null]) }
}

describe('Onglets : la pastille du verbe', () => {
  it('pastille, titre, croix ; la pastille est celle du menu, a la teinte de son verbe', () => {
    const { container } = rendre()
    const arrivee = container.querySelector('[data-rig-onglet="PROC_ARRIVEE"]')!
    expect(morceaux(arrivee)).toEqual(['<span>', '<span>', '<button>'])
    const pastille = arrivee.querySelector('.rig-vonglet__verbe')!
    expect(pastille).toBe(arrivee.firstElementChild)
    expect(pastille.className).toBe('rig-menu__pastille rig-vonglet__verbe rig-vonglet__verbe--rcs')
    expect(pastille.textContent).toBe('+')
    const audiences = container.querySelector('[data-rig-onglet="PROC_AUDIENCE"] .rig-vonglet__verbe')!
    expect([audiences.textContent, audiences.classList.contains('rig-vonglet__verbe--surv')]).toEqual(['!', true])
  })

  it('son survol nomme le verbe ; l\'onglet garde son aide', () => {
    const { container } = rendre()
    const arrivee = container.querySelector('[data-rig-onglet="PROC_ARRIVEE"]')!
    expect(arrivee.querySelector('.rig-vonglet__verbe')!.getAttribute('title')).toBe('Ouvrir')
    expect(arrivee.getAttribute('title')).toContain('Glissez l onglet')
  })

  it('une vue hors du menu n\'a pas de pastille', () => {
    const { container } = rendre()
    const lot = container.querySelector('[data-rig-onglet="PROC_TACHES_LOT"]')!
    expect(lot.querySelector('.rig-vonglet__verbe')).toBeNull()
    expect(morceaux(lot)).toEqual(['<span>', '<button>'])
  })

  it('un clic sur la pastille active l\'onglet, comme un clic sur son titre', () => {
    const { container, plans } = rendre()
    fireEvent.click(container.querySelector('[data-rig-onglet="PROC_AUDIENCE"] .rig-vonglet__verbe')!)
    expect(plans()).toEqual([['ongletActiver', 'PROC_AUDIENCE']])
  })
})
