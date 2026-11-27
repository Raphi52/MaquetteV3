import { afterEach, describe, expect, it, vi } from 'vitest'
import { useContext, type ReactElement } from 'react'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { Aiguillage } from '../plan/Aiguillage'
import { EcranVisible } from '../plan/visibilite'
import type { DescripteurEcranVue, GestePlan, OngletsReunisVue, ProcVue } from '../plan/types'
import { proc } from '../test/plan'
import { contexteDossier } from '../test/dossier'
import { oublierVuesDeSession } from '../composants/ListeModerne'
import { EcranGenere } from './EcranGenere'
import { EcranOnglets, type ProprietesEcranOnglets } from './EcranOnglets'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); oublierVuesDeSession(); retirerFauxPont() })

// Fiche client et Clients lisent leur liste par le pont (lot 14) : sans serveur, le mode « demo » et leurs exemples.
const sansServeur = () => installerFauxPont({ 'EcranClients.Clients': () => Demo, 'EcranFicheClient.Clients': () => Demo })
const Demo = { mode: 'demo', message: null, clients: [], total: 0 }

// L'ECRAN REUNI EN ONGLETS (Pont/MenuAccueil.cs, EntreeMenu.EnOnglets) : les deux ecrans des
// clients, tels que la vue du C# les envoie (Pont/PlanTravailVue.cs, OngletsReunisVue).
const clients = (actif: string): OngletsReunisVue => ({
  onglets: [
    { code: 'PROC_GCLIENT', libelle: 'Clients', descripteur: null },
    { code: 'PROC_FCLIENT', libelle: 'Fiche client', descripteur: null },
  ],
  actif,
})
const reunion = (reunis: OngletsReunisVue, partiel: Partial<ProcVue['ecran']> = {}) =>
  proc({ code: 'REUNION_CLIENTS', libelle: 'Comptes clients', ecran: { ...proc().ecran, ...partiel, reunis } })

const onglet = (nom: string) => screen.getByRole('tab', { name: nom })
// Par son attribut : un panneau cache n'a plus de nom accessible pour getByRole.
const panneau = (nom: string) => document.querySelector<HTMLElement>(`[role="tabpanel"][aria-label="${nom}"]`)!
const raisonSociale = () => (within(panneau('Fiche client')).getByLabelText('Raison sociale') as HTMLInputElement).value

describe('EcranOnglets, monte par l\'aiguillage', () => {
  it('montre un onglet par ecran reuni, l\'onglet demande actif, et l\'ecran d\'origine tel quel', async () => {
    sansServeur()
    render(<Aiguillage proc={reunion(clients('PROC_FCLIENT'))} plan={vi.fn<GestePlan>()} />)
    await vi.waitFor(() => expect(within(panneau('Fiche client')).queryByLabelText('Raison sociale')).not.toBeNull())
    expect(screen.getByRole('tablist').getAttribute('aria-label')).toBe('Comptes clients')
    expect(screen.getAllByRole('tab').map((t) => [t.textContent, t.getAttribute('aria-selected')]))
      .toEqual([['Clients', 'false'], ['Fiche client', 'true']])
    // L'ecran d'origine (EcranFicheClient), avec sa fiche de depart : rien n'est recopie.
    expect(raisonSociale()).toBe('SELARL LEROY & ASSOCIES')
    // L'onglet jamais ouvert n'est pas monte : son ecran n'a rien demande.
    expect(panneau('Clients')).toBeNull()
  })

  it('un clic renvoie l\'onglet au plan ; l\'onglet quitte reste monte, cache, avec ce qu\'on y a saisi', async () => {
    sansServeur()
    const plan = vi.fn<GestePlan>()
    const { rerender } = render(<Aiguillage proc={reunion(clients('PROC_FCLIENT'))} plan={plan} />)
    await vi.waitFor(() => expect(within(panneau('Fiche client')).queryByLabelText('Choisir un client')).not.toBeNull())
    fireEvent.change(within(panneau('Fiche client')).getByLabelText('Choisir un client'), { target: { value: 'DUPONT' } })

    fireEvent.click(onglet('Fiche client'))
    expect(plan).not.toHaveBeenCalled()
    fireEvent.click(onglet('Clients'))
    expect(plan).toHaveBeenCalledWith('ongletReuni', 'PROC_GCLIENT')

    // Le C# retient l'onglet et redessine : l'ecran des clients (EcranClients) est montre.
    rerender(<Aiguillage proc={reunion(clients('PROC_GCLIENT'))} plan={plan} />)
    expect(onglet('Clients').getAttribute('aria-selected')).toBe('true')
    expect(panneau('Clients').hidden).toBe(false)
    await vi.waitFor(() => expect(panneau('Clients').querySelector('.rig-lm-synthese')!.textContent).toBe('6 clients sur 6 · 4 actifs'))
    expect(panneau('Fiche client').hidden).toBe(true)

    // Retour sur la fiche (une « Fiche client » cherchee, par exemple) : le client choisi est toujours la.
    rerender(<Aiguillage proc={reunion(clients('PROC_FCLIENT'))} plan={plan} />)
    expect(panneau('Fiche client').hidden).toBe(false)
    expect((within(panneau('Fiche client')).getByLabelText('Etat du compte') as HTMLInputElement).value).toBe('Bloque')
  })

  it('un onglet generique monte EcranGenere avec SON descripteur ; l\'affaire de l\'ecran reuni passe aux onglets', () => {
    const descripteur: DescripteurEcranVue = { code: 'PILOTAGE', titre: 'Pilotage', forme: 'Etat', sujet: 'tableaux de bord', recherche: '',
      criteres: [], valider: 'Valider', rang: 1, etats: [], groupes: [], marches: [], sections: [], colonnes: [], parametres: [],
      gestes: [], lignes: [], membres: [], enOnglets: false }
    const dossier = contexteDossier()
    const vue = reunion({ onglets: [{ code: 'PROC_STAT', libelle: 'Statistiques', descripteur: null },
                                   { code: 'PROC_XPILOTAGE', libelle: 'Pilotage', descripteur }], actif: 'PROC_XPILOTAGE' }, { dossier })
    // Lu sur les elements rendus, sans monter EcranGenere (qui appellerait son pont).
    const el = Aiguillage({ proc: vue, plan: vi.fn<GestePlan>() }) as ReactElement<ProprietesEcranOnglets>
    expect(el.type).toBe(EcranOnglets)
    const pilotage = el.props.ecran('PROC_XPILOTAGE') as ReactElement<{ proc: ProcVue }>
    expect(pilotage.props.proc.ecran).toMatchObject({ descripteur, reunis: null, dossier })
    expect((Aiguillage({ proc: pilotage.props.proc, plan: vi.fn<GestePlan>() }) as ReactElement).type).toBe(EcranGenere)
  })
})

