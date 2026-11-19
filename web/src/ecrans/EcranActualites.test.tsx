import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranActualites, type VueActualites } from './EcranActualites'
import { installerFauxDotNet, retirerFauxDotNet } from '../test/faux-dotnet'

afterEach(() => { cleanup(); retirerFauxDotNet() })

// La liste et le depot partage vivent dans Pont/EcranActualites.cs (ecran a
// session), execute par tests/ActualitesCheck sur un depot jetable. Ici :
// l'ecran affiche la vue de la session et lui remonte chaque geste.
const types = [
  { code: 'fraude', libelle: 'Fraude', filet: 'alerte', aide: 'Vigilance nominative — reste affichee jusqu\'a retrait' },
  { code: 'info', libelle: 'Info', filet: 'attente', aide: 'Simple information — 7 jours' },
]

function vue(partiel: Partial<VueActualites> = {}): VueActualites {
  return {
    session: 's1', titre: '', message: '', type: 'info', auteur: 'Greffe', jours: '7', peutPublier: false, types,
    compteRendu: '', statutRendu: 'ok', cheminPartage: 'C:\\partage\\bulletins.json',
    liste: [{ filet: 'alerte', libelle: 'Fraude', titre: 'Alerte CNG', message: 'Vigilance', auteur: 'Greffe', horodatage: '17/09 08:40', retire: '—' }],
    ...partiel,
  }
}

describe('EcranActualites', () => {
  it('ouvre une session et affiche la liste, la source partagee et les types colores', async () => {
    const dotnet = installerFauxDotNet({ 'EcranActualites.Ouvrir': () => vue() })
    const { container } = render(<EcranActualites />)

    await screen.findByText('Alerte CNG')
    expect(dotnet.invokeMethodAsync).toHaveBeenCalledWith('RigV3Desktop', 'EcranActualites.Ouvrir')
    expect(screen.getByText('1 actualite(s)')).toBeTruthy()
    expect(screen.getByText('C:\\partage\\bulletins.json')).toBeTruthy()
    const choix = container.querySelectorAll('.rig-actualite__choix')
    expect(choix[1]!.className).toBe('rig-actualite__choix rig-actualite__choix--pris')
    expect(choix[0]!.getAttribute('title')).toBe('Vigilance nominative — reste affichee jusqu\'a retrait')
    expect((screen.getByRole('button', { name: 'Publier l\'actualite' }) as HTMLButtonElement).disabled).toBe(true)
    expect(container.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')[5]!.textContent).toBe('—')
  })

  it('saisir, choisir un type et publier passent par la session ; le compte rendu s\'affiche', async () => {
    const dotnet = installerFauxDotNet({
      'EcranActualites.Ouvrir': () => vue(),
      'EcranActualites.Agir': (_s: unknown, geste: unknown, valeur: unknown) => {
        if (geste === 'Titre') return vue({ titre: String(valeur), peutPublier: true })
        if (geste === 'ChoisirType') return vue({ titre: 'INPI HS', peutPublier: true, type: 'fraude', jours: '0' })
        return vue({ compteRendu: 'Actualite publiee : elle s\'affichera sur l\'accueil des postes.' })
      },
    })
    const { container } = render(<EcranActualites />)
    await screen.findByText('Alerte CNG')

    fireEvent.input(screen.getByLabelText('Titre'), { target: { value: 'INPI HS' } })
    await waitFor(() => expect((screen.getByRole('button', { name: 'Publier l\'actualite' }) as HTMLButtonElement).disabled).toBe(false))
    expect(dotnet.invokeMethodAsync).toHaveBeenLastCalledWith('RigV3Desktop', 'EcranActualites.Agir', 's1', 'Titre', 'INPI HS')

    fireEvent.click(screen.getByRole('button', { name: 'Fraude' }))
    // Choisir un type REPOSITIONNE la duree : le champ suit la session.
    await waitFor(() => expect((screen.getByLabelText(/Retirer apres/) as HTMLInputElement).value).toBe('0'))

    fireEvent.click(screen.getByRole('button', { name: 'Publier l\'actualite' }))
    await screen.findByText('Actualite publiee : elle s\'affichera sur l\'accueil des postes.')
    expect(container.querySelector('.rig-alerte .rig-filet-v')!.className).toBe('rig-filet-v rig-filet-v--ok')
    // « Publier » a vide le titre dans la session : le champ est reecrit.
    expect((screen.getByLabelText('Titre') as HTMLInputElement).value).toBe('')
  })

  it('« Retirer » remonte la place de l\'actualite dans la liste', async () => {
    const dotnet = installerFauxDotNet({ 'EcranActualites.Ouvrir': () => vue(), 'EcranActualites.Agir': () => vue({ liste: [] }) })
    render(<EcranActualites />)
    fireEvent.click(await screen.findByRole('button', { name: 'Retirer' }))
    await screen.findByText('Aucune actualite. La premiere publiee s\'affichera sur l\'accueil de tous les postes.')
    expect(dotnet.invokeMethodAsync).toHaveBeenLastCalledWith('RigV3Desktop', 'EcranActualites.Agir', 's1', 'Retirer', 0)
  })
})
