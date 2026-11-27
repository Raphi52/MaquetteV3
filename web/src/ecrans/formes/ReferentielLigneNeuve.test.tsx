// La LIGNE NEUVE d'un Referentiel (plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026) : « Ajouter » dans un onglet saisit, sous
// la ligne, les sections que le descripteur fait saisir a l'ajout dans ce groupe (sectionsAjout : les champs obligatoires d'une zone
// de « Tables de reference », que la grille ne porte pas) ; « Dupliquer » (descripteur.duplication, RIG v2 SQLBase_Handler.Copie)
// ouvre une ligne neuve pre-remplie de la ligne copiee, sauf sa cle, et ses sections de la fiche lue ; Enregistrer l'envoie en
// Ajouter, ids = [la ligne copiee]. Controle final du lot 14 : ni l'un ni l'autre n'avait de chemin (la ligne neuve n'envoyait que
// ses colonnes). Chaque test a son jumeau : sans ces reglages, la grille reste celle d'avant.

import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { EcranGenere, type VueLignesEcran } from '../EcranGenere'
import type { DescripteurEcranVue, LigneEcranVue } from '../../plan/types'
import { installerFauxPont, retirerFauxPont } from '../../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

const Zones = 'Zones'
// Le titre de la section de PROC_GTABREF (Pont/EcransGeneres/Configurer.cs), apostrophe droite comprise.
const SectionZone = "Champs d'une zone"

function grille(partiel: Partial<DescripteurEcranVue> = {}): DescripteurEcranVue {
  return {
    code: 'ECRAN_TEST', titre: 'Tables test', forme: 'Referentiel', sujet: 'codes', recherche: 'Rechercher…', criteres: [],
    valider: 'Valider', rang: 1,
    etats: [
      { code: 'AFaire', libelle: 'Libellé à saisir', icone: 'a-traiter' },
      { code: 'EnCours', libelle: 'Non saisissable', icone: 'saisie-manuelle' },
      { code: 'Validee', libelle: 'Utilisable en saisie', icone: 'validee' },
    ],
    groupes: ['États', Zones], marches: [], parametres: [], gestes: [], lignes: [], membres: [], enOnglets: false,
    colonnes: [{ libelle: 'Code', type: 'Texte', options: [] }, { libelle: 'Libellé', type: 'Texte', options: [] }],
    sections: [
      { titre: 'Enregistrement', consigne: '', obligatoire: true, champs: [{ libelle: 'Code', type: 'Texte', options: [] }] },
      { titre: SectionZone, consigne: 'Saisi quand la zone se crée.', obligatoire: false, champs: [
        { libelle: 'Synonyme', type: 'Texte', options: [] }, { libelle: 'Type', type: 'Liste', options: ['DATE', 'TEXT'] }] },
    ],
    ...partiel,
  }
}
const avecLigneNeuve = (partiel: Partial<DescripteurEcranVue> = {}) =>
  grille({ duplication: true, sectionsAjout: { [Zones]: [SectionZone] }, ...partiel })

const etat: LigneEcranVue = { nom: 'Ouverture', precisions: 'code ORJ', groupe: 'États', etat: 'Validee', faites: [], id: 'PC_ETAT|ORJ', valeurs: ['ORJ', 'Ouverture'] }
const zone: LigneEcranVue = { nom: 'Date du jugement', precisions: 'code DJ', groupe: Zones, etat: 'Validee', faites: [], id: 'PC_ETAT_ZONE|DJ', valeurs: ['DJ', 'Date du jugement'] }
const base = (lignes: LigneEcranVue[]): VueLignesEcran => ({ mode: 'base', message: null, greffe: '3801', lignes, exemples: [] })
const fait = () => ({ fait: true, message: 'Créé.', sansEcrivain: false, ids: ['PC_ETAT_ZONE|DZ'] })
const ficheZone = () => ({
  mode: 'base', message: null, fiche: {
    procedure: 'ECRAN_TEST', id: 'PC_ETAT_ZONE|DJ', nom: 'Date du jugement', lectureSeule: false, motif: null,
    sections: [{ titre: SectionZone, champs: [
      { libelle: 'Synonyme', valeur: 'DJUG', modifiable: false, colonnes: [], lignes: [] },
      { libelle: 'Type', valeur: 'DATE', modifiable: false, colonnes: [], lignes: [] }] }],
  },
})

const bouton = (c: HTMLElement, texte: string) => [...c.querySelectorAll('button')].find((b) => b.textContent?.startsWith(texte))
const champ = (c: HTMLElement, libelle: string) => c.querySelector(`tr.rig-eg-edition-ajout [aria-label="${libelle}"]`) as HTMLInputElement | null
const cellules = (c: HTMLElement) => [...c.querySelectorAll('tr.rig-eg-ligne--edition:not(.rig-eg-edition-ajout) input[type=text]')]
  .map((i) => i as HTMLInputElement)
