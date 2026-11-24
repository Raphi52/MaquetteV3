import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranChaines, euros, periodeLisible, type EvenementSigne, type GrandTotal, type VueEcranChaines } from './EcranChaines'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })
// fix-ok: tests suivis sur la liste moderne (lot 6 des listes) : puces de vues, pastilles, ligne depliee, filtre Ctrl K, pied -- pas un correctif.

// Les regles (signature chainee, controle 90 / 95, purge apres archive) vivent dans le serveur et sont prouvees par
// tests/SocleChainesCheck. Ici : l'ecran montre le journal et les totaux que le pont lui rend, en lecture seule, et
// remonte les criteres, le controle et la purge.
function evenement(partiel: Partial<EvenementSigne> = {}): EvenementSigne {
  return {
    id: 1, instant: '2026-09-01T05:59:00Z', code: 260, libelle: 'Initialisation des données',
    detail: 'Initialisation des données : ouverture des 7 chaînes signées de la V3.', auteur: 'AMITEL\\banc.amitel',
    purgeable: false, chaine: 'JNP', exercice: 2026, seq: 1, ...partiel,
  }
}

function total(partiel: Partial<GrandTotal> = {}): GrandTotal {
  return {
    chaine: 'GTF', type: 'GTP', periode: '20261120', total: 1284.6, cumulReel: 61902.35, cumulAbsolu: 62310.35, nbPieces: 14,
    calculeLe: '2026-11-20T19:02:00Z', information: 'Total du 20 NOVEMBRE 2026', seq: 83, parTaux: [{ taux: 20, montantTtc: 1001.1 }],
    ...partiel,
  }
}

function vue(partiel: Partial<VueEcranChaines> = {}): VueEcranChaines {
  return {
    mode: 'serveur', message: null, echec: false, greffe: '8002',
    etat: {
      greffe: '8002', ouverte: true, active: true, ouverteLe: '2026-09-01T05:59:00Z', fuseau: 'Romance Standard Time',
      empreinteCle: 'abc', enAttente: 0, exercicesArchives: [2025],
      chaines: [
        { code: 'FAC', libelle: 'Factures', maillons: 3, derniereSeq: 3, dernierMaillonLe: null },
        { code: 'JNP', libelle: 'Journal des événements non purgeables', maillons: 2, derniereSeq: 2, dernierMaillonLe: null },
      ],
    },
    filtre: { partie: null, code: null, du: null, au: null },
    journal: [evenement(), evenement({ id: 2, code: 50, libelle: 'Clôture de période (Jour/Mois)', purgeable: true, chaine: 'JP', seq: 4 }),
              evenement({ id: 3, code: 10, libelle: 'Changement du mode', seq: null, chaine: null })],
    totaux: [total(), total({ chaine: 'GTE', periode: '20261119', total: 1580, parTaux: [] }), total({ type: 'GTM', periode: '202610', total: 24410 })],
    codes: [{ code: 10, libelle: 'Changement du mode', purgeable: false, exigibilite: 'C' },
            { code: 260, libelle: 'Initialisation des données', purgeable: false, exigibilite: 'C' }],
    controle: null,
    ...partiel,
  }
}

