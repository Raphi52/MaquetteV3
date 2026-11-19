import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { Actualites, Activite, Alertes, AlertesBis, Metier, Nouveautes, Reglages, Taches } from './Tuiles'
import { morceaux } from '../test/morceaux'
import { ligne } from '../test/accueil'
import type { GesteAccueil, MetierVue, PanneauDemandesVue } from './types'

afterEach(cleanup)

function espion() {
  const geste = vi.fn<GesteAccueil>()
  return { geste, gestes: () => geste.mock.calls.map(([n, v]) => [n, v ?? null]) }
}

describe('corps des tuiles', () => {
  it('« A traiter » : groupes repliables, compte = CHARGE, pastille au palier, pas de filet ; detail sur reglage', () => {
    const { geste, gestes } = espion()
    const vue = {
      vide: false, afficherDetail: true,
      groupes: [
        { categorie: 'Arrivees', repliee: false, charge: 41, lignes: [ligne('a1', 22, 'DAS'), ligne('a2', 19, 'DAS qualifies')] },
        { categorie: 'DCA', repliee: true, charge: 3, lignes: [] },
      ],
    }
    const { container } = render(<Alertes vue={vue} geste={geste} />)
    const tetes = container.querySelectorAll('.rig-groupe-tete')
    expect(morceaux(tetes[0]!)).toEqual(['<span>', ' ', '<span>', ' ', '<span>'])
    expect(tetes[0]!.getAttribute('aria-expanded')).toBe('true')
    expect(tetes[0]!.getAttribute('title')).toBe('Replier la categorie Arrivees')
    expect(tetes[1]!.getAttribute('aria-expanded')).toBe('false')
    expect(tetes[1]!.querySelector('.rig-groupe-tete__chevron')!.className).toBe('rig-groupe-tete__chevron rig-groupe-tete__chevron--replie')
    expect(tetes[0]!.querySelector('.rig-groupe-tete__compte')!.textContent).toBe('41')
    const l = container.querySelector('.rig-alerte')!
    expect(l.querySelector('.rig-filet-v')).toBeNull()
    expect(l.querySelector('.rig-alerte__compte')!.className).toBe('rig-alerte__compte rig-alerte__compte--fort')
    expect(morceaux(l.querySelector('.rig-alerte__corps'))).toEqual(['<span>', '<br>', ' ', '<span>'])
    expect(morceaux(l.querySelector('.rig-alerte__vers'))).toEqual(['Ecran a1', ' ›'])
    fireEvent.click(tetes[1]!); fireEvent.click(l)
    expect(gestes()).toEqual([['repli', 'DCA'], ['alerte', 'a1']])
  })

  it('« Mes taches du jour » remonte « tache » ; vide, il le dit', () => {
    const { geste, gestes } = espion()
    const { container, rerender } = render(<Taches lignes={[ligne('t1', 1, 'Renommer')]} geste={geste} />)
    expect(morceaux(container.querySelector('.rig-alerte__corps'))).toEqual(['<span>', '<br>', ' ', '<span>'])
    fireEvent.click(container.querySelector('.rig-alerte')!)
    expect(gestes()).toEqual([['tache', 't1']])
    rerender(<Taches lignes={[]} geste={geste} />)
    expect(container.querySelector('.rig-vide')!.textContent).toBe('Aucune tache en attente sur les dossiers connus.')
  })

  it('Actualites : filet et type nommes, auteur — horodatage en trois morceaux, « Rediger »', () => {
    const { geste, gestes } = espion()
    const vue = { etat: 'A jour', actifs: [{ filet: 'alerte', type: 'FRAUDE', titre: 'Alerte CNG', message: 'Signalement', auteur: 'Greffe', horodatage: '17/11 09:00' }] }
    const { container } = render(<Actualites vue={vue} geste={geste} />)
    expect(container.querySelector('.rig-filet-v')!.className).toBe('rig-filet-v rig-filet-v--alerte')
    expect(container.querySelector('.rig-actualite__type')!.className).toBe('rig-actualite__type rig-actualite__type--alerte')
    const details = container.querySelectorAll('.rig-alerte__detail')
    expect(morceaux(details[1]!)).toEqual(['Greffe', ' — ', '17/11 09:00'])
    expect(details[2]!.textContent).toBe('A jour')
    fireEvent.click(container.querySelector('.rig-alerte--lien')!)
    expect(gestes()).toEqual([['rediger', null]])
  })

  it('Activite : filtre par agent (nom (login)), total et comptes en morceaux', () => {
    const { geste, gestes } = espion()
    const vue = {
      filtre: '', total: 12,
      agents: [{ login: 'cmartin', nom: 'Claire' }],
      comptes: [{ agent: 'Claire', nombre: 4 }],
      gestes: [{ agent: 'Claire', verbe: 'a ouvert', objet: 'dossier 004151', depuis: 'il y a 7 min' }],
    }
    const { container } = render(<Activite vue={vue} geste={geste} />)
    const filtre = container.querySelector<HTMLSelectElement>('.rig-activite__filtre')!
    expect(filtre.getAttribute('aria-label')).toBe('Filtrer par utilisateur')
    expect(morceaux(filtre.options[1]!)).toEqual(['Claire', ' (', 'cmartin', ')'])
    expect(morceaux(container.querySelector('.rig-activite__total'))).toEqual(['12', ' geste(s)'])
    expect(container.querySelector('.rig-activite__compte')!.getAttribute('title')).toBe("Claire : 4 geste(s) aujourd'hui")
    expect(morceaux(container.querySelector('.rig-activite__texte'))).toEqual(['<b>', ' ', 'a ouvert', ' ', 'dossier 004151'])
    fireEvent.change(filtre, { target: { value: 'cmartin' } })
    expect(gestes()).toEqual([['activite', 'cmartin']])
  })

  it('Nouveautes : icone puis module, un seul detail deplie', () => {
    const { geste, gestes } = espion()
    const n = { module: 'MENU', pictogramme: 'boussole', teinte: 'vert', action: 'Cherchez', suite: '154 procedures', detail: 'Le menu...', ouverte: true }
    const { container } = render(<Nouveautes nouveautes={[n, { ...n, module: 'PROFIL', ouverte: false }]} geste={geste} />)
    const [l1, l2] = [...container.querySelectorAll('.rig-nouv__ligne')]
    expect(morceaux(l1!.querySelector('.rig-nouv__module'))).toEqual(['<svg>', 'MENU'])
    expect(l1!.className).toBe('rig-nouv__ligne rig-nouv__ligne--ouverte')
    expect(l1!.querySelector('.rig-nouv__chevron')!.textContent).toBe('▾')
    expect(l2!.querySelector('.rig-nouv__chevron')!.textContent).toBe('▸')
    expect(container.querySelectorAll('.rig-nouv__detail')).toHaveLength(1)
    fireEvent.click(l2!)
    expect(gestes()).toEqual([['nouveaute', 'PROFIL']])
  })

  const panneau: PanneauDemandesVue = {
    ouverte: ligne('a1', 22, 'DAS a numeriser'), titreFermer: 'Reduire le widget',
    demandes: [{ chrono: '004131', societe: 'BODACC', siren: '-', type: 'BODACC', recuLe: '07/08/2026', anciennete: 33, etat: 'Validee' }],
  }
  const metier: MetierVue = {
    deploye: true, registre: 'RCS', titre: 'Arrivees', charge: 66, palierCharge: 'haut', menuOuvert: true,
    gestes: ['Numeriser', 'Qualifier'], titreOuvrir: 'Aller vers Ecran a1', filtre: '', filtreActif: false,
    urgents: 0, demat: 35, lignes: [ligne('a1', 22, 'DAS a numeriser'), ligne('a2', 19, 'DAS qualifies')], active: 'a1', panneau,
  }

  it('widget METIER : en-tete, menu des gestes, recherche, compteurs, alertes, panneau des demandes', () => {
    const { geste, gestes } = espion()
    const { container } = render(<Metier id="metier-arrivees" vue={metier} geste={geste} />)
    const racine = container.firstElementChild!
    expect(racine.className).toBe('rig-metier rig-metier--deploye')
    expect(morceaux(racine)).toEqual(['<div>', ' ', '<div>', ' '])
    expect(morceaux(racine.querySelector('.rig-metier__tete'))).toEqual(['<span>', ' ', '<span>', '<span>', ' ', '<button>', ' ', '<button>'])
    expect(racine.querySelector('.rig-metier__deployer')!.textContent).toBe('⤡')
    const menu = racine.querySelector('.rig-metier__menu')!
    const lignesMenu = menu.querySelectorAll('.rig-metier__menu-ligne')
    expect(lignesMenu[0]!.hasAttribute('disabled')).toBe(true)
    expect(morceaux(lignesMenu[1]!)).toEqual(['<span>', 'Ouvrir le dossier\n                    '])
    expect(lignesMenu[1]!.hasAttribute('disabled')).toBe(false)
    expect(racine.querySelector('.rig-metier__puce')!.className).toBe('rig-metier__puce ')
    expect([...racine.querySelectorAll('.rig-metier__ligne')].map((b) => b.className)).toEqual(['rig-metier__ligne rig-metier__ligne--active', 'rig-metier__ligne '])
    const droite = racine.querySelector('.rig-metier__droite')!
    expect(morceaux(droite.querySelector('.rig-metier__droite-compte'))).toEqual(['1', ' demande(s)'])
    expect(morceaux(droite.querySelector('tbody td:nth-child(6)'))).toEqual(['33', ' j'])
    expect([...droite.querySelectorAll('th')].map((t) => t.textContent)).toEqual(['N° chrono', 'Société', 'SIREN', 'Type', 'Reçu le', 'Anc.', 'État'])

    fireEvent.click(racine.querySelector('.rig-metier__deployer')!)
    fireEvent.click(racine.querySelector('.rig-metier__menu-bouton')!)
    fireEvent.click(lignesMenu[1]!)
    fireEvent.click(lignesMenu[lignesMenu.length - 2]!)
    fireEvent.click(lignesMenu[lignesMenu.length - 1]!)
    fireEvent.input(racine.querySelector('.rig-metier__saisie')!, { target: { value: '0041' } })
    fireEvent.click(racine.querySelectorAll('.rig-metier__ligne')[1]!)
    fireEvent.click(droite.querySelector('.rig-metier__ouvrir')!)
    fireEvent.click(droite.querySelector('.rig-metier__fermer')!)
    fireEvent.click(droite.querySelector('.rig-metier__chrono')!)
    expect(gestes()).toEqual([
      ['metierDeployer', 'metier-arrivees'], ['metierMenu', 'metier-arrivees'], ['metierOuvrir', 'metier-arrivees'],
      ['metierRafraichir', 'metier-arrivees'], ['metierParametrer', 'metier-arrivees'],
      ['metierFiltre', { id: 'metier-arrivees', texte: '0041' }], ['metierChoisir', { id: 'metier-arrivees', alerte: 'a2' }],
      ['metierAlerte', 'metier-arrivees'], ['metierReduire', 'metier-arrivees'], ['numero', '004131'],
    ])
  })

  it('widget METIER replie : pas de panneau ; sans alerte ouverte, « Ouvrir le dossier » est inactif', () => {
    const { geste } = espion()
    const { container } = render(<Metier id="metier-das" vue={{ ...metier, deploye: false, titreOuvrir: null, panneau: null, active: null }} geste={geste} />)
    expect(container.querySelector('.rig-metier__droite')).toBeNull()
    expect(container.querySelector('.rig-metier__deployer')!.textContent).toBe('⛶')
    const ouvrir = container.querySelectorAll('.rig-metier__menu-ligne')[1]!
    expect(ouvrir.hasAttribute('disabled')).toBe(true)
    expect(ouvrir.getAttribute('title')).toBe("Choisir une alerte d'abord")
  })

  it('« A traiter BIS » : choisir une alerte la SELECTIONNE (geste bisChoisir)', () => {
    const { geste, gestes } = espion()
    const vue = { vide: false, active: 'a1', panneau: { ...panneau, demandes: [] }, groupes: [{ categorie: 'Arrivees', repliee: false, charge: 22, lignes: [ligne('a1', 22, 'DAS')] }] }
    const { container } = render(<AlertesBis vue={vue} geste={geste} />)
    expect(morceaux(container.firstElementChild)).toEqual(['<div>', ' ', ' ', '<div>', ' '])
    expect(container.querySelector('.rig-vide')!.textContent).toBe('Aucune demande ne correspond à la recherche.')
    fireEvent.click(container.querySelector('.rig-metier__ligne')!)
    expect(gestes()).toEqual([['bisChoisir', 'a1']])
  })

  it('Reglages de « A traiter » : categories, alertes, niveau, ordre, plafond, detail', () => {
    const { geste, gestes } = espion()
    const vue = {
      genre: 'alertes' as const, niveauMini: 1, tri: 'gravite', limite: 5, afficherDetail: false, mesures: [],
      categories: [{ id: 'Arrivees', libelle: 'Arrivees', titre: 'Afficher ou masquer toutes les alertes de Arrivees', actif: true }],
      alertes: [{ id: 'a1', libelle: 'DAS', titre: 'Mene vers Ecran', actif: false }],
    }
    const { container } = render(<Reglages vue={vue} geste={geste} />)
    const [niveau, ordre] = [...container.querySelectorAll('select')]
    expect(niveau!.value).toBe('1')
    expect(ordre!.value).toBe('gravite')
    const plafond = container.querySelector<HTMLInputElement>('input[type="number"]')!
    expect(plafond.value).toBe('5')
    fireEvent.click(container.querySelectorAll('.rig-bascule')[0]!)
    fireEvent.click(container.querySelectorAll('.rig-bascule')[1]!)
    fireEvent.change(niveau!, { target: { value: '3' } })
    fireEvent.change(ordre!, { target: { value: 'titre' } })
    plafond.value = '9'
    plafond.dispatchEvent(new Event('change', { bubbles: true }))
    fireEvent.click(container.querySelectorAll('.rig-bascule')[2]!)
    expect(gestes()).toEqual([['categorie', 'Arrivees'], ['surveiller', 'a1'], ['niveau', '3'], ['tri', 'titre'], ['limite', '9'], ['detail', null]])
  })

  it('Reglages d\'une tuile sans reglage propre : renvoie aux reglages de l\'entete', () => {
    const { geste } = espion()
    const { container } = render(<Reglages vue={{ genre: 'aucun', categories: [], alertes: [], niveauMini: 0, tri: '', limite: 0, afficherDetail: false, mesures: [] }} geste={geste} />)
    expect(container.querySelector('.rig-vide')!.textContent!.replace(/\s+/g, ' ').trim())
      .toBe("Cette tuile n'a pas de reglage propre. Pour la retirer de l'accueil, ouvrir les reglages en haut a droite.")
  })
})
