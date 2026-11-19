import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render } from '@testing-library/react'
import { Accueil } from './Accueil'
import { morceaux } from '../test/morceaux'
import { ligne, panneauTuiles, tuile, vuePlan } from '../test/accueil'
import type { GesteIle, VuePlanAccueil } from '../accueil/types'

afterEach(cleanup)

// Les REGLES de l'accueil (menu du login, chiffres, dispositions, poses,
// alertes...) vivent dans Pont/Accueil.cs et sont jouees par les bancs C#
// (tests/ActiviteCheck, CharteCheck, PerfCheck, MenuGestesCheck, ThemeCheck).
// Ici : le balisage (memes classes, memes morceaux de texte que Razor) et les
// gestes que React remonte a la session par le rappel « geste ».
function rendre(vue: VuePlanAccueil = vuePlan()) {
  const geste = vi.fn<(g: GesteIle) => void>()
  const r = render(<Accueil vue={vue} geste={geste} />)
  return { ...r, geste, gestes: () => geste.mock.calls.map(([g]) => [g.nom, g.valeur]) }
}

describe('Accueil : la coquille et le menu', () => {
  it('pose rig-accueil et sa reduction, le menu puis le plan de travail', () => {
    const { container, rerender, geste } = rendre()
    const racine = container.firstElementChild!
    expect(racine.className).toBe('rig-accueil ')
    expect(morceaux(racine)).toEqual(['<nav>', ' ', '<div>'])
    // La tete du plan (« En-tete noir », 19/11/2026) porte l'entete PUIS la
    // recherche : le bandeau du sombre passe sous les deux.
    expect(morceaux(racine.querySelector('.rig-plan'))).toEqual(['<div>', ' ', '<div>', '<div>'])
    const tete = racine.querySelector('.rig-plan')!.firstElementChild!
    expect(tete.className).toBe('rig-plan__tete')
    expect(morceaux(tete)).toEqual(['<div>', ' ', '<div>'])
    expect(Array.from(tete.children, (e) => e.className)).toEqual(['rig-entete', 'rig-recherche rig-recherche--fixe'])
    rerender(<Accueil vue={vuePlan({ menuReduit: true })} geste={geste} />)
    expect(container.firstElementChild!.className).toBe('rig-accueil rig-accueil--menu-reduit')
    expect(container.querySelector('.rig-menu__replier')!.getAttribute('title')).toBe('Deployer le menu')
    expect(container.querySelector('.rig-menu__replier')!.textContent).toBe('»')
  })

  it('le bouton de repli remonte « menu »', () => {
    const { container, gestes } = rendre()
    expect(container.querySelector('.rig-menu__replier')!.getAttribute('title')).toBe('Reduire le menu')
    fireEvent.click(container.querySelector('.rig-menu__replier')!)
    expect(gestes()).toEqual([['menu', null]])
  })

  it('un metier : chevron, pastille, titre, compte en morceaux ; ouvert, ses verbes et ses ecrans', () => {
    const { container, gestes } = rendre()
    const groupes = container.querySelectorAll('.rig-menu__groupe')
    expect(groupes[0]!.className).toBe('rig-menu__groupe rig-menu__groupe--rcs rig-menu__groupe--ouvert')
    expect(groupes[1]!.className).toBe('rig-menu__groupe rig-menu__groupe--jud ')
    const tete = groupes[0]!.querySelector('.rig-menu__tete')!
    expect(morceaux(tete)).toEqual(['<span>', ' ', '<span>', ' ', '<span>', ' ', '<span>'])
    expect(tete.textContent).toBe('▸ + Ouvrir 3')
    const sous = groupes[0]!.querySelectorAll('.rig-menu__sous')
    expect([...sous].map((s) => s.className)).toEqual(['rig-menu__sous rig-menu__sous--ouvert', 'rig-menu__sous '])
    const lignes = groupes[0]!.querySelectorAll('.rig-menu__ligne')
    expect(lignes).toHaveLength(2)
    expect(morceaux(lignes[0]!)).toEqual(['<button>', ' ', '<button>'])
    const [lien, etoile] = lignes[1]!.querySelectorAll('button')
    expect(lien!.className).toBe('rig-menu__lien rig-menu__lien--metier rig-menu__lien--actif')
    expect(lien!.getAttribute('title')).toBe('une societe arrive')
    expect(etoile!.className).toBe('rig-menu__etoile rig-menu__etoile--allumee')
    expect(etoile!.getAttribute('title')).toBe('Retirer des favoris')
    expect(etoile!.textContent).toBe('★')
    expect(lignes[0]!.querySelector('.rig-menu__etoile')!.textContent).toBe('☆')

    fireEvent.click(tete)
    fireEvent.click(sous[1]!)
    fireEvent.click(lien!)
    fireEvent.click(etoile!)
    expect(gestes()).toEqual([
      ['moment', 'Ouvrir'],
      ['sous', { moment: 'Ouvrir', sous: 'Recevoir' }],
      ['ouvrir', 'CODE_B'],
      ['favori', 'CODE_B'],
    ])
  })

  it('le groupe Favoris n\'existe que s\'il y a une etoile, et se replie', () => {
    const { container, geste, rerender, gestes } = rendre()
    expect(container.querySelector('.rig-menu__groupe--fav')).toBeNull()
    const vue = vuePlan()
    vue.menu.favoris = [{ code: 'CODE_B', libelle: 'Ouvrir un dossier', detail: 'une societe arrive', actif: false }]
    rerender(<Accueil vue={vue} geste={geste} />)
    const fav = container.querySelector('.rig-menu__groupe--fav')!
    expect(fav.className).toBe('rig-menu__groupe rig-menu__groupe--fav rig-menu__groupe--ouvert')
    expect(fav.querySelector('.rig-menu__compte')!.textContent).toBe('1')
    fireEvent.click(fav.querySelector('.rig-menu__tete')!)
    fireEvent.click(fav.querySelector('.rig-menu__etoile')!)
    expect(gestes()).toEqual([['favorisOuverts', null], ['favori', 'CODE_B']])
  })
})

