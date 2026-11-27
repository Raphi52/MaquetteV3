// Ecran de la procedure PROC_EXPERT.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_EXPERT) :
//   - Recherche/OPE_RECHERCHE_EXPERT : TextRecherche, DataListviewExpert
//   - Edition/OPE_EDITION_EXPERT     : ComboBoxCivilite, TextNom, TextAbrege,
//     TextCompNom, TextDateNaissance, TextAdresse, TextTel, TextFax, TextMail,
//     ComboBoxQualifProfession, CheckBoxActif, OpeMesureActif / OpeMesureInactif
//   - Edition/OPE_MESURE_EXPERT -> Rig_Ult_Metier/LISTVIEW/
//     ULT_LISTVIEW_MESURES_EXPERT.cs, dont InitColumns() donne les SIX colonnes
//     reprises ici : Num mesure, Civilite, Nom participant, Complement nom,
//     Qualif prof, Adresse.
//
// Porte depuis Components/Ecrans/EcranExperts.razor (lot 4) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses experts et
// leurs mesures de demonstration l'ont suivi ici.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 2) : la liste des experts prend les
// briques de composants/ListeModerne.tsx. Tete (synthese, « Aller a un expert… » Ctrl K, Entree choisit l'expert
// reste seul), vues = la bascule « Experts actifs uniquement » prereglee, avatar + nom + qualite, Actif en point
// d'etat, pied. Ses cinq colonnes v2 tiennent sur la ligne : pas de ligne depliee. Les mesures de l'expert
// choisi : puces de vues comptees a la place des onglets, avatar du participant.
// « Aller a un expert… » EST la recherche v2 (TextRecherche, ancien champ « Nom, abrege » : meme etat, memes
// colonnes cherchees) : la liste est filtree ici, sans pont, deux champs auraient fait le meme filtre (decision
// du plan, lot 7). La fiche d'edition n'est pas une liste : inchangee, comme la selection et les autres filtres.
//
// BRANCHE SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026) : les experts et leurs mesures viennent du pont
// (Pont/EcranExpertsBase.cs) -- table EXPERT et PARTICIPANT joint a MESURE de la base du greffe (Serveur/Socle/EcransMain/
// LectureExperts.cs, Participant.GetListeParticipantJoinMesureByExpert cite). L'ecran n'ecrit rien. Sans serveur, les exemples
// ci-dessous, marques ; en panne, le motif et aucun expert.
// check: branchement neuf (lot 14), pas un correctif -- remplacements cibles, prouves par EcranExperts.test.tsx (vitest).

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { contientSansCasse, distinctsTries, estBlanc } from '../criteres'
import { CelluleIdentite, EnteteListe, ListeModerne, PiedListe, PointEtat, VuesListe } from '../composants/ListeModerne'

interface Expert {
  abrege: string
  civilite: string
  nom: string
  complementNom: string
  naissance: string
  adresse: string
  tel: string
  fax: string
  mail: string
  qualif: string
  actif: boolean
  /** Lu en base : le numero de l'expert (les mesures le citent) ; absent en demonstration. */
  cle?: string
}

// Colonnes de ULT_LISTVIEW_MESURES_EXPERT.InitColumns() (RIG v2) :
// MSINS_NUM_MESURE, PRTCP_CIVILITE, PRTCP_NOM, PRTCP_COMPLEMENT_NOM,
// PRTCP_QUALIF_PROFESSION, Adresse.AdresseComplete.
interface MesureExpert {
  expert: string
  numMesure: string
  civilite: string
  nomParticipant: string
  complementNom: string
  qualifProf: string
  adresse: string
  enCours: boolean
}

/** Ce que rend le pont (Pont/EcranExpertsBase.cs, VueExperts). */
export interface VueExperts {
  mode: ModeLecture
  message: string | null
  experts: Expert[]
  mesures: MesureExpert[]
  total: number
}

const cleDe = (e: Expert) => e.cle ?? e.abrege