async function menu(c: HTMLElement, nom: string) {
  await waitFor(() => expect(c.querySelector(`button[aria-label="Actions — ${nom}"]`)).not.toBeNull())
  fireEvent.click(c.querySelector(`button[aria-label="Actions — ${nom}"]`)!)
  return [...document.body.querySelectorAll('[role="menu"] button')]
}

describe('Referentiel : les champs d’une ligne neuve (sectionsAjout)', () => {
  it('Ajouter dans l’onglet des zones saisit ses champs sous la ligne, envoyes en « Section › Libelle »', async () => {
    const pont = installerFauxPont({ 'EcranGenere.Lignes': () => base([etat, zone]), 'EcranGenere.Geste': fait })
    const { container } = render(<EcranGenere descripteur={avecLigneNeuve()} />)
    await waitFor(() => expect(container.querySelector('tbody tr')).not.toBeNull())
    fireEvent.click(bouton(container, Zones)!)
    fireEvent.click(bouton(container, 'Ajouter')!)
    expect(champ(container, 'Synonyme')).not.toBeNull()
    const [code] = cellules(container)
    fireEvent.change(code!, { target: { value: 'DZ' } })
    fireEvent.change(champ(container, 'Synonyme')!, { target: { value: 'DZON' } })
    fireEvent.click(bouton(container, 'Enregistrer')!)
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', Zones, 'Ajouter', null, [],
      { Code: 'DZ', Libellé: '', [`${SectionZone} › Synonyme`]: 'DZON', [`${SectionZone} › Type`]: '' }))
  })

  it('jumeau : dans un onglet sans section d’ajout (ou sans sectionsAjout), la ligne neuve n’a que ses colonnes', async () => {
    const pont = installerFauxPont({ 'EcranGenere.Lignes': () => base([etat, zone]), 'EcranGenere.Geste': fait })
    const { container } = render(<EcranGenere descripteur={avecLigneNeuve()} />)
    await waitFor(() => expect(container.querySelector('tbody tr')).not.toBeNull())
    fireEvent.click(bouton(container, 'Ajouter')!)
    expect(container.querySelector('tr.rig-eg-edition-ajout')).toBeNull()
    fireEvent.change(cellules(container)[0]!, { target: { value: 'NEW' } })
    fireEvent.click(bouton(container, 'Enregistrer')!)
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', 'États', 'Ajouter', null, [],
      { Code: 'NEW', Libellé: '' }))
    cleanup()
    installerFauxPont({ 'EcranGenere.Lignes': () => base([etat, zone]) })
    const r = render(<EcranGenere descripteur={grille()} />)
    await waitFor(() => expect(r.container.querySelector('tbody tr')).not.toBeNull())
    fireEvent.click(bouton(r.container, Zones)!)
    fireEvent.click(bouton(r.container, 'Ajouter')!)
    expect(r.container.querySelector('tr.rig-eg-edition-ajout')).toBeNull()
  })
})

describe('Referentiel : « Dupliquer » une ligne (duplication)', () => {
  it('ouvre une ligne neuve pre-remplie sauf la cle, ses sections lues dans la fiche copiee ; Enregistrer = Ajouter, ids = [la copiee]', async () => {
    const pont = installerFauxPont({
      'EcranGenere.Lignes': () => base([etat, zone]), 'EcranGenere.Fiche': ficheZone, 'EcranGenere.Geste': fait,
    })
    const { container } = render(<EcranGenere descripteur={avecLigneNeuve()} />)
    const actions = await menu(container, 'Date du jugement')
    expect(actions.map((b) => b.textContent)).toEqual(['Modifier', 'Supprimer', 'Dupliquer'])
    fireEvent.click(actions[2]!)
    expect(cellules(container).map((i) => i.value)).toEqual(['', 'Date du jugement'])
    expect(container.querySelector('tr.rig-eg-edition-ajout [role=note]')?.textContent).toContain('Copie de « Date du jugement »')
    await waitFor(() => expect(champ(container, 'Synonyme')?.value).toBe('DJUG'))
    expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Fiche', 'ECRAN_TEST', Zones, 'PC_ETAT_ZONE|DJ')
    fireEvent.change(cellules(container)[0]!, { target: { value: 'DZ' } })
    fireEvent.click(bouton(container, 'Enregistrer')!)
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', Zones, 'Ajouter', null,
      ['PC_ETAT_ZONE|DJ'], { Code: 'DZ', Libellé: 'Date du jugement', [`${SectionZone} › Synonyme`]: 'DJUG', [`${SectionZone} › Type`]: 'DATE' }))
  })

  it('jumeau : sans duplication, pas de « Dupliquer » au menu de la ligne', async () => {
    installerFauxPont({ 'EcranGenere.Lignes': () => base([etat, zone]) })
    const { container } = render(<EcranGenere descripteur={grille()} />)
    expect((await menu(container, 'Date du jugement')).map((b) => b.textContent)).toEqual(['Modifier', 'Supprimer'])
  })
})
