// L'EXTRAIT KBIS d'un VRAI dossier, dans l'ecran d'une operation qui le nomme (descripteur.apercuKbis : PROC_XEX, edition
// interne d'un Kbis ; plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026). Le pont (Pont/ApercuKbisDossier.cs) lit la fiche
// du dossier dans la base du greffe et la met en extrait par Pont/ApercuKbis.cs ; l'ecran la dessine avec le MEME composant que
// l'apercu du plan de travail (composants/ApercuKbis.tsx). Une ligne d'exemple, sans serveur, ou une panne : le motif, jamais un
// extrait invente.

import { useEffect, useState } from 'react'
import { appeler } from '../../pont'
import { ApercuKbis, type VueKbis } from '../../composants/ApercuKbis'
import type { DescripteurEcranVue } from '../../plan/types'
import type { LigneTenue } from './commun'

/** Ce que le pont rend (Pont/ApercuKbisDossier.cs, VueApercuKbisDossier). */
export interface VueApercuKbisDossier { mode: 'base' | 'exemple' | 'erreur'; message: string | null; kbis: VueKbis | null }

export function ApercuKbisLu({ d, objet }: { d: DescripteurEcranVue; objet: LigneTenue }) {
  const [vue, poserVue] = useState<VueApercuKbisDossier | null>(null)
  const cle = `${objet.groupe}\u0000${objet.id ?? ''}`
  useEffect(() => {
    poserVue(null)
    let vivant = true
    appeler<VueApercuKbisDossier>('ApercuKbis.Dossier', d.code, objet.groupe, objet.id ?? null).then(
      (v) => { if (vivant) poserVue(v) },
      () => { if (vivant) poserVue({ mode: 'erreur', message: 'L’extrait n’a pas pu être lu dans la base du greffe.', kbis: null }) })
    return () => { vivant = false }
    // La ligne : sa cle suffit (une relecture apres le geste rend un objet egal).
  }, [d.code, cle])

  if (!vue) return null
  return (
    <section className="rig-eg-recap" aria-label={d.apercuKbis ?? 'Extrait Kbis'}>
      {vue.kbis ? <ApercuKbis vue={vue.kbis} /> : vue.message && <p className="rig-eg-note" role="note">{vue.message}</p>}
    </section>
  )
}