const ExemplesExperts: Expert[] = [
  { abrege: 'GIRARD', civilite: 'M.', nom: 'GIRARD Paul', complementNom: 'Cabinet Girard & Fils', naissance: '14/05/1971', adresse: '9 rue Gambetta, 51100 Reims', tel: '03 26 12 30 44', fax: '', mail: 'p.girard@exemple.fr', qualif: 'Expert-comptable', actif: true },
  { abrege: 'NOEL', civilite: 'Mme', nom: 'NOEL Julie', complementNom: '', naissance: '02/11/1980', adresse: '5 bd Foch, 51100 Reims', tel: '03 26 44 12 87', fax: '03 26 44 12 88', mail: 'j.noel@exemple.fr', qualif: 'Expert en evaluation', actif: true },
  { abrege: 'ROUX', civilite: 'M.', nom: 'ROUX Bernard', complementNom: 'SELARL Roux Expertise', naissance: '27/03/1958', adresse: '18 rue Talleyrand, 51100 Reims', tel: '03 26 05 76 21', fax: '', mail: 'b.roux@exemple.fr', qualif: 'Expert immobilier', actif: false },
]

const ExemplesMesures: MesureExpert[] = [
  { expert: 'GIRARD', numMesure: 'M-2026-0114', civilite: 'M.', nomParticipant: 'GIRARD Paul', complementNom: 'Cabinet Girard & Fils', qualifProf: 'Expert-comptable', adresse: '9 rue Gambetta, 51100 Reims', enCours: true },
  { expert: 'GIRARD', numMesure: 'M-2024-0503', civilite: 'M.', nomParticipant: 'GIRARD Paul', complementNom: 'Cabinet Girard & Fils', qualifProf: 'Expert-comptable', adresse: '9 rue Gambetta, 51100 Reims', enCours: false },
  { expert: 'NOEL', numMesure: 'M-2026-0128', civilite: 'Mme', nomParticipant: 'NOEL Julie', complementNom: '', qualifProf: 'Expert en evaluation', adresse: '5 bd Foch, 51100 Reims', enCours: true },
  { expert: 'ROUX', numMesure: 'M-2025-0087', civilite: 'M.', nomParticipant: 'ROUX Bernard', complementNom: 'SELARL Roux Expertise', qualifProf: 'Expert immobilier', adresse: '18 rue Talleyrand, 51100 Reims', enCours: false },
]

/** Le filtre de la recherche (texte, qualite, actifs) : la liste, et le compte de chaque vue. */
function filtrer(Experts: Expert[], recherche: string, critQualif: string, actifsSeuls: boolean): Expert[] {
  let q = Experts
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    q = q.filter((e) => contientSansCasse(e.nom, t) || contientSansCasse(e.abrege, t))
  }
  if (critQualif !== '') q = q.filter((e) => e.qualif === critQualif)
  if (actifsSeuls) q = q.filter((e) => e.actif)
  return q
}

/** Les vues : la bascule « Experts actifs uniquement », prereglee. */
export const VuesExperts: ReadonlyArray<{ cle: string; libelle: string; actifsSeuls: boolean }> = [
  { cle: 'actifs', libelle: 'Actifs', actifsSeuls: true },
  { cle: 'tous', libelle: 'Tous', actifsSeuls: false },
]