describe('Accueil : l\'entete', () => {
  it('la phrase du jour garde les morceaux de Razor', () => {
    const { container } = rendre()
    const phrase = container.querySelector('.rig-proue__phrase')!
    expect(morceaux(phrase)).toEqual(['<b>', ' depassent le delai legal', '<span>', '<b>', ' a traiter\n', '<span>', ' ', '<b>', ' ', '<span>'])
    expect(morceaux(phrase.querySelector('.rig-proue__alerte'))).toEqual(['2', ' ', 'dossiers'])
    expect(morceaux(phrase.querySelectorAll('b')[1]!)).toEqual(['66', ' ', 'arrivees'])
    expect(morceaux(container.querySelector('.rig-titre'))).toEqual(['\n                    Bonjour ', '<b>'])
  })

  it('au singulier, puis sans hors-delai ni traites releves', () => {
    const vue = vuePlan()
    vue.entete.phrase = { horsDelaiConnu: true, horsDelai: 0, arrivees: 1, traites: null }
    const { container } = rendre(vue)
    const phrase = container.querySelector('.rig-proue__phrase')!
    expect(morceaux(phrase)).toEqual(['<span>', '<span>', '<b>', ' a traiter\n'])
    expect(phrase.textContent).toBe('Aucun dossier hors delai legal·1 arrivee a traiter\n')
  })

  it('le panneau des tuiles : theme, dispositions, bascules, reinitialisation ; le voile le ferme', () => {
    const vue = vuePlan()
    vue.entete.panneauOuvert = true
    vue.entete.panneau = panneauTuiles()
    const { container, gestes } = rendre(vue)
    expect(container.querySelector('.rig-entete__action')!.className).toBe('rig-entete__action rig-entete__action--ouvert')
    const panneau = container.querySelector('.rig-tuiles-panneau')!
    expect(morceaux(panneau)).toEqual(['<label>', '<div>', ' ', '<span>', ' ', '<div>', '<button>', '<button>'])
    expect([...panneau.querySelectorAll('.rig-disposition')].map((b) => b.className)).toEqual(['rig-disposition rig-disposition--actif', 'rig-disposition '])
    expect([...panneau.querySelectorAll('.rig-bascule')].map((b) => b.className)).toEqual(['rig-bascule rig-bascule--actif', 'rig-bascule '])
    fireEvent.click(panneau.querySelector('.rig-tuiles-panneau__reset')!)
    fireEvent.click(panneau.querySelectorAll('.rig-disposition')[1]!)
    fireEvent.click(panneau.querySelectorAll('.rig-bascule')[1]!)
    fireEvent.click(container.querySelector('.rig-voile-clic')!)
    expect(gestes()).toEqual([['reinitialiser', null], ['disposition', 'metiers'], ['tuile', 'activite'], ['fermerPanneauTuiles', null]])
  })

  // Le theme etait dans un panneau de la barre du haut : depuis le 19/11/2026
  // (la barre ne porte plus que les onglets), il est en tete de ces reglages.
  it('reglages de l\'accueil : la liste des themes ; le choix part a la session', () => {
    const vue = vuePlan()
    vue.entete.panneauOuvert = true
    vue.entete.panneau = panneauTuiles({ theme: 'clair' })
    const { container, gestes } = rendre(vue)
    const champ = container.querySelector('.rig-tuiles-panneau')!.firstElementChild!
    expect(champ.className).toBe('rig-saisie-champ rig-tuiles-panneau__theme')
    expect(champ.querySelector('.rig-saisie-champ__libelle')!.textContent).toBe('Theme')
    const liste = champ.querySelector<HTMLSelectElement>('select#rig-choix-theme')!
    expect(liste.className).toBe('rig-saisie-champ__saisie')
    expect([...liste.options].map((o) => [o.value, o.textContent])).toEqual([['clair', 'Clair'], ['sombre', 'Sombre']])
    expect(liste.value).toBe('clair')
    expect(container.textContent).not.toContain('Mode sombre')
    fireEvent.change(liste, { target: { value: 'sombre' } })
    expect(gestes()).toEqual([['theme', 'sombre']])
  })

  it('la liste des themes garde le choix de l\'utilisateur jusqu\'a la reponse, puis suit la session', () => {
    const avec = (theme: string) => {
      const vue = vuePlan()
      vue.entete.panneauOuvert = true
      vue.entete.panneau = panneauTuiles({ theme })
      return vue
    }
    const { container, rerender, geste } = rendre(avec('clair'))
    const liste = container.querySelector<HTMLSelectElement>('#rig-choix-theme')!
    fireEvent.change(liste, { target: { value: 'sombre' } })
    rerender(<Accueil vue={avec('clair')} geste={geste} />)
    expect(liste.value).toBe('sombre')
    rerender(<Accueil vue={avec('sombre')} geste={geste} />)
    rerender(<Accueil vue={avec('clair')} geste={geste} />)
    expect(liste.value).toBe('clair')
  })

  it('le rond de profil est A DROITE DU TITRE quand l\'accueil est visible ; il change le login', () => {
    const vue = vuePlan()
    vue.entete.profil = {
      ouvert: true, initiales: 'RV', titre: 'rvilain · Greffier (RCS + JUD) : changer de login', login: 'rvilain',
      comptes: [
        { login: 'rvilain', initiales: 'RV', profil: 'Greffier', domaines: 'RCS + JUD', actif: true },
        { login: 'pjudic', initiales: 'PJ', profil: 'Redacteur judiciaire', domaines: 'JUD', actif: false },
      ],
    }
    const { container, gestes } = rendre(vue)
    const outils = container.querySelector('.rig-entete__outils')!
    expect(outils.lastElementChild!.className).toBe('rig-profil-ancre')
    const rond = outils.querySelector('.rig-profil')!
    expect(rond.className).toBe('rig-profil rig-profil--ouvert')
    expect(rond.getAttribute('aria-label')).toBe('Profil rvilain : changer de login')
    expect(rond.getAttribute('aria-expanded')).toBe('true')
    const comptes = outils.querySelectorAll('.rig-compte')
    expect([...comptes].map((c) => c.getAttribute('aria-pressed'))).toEqual(['true', 'false'])
    expect(morceaux(comptes[1]!.querySelector('.rig-compte__nom'))).toEqual(['pjudic', ' · ', 'Redacteur judiciaire'])
    fireEvent.click(comptes[1]!)
    fireEvent.click(outils.querySelector('.rig-voile-clic')!)
    fireEvent.click(rond)
    expect(gestes()).toEqual([['login', 'pjudic'], ['fermerProfil', null], ['profil', null]])
  })

  it('un ecran recouvre l\'accueil : plus de rond dans l\'entete', () => {
    const vue = vuePlan()
    vue.entete.profil = null
    const { container } = rendre(vue)
    expect(container.querySelector('.rig-entete__outils .rig-profil-ancre')).toBeNull()
  })
})

