// FORME « FICHE » : corriger un objet existant (une affaire, un dossier, une creance). On le RETROUVE (criteres et
// resultats), puis on le corrige dans un formulaire par sections aux vrais champs (texte, date, liste, case), et on
// Enregistre ou on Annule. Un champ laisse vide garde la valeur de la base : une section obligatoire ne manque donc
// jamais ici. La regle de la creation (Saisie, « au moins un champ rempli ») n'y vaut pas : appliquee a une fiche, elle
// redemandait l'objet deja ouvert (« Corriger une affaire » : « Instance est obligatoire », controle final du lot 1,
// 25/11/2026). Une ligne figee (verrouillee par un autre utilisateur...) s'ouvre en lecture seule.
// Ecran reuni : chaque ligne s'ouvre sur les sections de SA procedure.
//
// BRANCHEE SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 2, 26/11/2026) : une ligne de la base ouvre SA fiche lue
// (useFiche) -- chaque champ porte sa valeur, seuls ceux que l'ecrivain sait enregistrer se saisissent ; un objet que RIG
// v2 tient verrouille s'ouvre en lecture seule avec le message de RIG v2. Enregistrer n'envoie que les champs CHANGES
// (useGeste) ; ecrit, la fiche et la liste sont relues dans la base ; refuse par un controle de RIG v2, son message, et
// la saisie reste a l'ecran. Une ligne d'exemple garde la demonstration.

import { useState } from 'react'
import { EnteteListe, ListeModerne, PiedListe, PointEtat } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import { Resultats } from './Resultats'
import {
  Annonce, Sections, champLu, cleChamp, etatDe, figee, parcoursDe, refus, teinteEtat, useAnnonce, useFiche,
  useGeste, useLignes, type Saisies,
} from './commun'

export function FormeFiche({ d }: { d: DescripteurEcranVue }) {
  const lignesLues = useLignes(d)
  const { vue, lignes, relire } = lignesLues
  const [ouverte, poserOuverte] = useState<number | null>(null)
  const [saisies, poserSaisies] = useState<Saisies>({})
  const [annonce, annoncer] = useAnnonce()
  const jouer = useGeste(d, vue, annoncer)
  // La ligne ouverte se retrouve par son objet apres une relecture, qui renumerote les lignes : sa procedure (groupe) et son
  // id -- deux procedures d'un ecran reuni peuvent porter le meme id.
  const [objet, poserObjet] = useState<{ groupe: string; id: string } | null>(null)
  const sel = ouverte === null ? undefined
    : (objet !== null ? lignes.find((l) => l.groupe === objet.groupe && l.id === objet.id) : undefined) ?? lignes.find((l) => l.i === ouverte)
  const { fiche: vueFiche, relire: relireFiche } = useFiche(d, sel, vue)
  const fiche = vueFiche?.fiche ?? null

  if (!sel) {
    return <Resultats d={d} lignes={lignesLues} forme="Fiche" invite="Choisissez une ligne pour ouvrir sa fiche."
                      ouvrir={(l) => { poserOuverte(l.i); poserObjet(l.id ? { groupe: l.groupe, id: l.id } : null); poserSaisies({}); annoncer(null) }} />
  }

  const p = parcoursDe(d, sel.groupe)
  const etat = etatDe(d, sel)
  // fix-ok: mesure sur le bureau cache (26/11/2026, instance 2026OP01662 de la base de dev) -- apres un Enregistrer, la
  // ligne relue passe « Modifiée » (Validee), que figee() fige : la fiche se rouvrait en lecture seule, alors que RIG v2
  // remodifie une instance autant de fois qu'il faut (une demande MJUD par fois). Fiche LUE : la base decide (verrou de
  // RIG v2 = LectureSeule) ; sans fiche (ligne d'exemple), l'etat de la ligne fige comme avant.
  const lecture = fiche ? fiche.lectureSeule : figee(sel.etat)
  const fermer = () => { poserOuverte(null); poserObjet(null); poserSaisies({}); annoncer(null) }
  // Les champs CHANGES : une saisie qui differe de la valeur lue (sans fiche lue : tout champ rempli).
  const changes = Object.fromEntries(p.sections.flatMap((s) => s.champs.map((c) => {
    const cle = cleChamp(s.titre, c.libelle)
    const v = saisies[cle]
    if (v === undefined) return null
    if (fiche) return champLu(fiche, s.titre, c.libelle)?.modifiable && v !== champLu(fiche, s.titre, c.libelle)!.valeur ? [cle, v] as const : null
    return v.trim() !== '' && c.type !== 'Tableau' ? [cle, v] as const : null
  }).filter((x): x is readonly [string, string] => x !== null)))
  const enregistrer = () => {
    // Sans fiche lue, « change » = rempli : la meme regle que sectionFaite (la demonstration d'avant le lot 2).
    if (Object.keys(changes).length === 0) {
      annoncer(fiche
        ? `Aucun champ n’est modifié : « ${sel.nom} » garde les valeurs de la base.`
        : refus('Enregistrer', `: aucun champ n’est modifié, « ${sel.nom} » garde les valeurs de la base`))
      return
    }
    void jouer({ type: 'Enregistrer', groupe: sel.groupe, ids: [sel.id], valeurs: changes },
      refus('Enregistrer', `: « ${sel.nom} » n’est pas modifiée dans la base, la fiche reste ouverte`),
      () => { poserSaisies({}); relireFiche(); relire() })
  }
  return (
    <div className="rig-proc__corps rig-eg" data-forme="Fiche">
      <ListeModerne etiquette={`${p.titre} — ${sel.nom}`}>
        <EnteteListe titre={sel.nom} synthese={<>{d.membres.length > 0 ? `${p.titre} · ` : ''}{sel.precisions}</>} />
        <div className="rig-lm-outils">
          <button type="button" className="rig-lm-outil" onClick={fermer}>← Résultats</button>
          <PointEtat teinte={teinteEtat(etat)}>{etat?.libelle ?? sel.etat}</PointEtat>
        </div>
        {fiche?.lectureSeule && fiche.motif
          ? <p className="rig-eg-note" role="note">Fiche en lecture seule : {fiche.motif}.</p>
          : lecture && !fiche && <p className="rig-eg-note" role="note">Fiche figée ({etat?.libelle ?? sel.etat}) : elle s’ouvre en lecture seule.</p>}
        {vueFiche && vueFiche.mode !== 'base' && vueFiche.message && <p className="rig-eg-note" role="note">{vueFiche.message}</p>}
        {fiche && <p className="rig-eg-note" role="note">Fiche lue dans la base du greffe{vue?.greffe ? ` ${vue.greffe}` : ''}.</p>}
        <Sections sections={p.sections} saisies={saisies} lecture={lecture} fiche={fiche}
                  changer={(cle, v) => poserSaisies((avant) => ({ ...avant, [cle]: v }))} />
        <Annonce texte={annonce} />
        <PiedListe aide={lecture ? undefined : fiche ? 'Seuls les champs modifiés sont enregistrés.' : 'Les champs vides gardent la valeur de la base.'}>
          <button type="button" className="rig-lm-outil rig-lm-outil--fort" disabled={lecture} onClick={enregistrer}>Enregistrer</button>
          <button type="button" className="rig-lm-outil" onClick={fermer}>Annuler</button>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
