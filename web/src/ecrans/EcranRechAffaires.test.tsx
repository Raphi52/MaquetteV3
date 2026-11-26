import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { EcranRechAffaires, VuesPrereglees, objetDe, type LigneAffaire, type VueAffaires } from './EcranRechAffaires'
import { oublierVuesDeSession } from '../composants/ListeModerne'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { morceaux } from '../test/morceaux'

afterEach(() => { cleanup(); retirerFauxPont(); oublierVuesDeSession() })

// Le fonds d'affaires, le filtre et le tri sont faits par Pont/EcranRechAffaires.cs,
// executes par tests/TriCheck, OuvertureCheck et GedCheck. Ici : l'ecran affiche
// ce que le pont rend, lui transmet les criteres saisis, et remonte les gestes.
const ligne = (numero: string, nature = 'instance', autres: Partial<LigneAffaire> = {}): LigneAffaire => ({
  numero, idInstance: '55339', role: '2026F00301', codeAffaire: '010', nature, nomNature: 'Instance',
  libelle: `Affaire ${numero} - AMIENS BILLARDS`, nom: 'AMIENS BILLARDS', etat: 'En cours', siren: '109 678 235', type: 'RCS',
  saisine: '30/08/2026', creation: '01/09/2026', solde: '142,50', soldeValeur: 142.5, ...autres,
})

function vue(c: unknown, _cle: unknown, _asc: unknown, selection: unknown): VueAffaires {
  const criteres = c as Record<string, unknown>
  const numero = String(criteres.critNumero ?? '')
  const lignes = numero === 'ZZZ' ? [] : [ligne('2026B01067'), ligne('004176')]
  return {
    natures: [{ code: 'instance', nom: 'Instance' }], etats: ['En cours', 'Radiee'],
    lignes, resume: lignes.length ? `${lignes.length} instance` : '',
    aucunCritere: Object.values(criteres).every((v) => v === '' || v === null),
    sirenCherche: String(criteres.critLibelle ?? '').replace(/\D/g, '').length === 9 ? String(criteres.critLibelle).replace(/\D/g, '') : '',
    choisie: selection ? { numero: String(selection), idInstance: '55339', role: '2026F00301' } : null,
  }
}

/** Les criteres du DERNIER appel a Trouver (le pont recoit aussi Compter et Aiguiller). */
type Appels = ReturnType<typeof installerFauxPont>['appels']
const dernierTrouver = (appels: Appels) => appels.mock.calls.filter((c) => c[0] === 'EcranRechAffaires.Trouver').at(-1)!
const criteresDuDernier = (appels: Appels) => dernierTrouver(appels)[1] as Record<string, unknown>
const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')

