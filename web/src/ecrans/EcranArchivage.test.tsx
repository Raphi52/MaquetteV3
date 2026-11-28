import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { Demonstration, EcranArchivage, type VueArchivage } from './EcranArchivage'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// La tete de la liste moderne : « 4 retenues sur 5 · 58 pieces · bordereau non prepare ».
const synthese = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent!.trim()
const vue = (nom: string) => screen.getByRole('button', { name: new RegExp(`^${nom} \\d+$`) })

// Sans serveur, le pont rend le mode « demo » : l'ecran montre ses formalites d'exemple (Pont/EcranArchivageBase.cs).
const Demo: VueArchivage = { mode: 'demo', message: null, lignes: [], total: 0, motif: null, types: ['A', 'B', 'C', 'D', 'AC', 'I'], maxBordereau: 150 }

/** L'ecran sans serveur, une fois le pont repondu (la synthese compte alors les cinq exemples). */
async function ouvrir(ouvrirDossier = vi.fn()) {
  const pont = installerFauxPont({ 'EcranArchivage.Dossiers': () => Demo })
  const r = render(<EcranArchivage ouvrirDossier={ouvrirDossier} />)
  await vi.waitFor(() => expect(synthese(r.container)).toContain('sur 5'))
  return { pont, ...r }
}

