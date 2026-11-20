import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { Visionneuse } from './Visionneuse'
import { dossier } from '../test/plan'
import type { DossierVue, EnCoursDossierVue, GestePlan, MessageFilVue } from './types'

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
      'rig-vged__chemin', 'rig-vged__milieu', 'rig-vged__poignee', 'rig-vged__guide rig-vged__apercu',
      'rig-vged__poignee rig-vged__poignee--suivi', 'rig-vged__suivi',
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

// Demande du 19/11/2026 : la colonne de suivi se retracte a droite, et sa largeur
// se glisse comme celle des autres colonnes.
describe('Vue du dossier : la colonne de suivi se replie a droite', () => {
  it('le bouton devant les onglets remonte « suiviReplier »', () => {
    const { container, gestes } = rendre()
    const tete = container.querySelector('.rig-suivi__tete')!
    expect([...tete.children].map((e) => e.className)).toEqual(['rig-suivi__replier', 'rig-suivi__onglets'])
    fireEvent.click(tete.querySelector('.rig-suivi__replier')!)
    expect(gestes()).toEqual([['suiviReplier', null]])
  })

  it('repliee : un rail sans onglets ni poignee, qui garde le nombre d\'etapes a corriger et rouvre la colonne', () => {
    const { container, gestes } = rendre(dossier({ suiviReplie: true }))
    const suivi = container.querySelector('.rig-vged__corps > .rig-vged__suivi')!
    expect(suivi.className).toBe('rig-vged__suivi rig-vged__suivi--replie')
    expect(suivi.querySelector('.rig-suivi__onglets')).toBeNull()
    expect(suivi.querySelector('.rig-chrono')).toBeNull()
    expect(container.querySelector('.rig-vged__poignee--suivi')).toBeNull()
    const rouvrir = suivi.querySelector('.rig-suivi__rouvrir')!
    expect(rouvrir.textContent).toBe('Suivi1')
    expect(rouvrir.querySelector('.rig-suivi__nombre')!.getAttribute('title')).toBe('1 etape a corriger')
    fireEvent.click(rouvrir)
    expect(gestes()).toEqual([['suiviDeplier', null]])
  })

  it('repliee sans defaut : le rail n\'affiche pas de nombre', () => {
    const { container } = rendre(dossier({ suiviReplie: true, bandeau: null }))
    expect(container.querySelector('.rig-suivi__rouvrir')!.textContent).toBe('Suivi')
  })
})

// EN COURS SUR L'AFFAIRE (plan « Vue du dossier = coeur des actions », lot 3,
// 20/11/2026) : ce que la session rend pour 004151 (Pont/PlanTravailSuivi.cs,
// joue sur les vraies donnees par tests/PlanTravailCheck).
function enCours004151(): EnCoursDossierVue {
  const benier = { signature: 'C. BENIER', initiales: 'CB', nom: 'C. BENIER' }
  return {
    travail: [
      { nature: 'dem', numero: '004151', titre: 'Depot de bilan - TRANSNORD SA', detail: 'Demande du 29/08/2026 · TRANSNORD',
        etat: 'J+11', ton: 'al', filet: 'alerte', qui: benier, aMoi: false, attribuable: true,
        delai: 'Delai legal de 7 jours depasse de 4 jours', ecranLibelle: 'Demandes' },
      { nature: 'cmd', numero: 'CW-2026-000413', titre: 'Faillite - TRANSNORD SA', detail: 'Commande web · Infogreffe',
        etat: 'A traiter', ton: '', filet: 'action', qui: null, aMoi: false, attribuable: false, delai: null, ecranLibelle: 'Commandes web' },
      { nature: 'cou', numero: 'A-2026-0909', titre: 'Depot de bilan - TRANSNORD SA', detail: 'Courrier · Recommande',
        etat: 'A numeriser', ton: '', filet: 'attente', qui: benier, aMoi: false, attribuable: false, delai: null, ecranLibelle: '' },
    ],
    horsDelai: 1,
    activite: [
      { agent: 'Raphael', verbe: 'a valide', objet: 'depot du dossier 004151', depuis: 'il y a 7 min' },
      { agent: 'Claire', verbe: 'a depose', objet: 'acte au dossier 004151', depuis: 'il y a 22 min' },
    ],
  }
}

