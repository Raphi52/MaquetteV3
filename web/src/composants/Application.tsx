// L'APPLICATION ENTIERE de RIG V3 : ce que Components/Pages/Home.razor rendait,
// porte en React au lot 2 du plan « finir le passage a React » (19/11/2026).
// Home.razor n'est plus qu'une COQUILLE : elle monte cette ile avec la vue que
// calcule SessionPlanTravail (Pont/PlanTravail.cs et PlanTravailVue.cs) et
// recoit les gestes par deux rappels :
//   geste -> l'accueil (SessionAccueil.Agir, lot 1 : barre, menu, tuiles) ;
//   plan  -> le plan de travail (SessionPlanTravail.Jouer : onglets, ecrans,
//            visionneuse, saisie guidee, modales).
// Les ecrans de src/ecrans/ sont montes DIRECTEMENT par l'aiguillage
// (src/plan/Aiguillage.tsx), sans plus repasser par leurs coquilles .razor.

import { useCallback, useEffect, useReducer, useRef } from 'react'
import { Accueil } from './Accueil'
import { BarreRig } from './BarreRig'
import { ThemeRig } from './ThemeRig'
import type { GesteIle } from '../accueil/types'
import { ecouterEchap } from '../plan/clavier'
import { EnvoiMail, Impression, MenuNoeud, Nouveau } from '../plan/Modales'
import { Palette } from './Palette'
import { Procedure } from '../plan/Procedure'
import type { GestePlan, ProcVue, VueApplication } from '../plan/types'
import { Visionneuse } from '../plan/Visionneuse'

/**
 * LES ECRANS DES ONGLETS OUVERTS RESTENT MONTES (19/11/2026, conv-2017) :
 * changer d'onglet ne fait que les cacher, comme un onglet de navigateur.
 * Avant, seul l'ecran montre etait rendu : revenir sur un onglet reconstruisait
 * son ecran a neuf (sa session C# fermee puis rouverte) et perdait ce qu'on y
 * avait ouvert, comme la commande choisie dans « Commandes web ».
 *
 * `gardes` tient, par code d'onglet, la derniere vue recue quand l'ecran etait
 * montre. Un ecran sort de `gardes`, donc est demonte (session fermee), quand
 * son onglet se ferme ou part dans une autre fenetre (qui monte alors le sien).
 * Idempotente pour une meme vue : elle peut tourner pendant le rendu.
 */
function garderEcrans(gardes: Map<string, ProcVue>, v: VueApplication): void {
  const ouverts = new Set((v.onglets ?? []).filter((o) => !o.detache).map((o) => o.code))
  if (v.proc) {
    ouverts.add(v.proc.code)
    gardes.set(v.proc.code, v.proc)
  }
  for (const code of [...gardes.keys()]) if (!ouverts.has(code)) gardes.delete(code)
}

/** Ce que React passe au rappel « plan » (GestePlan cote C#). */
export interface GestePlanIle {
  nom: string
  valeur: unknown
  numero: number
}

export interface ProprietesApplication {
  vue: VueApplication
  geste: (g: GesteIle) => unknown
  plan: (g: GestePlanIle) => unknown
}

