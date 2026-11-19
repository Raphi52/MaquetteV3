import { afterEach, describe, expect, it } from 'vitest'
import { appeler, ecouter, rappel } from './pont'
import { installerFauxPont, retirerFauxPont } from './test/faux-pont'

afterEach(retirerFauxPont)

describe('pont.appeler', () => {
  it('joint la methode [JSInvokable] par la messagerie WebView2, avec ses arguments', async () => {
    const pont = installerFauxPont({ 'Ecran.Methode': (a, b) => `${String(a)}|${String(b)}` })
    await expect(appeler<string>('Ecran.Methode', 'x', true)).resolves.toBe('x|true')
    expect(pont.appels).toHaveBeenCalledWith('Ecran.Methode', 'x', true)
  })

  it('poste { appel, nom, args } et ne confond pas deux reponses arrivees dans le desordre', async () => {
    const postes: unknown[] = []
    const ecouteurs = new Set<(e: { data: unknown }) => void>()
    ;(globalThis as { chrome?: unknown }).chrome = {
      webview: {
        postMessage: (m: unknown) => { postes.push(m) },
        addEventListener: (_t: string, e: (e: { data: unknown }) => void) => { ecouteurs.add(e) },
        removeEventListener: (_t: string, e: (e: { data: unknown }) => void) => { ecouteurs.delete(e) },
      },
    }
    const premier = appeler<string>('Ecran.Lent', undefined)
    const second = appeler<string>('Ecran.Rapide')
    const [a, b] = postes as { appel: number; nom: string; args: unknown[] }[]
    expect(a).toMatchObject({ nom: 'Ecran.Lent', args: [null] })
    expect(b).toMatchObject({ nom: 'Ecran.Rapide', args: [] })
    expect(a!.appel).not.toBe(b!.appel)
    for (const e of ecouteurs) e({ data: { reponse: b!.appel, valeur: 'rapide' } })
    for (const e of ecouteurs) e({ data: { reponse: a!.appel, valeur: 'lent' } })
    await expect(second).resolves.toBe('rapide')
    await expect(premier).resolves.toBe('lent')
  })

  it('rejette avec un message clair hors de la fenetre RIG (chrome.webview absent)', async () => {
    await expect(appeler('Ecran.Methode')).rejects.toThrow(/chrome\.webview est absent/)
  })

  it('laisse remonter l\'erreur du C#, avec le nom de la methode', async () => {
    installerFauxPont({ 'Ecran.Casse': () => { throw new Error('boum C#') } })
    await expect(appeler('Ecran.Casse')).rejects.toThrow('Ecran.Casse : boum C#')
  })
})

describe('pont.ecouter', () => {
  it('recoit ce que le C# envoie sans demande, jusqu\'a ce qu\'on cesse d\'ecouter', () => {
    const pont = installerFauxPont({})
    const recus: unknown[] = []
    const cesser = ecouter('racine', (v) => recus.push(v))
    pont.pousser('racine', { a: 1 })
    pont.pousser('autre', { b: 2 })
    cesser()
    pont.pousser('racine', { a: 3 })
    expect(recus).toEqual([{ a: 1 }])
  })
})

describe('pont.rappel', () => {
  it('remonte le geste a la racine de la fenetre (Racine.Rappeler(nom, valeur))', async () => {
    const pont = installerFauxPont({ 'Racine.Rappeler': () => null })
    await rappel('ouvrirDossier')('004151')
    expect(pont.appels).toHaveBeenCalledWith('Racine.Rappeler', 'ouvrirDossier', '004151')
  })

  it('envoie null, jamais undefined, pour un geste sans valeur', async () => {
    const pont = installerFauxPont({ 'Racine.Rappeler': () => null })
    await rappel('fermer')()
    expect(pont.appels).toHaveBeenCalledWith('Racine.Rappeler', 'fermer', null)
  })
})
