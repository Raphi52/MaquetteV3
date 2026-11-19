// LES CHIFFRES DU JOUR (2026-11-18) et « PROUE » : trois compteurs deja
// calcules ailleurs, montres en tuiles pleines entre la recherche et la grille.
// « Arrivees » = le total de cette categorie dans « A traiter », les deux
// autres = les mesures de la tuile Performance : un chiffre ne peut donc jamais
// differer de sa tuile d'origine. Chaque tuile porte un rond cerne d'un ANNEAU
// qui se remplit, et chaque tuile est un BOUTON qui deplie sa liste dessous ;
// la grille se replie alors en resumes d'une ligne.
//
// Porte en React (lot 1 du plan « finir le passage a React ») depuis les
// blocs .rig-chiffres et .rig-proue-* de Home.razor.

import type { CSSProperties } from 'react'
import { Performance } from './Tuiles'
import type { ChiffresVue, GesteAccueil, ProueVue, ResumeSpanVue } from './types'

function cible(valeur: number): CSSProperties {
  return { '--rig-anneau-cible': valeur } as CSSProperties
}

function bouton(rang: number, deplie: number | null): string {
  return 'rig-chiffre rig-chiffre--' + rang + ' ' + (deplie === rang ? 'rig-chiffre--deplie' : '')
}

export function Chiffres({ vue, geste }: { vue: ChiffresVue, geste: GesteAccueil }) {
  const d = vue.deplie
  return (
    <div className="rig-chiffres">
      <button type="button" className={bouton(1, d)}
              aria-expanded={d === 1 ? 'true' : 'false'}
              title={(d === 1 ? 'Replier' : 'Deplier') + ' la liste des arrivees'}
              onClick={() => geste('chiffre', 1)}>
        <span className="rig-chiffre__libelle">Arrivees</span>
        {' '}
        <span className="rig-chiffre__rang">
          <span className="rig-chiffre__anneau" aria-hidden="true">
            <span className="rig-chiffre__cercle rig-chiffre__cercle--parts" style={{ background: vue.anneau1 }}></span>
            {' '}
            <span className="rig-chiffre__rond" aria-hidden="true"><svg viewBox="0 0 16 16"><path d="M2.5 9.5h3l1 2h3l1-2h3M3.5 4h9l1 5.5V13h-11V9.5z" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round"></path></svg></span>
          </span>
          {' '}
          <span className="rig-chiffre__texte">
            <b className="rig-chiffre__valeur">{vue.arrivees}</b>
            {' '}
            <span className="rig-chiffre__detail" title={vue.titreDetail1 ?? undefined}>{vue.detail1}</span>
          </span>
        </span>
      </button>
      {' '}
      <button type="button" className={bouton(2, d)}
              aria-expanded={d === 2 ? 'true' : 'false'}
              title={(d === 2 ? 'Replier' : 'Deplier') + ' les mesures de performance'}
              onClick={() => geste('chiffre', 2)}>
        <span className="rig-chiffre__libelle">Dossiers traites</span>
        {' '}
        <span className="rig-chiffre__rang">
          {/* L'anneau dit l'ATTEINTE de l'objectif, la meme grandeur qui colore
              la barre de la tuile Performance. */}
          <span className="rig-chiffre__anneau" aria-hidden="true">
            <span className="rig-chiffre__cercle rig-chiffre__cercle--jauge" style={cible(vue.cible2)}></span>
            {' '}
            <span className="rig-chiffre__rond" aria-hidden="true"><svg viewBox="0 0 16 16"><path d="m3.5 8.5 3 3 6-6.5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"></path></svg></span>
          </span>
          {' '}
          <span className="rig-chiffre__texte">
            <b className="rig-chiffre__valeur">{vue.valeur2}</b>
            {' '}
            <span className="rig-chiffre__detail">{vue.objectif2}</span>
          </span>
        </span>
      </button>
      {' '}
      <button type="button" className={bouton(3, d)}
              aria-expanded={d === 3 ? 'true' : 'false'}
              title={(d === 3 ? 'Replier' : 'Deplier') + ' la liste des dossiers hors delai legal'}
              onClick={() => geste('chiffre', 3)}>
        <span className="rig-chiffre__libelle">Hors delai legal</span>
        {' '}
        <span className="rig-chiffre__rang">
          {/* L'anneau dit la PART des dossiers en cours qui ont passe le delai ;
              des qu'il y en a un, il prend la teinte d'alerte et bat doucement. */}
          <span className={'rig-chiffre__anneau ' + (vue.alerte3 ? 'rig-chiffre__anneau--alerte' : '')} aria-hidden="true">
            <span className="rig-chiffre__cercle rig-chiffre__cercle--jauge" style={cible(vue.cible3)}></span>
            {' '}
            <span className="rig-chiffre__rond" aria-hidden="true"><svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.6" fill="none" stroke="currentColor" strokeWidth="1.5"></circle><path d="M8 4.8V8l2.2 1.6" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round"></path></svg></span>
          </span>
          {' '}
          <span className="rig-chiffre__texte">
            <b className="rig-chiffre__valeur">{vue.valeur3}</b>
            {' '}
            <span className="rig-chiffre__detail">{vue.objectif3}</span>
          </span>
        </span>
      </button>
    </div>
  )
}

