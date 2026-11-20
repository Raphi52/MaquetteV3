import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranRefus, titreRefus, type IdentiteRefusee } from './EcranRefus'

afterEach(() => { cleanup(); document.documentElement.removeAttribute('data-rig-theme') })

// Le jugement (ordre des refus, messages de la V2) est fait par le serveur et
// prouve par tests/SocleIdentiteCheck. Ici : l'ecran DIT ce que le serveur rend
// -- le compte, la raison, l'assistance -- et ne propose que de fermer.
const inconnu: IdentiteRefusee = {
  statut: 'inconnu',
  compte: 'jdupont',
  message: 'L\'utilisateur \'jdupont\' n\'a pas été défini dans RIG.',
  assistance: 'Merci de contacter la hotline d\'Amitel pour signaler le problème.',
}

describe('EcranRefus', () => {
  it('SOC-001 CA03 : nomme le compte, dit la raison et renvoie vers l\'assistance d\'Amitel', () => {
    const { container } = render(<EcranRefus refus={inconnu} theme="sombre" fermer={vi.fn()} deplacer={vi.fn()} />)

    expect(screen.getByRole('heading').textContent).toBe('Compte inconnu de RIG')
    expect(screen.getByRole('alert').textContent).toBe(inconnu.message)
    expect(container.querySelector('.rig-refus__compte b')!.textContent).toBe('jdupont')
    expect(container.querySelector('.rig-refus__assistance')!.textContent).toContain('hotline d\'Amitel')
    expect(document.documentElement.getAttribute('data-rig-theme')).toBe('sombre')
  })

  it('SOC-001 CA08 : aucune saisie, ni identifiant ni mot de passe ; un seul geste, fermer', () => {
    const fermer = vi.fn()
    const { container } = render(<EcranRefus refus={inconnu} theme="clair" fermer={fermer} deplacer={vi.fn()} />)

    expect(container.querySelectorAll('input, select, textarea')).toHaveLength(0)
    const boutons = screen.getAllByRole('button')
    expect(boutons.map((b) => b.textContent)).toEqual(['Fermer RIG'])
    fireEvent.click(boutons[0]!)
    expect(fermer).toHaveBeenCalledOnce()
  })

  it('le haut de la carte deplace la fenetre (pas de barre de titre native), au bouton gauche seulement', () => {
    const deplacer = vi.fn()
    const { container } = render(<EcranRefus refus={inconnu} theme="clair" fermer={vi.fn()} deplacer={deplacer} />)
    const tete = container.querySelector('.rig-refus__tete')!
    fireEvent.mouseDown(tete, { button: 2 })
    expect(deplacer).not.toHaveBeenCalled()
    fireEvent.mouseDown(tete, { button: 0 })
    expect(deplacer).toHaveBeenCalledOnce()
  })

  it('un titre par refus de la V2 (CA03 a CA06), puis la panne du serveur', () => {
    expect(['inconnu', 'sans-droit', 'desactive', 'sans-service', 'serveur-injoignable', 'autre'].map(titreRefus)).toEqual([
      'Compte inconnu de RIG', 'Aucune habilitation', 'Compte désactivé', 'Aucun service rattaché',
      'Serveur de RIG injoignable', 'Ouverture refusée',
    ])
  })

  it('sans contact d\'assistance, le bloc n\'est pas affiche', () => {
    const { container } = render(
      <EcranRefus refus={{ ...inconnu, assistance: null }} theme="clair" fermer={vi.fn()} deplacer={vi.fn()} />)
    expect(container.querySelector('.rig-refus__assistance')).toBeNull()
  })
})
