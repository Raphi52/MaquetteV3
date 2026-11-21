// Les ecrans A SESSION (lot 2) : l'etat vit dans le pont C#, React affiche la
// vue. Ce que ce module promet aux ecrans, verifie ici sans ecran reel :
// une session par montage, fermee au demontage ; seule la reponse du DERNIER
// geste est appliquee ; un champ n'est reecrit que quand la session change sa
// valeur (la regle du @bind de Blazor).

import { afterEach, describe, expect, it, vi } from 'vitest'
import { useState } from 'react'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { Saisie, useSession, type PontDeSession, type VueDeSession } from './session'

afterEach(cleanup)

interface VueTest extends VueDeSession { texte: string }

function Sonde({ pont }: { pont: PontDeSession<VueTest> }) {
  const [vue, geste] = useSession(pont)
  if (!vue) return <p>rien</p>
  return (
    <>
      <p data-testid="texte">{vue.texte}</p>
      <button onClick={() => geste('Poser', 'a')}>a</button>
      <button onClick={() => geste('Poser', 'ab')}>ab</button>
      <button onClick={() => geste('Basculer')}>sans valeur</button>
    </>
  )
}

/** Un pont dont chaque reponse attend qu'on la libere, dans l'ordre choisi par le test. */
function pontManuel() {
  const enAttente: Array<{ geste: string; valeur: unknown; repondre: (v: VueTest) => void }> = []
  const pont: PontDeSession<VueTest> = {
    ouvrir: vi.fn(async () => ({ session: 's1', texte: 'ouvert' })),
    agir: vi.fn((_session: string, geste: string, valeur: unknown) =>
      new Promise<VueTest>((repondre) => { enAttente.push({ geste, valeur, repondre }) })),
    fermer: vi.fn(async () => undefined),
  }
  return { pont, enAttente }
}

describe('useSession', () => {
  it('ouvre une session au montage, la ferme au demontage, et n\'affiche rien avant la reponse', async () => {
    const { pont } = pontManuel()
    const { unmount } = render(<Sonde pont={pont} />)
    expect(screen.getByText('rien')).toBeTruthy()
    expect(await screen.findByText('ouvert')).toBeTruthy()
    expect(pont.ouvrir).toHaveBeenCalledTimes(1)

    unmount()
    expect(pont.fermer).toHaveBeenCalledWith('s1')
  })

  it('remonte chaque geste avec l\'identifiant de session, une valeur absente part en null', async () => {
    const { pont } = pontManuel()
    render(<Sonde pont={pont} />)
    await screen.findByText('ouvert')

    fireEvent.click(screen.getByText('sans valeur'))
    expect(pont.agir).toHaveBeenLastCalledWith('s1', 'Basculer', null)
    fireEvent.click(screen.getByText('a'))
    expect(pont.agir).toHaveBeenLastCalledWith('s1', 'Poser', 'a')
  })

  it('n\'applique que la reponse du DERNIER geste, meme si une plus ancienne arrive apres', async () => {
    const { pont, enAttente } = pontManuel()
    render(<Sonde pont={pont} />)
    await screen.findByText('ouvert')

    fireEvent.click(screen.getByText('a'))
    fireEvent.click(screen.getByText('ab'))
    await act(async () => { enAttente[1]!.repondre({ session: 's1', texte: 'vue de ab' }) })
    expect(screen.getByTestId('texte').textContent).toBe('vue de ab')

    await act(async () => { enAttente[0]!.repondre({ session: 's1', texte: 'vue de a' }) })
    expect(screen.getByTestId('texte').textContent).toBe('vue de ab')
  })

  it('ferme la session si l\'ecran est demonte avant la reponse d\'ouverture', async () => {
    let ouvrir: (v: VueTest) => void = () => {}
    const pont: PontDeSession<VueTest> = {
      ouvrir: () => new Promise<VueTest>((r) => { ouvrir = r }),
      agir: vi.fn(),
      fermer: vi.fn(async () => undefined),
    }
    const { unmount } = render(<Sonde pont={pont} />)
    unmount()
    await act(async () => { ouvrir({ session: 'tardive', texte: 'x' }) })
    expect(pont.fermer).toHaveBeenCalledWith('tardive')
  })
})

