import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCourriersDivers, type CatalogueCourriersVue } from './EcranCourriersDivers'
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

// La tete de la liste moderne : « 4 courriers · 3 avec destinataire ».
const synthese = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')?.textContent ?? null
const vue = (nom: string) => screen.getByRole('button', { name: new RegExp(`^${nom} \\d+$`) })

describe('EcranCourriersDivers', () => {
  it('ouvre sur les quatre courriers actifs, le premier en modification', async () => {
    const { container } = render(<EcranCourriersDivers />)
    // Avant la reponse du pont : ni compte ni « aucun courrier », qui serait faux.
    expect(synthese(container)).toBeNull()
    expect(container.querySelector('.rig-lm-pied')).toBeNull()
    expect(screen.queryByText('Aucun courrier divers ne repond a cette recherche.')).toBeNull()
    await screen.findByText('Modification du courrier divers CD_CONV')
    expect(pont.appels).toHaveBeenCalledWith('EcranCourriersDivers.Catalogue')
    expect(synthese(container)).toBe('4 courriers · 3 avec destinataire')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('4 courriers affiches')
    expect(screen.getByText('Modification du courrier divers CD_CONV')).toBeTruthy()
    expect((screen.getByLabelText('Parametre edition (maquette)') as HTMLInputElement).value).toBe('MAQ_CONV_SIMPLE')
    expect([...container.querySelectorAll('.rig-chip')].map((c) => c.textContent)).toEqual(['Avec destinataire', 'Actif'])
    // Type d'entite en pastille (teinte de la nature d'affaire), actif en point colore.
    const premiere = container.querySelectorAll('tbody tr')[0]!
    expect(premiere.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-bleu')
    expect(premiere.querySelector('.rig-lm-pastille')!.textContent).toBe('Instance')
    expect(premiere.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
    expect(premiere.querySelector('.rig-lm-etat')!.textContent).toBe('Actif')
  })

  it('« Actifs seulement » eteint montre aussi le courrier inactif', async () => {
    const { container } = render(<EcranCourriersDivers />)
    await screen.findByText('CD_CONV')
    fireEvent.click(screen.getByRole('button', { name: 'Actifs seulement' }))
    expect(synthese(container)).toBe('5 courriers · 4 avec destinataire · 1 inactif')
    const inactif = [...container.querySelectorAll('tbody tr')].find((tr) => tr.textContent!.includes('CD_MESURE'))!
    expect(inactif.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-gris')
    expect(inactif.querySelector('.rig-lm-etat')!.textContent).toBe('Inactif')
    fireEvent.click(screen.getByText('CD_MESURE'))
    expect([...container.querySelectorAll('.rig-chip')].map((c) => c.textContent)).toEqual(['Avec destinataire', 'Inactif'])
  })

  it('cherche dans le code et le libelle, et filtre par type d\'entite', async () => {
    const { container } = render(<EcranCourriersDivers />)
    await screen.findByText('CD_CONV')
    fireEvent.change(screen.getByLabelText('Code ou libelle'), { target: { value: 'relance' } })
    expect(synthese(container)).toBe('1 courrier · 1 avec destinataire')
    fireEvent.change(screen.getByLabelText('Code ou libelle'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Type d\'entite'), { target: { value: 'INSTANCE' } })
    expect(synthese(container)).toBe('2 courriers · 2 avec destinataire')
    fireEvent.change(screen.getByLabelText('Type d\'entite'), { target: { value: 'INSTANCE_PREVENTION' } })
    expect(screen.getByText('Aucun courrier divers ne repond a cette recherche.')).toBeTruthy()
  })

  it('les vues posent les criteres existants (actifs seulement, type d\'entite), avec leur compte', async () => {
    const { container } = render(<EcranCourriersDivers />)
    await screen.findByText('CD_CONV')
    expect([...container.querySelectorAll('.rig-lm-vue')].map((v) => v.textContent)).toEqual([
      'Actifs 4', 'Tous 5', 'Instance 2', 'Instance prevention 0', 'Procedure collective 1', 'Prevention 1', 'Mesure 0'])
    expect(vue('Actifs').getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(vue('Tous'))
    expect(screen.getByRole('button', { name: 'Actifs seulement' }).className).toBe('rig-bascule')
    expect(synthese(container)).toBe('5 courriers · 4 avec destinataire · 1 inactif')
    fireEvent.click(vue('Procedure collective'))
    expect((screen.getByLabelText('Type d\'entite') as HTMLSelectElement).value).toBe('PROCEDURE_COLLECTIVE')
    expect(screen.getByRole('button', { name: 'Actifs seulement' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(synthese(container)).toBe('1 courrier · 1 avec destinataire')
    // La recherche saisie compte dans le compte des vues.
    fireEvent.change(screen.getByLabelText('Code ou libelle'), { target: { value: 'relance' } })
    expect(vue('Actifs').textContent).toBe('Actifs 1')
  })

  it('« Aller a un courrier… » filtre la liste affichee ; Entree choisit le courrier reste seul', async () => {
    const { container } = render(<EcranCourriersDivers />)
    await screen.findByText('CD_CONV')
    const champ = screen.getByLabelText('Aller a un courrier…')
    fireEvent.change(champ, { target: { value: 'mandataire' } })
    expect([...container.querySelectorAll('tbody tr td:first-child')].map((td) => td.textContent)).toEqual(['CD_NOTIF'])
    expect(synthese(container)).toBe('1 courrier · 1 avec destinataire')
    expect((screen.getByLabelText('Code ou libelle') as HTMLInputElement).value).toBe('')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(screen.getByText('Modification du courrier divers CD_NOTIF')).toBeTruthy()
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 4 courriers ne contient « zzz ».')).toBeTruthy()
  })
})
