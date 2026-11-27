// La grille d'un Referentiel dont les lignes ont chacune le formulaire de leur groupe (plan « RIG v3 100 % fonctionnel », lot 14,
// 2e reprise, 27/11/2026 ; « Tables de reference », Pont/EcransGeneres/Configurer.cs) :
//   - groupesModificationSeule : RIG v2 ne fait que modifier un type de depot AJMJ (OPE_AFFICHE_REF_CFEC_AJMJ_TYPE_DEPOT.cs:31-33) --
//     ni Ajouter dans son onglet, ni Supprimer ni Dupliquer au menu de ses lignes ;
//   - sectionsParGroupe : la ligne ouverte ne montre que les sections de SON groupe (la fiche d'une zone montrait celles des deux
//     autres tables « Non lu »), et y fait saisir les champs que la fiche dit modifiables ; Enregistrer envoie ceux qui changent.
// Chaque test a son jumeau : sans ces reglages, la grille reste celle d'avant.

import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { EcranGenere, type VueLignesEcran } from '../EcranGenere'
import type { DescripteurEcranVue, LigneEcranVue } from '../../plan/types'
import { installerFauxPont, retirerFauxPont } from '../../test/faux-pont'
import { NON_LU } from './commun'

afterEach(() => { cleanup(); retirerFauxPont() })

const Etats = 'États', Zones = 'Zones', Types = 'Types AJMJ'
const SectionEtat = "Champs d'un état de PC", SectionZone = "Champs d'une zone"

function grille(partiel: Partial<DescripteurEcranVue> = {}): DescripteurEcranVue {
  return {
    code: 'ECRAN_TEST', titre: 'Tables test', forme: 'Referentiel', sujet: 'codes', recherche: 'Rechercher…', criteres: [],
    valider: 'Valider', rang: 1,
    etats: [{ code: 'Validee', libelle: 'Utilisable en saisie', icone: 'validee' }],
    groupes: [Etats, Zones, Types], marches: [], parametres: [], gestes: [], lignes: [], membres: [], enOnglets: false,
    colonnes: [{ libelle: 'Code', type: 'Texte', options: [] }, { libelle: 'Libellé', type: 'Texte', options: [] }],
    sections: [
      { titre: SectionEtat, consigne: 'En lecture.', obligatoire: false, champs: [{ libelle: 'Régime', type: 'Texte', options: [] }] },
      { titre: SectionZone, consigne: 'Le formulaire d’une zone.', obligatoire: false, champs: [
        { libelle: 'Synonyme', type: 'Texte', options: [] }, { libelle: 'Type', type: 'Liste', options: ['DATE', 'TEXT'] },
        { libelle: 'Libellé', type: 'Texte', options: [] }] },
    ],
    duplication: true,
    ...partiel,
  }
}
const parGroupe = (partiel: Partial<DescripteurEcranVue> = {}) =>
  grille({ sectionsParGroupe: { [Etats]: [SectionEtat], [Zones]: [SectionZone] }, groupesModificationSeule: [Types], ...partiel })

const zone: LigneEcranVue = { nom: 'Date du jugement', precisions: 'code DJ', groupe: Zones, etat: 'Validee', faites: [], id: 'PC_ETAT_ZONE|DJ', valeurs: ['DJ', 'Date du jugement'] }
const type: LigneEcranVue = { nom: 'Rapport', precisions: 'code RAP', groupe: Types, etat: 'Validee', faites: [], id: 'DEMAT_CFEC_AJMJ_TYPE_DEPOT|RAP', valeurs: ['RAP', 'Rapport'] }
const base = (lignes: LigneEcranVue[]): VueLignesEcran => ({ mode: 'base', message: null, greffe: '3801', lignes, exemples: [] })
// La fiche d'une zone : ses champs modifiables, et un champ de meme libelle qu'une colonne (il se saisit dans la ligne, pas dessous).
const ficheZone = () => ({
  mode: 'base', message: null, fiche: {
    procedure: 'ECRAN_TEST', id: 'PC_ETAT_ZONE|DJ', nom: 'Date du jugement', lectureSeule: false, motif: null,
    sections: [{ titre: SectionZone, champs: [
      { libelle: 'Synonyme', valeur: 'DJUG', modifiable: true, colonnes: [], lignes: [] },
      { libelle: 'Type', valeur: 'DATE', modifiable: true, colonnes: [], lignes: [] },
      { libelle: 'Libellé', valeur: 'Date du jugement', modifiable: true, colonnes: [], lignes: [] }] }],
  },
})
const fait = () => ({ fait: true, message: 'Enregistré.', sansEcrivain: false, ids: ['PC_ETAT_ZONE|DJ'] })

