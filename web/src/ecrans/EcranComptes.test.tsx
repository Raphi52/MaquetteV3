import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranComptes, changements, type CompteAgent, type VueEcranComptes } from './EcranComptes'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

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
    await screen.findByText('1 compte(s) — Rose Responsable')
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

  it('lit l\'avant et l\'apres d\'un geste', () => {
    expect(changements('{"nom":"A","service":"RCS"}', '{"nom":"B","service":"RCS"}')).toBe('nom : A → B')
    expect(changements(null, '{"nom":"A"}')).toBe('création')
    expect(changements('{"roles":["X"]}', '{"roles":["X","Y"]}')).toBe('roles : X → X, Y')
    // Un compte sans rôle avant le geste : la liste vide se lit « — », comme une valeur absente
    // (capture du détail du geste, 21/11 : « roles :  → GREFFIER »).
    expect(changements('{"roles":[]}', '{"roles":["GREFFIER"]}')).toBe('roles : — → GREFFIER')
  })
})