describe('Vue du dossier : « En cours » en tete de l\'onglet Suivi', () => {
  it('les lignes de l\'affaire, le plus urgent d\'abord, avant le reste a faire ; le hors delai en alerte, avec son agent', () => {
    const { container } = rendre(dossier({ numero: '004151', enCours: enCours004151() }))
    const panneau = container.querySelector('.rig-suivi__panneau')!
    expect([...panneau.children].map((e) => e.className)).toEqual(['rig-encours', 'rig-reste', 'rig-chrono'])
    const titre = container.querySelector('.rig-encours > .rig-suivi__titre')!
    expect(titre.textContent).toBe('En cours · 3')
    expect(titre.getAttribute('title')).toBe('3 en cours, dont 1 hors delai')
    const lignes = [...container.querySelectorAll('.rig-encours__ligne')]
    expect(lignes.map((l) => l.querySelector('.rig-encours__tete')!.textContent))
      .toEqual(['Demande004151J+11', 'Commande webCW-2026-000413A traiter', 'CourrierA-2026-0909A numeriser'])
    // Hors delai : la ligne, sa pastille et son filet en alerte, le delai ecrit.
    expect(lignes[0]!.className).toBe('rig-encours__ligne rig-encours__ligne--alerte')
    expect(lignes[0]!.querySelector('.rig-encours__etat')!.className).toBe('rig-encours__etat rig-encours__etat--al')
    expect(lignes[0]!.querySelector('.rig-filet-v')!.className).toBe('rig-filet-v rig-filet-v--alerte')
    expect(lignes[0]!.querySelector('.rig-encours__titre')!.textContent).toBe('Depot de bilan - TRANSNORD SA')
    expect(lignes[0]!.querySelector('.rig-encours__titre')!.getAttribute('title')).toBe('Demande du 29/08/2026 · TRANSNORD')
    expect(lignes[0]!.querySelector('.rig-encours__delai')!.textContent).toBe('Delai legal de 7 jours depasse de 4 jours')
    expect(lignes[0]!.querySelector('.rig-encours__qui')!.textContent).toBe('CBC. BENIER')
    // Jumeau : une ligne a traiter n'est pas en alerte, et personne ne s'en occupe.
    expect(lignes[1]!.className).toBe('rig-encours__ligne')
    expect(lignes[1]!.querySelector('.rig-encours__etat')!.className).toBe('rig-encours__etat')
    expect(lignes[1]!.querySelector('.rig-encours__delai')).toBeNull()
    expect(lignes[1]!.querySelector('.rig-encours__qui')!.textContent).toBe('Personne')
  })

  it('un clic sur la ligne ouvre son ecran POUR ce dossier ; une ligne sans ecran ouvrable se lit seulement', () => {
    const { container, gestes } = rendre(dossier({ numero: '004151', enCours: enCours004151() }))
    const lignes = container.querySelectorAll('.rig-encours__ligne')
    const ouvrir = lignes[0]!.querySelector('button.rig-encours__ouvrir')!
    expect(ouvrir.getAttribute('title')).toBe('Ouvrir l\'ecran Demandes pour ce dossier')
    fireEvent.click(ouvrir)
    fireEvent.click(lignes[1]!.querySelector('button.rig-encours__ouvrir')!)
    expect(gestes()).toEqual([
      ['enCoursOuvrir', { nature: 'dem', numero: '004151' }],
      ['enCoursOuvrir', { nature: 'cmd', numero: 'CW-2026-000413' }],
    ])
    expect(lignes[2]!.querySelector('button.rig-encours__ouvrir')).toBeNull()
    fireEvent.click(lignes[2]!.querySelector('.rig-encours__ouvrir')!)
    expect(gestes()).toHaveLength(2)
  })

  it('« M\'attribuer » sur la seule ligne attribuable : le geste de la palette, a mon nom', () => {
    const { container, gestes } = rendre(dossier({ numero: '004151', enCours: enCours004151() }))
    const boutons = [...container.querySelectorAll('.rig-encours__attribuer')]
    expect(boutons.map((b) => b.closest('.rig-encours__ligne')!.querySelector('.rig-encours__numero')!.textContent)).toEqual(['004151'])
    expect(boutons[0]!.textContent).toBe('M\'attribuer')
    fireEvent.click(boutons[0]!)
    expect(gestes()).toEqual([['paletteAttribuer', { numero: '004151', signature: null }]])
  })

  it('attribuee a moi : plus de « M\'attribuer », mon avatar marque', () => {
    const e = enCours004151()
    const moi = { signature: 'R. VILAIN', initiales: 'RV', nom: 'Raphael' }
    e.travail[0] = { ...e.travail[0]!, qui: moi, aMoi: true, attribuable: false }
    const { container } = rendre(dossier({ numero: '004151', enCours: e }))
    const ligne = container.querySelector('.rig-encours__ligne')!
    expect(ligne.querySelector('.rig-encours__attribuer')).toBeNull()
    expect(ligne.querySelector('.rig-encours__qui')!.textContent).toBe('RVR. VILAIN')
    expect(ligne.querySelector('.rig-encours__avatar')!.className).toBe('rig-encours__avatar rig-encours__avatar--moi')
  })

  it('les derniers gestes sur l\'affaire, du plus recent au plus ancien', () => {
    const { container } = rendre(dossier({ numero: '004151', enCours: enCours004151() }))
    expect(container.querySelector('.rig-encours__sous-titre')!.textContent).toBe('Dernieres actions')
    expect([...container.querySelectorAll('.rig-encours__geste')].map((g) => g.textContent)).toEqual([
      'Raphael a valide depot du dossier 004151il y a 7 min', 'Claire a depose acte au dossier 004151il y a 22 min',
    ])
  })

  it('rien en cours : il le dit, sans section de gestes', () => {
    const { container } = rendre()
    expect(container.querySelector('.rig-encours')!.textContent).toBe('En cours · 0Rien en cours sur ce dossier.')
    expect(container.querySelector('.rig-encours__sous-titre')).toBeNull()
  })

  it('repliee : le rail garde aussi le nombre en cours, en alerte s\'il y a du hors delai', () => {
    const { container } = rendre(dossier({ suiviReplie: true, enCours: enCours004151() }))
    const nombres = [...container.querySelectorAll('.rig-suivi__rouvrir .rig-suivi__nombre')]
    expect(nombres.map((n) => [n.className, n.textContent, n.getAttribute('title')])).toEqual([
      ['rig-suivi__nombre rig-suivi__nombre--alerte', '1', '1 etape a corriger'],
      ['rig-suivi__nombre rig-suivi__nombre--encours rig-suivi__nombre--alerte', '3', '3 en cours, dont 1 hors delai'],
    ])
    expect(nombres[1]!.querySelector('svg')).not.toBeNull()
  })

  it('jumeau : sans hors delai, le nombre en cours du rail n\'est pas en alerte', () => {
    const e = { ...enCours004151(), horsDelai: 0 }
    const { container } = rendre(dossier({ suiviReplie: true, bandeau: null, enCours: e }))
    const n = container.querySelector('.rig-suivi__nombre--encours')!
    expect([n.className, n.getAttribute('title')]).toEqual(['rig-suivi__nombre rig-suivi__nombre--encours', '3 en cours'])
  })
})
