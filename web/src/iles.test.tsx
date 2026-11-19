import { describe, expect, it } from 'vitest'
import { composantMontable, composantsMontables } from './iles'

// Les noms ATTENDUS se lisent sur le disque (les fichiers que le registre doit
// rendre montables), pas dans une liste ecrite ici : chaque lot qui porte un
// ecran n'a rien a ajouter a ce test, et deux lots en parallele ne se disputent
// plus cette ligne (lot 5, 18/11/2026 : la liste figee du lot 1 cassait des
// le premier ecran ajoute). Le montage lui-meme est teste dans racine.test.tsx.
const fichiersPortes = Object.keys(import.meta.glob(['./ecrans/*.tsx', './composants/*.tsx', '!./**/*.test.tsx']))
  .map((chemin) => chemin.slice(chemin.lastIndexOf('/') + 1).replace(/\.tsx$/, ''))
  .sort()

describe('iles (registre des composants montables a la racine)', () => {
  it('chaque ecran et composant porte est montable sous le nom de son fichier', () => {
    expect(fichiersPortes).toEqual(expect.arrayContaining(['Application', 'DocumentDetacheVue', 'EcranFactures', 'EcranVerrous', 'EnteteTriable']))
    expect(composantsMontables()).toEqual(fichiersPortes)
  })

  it('rend le composant exporte par le fichier de ce nom', () => {
    expect(composantMontable('EcranVerrous').name).toBe('EcranVerrous')
    expect(composantMontable('Application').name).toBe('Application')
  })

  it('un nom inconnu echoue en listant les noms montables', () => {
    expect(() => composantMontable('EcranInconnu')).toThrow(/EcranInconnu.*EcranFactures.*EcranVerrous/)
  })
})