describe('EcranRechAffaires', () => {
  it('affiche les affaires rendues par le pont : une ligne par affaire, synthese, pied, resume en infobulle', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    const { container } = render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)

    const synthese = await screen.findByText(/resultats ·/)
    expect(espaces(synthese.textContent)).toBe('2 resultats · 2 en cours · solde 285,00 €')
    expect(synthese.title).toBe('2 instance')
    expect(pont.appels.mock.calls[0]![0]).toBe('EcranRechAffaires.Trouver')
    const rangees = container.querySelectorAll('tbody tr')
    expect(rangees).toHaveLength(2)
    // Chevron, Affaire, Nature, Etat, Saisine, Solde, menu : sept cellules, UNE ligne.
    const cellules = rangees[0]!.querySelectorAll(':scope > td')
    expect(cellules).toHaveLength(7)
    expect(cellules[1]!.textContent).toBe('ABAMIENS BILLARDSAffaire 2026B01067 · 2026B01067')
    expect(cellules[2]!.querySelector('.rig-lm-pastille')!.textContent).toBe('Instance')
    expect(cellules[3]!.querySelector('.rig-lm-etat')!.textContent).toBe('En cours')
    expect(cellules[4]!.textContent).toBe('30/08/2026')
    expect(cellules[5]!.textContent).toBe('142,50 €')
    expect([...container.querySelectorAll('th.rig-th--triable')].map((th) => th.textContent))
      .toEqual(['Affaire', 'Nature', 'Etat', 'Saisine', 'Solde'])
    expect(espaces(container.querySelector('.rig-lm-pied')!.textContent))
      .toBe('2 affaires affichees' + 'Solde 285,00 €' + 'En cours 2' + 'Double-clic sur une ligne pour ouvrir l\'affaire dans la visionneuse.')
  })

  it('l\'objet est le libelle avant « - », le numero suit l\'objet', () => {
    expect(objetDe({ libelle: 'Modification - SCI DU PARC', nom: 'SCI DU PARC' })).toBe('Modification')
    expect(objetDe({ libelle: 'Liquidation judiciaire - MOREAU Entreprise individuelle', nom: 'MOREAU EI' })).toBe('Liquidation judiciaire')
    expect(objetDe({ libelle: 'Sans tiret', nom: 'X' })).toBe('Sans tiret')
    expect(objetDe({ libelle: 'SOCIETE Z', nom: 'SOCIETE Z' })).toBe('')
  })

  it('pre-remplit le numero d\'affaire recu de l\'accueil', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires noAffaire=" 004176 " ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/resultats/)
    expect((pont.appels.mock.calls[0]![1] as { critNumero: string }).critNumero).toBe('004176')
  })

  it('double-clic : remonte le numero de l\'affaire a ouvrir', async () => {
    installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    const ouvrir = vi.fn()
    render(<EcranRechAffaires ouvrir={ouvrir} ouvrirUnDossier={vi.fn()} />)
    fireEvent.doubleClick(await screen.findByText('004176'))
    expect(ouvrir).toHaveBeenCalledWith('004176')
  })

  it('un clic sur un en-tete demande le tri au pont, un second l\'inverse', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/resultats/)
    fireEvent.click(screen.getByText('Affaire'))
    await waitFor(() => expect(dernierTrouver(pont.appels).slice(2, 4)).toEqual(['Nom', true]))
    fireEvent.click(screen.getByText('Affaire'))
    await waitFor(() => expect(dernierTrouver(pont.appels).slice(2, 4)).toEqual(['Nom', false]))
    expect(screen.getByText('Affaire').getAttribute('aria-sort')).toBe('descending')
  })

  it('« Trier » propose les 13 cles du pont ; le sens s\'inverse ; l\'ordre d\'origine revient', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/resultats/)
    const trier = screen.getByLabelText('Trier') as HTMLSelectElement
    expect([...trier.options].map((o) => o.value)).toEqual(['', 'Numero', 'IdInstance', 'Role', 'CodeAffaire', 'Nature',
      'Libelle', 'Nom', 'Etat', 'Siren', 'Type', 'Saisine', 'Creation', 'Solde'])
    fireEvent.change(trier, { target: { value: 'Creation' } })
    await waitFor(() => expect(dernierTrouver(pont.appels).slice(2, 4)).toEqual(['Creation', true]))
    fireEvent.click(screen.getByRole('button', { name: 'Ordre croissant, inverser' }))
    await waitFor(() => expect(dernierTrouver(pont.appels).slice(2, 4)).toEqual(['Creation', false]))
    fireEvent.change(trier, { target: { value: '' } })
    await waitFor(() => expect(dernierTrouver(pont.appels).slice(2, 4)).toEqual([null, true]))
  })

  it('recherche restee vide : « Ouvrir un dossier » emporte le SIREN cherche', async () => {
    installerFauxPont({
      'EcranRechAffaires.Trouver': (c: unknown, ...r: unknown[]) =>
        ({ ...vue(c, ...(r as [unknown, unknown, unknown])), lignes: [], resume: '' }),
    })
    const ouvrirUnDossier = vi.fn()
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={ouvrirUnDossier} />)
    expect(await screen.findByText('Saisissez au moins un critere, puis lancez la recherche.')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Libelle, SIREN'), { target: { value: '512 345 678' } })
    await screen.findByText('Aucune affaire ne repond a ces criteres.')
    const pourLeSiren = await screen.findByText('pour le SIREN 512345678')
    expect(morceaux(pourLeSiren)).toEqual([' pour le SIREN ', '512345678'])
    expect(morceaux(pourLeSiren.parentElement)).toEqual(['<button>', '<span>'])
    expect(pourLeSiren.closest('td')!.getAttribute('colspan')).toBe('7')
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir un dossier' }))
    expect(ouvrirUnDossier).toHaveBeenCalledWith('512345678')
  })

  // SOC-015 (lot 3 de la phase 2) : l'aiguillage vient de Pont/AncienRig.cs (banc tests/SocleAiguillageCheck).
  const aiguiller = (numeros: unknown) => ({
    mode: 'demo', greffe: null, passage: '01/08/2026 08:00', message: null,
    affaires: (numeros as string[]).map((numero) => numero === '004176'
      ? { numero, logiciel: 'V2', motif: 'creee le 24/08/2026 00:00, avant le passage du 01/09/2026 08:00 : traitee dans la V2' }
      : { numero, logiciel: 'V3', motif: 'traitee dans la V3' }),
  })

  it('SOC-015 CA01 : une affaire creee avant le passage porte la mention « traitee dans la V2 », les autres non', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue, 'EcranRechAffaires.Aiguiller': aiguiller })
    const { container } = render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    const mention = await screen.findByText('traitee dans la V2')
    // La mention suit le nom et l'objet · numero de SA ligne, dans la cellule Affaire.
    expect(within(mention.closest('td')!).getByText('004176')).toBeTruthy()
    expect(mention.closest('.rig-lm-identite')!.lastElementChild).toBe(mention)
    expect(mention.title).toContain('traitee dans la V2')
    expect(container.querySelectorAll('.rig-badge')).toHaveLength(1)
    expect(container.querySelectorAll('tbody tr')[0]!.querySelectorAll(':scope > td')).toHaveLength(7)
    expect(pont.appels.mock.calls.find((c) => c[0] === 'EcranRechAffaires.Aiguiller')![1]).toEqual(['004176', '2026B01067'])

    // Un tri ne change pas la liste des numeros : l'aiguillage n'est pas redemande.
    fireEvent.click(screen.getByText('Affaire'))
    await waitFor(() => expect(dernierTrouver(pont.appels).slice(2, 4)).toEqual(['Nom', true]))
    expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranRechAffaires.Aiguiller')).toHaveLength(1)
  })

  it('SOC-015 CA02, CA03 : une ancienne affaire choisie propose « Ouvrir dans l\'ancien RIG », qui rend compte du geste', async () => {
    const pont = installerFauxPont({
      'EcranRechAffaires.Trouver': vue, 'EcranRechAffaires.Aiguiller': aiguiller,
      'EcranRechAffaires.OuvrirDansAncienRig': (numero: unknown) => ({ fait: true, geste: 'lance', numero, ligneDeCommande: '"C:\\RIG\\exe\\RigClientAccueil.exe" /g8002',
        message: `Le numero ${String(numero)} est copie : collez-le dans « N° affaire » de la recherche d'affaires de l'ancien RIG.` }),
    })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText('traitee dans la V2')
    // Une affaire de la V3 choisie : pas de bloc « Ancien RIG », l'ecran est celui d'avant.
    fireEvent.click(screen.getByText('2026B01067'))
    await screen.findByText('2026B01067 · instance 55339 · role 2026F00301')
    expect(screen.queryByRole('button', { name: 'Ouvrir dans l\'ancien RIG' })).toBeNull()

    fireEvent.click(screen.getByText('004176'))
    fireEvent.click(await screen.findByRole('button', { name: 'Ouvrir dans l\'ancien RIG' }))
    expect((await screen.findByRole('status')).textContent).toContain('Le numero 004176 est copie')
    expect(pont.appels.mock.calls.find((c) => c[0] === 'EcranRechAffaires.OuvrirDansAncienRig')!.slice(1)).toEqual(['004176'])
  })

  it('SOC-015 CA04 : la suppression d\'une ancienne affaire est refusee ; celle d\'une affaire de la V3 reste simulee', async () => {
    installerFauxPont({ 'EcranRechAffaires.Trouver': vue, 'EcranRechAffaires.Aiguiller': aiguiller })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText('traitee dans la V2')
    fireEvent.click(screen.getByText('004176'))
    await screen.findByRole('button', { name: 'Ouvrir dans l\'ancien RIG' })
    fireEvent.click(screen.getByRole('button', { name: 'Supprimer l\'instance' }))
    expect(screen.getByText(/affaire traitee dans la V2, elle ne se supprime pas ici\. Ouvrez-la dans l'ancien RIG\./)).toBeTruthy()
    expect(screen.queryByText(/suppression simulee/)).toBeNull()

    fireEvent.click(screen.getByText('2026B01067'))
    await screen.findByText('2026B01067 · instance 55339 · role 2026F00301')
    fireEvent.click(screen.getByRole('button', { name: 'Supprimer l\'instance' }))
    expect(screen.getByText(/suppression simulee — numeros metiers liberes\./)).toBeTruthy()
  })

  it('Entree dans les criteres et suppression simulee de l\'instance choisie ; la ligne choisie est marquee', async () => {
    installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    fireEvent.click(await screen.findByText('2026B01067'))
    expect(morceaux(await screen.findByText('2026B01067 · instance 55339 · role 2026F00301')))
      .toEqual(['2026B01067', ' · instance ', '55339', ' · role ', '2026F00301'])
    expect(screen.getByText('2026B01067').closest('tr')!.className).toBe('rig-ligne--valide')
    expect(screen.getByText('004176').closest('tr')!.className).toBe('')
    fireEvent.click(screen.getByRole('button', { name: 'Supprimer l\'instance' }))
    expect(screen.getByText(/suppression simulee — numeros metiers liberes\./)).toBeTruthy()
    fireEvent.keyDown(screen.getByLabelText('N° Affaire'), { key: 'Enter' })
    expect(screen.queryByText(/suppression simulee/)).toBeNull()
  })

  it('F5 et F7 dans les criteres relancent la recherche comme Entree (touches de l\'ancien RIG)', async () => {
    installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    fireEvent.click(await screen.findByText('2026B01067'))
    for (const touche of ['F5', 'F7']) {
      fireEvent.click(await screen.findByRole('button', { name: 'Supprimer l\'instance' }))
      expect(screen.getByText(/suppression simulee/)).toBeTruthy()
      expect(fireEvent.keyDown(screen.getByLabelText('N° Affaire'), { key: touche })).toBe(false)
      expect(screen.queryByText(/suppression simulee/)).toBeNull()
    }
  })

  // --- Liste moderne « Tout reuni » (lot 1 des listes modernes, 24/11/2026).

  /** Un fonds varie : natures, etats, domaines et soldes differents. */
  const varie = (c: unknown, ...r: unknown[]): VueAffaires => ({
    ...vue(c, ...(r as [unknown, unknown, unknown])),
    natures: [{ code: 'instance', nom: 'Instance' }, { code: 'collective', nom: 'Procedure collective' },
              { code: 'prevention', nom: 'Prevention' }, { code: 'mesure', nom: 'Mesure' }],
    lignes: [
      ligne('004176', 'instance', { nom: 'SCI DU PARC', libelle: 'Modification - SCI DU PARC', solde: '142,50', soldeValeur: 142.5 }),
      ligne('J-00931', 'collective', { nomNature: 'Procedure collective', nom: 'TRANSNORD SA', libelle: 'Depot de bilan - TRANSNORD SA',
        type: 'JUD', solde: '415,00', soldeValeur: 415 }),
      ligne('P-00221', 'prevention', { nomNature: 'Prevention', nom: 'SOMME LOGISTIQUE', libelle: 'Conciliation - SOMME LOGISTIQUE',
        type: 'JUD', etat: 'Cloturee', solde: '0,00', soldeValeur: 0 }),
    ],
  })

  it('les cartes chiffrees comptent la liste et POSENT les criteres existants (nature, etat, domaine)', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': varie })
    const { container } = render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/resultats ·/)
    const cartes = container.querySelectorAll('.rig-lm-carte')
    expect([...cartes].map((c) => espaces(c.querySelector('.rig-lm-carte__valeur')!.textContent)))
      .toEqual(['557,50 €', '2 sur 3', 'RCS 1 · JUD 2'])
    expect(espaces(cartes[0]!.querySelector('.rig-lm-legende')!.textContent))
      .toBe('Instance 142,50 €Procedure 415,00 €Prevention 0,00 €Mesure 0,00 €')

    fireEvent.click(screen.getByRole('button', { name: /^Procedure 415,00/ , pressed: false }))
    await waitFor(() => expect(criteresDuDernier(pont.appels).critNature).toBe('collective'))
    expect((screen.getByLabelText('Nature') as HTMLSelectElement).value).toBe('collective')
    // Un second clic retire le filtre ; un clic sur la carte aussi.
    fireEvent.click(screen.getByRole('button', { name: /^Procedure 415,00/, pressed: true }))
    await waitFor(() => expect(criteresDuDernier(pont.appels).critNature).toBe(''))

    fireEvent.click(screen.getByText('En cours', { selector: '.rig-lm-carte__titre' }))
    await waitFor(() => expect(criteresDuDernier(pont.appels).critEtat).toBe('En cours'))
    expect(cartes[1]!.getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(cartes[1]!)
    await waitFor(() => expect(criteresDuDernier(pont.appels).critEtat).toBe(''))

    fireEvent.click(cartes[2]!.querySelector('.rig-lm-segment.rig-lm-t-ardoise')!)
    await waitFor(() => expect(criteresDuDernier(pont.appels).critType).toBe('JUD'))
    fireEvent.click(cartes[2]!)
    await waitFor(() => expect(criteresDuDernier(pont.appels).critType).toBe(''))
  })

  it('la ligne porte avatar, pastille et point colores, et une mini-barre proportionnelle au plus grand solde', async () => {
    installerFauxPont({ 'EcranRechAffaires.Trouver': varie })
    const { container } = render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/resultats ·/)
    const rangees = [...container.querySelectorAll('tbody tr')]
    expect(rangees.map((r) => r.querySelector('.rig-lm-avatar')!.textContent)).toEqual(['P', 'T', 'SL'])
    expect(rangees.map((r) => r.querySelector('.rig-lm-pastille')!.className.split(' ')[1])).toEqual(['rig-lm-t-bleu', 'rig-lm-t-orange', 'rig-lm-t-ardoise'])
    expect(rangees.map((r) => r.querySelector('.rig-lm-etat')!.className.split(' ')[1])).toEqual(['rig-lm-t-bleu', 'rig-lm-t-bleu', 'rig-lm-t-gris'])
    expect(rangees.map((r) => (r.querySelector('.rig-lm-montant__barre b') as HTMLElement).style.width)).toEqual(['34%', '100%', '0%'])
    expect(rangees[2]!.querySelector('.rig-lm-montant__texte--zero')!.textContent).toBe('0,00 €')
    // Le texte entier, au survol de la cellule coupee.
    expect((rangees[0]!.children[1] as HTMLElement).title).toBe('SCI DU PARC — Modification - SCI DU PARC · 004176')
  })

  it('les vues : comptees par le pont, une vue POSE ses criteres, la puce active suit les criteres', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue, 'EcranRechAffaires.Compter': (jeux: unknown) => (jeux as unknown[]).map((_, i) => 15 - i) })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    expect(await screen.findByRole('button', { name: 'Toutes 15', pressed: true })).toBeTruthy()
    const jeux = pont.appels.mock.calls.find((c) => c[0] === 'EcranRechAffaires.Compter')![1] as Record<string, unknown>[]
    expect(jeux).toHaveLength(VuesPrereglees.length)
    expect(jeux[1]).toMatchObject({ critNature: 'collective', critEtat: 'En cours', critSaisineDu: null })
    expect(jeux[2]).toMatchObject({ critSoldeMin: '100' })

    fireEvent.click(screen.getByRole('button', { name: 'Solde > 100 € 13' }))
    await waitFor(() => expect(criteresDuDernier(pont.appels).critSoldeMin).toBe('100'))
    expect((screen.getByLabelText('Solde superieur a (€)') as HTMLInputElement).value).toBe('100')
    expect(screen.getByRole('button', { name: 'Solde > 100 € 13', pressed: true })).toBeTruthy()

    // Un critere saisi a la main : plus aucune vue ne correspond.
    fireEvent.change(screen.getByLabelText('N° Affaire'), { target: { value: '004' } })
    await waitFor(() => expect(screen.queryAllByRole('button', { pressed: true })).toHaveLength(0))

    fireEvent.click(screen.getByRole('button', { name: 'Toutes 15' }))
    await waitFor(() => expect(criteresDuDernier(pont.appels)).toMatchObject({ critNumero: '', critSoldeMin: '' }))
    expect(screen.getByRole('button', { name: 'Toutes 15', pressed: true })).toBeTruthy()
  })

  it('« + Nouvelle vue » enregistre les criteres courants sous un nom, pour la session (l\'ecran rouvert la garde)', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue, 'EcranRechAffaires.Compter': (jeux: unknown) => (jeux as unknown[]).map(() => 1) })
    const premier = render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/resultats/)
    fireEvent.change(screen.getByLabelText('Domaine'), { target: { value: 'RCS' } })
    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    fireEvent.change(screen.getByLabelText('Nom de la nouvelle vue'), { target: { value: 'Mes RCS' } })
    fireEvent.keyDown(screen.getByLabelText('Nom de la nouvelle vue'), { key: 'Enter' })
    expect(await screen.findByRole('button', { name: 'Mes RCS 1', pressed: true })).toBeTruthy()
    const derniersJeux = pont.appels.mock.calls.filter((c) => c[0] === 'EcranRechAffaires.Compter').at(-1)![1] as Record<string, unknown>[]
    expect(derniersJeux).toHaveLength(VuesPrereglees.length + 1)
    expect(derniersJeux.at(-1)).toMatchObject({ critType: 'RCS', critNumero: '' })

    premier.unmount()
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/resultats/)
    fireEvent.click(await screen.findByRole('button', { name: 'Mes RCS 1', pressed: false }))
    await waitFor(() => expect(criteresDuDernier(pont.appels).critType).toBe('RCS'))
  })

  it('« Aller a une affaire… » filtre la liste affichee sans toucher aux criteres ; Entree ouvre l\'affaire restee seule', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': varie })
    const ouvrir = vi.fn()
    const { container } = render(<EcranRechAffaires ouvrir={ouvrir} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/resultats ·/)
    const appelsAvant = pont.appels.mock.calls.filter((c) => c[0] === 'EcranRechAffaires.Trouver').length
    const champ = screen.getByLabelText('Aller a une affaire…')

    // Ctrl K y pose le curseur (et la palette de l'application ne s'ouvre pas).
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)

    fireEvent.change(champ, { target: { value: 'logist' } })
    expect([...container.querySelectorAll('tbody tr')].map((r) => r.querySelector('b')!.textContent)).toEqual(['SOMME LOGISTIQUE'])
    expect(espaces(screen.getByText(/resultat ·/).textContent)).toBe('1 resultat · 0 en cours · solde 0,00 €')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(ouvrir).toHaveBeenCalledWith('P-00221')

    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 3 affaires ne contient « zzz ».')).toBeTruthy()
    // Un SIREN tape sans ses espaces retrouve les affaires.
    fireEvent.change(champ, { target: { value: '109678235' } })
    expect(container.querySelectorAll('tbody tr')).toHaveLength(3)
    expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranRechAffaires.Trouver')).toHaveLength(appelsAvant)
  })

  it('le chevron deplie la ligne (colonnes secondaires, Ouvrir, Historique) sans la choisir', async () => {
    const pont = installerFauxPont({
      'EcranRechAffaires.Trouver': varie,
      'EcranRechAffaires.HistoriqueAncienne': () => ({ mode: 'demo', message: null, greffe: null, critere: 'gestion', valeur: '004176',
        historique: { titre: 'SCI DU PARC', affaires: [{ id: 1, libelle: 'Modification', marque: 'V3', etat: 'En cours', demandes: [] }] },
        arrivee: null, anciens: [], reprises: [] }),
    })
    const ouvrir = vi.fn()
    const { container } = render(<EcranRechAffaires ouvrir={ouvrir} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/resultats ·/)
    const rangee = screen.getByText('SCI DU PARC').closest('tr')!
    fireEvent.click(within(rangee).getByRole('button', { name: 'Deplier la ligne' }))

    expect(rangee.className).toBe('rig-lm-ouverte')
    const detail = rangee.nextElementSibling!
    expect(detail.className).toBe('rig-lm-detail')
    expect([...detail.querySelectorAll('.rig-lm-champ__libelle')].map((l) => l.textContent))
      .toEqual(['Id instance', 'N° de role', 'Code affaire', 'SIREN', 'Domaine', 'Date creation'])
    expect(detail.querySelector('td')!.getAttribute('colspan')).toBe('7')
    expect(screen.getByText('Selectionnez une instance dans les resultats.')).toBeTruthy()

    fireEvent.click(within(detail as HTMLElement).getByRole('button', { name: 'Ouvrir' }))
    expect(ouvrir).toHaveBeenCalledWith('004176')
    fireEvent.click(within(detail as HTMLElement).getByRole('button', { name: 'Historique' }))
    expect((await within(detail as HTMLElement).findByRole('status')).textContent)
      .toBe('SCI DU PARC : Modification (V3, En cours, 0 demande(s) : )')
    expect(pont.appels.mock.calls.find((c) => c[0] === 'EcranRechAffaires.HistoriqueAncienne')!.slice(1)).toEqual(['004176'])

    fireEvent.click(within(rangee).getByRole('button', { name: 'Replier la ligne' }))
    expect(container.querySelector('.rig-lm-detail')).toBeNull()
  })

  it('le menu « … » d\'une ligne ouvre ses actions ; une ancienne affaire y propose l\'ancien RIG', async () => {
    const pont = installerFauxPont({
      'EcranRechAffaires.Trouver': vue, 'EcranRechAffaires.Aiguiller': aiguiller,
      'EcranRechAffaires.OuvrirDansAncienRig': (numero: unknown) => ({ fait: true, geste: 'lance', numero, ligneDeCommande: null, message: 'copie' }),
    })
    const ouvrir = vi.fn()
    render(<EcranRechAffaires ouvrir={ouvrir} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText('traitee dans la V2')
    fireEvent.click(screen.getByRole('button', { name: 'Actions de l\'affaire 2026B01067' }))
    expect(screen.getAllByRole('menuitem').map((m) => m.textContent)).toEqual(['Ouvrir', 'Historique', 'Deplier la ligne'])
    fireEvent.click(screen.getByRole('menuitem', { name: 'Ouvrir' }))
    expect(ouvrir).toHaveBeenCalledWith('2026B01067')

    fireEvent.click(screen.getByRole('button', { name: 'Actions de l\'affaire 004176' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Ouvrir dans l\'ancien RIG' }))
    expect((await screen.findByRole('status')).textContent).toBe('copie')
    expect(pont.appels.mock.calls.find((c) => c[0] === 'EcranRechAffaires.OuvrirDansAncienRig')!.slice(1)).toEqual(['004176'])
  })

  it('« Exporter » telecharge la liste affichee en CSV, les 13 colonnes du pont', async () => {
    installerFauxPont({ 'EcranRechAffaires.Trouver': varie })
    const recu: { blob?: Blob } = {}
    const creer = vi.fn((b: Blob) => { recu.blob = b; return 'blob:affaires' })
    Object.assign(URL, { createObjectURL: creer, revokeObjectURL: vi.fn() })
    const clic = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    try {
      render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
      await screen.findByText(/resultats ·/)
      fireEvent.change(screen.getByLabelText('Aller a une affaire…'), { target: { value: 'transnord' } })
      fireEvent.click(screen.getByRole('button', { name: 'Exporter' }))
      expect(clic).toHaveBeenCalledTimes(1)
      const lignes = (await recu.blob!.text()).replace(/^\uFEFF/, '').split('\r\n')
      expect(lignes).toEqual([
        'N° Affaire;Id instance;N° de role;Code affaire;Nature;Libelle;Nom;Etat;SIREN;Domaine;Saisine;Date creation;Solde',
        'J-00931;55339;2026F00301;010;Procedure collective;Depot de bilan - TRANSNORD SA;TRANSNORD SA;En cours;109 678 235;JUD;30/08/2026;01/09/2026;415,00',
      ])
    } finally {
      clic.mockRestore()
    }
  })
})

