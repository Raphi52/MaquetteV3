// BANDES DE BORD de la fenetre principale (18/11/2026 ; portees de Main.razor
// en React au retrait de Blazor, 19/11/2026). Le WebView couvre toute la
// fenetre, sans marge (MainWindow.xaml), et avale donc le pointeur sur les
// bords. Ces bandes invisibles de 5 px (rig-charte.css, .rig-bord--*) rendent
// le redimensionnement a Windows : un appui part au C#
// (RacinePlanTravail.Rappeler « bord » -> FenetreInterop.Redimensionner), qui
// laisse Windows mener le geste, aimantation comprise. La racine ne les pose
// que dans la fenetre principale non agrandie (RacinePlanTravail.Bords).

/** Les 8 bords et coins : le nom de BordFenetre (FenetreInterop.cs) et sa classe. */
export const BORDS: readonly (readonly [bord: string, classe: string])[] = [
  ['Haut', 'h'], ['Bas', 'b'],
  ['Gauche', 'g'], ['Droite', 'd'],
  ['HautGauche', 'hg'], ['HautDroite', 'hd'],
  ['BasGauche', 'bg'], ['BasDroite', 'bd'],
]

export interface ProprietesBords {
  redimensionner: (bord: string) => unknown
}

export function Bords({ redimensionner }: ProprietesBords) {
  return (
    <>
      {BORDS.map(([bord, classe]) => (
        <div key={bord} className={'rig-bord rig-bord--' + classe} aria-hidden="true"
             onMouseDown={() => { void redimensionner(bord) }}></div>
      ))}
    </>
  )
}