describe('Accueil : la recherche', () => {
  it('la pastille « Ctrl K » est un bouton qui ouvre la palette', () => {
    const { container, gestes } = rendre()
    const pastille = container.querySelector<HTMLButtonElement>('.rig-recherche button.rig-touche')!
    expect(pastille.textContent).toBe('Ctrl K')
    fireEvent.click(pastille)
    expect(gestes()).toEqual([['palette', null]])
  })

  it('le texte remonte a chaque frappe et ne s\'ecrase pas tant que la session n\'a pas change', () => {
    const { container, gestes, rerender, geste } = rendre()
    const saisie = container.querySelector<HTMLInputElement>('.rig-recherche__saisie')!
    expect(saisie.placeholder).toBe('Rechercher un SIREN, une societe, un dirigeant, un n° de demande…')
    fireEvent.input(saisie, { target: { value: 'fact' } })
    expect(gestes()).toEqual([['recherche', 'fact']])
    expect(container.querySelector('.rig-resultats')).toBeNull()
    // Une affaire ouverte vide la recherche cote C# : le champ suit.
    const vue = vuePlan()
    vue.recherche.texte = 'fact'
    rerender(<Accueil vue={vue} geste={geste} />)
    rerender(<Accueil vue={vuePlan()} geste={geste} />)
    expect(saisie.value).toBe('')
  })

  it('les resultats : traitements, affaires, elements, en morceaux ; un clic remonte le RANG', () => {
    const vue = vuePlan()
    vue.recherche = {
      texte: 'ami',
      traitements: [{ libelle: 'Factures' }],
      affaires: [{ numero: '004182', objet: 'Immatriculation - AMIENS', siren: '109 678 235', type: 'RCS', statut: 'action' }],
      elements: [{ statut: 'alerte', nom: 'Depot 2024/0141', numero: '2026B01067', chemin: 'Dossier RCS > Depots' }],
    }
    const { container, gestes } = rendre(vue)
    const [t, a, e] = [...container.querySelectorAll('.rig-resultat')]
    expect(morceaux(t!)).toEqual(['<span>', ' ', '<span>', ' ', '<span>', ' ', '<span>'])
    expect(morceaux(t!.children[2]!)).toEqual(['Factures', '<br>', ' ', '<span>'])
    expect(a!.querySelector('.rig-filet-v')!.className).toBe('rig-filet-v rig-filet-v--action')
    expect(morceaux(a!.children[2]!.querySelector('span'))).toEqual(['SIREN ', '109 678 235'])
    expect(morceaux(e!.children[2]!.querySelector('span'))).toEqual(['2026B01067', ' · ', 'Dossier RCS > Depots'])
    expect(e!.querySelector('.rig-resultat__type')!.textContent).toBe('Element')
    fireEvent.click(t!); fireEvent.click(a!); fireEvent.click(e!)
    expect(gestes()).toEqual([['traitement', 0], ['affaire', 0], ['element', 0]])
  })
})