describe('EcranOnglets', () => {
  // Une sonde a la place d'un ecran : elle dit si l'onglet la croit visible (plan/visibilite.ts).
  function Sonde({ code }: { code: string }) {
    return <span>{code + (useContext(EcranVisible) ? ' visible' : ' cache')}</span>
  }
  const onglets = [{ code: 'A', libelle: 'Alpha' }, { code: 'B', libelle: 'Beta' }, { code: 'C', libelle: 'Gamma' }]
  const monter = (actif: string, choisir = vi.fn<(code: string) => void>()) =>
    ({ choisir, ...render(<EcranOnglets titre="Reuni" onglets={onglets} actif={actif} choisir={choisir}
                                        ecran={(code) => <Sonde code={code} />} />) })

  it('un onglet inconnu montre le premier ; les fleches passent a l\'onglet voisin, en boucle', () => {
    const { choisir } = monter('INCONNU')
    expect(onglet('Alpha').getAttribute('aria-selected')).toBe('true')
    expect(onglet('Alpha').tabIndex).toBe(0)
    expect(onglet('Beta').tabIndex).toBe(-1)
    fireEvent.keyDown(screen.getByRole('tablist'), { key: 'ArrowRight' })
    fireEvent.keyDown(screen.getByRole('tablist'), { key: 'ArrowLeft' })
    fireEvent.keyDown(screen.getByRole('tablist'), { key: 'Enter' })
    expect(choisir.mock.calls).toEqual([['B'], ['C']])
  })

  it('un ecran cache se sait cache : ses ecoutes du clavier se taisent', () => {
    const { rerender } = monter('A')
    expect(screen.getByText('A visible')).toBeTruthy()
    rerender(<EcranOnglets titre="Reuni" onglets={onglets} actif="B" choisir={vi.fn()} ecran={(code) => <Sonde code={code} />} />)
    expect(screen.getByText('A cache')).toBeTruthy()
    expect(screen.getByText('B visible')).toBeTruthy()
    expect(screen.queryByText(/^C /)).toBeNull()
    // Dans un onglet du haut lui-meme cache, aucun de ses ecrans ne se croit visible.
    rerender(
      <EcranVisible.Provider value={false}>
        <EcranOnglets titre="Reuni" onglets={onglets} actif="B" choisir={vi.fn()} ecran={(code) => <Sonde code={code} />} />
      </EcranVisible.Provider>,
    )
    expect(screen.getByText('B cache')).toBeTruthy()
  })
})