describe('EcranArchivage', () => {
  it('retient les radiations de 2012 a 2016, bordereau non prepare ; les exemples sont marques', async () => {
    const { container, pont } = await ouvrir()
    expect(pont.appels).toHaveBeenCalledWith('EcranArchivage.Dossiers', '2012-01-01', '2016-12-31', '', '')
    expect(synthese(container)).toBe('4 retenues sur 5 · 58 pieces · bordereau non prepare')
    expect(container.querySelector('.rig-tcw__exemple')!.textContent).toBe('exemple')
    const premiere = container.querySelectorAll('tbody tr')[0]!
    const cellules = premiere.querySelectorAll('td')
    expect(cellules).toHaveLength(6)
    // Dossier : avatar d'initiales, denomination en gras, numero de dossier en gris.
    expect(cellules[0]!.querySelector('.rig-lm-avatar')!.textContent).toBe('MC')
    expect(cellules[0]!.querySelector('.rig-lm-identite__nom')!.textContent).toBe('SARL MENUISERIE COLLIN')
    expect(cellules[0]!.querySelector('.rig-lm-identite__objet')!.textContent).toBe('1998B00214')
    expect(cellules[1]!.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-bleu')
    expect([...cellules].slice(1).map((td) => td.textContent)).toEqual(['PM', '12/03/2014', '04/11/2013', '18', 'A archiver'])
    expect(premiere.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('4 formalites affichees' + 'Pieces 58')
  })

  it('« Preparer le bordereau » n ecrit rien et le dit : « Demonstration » sans serveur, jamais « Sur bordereau »', async () => {
    const { container } = await ouvrir()
    fireEvent.click(screen.getByRole('button', { name: 'Preparer le bordereau' }))
    expect(screen.getByRole('status').textContent).toBe(Demonstration)
    expect(synthese(container)).toBe('4 retenues sur 5 · 58 pieces · bordereau non prepare')
    expect([...container.querySelectorAll('.rig-lm-etat')].every((e) => e.textContent === 'A archiver')).toBe(true)
  })

  it('filtre par type RCS, par dates, et borne au nombre de dossiers', async () => {
    const { container } = await ouvrir()
    fireEvent.change(screen.getByLabelText('Type RCS'), { target: { value: 'PP' } })
    expect(synthese(container)).toBe('2 retenues sur 5 · 16 pieces · bordereau non prepare')
    fireEvent.change(screen.getByLabelText('Type RCS'), { target: { value: '' } })

    fireEvent.change(screen.getByLabelText('Date maxi.'), { target: { value: '' } })
    expect(synthese(container)).toBe('5 retenues sur 5 · 69 pieces · bordereau non prepare')

    fireEvent.change(screen.getByLabelText('Nombre de dossiers'), { target: { value: '2' } })
    expect(synthese(container)).toBe('2 retenues sur 5 · 42 pieces · bordereau non prepare')

    fireEvent.change(screen.getByLabelText('Date mini.'), { target: { value: '2030-01-01' } })
    expect(screen.getByText('Aucune formalite dans cette plage de radiation.')).toBeTruthy()
  })

  it('les vues sont le critere Type RCS, avec le compte de chacune', async () => {
    const { container } = await ouvrir()
    expect([...container.querySelectorAll('.rig-lm-vue')].map((v) => v.textContent))
      .toEqual(['Toutes 4', 'Personnes morales 2', 'Personnes physiques 2'])
    expect(vue('Toutes').getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(vue('Personnes morales'))
    expect((screen.getByLabelText('Type RCS') as HTMLSelectElement).value).toBe('PM')
    expect(vue('Personnes morales').getAttribute('aria-pressed')).toBe('true')
    expect(synthese(container)).toBe('2 retenues sur 5 · 42 pieces · bordereau non prepare')
    // Le critere pose a la main allume la vue qui lui correspond.
    fireEvent.change(screen.getByLabelText('Type RCS'), { target: { value: 'PP' } })
    expect(vue('Personnes physiques').getAttribute('aria-pressed')).toBe('true')
  })

  it('« Aller a une formalite… » filtre la liste affichee ; Entree ouvre le dossier reste seul', async () => {
    const ouvrirDossier = vi.fn()
    const { container } = await ouvrir(ouvrirDossier)
    const champ = screen.getByLabelText('Aller a une formalite…')
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)

    fireEvent.change(champ, { target: { value: 'martin' } })
    expect([...container.querySelectorAll('tbody tr b')].map((b) => b.textContent)).toEqual(['MARTIN Claire'])
    expect(synthese(container)).toBe('1 retenue sur 5 · 9 pieces · bordereau non prepare')
    // Les criteres ne bougent pas.
    expect((screen.getByLabelText('Type RCS') as HTMLSelectElement).value).toBe('')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(ouvrirDossier).toHaveBeenCalledWith('1999A00456')

    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 4 formalites ne contient « zzz ».')).toBeTruthy()
  })

  it('une formalite cliquee remonte son dossier', async () => {
    const ouvrirDossier = vi.fn()
    await ouvrir(ouvrirDossier)
    fireEvent.click(screen.getByText('DUPONT Jean'))
    expect(ouvrirDossier).toHaveBeenCalledWith('1995A00120')
  })
})

// Branche sur la base (plan « RIG v3 100 % fonctionnel », lot 20) : les dossiers radies de la base ; les types de RIG v2 ;
// le type, obligatoire, part au pont ; « Preparer le bordereau » est ecrit par le serveur (lot 11 du plan du parcours complet) :
// son compte rendu, puis la liste relue -- son refus en alerte ; en panne, le motif et aucune ligne.
// fix-ok: pas un correctif -- lot 11 (28/11/2026) : SansBordereau (« la V3 n'édite pas encore ») n'est plus vrai, le geste écrit.
describe('EcranArchivage branche sur la base', () => {
  const lue = { dossier: '2019B27001', denomination: 'ALPHA BASE SARL', type: 'B1', radiation: '2015-06-03' }
  const Base = (v: Partial<VueArchivage>): VueArchivage =>
    ({ mode: 'serveur', message: null, lignes: [], total: 0, motif: null, types: ['A', 'B', 'C', 'D', 'AC', 'I'], maxBordereau: 150, ...v })

  it('sans type, le motif de RIG v2 ; un type choisi lit ses dossiers, sans colonne inventee', async () => {
    const pont = installerFauxPont({
      'EcranArchivage.Dossiers': (_du: unknown, _au: unknown, type: unknown) => type === 'B'
        ? Base({ lignes: [lue], total: 1 })
        : Base({ motif: 'Saisissez les dates de radiation et le type RCS : RIG v2 les exige pour chercher les dossiers à archiver.' }),
    })
    const { container } = render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    await screen.findByText(/RIG v2 les exige/)
    expect([...(screen.getByLabelText('Type RCS') as HTMLSelectElement).options].map((o) => o.value)).toEqual(['', 'A', 'B', 'C', 'D', 'AC', 'I'])
    expect(screen.queryByText('SARL MENUISERIE COLLIN')).toBeNull()
    fireEvent.change(screen.getByLabelText('Type RCS'), { target: { value: 'B' } })
    await screen.findByText('ALPHA BASE SARL')
    expect(pont.appels).toHaveBeenCalledWith('EcranArchivage.Dossiers', '2012-01-01', '2016-12-31', 'B', '')
    expect(synthese(container)).toBe('1 retenue sur 1 · bordereau non prepare')
    const cellules = [...container.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')].slice(1).map((td) => td.textContent)
    expect(cellules).toEqual(['B1', '03/06/2015', '', '', 'A archiver'])
    // « Nombre de dossiers » est le compte de RIG v2, en lecture ; aucune colonne de pieces inventee au pied.
    const compteV2 = screen.getByLabelText('Nombre de dossiers') as HTMLInputElement
    expect([compteV2.value, compteV2.readOnly]).toEqual(['1', true])
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('1 formalite affichee')
    expect(container.querySelector('.rig-tcw__exemple')).toBeNull()
  })

  it('« Preparer le bordereau » en base : le geste part au pont avec les criteres, son compte rendu, puis la liste relue', async () => {
    let prepare = false
    const pont = installerFauxPont({
      'EcranArchivage.Dossiers': () => Base(prepare ? { lignes: [], total: 0 } : { lignes: [lue], total: 1 }),
      'EcranArchivage.Bordereau': () => {
        prepare = true
        return { fait: true, sansEcrivain: false, ids: ['27001'],
                 message: 'Bordereau préparé pour 1 dossier(s) (2019B27001) : annexe « Historique des FORMALITES » et bordereau BGEDANNEX_1_IMP à imprimer.' }
      },
    })
    render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    await screen.findByText('ALPHA BASE SARL')
    fireEvent.click(screen.getByRole('button', { name: 'Preparer le bordereau' }))
    expect((await screen.findByRole('status')).textContent).toContain('Bordereau préparé pour 1 dossier(s) (2019B27001)')
    expect(pont.appels).toHaveBeenCalledWith('EcranArchivage.Bordereau', '2012-01-01', '2016-12-31', '', '')
    await vi.waitFor(() => expect(screen.queryByText('ALPHA BASE SARL')).toBeNull())
  })

  it('« Preparer le bordereau » refuse par le serveur : le refus en alerte, la liste inchangee', async () => {
    installerFauxPont({
      'EcranArchivage.Dossiers': () => Base({ lignes: [lue], total: 1 }),
      'EcranArchivage.Bordereau': () => ({ fait: false, sansEcrivain: false, ids: [],
        message: '0 dossier(s) à archiver : RIG v2 ne prépare le bordereau que pour 1 à 149 dossiers (VALIDER reste grisé). Rien n’est enregistré.' }),
    })
    render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    await screen.findByText('ALPHA BASE SARL')
    fireEvent.click(screen.getByRole('button', { name: 'Preparer le bordereau' }))
    expect((await screen.findByRole('alert')).textContent).toContain('ne prépare le bordereau que pour 1 à 149 dossiers')
    expect(screen.getByText('ALPHA BASE SARL')).not.toBeNull()
  })

  // Capture 14 du lot 11 (28/11/2026) : le refus du bordereau sur 1960A00017 restait affiché sous la liste relue d'autres dates,
  // où ce dossier n'était plus. Un compte rendu vaut pour les critères qui l'ont produit : en changer l'efface.
  it('un refus disparait quand les criteres changent : il ne parle plus de la liste affichee', async () => {
    installerFauxPont({
      'EcranArchivage.Dossiers': () => Base({ lignes: [lue], total: 1 }),
      'EcranArchivage.Bordereau': () => ({ fait: false, sansEcrivain: false, ids: [],
        message: "Le dossier 1960A00017 n'a aucune action au registre : RIG v2 s'arrête sur lui sans bordereau. Rien n'est enregistré." }),
    })
    render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    await screen.findByText('ALPHA BASE SARL')
    fireEvent.click(screen.getByRole('button', { name: 'Preparer le bordereau' }))
    expect((await screen.findByRole('alert')).textContent).toContain('1960A00017')
    fireEvent.change(screen.getByLabelText('Date mini.'), { target: { value: '2011-01-01' } })
    await vi.waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
  })

  it('150 dossiers ou plus : RIG v2 refuse le bordereau, l ecran le dit', async () => {
    installerFauxPont({ 'EcranArchivage.Dossiers': () => Base({ lignes: [lue], total: 150 }) })
    render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    await screen.findByText("150 dossiers : RIG v2 n'admet le bordereau que pour moins de 150 dossiers.")
  })

  it('en panne : le motif, aucune ligne d exemple', async () => {
    installerFauxPont({ 'EcranArchivage.Dossiers': () => Base({ mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.' }) })
    render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    await screen.findByText(/La base du greffe 3801 ne répond pas/)
    expect(screen.queryByText('SARL MENUISERIE COLLIN')).toBeNull()
  })
})
