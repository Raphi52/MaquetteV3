// FORME « AUDIENCE » : composition, preparation, convocations, retour d'audience, deliberes. On CHOISIT l'audience
// (ses parametres de la fiche RIG v2 : la date, la chambre ou le cabinet), on voit les affaires au role, et on joue le
// geste de la fiche (decision, renvoi, convocation...) sur les affaires cochees, ou sur une affaire par son menu. Le geste
// va a l'ecrivain de la procedure (type Decider, son nom, les parametres de l'audience) ; ecrit, le role est relu (plan
// « RIG v3 100 % fonctionnel », lot 2). Sans ecrivain, il ne change rien et le dit.
// LA BASE (lot 2) : quand le lecteur sait chercher, le texte et les parametres de l'audience (au libelle du descripteur)
// choisissent le role DANS la base (useRecherche) ; une affaire se deplie sur SA fiche lue, en lecture (FicheLue).

import { useState } from 'react'
import { EnteteListe, ListeModerne, PiedListe } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import {
  Annonce, ChampSaisie, FicheLue, NoteRecherche, Provenance, TableLignes, figee, gestesDe, refus, synthese,
  useAnnonce, useGesteEnLot, useLignes, useRecherche, type LigneTenue, type Saisies,
} from './commun'

export function FormeAudience({ d }: { d: DescripteurEcranVue }) {
  const lues = useLignes(d)
  const { vue, lignes, relire } = lues
  const [audience, poserAudience] = useState<Saisies>({})
  const [texte, poserTexte] = useState('')
  const [cochees, poserCochees] = useState<ReadonlySet<number>>(() => new Set())
  const [annonce, annoncer] = useAnnonce()

  // Les parametres choisissent le role dans la base ; a l'ecran, seul le texte filtre (une date ne se lit pas dans une ligne).
  const { visibles, enBase, filtre } = useRecherche(lues, texte, audience, false)
  const choisies = lignes.filter((l) => cochees.has(l.i))
  const choix = d.parametres.map((p) => audience[p.libelle] ?? '').filter((v) => v.trim() !== '')
  const enLot = useGesteEnLot(d, vue, annoncer)
  const jouer = (geste: string, visees: LigneTenue[]) => void enLot('Decider', geste, visees, audience,
    (n) => refus(geste, `sur ${n} affaire${n > 1 ? 's' : ''} : le rôle n’est pas modifié`),
    () => { poserCochees(new Set()); relire() })
  const basculer = (i: number) => poserCochees((avant) => {
    const apres = new Set(avant)
    if (!apres.delete(i)) apres.add(i)
    return apres
  })

  return (
    <div className="rig-proc__corps rig-eg" data-forme="Audience">
      <aside className="rig-criteres" aria-label={`Audience — ${d.titre}`}>
        <div className="rig-kicker">Audience</div>
        {d.parametres.map((p) => (
          <ChampSaisie key={p.libelle} champ={p} valeur={audience[p.libelle] ?? ''}
                       changer={(v) => poserAudience((avant) => ({ ...avant, [p.libelle]: v }))} />
        ))}
        <div className="rig-criteres__actions">
          <button type="button" className="rig-bouton rig-bouton--plat" onClick={() => poserAudience({})}>Effacer</button>
        </div>
      </aside>
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre}
                     synthese={<>{synthese(d, vue, lignes)}{choix.length > 0 ? ` · audience ${choix.join(' · ')}` : ' · choisissez l’audience'}</>}
                     filtre={{ valeur: texte, changer: poserTexte, indication: d.recherche }} />
        {vue && <Provenance vue={vue} />}
        <NoteRecherche enBase={enBase} />
        <div className="rig-lm-outils rig-eg-lot" role="toolbar" aria-label="Gestes sur les affaires cochées">
          <span className="rig-eg-lot__compte"><b>{choisies.length}</b> cochée{choisies.length > 1 ? 's' : ''}</span>
          {gestesDe(d).map((g, k) => (
            <button key={g} type="button" className={`rig-lm-outil${k === 0 ? ' rig-lm-outil--fort' : ''}`}
                    disabled={choisies.length === 0} onClick={() => jouer(g, choisies)}>{g}</button>
          ))}
        </div>
        <Annonce texte={annonce} />
        <TableLignes d={d} vue={vue} lignes={visibles} total={lignes.length} filtre={filtre}
                     detail={(l) => <FicheLue d={d} l={l} vue={vue} />}
                     cases={{ cochees, basculer, toutes: (cocher) => poserCochees(cocher ? new Set(visibles.filter((l) => !figee(l.etat)).map((l) => l.i)) : new Set()) }}
                     actions={(l) => gestesDe(d).map((g) => ({ libelle: g, desactive: figee(l.etat), agir: () => jouer(g, [l]) }))} />
        <PiedListe aide="Cochez des affaires, puis choisissez le geste.">
          <span><b>{visibles.length}</b> au rôle</span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
