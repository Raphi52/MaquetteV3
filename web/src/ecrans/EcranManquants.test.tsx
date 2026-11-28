import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranManquants, type NumeroManquant, type VueEcranManquants } from './EcranManquants'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// La liste (repris de la V2, declares dans la V3, rendus exclus) est tenue par la vue num.NumeroManquant et la route
// du serveur, prouvees par tests/NumerotationCompteurCheck. Ici : l'ecran montre la liste et la filtre.
function manquant(partiel: Partial<NumeroManquant> = {}): NumeroManquant {
  return {
    serie: 'RC1', annee: 2024, jour: 0, chrono: 8948, numero: 'F24/008948', motif: 'manquant constaté par la V2, motif inconnu',
    logiciel: 'V2', declareLe: '2026-11-20T18:00:00', ...partiel,
  }
}

function vue(partiel: Partial<VueEcranManquants> = {}): VueEcranManquants {
  return {
    mode: 'serveur', message: null, greffe: '3801',
    manquants: [
      manquant(),
      manquant({ annee: 2025, chrono: 14518, numero: 'F25/014518' }),
      manquant({ serie: 'RC5', annee: 2026, chrono: 7, numero: 'F26/000007', motif: 'inscription abandonnée', logiciel: 'V3' }),
    ],
    ...partiel,
  }
}

const synthese = () => document.querySelector('.rig-lm-synthese')!.textContent
const lignes = () => screen.getAllByRole('row').slice(1)
/** Une puce de vue : la serie suivie de son compte. */
const puce = (libelle: string) => screen.getByRole('button', { name: new RegExp(`^${libelle} \\d+$`) })

