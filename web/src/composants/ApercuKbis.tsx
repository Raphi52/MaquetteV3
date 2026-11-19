// Apercu d'un extrait Kbis avec la mise en page NATIONALE de RIG v2.
// Source : \\ged2\Schema_RIG_XSD$\KBisXml\v35 xsl 1.24\KbisFromXMLPreformatPDF - xsd 0.35 - xsl 1.24.xsl
// (copie : wwwroot/kbis/). Valeurs reprises de la feuille XSL-FO :
// A4 21 x 29.7 cm, marges gauche 1 cm / droite 2 cm, police Times,
// entete greffe 8pt (libelles 10pt gras), « Extrait Kbis » 11pt gras italique,
// titre 10pt gras centre, sections 9pt gras + filet 0.9px, lignes 10pt,
// colonne des titres 205px en italique, pied « FIN DE L'EXTRAIT » a droite.
// Les VALEURS viennent du formulaire : chaque frappe re-rend l'extrait.
//
// Porte en React (lot 6 de la migration, 19/11/2026) depuis
// Components/ApercuKbis.razor : meme balisage, memes classes CSS. Les valeurs
// (greffe, dates, capital, sections sans ligne masquees) sont calculees par
// Pont/ApercuKbis.cs, que la coquille appelle avec la lecture du formulaire.

/** Une ligne de l'extrait (LigneKbisVue). */
export interface LigneKbisVue {
  titre: string
  libelle: string
}

/** Une section, avec ses seules lignes renseignees (SectionKbisVue). */
export interface SectionKbisVue {
  titre: string
  lignes: LigneKbisVue[]
}

/** Ce que Pont/ApercuKbis.cs calcule (VueKbis). */
export interface VueKbis {
  greffeLibelle: string
  greffeAdresse: string
  gestion: string
  fraicheur: string
  sections: SectionKbisVue[]
  villeRcs: string
  horodatage: string
  numero: string
}

export interface ProprietesApercuKbis {
  vue: VueKbis
}

export function ApercuKbis({ vue }: ProprietesApercuKbis) {
  return (
    <div className="rig-kbis">
      <div className="rig-kbis__entete">
        <div className="rig-kbis__greffe-libelle">{vue.greffeLibelle}</div>
        {' '}
        <div className="rig-kbis__greffe-libelle">{'Registre du Commerce et des Societes'}</div>
        {vue.greffeAdresse.length > 0 && (
          <div>{vue.greffeAdresse}</div>
        )}
        <div className="rig-kbis__gestion">Numero de gestion {vue.gestion}</div>
      </div>
      {' '}
      <div className="rig-kbis__kbis">Extrait Kbis</div>
      {' '}
      <div className="rig-kbis__titre">EXTRAIT D'IMMATRICULATION PRINCIPALE AU REGISTRE DU COMMERCE ET DES SOCIETES</div>
      {' '}
      <div className="rig-kbis__fraicheur">a jour au {vue.fraicheur}</div>
      {' '}
      <table className="rig-kbis__corps">
        <colgroup><col className="rig-kbis__col-titre" /><col /></colgroup>
        {/* Comme la XSL : une section sans aucune ligne renseignee n'est pas
            affichee (le pont ne la rend pas), une ligne sans libelle non plus.
            Le <tbody> est celui que le navigateur cree de lui-meme autour des
            lignes d'une table (Razor les posait dans <table>, que le CSS
            mettait deja dans un groupe de lignes anonyme) : meme rendu, et
            React n'accepte pas un <tr> enfant direct d'une <table>. */}
        <tbody>
          {vue.sections.map((section) => [
            <tr key={section.titre}><td colSpan={2} className="rig-kbis__section">{section.titre}</td></tr>,
            ...section.lignes.map((l, i) => (
              <tr key={section.titre + '/' + i} className="rig-kbis__ligne">
                <td className="rig-kbis__ligne-titre">{l.titre}</td>
                <td>{l.libelle}</td>
              </tr>
            )),
          ])}
        </tbody>
      </table>
      {' '}
      <div className="rig-kbis__bas">
        <div className="rig-kbis__signature">Le Greffier</div>
        {' '}
        <div>FIN DE L'EXTRAIT</div>
      </div>
      {' '}
      <div className="rig-kbis__pied">
        <span>R.C.S. {vue.villeRcs} - {vue.horodatage}&nbsp;&nbsp;&nbsp;&nbsp;{vue.numero}</span>
        {' '}
        <span>page 1/1</span>
      </div>
    </div>
  )
}