describe('Accueil : chiffres du jour et « Proue »', () => {
  it('trois boutons chiffres ; les anneaux portent leur cible ; le troisieme bat en alerte', () => {
    const { container, gestes } = rendre()
    const chiffres = container.querySelectorAll('.rig-chiffre')
    expect([...chiffres].map((c) => c.className)).toEqual(['rig-chiffre rig-chiffre--1 ', 'rig-chiffre rig-chiffre--2 ', 'rig-chiffre rig-chiffre--3 '])
    expect(container.querySelectorAll('.rig-chiffre__rond')).toHaveLength(3)
    expect(chiffres[0]!.getAttribute('title')).toBe('Deplier la liste des arrivees')
    expect(chiffres[0]!.getAttribute('aria-expanded')).toBe('false')
    expect((chiffres[1]!.querySelector('.rig-chiffre__cercle') as HTMLElement).style.getPropertyValue('--rig-anneau-cible')).toBe('44')
    expect(chiffres[2]!.querySelector('.rig-chiffre__anneau')!.className).toBe('rig-chiffre__anneau rig-chiffre__anneau--alerte')
    expect(chiffres[0]!.querySelector('.rig-chiffre__detail')!.getAttribute('title')).toBe('DAS')
    fireEvent.click(chiffres[2]!)
    expect(gestes()).toEqual([['chiffre', 3]])
    expect(container.querySelector('.rig-grille')!.className).toBe('rig-grille ')
  })

  it('le chiffre deplie : sa liste, puis les tuiles en resume ; la grille se replie', () => {
    const vue = vuePlan()
    vue.chiffres.deplie = 3
    vue.proue = {
      rang: 3, compte: '2', lien: true, arrivees: [], mesures: [],
      horsDelai: [{ dossier: 'J-00931', nom: 'TRANSNORD SA', jours: 11, numero: '004151', creation: '08/11/2026' }],
      resumes: [
        { id: 'alertes', titre: 'A traiter', ligne: [{ morceaux: [{ texte: '66', gras: true }, { texte: ' ', gras: false }, { texte: 'Arrivees', gras: false }], blancAvant: false }] },
        { id: 'performance', titre: 'Performance', ligne: [
          { morceaux: [{ texte: 'Rejets', gras: false }, { texte: ' ', gras: false }, { texte: '1', gras: true }], blancAvant: false },
          { morceaux: [{ texte: 'obj. <= 1', gras: false }], blancAvant: true },
        ] },
      ],
    }
    const { container, gestes } = rendre(vue)
    expect(container.querySelector('.rig-chiffre--3')!.className).toBe('rig-chiffre rig-chiffre--3 rig-chiffre--deplie')
    expect(container.querySelector('.rig-proue-pointe')!.className).toBe('rig-proue-pointe rig-proue-pointe--3')
    const liste = container.querySelector('.rig-proue-liste')!
    expect(liste.getAttribute('aria-label')).toBe('Hors delai legal')
    expect(morceaux(liste.querySelector('.rig-proue-liste__titre'))).toEqual(['Hors delai legal · ', '<b>'])
    const detail = liste.querySelector('.rig-alerte__detail')!
    expect(morceaux(detail)).toEqual(['J+', '11', ' · demande ', '004151', ' · creee le ', '08/11/2026'])
    expect(morceaux(liste.querySelector('.rig-alerte__vers'))).toEqual(['affaire ', 'J-00931', ' ›'])
    const lignes = container.querySelectorAll('.rig-proue-resume__ligne')
    expect(morceaux(lignes[0]!)).toEqual(['<span>'])
    expect(morceaux(lignes[0]!.firstElementChild)).toEqual(['<b>', ' ', 'Arrivees'])
    expect(morceaux(lignes[1]!)).toEqual(['<span>', ' ', '<span>'])
    expect(container.querySelector('.rig-grille')!.className).toBe('rig-grille rig-grille--repliee')
    fireEvent.click(liste.querySelector('.rig-proue-liste__lien')!)
    fireEvent.click(liste.querySelector('.rig-alerte')!)
    fireEvent.click(container.querySelectorAll('.rig-proue-resume')[1]!)
    expect(gestes()).toEqual([['voirTuile', 'taches'], ['numero', 'J-00931'], ['voirTuile', 'performance']])
  })

  it('les arrivees depliees remontent l\'alerte cliquee ; sans tuile visible, pas de lien', () => {
    const vue = vuePlan()
    vue.chiffres.deplie = 1
    vue.proue = { rang: 1, compte: '22', lien: false, arrivees: [ligne('a1', 22, 'DAS a numeriser')], mesures: [], horsDelai: [], resumes: [] }
    const { container, gestes } = rendre(vue)
    expect(container.querySelector('.rig-proue-liste__lien')).toBeNull()
    expect(morceaux(container.querySelector('.rig-proue-liste .rig-alerte__vers'))).toEqual(['Ecran a1', ' ›'])
    fireEvent.click(container.querySelector('.rig-proue-liste .rig-alerte')!)
    expect(gestes()).toEqual([['alerte', 'a1']])
  })
})

