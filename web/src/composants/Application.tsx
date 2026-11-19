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
import { Procedure } from '../plan/Procedure'
import type { GestePlan, VueApplication } from '../plan/types'
import { Visionneuse } from '../plan/Visionneuse'

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

  // Le dernier defilement de la zone des etapes deja joue : tenu ici, il
  // survit a la fermeture et a la reouverture des etapes.
  const defilementJoue = useRef(vue.defilement?.numero ?? 0)

  return (
    <div data-rig-theme={v.theme}
         style={{ background: 'var(--rig-fond)', color: 'var(--rig-texte)', height: '100vh', overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
      <div className="rig-filet-marque"></div>
      <ThemeRig theme={v.theme} />
      {/* LA BARRE DU HAUT, « comme Brave » : les onglets (glisser pour
          reordonner, hors de la fenetre pour sortir l'onglet sur un autre
          ecran), puis les boutons de la fenetre. */}
      {v.barre && <BarreRig onglets={v.onglets} finsDeGlisse={v.finsDeGlisse} geste={geste} plan={gestePlan} />}
      {v.accueil && <Accueil vue={v.accueil} geste={geste} />}
      {/* ECRAN DE PROCEDURE : le cadre est commun, l'ecran vient de l'aiguillage. */}
      {v.proc && <Procedure proc={v.proc} plan={gestePlan} />}
      {/* VISIONNEUSE DOCUMENTAIRE (esprit VGED) */}
      {v.dossier && (
        <Visionneuse dossier={v.dossier} pagination={v.pagination} defilement={v.defilement}
                     defilementJoue={defilementJoue} plan={gestePlan} />
      )}
      {v.impression && <Impression vue={v.impression} plan={gestePlan} />}
      {v.envoiMail && <EnvoiMail vue={v.envoiMail} plan={gestePlan} />}
      {v.nouveau && <Nouveau vue={v.nouveau} plan={gestePlan} />}
      {v.menuNoeud && <MenuNoeud vue={v.menuNoeud} plan={gestePlan} />}
    </div>
  )
}
