// L'ECRAN REUNI EN ONGLETS (25/11/2026, conv-2147). RIG v2 avait un ecran par procedure ;
// le menu en reunit plusieurs d'un meme metier sous une seule entree (Pont/MenuAccueil.cs,
// EntreeMenu.Reunit). Les ecrans generiques se fondent dans une liste commune (EcranGenere) ;
// un ecran fait a la main a sa propre logique, il garde donc SON ecran, dans un onglet
// (EntreeMenu.EnOnglets). Cet ecran ne connait aucune procedure : il pose la barre des
// onglets et monte, pour chacun, l'ecran que l'aiguillage lui donne -- celui d'origine, tel
// quel, sans copie. Sans origine RIG v2 : l'ancien RIG ouvrait chaque procedure dans sa propre
// fenetre ; chaque onglet, lui, garde la procedure dont son ecran est porte.
//
// L'onglet montre vient du C# (« Fiche client » cherchee ouvre l'ecran reuni sur la fiche,
// Pont/PlanTravail.cs, EcranQuiReunit) ; un clic le lui renvoie (geste « ongletReuni »), qui
// le retient et redessine. Un onglet deja montre reste monte, cache : son ecran garde ce
// qu'on y a saisi, comme les onglets du haut (plan/visibilite.ts) ; ses ecoutes du clavier se
// taisent tant qu'il est cache. Un onglet jamais ouvert n'est pas monte.

import { useContext, useState, type KeyboardEvent, type ReactNode } from 'react'
import { EcranVisible } from '../plan/visibilite'

export interface OngletEcran {
  code: string
  libelle: string
}

export interface ProprietesEcranOnglets {
  /** Le nom de l'ecran reuni (son entree au menu) : il nomme la barre des onglets. */
  titre: string
  onglets: OngletEcran[]
  /** Le code de l'onglet montre ; inconnu, le premier est montre. */
  actif: string
  /** Un autre onglet est choisi. */
  choisir: (code: string) => void
  /** L'ecran d'origine d'un onglet. */
  ecran: (code: string) => ReactNode
}

export function EcranOnglets({ titre, onglets, actif, choisir, ecran }: ProprietesEcranOnglets) {
  const visible = useContext(EcranVisible)
  const montre = onglets.some((o) => o.code === actif) ? actif : onglets[0]?.code ?? ''
  const [montes, setMontes] = useState<string[]>([montre])
  if (!montes.includes(montre)) setMontes([...montes, montre])

  // Fleches gauche et droite d'un onglet a l'autre, quand la barre a le focus (motif ARIA des onglets).
  const surTouche = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return
    const i = onglets.findIndex((o) => o.code === montre)
    const suivant = onglets[(i + (e.key === 'ArrowRight' ? 1 : onglets.length - 1)) % onglets.length]
    if (!suivant) return
    e.preventDefault()
    choisir(suivant.code)
  }

  return (
    <div className="rig-reunis">
      <div className="rig-reunis__onglets" role="tablist" aria-label={titre} onKeyDown={surTouche}>
        {onglets.map((o) => (
          <button key={o.code} type="button" role="tab" aria-selected={o.code === montre}
                  tabIndex={o.code === montre ? 0 : -1}
                  className={'rig-reunis__onglet' + (o.code === montre ? ' rig-reunis__onglet--actif' : '')}
                  onClick={() => { if (o.code !== montre) choisir(o.code) }}>
            {o.libelle}
          </button>
        ))}
      </div>
      {onglets.filter((o) => montes.includes(o.code)).map((o) => (
        <div key={o.code} className="rig-reunis__panneau" role="tabpanel" aria-label={o.libelle}
             hidden={o.code !== montre}>
          <EcranVisible.Provider value={visible && o.code === montre}>
            {ecran(o.code)}
          </EcranVisible.Provider>
        </div>
      ))}
    </div>
  )
}
