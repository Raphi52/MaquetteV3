import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { Visionneuse } from './Visionneuse'
import { dossier } from '../test/plan'
import type { DossierVue, GestePlan, MessageFilVue } from './types'

afterEach(cleanup)

// LA VUE DU DOSSIER, proposition 3 « Onglets Suivi / Fil » du /draft du 19/11/2026
// (conv-2009) : l'arbre se replie en un chemin, la colonne de suivi a deux onglets.
// Les regles (chemin, fil, teintes) sont jouees par tests/PlanTravailCheck ; ici,
// le balisage rendu et les gestes remontes au plan de travail.
function rendre(d: DossierVue = dossier()) {
  const plan = vi.fn<GestePlan>()
  const r = render(<Visionneuse dossier={d} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
  return { ...r, plan, gestes: () => plan.mock.calls.filter(([nom]) => nom !== 'etapeVisible').map(([nom, valeur]) => [nom, valeur ?? null]) }
}

const note: MessageFilVue = {
  initiales: 'CB', auteur: 'C. BENIER', quand: 'hier 16:20', texte: 'Le PV d\'assemblee est arrive.',
  modification: false, champ: '', avant: '', apres: '',
}
const modif: MessageFilVue = {
  initiales: 'RV', auteur: 'R. VILAIN', quand: '10:42', texte: '',
  modification: true, champ: 'Montant du capital', avant: '15000', apres: '40000',
}

describe('Vue du dossier : l\'arbre se replie', () => {
  it('le bouton de l\'entete de l\'arbre remonte « arbreReplier »', () => {
    const { container, gestes } = rendre()
    expect(container.querySelector('.rig-vged__chemin')).toBeNull()
    fireEvent.click(container.querySelector('.rig-arbre__replier')!)
    expect(gestes()).toEqual([['arbreReplier', null]])
  })

  it('replie : plus de colonne d\'arbre ni de premiere poignee, le chemin de l\'element choisi en tete', () => {
    const { container, gestes } = rendre(dossier({ arbreReplie: true, poignee: false }))
    expect(container.querySelector('.rig-vged__arbre')).toBeNull()
    expect([...container.querySelector('.rig-vged__corps')!.children].map((e) => e.className)).toEqual([
      'rig-vged__chemin', 'rig-vged__milieu', 'rig-vged__poignee', 'rig-vged__guide rig-vged__apercu', 'rig-vged__suivi',
    ])
    const etapes = [...container.querySelectorAll('.rig-vged__chemin-etapes > li')]
    expect(etapes.map((e) => e.textContent)).toEqual(['004176', 'Dossier RCS', 'Statuts signes'])
    expect(etapes.map((e) => e.className)).toEqual(['', '', 'rig-vged__chemin-courant'])
    fireEvent.click(container.querySelector('.rig-vged__chemin-arbre')!)
    expect(gestes()).toEqual([['arbreDeplier', null]])
  })
})

describe('Vue du dossier : la colonne de suivi, onglets Suivi / Fil', () => {
  it('deux onglets, Suivi choisi au depart ; leurs nombres : etapes a corriger (alerte), messages', () => {
    const { container } = rendre(dossier({ fil: [note, modif] }))
    const onglets = [...container.querySelectorAll('.rig-suivi__onglet')]
    // Plus « Suivi 10 » (1 etape a corriger + 9 pieces additionnees) : le nombre dit ce qui attend.
    expect(onglets.map((o) => o.textContent)).toEqual(['Suivi 1', 'Fil 2'])
    expect(onglets.map((o) => o.querySelector('.rig-suivi__nombre')!.className))
      .toEqual(['rig-suivi__nombre rig-suivi__nombre--alerte', 'rig-suivi__nombre'])
    expect(onglets.map((o) => o.getAttribute('title'))).toEqual(['1 etape a corriger', '2 messages'])
    expect(onglets.map((o) => o.getAttribute('aria-selected'))).toEqual(['true', 'false'])
    expect(container.querySelector('.rig-fil')).toBeNull()
  })

  it('zero : l\'onglet n\'affiche ni nombre ni infobulle', () => {
    const { container } = rendre(dossier({ bandeau: null, fil: [] }))
    const onglets = [...container.querySelectorAll('.rig-suivi__onglet')]
    expect(onglets.map((o) => o.textContent)).toEqual(['Suivi', 'Fil'])
    expect(container.querySelector('.rig-suivi__nombre')).toBeNull()
    expect(onglets.map((o) => o.getAttribute('title'))).toEqual([null, null])
  })

  it('Suivi : le reste a faire, une ligne par etape ; un clic ouvre l\'etape', () => {
    const { container, gestes } = rendre()
    expect(container.querySelector('.rig-reste .rig-suivi__titre')!.textContent).toBe('Reste a faire · 1')
    const ligne = container.querySelector('.rig-reste__ligne')!
    expect(ligne.className).toBe('rig-reste__ligne rig-reste__ligne--refuse')
    expect(ligne.textContent).toBe('Identite1 point')
    expect(ligne.getAttribute('title')).toBe('SIREN : cle de controle du SIREN incorrecte')
    fireEvent.click(ligne)
    expect(gestes()).toEqual([['allerAuDefautBandeau', { etape: 1, fiche: 0 }]])
  })

  it('Suivi sans defaut : « Rien a corriger. »', () => {
    const { container } = rendre(dossier({ bandeau: null }))
    expect(container.querySelector('.rig-reste')!.textContent).toBe('Reste a faire · 0Rien a corriger.')
  })

  it('Fil : n\'apparait qu\'une fois choisi ; une note en bulle, une modification avec avant et apres', () => {
    const { container } = rendre(dossier({ fil: [note, modif] }))
    fireEvent.click(container.querySelectorAll('.rig-suivi__onglet')[1]!)
    expect(container.querySelector('.rig-suivi__onglet--actif')!.textContent).toBe('Fil 2')
    expect(container.querySelector('.rig-reste')).toBeNull()
    const messages = container.querySelectorAll('.rig-fil__message')
    expect(messages).toHaveLength(2)
    expect(messages[0]!.querySelector('.rig-fil__avatar')!.textContent).toBe('CB')
    expect(messages[0]!.querySelector('.rig-fil__tete')!.textContent).toBe('C. BENIERhier 16:20')
    expect(messages[0]!.querySelector('.rig-fil__bulle')!.textContent).toBe('Le PV d\'assemblee est arrive.')
    expect(messages[1]!.className).toBe('rig-fil__message rig-fil__message--modif')
    expect(messages[1]!.querySelector('.rig-fil__tete')!.textContent).toBe('R. VILAIN a modifie Montant du capital10:42')
    expect(messages[1]!.querySelector('.rig-fil__diff')!.textContent).toBe('15000 → 40000')
  })

  it('Fil vide : il le dit ; Entree envoie la note, Maj+Entree passe a la ligne, rien de vide ne part', () => {
    const { container, gestes } = rendre()
    fireEvent.click(container.querySelectorAll('.rig-suivi__onglet')[1]!)
    expect(container.querySelector('.rig-fil__vide')!.textContent).toContain('Aucun message sur ce dossier.')
    const champ = container.querySelector('.rig-fil__champ') as HTMLTextAreaElement
    const envoyer = container.querySelector('.rig-fil__envoyer') as HTMLButtonElement
    expect(envoyer.disabled).toBe(true)
    fireEvent.keyDown(champ, { key: 'Enter' })
    fireEvent.change(champ, { target: { value: 'Date de naissance a demander' } })
    expect(envoyer.disabled).toBe(false)
    fireEvent.keyDown(champ, { key: 'Enter', shiftKey: true })
    expect(gestes()).toEqual([])
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(gestes()).toEqual([['filEcrire', 'Date de naissance a demander']])
    expect(champ.value).toBe('')
    fireEvent.change(champ, { target: { value: 'Relance faite' } })
    fireEvent.click(envoyer)
    expect(gestes()[1]).toEqual(['filEcrire', 'Relance faite'])
  })
})
