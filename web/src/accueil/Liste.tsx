// Une liste deroulante dont la valeur vit dans la session C# (SessionAccueil),
// avec la regle de Blazor : la liste n'est repositionnee QUE quand la valeur de
// la session CHANGE. Un <select value> controle par React reviendrait sur
// l'ancienne valeur entre le choix et la reponse du C# (un aller-retour), et
// le theme choisi clignoterait ; ici le choix reste affiche tel quel.
// Meme parti que Saisie (src/session.tsx) pour un champ de texte.

import { useEffect, useRef, type ReactNode, type SelectHTMLAttributes } from 'react'

type AttributsListe = Omit<SelectHTMLAttributes<HTMLSelectElement>, 'value' | 'defaultValue' | 'onChange'>

export interface ProprietesListe extends AttributsListe {
  valeur: string
  changer: (valeur: string) => void
  children: ReactNode
}

export function Liste({ valeur, changer, children, ...attributs }: ProprietesListe) {
  const liste = useRef<HTMLSelectElement>(null)

  // Apres chaque rendu des options : une option ajoutee apres coup (la liste
  // des agents) doit pouvoir porter la valeur, comme Blazor la reposait.
  useEffect(() => {
    const el = liste.current
    if (el && el.value !== valeur) el.value = valeur
  }, [valeur])

  return (
    <select ref={liste} defaultValue={valeur} onChange={(e) => changer(e.currentTarget.value)} {...attributs}>
      {children}
    </select>
  )
}
