import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCourriersDivers, type CatalogueCourriersVue } from './EcranCourriersDivers'
import { contexteDossier } from '../test/dossier'
import { installerFauxPont, retirerFauxPont, type FauxPont } from '../test/faux-pont'

// La liste des modeles vit dans Metier/CourriersDivers.cs, la meme que la page du
// dossier propose (tests/PlanTravailCheck la verifie) ; l'ecran la lit par son pont.
const catalogue: CatalogueCourriersVue = {
  types: ['INSTANCE', 'INSTANCE_PREVENTION', 'PROCEDURE_COLLECTIVE', 'PREVENTION', 'MESURE'],
  courriers: [
    { code: 'CD_CONV', libelle: 'Convocation simple', typeEntite: 'INSTANCE', paramEdition: 'MAQ_CONV_SIMPLE', avecDestinataire: true, actif: true },
    { code: 'CD_RELAN', libelle: 'Relance de piece manquante', typeEntite: 'INSTANCE', paramEdition: 'MAQ_RELANCE', avecDestinataire: true, actif: true },
    { code: 'CD_NOTIF', libelle: 'Notification au mandataire', typeEntite: 'PROCEDURE_COLLECTIVE', paramEdition: 'MAQ_NOTIF_MAND', avecDestinataire: true, actif: true },
    { code: 'CD_INFO', libelle: 'Information sans destinataire', typeEntite: 'PREVENTION', paramEdition: 'MAQ_INFO', avecDestinataire: false, actif: true },
    { code: 'CD_MESURE', libelle: 'Courrier de suivi de mesure', typeEntite: 'MESURE', paramEdition: 'MAQ_SUIVI_MES', avecDestinataire: true, actif: false },
  ],
}

let pont: FauxPont
beforeEach(() => { pont = installerFauxPont({ 'EcranCourriersDivers.Catalogue': () => catalogue }) })
afterEach(() => { cleanup(); retirerFauxPont() })

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent

describe('EcranCourriersDivers', () => {
  it('ouvre sur les quatre courriers actifs, le premier en modification', async () => {
    const { container } = render(<EcranCourriersDivers />)
    // Avant la reponse du pont : ni compte ni « aucun courrier », qui serait faux.
    expect(compte(container)).toBe('')
    expect(screen.queryByText('Aucun courrier divers ne repond a cette recherche.')).toBeNull()
    await screen.findByText('Modification du courrier divers CD_CONV')
    expect(pont.appels).toHaveBeenCalledWith('EcranCourriersDivers.Catalogue')
    expect(compte(container)).toBe('4 courrier(s)')
    expect(screen.getByText('Modification du courrier divers CD_CONV')).toBeTruthy()
    expect((screen.getByLabelText('Parametre edition (maquette)') as HTMLInputElement).value).toBe('MAQ_CONV_SIMPLE')
    expect([...container.querySelectorAll('.rig-chip')].map((c) => c.textContent)).toEqual(['Avec destinataire', 'Actif'])
  })

  it('« Actifs seulement » eteint montre aussi le courrier inactif', async () => {
    const { container } = render(<EcranCourriersDivers />)
    await screen.findByText('CD_CONV')
    fireEvent.click(screen.getByRole('button', { name: 'Actifs seulement' }))
    expect(compte(container)).toBe('5 courrier(s)')
    fireEvent.click(screen.getByText('CD_MESURE'))
    expect([...container.querySelectorAll('.rig-chip')].map((c) => c.textContent)).toEqual(['Avec destinataire', 'Inactif'])
  })

  it('cherche dans le code et le libelle, et filtre par type d\'entite', async () => {
    const { container } = render(<EcranCourriersDivers />)
    await screen.findByText('CD_CONV')
    fireEvent.change(screen.getByLabelText('Code ou libelle'), { target: { value: 'relance' } })
    expect(compte(container)).toBe('1 courrier(s)')
    fireEvent.change(screen.getByLabelText('Code ou libelle'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Type d\'entite'), { target: { value: 'INSTANCE' } })
    expect(compte(container)).toBe('2 courrier(s)')
    fireEvent.change(screen.getByLabelText('Type d\'entite'), { target: { value: 'INSTANCE_PREVENTION' } })
    expect(screen.getByText('Aucun courrier divers ne repond a cette recherche.')).toBeTruthy()
  })
})

// Ouvert depuis la page d'une affaire : le courrier est pour elle, deja choisie
// (bloc « Pour le dossier ») ; le catalogue ne change pas (plan « Vue du dossier », lot 2).
describe('EcranCourriersDivers ouvert depuis une affaire', () => {
  it('montre l\'affaire choisie en tete ; sans affaire, l\'ecran d\'avant', async () => {
    const avec = render(<EcranCourriersDivers dossier={contexteDossier()} />)
    await screen.findByText('CD_CONV')
    expect(screen.getByText('Pour le dossier')).toBeTruthy()
    expect((screen.getByLabelText('N de dossier') as HTMLInputElement).value).toBe('004176')
    expect((screen.getByLabelText('Denomination') as HTMLInputElement).value).toBe('SCI DU PARC')
    expect(compte(avec.container)).toBe('4 courrier(s)')
    avec.unmount()

    const sans = render(<EcranCourriersDivers dossier={null} />)
    expect(screen.queryByText('Pour le dossier')).toBeNull()
    expect(sans.container.querySelector('.rig-criteres > .rig-kicker')!.textContent).toBe('Courriers divers')
  })
})