export function EcranExperts() {
  // La recherche v2 (TextRecherche) : le champ « Aller a un expert… » de la tete.
  const [recherche, setRecherche] = useState('')
  const [critQualif, setCritQualif] = useState('')
  const [actifsSeuls, setActifsSeuls] = useState(true)
  const [mesuresActives, setMesuresActives] = useState(true)
  const [cleChoisie, setCleChoisie] = useState<string | null>(null)

  // La vue du pont : null tant qu'il n'a pas repondu (aucun expert).
  const [vue, setVue] = useState<VueExperts | null>(null)
  useEffect(() => {
    let perime = false
    void appeler<VueExperts>('EcranExperts.Experts').then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [])
  const Experts: Expert[] = vue === null ? [] : vue.mode === 'serveur' ? vue.experts : vue.mode === 'demo' ? ExemplesExperts : []
  const Mesures: MesureExpert[] = vue?.mode === 'serveur' ? vue.mesures : vue?.mode === 'demo' ? ExemplesMesures : []
  const Qualifs = distinctsTries(Experts.map((x) => x.qualif).filter((q) => q !== ''))
  // Le premier expert est choisi au depart, comme avant le branchement.
  const selection = Experts.find((x) => cleDe(x) === cleChoisie) ?? (cleChoisie === null ? Experts[0] ?? null : null)
  const setSelection = (x: Expert) => setCleChoisie(cleDe(x))

  const trouves = filtrer(Experts, recherche, critQualif, actifsSeuls)

  const actifs = trouves.filter((ex) => ex.actif).length
  const qualites = distinctsTries(trouves.map((ex) => ex.qualif)).length

  const e = selection
  const mesuresDe = (enCours: boolean) => (e ? Mesures.filter((m) => m.expert === cleDe(e) && m.enCours === enCours) : [])
  const mesures = mesuresDe(mesuresActives)

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <ProvenanceEcran mode={vue?.mode} message={vue?.message} quoi="Experts" />
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Recherche d'un expert</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Qualite / profession</span>
          <select className="rig-saisie-champ__saisie" value={critQualif} onChange={(ev) => setCritQualif(ev.target.value)}>
            <option value="">Toutes</option>
            {Qualifs.map((q) => <option key={q} value={q}>{q}</option>)}
          </select>
        </label>

        <button className={`rig-bascule${actifsSeuls ? ' rig-bascule--actif' : ''}`}
                onClick={() => setActifsSeuls(!actifsSeuls)}>
          <span className="rig-bascule__temoin"></span>
          <span>Experts actifs uniquement</span>
        </button>

      </aside>

      <ListeModerne etiquette="Experts">
        {/* Liste de recherche : les CINQ colonnes de DataListviewExpert en RIG v2
            (Recherche/OPE_RECHERCHE_EXPERT.cs, appels AddColumns) --
              Abrege   EXPRT_ABREGE
              Civilite EXPRT_CIVILITE
              Nom      EXPRT_NOM
              Mail     EXPRT_ADRESSE_MAIL
              Actif    EXPRT_EST_ACTIF
            Elles ne tenaient pas dans la colonne de criteres (320px) : la liste
            occupe donc le haut du plan de travail, comme en v2 ou la recherche
            precede l'edition. */}
        <EnteteListe titre="Liste des experts"
                     synthese={<>{trouves.length}{trouves.length > 1 ? ' experts · ' : ' expert · '}<b>{actifs}</b>
                       {actifs > 1 ? ' actifs · ' : ' actif · '}<b>{qualites}</b>{qualites > 1 ? ' qualites' : ' qualite'}</>}
                     filtre={{ valeur: recherche, changer: setRecherche, indication: 'Aller a un expert…',
                               valider: () => { if (trouves.length === 1) setSelection(trouves[0]!) } }} />

        <VuesListe etiquette="Vues des experts"
                   vues={VuesExperts.map((v) => ({ cle: v.cle, libelle: v.libelle, compte: filtrer(Experts, recherche, critQualif, v.actifsSeuls).length }))}
                   active={VuesExperts.find((v) => v.actifsSeuls === actifsSeuls)?.cle ?? null}
                   choisir={(cle) => { const v = VuesExperts.find((x) => x.cle === cle); if (v) setActifsSeuls(v.actifsSeuls) }} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: '13%' }} /><col style={{ width: '9%' }} /><col /><col style={{ width: '24%' }} />
              <col style={{ width: '11%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>Abrege</th>
                <th>Civilite</th>
                <th>Nom</th>
                <th>Mail</th>
                <th>Actif</th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((ex) => (
                <tr key={cleDe(ex)} onClick={() => setSelection(ex)}
                    style={selection !== null && cleDe(selection) === cleDe(ex) ? { boxShadow: 'inset 3px 0 0 0 var(--rig-focus)' } : undefined}>
                  <td className="rig-table__mono">{ex.abrege}</td>
                  <td>{ex.civilite}</td>
                  <CelluleIdentite nom={ex.nom} objet={ex.qualif} titre={`${ex.nom} · ${ex.qualif}`} />
                  <td title={ex.mail}>{ex.mail}</td>
                  <td><PointEtat teinte={ex.actif ? 'vert' : 'gris'}>{ex.actif ? 'Actif' : 'Inactif'}</PointEtat></td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr className="rig-table__ligne--inerte"><td colSpan={5} className="rig-table__vide">Aucun expert ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{trouves.length}</b>{trouves.length > 1 ? ' experts affiches' : ' expert affiche'}</span>
          <span>Actifs <b>{actifs}</b></span>
        </PiedListe>

        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Edition d'un expert</div>
          <span className="rig-resultats-proc__compte">{selection?.abrege ?? 'aucun expert selectionne'}</span>
        </div>

        {e ? (
          <>
            <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
              <div className="rig-kicker">Identite</div>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Civilite</span>
                  <input className="rig-saisie-champ__saisie" value={e.civilite} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Abrege</span>
                  <input className="rig-saisie-champ__saisie" value={e.abrege} readOnly />
                </label>
              </div>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Nom</span>
                  <input className="rig-saisie-champ__saisie" value={e.nom} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Complement de nom</span>
                  <input className="rig-saisie-champ__saisie" value={e.complementNom} readOnly />
                </label>
              </div>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Date de naissance</span>
                <input className="rig-saisie-champ__saisie" value={e.naissance} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Adresse</span>
                <input className="rig-saisie-champ__saisie" value={e.adresse} readOnly />
              </label>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Telephone</span>
                  <input className="rig-saisie-champ__saisie" value={e.tel} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Fax</span>
                  <input className="rig-saisie-champ__saisie" value={e.fax} readOnly />
                </label>
              </div>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">E-mail</span>
                <input className="rig-saisie-champ__saisie" value={e.mail} readOnly />
              </label>
              {/* alignSelf : le bloc est une colonne flex qui ETIRE ses enfants ; la pastille courait sur toute la
                  largeur de la fiche (capture orange du lot 2 des listes modernes). */}
              <span className={`rig-etat rig-etat--${e.actif ? 'ok' : 'attente'}`} style={{ alignSelf: 'flex-start' }}>
                {e.actif ? 'Actif' : 'Inactif'}
              </span>
            </div>

            {/* Les deux listes de mesures de la v2 (OpeMesureActif / OpeMesureInactif) : des puces comptees a la
                place des onglets ; leur groupe porte son nom, distinct de celui des experts. */}
            <VuesListe etiquette="Vues des mesures" vues={[
                         { cle: 'en-cours', libelle: 'Mesures en cours', compte: mesuresDe(true).length },
                         { cle: 'terminees', libelle: 'Mesures terminees', compte: mesuresDe(false).length },
                       ]}
                       active={mesuresActives ? 'en-cours' : 'terminees'}
                       choisir={(cle) => setMesuresActives(cle === 'en-cours')} />

            <div className="rig-table-cadre">
              <table className="rig-table">
                <thead>
                  <tr>
                    <th>Num mesure</th>
                    <th>Civilite</th>
                    <th>Nom participant</th>
                    <th>Complement nom</th>
                    <th>Qualif prof</th>
                    <th>Adresse</th>
                  </tr>
                </thead>
                <tbody>
                  {mesures.map((m) => (
                    <tr key={m.numMesure}>
                      <td className="rig-table__mono">{m.numMesure}</td>
                      <td>{m.civilite}</td>
                      <CelluleIdentite nom={m.nomParticipant} titre={m.nomParticipant} />
                      <td>{m.complementNom}</td>
                      <td>{m.qualifProf}</td>
                      <td>{m.adresse}</td>
                    </tr>
                  ))}
                  {mesures.length === 0 && (
                    <tr><td colSpan={6} className="rig-table__vide">Aucune mesure dans cette liste.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </>
        ) : (
          <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>
            Choisissez un expert dans la liste de gauche.
          </div>
        )}
      </ListeModerne>
    </div>
  )
}