describe('Saisie', () => {
  it('« input » : remonte chaque frappe (le @bind:event="oninput" de Blazor)', () => {
    const changer = vi.fn()
    render(<Saisie aria-label="champ" valeur="" changer={changer} />)
    fireEvent.input(screen.getByLabelText('champ'), { target: { value: 'DUP' } })
    expect(changer).toHaveBeenCalledWith('DUP')
  })

  it('« change » : ne remonte qu\'a la sortie du champ (le @bind seul)', () => {
    const changer = vi.fn()
    render(<Saisie aria-label="champ" type="number" quand="change" valeur="500" changer={changer} />)
    const champ = screen.getByLabelText('champ') as HTMLInputElement
    fireEvent.input(champ, { target: { value: '120' } })
    expect(changer).not.toHaveBeenCalled()
    fireEvent.change(champ, { target: { value: '120' } })
    expect(changer).toHaveBeenCalledWith('120')
  })

  it('« change » : ecoute des que le champ est a l\'ecran, avant les effets differes de React', async () => {
    // Cause du test instable « volet Articles » (EcranRequisitions.test.tsx,
    // 19/11/2026). La vue d'une session arrive HORS act (reponse du pont) :
    // React la rend dans une tache de son ordonnanceur et, si cette tache a
    // depasse ses 5 ms (machine chargee), repousse les useEffect a une tache
    // suivante. Entre les deux, le champ est deja dans la page : un « change »
    // tire la (le test, ou une saisie) etait perdu si l'ecoute s'accrochait dans
    // un useEffect. Le rendu lent ci-dessous rend ce cas certain, sans charge.
    const changer = vi.fn()
    let montrer: () => void = () => {}
    function RenduLent() {
      const debut = performance.now()
      while (performance.now() - debut < 20) { /* depasse la tranche de 5 ms */ }
      return null
    }
    function Differe() {
      const [visible, setVisible] = useState(false)
      montrer = () => setVisible(true)
      return visible ? <><RenduLent /><Saisie aria-label="champ" quand="change" valeur="" changer={changer} /></> : null
    }
    const { container } = render(<Differe />)
    const changeTireAuRendu = new Promise<void>((fini) => {
      // Rappel d'un MutationObserver : juste apres l'ecriture du champ dans la
      // page, avant toute autre tache -- le moment ou findByText rend la main.
      const observateur = new MutationObserver(() => {
        const champ = container.querySelector('input')
        if (!champ) return
        observateur.disconnect()
        fireEvent.change(champ, { target: { value: '5' } })
        fini()
      })
      observateur.observe(container, { childList: true, subtree: true })
    })
    const avertir = vi.spyOn(console, 'error').mockImplementation(() => {}) // « not wrapped in act » : voulu
    try {
      montrer() // hors act, comme la reponse du pont dans useSession
      await changeTireAuRendu
    } finally { avertir.mockRestore() }
    expect(changer).toHaveBeenCalledWith('5')
  })

  it('suit la valeur de la session des que la vue est a l\'ecran, avant les effets differes de React', async () => {
    // Cause du test instable « Actualites » (EcranActualites.test.tsx:65,
    // 21/11/2026, 2 echecs sur 30) : meme course que le cas ci-dessus. La vue
    // qui vide le titre arrive hors act ; si la reecriture du champ vit dans un
    // useEffect repousse, le compte rendu est deja dans la page alors que le
    // champ montre encore l'ancien titre.
    let poser: (v: string) => void = () => {}
    function RenduLent() {
      const debut = performance.now()
      while (performance.now() - debut < 20) { /* depasse la tranche de 5 ms */ }
      return null
    }
    function Differe() {
      const [valeur, setValeur] = useState('INPI HS')
      poser = setValeur
      return <><RenduLent /><p data-testid="cr">{valeur === '' ? 'publiee' : ''}</p>
        <Saisie aria-label="champ" valeur={valeur} changer={() => {}} /></>
    }
    const { container } = render(<Differe />)
    const champ = screen.getByLabelText('champ') as HTMLInputElement
    const luAuRendu = new Promise<string>((fini) => {
      // Juste apres l'ecriture du compte rendu dans la page, avant toute autre
      // tache : le moment ou findByText rend la main au test.
      const observateur = new MutationObserver(() => {
        if (screen.getByTestId('cr').textContent !== 'publiee') return
        observateur.disconnect()
        fini(champ.value)
      })
      observateur.observe(container, { childList: true, subtree: true, characterData: true })
    })
    const avertir = vi.spyOn(console, 'error').mockImplementation(() => {}) // « not wrapped in act » : voulu
    try {
      poser('') // hors act, comme la reponse du pont dans useSession
      expect(await luAuRendu).toBe('')
    } finally { avertir.mockRestore() }
  })

  it('ne reecrit le champ que quand la valeur de la session CHANGE', () => {
    const { rerender } = render(<Saisie aria-label="champ" valeur="DUPONT" changer={() => {}} />)
    const champ = screen.getByLabelText('champ') as HTMLInputElement

    // Une saisie que la session refuse (elle garde « DUPONT ») reste affichee.
    champ.value = 'abc'
    rerender(<Saisie aria-label="champ" valeur="DUPONT" changer={() => {}} />)
    expect(champ.value).toBe('abc')

    // La session change sa valeur (Reinitialiser) : le champ suit.
    rerender(<Saisie aria-label="champ" valeur="" changer={() => {}} />)
    expect(champ.value).toBe('')
  })
})