const bouton = (c: HTMLElement, texte: string) => [...c.querySelectorAll('button')].find((b) => b.textContent?.startsWith(texte))
const sousLaLigne = (c: HTMLElement) => c.querySelector('tr.rig-eg-edition-lu') as HTMLElement | null
async function menu(c: HTMLElement, nom: string) {
  await waitFor(() => expect(c.querySelector(`button[aria-label="Actions — ${nom}"]`)).not.toBeNull())
  fireEvent.click(c.querySelector(`button[aria-label="Actions — ${nom}"]`)!)
  return [...document.body.querySelectorAll('[role="menu"] button')]
}

describe('Referentiel : un groupe ou RIG v2 ne fait que modifier (groupesModificationSeule)', () => {
  it('ni Supprimer ni Dupliquer au menu de ses lignes, ni Ajouter dans son onglet', async () => {
    installerFauxPont({ 'EcranGenere.Lignes': () => base([zone, type]) })
    const { container } = render(<EcranGenere descripteur={parGroupe()} />)
    expect((await menu(container, 'Rapport')).map((b) => b.textContent)).toEqual(['Modifier'])
    fireEvent.click(bouton(container, Types)!)
    expect(bouton(container, 'Ajouter')).toBeUndefined()
  })

  it('jumeau : les lignes et l’onglet des autres groupes gardent Ajouter, Supprimer et Dupliquer', async () => {
    installerFauxPont({ 'EcranGenere.Lignes': () => base([zone, type]) })
    const { container } = render(<EcranGenere descripteur={parGroupe()} />)
    expect((await menu(container, 'Date du jugement')).map((b) => b.textContent)).toEqual(['Modifier', 'Supprimer', 'Dupliquer'])
    fireEvent.click(bouton(container, Zones)!)
    expect(bouton(container, 'Ajouter')).toBeDefined()
  })
})

describe('Referentiel : les sections d’une ligne selon son groupe (sectionsParGroupe)', () => {
  it('la ligne ouverte ne montre que ses sections, y saisit ses champs modifiables et envoie ceux qui changent', async () => {
    const pont = installerFauxPont({ 'EcranGenere.Lignes': () => base([zone]), 'EcranGenere.Fiche': ficheZone, 'EcranGenere.Geste': fait })
    const { container } = render(<EcranGenere descripteur={parGroupe()} />)
    fireEvent.click((await menu(container, 'Date du jugement'))[0]!)
    await waitFor(() => expect(sousLaLigne(container)?.querySelector('[aria-label="Synonyme"]')).not.toBeNull())
    const dessous = sousLaLigne(container)!
    // Rien de la table d'a cote, aucun « Non lu ».
    expect(dessous.querySelector(`section[aria-label="${SectionEtat}"]`)).toBeNull()
    expect(dessous.textContent).not.toContain(NON_LU)
    // Le libelle est une colonne : il se lit sous la ligne, il se saisit dans la ligne.
    expect(dessous.querySelector('input[aria-label="Libellé"]')).toBeNull()
    const synonyme = dessous.querySelector('input[aria-label="Synonyme"]') as HTMLInputElement
    expect(synonyme.value).toBe('DJUG')
    fireEvent.change(synonyme, { target: { value: 'DJ2' } })
    fireEvent.click(bouton(container, 'Enregistrer')!)
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', Zones, 'Modifier', null,
      ['PC_ETAT_ZONE|DJ'], { [`${SectionZone} › Synonyme`]: 'DJ2' }))
  })

  it('jumeau : sans sectionsParGroupe, la ligne ouverte lit toutes les sections, sans rien y saisir ni envoyer', async () => {
    const pont = installerFauxPont({ 'EcranGenere.Lignes': () => base([zone]), 'EcranGenere.Fiche': ficheZone, 'EcranGenere.Geste': fait })
    const { container } = render(<EcranGenere descripteur={grille()} />)
    fireEvent.click((await menu(container, 'Date du jugement'))[0]!)
    await waitFor(() => expect(sousLaLigne(container)?.textContent).toContain('DJUG'))
    const dessous = sousLaLigne(container)!
    expect(dessous.querySelector(`section[aria-label="${SectionEtat}"]`)?.textContent).toContain(NON_LU)
    expect(dessous.querySelector('input[aria-label="Synonyme"]')).toBeNull()
    fireEvent.click(bouton(container, 'Enregistrer')!)
    await waitFor(() => expect(container.textContent).toContain('Aucune colonne n’est modifiée'))
    expect(pont.appels).not.toHaveBeenCalledWith('EcranGenere.Geste', expect.anything(), expect.anything(), 'Modifier',
      expect.anything(), expect.anything(), expect.anything())
  })
})
