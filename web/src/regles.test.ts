// Regles de structure de web/src, verifiees sur les SOURCES (lues brutes par
// Vite). Elles tiennent ce que web/README.md promet aux lots suivants.

import { describe, expect, it } from 'vitest'

const sources = import.meta.glob<string>(['./**/*.ts', './**/*.tsx', '!./**/*.test.ts', '!./**/*.test.tsx', '!./test/**'], {
  query: '?raw',
  import: 'default',
  eager: true,
})

describe('regles de web/src', () => {
  it('lit bien les sources (garde-fou contre un glob vide)', () => {
    expect(Object.keys(sources)).toContain('./pont.ts')
    expect(Object.keys(sources).length).toBeGreaterThanOrEqual(6)
  })

  it('pont.ts est le SEUL fichier qui parle au C# (chrome.webview ; ni DotNet ni invokeMethodAsync, retires avec Blazor)', () => {
    const fautifs = Object.entries(sources)
      .filter(([chemin]) => chemin !== './pont.ts')
      .filter(([, texte]) => /\bchrome\??\.webview\b|\bpostMessage\b|\bDotNet\b|invokeMethodAsync/.test(texte))
      .map(([chemin]) => chemin)
    expect(fautifs).toEqual([])
  })

  it('pont.ts reste generique : aucun nom d\'ecran dedans', () => {
    expect(sources['./pont.ts']).not.toMatch(/\bEcran[A-Z]\w*/)
  })

  it('chaque ecran exporte un composant nomme comme son fichier (registre de iles.ts)', () => {
    for (const [chemin, texte] of Object.entries(sources)) {
      const m = /^\.\/(?:ecrans|composants)\/(\w+)\.tsx$/.exec(chemin)
      if (!m) continue
      expect(texte, chemin).toMatch(new RegExp(`export function ${m[1]}\\b`))
    }
  })

  it('chaque appel au pont vise un identifiant <NomEcran>.<Methode> de SON ecran', () => {
    for (const [chemin, texte] of Object.entries(sources)) {
      const m = /^\.\/ecrans\/(\w+)\.tsx$/.exec(chemin)
      if (!m) continue
      for (const appel of texte.matchAll(/appeler<[^>]*>\(\s*'([^']+)'/g)) {
        expect(appel[1], chemin).toMatch(new RegExp(`^${m[1]}\\.\\w+$`))
      }
    }
  })
})
