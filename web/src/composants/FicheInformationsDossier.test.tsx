import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { FicheInformationsDossier, type VueFiche } from './FicheInformationsDossier'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { morceaux } from '../test/morceaux'

afterEach(() => { cleanup(); retirerFauxPont() })

// Etat et regles de la fiche (etapes, vues, saisie guidee, verrou, tache,
// historique) vivent dans la session de Pont/FicheInformationsDossier.cs,
// executee par tests/EtapesInfoCheck, GedCheck, SaisieCheck et SimuEdiCheck.
// Ici : la fiche lit la vue de la session ouverte par la coquille, rend le meme
// balisage, remonte chaque geste et les rappels que la session leve.
function vue(plus: Partial<VueFiche> = {}): VueFiche {
  return {
    session: 's1', domaine: 'Dossier RCS', champs: 523, etapes: 6, blocs: 31, titre: 'Informations du dossier',
    numero: '004176', sources: 'REPRIAC, REPRIPP', menuActions: false, actions: [], historique: false,
    derniereAction: null, entrees: [], tache: null, verrou: null, filtre: '',
    frise: {
      page: 0, precedentInactif: true, suivantInactif: false,
      pas: [
        { index: 0, titre: 'Identite', blocs: 3, remplis: 1, total: 123, etat: 'encours', courant: true, bloquants: 3, jauge: 0 },
        { index: 1, titre: 'Personnes liees', blocs: 5, remplis: 0, total: 205, etat: 'vide', courant: false, bloquants: 0, jauge: 0 },
      ],
    },
    remplissage: { choix: 2, remplis: 1, aSaisir: 522, total: 523, aucunChamp: false, guide: false },
    guide: null, lectureSeule: false,
    sections: [{
      s: 0, titre: 'Dossier RCS', table: 'DOSSIER_RCS', champs: [
        { c: 0, libelle: 'Etat Dossier', processus: 'REPRIAC', cle: 'DOSSIER_RCS.ETAT', valeur: 'Actif', titreColonne: 'En base : DSSRC_ETAT' },
        { c: 3, libelle: 'N° SIREN', processus: 'REPRIAC', cle: 'DOSSIER_RCS.SIREN', valeur: '', titreColonne: 'DOSSIER_RCS.SIREN' },
      ],
    }],
    rappels: [],
    ...plus,
  }
}

function monter(reponses: Record<string, (...a: unknown[]) => unknown>, version = 0) {
  const pont = installerFauxPont(reponses)
  const rappels = { fermer: vi.fn(), toutAffiche: vi.fn(), champModifie: vi.fn() }
  const r = render(<FicheInformationsDossier session="s1" version={version} {...rappels} />)
  return { ...r, pont, ...rappels }
}