describe('EcranChaines', () => {
  it('SOC-018 CA07 : montre le journal des evenements, signe ou en attente, et l\'etat des 7 chaines', async () => {
    installerFauxPont({ 'EcranChaines.Ouvrir': () => vue() })
    render(<EcranChaines />)
    expect(await screen.findByRole('button', { name: 'Journal 3' })).toBeTruthy()
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(3)
    expect(lignes[0]!.textContent).toContain('260')
    expect(lignes[0]!.textContent).toContain('NP n° 1')
    expect(lignes[1]!.textContent).toContain('P 2026 n° 4')
    expect(lignes[2]!.textContent).toContain('En attente')
    expect(screen.getByLabelText('Les 7 chaînes signées').textContent).toContain('Factures : 3')
    // Le maillon en pastille (non purgeable ardoise, purgeable bleu), en attente en point orange ; l'auteur en avatar.
    expect(lignes[0]!.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-ardoise')
    expect(lignes[1]!.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-bleu')
    expect(lignes[2]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
    expect(lignes[0]!.querySelector('.rig-lm-identite')!.textContent).toBe('ABAMITEL\\banc.amitel')
    expect(document.querySelector('.rig-lm-titre')!.textContent).toBe('Journal des événements')
    expect(document.querySelector('.rig-lm-synthese')!.textContent)
      .toBe('Greffe 8002 — chaînes ouvertes le 01/09/2026 07:59 · 3 événement(s) · 1 en attente de signature')
    expect(document.querySelector('.rig-lm-pied')!.textContent)
      .toBe('3 événement(s) affiché(s)En attente de signature 1Lecture seule : un événement signé ne se modifie pas.')
  })

  it('SOC-018 CA07 : filtre par partie, type et date, et le remonte au pont', async () => {
    const pont = installerFauxPont({
      'EcranChaines.Ouvrir': () => vue(),
      'EcranChaines.Rechercher': () => vue({ journal: [evenement({ id: 9, code: 10, libelle: 'Changement du mode' })] }),
    })
    render(<EcranChaines />)
    await screen.findByRole('button', { name: 'Journal 3' })
    fireEvent.change(screen.getByLabelText('Partie du journal'), { target: { value: 'np' } })
    fireEvent.change(screen.getByLabelText("Type d'événement"), { target: { value: '10' } })
    fireEvent.change(screen.getByLabelText('Du'), { target: { value: '2026-09-01' } })
    fireEvent.change(screen.getByLabelText('Au'), { target: { value: '2026-11-30' } })
    fireEvent.click(screen.getByRole('button', { name: 'Rechercher' }))
    expect(await screen.findByRole('button', { name: 'Journal 1' })).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranChaines.Rechercher', { partie: 'np', code: 10, du: '2026-09-01', au: '2026-11-30' })
  })

  it('SOC-027 : Entree, F5 et F7 dans un critere lancent la recherche, sans laisser F5 recharger la page', async () => {
    const pont = installerFauxPont({ 'EcranChaines.Ouvrir': () => vue(), 'EcranChaines.Rechercher': () => vue() })
    render(<EcranChaines />)
    await screen.findByRole('button', { name: 'Journal 3' })
    for (const [rang, touche] of ['Enter', 'F5', 'F7'].entries()) {
      const laissee = fireEvent.keyDown(screen.getByLabelText('Partie du journal'), { key: touche })
      expect(laissee).toBe(touche === 'Enter')
      expect(pont.appels.mock.calls.filter(([m]) => m === 'EcranChaines.Rechercher')).toHaveLength(rang + 1)
    }
  })

  it('SOC-018 CA07 : les grands totaux des factures et des encaissements se lisent, sans rien a modifier', async () => {
    installerFauxPont({ 'EcranChaines.Ouvrir': () => vue() })
    render(<EcranChaines />)
    await screen.findByRole('button', { name: 'Journal 3' })
    fireEvent.click(screen.getByRole('button', { name: 'Grands totaux des factures' }))
    let lignes = screen.getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(2)
    expect(lignes[0]!.textContent).toContain('20/11/2026')
    expect(lignes[0]!.textContent).toContain(euros(1284.6))
    expect(lignes[1]!.textContent).toContain('Mois')
    expect(lignes.map((l) => l.querySelector('.rig-lm-pastille')!.className))
      .toEqual(['rig-lm-pastille rig-lm-t-bleu', 'rig-lm-pastille rig-lm-t-violet'])
    // Huit colonnes d'avant : cumul absolu, taux de TVA, date de calcul et maillon sont dans la ligne depliee.
    expect(lignes[0]!.textContent).not.toContain(euros(62310.35))
    fireEvent.click(within(lignes[0]!).getByRole('button', { name: 'Deplier la ligne' }))
    const detail = document.querySelector('tr.rig-lm-detail')!
    expect(detail.textContent).toContain(euros(62310.35))
    expect(detail.textContent).toContain(`20 % : ${euros(1001.1)}`)
    expect(detail.textContent).toContain('Maillon83')
    expect(document.querySelector('.rig-lm-pied')!.textContent).toContain(`Cumul perpétuel ${euros(61902.35)}`)
    fireEvent.click(within(lignes[0]!).getByRole('button', { name: 'Replier la ligne' }))
    expect(document.querySelector('tr.rig-lm-detail')).toBeNull()
    expect(screen.queryAllByRole('textbox').filter((t) => (t as HTMLInputElement).closest('table'))).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'Grands totaux des encaissements' }))
    lignes = screen.getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(1)
    expect(lignes[0]!.textContent).toContain(euros(1580))
  })

  it('SOC-018 CA07 : la purge vise un exercice et montre le refus du serveur tel quel', async () => {
    const pont = installerFauxPont({
      'EcranChaines.Ouvrir': () => vue(),
      'EcranChaines.Purger': () => vue({
        echec: true, message: "Le journal purgeable de l'exercice 2026 ne se purge qu'après son archive fiscale (clôture de l'exercice) : rien n'a été effacé.",
      }),
    })
    render(<EcranChaines />)
    await screen.findByRole('button', { name: 'Journal 3' })
    fireEvent.change(screen.getByLabelText('Exercice à purger'), { target: { value: '2026' } })
    fireEvent.click(screen.getByRole('button', { name: 'Purger le journal purgeable' }))
    expect((await screen.findByRole('alert')).textContent).toContain("ne se purge qu'après son archive fiscale")
    expect(pont.appels).toHaveBeenCalledWith('EcranChaines.Purger', { exercice: 2026 })
  })

  it('SOC-018 CA04 : le controle remonte son bilan et la liste des defauts trouves', async () => {
    installerFauxPont({
      'EcranChaines.Ouvrir': () => vue(),
      'EcranChaines.Controler': () => vue({
        echec: true, message: 'Contrôle : 1 défaut(s) d\'intégrité et 0 rupture(s) de séquence, écrits au journal non purgeable (événements 90 et 95).',
        controle: {
          le: '2026-11-21T10:00:00Z', valeurProbante: true, reserve: null, signeesAvant: 0, archives: [], evenementsEcrits: [12], integre: false,
          chaines: [{ code: 'FAC', libelle: 'Factures', maillons: 3, valides: 2, defauts: ['maillon 2 (facture V3-26-000002) : la signature ne correspond plus au contenu'], ruptures: [] }],
        },
      }),
    })
    render(<EcranChaines />)
    await screen.findByRole('button', { name: 'Journal 3' })
    fireEvent.click(screen.getByRole('button', { name: "Contrôler l'intégrité" }))
    expect((await screen.findByRole('alert')).textContent).toContain('1 défaut')
    expect(screen.getByText(/Factures — maillon 2 \(facture V3-26-000002\)/)).toBeTruthy()
  })

  it('demonstration : lecture seule, controle et purge desactives', async () => {
    installerFauxPont({ 'EcranChaines.Ouvrir': () => vue({ mode: 'demo', greffe: null }) })
    render(<EcranChaines />)
    expect(await screen.findByText('Démonstration', { selector: '.rig-lm-synthese b' })).toBeTruthy()
    expect((screen.getByRole('button', { name: "Contrôler l'intégrité" }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Purger le journal purgeable' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('refus : un agent sans fonction de facturation voit la raison, sans journal', async () => {
    installerFauxPont({
      'EcranChaines.Ouvrir': () => ({ ...vue({ mode: 'refuse', echec: true, journal: [], totaux: [], etat: null }),
        message: "Refusé : le compte AMITEL\\banc.agent n'a aucune fonction de facturation ou d'encaissement dans le greffe 8002." }),
    })
    render(<EcranChaines />)
    expect((await screen.findByRole('alert')).textContent).toContain('aucune fonction de facturation')
    expect(screen.queryAllByRole('row')).toHaveLength(0)
  })

  it('lit les periodes des grands totaux', () => {
    expect(periodeLisible('GTP', '20251230')).toBe('30/12/2025')
    expect(periodeLisible('GTM', '202512')).toBe('12/2025')
    expect(periodeLisible('GTA/E', '2025')).toBe('exercice 2025')
  })
})

// « Aller a un evenement… » (Ctrl K) filtre le journal AFFICHE, sans redemander au pont (lot 6 des listes modernes).
describe('EcranChaines, filtre rapide', () => {
  it('Ctrl K pose le curseur dans le champ du journal ; la frappe filtre ; les totaux n\'en ont pas', async () => {
    const pont = installerFauxPont({ 'EcranChaines.Ouvrir': () => vue() })
    render(<EcranChaines />)
    await screen.findByRole('button', { name: 'Journal 3' })
    const appels = pont.appels.mock.calls.length
    const champ = screen.getByLabelText('Aller à un événement…')
    expect(fireEvent.keyDown(window, { key: 'k', ctrlKey: true })).toBe(false)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: 'cloture' } })
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(1)
    expect(lignes[0]!.textContent).toContain('Clôture de période')
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 3 événements ne contient « zzz ».')).toBeTruthy()
    expect(pont.appels.mock.calls.length).toBe(appels)
    fireEvent.click(screen.getByRole('button', { name: 'Grands totaux des factures' }))
    expect(screen.queryByLabelText('Aller à un événement…')).toBeNull()
  })
})
