import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranTableauEditions, type LigneEdition } from './EcranTableauEditions'
import { installerFauxDotNet, retirerFauxDotNet } from '../test/faux-dotnet'
import { morceaux } from '../test/morceaux'

afterEach(() => { cleanup(); retirerFauxDotNet() })

const detail = {
  designation: 'GREFFE DU TRIBUNAL', attentionDe: 'M. DUPONT', adressePostale: '1 rue X\n80000 AMIENS', typePapier: 'ENTETE',
  telephone: '03 22 00 00 00', siren: '109 678 235', natureCoffreFort: 'KBIS', coffreFort: 'oui',
  natureTribunalDigital: '', tribunalDigital: '', erreurTribunalDigital: '', courriel: 'a@b.fr',
  conserverPjOrigine: true, observation: 'RAS',
}

const base = { agrafage: false, coffreFort: false, tribunalDigital: false, envoiParMail: false, exemplaires: 1,
  imprimer: true, imprimante: 'HP', fileImprimante: 'FILE1', affAvantImpression: false, rectoVerso: true,
  fileDifferee: '', impressionSupplementaire: false }

const editions: LigneEdition[] = [
  { ...base, destinataire: 'Greffe', nomDocument: '', niveau: 0, estGroupe: true, detail: null },
  { ...base, destinataire: 'Requerant', nomDocument: 'Kbis', agrafage: true, niveau: 1, estGroupe: false, detail },
  { ...base, destinataire: 'Archives', nomDocument: 'Copie', niveau: 1, estGroupe: false, detail: null },
]

describe('EcranTableauEditions', () => {
  it('rend les colonnes v2, les groupes et le compte des editions', async () => {
    installerFauxDotNet({ 'EcranTableauEditions.TypesPapier': () => ['NORMAL', 'ENTETE'] })
    const { container } = render(<EcranTableauEditions editions={editions} affaire="0000 AMITEL" fermer={vi.fn()} />)

    expect(screen.getByText('Tableau des editions')).toBeTruthy()
    expect(morceaux(screen.getByText('0000 AMITEL · 2 editions'))).toEqual(['0000 AMITEL', ' · ', '2', ' editions'])
    expect(container.querySelectorAll('thead th')).toHaveLength(14)
    const groupe = container.querySelector('tr.rig-editions__groupe td') as HTMLTableCellElement
    expect(groupe.colSpan).toBe(14)
    expect(groupe.style.paddingLeft).toBe('6px')
    const ligne = container.querySelectorAll('tbody tr')[1]!
    expect((ligne.firstElementChild as HTMLElement).style.paddingLeft).toBe('20px')
    expect([...ligne.querySelectorAll('td')].map((td) => td.textContent).slice(0, 3)).toEqual(['Requerant', 'x', 'Kbis'])
    expect(screen.getByText('Choisissez un destinataire dans le tableau des editions.')).toBeTruthy()
  })

  it('choisir une ligne montre le detail du destinataire, en lecture seule', async () => {
    installerFauxDotNet({ 'EcranTableauEditions.TypesPapier': () => ['NORMAL', 'ENTETE'] })
    const { container } = render(<EcranTableauEditions editions={editions} affaire="0000" fermer={vi.fn()} />)

    fireEvent.click(screen.getByText('Requerant'))
    expect(container.querySelectorAll('tbody tr')[1]!.className).toBe('rig-editions__ligne--actif')
    expect((screen.getByLabelText('Designation') as HTMLInputElement).value).toBe('GREFFE DU TRIBUNAL')
    expect((screen.getByLabelText('Designation') as HTMLInputElement).readOnly).toBe(true)
    expect((screen.getByLabelText('Adresse postale') as HTMLTextAreaElement).value).toBe('1 rue X\n80000 AMIENS')
    await screen.findByRole('option', { name: 'ENTETE' })
    expect((screen.getByLabelText('Type de papier') as HTMLSelectElement).value).toBe('ENTETE')
    expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(true)

    // Une ligne sans detail ramene le message d'invite.
    fireEvent.click(screen.getByText('Archives'))
    expect(screen.getByText('Choisissez un destinataire dans le tableau des editions.')).toBeTruthy()
  })

  it('« Retour a l\'affaire » remonte la fermeture a la coquille', () => {
    installerFauxDotNet({ 'EcranTableauEditions.TypesPapier': () => [] })
    const fermer = vi.fn()
    render(<EcranTableauEditions editions={[]} affaire="0000" fermer={fermer} />)
    fireEvent.click(screen.getByRole('button', { name: 'Retour a l\'affaire' }))
    expect(fermer).toHaveBeenCalledTimes(1)
  })
})
