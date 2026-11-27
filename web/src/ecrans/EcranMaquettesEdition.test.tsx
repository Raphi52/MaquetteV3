import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranMaquettesEdition, type VueMaquettes } from './EcranMaquettesEdition'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(cleanup)

// La tete de la liste moderne : « 5 maquettes · 1 Excel · 4 Word ».
const synthese = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent
const edition = (c: HTMLElement) => c.querySelector('.rig-nonmaq__texte--sourd')!.textContent!.replace(/\s+/g, ' ').trim()
const vue = (nom: string) => screen.getByRole('button', { name: new RegExp(`^${nom} \\d+$`) })

describe('EcranMaquettesEdition', () => {
  it('liste les cinq maquettes et les tables liees de la premiere', () => {
    const { container } = render(<EcranMaquettesEdition />)
    expect(synthese(container)).toBe('5 maquettes · 1 Excel · 4 Word')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('5 maquettes affichees' + 'Tables liees 12')
    expect(screen.getByText('Tables liees a MQ_KBIS')).toBeTruthy()
    expect([...container.querySelectorAll('.rig-chip')].map((c) => c.textContent)).toEqual(['DOSSIER', 'DEPOT', 'CLIENT'])
    expect(edition(container)).toBe('Edition : Greffe - Accueil — A4 en-tete — Lot 1 - courrier simple — recto-verso')
    // Le type de document en pastille.
    const pastille = container.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')[3]!.querySelector('.rig-lm-pastille')!
    expect([pastille.textContent, pastille.className]).toEqual(['Extrait', 'rig-lm-pastille rig-lm-t-bleu'])
  })

  it('les quatre listes de lot ont le meme catalogue et leur propre choix', () => {
    render(<EcranMaquettesEdition />)
    const lots = ['Lot standard', 'Lot LRAR', 'Lot iLRAR', 'Lot de substitution']
      .map((l) => screen.getByLabelText(l) as HTMLSelectElement)
    expect(lots.map((s) => s.value)).toEqual(['Lot 1 - courrier simple', 'Lot 3 - recommande', 'Lot 2 - courrier suivi', '(aucun)'])
    for (const s of lots) expect(s.options).toHaveLength(5)
  })

  it('les parametres d\'edition suivent les choix : lot, recto-verso, agrafage', () => {
    const { container } = render(<EcranMaquettesEdition />)
    fireEvent.change(screen.getByLabelText('Lot standard'), { target: { value: 'Lot 4 - depot guichet' } })
    fireEvent.click(screen.getByRole('button', { name: 'Recto-verso' }))
    fireEvent.click(screen.getByRole('button', { name: 'Agrafage' }))
    expect(edition(container)).toBe('Edition : Greffe - Accueil — A4 en-tete — Lot 4 - depot guichet — agrafe')
  })

  it('filtre par ecran d\'origine (liste triee) et ouvre une maquette', () => {
    const { container } = render(<EcranMaquettesEdition />)
    expect([...(screen.getByLabelText('Ecran d\'origine') as HTMLSelectElement).options].map((o) => o.textContent))
      .toEqual(['Tous', 'Archivage', 'Audiences', 'Courrier du jour', 'Demandes recues', 'Factures'])
    fireEvent.change(screen.getByLabelText('Type de document'), { target: { value: 'Courrier' } })
    expect(synthese(container)).toBe('2 maquettes · 2 Word')
    fireEvent.click(screen.getByText('MQ_CONVOC'))
    expect(screen.getByText('Tables liees a MQ_CONVOC')).toBeTruthy()
    expect(container.querySelectorAll('tbody tr')[1]!.querySelectorAll('td')[5]!.textContent).toBe('22/07/2026')
  })

  it('les vues sont le critere Type de document, avec le compte de chacune dans l\'ecran d\'origine choisi', () => {
    const { container } = render(<EcranMaquettesEdition />)
    expect([...container.querySelectorAll('.rig-lm-vue')].map((v) => v.textContent))
      .toEqual(['Toutes 5', 'Bordereau 1', 'Courrier 2', 'Extrait 1', 'Facture 1'])
    expect(vue('Toutes').getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(vue('Courrier'))
    expect((screen.getByLabelText('Type de document') as HTMLSelectElement).value).toBe('Courrier')
    expect(vue('Courrier').getAttribute('aria-pressed')).toBe('true')
    expect(synthese(container)).toBe('2 maquettes · 2 Word')
    fireEvent.change(screen.getByLabelText('Ecran d\'origine'), { target: { value: 'Audiences' } })
    expect(vue('Courrier').textContent).toBe('Courrier 1')
    expect(vue('Extrait').textContent).toBe('Extrait 0')
  })

  it('« Aller a une maquette… » filtre la liste affichee ; Entree choisit la maquette restee seule', () => {
    const { container } = render(<EcranMaquettesEdition />)
    const champ = screen.getByLabelText('Aller a une maquette…')
    fireEvent.change(champ, { target: { value: 'facture' } })
    expect([...container.querySelectorAll('tbody tr td:first-child')].map((td) => td.textContent)).toEqual(['MQ_FACT'])
    expect(synthese(container)).toBe('1 maquette · 1 Excel')
    expect((screen.getByLabelText('Type de document') as HTMLSelectElement).value).toBe('')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(screen.getByText('Tables liees a MQ_FACT')).toBeTruthy()
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 5 maquettes ne contient « zzz ».')).toBeTruthy()
  })
})

// D'ou viennent les maquettes (plan « RIG v3 100 % fonctionnel », lot 15) : la demonstration est marquee « exemple », la base ne
// l'est pas et remplace la demonstration (sa premiere maquette remplit le panneau avec son premier parametre d'edition), une panne
// montre son motif et AUCUNE maquette (Pont/EcranMaquettesEdition.cs).
describe('EcranMaquettesEdition : provenance des maquettes', () => {
  afterEach(retirerFauxPont)
  const parametre = {
    code: '00ADM', libelle: 'lettre administrateur', processus: 'EDIT_INS', nature: '', imprimante: '', typePapier: 'NORMAL',
    lot: 'LOT_FICHIER_AMI', lotLrar: '', lotIlrar: '', lotSubstitution: '', nbExemplaires: '2', rectoVerso: true, agrafage: false,
    afficherAvantImpression: true, docNonModifiable: false, impression: true, garderEditionLot1: false, typeGeneration: 'FUSION_RIG',
    requeteSql: '',
  }
  const base: VueMaquettes = {
    maquettes: [
      { code: '3801_00ADM', libelle: 'Lettre Administrateur', process: 'EDIT_INS', typeDoc: 'html', typeMaquette: 'RIG', maj: '',
        tables: ['CODE EVE PRO · 0101'], entetePlieuse: true, parametres: [parametre] },
      { code: 'ETAT_CRYSTAL', libelle: 'Etat Crystal', process: '', typeDoc: 'rpt', typeMaquette: 'CRYSTAL', maj: '', tables: [],
        entetePlieuse: false, parametres: [] },
    ],
    total: 2, mode: 'serveur', message: null,
  }

  it('sans pont, la demonstration est marquee « exemple »', () => {
    const { container } = render(<EcranMaquettesEdition />)
    expect(container.querySelector('[role="note"]')!.textContent).toContain('Maquettes de démonstration')
  })

  it('branche au serveur, les maquettes de la base sans mention ; la premiere remplit le panneau de son parametre', async () => {
    installerFauxPont({ 'EcranMaquettesEdition.Maquettes': () => base })
    const { container } = render(<EcranMaquettesEdition />)
    await screen.findByText('Tables liees a 3801_00ADM')
    expect(container.querySelector('[role="note"]')).toBeNull()
    expect([...container.querySelectorAll('tbody tr td:first-child')].map((td) => td.textContent)).toEqual(['3801_00ADM', 'ETAT_CRYSTAL'])
    expect(synthese(container)).toBe('2 maquettes · 1 CRYSTAL · 1 RIG')
    expect([...container.querySelectorAll('.rig-chip')].map((c) => c.textContent)).toEqual(['CODE EVE PRO · 0101'])
    expect((screen.getByLabelText("Code parametre d'edition") as HTMLInputElement).value).toBe('00ADM')
    expect((screen.getByLabelText('Lot standard') as HTMLSelectElement).value).toBe('LOT_FICHIER_AMI')
    expect((screen.getByLabelText('Type de papier') as HTMLSelectElement).value).toBe('NORMAL')
    expect((screen.getByLabelText("Nombre d'exemplaires") as HTMLInputElement).value).toBe('2')
    expect(screen.getByRole('button', { name: 'Entete au format plieuse' }).className).toContain('rig-bascule--actif')
    expect(container.querySelector('.rig-maq-parametres')!.textContent).toBe("Parametres d'edition : 00ADM")
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('Fichier 2')
    // Une maquette sans parametre vide le panneau et le dit.
    fireEvent.click(screen.getByText('ETAT_CRYSTAL'))
    expect((screen.getByLabelText("Code parametre d'edition") as HTMLInputElement).value).toBe('')
    expect(container.querySelector('.rig-maq-parametres')!.textContent).toBe("Aucun parametre d'edition")
  })

  it('en panne, le motif et aucune maquette (jamais la demonstration a la place)', async () => {
    installerFauxPont({ 'EcranMaquettesEdition.Maquettes': () => ({ maquettes: [], total: 0, mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.' }) })
    const { container } = render(<EcranMaquettesEdition />)
    expect((await screen.findByRole('alert')).textContent).toContain('La base du greffe 3801 ne répond pas.')
    expect(container.querySelectorAll('tbody tr td.rig-table__vide')).toHaveLength(1)
    expect(screen.queryByText('MQ_KBIS')).toBeNull()
  })
})
