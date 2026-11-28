import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranActeurs, type VueActeurs } from './EcranActeurs'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Valeurs attendues : celles de l'ecran Blazor (EcranActeurs.razor) : 5 acteurs dont 4 actifs,
// LEROY (mandataire) choisi au depart.
const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value
const puces = (c: HTMLElement) => [...c.querySelectorAll('.rig-chip')].map((p) => p.textContent)

// Sans serveur, le pont rend le mode « demo » : l'ecran montre ses acteurs d'exemple, marques (Pont/EcranActeursBase.cs).
const Demo: VueActeurs = { mode: 'demo', message: null, acteurs: [], total: 0 }
async function ouvrir(v: VueActeurs = Demo) {
  const pont = installerFauxPont({ 'EcranActeurs.Acteurs': () => v })
  const r = render(<EcranActeurs />)
  await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranActeurs.Acteurs'))
  await vi.waitFor(() => expect(r.container.querySelector('.rig-resultats-proc__compte')!.textContent).not.toBe('aucun acteur selectionne'))
  return r
}

/** Un acteur lu en base, aux champs du pont. */
const lu = (abrege: string, type: string, cle: string, actif = true) => ({
  abrege, civilite: 'MR', nom: abrege + 'NOM', prenom: '', complementNom: '', adresse: '', tel: '', fax: '', mail: '', qualif: '', statut: '',
  chambre: '', section: '', debut: '', fin: null, bureau: '', nomCoffre: '', actif, type, siren: '', utilisateur: '', cle,
})

describe('EcranActeurs', () => {
  it('affiche les acteurs actifs et la fiche du mandataire LEROY', async () => {
    const { container } = await ouvrir()
    expect(screen.getByText('Resultats (4)')).toBeTruthy()
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('LEROY')
    expect(valeur('Debut de fonction')).toBe('08/01/2018')
    expect(valeur('Fin de fonction')).toBe('')
    expect(valeur('SIREN')).toBe('402 118 553')
    expect(puces(container)).toEqual([
      'Administrateur judiciaire', 'Commissaire a l\'execution du plan',
      'Commissaire priseur', 'Mandataire conciliation', 'RDC liquidation MDJ',
    ])
    // Statuts : Distinct().Order() apres « Tous ».
    expect([...(screen.getByLabelText('Statut', { selector: 'select' }) as HTMLSelectElement).options].map((o) => o.text))
      .toEqual(['Tous', 'En fonction', 'Retiree'])
    expect(screen.getByRole('note').textContent).toContain('de démonstration')
  })

  it('le type commande les champs : juge, greffier, expert inactif', async () => {
    const { container } = await ouvrir()
    fireEvent.click(screen.getByRole('button', { name: 'PETIT — PETIT' }))
    expect(screen.queryByLabelText('SIREN')).toBeNull()
    expect(puces(container)).toHaveLength(12)

    fireEvent.click(screen.getByRole('button', { name: 'DUVAL — DUVAL' }))
    expect(valeur('Compte utilisateur lie')).toBe('hduval')

    fireEvent.click(screen.getByRole('button', { name: 'Acteurs actifs uniquement' }))
    expect(screen.getByText('Resultats (5)')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'MOREAU — MOREAU' }))
    expect(valeur('Fin de fonction')).toBe('31/12/2024')
    expect(screen.getByText('Inactif').className).toBe('rig-etat rig-etat--attente')
  })

  // Branche sur la base (lot 14) : les acteurs du pont, sans un exemple ; deux acteurs de meme abrege (deux tables) restent deux.
  it('en base, les acteurs lus et eux seuls, chacun par sa cle ; en panne, le motif et aucun acteur', async () => {
    const enBase: VueActeurs = { mode: 'serveur', message: null, total: 3,
      acteurs: [lu('J14A', 'Juge', 'Juge-1'), lu('J14A', 'Mandataire', 'Mandataire-1'), lu('X14', 'Expert', 'Expert-2', false)] }
    await ouvrir(enBase)
    expect(screen.getByText('Resultats (2)')).toBeTruthy()
    expect(screen.queryByText(/LEROY/)).toBeNull()
    expect(screen.queryByRole('note')).toBeNull()
    fireEvent.click(screen.getAllByRole('button', { name: 'J14A — J14ANOM' })[1]!)
    expect(valeur('Type d\'acteur')).toBe('Mandataire')
    cleanup()
    retirerFauxPont()
    await ouvrirPanne()
  })

  // « Enregistrer les coordonnees » (lot 9 du parcours complet) : en base, telephone, fax et courriel partent au pont avec la cle de
  // la ligne ; fait, l'ecran relit la base ; refuse, le motif de RIG v2. La demonstration reste en lecture, sans bouton.
  it('en base, « Enregistrer les coordonnees » envoie la cle et les coordonnees, puis relit ; un refus est dit', async () => {
    let fait = false
    const enregistrer = vi.fn((...args: unknown[]) => {
      const tel = args[1] as string
      if (tel === '12') return { fait: false, message: '« Téléphone » : Numéro de téléphone invalide.', sansEcrivain: false, ids: [] }
      fait = true
      return { fait: true, message: 'Coordonnées du juge J14ANOM enregistrées.', sansEcrivain: false, ids: ['Juge-1'] }
    })
    const pont = installerFauxPont({
      'EcranActeurs.Acteurs': () => ({ mode: 'serveur', message: null, total: 1,
        acteurs: [{ ...lu('J14A', 'Juge', 'Juge-1'), tel: fait ? '0123456789' : '' }] }),
      'EcranActeurs.EnregistrerCoordonnees': enregistrer,
    })
    render(<EcranActeurs />)
    await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranActeurs.Acteurs'))
    const tel = await screen.findByLabelText('Telephone') as HTMLInputElement
    expect(tel.readOnly).toBe(false)
    fireEvent.change(tel, { target: { value: '12' } })
    fireEvent.click(screen.getByRole('button', { name: 'Enregistrer les coordonnees' }))
    expect((await screen.findByRole('alert')).textContent).toBe('Refus « Téléphone » : Numéro de téléphone invalide.')
    fireEvent.change(screen.getByLabelText('Telephone'), { target: { value: '0123456789' } })
    fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: 'juge@exemple.fr' } })
    fireEvent.click(screen.getByRole('button', { name: 'Enregistrer les coordonnees' }))
    expect((await screen.findByRole('status')).textContent).toBe('Coordonnées du juge J14ANOM enregistrées.')
    expect(enregistrer).toHaveBeenLastCalledWith('Juge-1', '0123456789', '', 'juge@exemple.fr')
    expect(pont.appels.mock.calls.filter((x) => x[0] === 'EcranActeurs.Acteurs')).toHaveLength(2)
    await vi.waitFor(() => expect(valeur('Telephone')).toBe('0123456789'))
    cleanup()
    retirerFauxPont()
    await ouvrir()
    expect((screen.getByLabelText('Telephone') as HTMLInputElement).readOnly).toBe(true)
    expect(screen.queryByRole('button', { name: 'Enregistrer les coordonnees' })).toBeNull()
  })
})

async function ouvrirPanne() {
  const panne: VueActeurs = { mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.', acteurs: [], total: 0 }
  const pont = installerFauxPont({ 'EcranActeurs.Acteurs': () => panne })
  render(<EcranActeurs />)
  await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranActeurs.Acteurs'))
  await vi.waitFor(() => expect(screen.getByText(/ne répond pas/)).toBeTruthy())
  expect(screen.getByText('Resultats (0)')).toBeTruthy()
}
