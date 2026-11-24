import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranComptes, changements, type CompteAgent, type VueEcranComptes } from './EcranComptes'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')
/** La phrase sous le titre de la liste (EnteteListe), une fois l'ecran charge. */
const synthese = async () => espaces((await screen.findByText((_, el) => el?.className === 'rig-lm-synthese')).textContent)

// Les regles (doublon, abrege, roles reserves, journaux) vivent dans le serveur et sont prouvees par
// tests/SocleDroitsCheck. Ici : l'ecran montre ce que le pont lui rend et lui remonte chaque geste.
const lucie: CompteAgent = {
  id: 7, compteWindows: 'lmartin', nom: 'Martin', prenom: 'Lucie', civilite: 'Mme', abrege: 'lmartin', codeGreffe: '3801',
  idService: 1, service: 'Registre du commerce', desactive: false, estAmitel: false, estAdministrateur: false, roles: ['GREFFIER'],
}

function vue(partiel: Partial<VueEcranComptes> = {}): VueEcranComptes {
  return {
    mode: 'serveur', message: null, echec: false, agent: 'Rose Responsable',
    comptes: [lucie],
    services: [{ id: 1, abrege: 'RCS', nom: 'Registre du commerce' }],
    roles: [
      { nom: 'GREFFIER', libelle: 'Greffier', reserveAmitel: false, reserveAdministrateur: false, comptabilite: false },
      { nom: 'FACT : STANDARD', libelle: 'Facturation standard', reserveAmitel: false, reserveAdministrateur: false, comptabilite: true },
    ],
    journal: [{
      id: 3, instant: '2026-11-20T08:05:00Z', phrase: 'Rose Responsable a modifié le compte lmartin', qui: 'Rose Responsable', verbe: 'a modifié',
      famille: 'compte', objet: 'le compte lmartin', ecran: 'Comptes des agents', avant: '{"nom":"Martin"}', apres: '{"nom":"Martin-Durand"}', source: 'v3',
    }],
    journalDroits: [{ id: 1, instant: '2026-11-20T08:00:00Z', compteVise: 'lmartin', role: 'GREFFIER', sens: 'ajout', auteur: 'Rose Responsable', ecran: 'Comptes des agents' }],
    toutLeGreffe: true, selection: null, proposerDesactivation: false,
    ...partiel,
  }
}

