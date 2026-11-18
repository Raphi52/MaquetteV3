// Ecran des procedures du menu qui n'ont PAS d'ecran porte.
// Avant, elles retombaient sur l'ecran des Demandes : en demonstration,
// cela laissait croire que « Cloture » ou « Factures » affichaient les
// demandes. Un ecran qui se nomme est plus honnete qu'un ecran qui ment.
//
// Depuis le portage de septembre 2026, toutes les procedures du menu ont
// leur ecran SAUF celles dont RIG v2 ne contient rien a porter : le motif
// est alors affiche tel quel, avec le constat fait dans les sources v2.
//
// Porte depuis Components/Ecrans/EcranNonMaquette.razor (lot 5) : meme
// balisage, memes classes CSS. Le motif est ecrit par Home.razor
// (MotifNonMaquette) et arrive en propriete, comme le code et le libelle.

export interface ProprietesEcranNonMaquette {
  code: string
  libelle: string
  /** Raison precise, quand elle est connue, de l'absence d'ecran. */
  motif?: string | null
}

export function EcranNonMaquette({ libelle, motif }: ProprietesEcranNonMaquette) {
  return (
    <div className="rig-nonmaq">
      <div className="rig-nonmaq__kicker">Ecran a venir</div>
      <div className="rig-nonmaq__titre">{libelle}</div>

      {motif && motif.trim().length > 0
        ? <p className="rig-nonmaq__texte">{motif}</p>
        : (
          <p className="rig-nonmaq__texte">
            Cet ecran n'est pas encore disponible.
          </p>
        )}
    </div>
  )
}