export function Application({ vue, geste, plan }: ProprietesApplication) {
  // UNE VUE PERIMEE N'EST JAMAIS AFFICHEE. Chaque geste du plan de travail
  // part numerote ; la vue dit le dernier numero joue par le C#. Tant que la
  // reponse au DERNIER geste n'est pas arrivee, la page garde la vue qu'elle
  // montre : deux frappes rapides (« a » puis « ab ») ne font pas reapparaitre
  // « a » dans le champ, une frise ne revient pas en arriere entre deux clics.
  const envoye = useRef(0)
  const montree = useRef(vue)
  const [, redessiner] = useReducer((n: number) => n + 1, 0)
  if (vue.geste >= envoye.current) montree.current = vue
  const v = montree.current

  const planCourant = useRef(plan)
  useEffect(() => { planCourant.current = plan }, [plan])
  const gestePlan = useCallback<GestePlan>((nom, valeur) => {
    const numero = ++envoye.current
    Promise.resolve(planCourant.current({ nom, valeur: valeur ?? null, numero })).catch((erreur: unknown) => {
      // Le C# a retenu le numero avant d'echouer ; on cesse d'attendre sa vue.
      console.error('[plan] geste en echec', nom, erreur)
      envoye.current = 0
      redessiner()
    })
  }, [])

  // Echap ferme ce qui est ouvert, du plus interieur au plus exterieur.
  useEffect(() => ecouterEchap(() => gestePlan('echap')), [gestePlan])

  // Ctrl K ouvre la PALETTE depuis n'importe quel ecran (Palette.tsx), dans la
  // fenetre principale. Un ecran qui garde Ctrl K pour sa propre recherche
  // (Commandes web) l'a deja prise : il ecoute a la capture et appelle
  // preventDefault. Palette ouverte, c'est elle qui repond a la touche.
  const paletteOuverte = useRef(false)
  paletteOuverte.current = v.palette !== null
  const barre = v.barre
  useEffect(() => {
    if (!barre) return
    const surTouche = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== 'k' || e.defaultPrevented || paletteOuverte.current) return
      e.preventDefault()
      gestePlan('paletteOuvrir')
    }
    window.addEventListener('keydown', surTouche)
    return () => window.removeEventListener('keydown', surTouche)
  }, [barre, gestePlan])

  // Le dernier defilement de la zone des etapes deja joue : tenu ici, il
  // survit a la fermeture et a la reouverture des etapes.
  const defilementJoue = useRef(vue.defilement?.numero ?? 0)

  // Les ecrans des onglets ouverts (voir garderEcrans).
  const ecrans = useRef(new Map<string, ProcVue>())
  garderEcrans(ecrans.current, v)

  return (
    <div data-rig-theme={v.theme}
         style={{ background: 'var(--rig-fond)', color: 'var(--rig-texte)', height: '100vh', overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
      <div className="rig-filet-marque"></div>
      <ThemeRig theme={v.theme} />
      {/* LA BARRE DU HAUT, « comme Brave » : les onglets (glisser pour
          reordonner, hors de la fenetre pour sortir l'onglet sur un autre
          ecran), puis les boutons de la fenetre. */}
      {v.barre && <BarreRig onglets={v.onglets} finsDeGlisse={v.finsDeGlisse} accueil={!v.proc && !v.dossier}
                            geste={geste} plan={gestePlan} />}
      {/* Ouverte depuis la barre de l'accueil, la palette est la liste deroulante
          de cette barre : c'est l'accueil qui la dessine. */}
      {v.accueil && <Accueil vue={v.accueil} geste={geste} palette={v.palette?.ancree ? v.palette : null} plan={gestePlan} />}
      {/* ECRANS DE PROCEDURE : le cadre est commun, l'ecran vient de l'aiguillage.
          Ceux des onglets ouverts restent montes, caches hors de leur onglet. */}
      {[...ecrans.current.values()].map((p) => (
        <Procedure key={p.code} proc={p} plan={gestePlan} cachee={p.code !== v.proc?.code} />
      ))}
      {/* VISIONNEUSE DOCUMENTAIRE (esprit VGED) */}
      {v.dossier && (
        <Visionneuse dossier={v.dossier} pagination={v.pagination} defilement={v.defilement}
                     defilementJoue={defilementJoue} plan={gestePlan} />
      )}
      {v.impression && <Impression vue={v.impression} plan={gestePlan} />}
      {v.envoiMail && <EnvoiMail vue={v.envoiMail} plan={gestePlan} />}
      {v.nouveau && <Nouveau vue={v.nouveau} plan={gestePlan} />}
      {v.menuNoeud && <MenuNoeud vue={v.menuNoeud} plan={gestePlan} />}
      {/* LA PALETTE Ctrl K, par-dessus tout le reste (sauf accrochee a la barre de l'accueil). */}
      {v.palette && !(v.palette.ancree && v.accueil) && <Palette vue={v.palette} plan={gestePlan} />}
    </div>
  )
}