// La base du greffe (lot 6 du plan « RIG v3 100 % fonctionnel ») : Pont/EcranRechAffairesBase.cs rend le mode, une note, et les
// demandes et factures de chaque affaire (grille de PROC_RECHAFF), montrees dans la ligne depliee.
describe('EcranRechAffaires lue en base', () => {
  it('la note dit ce que la liste montre ; demandes et factures dans la ligne depliee ; aucune mention « exemple »', async () => {
    const note = "Base du greffe : les affaires trouvées par leur numéro (ou celui d'une demande, d'une facture), leur libellé ou SIREN, ou leur instance. Sans critère, RIG v2 ne cherche rien."
    installerFauxPont({
      'EcranRechAffaires.Trouver': (c: unknown, k: unknown, a: unknown, sel: unknown) => ({
        ...vue(c, k, a, sel), mode: 'serveur', message: note,
        lignes: [ligne('1642001', 'instance', { demandes: 'D1642101 ; D1642102', factures: 'F1642101' })],
      }),
      'EcranRechAffaires.Compter': (vues: unknown) => (vues as unknown[]).map(() => 1),
      'EcranRechAffaires.Aiguiller': () => ({ mode: 'serveur', greffe: '3801', passage: null, message: null, affaires: [] }),
    })
    const { container } = render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/resultat ·/)
    expect(screen.getByRole('note').textContent).toBe(note)
    expect(container.textContent).not.toMatch(/exemple/i)
    fireEvent.click(within(container.querySelector('tbody tr')!).getByRole('button', { name: 'Deplier la ligne' }))
    const champs = [...container.querySelectorAll('.rig-lm-detail .rig-lm-champ')].map((c) => [
      c.querySelector('.rig-lm-champ__libelle')!.textContent, c.querySelector('.rig-lm-champ__valeur')!.textContent])
    expect(champs.slice(-2)).toEqual([['Demandes', 'D1642101 ; D1642102'], ['Factures', 'F1642101']])
  })

  it('base en panne : le motif en alerte, aucune ligne', async () => {
    installerFauxPont({
      'EcranRechAffaires.Trouver': (c: unknown, k: unknown, a: unknown, sel: unknown) => ({
        ...vue(c, k, a, sel), lignes: [], resume: '', mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.',
      }),
      'EcranRechAffaires.Compter': (vues: unknown) => (vues as unknown[]).map(() => 0),
      'EcranRechAffaires.Aiguiller': () => ({ mode: 'serveur', greffe: '3801', passage: null, message: null, affaires: [] }),
    })
    const { container } = render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    expect((await screen.findByRole('alert')).textContent).toMatch(/La base du greffe 3801 ne répond pas/)
    expect(container.querySelectorAll('tbody tr .rig-lm-identite')).toHaveLength(0)
  })
})
