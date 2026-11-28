import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranExperts, type VueExperts } from './EcranExperts'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Sans serveur, le pont rend le mode « demo » : l'ecran montre ses experts d'exemple, marques (Pont/EcranExpertsBase.cs).
const Demo: VueExperts = { mode: 'demo', message: null, experts: [], mesures: [], total: 0 }
async function ouvrir(v: VueExperts = Demo) {
  const pont = installerFauxPont({ 'EcranExperts.Experts': () => v })
  const r = render(<EcranExperts />)
  await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranExperts.Experts'))
  await vi.waitFor(() => expect(r.container.querySelector('.rig-resultats-proc__compte')!.textContent).not.toBe('aucun expert selectionne'))
  return r
}

// Valeurs attendues : celles de l'ecran Blazor (EcranExperts.razor) : 3 experts dont 2 actifs,
// GIRARD choisi au depart.
const lignes = (c: HTMLElement, table = 0) =>
  [...c.querySelectorAll('table')[table]!.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')
/** La phrase de synthese de la liste des experts : le compte, les actifs, les qualites. */
const synthese = (c: HTMLElement) => espaces(c.querySelector('.rig-lm-synthese')!.textContent)
/** Le compteur de la fiche d'edition : l'abrege de l'expert choisi. */
const choisi = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent

describe('EcranExperts', () => {
  it('affiche les experts actifs, la fiche et les mesures en cours de GIRARD', async () => {
    const { container } = await ouvrir()
    expect(container.querySelector('.rig-lm-titre')!.textContent).toBe('Liste des experts')
    expect(synthese(container)).toBe('2 experts · 2 actifs · 2 qualites')
    const experts = [...container.querySelectorAll('table')[0]!.querySelectorAll('tbody tr')]
    expect(experts.map((tr) => {
      const td = [...tr.querySelectorAll('td')]
      return [td[0]!.textContent, td[1]!.textContent, tr.querySelector('.rig-lm-identite__nom')!.textContent,
              tr.querySelector('.rig-lm-identite__objet')!.textContent, td[3]!.textContent, td[4]!.textContent]
    })).toEqual([
      ['GIRARD', 'M.', 'GIRARD Paul', 'Expert-comptable', 'p.girard@exemple.fr', 'Actif'],
      ['NOEL', 'Mme', 'NOEL Julie', 'Expert en evaluation', 'j.noel@exemple.fr', 'Actif'],
    ])
    expect(experts[0]!.querySelector('.rig-lm-avatar')!.textContent).toBe('GP')
    expect(experts[0]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
    // La ligne choisie porte le trait de focus.
    expect((container.querySelector('tbody tr') as HTMLElement).style.boxShadow).toBe('inset 3px 0 0 0 var(--rig-focus)')
    expect((screen.getByLabelText('Complement de nom') as HTMLInputElement).value).toBe('Cabinet Girard & Fils')
    expect(lignes(container, 1)).toEqual([
      ['M-2026-0114', 'M.', 'GPGIRARD Paul', 'Cabinet Girard & Fils', 'Expert-comptable', '9 rue Gambetta, 51100 Reims'],
    ])
    expect([...container.querySelector('.rig-lm-pied')!.children].map((el) => espaces(el.textContent))).toEqual(['2 experts affiches', 'Actifs 2'])
  })

  it('les mesures terminees, les inactifs et le filtre de qualite', async () => {
    const { container } = await ouvrir()
    const mesures = screen.getByRole('group', { name: 'Vues des mesures' })
    expect([...mesures.querySelectorAll('.rig-lm-vue')].map((v) => espaces(v.textContent))).toEqual(['Mesures en cours 1', 'Mesures terminees 1'])
    fireEvent.click(screen.getByText('Mesures terminees'))
    expect(lignes(container, 1).map((l) => l[0])).toEqual(['M-2024-0503'])
    expect(within(mesures).getByRole('button', { name: 'Mesures terminees 1' }).getAttribute('aria-pressed')).toBe('true')

    fireEvent.click(screen.getByRole('button', { name: 'Experts actifs uniquement' }))
    expect(synthese(container)).toBe('3 experts · 2 actifs · 3 qualites')
    fireEvent.change(screen.getByLabelText('Qualite / profession'), { target: { value: 'Expert immobilier' } })
    expect(lignes(container).map((l) => l[0])).toEqual(['ROUX'])
    fireEvent.click(container.querySelector('tbody tr')!)
    expect(choisi(container)).toBe('ROUX')
    expect(container.querySelector('tbody tr .rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-gris')
    const pastille = screen.getByText('Inactif', { selector: '.rig-criteres__bloc > .rig-etat' })
    expect(pastille.className).toBe('rig-etat rig-etat--attente')
    // Dans la colonne flex de la fiche, la pastille garde sa largeur (elle courait sur toute la fiche).
    expect(pastille.style.alignSelf).toBe('flex-start')
  })

  // Un seul champ pour un seul filtre (decision du plan des listes, lot 7) : « Aller a un expert… » EST la recherche
  // v2 (ancien « Nom, abrege »). S'il redevenait un second filtre pose sur la liste affichee, le compte des vues
  // (« Tous 1 ») resterait a 3 ; s'il cherchait aussi la qualite, « immobilier » trouverait ROUX.
  it('les vues posent « Experts actifs uniquement » ; « Aller a un expert… » (Ctrl K) est la recherche nom/abrege, Entree choisit l\'expert reste seul', async () => {
    const { container } = await ouvrir()
    expect(screen.queryByLabelText('Nom, abrege')).toBeNull()
    const experts = screen.getByRole('group', { name: 'Vues des experts' })
    expect([...experts.querySelectorAll('.rig-lm-vue')].map((v) => [espaces(v.textContent), v.getAttribute('aria-pressed')]))
      .toEqual([['Actifs 2', 'true'], ['Tous 3', 'false']])
    fireEvent.click(within(experts).getByRole('button', { name: 'Tous 3' }))
    expect(screen.getByRole('button', { name: 'Experts actifs uniquement' }).className).toBe('rig-bascule')
    expect(synthese(container)).toBe('3 experts · 2 actifs · 3 qualites')

    const champ = screen.getByLabelText('Aller a un expert…')
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: '  bernard ' } })
    expect(lignes(container).map((l) => l[0])).toEqual(['ROUX'])
    expect([...experts.querySelectorAll('.rig-lm-vue')].map((v) => espaces(v.textContent))).toEqual(['Actifs 0', 'Tous 1'])
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(choisi(container)).toBe('ROUX')
    fireEvent.change(champ, { target: { value: 'noel' } })
    expect(lignes(container).map((l) => l[0])).toEqual(['NOEL'])
    fireEvent.change(champ, { target: { value: 'immobilier' } })
    expect(screen.getByText('Aucun expert ne repond a ces criteres.')).toBeTruthy()
  })

  // Branche sur la base (lot 14) : les experts du pont et leurs mesures, rattachees par le numero de l'expert ; aucun exemple.
  it('en base, les experts lus et leurs mesures par numero ; aucun exemple, aucune note', async () => {
    const expert = (abrege: string, cle: string) => ({ abrege, civilite: 'MR', nom: abrege + ' Paul', complementNom: '', naissance: '', adresse: '',
      tel: '', fax: '', mail: '', qualif: 'EC', actif: true, cle })
    const { container } = await ouvrir({ mode: 'serveur', message: null, total: 2, experts: [expert('EXA', '7'), expert('EXB', '8')],
      mesures: [{ expert: '8', numMesure: 'M14-1', civilite: 'MR', nomParticipant: 'EXB Paul', complementNom: '', qualifProf: 'EC', adresse: '', enCours: true }] })
    expect(lignes(container).map((l) => l[0])).toEqual(['EXA', 'EXB'])
    expect(screen.queryByText(/GIRARD/)).toBeNull()
    expect(screen.queryByRole('note')).toBeNull()
    fireEvent.click(container.querySelectorAll('table')[0]!.querySelectorAll('tbody tr')[1]!)
    expect(choisi(container)).toBe('EXB')
    expect(screen.getByText('M14-1')).toBeTruthy()
  })

  // « Enregistrer la fiche » (lot 9 du parcours complet) : en base, la saisie et les participants coches partent au pont ; faite, la
  // fiche relit la base ; refusee, le motif de RIG v2. La demonstration reste en lecture, sans bouton.
  it('en base, « Enregistrer la fiche » envoie la saisie et les participants coches, puis relit la base', async () => {
    const expert = (nom: string, tel: string) => ({ abrege: 'EXB', civilite: 'MR', nom, complementNom: '', naissance: '', adresse: '',
      tel, fax: '', mail: '', qualif: 'EC', actif: true, cle: '8' })
    const mesure = (nom: string) => ({ expert: '8', numMesure: 'M14-1', civilite: 'MR', nomParticipant: nom, complementNom: '', qualifProf: 'EC',
      adresse: '', enCours: true, participant: '81' })
    let fait = false
    const enregistrer = vi.fn(() => { fait = true; return { fait: true, message: 'Expert EXB enregistré, 1 participant(s) mis à jour.', sansEcrivain: false, ids: ['8'] } })
    const pont = installerFauxPont({
      'EcranExperts.Experts': () => ({ mode: 'serveur', message: null, total: 1,
        experts: [fait ? expert('EXB NOUVEAU', '0123456789') : expert('EXB Paul', '')], mesures: [mesure(fait ? 'EXB NOUVEAU' : 'EXB Paul')] }),
      'EcranExperts.Enregistrer': enregistrer,
    })
    render(<EcranExperts />)
    await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranExperts.Experts'))
    const nom = await screen.findByLabelText('Nom') as HTMLInputElement
    expect(nom.readOnly).toBe(false)
    fireEvent.change(nom, { target: { value: 'EXB NOUVEAU' } })
    fireEvent.change(screen.getByLabelText('Telephone'), { target: { value: '0123456789' } })
    fireEvent.click(screen.getByLabelText('Reporter la fiche sur M14-1'))
    fireEvent.click(screen.getByRole('button', { name: 'Enregistrer la fiche' }))
    expect((await screen.findByRole('status')).textContent).toBe('Expert EXB enregistré, 1 participant(s) mis à jour.')
    expect(enregistrer).toHaveBeenCalledWith('8', { nom: 'EXB NOUVEAU', complementNom: '', tel: '0123456789', fax: '', mail: '', participants: ['81'] })
    await vi.waitFor(() => expect((screen.getByLabelText('Telephone') as HTMLInputElement).value).toBe('0123456789'))
    expect(screen.getAllByText('EXB NOUVEAU').length).toBeGreaterThan(0)
  })

  it('en base, un refus de RIG v2 est dit, rien n\'est relu ; la demonstration reste en lecture', async () => {
    const pont = installerFauxPont({
      'EcranExperts.Experts': () => ({ mode: 'serveur', message: null, total: 1, mesures: [],
        experts: [{ abrege: 'EXB', civilite: 'MR', nom: 'EXB', complementNom: '', naissance: '', adresse: '', tel: '', fax: '', mail: '', qualif: '', actif: true, cle: '8' }] }),
      'EcranExperts.Enregistrer': () => ({ fait: false, message: '« Téléphone » : Numéro de téléphone invalide.', sansEcrivain: false, ids: [] }),
    })
    render(<EcranExperts />)
    fireEvent.change(await screen.findByLabelText('Telephone'), { target: { value: '12' } })
    fireEvent.click(screen.getByRole('button', { name: 'Enregistrer la fiche' }))
    expect((await screen.findByRole('alert')).textContent).toBe('Refus « Téléphone » : Numéro de téléphone invalide.')
    expect(pont.appels.mock.calls.filter((a) => a[0] === 'EcranExperts.Experts')).toHaveLength(1)
    cleanup()
    retirerFauxPont()
    await ouvrir()
    expect((screen.getByLabelText('Nom') as HTMLInputElement).readOnly).toBe(true)
    expect(screen.queryByRole('button', { name: 'Enregistrer la fiche' })).toBeNull()
  })
})
