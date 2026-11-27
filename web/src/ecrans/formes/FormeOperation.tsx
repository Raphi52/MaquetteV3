// FORME « OPERATION » : un geste ponctuel sur un ou quelques objets (supprimer ou liberer une instance, joindre,
// rapprocher ou remplacer un acteur, changer le juge-commissaire). Trois temps, et pas un de plus : CHOISIR les objets,
// voir les CONSEQUENCES (les regles de la fiche RIG v2 et ses champs), CONFIRMER par le bouton de la fiche. Ecran
// reuni : on agit dans UNE procedure a la fois (cocher une ligne d'une autre procedure repart d'elle).
// Une operation sur UN objet que la fiche RIG v2 RECOIT d'un autre ecran (descripteur.objetUnique : PROC_IA et sa liasse)
// n'a pas de liste : voir OperationSurUnObjet.
// Confirmer va a l'ecrivain de la procedure (type Confirmer, les objets choisis, les champs remplis) ; fait, la liste est
// relue (plan « RIG v3 100 % fonctionnel », lot 2). Sans ecrivain, la confirmation ne change rien et le dit.
// PLUSIEURS GESTES (lot 11) : une fiche RIG v2 qui offre plusieurs boutons sur les memes objets (« Générer » ou « Supprimer
// le bordereau » d'un envoi JAL, OPE_A_ENVOYER / OPE_DEJA_ENVOYER ; valider ou editer un certificat BODACC) les nomme dans
// descripteur.gestes : CONFIRMER montre un bouton par geste (gestesDe), chacun envoye a l'ecrivain sous son nom. Sans gestes
// nommes, le bouton valider seul, comme avant. fix-ok: mesure par le controle final du lot 11 (27/11/2026) -- confirmer
// n'envoyait que p.valider, si bien qu'un second geste V2 de la meme liste n'atteignait jamais son ecrivain.
// LA BASE (lot 2) : quand le lecteur sait chercher, le texte et les criteres cherchent dans la base (useRecherche) -- un
// objet sur UN numero aussi, qui n'est donc pas limite aux lignes deja lues ; un objet se deplie (ou s'ouvre, sur UN
// objet) sur SA fiche lue, en lecture (FicheLue, useFiche). fix-ok: mesure par le controle final du lot 2 (26/11/2026) --
// les deux operations filtraient seulement les lignes deja lues (passeCriteres/texteLigne a l'ecran, jamais chercher) et
// montraient leurs sections sans valeurs : aucun appel a EcranGenere.Chercher ni EcranGenere.Fiche ne partait.

import { useEffect, useMemo, useState } from 'react'
import { EnteteListe, ListeModerne, normaliser, PiedListe } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import {
  Annonce, ChampSaisie, DEMONSTRATION, Exemple, FicheLue, NoteRecherche, PanneauCriteres, Provenance, Sections,
  TableLignes, cleChamp, etatDe, figee, gestesDe, manquantes, parcoursDe, refus, useAnnonce, useFiche, useGeste, useLignes,
  useRecherche, type Saisies,
} from './commun'

const Temps = ['Choisir', 'Conséquences', 'Confirmer'] as const

export function FormeOperation({ d }: { d: DescripteurEcranVue }) {
  return d.objetUnique ? <OperationSurUnObjet d={d} /> : <OperationSurLaListe d={d} />
}