/** Un span de la ligne de resume : chaque morceau est un noeud de texte a part. */
function SpanResume({ span }: { span: ResumeSpanVue }) {
  return (
    <>
      {span.blancAvant && ' '}
      <span>{span.morceaux.map((m, i) => (m.gras ? <b key={i}>{m.texte}</b> : m.texte))}</span>
    </>
  )
}

const LIBELLES = { 1: 'Arrivees', 2: 'Performance', 3: 'Hors delai legal' } as Record<number, string>

/**
 * « Proue » : la liste du chiffre deplie, juste sous lui, puis les tuiles de
 * la grille en RESUME d'une ligne. La grille reste dans la page (masquee) :
 * son glisser-deposer y reste branche.
 */
export function Proue({ proue, geste }: { proue: ProueVue, geste: GesteAccueil }) {
  const rang = proue.rang
  return (
    <>
      <div className={'rig-proue-pointe rig-proue-pointe--' + rang} aria-hidden="true"></div>
      {' '}
      <section className="rig-proue-liste" aria-label={LIBELLES[rang] ?? LIBELLES[3]}>
        {rang === 1 && (
          <>
            <header className="rig-proue-liste__tete">
              <span className="rig-proue-liste__titre">{'Arrivees · '}<b>{proue.compte}</b></span>
              {proue.lien && (
                <button type="button" className="rig-proue-liste__lien"
                        onClick={() => geste('voirTuile', 'alertes')}>{'Tout voir dans A traiter →'}</button>
              )}
            </header>
            {proue.arrivees.length === 0 && <div className="rig-vide">Aucune arrivee a traiter.</div>}
            {proue.arrivees.map((a) => (
              <button key={a.id} className="rig-alerte rig-alerte--lien" type="button"
                      title={'Aller vers ' + a.libelleCible}
                      onClick={() => geste('alerte', a.id)}>
                <span className={'rig-alerte__compte rig-alerte__compte--' + a.palier}>{a.nombre}</span>
                {' '}
                <span className="rig-alerte__corps">
                  <span className="rig-alerte__titre">{a.titre}</span>
                </span>
                {' '}
                <span className="rig-alerte__vers">{a.libelleCible}{' ›'}</span>
              </button>
            ))}
          </>
        )}
        {rang === 2 && (
          <>
            <header className="rig-proue-liste__tete">
              <span className="rig-proue-liste__titre">{'Dossiers traites · '}<b>{proue.compte}</b></span>
              {proue.lien && (
                <button type="button" className="rig-proue-liste__lien"
                        onClick={() => geste('voirTuile', 'performance')}>{'Tout voir dans Performance →'}</button>
              )}
            </header>
            <Performance mesures={proue.mesures} />
          </>
        )}
        {rang !== 1 && rang !== 2 && (
          <>
            <header className="rig-proue-liste__tete">
              <span className="rig-proue-liste__titre">{'Hors delai legal · '}<b>{proue.compte}</b></span>
              {proue.lien && (
                <button type="button" className="rig-proue-liste__lien"
                        onClick={() => geste('voirTuile', 'taches')}>{'Tout voir dans Mes taches du jour →'}</button>
              )}
            </header>
            {proue.horsDelai.length === 0 && <div className="rig-vide">Aucun dossier hors delai legal.</div>}
            {proue.horsDelai.map((d) => (
              <button key={d.numero} className="rig-alerte rig-alerte--lien" type="button"
                      title={"Ouvrir l'affaire " + d.dossier}
                      onClick={() => geste('numero', d.dossier)}>
                <span className="rig-filet-v rig-filet-v--alerte"></span>
                {' '}
                <span className="rig-alerte__corps">
                  <span className="rig-alerte__titre">{d.nom}</span><br />
                  {' '}
                  {/* Jamais le code du traitement : l'accueil ne l'affiche pas. */}
                  <span className="rig-alerte__detail">{'J+'}{d.jours}{' · demande '}{d.numero}{' · creee le '}{d.creation}</span>
                </span>
                {' '}
                <span className="rig-alerte__vers">{'affaire '}{d.dossier}{' ›'}</span>
              </button>
            ))}
          </>
        )}
      </section>
      {' '}
      <div className="rig-proue-resumes">
        {proue.resumes.map((def) => (
          <button key={def.id} type="button" className="rig-proue-resume"
                  title={'Revenir a la grille sur la tuile ' + def.titre}
                  onClick={() => geste('voirTuile', def.id)}>
            <span className="rig-proue-resume__tete">
              <span className="rig-widget__poignee" aria-hidden="true">{'⋮⋮'}</span>
              {' '}
              <span className="rig-widget__titre">{def.titre}</span>
            </span>
            {' '}
            <span className="rig-proue-resume__ligne">
              {def.ligne.map((s, i) => <SpanResume key={i} span={s} />)}
            </span>
          </button>
        ))}
      </div>
    </>
  )
}
