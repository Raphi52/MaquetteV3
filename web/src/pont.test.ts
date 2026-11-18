import { afterEach, describe, expect, it, vi } from 'vitest'
import { appeler, rappelBlazor, ASSEMBLAGE } from './pont'
import { installerFauxDotNet, retirerFauxDotNet } from './test/faux-dotnet'

afterEach(retirerFauxDotNet)

describe('pont.appeler', () => {
  it('joint la methode [JSInvokable] de l\'assemblage RigV3Desktop avec ses arguments', async () => {
    const dotnet = installerFauxDotNet({ 'Ecran.Methode': (a, b) => `${String(a)}|${String(b)}` })
    await expect(appeler<string>('Ecran.Methode', 'x', true)).resolves.toBe('x|true')
    expect(dotnet.invokeMethodAsync).toHaveBeenCalledWith(ASSEMBLAGE, 'Ecran.Methode', 'x', true)
    expect(ASSEMBLAGE).toBe('RigV3Desktop')
  })

  it('rejette avec un message clair hors de la fenetre RIG (DotNet absent)', async () => {
    await expect(appeler('Ecran.Methode')).rejects.toThrow(/DotNet est absent/)
  })

  it('laisse remonter l\'erreur du C#', async () => {
    installerFauxDotNet({ 'Ecran.Casse': () => { throw new Error('boum C#') } })
    await expect(appeler('Ecran.Casse')).rejects.toThrow('boum C#')
  })
})

describe('pont.rappelBlazor', () => {
  it('remonte le geste par IleReact.Rappeler(nom, valeur)', async () => {
    const reference = { invokeMethodAsync: vi.fn(async () => undefined) }
    await rappelBlazor(reference, 'ouvrirDossier')('004151')
    expect(reference.invokeMethodAsync).toHaveBeenCalledWith('Rappeler', 'ouvrirDossier', '004151')
  })

  it('envoie null, jamais undefined, pour un geste sans valeur', async () => {
    const reference = { invokeMethodAsync: vi.fn(async () => undefined) }
    await rappelBlazor(reference, 'fermer')()
    expect(reference.invokeMethodAsync).toHaveBeenCalledWith('Rappeler', 'fermer', null)
  })
})
