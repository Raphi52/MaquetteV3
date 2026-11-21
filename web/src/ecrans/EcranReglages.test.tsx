import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranReglages, trace, type ReglageGreffe, type VueEcranReglages } from './EcranReglages'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Les regles (droit de modifier, auteur et date poses, journal, 404 sur un reglage absent) vivent dans le serveur
// et sont prouvees par tests/SocleGreffesCheck. Ici : l'ecran montre les reglages et remonte chaque modification.
function reglage(partiel: Partial<ReglageGreffe> = {}): ReglageGreffe {
  return {
    code: 'EXPRESSO_LC_ORDRE_NUM', libelle: 'Ordre d\'attribution des n° de chèques pour les LC', valeur: 'C',
    utilisation: 'RIG_FACTU_EXPRESSO : Lettres_chèques', auteurCreation: 'rig', dateCreation: '2024-03-01T09:00:00',
    auteurModif: 'INTRANET\\responsable', dateModif: '2026-11-19T15:56:00', ...partiel,
  }
}

function vue(partiel: Partial<VueEcranReglages> = {}): VueEcranReglages {
  return {
    mode: 'serveur', message: null, echec: false, greffe: '3801', selection: null,
    reglages: [reglage(), reglage({ code: 'PROC_RELCLI_ultCheckBoxLettre', libelle: 'Avec lettre', valeur: 'True', utilisation: null, auteurModif: null, dateModif: null })],
    ...partiel,
  }
}

describe('EcranReglages', () => {
  it('SOC-003 CA07 : chaque reglage montre qui l\'a modifie et quand ; la valeur reste le texte de la V2', async () => {
    installerFauxPont({ 'EcranReglages.Ouvrir': () => vue() })
    render(<EcranReglages />)
    expect(await screen.findByText('2 réglage(s) — greffe 3801')).toBeTruthy()
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes[0]!.textContent).toContain('INTRANET\\responsable')
    expect(lignes[0]!.textContent).toContain('19/11/2026')
    expect(lignes[1]!.textContent).toContain('True')
    fireEvent.click(lignes[0]!)
    expect(screen.getByText(/Dernière modification : par INTRANET\\responsable le 19\/11\/2026/)).toBeTruthy()
    expect(screen.getByText('Utilisation : RIG_FACTU_EXPRESSO : Lettres_chèques')).toBeTruthy()
  })

  it('SOC-003 T03 : modifie la valeur du reglage choisi et affiche le compte rendu', async () => {
    const pont = installerFauxPont({
      'EcranReglages.Ouvrir': () => vue(),
      'EcranReglages.Modifier': () => vue({ message: 'Réglage EXPRESSO_LC_ORDRE_NUM modifié : « D », par raphael.vilain.', selection: 'EXPRESSO_LC_ORDRE_NUM',
        reglages: [reglage({ valeur: 'D', auteurModif: 'raphael.vilain' })] }),
    })
    render(<EcranReglages />)
    await screen.findByText('2 réglage(s) — greffe 3801')
    const bouton = screen.getByRole('button', { name: 'Enregistrer' }) as HTMLButtonElement
    expect(bouton.disabled).toBe(true)
    fireEvent.click(screen.getAllByRole('row')[1]!)
    expect(bouton.disabled).toBe(true)
    fireEvent.change(screen.getByLabelText('Valeur'), { target: { value: 'D' } })
    expect(bouton.disabled).toBe(false)
    fireEvent.click(bouton)
    expect((await screen.findByRole('status')).textContent).toContain('modifié : « D »')
    expect(pont.appels).toHaveBeenCalledWith('EcranReglages.Modifier', 'EXPRESSO_LC_ORDRE_NUM', 'D')
  })

  it('une valeur videe part comme null (la V2 permet un reglage NULL)', async () => {
    const pont = installerFauxPont({ 'EcranReglages.Ouvrir': () => vue(), 'EcranReglages.Modifier': () => vue() })
    render(<EcranReglages />)
    await screen.findByText('2 réglage(s) — greffe 3801')
    fireEvent.click(screen.getAllByRole('row')[1]!)
    fireEvent.change(screen.getByLabelText('Valeur'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Enregistrer' }))
    await screen.findByText('2 réglage(s) — greffe 3801')
    expect(pont.appels).toHaveBeenCalledWith('EcranReglages.Modifier', 'EXPRESSO_LC_ORDRE_NUM', null)
  })

  it('sans le droit : le motif du serveur s\'affiche en alerte et la saisie reste', async () => {
    installerFauxPont({
      'EcranReglages.Ouvrir': () => vue(),
      'EcranReglages.Modifier': () => vue({ echec: true, selection: 'EXPRESSO_LC_ORDRE_NUM',
        message: 'Modification refusee : le compte agent n\'a pas le droit de modifier le greffe (il faut etre administrateur d\'Amitel...).' }),
    })
    render(<EcranReglages />)
    await screen.findByText('2 réglage(s) — greffe 3801')
    fireEvent.click(screen.getAllByRole('row')[1]!)
    fireEvent.change(screen.getByLabelText('Valeur'), { target: { value: 'D' } })
    fireEvent.click(screen.getByRole('button', { name: 'Enregistrer' }))
    expect((await screen.findByRole('alert')).textContent).toContain('n\'a pas le droit de modifier le greffe')
    expect((screen.getByLabelText('Valeur') as HTMLTextAreaElement).value).toBe('D')
  })

  it('sans serveur : les reglages de demonstration, en lecture seule', async () => {
    installerFauxPont({ 'EcranReglages.Ouvrir': () => vue({ mode: 'demo', greffe: null }) })
    render(<EcranReglages />)
    expect(await screen.findAllByText('Démonstration')).toHaveLength(2)
    fireEvent.click(screen.getAllByRole('row')[1]!)
    expect((screen.getByLabelText('Valeur') as HTMLTextAreaElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Enregistrer' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('branchee sans greffe ouvert : le dit, sans liste', async () => {
    installerFauxPont({ 'EcranReglages.Ouvrir': () => vue({ mode: 'sans-greffe', greffe: null, reglages: [], echec: true, message: 'Aucun greffe n\'est ouvert.' }) })
    render(<EcranReglages />)
    expect((await screen.findByRole('alert')).textContent).toContain('Aucun greffe ouvert')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('cherche par code, libelle ou valeur', async () => {
    installerFauxPont({ 'EcranReglages.Ouvrir': () => vue() })
    render(<EcranReglages />)
    await screen.findByText('2 réglage(s) — greffe 3801')
    fireEvent.change(screen.getByLabelText('Code, libellé ou valeur'), { target: { value: 'true' } })
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(1)
    expect(lignes[0]!.textContent).toContain('PROC_RELCLI_ultCheckBoxLettre')
    expect(trace(null, null)).toBe('jamais')
  })
})