function OperationSurLaListe({ d }: { d: DescripteurEcranVue }) {
  const lues = useLignes(d)
  const { vue, lignes, relire } = lues
  const [temps, poserTemps] = useState(0)
  const [texte, poserTexte] = useState('')
  const [criteres, poserCriteres] = useState<Saisies>({})
  const [cochees, poserCochees] = useState<ReadonlySet<number>>(() => new Set())
  const [saisies, poserSaisies] = useState<Saisies>({})
  const [annonce, annoncer] = useAnnonce()
  const jouer = useGeste(d, vue, annoncer)

  const { visibles, enBase, filtre } = useRecherche(lues, texte, criteres)
  const choisies = lignes.filter((l) => cochees.has(l.i))
  const p = choisies[0] ? parcoursDe(d, choisies[0].groupe) : d.membres[0] ?? d
  const basculer = (i: number) => poserCochees((avant) => {
    const ligne = lignes.find((l) => l.i === i)
    const autre = ligne && choisies[0] && ligne.groupe !== choisies[0].groupe
    const apres = new Set(autre ? [] : avant)
    if (!apres.delete(i)) apres.add(i)
    return apres
  })
  const gestes = gestesDe(p)
  const confirmer = (geste: string) => {
    const manque = manquantes(p.sections, saisies)
    if (manque.length > 0) {
      poserTemps(1)
      annoncer(`Contrôle de la fiche RIG v2 : « ${manque.join(' », « ')} » ${manque.length > 1 ? 'sont obligatoires' : 'est obligatoire'}. ${DEMONSTRATION}.`)
      return
    }
    const remplies = Object.fromEntries(Object.entries(saisies).filter(([, v]) => v.trim() !== ''))
    void jouer({ type: 'Confirmer', nom: geste, groupe: choisies[0]?.groupe ?? p.titre, ids: choisies.map((l) => l.id), valeurs: remplies },
      refus(geste, `sur ${choisies.length} objet${choisies.length > 1 ? 's' : ''} : rien n’est modifié dans la base`),
      () => { poserCochees(new Set()); poserSaisies({}); poserTemps(0); relire() })
  }
  const remplis = p.sections.flatMap((s) => s.champs.map((c) => ({ c, v: saisies[cleChamp(s.titre, c.libelle)] ?? '' })))
    .filter(({ v }) => v.trim() !== '')

  return (
    <div className="rig-proc__corps rig-eg" data-forme="Operation">
      {temps === 0 && (
        <PanneauCriteres d={d} criteres={criteres} changer={(c, v) => poserCriteres((avant) => ({ ...avant, [c]: v }))}
                         effacer={() => poserCriteres({})} />
      )}
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={<>{p !== d ? `${p.titre} · ` : ''}<b>{choisies.length}</b> choisi{choisies.length > 1 ? 's' : ''}</>}
                     filtre={temps === 0 ? { valeur: texte, changer: poserTexte, indication: d.recherche } : undefined} />
        <ol className="rig-frise rig-frise--h rig-eg-temps" aria-label="Temps de l’opération">
          {Temps.map((t, k) => (
            <li key={t} className={`rig-frise__pas${k < temps ? ' rig-frise__pas--complet' : ''}${k === temps ? ' rig-frise__pas--courant' : ''}`}>
              <button type="button" className="rig-frise__bouton" aria-current={k === temps ? 'step' : undefined}
                      disabled={k > 0 && choisies.length === 0} onClick={() => poserTemps(k)}>
                <span className="rig-frise__pastille">{k < temps ? '✓' : k + 1}</span>
                <span className="rig-frise__libelle">{t}</span>
              </button>
            </li>
          ))}
        </ol>
        {temps === 0 && (
          <>
            {vue && <Provenance vue={vue} />}
            <NoteRecherche enBase={enBase} />
            <TableLignes d={d} vue={vue} lignes={visibles} total={lignes.length} filtre={filtre}
                         detail={(l) => <FicheLue d={d} l={l} vue={vue} />}
                         cases={{ cochees, basculer, toutes: (cocher) => poserCochees(cocher
                           ? new Set(visibles.filter((l) => !figee(l.etat) && (!choisies[0] || l.groupe === choisies[0].groupe)).map((l) => l.i))
                           : new Set()) }} />
          </>
        )}
        {temps === 1 && (
          <Sections sections={p.sections} saisies={saisies} changer={(cle, v) => poserSaisies((avant) => ({ ...avant, [cle]: v }))} />
        )}
        {temps === 2 && (
          <div className="rig-eg-recap">
            <p><b>{gestes.join(' ou ')}</b> sur :</p>
            <ul>{choisies.map((l) => <li key={l.i}><b>{l.nom}</b> — {l.precisions}</li>)}</ul>
            {remplis.length > 0 && (
              <dl className="rig-eg-lecture">
                {remplis.map(({ c, v }) => <div key={c.libelle} className="rig-eg-lecture__champ"><dt>{c.libelle}</dt><dd>{v === 'oui' ? 'Oui' : v}</dd></div>)}
              </dl>
            )}
          </div>
        )}
        <Annonce texte={annonce} />
        <PiedListe>
          <button type="button" className="rig-lm-outil" disabled={temps === 0} onClick={() => poserTemps(temps - 1)}>Précédent</button>
          {temps < 2 ? (
            <button type="button" className="rig-lm-outil rig-lm-outil--fort" disabled={choisies.length === 0}
                    onClick={() => poserTemps(temps + 1)}>Suivant</button>
          ) : gestes.map((g, k) => (
            <button key={g} type="button" className={`rig-lm-outil${k === 0 ? ' rig-lm-outil--fort' : ''}`} onClick={() => confirmer(g)}>{g}</button>
          ))}
        </PiedListe>
      </ListeModerne>
    </div>
  )
}