describe('FicheInformationsDossier', () => {
  it('lit la vue de la session ouverte par la coquille ; l\'entete en morceaux Razor', async () => {
    const { container, pont } = monter({ 'FicheInformationsDossier.Vue': () => vue() })
    await screen.findByText('Informations du dossier')
    expect(pont.appels).toHaveBeenCalledWith('FicheInformationsDossier.Vue', 's1')
    expect(morceaux(container.querySelector('.rig-guide__entete .rig-kicker')))
      .toEqual(['Dossier RCS', ' · ', '523', ' champs · ', '6', ' étapes · ', '31', ' blocs'])
    expect(morceaux(container.querySelector('.rig-guide__source')))
      .toEqual(['004176', ' · source RIG v2 : ', 'REPRIAC, REPRIPP'])
    expect(container.querySelector('.rig-fiche__outils > button[aria-pressed]')).toBeNull()
    expect(screen.getByText('Historique').className).toBe('rig-bouton rig-bouton--plat')
  })

  it('la frise a jauges : etape courante, requis restants, compte en morceaux, fleches', async () => {
    const { container } = monter({ 'FicheInformationsDossier.Vue': () => vue() })
    await screen.findByText('Identite')
    const ol = container.querySelector('ol.rig-frise.rig-frise--h.rig-infos__frise')!
    const pas = ol.querySelectorAll('li.rig-frise__pas')
    expect(pas[0]!.className).toBe('rig-frise__pas rig-frise__pas--encours rig-frise__pas--courant')
    expect(pas[1]!.className).toBe('rig-frise__pas rig-frise__pas--vide ')
    expect(pas[0]!.querySelector('button')!.getAttribute('title')).toBe('Identite - 3 bloc(s), 123 champs')
    expect(morceaux(pas[0]!.querySelector('.rig-frise__requis-restants'))).toEqual(['3', ' requis'])
    expect(morceaux(pas[1]!.querySelector('.rig-frise__compte'))).toEqual(['0', ' / ', '205'])
    expect(pas[1]!.querySelector('.rig-frise__jauge-part')!.getAttribute('style')).toBe('width: 0%;')
    const fleches = ol.querySelectorAll('.rig-frise__fleche-bouton')
    expect((fleches[0] as HTMLButtonElement).disabled).toBe(true)
    expect((fleches[1] as HTMLButtonElement).disabled).toBe(false)
  })

  it('les vues de remplissage et la liste des champs (cle de trace, colonne reelle en infobulle)', async () => {
    const { container } = monter({ 'FicheInformationsDossier.Vue': () => vue() })
    await screen.findByText('Etat Dossier')
    const choix = [...container.querySelectorAll('.rig-infos__vue-choix')]
    expect(choix.map((b) => morceaux(b))).toEqual([['Renseignes ', '1'], ['A saisir ', '522'], ['Tous ', '523']])
    expect(choix[2]!.className).toBe('rig-infos__vue-choix rig-infos__vue-choix--actif')
    const details = container.querySelector('details.rig-infos__section')!
    expect(details.hasAttribute('open')).toBe(true)
    expect(morceaux(details.querySelector('summary.rig-infos__titre'))).toEqual(['Dossier RCS', '<span>', '<span>'])
    const label = details.querySelector('label.rig-saisie-champ.rig-saisie-champ--moitie')!
    expect(label.getAttribute('title')).toBe('REPRIAC')
    expect((label.querySelector('input.rig-saisie-champ__saisie') as HTMLInputElement).value).toBe('Actif')
    const colonne = label.querySelector('.rig-saisie-champ__colonne')!
    expect([colonne.textContent, colonne.getAttribute('title')]).toEqual(['DOSSIER_RCS.ETAT', 'En base : DSSRC_ETAT'])
  })

  it('une saisie part a la session au « change », et le rappel champModifie remonte a la coquille', async () => {
    const agir = vi.fn(() => vue({ rappels: ['champModifie'] }))
    const { container, champModifie } = monter({ 'FicheInformationsDossier.Vue': () => vue(), 'FicheInformationsDossier.Agir': agir })
    await screen.findByText('N° SIREN')
    const siren = container.querySelectorAll('.rig-champs input.rig-saisie-champ__saisie')[1] as HTMLInputElement
    siren.value = '812445901'
    fireEvent.input(siren)
    expect(agir).not.toHaveBeenCalled()
    fireEvent.change(siren)
    await waitFor(() => expect(champModifie).toHaveBeenCalledTimes(1))
    expect(agir).toHaveBeenCalledWith('s1', 'saisir', { s: 0, c: 3, valeur: '812445901' })
  })

  it('le filtre part a chaque frappe ; une etape de la frise et les vues sont des gestes de la session', async () => {
    const agir = vi.fn((_s: unknown, _geste: unknown, _valeur: unknown) => vue())
    const { container } = monter({ 'FicheInformationsDossier.Vue': () => vue(), 'FicheInformationsDossier.Agir': agir })
    await screen.findByText('Identite')
    const filtre = container.querySelector('input.rig-saisie-champ__saisie.rig-infos__filtre') as HTMLInputElement
    expect(filtre.getAttribute('type')).toBe('search')
    expect(filtre.getAttribute('placeholder')).toBe('Filtrer un champ (libelle ou colonne)...')
    fireEvent.input(filtre, { target: { value: 'siren' } })
    fireEvent.click(screen.getByText('Personnes liees'))
    fireEvent.click(screen.getByText('A saisir', { exact: false }))
    fireEvent.click(container.querySelectorAll('.rig-frise__fleche-bouton')[1]!)
    await waitFor(() => expect(agir).toHaveBeenCalledTimes(4))
    expect(agir.mock.calls.map((c) => [c[1], c[2]])).toEqual([['filtre', 'siren'], ['etape', 1], ['remplissage', 1], ['page', 1]])
  })

  it('la VUE VIDE dit pourquoi et propose la vue qui montre des champs', async () => {
    // Un dossier sans aucun champ renseigne s'ouvre sur « Renseignes 0 » : la carte restait blanche.
    const rien = vue({ remplissage: { choix: 0, remplis: 0, aSaisir: 523, total: 523, aucunChamp: true, guide: false }, sections: [] })
    const agir = vi.fn((_s: unknown, _geste: unknown, _valeur: unknown) => rien)
    const { container } = monter({ 'FicheInformationsDossier.Vue': () => rien, 'FicheInformationsDossier.Agir': agir })
    const vide = await waitFor(() => { const v = container.querySelector('.rig-infos__vide'); expect(v).not.toBeNull(); return v! })
    expect(vide.querySelector('b')!.textContent).toBe('Aucun champ renseigne pour ce dossier.')
    const boutons = [...vide.querySelectorAll('button')]
    expect(boutons.map((b) => b.textContent)).toEqual(['Voir les champs a saisir (523)'])
    fireEvent.click(boutons[0]!)
    await waitFor(() => expect(agir).toHaveBeenCalledWith('s1', 'remplissage', 1))
    // L'ancienne mention en petites capitales a disparu de la rangee des vues.
    expect(container.querySelector('.rig-infos__vue')!.textContent).not.toContain('aucun champ')
  })

  it('vue vide alors que d\'autres champs existent : elle propose les deux autres vues utiles', async () => {
    const tout = vue({ remplissage: { choix: 1, remplis: 4, aSaisir: 0, total: 4, aucunChamp: true, guide: false }, sections: [] })
    const { container } = monter({ 'FicheInformationsDossier.Vue': () => tout })
    const vide = await waitFor(() => { const v = container.querySelector('.rig-infos__vide'); expect(v).not.toBeNull(); return v! })
    expect(vide.querySelector('b')!.textContent).toBe('Aucun champ dans cette vue.')
    expect([...vide.querySelectorAll('button')].map((b) => b.textContent)).toEqual(['Voir les champs renseignes (4)'])
  })

  it('des champs a l\'ecran : pas de vue vide', async () => {
    const { container } = monter({ 'FicheInformationsDossier.Vue': () => vue() })
    await screen.findByText('Etat Dossier')
    expect(container.querySelector('.rig-infos__vide')).toBeNull()
  })

  it('le menu Actions XACTION et la note de l\'action demandee', async () => {
    const actions = [{ code: '1GDMNX', libelle: 'Demande du XACTION' }, { code: 'XRBODAC', libelle: 'Corriger le BODACC' }]
    const agir = vi.fn((_s: unknown, geste: unknown, _valeur: unknown) => geste === 'menuActions'
      ? vue({ menuActions: true, actions })
      : vue({ derniereAction: actions[1]! }))
    const { container } = monter({ 'FicheInformationsDossier.Vue': () => vue(), 'FicheInformationsDossier.Agir': agir })
    fireEvent.click(await screen.findByText('Actions', { exact: false }))
    const menu = await screen.findByRole('menu')
    expect(menu.className).toBe('rig-fiche__actions')
    expect(container.querySelector('.rig-fiche__menu > button')!.getAttribute('aria-expanded')).toBe('true')
    const item = menu.querySelectorAll('button[role="menuitem"]')[1]!
    expect(item.getAttribute('title')).toBe('XRBODAC')
    expect(morceaux(item)).toEqual(['<span>', '<span>'])
    fireEvent.click(item)
    const note = await screen.findByText('Corriger le BODACC', { exact: false, selector: '.rig-fiche__note' })
    expect(morceaux(note)).toEqual(['Action demandée : ', 'Corriger le BODACC', ' (', 'XRBODAC', ') — notée au journal du dossier.'])
    expect(agir.mock.calls.map((c) => [c[1], c[2]])).toEqual([['menuActions', null], ['choisir', 'XRBODAC']])
  })

  it('l\'historique REMPLACE le formulaire par le journal des modifications', async () => {
    const entree = { initiales: 'JD', auteur: 'jean.dupont', champ: 'N° parution', element: 'Formalites > Bodacc',
                     avant: '(vide)', apres: '13', quand: '19/11/2026 08:30', colonne: 'BODACC.NUM_PARUTION' }
    const { container } = monter({
      'FicheInformationsDossier.Vue': () => vue({ historique: true, entrees: [entree], frise: null, remplissage: null, sections: [] }),
    })
    await screen.findByText('Formulaire')
    expect(container.querySelector('.rig-infos')).toBeNull()
    expect(container.querySelector('.rig-fiche__outils > button[aria-pressed="true"]')!.textContent).toBe('Formulaire')
    const art = container.querySelector('article.rig-histo__entree')!
    expect(art.querySelector('.rig-histo__pastille')!.textContent).toBe('JD')
    expect(morceaux(art.querySelector('.rig-histo__phrase')))
      .toEqual(['<strong>', ' a modifié ', '<strong>', ' dans ', '<em>'])
    expect(art.querySelector('.rig-histo__avant')!.textContent).toBe('(vide)')
    expect(morceaux(art.querySelector('.rig-histo__meta'))).toEqual(['19/11/2026 08:30', ' · ', 'BODACC.NUM_PARUTION'])
  })

  it('un historique vide le dit, avec le numero du dossier en morceau', async () => {
    const { container } = monter({ 'FicheInformationsDossier.Vue': () => vue({ historique: true }) })
    const vide = await screen.findByText('Aucune modification enregistrée pour', { exact: false, selector: 'p.rig-histo__vide' })
    expect(container.querySelector('article')).toBeNull()
    expect(morceaux(vide)).toEqual(['Aucune modification enregistrée pour ', '004176', '.'])
  })

  it('le verrou KBIS : lecture seule, demande, brouillon, attente', async () => {
    const lecture = vue({ verrou: { enAttente: false, attente: 0, enEdition: false, brouillon: 0 }, lectureSeule: true })
    const r1 = monter({ 'FicheInformationsDossier.Vue': () => lecture })
    await screen.findByText('Demander une modification')
    expect(r1.container.querySelector('.rig-verrou')!.className).toBe('rig-verrou ')
    expect((r1.container.querySelector('.rig-champs input') as HTMLInputElement).readOnly).toBe(true)
    cleanup(); retirerFauxPont()

    const edition = vue({ verrou: { enAttente: false, attente: 0, enEdition: true, brouillon: 0 } })
    const r2 = monter({ 'FicheInformationsDossier.Vue': () => edition })
    await screen.findByText('Envoyer la demande')
    expect(r2.container.querySelector('.rig-verrou')!.className).toBe('rig-verrou rig-verrou--edition')
    expect((screen.getByText('Envoyer la demande') as HTMLButtonElement).disabled).toBe(true)
    expect(morceaux(r2.container.querySelector('.rig-verrou__texte')))
      .toEqual(['Modification en cours — ', '0', ' champ(s) modifie(s).'])
    cleanup(); retirerFauxPont()

    const attente = vue({ verrou: { enAttente: true, attente: 1, enEdition: false, brouillon: 0 } })
    const r3 = monter({ 'FicheInformationsDossier.Vue': () => attente })
    await screen.findByText('Modification en attente')
    expect(r3.container.querySelector('.rig-verrou')!.className).toBe('rig-verrou rig-verrou--attente')
    expect(morceaux(r3.container.querySelector('.rig-verrou__texte'))[0]).toBe('1')
  })

  it('le mode tache : bandeau, bouton qui rouvre tout, et le rappel toutAffiche remonte', async () => {
    const tache = vue({ tache: { code: 'DENO', titre: 'Renommer la societe', tousChamps: false, caches: 520 }, frise: null, remplissage: null })
    const agir = vi.fn(() => vue({ tache: { ...tache.tache!, tousChamps: true }, rappels: ['toutAffiche'] }))
    const { container, toutAffiche } = monter({ 'FicheInformationsDossier.Vue': () => tache, 'FicheInformationsDossier.Agir': agir })
    const bouton = await screen.findByText('Afficher tous les champs (520 masques)')
    expect(container.querySelector('.rig-tache__code')!.textContent).toBe('DENO')
    expect(container.querySelector('.rig-infos__vue')).toBeNull()
    fireEvent.click(bouton)
    await waitFor(() => expect(toutAffiche).toHaveBeenCalledTimes(1))
    await screen.findByText('Revenir aux champs de la tache')
  })

  it('la saisie guidee : kicker en morceaux, jauge, focus rendu, Entree envoie le texte tape', async () => {
    const guide = (rang: number, focus: number) => vue({
      remplissage: { choix: 0, remplis: 0, aSaisir: 523, total: 523, aucunChamp: true, guide: true },
      sections: [],
      guide: { vide: false, rang, total: 116, nbRequis: 3, requis: true, partRequis: 2, avance: rang * 100 / 116 | 0,
               etape: 'Identite', section: 'Dossier RCS', libelle: rang === 1 ? "Date d'immatriculation" : 'Type de dossier RCS',
               cle: rang === 1 ? 'DOSSIER_RCS.DATE_IMMAT' : 'DOSSIER_RCS.TYPE_RCS', valeur: '', suivant: 'Motif',
               focus },
    })
    const agir = vi.fn(() => guide(2, 2))
    const { container } = monter({ 'FicheInformationsDossier.Vue': () => guide(1, 1), 'FicheInformationsDossier.Agir': agir })
    await screen.findByText("Date d'immatriculation")
    const kicker = container.querySelector('.rig-guidee .rig-kicker')!
    expect(morceaux(kicker)).toEqual(['<span>', ' · ', 'Identite', ' · ', 'Dossier RCS', ' · champ ', '1', ' / ', '116'])
    expect(morceaux(kicker.querySelector('.rig-guidee__requis'))).toEqual(['Requis ', '1', ' / ', '3'])
    expect(container.querySelector('.rig-guidee__jauge')!.getAttribute('style'))
      .toBe('background: linear-gradient(90deg,var(--rig-alerte-fond,#f6dcd8) 0 2%,var(--rig-bord) 2% 100%);')
    expect(container.querySelector('.rig-infos__vue-guide')!.className).toBe('rig-infos__vue-guide rig-infos__vue-guide--actif')
    const champ = container.querySelector('input.rig-guidee__saisie') as HTMLInputElement
    expect(champ.className).toBe('rig-guidee__saisie rig-guidee__saisie--requis')
    expect(document.activeElement).toBe(champ)
    champ.value = '12/03/2014'
    fireEvent.input(champ)
    fireEvent.keyDown(champ, { key: 'a' })
    expect(agir).not.toHaveBeenCalled()
    fireEvent.keyDown(champ, { key: 'Enter' })
    await screen.findByText('Type de dossier RCS')
    expect(agir).toHaveBeenCalledWith('s1', 'clavierGuide', { key: 'Enter', shiftKey: false, saisie: '12/03/2014' })
    // Le champ suivant montre SA valeur (le .razor y laissait le texte tape).
    const suivant = container.querySelector('input.rig-guidee__saisie') as HTMLInputElement
    expect(suivant.value).toBe('')
    expect(document.activeElement).toBe(suivant)
    expect((screen.getByText('‹ Precedent') as HTMLButtonElement).disabled).toBe(false)
  })

  it('une nouvelle version relit la vue ; le bouton × ferme par la coquille', async () => {
    const lire = vi.fn(() => vue())
    const { rerender, fermer } = monter({ 'FicheInformationsDossier.Vue': lire })
    await screen.findByText('Informations du dossier')
    rerender(<FicheInformationsDossier session="s1" version={1} fermer={fermer} toutAffiche={vi.fn()} champModifie={vi.fn()} />)
    await waitFor(() => expect(lire).toHaveBeenCalledTimes(2))
    fireEvent.click(screen.getByTitle('Fermer'))
    expect(fermer).toHaveBeenCalledTimes(1)
  })
})
