import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranAide, type VueEcranAide } from './EcranAide'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Les gestes viennent du pont (Pont/EcranAide.cs), tires des regles du clavier et du menu : tests/SocleBetaCheck verifie
// qu'ils existent (SOC-026 CA06) et que « vue » suit l'agent d'un poste a l'autre. Ici : l'ecran les montre et ouvre la fiche.
function vue(partiel: Partial<VueEcranAide> = {}): VueEcranAide {
  return {
    premiereFois: true,
    gestes: [
      { titre: 'Trouver un écran : le menu, geste › verbe', touche: null, comment: 'À gauche, les gestes du greffe.' },
      { titre: 'Rechercher partout', touche: 'Ctrl K', comment: 'De n\'importe quel écran.' },
      { titre: 'Lancer une recherche', touche: 'Entrée, F5 ou F7', comment: 'Dans un critère.' },
      { titre: 'Ouvrir une pièce dans sa propre fenêtre', touche: null, comment: '« Détacher ».' },
      { titre: 'Signaler un défaut', touche: 'Ctrl Maj D', comment: 'Ou le bouton de l\'accueil.' },
    ],
    ecranTouches: 'PROC_TOUCHES', nomEcranTouches: 'Ancienne touche, nouvelle touche',
    cheminMenu: 'Configurer › Prendre en main la V3 › Premiers pas', ...partiel,
  }
}

describe('EcranAide', () => {
  it('SOC-026 CA06 : a la premiere ouverture, montre les gestes principaux et dit qu\'elle est vue', async () => {
    const pont = installerFauxPont({ 'EcranAide.Ouvrir': () => vue() })
    render(<EcranAide ouvrir={() => {}} />)
    expect((await screen.findByRole('status')).textContent).toBe('Première ouverture')
    const lignes = [...screen.getByRole('table', { name: 'Les gestes principaux' }).querySelectorAll('tbody tr')]
    expect(lignes.map((l) => l.querySelectorAll('td')[1]!.textContent)).toEqual(['Souris', 'Ctrl K', 'Entrée, F5 ou F7', 'Souris', 'Ctrl Maj D'])
    expect(screen.getByText(/Elle ne s'ouvrira plus d'elle-même/)).toBeTruthy()
    expect(screen.getByText(/Configurer › Prendre en main la V3 › Premiers pas/)).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranAide.Ouvrir')
  })

  it('SOC-026 CA06 : le bouton ouvre la fiche « Ancienne touche, nouvelle touche »', async () => {
    installerFauxPont({ 'EcranAide.Ouvrir': () => vue() })
    const ouvrir = vi.fn()
    render(<EcranAide ouvrir={ouvrir} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Ouvrir la fiche « Ancienne touche, nouvelle touche »' }))
    expect(ouvrir).toHaveBeenCalledWith('PROC_TOUCHES')
  })

  it('rouverte depuis le menu, l\'aide ne se dit plus « premiere ouverture »', async () => {
    installerFauxPont({ 'EcranAide.Ouvrir': () => vue({ premiereFois: false }) })
    render(<EcranAide ouvrir={() => {}} />)
    await screen.findByRole('table', { name: 'Les gestes principaux' })
    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.queryByText(/Elle ne s'ouvrira plus d'elle-même/)).toBeNull()
  })
})