const TempsUnObjet = ['Lancement', 'Résultat'] as const

// Une operation sur UN objet que la fiche RIG v2 RECOIT d'un autre ecran, au lieu de le choisir : PROC_IA est chargee par
// la visionneuse des pieces d'une liasse, qui lui passe le numero de la liasse affichee (OPE_DOC_DEMAT.cs:818). Aucune
// liste : l'objet se designe par son numero, dans le seul critere du descripteur. Deux temps (proc_ia.md:151-156) :
// LANCEMENT -- l'objet, ce que le geste emporte (les sections), le bouton apres confirmation (OPE_IA.cs:66) --, puis
// RESULTAT (descripteur.resultat), qui n'existe qu'une fois le geste fait. Un objet deja traite s'ouvre sur son resultat
// et n'a plus de bouton : PROC_IA relit sa reponse sans rappeler le service (PROC_IA.cs:60-64) et cache le lancement des
// qu'une reponse existe (ETP_ENTETE.cs:36-37). Controle final du lot 1 (25/11/2026) : la liasse se cochait dans une liste,
// et le resultat et l'avis s'affichaient avant le lancement.
function OperationSurUnObjet({ d }: { d: DescripteurEcranVue }) {
  const lues = useLignes(d)
  const { vue, lignes, relire } = lues
  const resultat = d.resultat ?? []
  const critere = d.criteres[0] ?? 'Numéro'
  const [numero, poserNumero] = useState('')
  const [temps, poserTemps] = useState(0)
  const [aConfirmer, poserAConfirmer] = useState(false)
  const [annonce, annoncer] = useAnnonce()
  const jouer = useGeste(d, vue, annoncer)

  const trouver = (texte: string) => {
    const cherche = normaliser(texte.trim())
    return cherche === '' ? undefined : lignes.find((l) => normaliser(l.nom) === cherche)
  }
  // Le numero cherche aussi dans la base (son seul critere) : l'objet n'a pas a etre parmi les lignes deja lues.
  const parNumero = useMemo(() => ({ [critere]: numero }), [critere, numero])
  const { enBase } = useRecherche(lues, '', parNumero)
  const objet = trouver(numero)
  const { fiche } = useFiche(d, objet, vue)
  const traite = objet !== undefined && figee(objet.etat)
  const etat = objet ? etatDe(d, objet) : undefined
  const tempsMontres = resultat.length > 0 ? TempsUnObjet : TempsUnObjet.slice(0, 1)
  // Un autre numero, un autre objet : deja traite, il s'ouvre sur son resultat.
  const saisir = (texte: string) => {
    const autre = trouver(texte)
    poserNumero(texte)
    poserTemps(autre && figee(autre.etat) && resultat.length > 0 ? 1 : 0)
    poserAConfirmer(false)
    annoncer(null)
  }
  // Un objet trouve PLUS TARD, par la recherche dans la base, s'ouvre comme s'il avait ete la a la frappe.
  const cleObjet = objet ? `${objet.groupe}\u0000${objet.nom}` : ''
  useEffect(() => {
    if (objet && figee(objet.etat) && resultat.length > 0) poserTemps(1)
    // objet : sa cle suffit (une relecture apres le geste ne doit pas changer de temps).
  }, [cleObjet])
  const lancer = () => {
    poserAConfirmer(false)
    if (objet) void jouer({ type: 'Confirmer', nom: d.valider, groupe: objet.groupe, ids: [objet.id], valeurs: {} },
      refus(d.valider, `sur « ${objet.nom} » : rien n’est modifié dans la base`), relire)
  }

  return (
    <div className="rig-proc__corps rig-eg" data-forme="Operation">
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={<>{critere} : <b>{objet?.nom ?? '—'}</b>{etat ? ` · ${etat.libelle}` : ''}</>} />
        <ol className="rig-frise rig-frise--h rig-eg-temps" aria-label="Temps de l’opération">
          {tempsMontres.map((t, k) => (
            <li key={t} className={`rig-frise__pas${k < temps ? ' rig-frise__pas--complet' : ''}${k === temps ? ' rig-frise__pas--courant' : ''}`}>
              <button type="button" className="rig-frise__bouton" aria-current={k === temps ? 'step' : undefined}
                      disabled={k > 0 && !traite} onClick={() => poserTemps(k)}>
                <span className="rig-frise__pastille">{k < temps ? '✓' : k + 1}</span>
                <span className="rig-frise__libelle">{t}</span>
              </button>
            </li>
          ))}
        </ol>
        {temps === 0 && (
          <>
            <div className="rig-eg-recap">
              <div className="rig-champs rig-eg-champs">
                <ChampSaisie champ={{ libelle: critere, type: 'Texte', options: [] }} valeur={numero} changer={saisir} />
              </div>
            </div>
            {vue && (
              <>
                <p className="rig-eg-note" role="note">
                  RIG v2 reçoit ce numéro de l’écran qui l’ouvre ; ici, il se saisit.
                  {vue.mode !== 'base' && lignes[0] && <> <Exemple /> par exemple « {lignes[0].nom} ».</>}
                </p>
                <Provenance vue={vue} />
                <NoteRecherche enBase={enBase} />
              </>
            )}
            {numero.trim() !== '' && !objet && (
              <p className="rig-eg-annonce" role="status">Aucune ligne ne porte le numéro « {numero.trim()} ».</p>
            )}
            {objet && (
              <>
                <div className="rig-eg-recap" aria-label="Objet de l’opération">
                  <p><b>{objet.nom}</b> — {objet.precisions}</p>
                </div>
                {fiche && fiche.mode !== 'base' && fiche.message && <p className="rig-eg-note" role="note">{fiche.message}</p>}
                <Sections sections={d.sections} saisies={{}} changer={() => undefined} lecture fiche={fiche?.fiche ?? null} />
                {traite ? (
                  <p className="rig-eg-note" role="note">
                    {etat?.libelle ?? 'Déjà traité'} : le résultat se relit sans relancer « {d.valider} ».
                  </p>
                ) : resultat.length > 0 && (
                  <p className="rig-eg-note" role="note">
                    Le résultat ({resultat.map((s) => s.titre).join(', ')}) s’affiche au temps suivant, une fois « {d.valider} » fait.
                  </p>
                )}
                {aConfirmer && (
                  <div className="rig-eg-recap" role="alertdialog" aria-label="Confirmation">
                    <p>{d.valider} sur « {objet.nom} » ?</p>
                    <div className="rig-eg-edition-actions">
                      <button type="button" className="rig-lm-outil rig-lm-outil--fort" onClick={lancer}>Confirmer</button>
                      <button type="button" className="rig-lm-outil" onClick={() => poserAConfirmer(false)}>Annuler</button>
                    </div>
                  </div>
                )}
              </>
            )}
          </>
        )}
        {temps === 1 && objet && (
          <>
            <div className="rig-eg-recap" aria-label="Objet de l’opération">
              <p><b>{objet.nom}</b> — {objet.precisions}</p>
            </div>
            <Sections sections={resultat} saisies={{}} changer={() => undefined} lecture fiche={fiche?.fiche ?? null} />
          </>
        )}
        <Annonce texte={annonce} />
        <PiedListe>
          {temps === 1 && <button type="button" className="rig-lm-outil" onClick={() => poserTemps(0)}>Précédent</button>}
          {temps === 0 && objet && !traite && (
            <button type="button" className="rig-lm-outil rig-lm-outil--fort" disabled={aConfirmer}
                    onClick={() => { annoncer(null); poserAConfirmer(true) }}>{d.valider}</button>
          )}
          {temps === 0 && traite && resultat.length > 0 && (
            <button type="button" className="rig-lm-outil rig-lm-outil--fort" onClick={() => poserTemps(1)}>Voir le résultat</button>
          )}
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