describe('EcranComptes', () => {
  it('SOC-002 CA04 : sans le droit de gerer les droits, l\'ecran ne montre que le refus', async () => {
    installerFauxPont({ 'EcranComptes.Ouvrir': () => vue({ mode: 'refuse', echec: true, comptes: [], message: 'Vous n\'avez pas le droit de gérer les comptes des agents.' }) })
    render(<EcranComptes />)
    expect((await screen.findByRole('alert')).textContent).toContain('Accès refusé')
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByText('Nouveau compte')).toBeNull()
  })

  it('SOC-004 CA01 : cree un compte avec ses roles, et affiche le compte rendu du serveur', async () => {
    const pont = installerFauxPont({
      'EcranComptes.Ouvrir': () => vue(),
      'EcranComptes.Creer': () => vue({ message: 'Compte créé.', comptes: [lucie, { ...lucie, id: 8, compteWindows: 'jnouveau', nom: 'Nouveau', prenom: 'Jean' }], selection: 8 }),
    })
    const { container } = render(<EcranComptes />)
    expect(await synthese()).toBe('1 compte · 1 actif — Rose Responsable')
    fireEvent.click(screen.getByRole('button', { name: 'Nouveau compte' }))
    const saisies = container.querySelectorAll<HTMLInputElement>('.rig-criteres__bloc input.rig-saisie-champ__saisie')
    fireEvent.change(saisies[0]!, { target: { value: 'jnouveau' } })
    fireEvent.change(saisies[1]!, { target: { value: 'Nouveau' } })
    fireEvent.change(saisies[2]!, { target: { value: 'Jean' } })
    fireEvent.change(container.querySelectorAll('select')[1]!, { target: { value: '1' } })
    fireEvent.click(screen.getByText('Greffier'))
    fireEvent.click(screen.getByText('Créer le compte'))
    expect((await screen.findByRole('status')).textContent).toContain('Compte créé.')
    expect(pont.appels).toHaveBeenCalledWith('EcranComptes.Creer', {
      id: null, compteWindows: 'jnouveau', nom: 'Nouveau', prenom: 'Jean', civilite: 'M.', idService: 1, roles: ['GREFFIER'], ecran: null,
    })
    expect(screen.getByText('Compte jnouveau')).toBeTruthy()
  })

  it('SOC-004 CA03, CA04 : desactive un compte ; une suppression refusee propose la desactivation', async () => {
    const pont = installerFauxPont({
      'EcranComptes.Ouvrir': () => vue(),
      'EcranComptes.Supprimer': () => vue({ message: 'Le compte lmartin a déjà agi : désactivez-le.', echec: true, selection: 7, proposerDesactivation: true }),
      'EcranComptes.Desactiver': () => vue({ message: 'Compte désactivé.', comptes: [{ ...lucie, desactive: true }], selection: 7 }),
    })
    render(<EcranComptes />)
    fireEvent.click(await screen.findByText('lmartin — Lucie Martin'))
    fireEvent.click(screen.getByText('Supprimer'))
    const refus = await screen.findByRole('alert')
    expect(refus.textContent).toContain('a déjà agi')
    fireEvent.click(within(refus).getByText('Désactiver ce compte'))
    expect((await screen.findByRole('status')).textContent).toContain('Compte désactivé.')
    expect(pont.appels).toHaveBeenCalledWith('EcranComptes.Supprimer', 7)
    expect(pont.appels).toHaveBeenCalledWith('EcranComptes.Desactiver', 7)
    expect(screen.getAllByText('Désactivé').length).toBeGreaterThan(0)
  })

  it('le compte rendu d\'un geste s\'efface quand on change d\'onglet ou d\'agent', async () => {
    // Capture du 21/11 : « Compte désactivé. » restait affiche sur le journal des gestes. Et un refus
    // qui propose « Désactiver ce compte » ne doit pas survivre au choix d'un autre agent : son bouton
    // agirait sur le compte nouvellement choisi.
    // fix-ok: rouge avant correction (« Compte désactivé. » encore present apres le clic sur le journal, 21/11 00:35).
    const paul: CompteAgent = { ...lucie, id: 9, compteWindows: 'pancien', nom: 'Ancien', prenom: 'Paul' }
    installerFauxPont({
      'EcranComptes.Ouvrir': () => vue({ comptes: [lucie, paul] }),
      'EcranComptes.Desactiver': () => vue({ message: 'Compte désactivé.', comptes: [{ ...lucie, desactive: true }, paul], selection: 7 }),
      'EcranComptes.Supprimer': () => vue({ message: 'Le compte lmartin a déjà agi : désactivez-le.', echec: true, comptes: [lucie, paul], selection: 7, proposerDesactivation: true }),
    })
    render(<EcranComptes />)
    fireEvent.click(await screen.findByText('lmartin — Lucie Martin'))
    fireEvent.click(screen.getByText('Désactiver'))
    expect((await screen.findByRole('status')).textContent).toContain('Compte désactivé.')
    fireEvent.click(screen.getByText('Journal des gestes'))
    expect(screen.queryByText(/Compte désactivé\./)).toBeNull()

    fireEvent.click(screen.getByText('Comptes'))
    fireEvent.click(screen.getByText(/^lmartin — Lucie Martin/))
    fireEvent.click(screen.getByText('Supprimer'))
    expect((await screen.findByRole('alert')).textContent).toContain('a déjà agi')
    fireEvent.click(screen.getByText('pancien — Paul Ancien'))
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.queryByText('Désactiver ce compte')).toBeNull()
  })

  it('SOC-005 CA01 : donner un role a un compte existant passe par le serveur', async () => {
    const pont = installerFauxPont({
      'EcranComptes.Ouvrir': () => vue(),
      'EcranComptes.Role': () => vue({ message: 'Rôle ajouté.', comptes: [{ ...lucie, roles: ['FACT : STANDARD', 'GREFFIER'] }], selection: 7 }),
    })
    render(<EcranComptes />)
    fireEvent.click(await screen.findByText('lmartin — Lucie Martin'))
    fireEvent.click(screen.getByText('Facturation standard (facturation)'))
    expect((await screen.findByRole('status')).textContent).toContain('Rôle ajouté.')
    expect(pont.appels).toHaveBeenCalledWith('EcranComptes.Role', 7, 'FACT : STANDARD', true)
  })

  it('SOC-016 CA03 : le journal des gestes se lit en phrases, avec l\'ecran et l\'avant/apres', async () => {
    installerFauxPont({ 'EcranComptes.Ouvrir': () => vue() })
    render(<EcranComptes />)
    fireEvent.click(await screen.findByText('Journal des gestes'))
    expect(within(screen.getByRole('table')).getByText('Rose Responsable a modifié le compte lmartin')).toBeTruthy()
    expect(within(screen.getByRole('table')).getByText('Comptes des agents')).toBeTruthy()
    expect(within(screen.getByRole('table')).getByText('nom : Martin → Martin-Durand')).toBeTruthy()
    // Fenetre etroite : les colonnes se partagent la largeur (aucune ne sort du cadre) et le texte
    // coupe par « … » se lit entier au survol.
    expect(screen.getByRole('table').style.tableLayout).toBe('fixed')
    expect(screen.getByRole('table').querySelectorAll('col')).toHaveLength(4)
    expect(within(screen.getByRole('table')).getByText('Comptes des agents').getAttribute('title')).toBe('Comptes des agents')
    fireEvent.click(screen.getByText('Journal des droits'))
    expect(screen.getByText('Ajouté')).toBeTruthy()
  })

  it('SOC-016 CA03 : en fenetre etroite, le geste choisi se lit en entier sous la grille, sans survol', async () => {
    const long = 'Rose Responsable a donné le rôle FACT : STANDARD au compte lbernard-petit du service RCS'
    installerFauxPont({
      'EcranComptes.Ouvrir': () => vue({
        journal: [
          { id: 5, instant: '2026-11-20T08:10:00Z', phrase: long, qui: 'Rose Responsable', verbe: 'a donné', famille: 'role',
            objet: 'le rôle FACT : STANDARD', ecran: 'Comptes des agents', avant: '{"roles":["GREFFIER"]}', apres: '{"roles":["GREFFIER","FACT : STANDARD"]}', source: 'v3' },
          ...vue().journal,
        ],
      }),
    })
    render(<EcranComptes />)
    fireEvent.click(await screen.findByText('Journal des gestes'))
    // Le dernier geste est detaille d'office ; le detail replie ses lignes (la grille, elle, reste
    // sur une ligne par geste, regle du 17/09) : rien n'y finit par « … ».
    const detail = screen.getByRole('region', { name: 'Détail du geste' })
    expect(within(detail).getByText(long).style.whiteSpace).toBe('normal')
    expect(within(detail).getByText('roles : GREFFIER → GREFFIER, FACT : STANDARD')).toBeTruthy()
    expect(within(detail).getByText('Comptes des agents')).toBeTruthy()
    const lignes = within(screen.getByRole('table')).getAllByRole('row').slice(1)
    expect(lignes[0]!.getAttribute('aria-selected')).toBe('true')
    // Un clic sur une autre ligne la detaille a son tour.
    fireEvent.click(within(screen.getByRole('table')).getByText('Rose Responsable a modifié le compte lmartin'))
    expect(within(detail).getByText('Rose Responsable a modifié le compte lmartin')).toBeTruthy()
    expect(within(detail).getByText('nom : Martin → Martin-Durand')).toBeTruthy()
    expect(lignes[1]!.getAttribute('aria-selected')).toBe('true')
    expect(lignes[0]!.getAttribute('aria-selected')).toBe('false')
  })

  it('SOC-016 CA03 : un agent qui n\'est pas chef de greffe est prevenu qu\'il ne voit que ses gestes', async () => {
    installerFauxPont({ 'EcranComptes.Ouvrir': () => vue({ toutLeGreffe: false, journal: [] }) })
    render(<EcranComptes />)
    fireEvent.click(await screen.findByText('Journal des gestes'))
    expect(screen.getByText(/Seuls vos propres gestes/)).toBeTruthy()
  })

  it('liste moderne : tete, puces des onglets, avatar, pastille de service, point d\'etat, pied ; Ctrl K filtre chaque liste', async () => {
    const paul: CompteAgent = { ...lucie, id: 9, compteWindows: 'pancien', nom: 'Ancien', prenom: 'Paul', desactive: true, idService: 2, service: 'Judiciaire' }
    installerFauxPont({
      'EcranComptes.Ouvrir': () => vue({
        comptes: [lucie, paul],
        services: [{ id: 1, abrege: 'RCS', nom: 'Registre du commerce' }, { id: 2, abrege: 'JUD', nom: 'Judiciaire' }],
        journalDroits: [...vue().journalDroits, { id: 2, instant: '2026-11-20T09:00:00Z', compteVise: 'pancien', role: 'GREFFIER', sens: 'retrait', auteur: 'Rose Responsable', ecran: null }],
      }),
    })
    const { container } = render(<EcranComptes />)
    expect(await synthese()).toBe('2 comptes · 1 actif — Rose Responsable')
    expect(screen.getByText('Comptes des agents', { selector: '.rig-lm-titre' })).toBeTruthy()
    // Les onglets sont des puces de vues, avec le compte de chaque liste ; la puce de l'onglet montre est pressee.
    const puces = [...container.querySelectorAll('.rig-lm-vue')]
    expect(puces.map((p) => espaces(p.textContent))).toEqual(['Comptes 2', 'Journal des gestes 1', 'Journal des droits 2'])
    expect(puces.map((p) => p.getAttribute('aria-pressed'))).toEqual(['true', 'false', 'false'])

    // Une ligne par compte : l'agent en avatar (nom en gras, compte Windows en gris), le service en pastille, l'etat en point.
    const rangees = [...container.querySelectorAll('tbody tr')]
    expect(rangees.map((r) => r.querySelector('.rig-lm-avatar')!.textContent)).toEqual(['LM', 'PA'])
    expect(rangees[0]!.querySelector('.rig-lm-identite__nom')!.textContent).toBe('Lucie Martin')
    expect(rangees[0]!.querySelector('.rig-lm-identite__objet')!.textContent).toBe('lmartin')
    expect(rangees[0]!.querySelectorAll('td')[0]!.title).toBe('Mme Lucie Martin — lmartin')
    expect(rangees.map((r) => r.querySelector('.rig-lm-pastille')!.className)).toEqual(['rig-lm-pastille rig-lm-t-bleu', 'rig-lm-pastille rig-lm-t-ardoise'])
    expect(rangees.map((r) => espaces(r.querySelector('.rig-lm-etat')!.textContent))).toEqual(['Actif', 'Désactivé'])
    expect(rangees.map((r) => r.querySelector('.rig-lm-etat')!.className)).toEqual(['rig-lm-etat rig-lm-t-vert', 'rig-lm-etat rig-lm-t-gris'])
    expect(espaces(container.querySelector('.rig-lm-pied')!.textContent)).toBe('2 comptes affichés' + 'Actifs 1' + 'Désactivés 1')

    // Ctrl K pose le curseur dans « Aller à un compte… » : sur cet onglet, c'est le SEUL champ de la recherche
    // des comptes (celui de gauche ne revient que sur les journaux, ou la tete filtre le journal).
    expect(screen.queryByLabelText('Compte, nom ou prénom')).toBeNull()
    const champ = screen.getByLabelText('Aller à un compte…') as HTMLInputElement
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: 'ancien' } })
    // Les noms des lignes restantes, pas le nombre de <tr> : quand rien ne correspond, la ligne « Aucun compte… »
    // est aussi un <tr>, et un compte de 1 passerait a tort (piege releve au lot 3 du plan des listes modernes).
    expect([...container.querySelectorAll('tbody .rig-lm-identite__nom')].map((n) => n.textContent)).toEqual(['Paul Ancien'])
    expect(await synthese()).toBe('1 compte · 0 actif — Rose Responsable')
    // Entree sur un seul compte restant : il passe dans la fiche, comme un clic sur sa ligne.
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(screen.getByText('Compte pancien')).toBeTruthy()

    // Journal des droits : le changement en point colore, l'auteur en avatar ; son propre « Aller à… ».
    fireEvent.click(screen.getByRole('button', { name: 'Journal des droits 2' }))
    expect(await synthese()).toBe('2 changements · 1 ajout · 1 retrait')
    // La recherche des comptes revient a gauche, avec la valeur tapee dans la tete : un seul filtre.
    expect((screen.getByLabelText('Compte, nom ou prénom') as HTMLInputElement).value).toBe('ancien')
    expect(screen.getByText('Comptes (1)')).toBeTruthy()
    expect(screen.queryByLabelText('Aller à un compte…')).toBeNull()
    const droits = [...container.querySelectorAll('tbody tr')]
    expect(droits.map((r) => r.querySelector('.rig-lm-etat')!.className)).toEqual(['rig-lm-etat rig-lm-t-vert', 'rig-lm-etat rig-lm-t-orange'])
    expect(droits[0]!.querySelector('.rig-lm-identite__nom')!.textContent).toBe('Rose Responsable')
    fireEvent.change(screen.getByLabelText('Aller à un compte ou un rôle…'), { target: { value: 'pancien' } })
    // Le changement qui reste (le retrait du role de pancien), pas le nombre de <tr> (meme piege que ci-dessus).
    expect([...container.querySelectorAll('tbody .rig-lm-etat')].map((e) => espaces(e.textContent))).toEqual(['Retiré'])
    expect(espaces(container.querySelector('.rig-lm-pied')!.textContent)).toBe('1 changement affiché' + 'Ajouts 0' + 'Retraits 1')
    fireEvent.change(screen.getByLabelText('Aller à un compte ou un rôle…'), { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 2 changements ne contient « zzz ».')).toBeTruthy()

    // Journal des gestes : filtre sur la phrase, l'ecran et l'avant/apres ; l'encadre du detail reste sous le tableau.
    fireEvent.click(screen.getByRole('button', { name: 'Journal des gestes 1' }))
    expect(await synthese()).toBe('1 geste · 1 agent · 1 écran')
    fireEvent.change(screen.getByLabelText('Aller à un geste…'), { target: { value: 'martin-durand' } })
    expect(within(screen.getByRole('table')).getByText('Rose Responsable a modifié le compte lmartin')).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Détail du geste' })).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Aller à un geste…'), { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 1 gestes ne contient « zzz ».')).toBeTruthy()
  })

  it('lit l\'avant et l\'apres d\'un geste', () => {
    expect(changements('{"nom":"A","service":"RCS"}', '{"nom":"B","service":"RCS"}')).toBe('nom : A → B')
    expect(changements(null, '{"nom":"A"}')).toBe('création')
    expect(changements('{"roles":["X"]}', '{"roles":["X","Y"]}')).toBe('roles : X → X, Y')
    // Un compte sans rôle avant le geste : la liste vide se lit « — », comme une valeur absente
    // (capture du détail du geste, 21/11 : « roles :  → GREFFIER »).
    expect(changements('{"roles":[]}', '{"roles":["GREFFIER"]}')).toBe('roles : — → GREFFIER')
  })
})