describe('Accueil : la grille de tuiles', () => {
  it('chaque tuile porte sa pose (data-* et style) et son rang ; un widget metier n\'a pas de titre d\'en-tete', () => {
    const vue = vuePlan()
    vue.tuiles.push(tuile('metier-das', { colonne: 10, ligne: 6, largeur: 3, hauteur: 3, rang: 96, devant: true, maitresse: false }))
    vue.tuiles[0]!.maitresse = true
    const { container } = rendre(vue)
    const [alertes, , metier] = [...container.querySelectorAll<HTMLElement>('.rig-widget')]
    expect(alertes!.className).toBe('rig-widget rig-widget--maitresse ')
    expect(alertes!.dataset).toMatchObject({ tuile: 'alertes', col: '7', ligne: '1', larg: '6', haut: '3' })
    expect(alertes!.style.gridColumn).toBe('7 / span 6')
    expect(alertes!.style.gridRow).toBe('1 / span 3')
    expect(alertes!.style.zIndex).toBe('2')
    expect(metier!.className).toBe('rig-widget  rig-widget--devant')
    expect(metier!.style.zIndex).toBe('96')
    expect(morceaux(alertes!)).toEqual(['<header>', ' ', '<div>', ' ', '<span>'])
    expect(morceaux(alertes!.querySelector('.rig-widget__tete'))).toEqual(['<span>', '<span>', '<button>'])
    expect(morceaux(metier!.querySelector('.rig-widget__tete'))).toEqual(['<span>', '<button>'])
    expect(alertes!.querySelector('.rig-widget__poignee')!.textContent).toBe('⋮⋮')
  })

  it('l\'engrenage d\'une tuile remonte « reglagesTuile » et n\'amorce pas de deplacement', () => {
    const { container, gestes } = rendre()
    const bouton = container.querySelector('.rig-widget__reglages')!
    fireEvent.pointerDown(bouton, { button: 0, clientX: 10, clientY: 10 })
    expect(container.querySelector('.rig-grille')!.className).toBe('rig-grille ')
    fireEvent.click(bouton)
    expect(gestes()).toEqual([['reglagesTuile', 'alertes']])
  })

  it('GLISSER-DEPOSER : la tuile suit le pointeur case par case, puis sa case part au C#', () => {
    const { container, gestes, geste, rerender } = rendre()
    const grille = container.querySelector<HTMLElement>('.rig-grille')!
    // 12 colonnes de 88 px + 11 ecarts de 12 px = 1188 px : un pas de 100 px.
    grille.getBoundingClientRect = () => ({ width: 1188, height: 600, top: 0, left: 0, right: 1188, bottom: 600, x: 0, y: 0, toJSON: () => '' })
    const perf = container.querySelector<HTMLElement>('[data-tuile="performance"]')!
    fireEvent.pointerDown(perf.querySelector('.rig-widget__titre')!, { button: 0, clientX: 50, clientY: 20, pointerId: 1 })
    expect(grille.className).toBe('rig-grille  rig-grille--pose')
    expect(perf.className).toContain('rig-widget--saisi')
    expect(perf.style.zIndex).toBe('60')
    act(() => { window.dispatchEvent(new MouseEvent('pointermove', { clientX: 350, clientY: 20 + 128 * 2 })) })
    expect(perf.style.gridColumn).toBe('4 / span 6')
    expect(perf.style.gridRow).toBe('6 / span 2')
    expect(perf.dataset.col).toBe('4')
    // Jamais au-dela du bord droit : 12 - 6 + 1 = 7.
    act(() => { window.dispatchEvent(new MouseEvent('pointermove', { clientX: 5000, clientY: 20 })) })
    expect(perf.style.gridColumn).toBe('7 / span 6')
    act(() => { window.dispatchEvent(new MouseEvent('pointerup', {})) })
    expect(gestes()).toEqual([['poser', { id: 'performance', colonne: 7, ligne: 4 }]])
    expect(grille.className).toBe('rig-grille ')
    expect(perf.className).not.toContain('rig-widget--saisi')
    // Jusqu'au retour de la vue, la tuile reste ou on l'a lachee (rang d'origine).
    expect(perf.style.gridColumn).toBe('7 / span 6')
    expect(perf.style.zIndex).toBe('3')
    const vue = vuePlan()
    vue.tuiles[1] = { ...vue.tuiles[1]!, colonne: 7, rang: 9 }
    rerender(<Accueil vue={vue} geste={geste} />)
    expect(perf.style.gridColumn).toBe('7 / span 6')
    expect(perf.style.zIndex).toBe('9')
  })

  it('REDIMENSIONNER par la poignee : 3 colonnes au moins, jamais au-dela du bord', () => {
    const { container, gestes } = rendre()
    const grille = container.querySelector<HTMLElement>('.rig-grille')!
    grille.getBoundingClientRect = () => ({ width: 1188, height: 600, top: 0, left: 0, right: 1188, bottom: 600, x: 0, y: 0, toJSON: () => '' })
    const alertes = container.querySelector<HTMLElement>('[data-tuile="alertes"]')!
    fireEvent.pointerDown(alertes.querySelector('.rig-widget__taille')!, { button: 0, clientX: 600, clientY: 300, pointerId: 1 })
    act(() => { window.dispatchEvent(new MouseEvent('pointermove', { clientX: 0, clientY: 300 + 128 })) })
    expect(alertes.style.gridColumn).toBe('7 / span 3')
    expect(alertes.style.gridRow).toBe('1 / span 4')
    act(() => { window.dispatchEvent(new MouseEvent('pointermove', { clientX: 5000, clientY: 300 })) })
    expect(alertes.style.gridColumn).toBe('7 / span 6')
    act(() => { window.dispatchEvent(new MouseEvent('pointerup', {})) })
    expect(gestes()).toEqual([['dimensionner', { id: 'alertes', largeur: 6, hauteur: 3 }]])
  })

  it('un appui hors de l\'en-tete ou du bouton gauche ne deplace rien', () => {
    const { container, gestes } = rendre()
    const alertes = container.querySelector<HTMLElement>('[data-tuile="alertes"]')!
    fireEvent.pointerDown(alertes.querySelector('.rig-widget__corps')!, { button: 0, clientX: 1, clientY: 1 })
    fireEvent.pointerDown(alertes.querySelector('.rig-widget__titre')!, { button: 2, clientX: 1, clientY: 1 })
    act(() => { window.dispatchEvent(new MouseEvent('pointerup', {})) })
    expect(gestes()).toEqual([])
  })

  it('« Proue » : la tuile signalee defile dans la vue et s\'entoure d\'or un instant', () => {
    vi.useFakeTimers()
    try {
      const { container, geste, rerender } = rendre()
      const defiler = vi.fn()
      Element.prototype.scrollIntoView = defiler
      rerender(<Accueil vue={vuePlan({ signal: { id: 'performance', numero: 1 } })} geste={geste} />)
      const perf = container.querySelector<HTMLElement>('[data-tuile="performance"]')!
      expect(defiler).toHaveBeenCalledWith({ block: 'nearest', behavior: 'smooth' })
      expect(perf.className).toContain('rig-widget--signale')
      // Une nouvelle vue (meme numero) ne relance pas le signal ni ne coupe la minuterie.
      rerender(<Accueil vue={vuePlan({ signal: { id: 'performance', numero: 1 } })} geste={geste} />)
      expect(defiler).toHaveBeenCalledTimes(1)
      act(() => { vi.advanceTimersByTime(1400) })
      expect(perf.className).not.toContain('rig-widget--signale')
    } finally {
      vi.useRealTimers()
    }
  })
})
