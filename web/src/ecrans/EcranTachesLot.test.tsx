import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranTachesLot, type VueTachesLot } from './EcranTachesLot'
import { installerFauxDotNet, retirerFauxDotNet } from '../test/faux-dotnet'

afterEach(() => { cleanup(); retirerFauxDotNet() })

// Les regles (lots, saisie dans la trace DU dossier, cloture par dossier) vivent
// dans Pont/EcranTachesLot.cs, executees par tests/GedCheck. Ici : l'ecran lit la
// vue de la session que la coquille a ouverte, et lui remonte chaque geste.
const deno = { code: 'DENO', titre: 'Changer la denomination', dossiers: 2, aRepetition: false, actif: true }
const dirigeant = { code: 'DSTE', titre: 'Changer un dirigeant', dossiers: 1, aRepetition: true, actif: false }

function vueDeno(valeur = ''): VueTachesLot {
  return {
    session: 's1',
    lots: [deno, dirigeant],
    recette: {
      code: 'DENO', titre: 'Changer la denomination', evenements: 'DENO', aRepetition: false, libelleLigne: 'ligne',
      champs: [{ libelle: 'Denomination', colonne: 'INSCRIT_PM.DENOMINATION' }], effets: 2,
    },
    dossiers: [
      { dossier: '004151', nom: 'TRANSNORD SA', valeurs: [valeur], restants: valeur ? 0 : 1, effetsRestants: 2, pret: false },
      { dossier: '004176', nom: 'SCI DU PARC', valeurs: ['NOUVEAU NOM'], restants: 0, effetsRestants: 0, pret: true },
    ],
    dernierMessage: '',
  }
}

function vueDirigeant(): VueTachesLot {
  return {
    ...vueDeno(),
    lots: [{ ...deno, actif: false }, { ...dirigeant, actif: true }],
    recette: { code: 'DSTE', titre: 'Changer un dirigeant', evenements: 'DSTE / DPER', aRepetition: true,
               libelleLigne: 'dirigeant', champs: [], effets: 0 },
    dossiers: [{ dossier: '004179', nom: 'MARTIN EURL', valeurs: [], restants: 0, effetsRestants: 0, pret: true }],
  }
}

describe('EcranTachesLot', () => {
  it('lit la vue de la session ouverte par la coquille : lots, en-tete, colonnes de la recette', async () => {
    const dotnet = installerFauxDotNet({ 'EcranTachesLot.Vue': () => vueDeno() })
    const { container } = render(<EcranTachesLot session="s1" version={0} />)

    await screen.findByText('TRANSNORD SA')
    expect(dotnet.invokeMethodAsync).toHaveBeenCalledWith('RigV3Desktop', 'EcranTachesLot.Vue', 's1')
    const lots = container.querySelectorAll('aside button.rig-alerte')
    expect(lots[0]!.className).toBe('rig-alerte rig-alerte--lien rig-alerte--actif')
    expect(lots[0]!.getAttribute('title')).toBe('Traiter les 2 dossiers en lot')
    expect(lots[1]!.querySelector('.rig-alerte__detail')!.textContent).toBe('1 dossier(s) · saisie ligne par ligne')
    expect(lots[1]!.getAttribute('title')).toBe('Cette tache se saisit dossier par dossier')
    expect(container.querySelector('.rig-resultats-proc__entete .rig-kicker')!.textContent)
      .toBe('Changer la denomination ·  DENO')
    expect(screen.getByText('Denomination').getAttribute('title')).toBe('INSCRIT_PM.DENOMINATION')

    const [incomplet, pret] = [...container.querySelectorAll('tbody tr')]
    expect(incomplet!.getAttribute('title')).toBe('1 champ(s) et 2 publication(s) a traiter')
    expect(incomplet!.querySelectorAll('td')[3]!.textContent).toBe('0 / 2 tout')
    expect((incomplet!.querySelector('.rig-tache__terminer') as HTMLButtonElement).disabled).toBe(true)
    expect(pret!.getAttribute('title')).toBe('Pret a terminer')
    expect((pret!.querySelector('.rig-tache__terminer') as HTMLButtonElement).disabled).toBe(false)
  })

  it('remonte la saisie (a la sortie du champ), la propagation et la cloture du dossier', async () => {
    const dotnet = installerFauxDotNet({
      'EcranTachesLot.Vue': () => vueDeno(),
      'EcranTachesLot.Agir': (_s: unknown, geste: unknown) =>
        geste === 'Terminer' ? { ...vueDeno('X'), dernierMessage: '004176 termine. Son alerte ne reviendra plus.' } : vueDeno('X'),
    })
    const { container } = render(<EcranTachesLot session="s1" version={0} />)
    await screen.findByText('TRANSNORD SA')

    const champ = container.querySelector('tbody tr input') as HTMLInputElement
    fireEvent.change(champ, { target: { value: 'X' } })
    await waitFor(() => expect(dotnet.invokeMethodAsync).toHaveBeenCalledWith(
      'RigV3Desktop', 'EcranTachesLot.Agir', 's1', 'Ecrire', { dossier: '004151', champ: 0, valeur: 'X' }))

    fireEvent.click(screen.getAllByRole('button', { name: 'tout' })[0]!)
    await waitFor(() => expect(dotnet.invokeMethodAsync).toHaveBeenLastCalledWith('RigV3Desktop', 'EcranTachesLot.Agir', 's1', 'ToutPropager', '004151'))

    fireEvent.click(screen.getAllByRole('button', { name: 'Terminer' })[1]!)
    await screen.findByText('004176 termine. Son alerte ne reviendra plus.')
    expect(dotnet.invokeMethodAsync).toHaveBeenLastCalledWith('RigV3Desktop', 'EcranTachesLot.Agir', 's1', 'Terminer', '004176')
  })

  it('une tache a repetition est REFUSEE en lot, avec son motif', async () => {
    installerFauxDotNet({ 'EcranTachesLot.Vue': () => vueDeno(), 'EcranTachesLot.Agir': () => vueDirigeant() })
    const { container } = render(<EcranTachesLot session="s1" version={0} />)
    fireEvent.click(await screen.findByText('Changer un dirigeant'))
    await waitFor(() => expect(container.querySelector('table')).toBeNull())
    expect(container.querySelector('.rig-resultats-proc .rig-vide')!.textContent)
      .toBe('« Changer un dirigeant » se saisit ligne par ligne (plusieurs dirigeants par dossier). Ouvrir chaque affaire depuis la file d\'alertes.')
  })

  it('une nouvelle version (la coquille a change de recette) relit la vue', async () => {
    const vues = [vueDeno(), vueDirigeant()]
    const dotnet = installerFauxDotNet({ 'EcranTachesLot.Vue': () => vues.shift() })
    const { rerender } = render(<EcranTachesLot session="s1" version={0} />)
    await screen.findByText('TRANSNORD SA')
    rerender(<EcranTachesLot session="s1" version={1} />)
    await screen.findByText(/se saisit/)
    expect(dotnet.invokeMethodAsync).toHaveBeenCalledTimes(2)
  })
})
