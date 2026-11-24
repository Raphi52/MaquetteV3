import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranTachesLot, type VueTachesLot } from './EcranTachesLot'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { morceaux } from '../test/morceaux'

afterEach(() => { cleanup(); retirerFauxPont() })
// fix-ok: tests suivis sur la liste moderne (lot 6 des listes) : tete, avatar, point d'etat, pied, filtre Ctrl K -- pas un correctif.

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
    const pont = installerFauxPont({ 'EcranTachesLot.Vue': () => vueDeno() })
    const { container } = render(<EcranTachesLot session="s1" version={0} />)

    await screen.findByText('TRANSNORD SA')
    expect(pont.appels).toHaveBeenCalledWith('EcranTachesLot.Vue', 's1')
    const lots = container.querySelectorAll('aside button.rig-alerte')
    expect(lots[0]!.className).toBe('rig-alerte rig-alerte--lien rig-alerte--actif')
    expect(lots[0]!.getAttribute('title')).toBe('Traiter les 2 dossiers en lot')
    expect(lots[1]!.querySelector('.rig-alerte__detail')!.textContent).toBe('1 dossier(s) · saisie ligne par ligne')
    expect(morceaux(lots[0]!.querySelector('.rig-alerte__detail'))).toEqual(['2', ' dossier(s) '])
    expect(morceaux(lots[1]!.querySelector('.rig-alerte__detail')))
      .toEqual(['1', ' dossier(s) ', '· saisie ligne par ligne'])
    expect(lots[1]!.getAttribute('title')).toBe('Cette tache se saisit dossier par dossier')
    // La tete de la liste moderne : le titre de la recette, puis ses evenements et les comptes du lot.
    expect(container.querySelector('.rig-lm-titre')!.textContent).toBe('Changer la denomination')
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('DENO · 2 dossier(s) · 1 pret(s) a terminer')
    expect(screen.getByText('Denomination').getAttribute('title')).toBe('INSCRIT_PM.DENOMINATION')

    const [incomplet, pret] = [...container.querySelectorAll('tbody tr')]
    expect(incomplet!.getAttribute('title')).toBe('1 champ(s) et 2 publication(s) a traiter')
    expect(incomplet!.querySelectorAll('td')[3]!.textContent).toBe('0 / 2 tout')
    expect(morceaux(incomplet!.querySelectorAll('td')[3]!)).toEqual(['0', ' / ', '2', ' ', '<button>'])
    expect((incomplet!.querySelector('.rig-tache__terminer') as HTMLButtonElement).disabled).toBe(true)
    expect(pret!.getAttribute('title')).toBe('Pret a terminer')
    expect((pret!.querySelector('.rig-tache__terminer') as HTMLButtonElement).disabled).toBe(false)
    // La societe en avatar + nom, son numero en gris ; l'etat du dossier en point colore.
    expect(incomplet!.querySelectorAll('td')[0]!.textContent).toBe('TTRANSNORD SA004151')
    expect(incomplet!.querySelectorAll('td')[2]!.textContent).toBe('3 a traiter')
    expect(incomplet!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
    expect(pret!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
    expect(container.querySelector('.rig-lm-pied')!.textContent)
      .toBe('2 dossier(s) affiche(s)Prets 1Terminer clot la tache sur ce seul dossier, quand ses champs et ses publications sont faits.')
  })

  it('« Aller a un dossier… » (Ctrl K) filtre les lignes affichees, sans geste vers la session', async () => {
    const pont = installerFauxPont({ 'EcranTachesLot.Vue': () => vueDeno() })
    const { container } = render(<EcranTachesLot session="s1" version={0} />)
    await screen.findByText('TRANSNORD SA')
    const appels = pont.appels.mock.calls.length
    const champ = screen.getByLabelText('Aller a un dossier…')
    expect(fireEvent.keyDown(window, { key: 'k', ctrlKey: true })).toBe(false)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: '004176' } })
    expect([...container.querySelectorAll('tbody tr')].map((tr) => tr.querySelector('td')!.textContent)).toEqual(['PSCI DU PARC004176'])
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('DENO · 1 dossier(s) · 1 pret(s) a terminer')
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 2 dossiers ne contient « zzz ».')).toBeTruthy()
    expect(pont.appels.mock.calls.length).toBe(appels)
  })

  it('le filtre ne vaut que pour sa tache : une autre tache choisie repart sans filtre', async () => {
    const autre: VueTachesLot = {
      ...vueDeno(),
      recette: { ...vueDeno().recette!, code: 'SIEGE', titre: 'Transferer le siege', evenements: 'SIEGE' },
      dossiers: [{ dossier: '004188', nom: 'NORD MARINE SAS', valeurs: [''], restants: 1, effetsRestants: 1, pret: false }],
    }
    installerFauxPont({ 'EcranTachesLot.Vue': () => vueDeno(), 'EcranTachesLot.Agir': () => autre })
    const { container } = render(<EcranTachesLot session="s1" version={0} />)
    await screen.findByText('TRANSNORD SA')
    fireEvent.change(screen.getByLabelText('Aller a un dossier…'), { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 2 dossiers ne contient « zzz ».')).toBeTruthy()
    fireEvent.click(screen.getByText('Changer un dirigeant'))
    await screen.findByText('NORD MARINE SAS')
    expect((screen.getByLabelText('Aller a un dossier…') as HTMLInputElement).value).toBe('')
    expect(container.querySelectorAll('tbody tr')).toHaveLength(1)
  })

  it('remonte la saisie (a la sortie du champ), la propagation et la cloture du dossier', async () => {
    const pont = installerFauxPont({
      'EcranTachesLot.Vue': () => vueDeno(),
      'EcranTachesLot.Agir': (_s: unknown, geste: unknown) =>
        geste === 'Terminer' ? { ...vueDeno('X'), dernierMessage: '004176 termine. Son alerte ne reviendra plus.' } : vueDeno('X'),
    })
    const { container } = render(<EcranTachesLot session="s1" version={0} />)
    await screen.findByText('TRANSNORD SA')

    const champ = container.querySelector('tbody tr input') as HTMLInputElement
    fireEvent.change(champ, { target: { value: 'X' } })
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith(
      'EcranTachesLot.Agir', 's1', 'Ecrire', { dossier: '004151', champ: 0, valeur: 'X' }))

    fireEvent.click(screen.getAllByRole('button', { name: 'tout' })[0]!)
    await waitFor(() => expect(pont.appels).toHaveBeenLastCalledWith('EcranTachesLot.Agir', 's1', 'ToutPropager', '004151'))

    fireEvent.click(screen.getAllByRole('button', { name: 'Terminer' })[1]!)
    await screen.findByText('004176 termine. Son alerte ne reviendra plus.')
    expect(pont.appels).toHaveBeenLastCalledWith('EcranTachesLot.Agir', 's1', 'Terminer', '004176')
  })

  it('une tache a repetition est REFUSEE en lot, avec son motif', async () => {
    installerFauxPont({ 'EcranTachesLot.Vue': () => vueDeno(), 'EcranTachesLot.Agir': () => vueDirigeant() })
    const { container } = render(<EcranTachesLot session="s1" version={0} />)
    fireEvent.click(await screen.findByText('Changer un dirigeant'))
    await waitFor(() => expect(container.querySelector('table')).toBeNull())
    expect(container.querySelector('.rig-resultats-proc .rig-vide')!.textContent)
      .toBe('« Changer un dirigeant » se saisit ligne par ligne (plusieurs dirigeants par dossier). Ouvrir chaque affaire depuis la file d\'alertes.')
    expect(morceaux(container.querySelector('.rig-resultats-proc .rig-vide'))).toEqual([
      '« ', 'Changer un dirigeant', ' » se saisit ', '<b>', ' (plusieurs ', 'dirigeant',
      's par dossier). Ouvrir chaque affaire depuis la file d\'alertes.'])
  })

  it('une nouvelle version (la coquille a change de recette) relit la vue', async () => {
    const vues = [vueDeno(), vueDirigeant()]
    const pont = installerFauxPont({ 'EcranTachesLot.Vue': () => vues.shift() })
    const { rerender } = render(<EcranTachesLot session="s1" version={0} />)
    await screen.findByText('TRANSNORD SA')
    rerender(<EcranTachesLot session="s1" version={1} />)
    await screen.findByText(/se saisit/)
    expect(pont.appels).toHaveBeenCalledTimes(2)
  })
})