describe('EcranManquants', () => {
  it('NUM-013 T04, CA02, CA03 : chaque numero manquant avec son motif et son origine', async () => {
    installerFauxPont({ 'EcranManquants.Ouvrir': () => vue() })
    render(<EcranManquants />)
    expect(await screen.findByText('F24/008948')).toBeTruthy()
    expect(synthese()).toBe('3 numéro(s) — greffe 3801 · 2 repris de la V2')
    expect(lignes()[0]!.textContent).toContain('F24/008948')
    expect(lignes()[0]!.textContent).toContain('manquant constaté par la V2, motif inconnu')
    expect(lignes()[2]!.textContent).toContain('inscription abandonnée')
    expect(lignes()[2]!.textContent).toContain('V3')
    // Serie et origine en pastilles.
    expect(lignes()[2]!.querySelectorAll('.rig-lm-pastille')[1]!.className).toBe('rig-lm-pastille rig-lm-t-bleu')
    expect(lignes()[0]!.querySelectorAll('.rig-lm-pastille')[1]!.className).toBe('rig-lm-pastille rig-lm-t-ardoise')
  })

  it('NUM-013 CA04 : filtre par serie et par annee (RC1, RC5 et RC6 partagent la forme « F »)', async () => {
    installerFauxPont({ 'EcranManquants.Ouvrir': () => vue() })
    render(<EcranManquants />)
    await screen.findByText('F24/008948')
    fireEvent.change(screen.getByLabelText('Série'), { target: { value: 'RC1' } })
    expect(synthese()).toBe('2 numéro(s) — greffe 3801 · 2 repris de la V2')
    fireEvent.change(screen.getByLabelText('Année'), { target: { value: '2025' } })
    expect(lignes()).toHaveLength(1)
    expect(lignes()[0]!.textContent).toContain('F25/014518')
    fireEvent.change(screen.getByLabelText('Série'), { target: { value: 'RC5' } })
    expect(screen.getByText(/le registre est continu/)).toBeTruthy()
  })

  it('sans la fonction LNUMCM : le refus du serveur, sans liste', async () => {
    installerFauxPont({ 'EcranManquants.Ouvrir': () => vue({ mode: 'refuse', manquants: [], message: 'Refuse : le compte x n\'a pas la fonction LNUMCM.' }) })
    render(<EcranManquants />)
    const alerte = await screen.findByRole('alert')
    expect(alerte.textContent).toContain('Accès refusé')
    expect(alerte.textContent).toContain('LNUMCM')
    expect(alerte.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('sans serveur : la liste de demonstration', async () => {
    installerFauxPont({ 'EcranManquants.Ouvrir': () => vue({ mode: 'demo', greffe: null }) })
    render(<EcranManquants />)
    expect(await screen.findAllByText('Démonstration')).toHaveLength(1)
    expect(synthese()).toBe('Démonstration · 3 numéro(s) · 2 repris de la V2')
    expect(lignes()).toHaveLength(3)
  })
})

describe('EcranManquants en liste moderne', () => {
  it('une vue par serie pose le critere « Série » et remet l\'annee a « Toutes »', async () => {
    installerFauxPont({ 'EcranManquants.Ouvrir': () => vue() })
    render(<EcranManquants />)
    await screen.findByText('F24/008948')
    expect(['Toutes', 'RC1', 'RC5'].map((s) => puce(s).textContent)).toEqual(['Toutes 3', 'RC1 2', 'RC5 1'])
    expect(puce('Toutes').getAttribute('aria-pressed')).toBe('true')

    fireEvent.change(screen.getByLabelText('Année'), { target: { value: '2025' } })
    // Une annee choisie a la main : aucune vue ne la decrit.
    expect(puce('Toutes').getAttribute('aria-pressed')).toBe('false')
    fireEvent.click(puce('RC1'))
    expect((screen.getByLabelText('Série') as HTMLSelectElement).value).toBe('RC1')
    expect((screen.getByLabelText('Année') as HTMLSelectElement).value).toBe('')
    expect(lignes()).toHaveLength(2)
    expect(puce('RC1').getAttribute('aria-pressed')).toBe('true')
  })

  it('« Aller à un numéro… » filtre la liste affichee sur le numero et le motif', async () => {
    installerFauxPont({ 'EcranManquants.Ouvrir': () => vue() })
    render(<EcranManquants />)
    await screen.findByText('F24/008948')
    const champ = screen.getByRole('textbox', { name: 'Aller à un numéro…' })
    fireEvent.change(champ, { target: { value: 'abandonn' } })
    expect(lignes()).toHaveLength(1)
    expect(lignes()[0]!.textContent).toContain('F26/000007')
    expect(document.querySelector('.rig-lm-pied')!.textContent).toBe('1 numéro affichéRepris de la V2 0Déclarés dans la V3 1')
    // Sans accents, comme l'ecran modele (Recherche d'affaires) : « abandonnee » trouve « abandonnée ».
    fireEvent.change(champ, { target: { value: 'abandonnee' } })
    expect(lignes()).toHaveLength(1)
    expect(lignes()[0]!.textContent).toContain('F26/000007')
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 3 numéros ne contient « zzz ».')).toBeTruthy()
  })
})

// Le geste (lot 11 du plan du parcours complet) : « Éditer la liste », l'etape ALNUMCM de LNUMCM, ecrite par le serveur
// (Serveur/Socle/EcransMain/EcritureManquants.cs, prouvee par tests/ParcoursCompletCheck/Sondes/Manquants.cs).
describe('EcranManquants : editer la liste', () => {
  it('le geste part au pont et son compte rendu s affiche ; le refus du serveur en alerte', async () => {
    let refuser = false
    const pont = installerFauxPont({
      'EcranManquants.Ouvrir': () => vue(),
      'EcranManquants.Editer': () => refuser
        ? { fait: false, sansEcrivain: false, ids: [], message: 'Refuse : le compte x n\'a pas la fonction LNUMCM (Liste des numeros chrono manquants) dans le greffe 3801.' }
        : { fait: true, sansEcrivain: false, ids: ['4100601'], message: 'Liste des numéros chrono manquants à imprimer (demande D2633200046).' },
    })
    render(<EcranManquants />)
    fireEvent.click(await screen.findByRole('button', { name: 'Éditer la liste' }))
    expect((await screen.findByRole('status')).textContent).toContain('à imprimer (demande D2633200046)')
    expect(pont.appels).toHaveBeenCalledWith('EcranManquants.Editer')
    refuser = true
    fireEvent.click(screen.getByRole('button', { name: 'Éditer la liste' }))
    expect((await screen.findByRole('alert')).textContent).toContain("n'a pas la fonction LNUMCM")
  })
})
