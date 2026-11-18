<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet xmlns:i18n="i18n:i18n"
				xmlns:infg="infg:infg"
				xmlns:infogreffe="http://www.infogreffe.fr/fr/Centralisation/Dossier/v0"
				xmlns:xs="http://www.w3.org/2001/XMLSchema"
				xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
				xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
				version="2.0"
				xsi:schemaLocation="http://www.infogreffe.fr/fr/Centralisation/Dossier/v0 Extrait_0.3.xsd"
				exclude-result-prefixes="i18n infg xsi xs infogreffe">
	<!-- VERSION 202501 - XSD Version (0.32 - 0.10) -->
	<!-- on utilise une forme xml pour tous les caracteres accentues car eclipse, wincvs, winmerge et autres generent trop souvent
	des erreurs d'encodage et puis ca permet de pouvoir changer le jeu sans faire de conversion
	&#232; = è  (e grave)
	&#233; = é  (e acute)
	&#244; = ô  (o circ)
	&#224; = à  (a grave)
	&#166; = :  (colon)
	&#176; = °  (deg)
	expression reguliere utilisee dans eclipse pour trouver les caracteres accentues ou abimes: [^a-zA-Z0-9!_,;\(\)\#\&\/\:\?\+ï¿½\-="<>.\s\@'"\$\[\]\|\*\{\}\%]
	-->
	<xsl:output method="xml" version="1.0" encoding="UTF-8" indent="yes"/>
	<xsl:template match="*|/">
		<xsl:apply-templates/>
	</xsl:template>
	<xsl:template match="text()|@*">
		<xsl:value-of select="."/>
	</xsl:template>
	<xsl:variable name="dateDuJour" select="current-date()"/>
	<xsl:variable name="dateDuJourAvecHeure" select="current-dateTime()"/>
	<xsl:variable name="dateDuJourSansSeparateur"
				  select="format-date(current-date(), '[Y0001][M01][D01]')"/>
	<xsl:variable name="greffes.TI" select="('XXXX')"/>
	<xsl:variable name="greffes.TJ"
				  select="('5751','5752','5753','6751','6752','6851','6852')"/>
	<xsl:variable name="greffes.TAE"
				  select="('1303','7202','8701','6901','5402','8401','8901','7501','2202','7606','9201','7803')"/>
	<xsl:variable name="greffes.TMC"
				  select="('9742','9711','9721','9731','9741','9712','9761')"/>
	<xsl:variable name="greffes.NC" select="('9812')"/>
	<xsl:variable name="greffes.Tahiti" select="('9871')"/>
	<xsl:variable name="statutDossier.BCD" select="string('BCD')"/>
	<xsl:variable name="statutDossier.AC" select="string('AC')"/>
	<xsl:variable name="statutDossier.A" select="string('A')"/>
	<xsl:variable name="statutDossier.B" select="string('B')"/>
	<xsl:variable name="statutDossier.C" select="string('C')"/>
	<xsl:variable name="statutDossier.D" select="string('D')"/>
	<xsl:variable name="statutDossier.M" select="string('M')"/>
	<xsl:variable name="statutDossier.P" select="string('P')"/>
	<xsl:variable name="statutDossier.I" select="string('I')"/>
	<xsl:variable name="typeInscription.P" select="string('P')"/>
	<xsl:variable name="typeInscription.S" select="string('S')"/>
	<xsl:variable name="statutRegistre.RCS" select="string('R.C.S.')"/>
	<xsl:variable name="statutRegistre.RSAC" select="string('R.S.A.C.')"/>
	<xsl:variable name="statutRegistre.RSEIRL" select="string('R.S.E.I.R.L.')"/>
	<xsl:variable name="etablissementDansRessortType.SIE">SIE</xsl:variable>
	<xsl:variable name="etablissementDansRessortType.SEP">SEP</xsl:variable>
	<xsl:variable name="etablissementDansRessortType.PRI">PRI</xsl:variable>
	<xsl:variable name="etablissementDansRessortType.autre">autre</xsl:variable>
	<xsl:variable name="formeJuridique.ZZZh">ZZZh</xsl:variable>
	<xsl:variable name="formeJuridique.EPICh">EPICh</xsl:variable>
	<xsl:variable name="naf.0000Z">0000Z</xsl:variable>
	<xsl:variable name="intervenantType.INDIVISAIRE">INDIVISAIRE</xsl:variable>
	<xsl:variable name="intervenantType.LOUEUR">LOUEUR</xsl:variable>
	<xsl:variable name="intervenantType.PROPRIO_INDIVIS">PROPRIO_INDIVIS</xsl:variable>
	<xsl:variable name="intervenantType.PRECEDENT_EXPLOIT">PRECEDENT_EXPLOIT</xsl:variable>
	<xsl:variable name="intervenantType.PRECEDENT_PROPRIO">PRECEDENT_PROPRIO</xsl:variable>
	<xsl:variable name="intervenantType.PRECEDENT_PROEXP">PRECEDENT_PROEXP</xsl:variable>
	<xsl:variable name="intervenantType.PERS_APPUI">PERS_APPUI</xsl:variable>
	<xsl:variable name="intervenantType.MANDANT">MANDANT</xsl:variable>
	<xsl:variable name="intervenantType.MANDATAIRE">MANDATAIRE</xsl:variable>
	<xsl:variable name="intervenantType.HERIT">HERIT</xsl:variable>
	<xsl:variable name="intervenantType.PERS_ENGAGE_ETS">PERS_ENGAGE_ETS</xsl:variable>
	<xsl:variable name="intervenantType.PERS_APRES_DECES">PERS_APRES_DECES</xsl:variable>
	<xsl:variable name="intervenantType.FONDE_POUVOIRS">FONDE_POUVOIRS</xsl:variable>
	<xsl:variable name="intervenantType.CEDANT">CEDANT</xsl:variable>
	<xsl:variable name="fonctionQualCode.ADMh">ADMh</xsl:variable>
	<xsl:variable name="fonctionQualCode.MEMCONSURh">MEMCONSURh</xsl:variable>
	<xsl:variable name="fonctionQualCode.GERh">GERh</xsl:variable>
	<xsl:variable name="fonctionQualCode.SOCGESh">SOCGESh</xsl:variable>
	<xsl:variable name="fonctionQualCode.PREh">PREh</xsl:variable>
	<xsl:variable name="fonctionCode.LIQh">LIQh</xsl:variable>
	<xsl:variable name="fonctionCode.CACTITh">CACTITh</xsl:variable>
	<xsl:variable name="fonctionCode.CACSUPh">CACSUPh</xsl:variable>
	<xsl:variable name="fonctionCode.MEMh">MEMh</xsl:variable>
	<xsl:variable name="capaciteEngagerSociete.INCONNU">INCONNU</xsl:variable>
	<xsl:variable name="capaciteEngagerSociete.NON">NON</xsl:variable>
	<xsl:variable name="capaciteEngagerSociete.SEUL">SEUL</xsl:variable>
	<xsl:variable name="capaciteEngagerSociete.CONJOINTEMENT">CONJOINTEMENT</xsl:variable>
	<xsl:variable name="etatProcedure.AA">AA</xsl:variable>
	<xsl:variable name="etatProcedure.A2">A2</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.CAPE">CAPE</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.DIVERS">DIVERS</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.INSAISISSABILITE">INSAISISSABILITE</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.RESTRUCTURATION">RESTRUCTURATION</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.JUDICIAIRE">JUDICIAIRE</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.RADIATION">RADIATION</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.FORME_EPIC">FORME_EPIC</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.CAPITAL">CAPITAL</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.LEGISLATION">LEGISLATION</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.DOMICILIATION">DOMICILIATION</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.ACTE_EPIC">ACTE_EPIC</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.FS_RESULT">FS_RESULT</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.FS_AUGM">FS_AUGM</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.FUS_SCIS">FUS_SCIS</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.CESS_PARTIELLE">CESS_PARTIELLE</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.CESS_TOTALE">CESS_TOTALE</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.AUTORIS_DIPLOME">AUTORIS_DIPLOME</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.DISSOLUTION">DISSOLUTION</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.DECES">DECES</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.TUTELLE_CURATELLE">TUTELLE_CURATELLE</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.INTERNET">INTERNET</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.EIRL">EIRL</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.SANCTION">SANCTION</xsl:variable>
	<xsl:variable name="mentionTypeAggrege.CARENCE_GERANCE">CARENCE_GERANCE</xsl:variable>
	<xsl:variable name="origineFondType.CES">CES</xsl:variable>
	<xsl:variable name="origineFondType.DIV">DIV</xsl:variable>
	<xsl:variable name="origineFondType.ACH">ACH</xsl:variable>
	<xsl:variable name="origineFondType.ACB">ACB</xsl:variable>
	<xsl:variable name="origineFondType.ASB">ASB</xsl:variable>
	<xsl:variable name="origineFondType.LIC">LIC</xsl:variable>
	<xsl:variable name="origineFondType.PAR">PAR</xsl:variable>
	<xsl:variable name="origineFondType.TAD">TAD</xsl:variable>
	<xsl:variable name="origineFondType.LAC">LAC</xsl:variable>
	<xsl:variable name="origineFondType.AFS">AFS</xsl:variable>
	<xsl:variable name="modeExploitation.DIV">DIV</xsl:variable>
	<xsl:variable name="motifRadiation.R_TRANS">R_TRANS</xsl:variable>
	<xsl:variable name="motifRadiation.R_CF_OBS">R_CF_OBS</xsl:variable>
	<xsl:variable name="formeJuridique.SAh">SAh</xsl:variable>
	<xsl:variable name="formeJuridique.SACAh">SACAh</xsl:variable>
	<xsl:variable name="formeJuridique.SAOSh">SAOSh</xsl:variable>
	<xsl:variable name="formeJuridique.SAPOh">SAPOh</xsl:variable>
	<xsl:variable name="formeJuridique.SAPh">SAPh</xsl:variable>
	<xsl:variable name="formeJuridique.SAEMh">SAEMh</xsl:variable>
	<xsl:variable name="formeJuridique.SAHLMh">SAHLMh</xsl:variable>
	<xsl:variable name="formeJuridique.SAHLMCh">SAHLMCh</xsl:variable>
	<xsl:variable name="formeJuridique.SASPh">SASPh</xsl:variable>
	<xsl:variable name="formeJuridique.GIEh">GIEh</xsl:variable>
	<xsl:variable name="formeJuridique.GIEVh">GIEVh</xsl:variable>
	<xsl:variable name="formeJuridique.SADIRh">SADIRh</xsl:variable>
	<xsl:variable name="formeJuridique.SAHLMDh">SAHLMDh</xsl:variable>
	<xsl:variable name="formeJuridique.GEIEh">GEIEh</xsl:variable>
	<xsl:variable name="formeJuridique.SPPICAVh">SPPICAVh</xsl:variable>
	<xsl:variable name="formeJuridique.SPPICAVCAh">SPPICAVCAh</xsl:variable>
	<xsl:variable name="formeJuridique.SPPICAVASh">SPPICAVASh</xsl:variable>
	<xsl:variable name="formeJuridique.COIFAh">COIFAh</xsl:variable>
	<xsl:variable name="formeJuridique.COIFAVh">COIFAVh</xsl:variable>
	<xsl:variable name="formeJuridique.COICAFAVh">COICAFAVh</xsl:variable>
	<xsl:variable name="formeJuridique.COIHLMFAh">COIHLMFAh</xsl:variable>
	<xsl:variable name="formeJuridique.COIHLMFAVh">COIHLMFAVh</xsl:variable>
	<xsl:variable name="formeJuridique.COIFADVh">COIFADVh</xsl:variable>
	<xsl:variable name="formeJuridique.COIFACh">COIFACh</xsl:variable>
	<xsl:variable name="formeJuridique.SPLh">SPLh</xsl:variable>
	<xsl:variable name="formeJuridique.SPLAh">SPLAh</xsl:variable>
	<xsl:variable name="formeJuridique.SPLAFAh">SPLAFAh</xsl:variable>
	<!-- I18N -->
	<xsl:variable name="vLang_default">fr</xsl:variable>
	<xsl:param name="pLang">fr</xsl:param>
	<xsl:variable name="i18n">
		<i18n:hash-list lang="fr">
			<!-- Header -->
			<i18n:string key="greffe-libelle">Greffe du Tribunal de Commerce</i18n:string>
			<i18n:string key="greffe-libelle-TI">Greffe du Tribunal d'Instance</i18n:string>
			<!-- XAS 20170819 AN2017_INTEGRATION_STRASBOURG_MULHOUSE TI/TMC-->
			<i18n:string key="greffe-libelle-TJ">Greffe du Tribunal Judiciaire</i18n:string>
			<!-- XAS 20191219 ajout du libelle Tribunal Judiciaire  -->
			<i18n:string key="greffe-libelle-TMC">Greffe du Tribunal Mixte de Commerce</i18n:string>
			<i18n:string key="greffe-libelle-TAE">Greffe du Tribunal des Activités Économiques</i18n:string>
			<i18n:string key="greffe-libelle2-NC">Direction des Affaires Economiques</i18n:string>
			<i18n:string key="greffe-libelle-NC">Gouvernement</i18n:string>
			<i18n:string key="greffe-nom-NC">de Nouvelle Calédonie</i18n:string>
			<i18n:string key="dateFraicheurMsg">à jour au</i18n:string>
			<i18n:string key="numeroGestion">N° de gestion</i18n:string>
			<i18n:string key="codeVerif">Code de vérification</i18n:string>
			<!-- Footer -->
			<!-- libelles des titres du kbis -->
			<i18n:string key="titre-kbis">Extrait Kbis</i18n:string>
			<i18n:string key="titre-RCS">EXTRAIT D'IMMATRICULATION PRINCIPALE AU REGISTRE DU COMMERCE ET DES SOCIETES</i18n:string>
			<i18n:string key="titre-RCS-SEC">EXTRAIT D'IMMATRICULATION SECONDAIRE AU REGISTRE DU COMMERCE ET DES SOCIETES</i18n:string>
			<i18n:string key="titre-RSAC">EXTRAIT D'IMMATRICULATION AU REGISTRE SPECIAL DES AGENTS COMMERCIAUX</i18n:string>
			<i18n:string key="titre-RSEIRL">EXTRAIT D'IMMATRICULATION AU REGISTRE SPECIAL DES ENTREPRENEURS INDIVIDUELS A RESPONSABILITE LIMITEE</i18n:string>
			<!-- libelles des titres des section -->
			<i18n:string key="section-RenRelPersMoral" twoLine="false">RENSEIGNEMENTS RELATIFS A LA PERSONNE MORALE</i18n:string>
			<i18n:string key="section-RenRelPers" twoLine="false">RENSEIGNEMENTS RELATIFS A LA PERSONNE</i18n:string>
			<i18n:string key="section-GestionDirectionAdminCtrl" twoLine="false">GESTION, DIRECTION, ADMINISTRATION, CONTROLE, ASSOCIES OU MEMBRES</i18n:string>
			<i18n:string key="section-Administration" twoLine="false">ADMINISTRATION</i18n:string>
			<i18n:string key="section-IdentConjointPersonneLieePactCivilSolidariteCollaboreActiviteSocieteOuConjointConcubinPM"
						 twoLine="true">IDENTIFICATION DU CONJOINT, DE LA PERSONNE LIEE PAR UN PACTE CIVIL DE SOLIDARITE OU VIVANT EN CONCUBINAGE AVEC LE GERANT ASSOCIE UNIQUE OU GERANT ASSOCIE MAJORITAIRE, QUI EXERCE UNE ACTIVITE PROFESSIONNELLE REGULIERE DANS L'ENTREPRISE</i18n:string>
			<i18n:string key="section-IdentConjointPersonneLieePactCivilSolidariteCollaboreActiviteSocieteOuConjointConcubinPP"
						 twoLine="true">IDENTIFICATION DU CONJOINT, DE LA PERSONNE LIEE PAR UN PACTE CIVIL DE SOLIDARITE OU VIVANT EN CONCUBINAGE AVEC LE CHEF D'ENTREPRISE, QUI EXERCE UNE ACTIVITE PROFESSIONNELLE REGULIERE DANS L'ENTREPRISE</i18n:string>
			<i18n:string key="section-RenseignementsRelatifAffectationPatrimoine"
						 twoLine="false">RENSEIGNEMENTS RELATIFS A L'AFFECTATION DU PATRIMOINE</i18n:string>
			<i18n:string key="section-RenseignementsRelatifPrincipalEtablissementFrance"
						 twoLine="false">RENSEIGNEMENTS RELATIFS AU PRINCIPAL ETABLISSEMENT EN FRANCE</i18n:string>
			<i18n:string key="section-RenseignementsRelatifActivite" twoLine="false">RENSEIGNEMENTS RELATIFS A L'ACTIVITE</i18n:string>
			<i18n:string key="section-autreEtablissementRessort" twoLine="false">RENSEIGNEMENTS RELATIFS A L'AUTRE ETABLISSEMENT DANS LE RESSORT</i18n:string>
			<i18n:string key="section-autresEtablissementsRessort" twoLine="false">RENSEIGNEMENTS RELATIFS AUX AUTRES ETABLISSEMENTS DANS LE RESSORT</i18n:string>
			<i18n:string key="section-immatriculationHorsRessort" twoLine="false">IMMATRICULATION HORS RESSORT</i18n:string>
			<i18n:string key="section-immatriculationsHorsRessort" twoLine="false">IMMATRICULATIONS HORS RESSORT</i18n:string>
			<i18n:string key="section-etablissementsDansEtatMembreUninonEuropeenneOuEspaceEconomiqueEuropeen"
						 twoLine="false">ETABLISSEMENTS DANS UN ETAT MEMBRE DE L'UNION EUROPEENNE OU DE L'ESPACE ECONOMIQUE EUROPEEN</i18n:string>
			<i18n:string key="section-etablissementDansEtatMembreUninonEuropeenneOuEspaceEconomiqueEuropeen"
						 twoLine="false">ETABLISSEMENT DANS UN ETAT MEMBRE DE L'UNION EUROPEENNE OU DE L'ESPACE ECONOMIQUE EUROPEEN</i18n:string>
			<i18n:string key="section-autresMentionsObservations" twoLine="false">OBSERVATIONS ET RENSEIGNEMENTS COMPLEMENTAIRES</i18n:string>
			<!--i18n:string key="section-autreMentionObservation">AUTRE MENTION OU OBSERVATION</i18n:string-->
			<i18n:string key="section-mentionDeclarationInsaisissabilite" twoLine="false">MENTION D'UNE DECLARATION D'INSAISISSABILITE</i18n:string>
			<i18n:string key="section-mentionsOfficeRelativesProcedureCollective" twoLine="true">MENTIONS DES DECISIONS INTERVENUES DANS DES PROCEDURES DE SAUVEGARDE, DE REDRESSEMENT JUDICIAIRE, DE LIQUIDATION JUDICIAIRE, DE REGLEMENT JUDICIAIRE OU DE LIQUIDATION DES BIENS</i18n:string>
			<!--i18n:string key="section-mentionOfficeRelativeProcedureCollective">MENTION D'OFFICE RELATIVE A UNE PROCEDURE COLLECTIVE</i18n:string-->
			<i18n:string key="section-mentionsRelativesSocietesParticipeOperationFusionScissionApportPartielActif"
						 twoLine="true">MENTIONS RELATIVES AUX SOCIETES AYANT PARTICIPE A UNE OPERATION DE FUSION/SCISSION/APPORT PARTIEL D'ACTIF</i18n:string>
			<i18n:string key="section-societeResultantFusionScission" twoLine="false">SOCIETE RESULTANT D'UNE FUSION OU D'UNE SCISSION</i18n:string>
			<i18n:string key="section-fusionScissionAyantEntraineAugmentationCapital"
						 twoLine="false">FUSION(S) OU SCISSION(S) AYANT ENTRAINE UNE AUGMENTATION DE CAPITAL</i18n:string>
			<!--i18n:string key="section-mentionsRelativesSocietesParticipeOperationFusionScissionApportPartielActif">MENTIONS RELATIVES AUX SOCIETES AYANT PARTICIPE A UNE OPERATION DE FUSION/SCISSION/APPORT PARTIEL D'ACTIF</i18n:string>
			<i18n:string key="section-mentionRelativeSocietesParticipeOperationFusionScissionApportPartielActif">MENTION RELATIVE AUX SOCIETES AYANT PARTICIPE A UNE OPERATION DE FUSION/SCISSION/APPORT PARTIEL D'ACTIF</i18n:string-->
			<i18n:string key="section-radiation" twoLine="false">RADIATION</i18n:string>
			<i18n:string key="section-identificationPersonnePhysiqueCommercant" twoLine="false">IDENTIFICATION DE LA PERSONNE PHYSIQUE - COMMERCANT</i18n:string>
			<i18n:string key="section-identificationPersonnePhysique" twoLine="false">IDENTIFICATION DE LA PERSONNE PHYSIQUE</i18n:string>
			<i18n:string key="section-identification" twoLine="false">IDENTIFICATION DE LA PERSONNE MORALE</i18n:string>
			<i18n:string key="section-RenseignementsRelatifPremierEtablissementImmatriculeFranceActivite"
						 twoLine="false">RENSEIGNEMENTS RELATIFS A L'ACTIVITE ET AU PREMIER ETABLISSEMENT IMMATRICULE EN FRANCE</i18n:string>
			<i18n:string key="section-RenseignementsRelatifPremierEtablissementImmatriculePolynesieFrancaiseActivite"
						 twoLine="false">RENSEIGNEMENTS RELATIFS A L'ACTIVITE ET AU PREMIER ETABLISSEMENT IMMATRICULE EN POLYNESIE FRANCAISE</i18n:string>
			<i18n:string key="section-RenseignementsRelatifEtablissementPrincipalActivite"
						 twoLine="false">RENSEIGNEMENTS RELATIFS A L'ACTIVITE ET A L'ETABLISSEMENT PRINCIPAL </i18n:string>
			<i18n:string key="section-RenseignementsRelatifEtablissementSecondaireActivite"
						 twoLine="false">RENSEIGNEMENTS RELATIFS A L'ACTIVITE ET A L'ETABLISSEMENT SECONDAIRE</i18n:string>
			<i18n:string key="section-DissolutionDecisionPrononcantNullite" twoLine="false">DISSOLUTION OU DECISION PRONONCANT LA NULLITE</i18n:string>
			<i18n:string key="section-mesureProtectionEntrepreneurIndividuel" twoLine="false">MESURE(S) DE PROTECTION DE L'ENTREPRENEUR INDIVIDUEL</i18n:string>
			<!-- libelles des titres des balise information -->
			<i18n:string key="info-adresseSiege">Adresse du siège</i18n:string>
			<i18n:string key="info-euidSiegeFr">Numéro d’identification Européen - EUID</i18n:string>
			<i18n:string key="info-adressePro">Adresse professionnelle</i18n:string>
			<i18n:string key="info-immatGreffe">Immatriculé au Greffe de</i18n:string>
			<i18n:string key="info-regPubEtranger">Pays, lieu et numéro d'immatriculation</i18n:string>
			<i18n:string key="info-siegeFixRep">Siège installé au domicile du représentant légal</i18n:string>
			<i18n:string key="info-nomDenoDomiciliataire">Nom ou dénomination du domiciliataire</i18n:string>
			<!--i18n:string key="info-siegeFix">Siège fixé dans une entreprise de domiciliation</i18n:string-->
			<i18n:string key="info-domiciliationCommun">Domiciliation en commun</i18n:string>
			<i18n:string key="info-formJuridique">Forme juridique</i18n:string>
			<i18n:string key="info-formEntreprise">Forme de l'entreprise</i18n:string>
			<i18n:string key="info-capitalVar">Capital variable (minimum)</i18n:string>
			<i18n:string key="info-capital">Capital social</i18n:string>
			<i18n:string key="info-principalActiviteEntrep">Activités principales</i18n:string>
			<i18n:string key="info-naturePrincipActivite">Nature de(s) activité(s)</i18n:string>
			<i18n:string key="info-dateAgrement">Date d'agrément</i18n:string>
			<i18n:string key="info-dateClotureExcerciceSocial">Date de clôture de l'exercice social</i18n:string>
			<i18n:string key="info-dateCloturePremierExcerciceSocial">Date de clôture du 1er exercice social</i18n:string>
			<i18n:string key="info-dureePersonneMorale">Durée de la personne morale</i18n:string>
			<i18n:string key="info-constitution">Date d'immatriculation d'origine</i18n:string>
			<i18n:string key="info-depotActeConstitutifs">Dépôt d'actes constitutifs</i18n:string>
			<i18n:string key="info-journalAnnonceLegales">Journal d'annonces légales</i18n:string>
			<i18n:string key="info-transfertDu">Transfert du</i18n:string>
			<i18n:string key="info-transfertDe">Transfert de</i18n:string>
			<i18n:string key="info-transfertAu">Transfert au</i18n:string>
			<i18n:string key="info-enDateDu">en date du</i18n:string>
			<i18n:string key="info-depotActesTransfert">Dépôt d'actes de transfert</i18n:string>
			<i18n:string key="info-journalAnnoncesLegales">Journal d'annonces légales</i18n:string>
			<i18n:string key="info-contratAppuiCAPE">Contrat d'appui</i18n:string>
			<i18n:string key="info-numImmat">Immatriculation au RCS, numéro</i18n:string>
			<!-- since AN019_KBIS_DATE_DOMICILIATION-->
			<i18n:string key="info-nomDateContratDomiciliaire">Date du contrat </i18n:string>
			<i18n:string key="info-nomDateCessationDomiciliaire">Date de cessation de la domiciliation</i18n:string>
			<!-- 20170306 XAS Mantis 47712 Afficher le bon registre sur l'extrait -->
			<i18n:string key="info-numImmat-RCS">Immatriculation au RCS, numéro</i18n:string>
			<i18n:string key="info-numImmatPrincipale-RCS">Immatriculation principale au RCS, numéro</i18n:string>
			<i18n:string key="info-numImmat-RSAC">Immatriculation au RSAC, numéro</i18n:string>
			<i18n:string key="info-numImmat-RSEIRL">Immatriculation au RSEIRL, numéro</i18n:string>
			<i18n:string key="info-immatRCS">Immatriculation au RCS</i18n:string>
			<i18n:string key="info-numUniqueIdentification">Numéro unique d'identification</i18n:string>
			<i18n:string key="info-numIdentification">Numéro d'identification</i18n:string>
			<!-- PAPEETE -->
			<i18n:string key="info-numImmatEtranger">Numéro et lieu d'immatriculation</i18n:string>
			<i18n:string key="info-immatriculationHorsRessortUE">Pays, lieu et numéro d'immatriculation</i18n:string>
			<i18n:string key="info-adresse">Adresse</i18n:string>
			<i18n:string key="info-adresseEtabPrincipal">Adresse de l'établissement principal</i18n:string>
			<i18n:string key="info-adresseSiegeSocial">Adresse du siège social</i18n:string>
			<i18n:string key="info-denomination">Dénomination</i18n:string>
			<i18n:string key="info-denominationUtilisePourActivite">Dénomination utilisée pour l'activité</i18n:string>
			<i18n:string key="info-representePar">Représenté par</i18n:string>
			<i18n:string key="info-representantPermanent">Représentant permanent</i18n:string>
			<i18n:string key="info-personnePouvoir">Personne ayant le pouvoir de diriger, gérer ou engager à titre habituel</i18n:string>
			<i18n:string key="info-nomPrenoms">Nom, prénoms</i18n:string>
			<i18n:string key="info-pseudonyme">Pseudonyme</i18n:string>
			<i18n:string key="info-demeurant">Demeurant</i18n:string>
			<i18n:string key="info-domicile">Domicile</i18n:string>
			<i18n:string key="info-domicilePersonel">Domicile personnel</i18n:string>
			<i18n:string key="info-communeRattachement">Commune de rattachement</i18n:string>
			<i18n:string key="info-domicilePersonelOuAdressePro">Domicile personnel ou adresse professionnelle</i18n:string>
			<i18n:string key="info-declarationAffectationPatrimoine">Déclaration d'affectation du patrimoine</i18n:string>
			<i18n:string key="info-objetActiviteProfessionnelle">Objet de l'activité professionnelle</i18n:string>
			<i18n:string key="info-adresseExercice">Adresse de l'établissement principal où est exercé l'activité ou du local d'habitation où l'entreprise est fixée</i18n:string>
			<i18n:string key="info-dateClotureExerciceComptable">Date de clôture de l'exercice comptable</i18n:string>
			<i18n:string key="info-adresseProfessionnelle">Adresse professionnelle</i18n:string>
			<i18n:string key="info-dateCessationActivite">Date de cessation totale de l'activité</i18n:string>
			<i18n:string key="info-sansActiviteCompter">Sans activité à compter du</i18n:string>
			<i18n:string key="info-cessationActivite">Cessation totale d'activité</i18n:string>
			<i18n:string key="info-activitePrincipale">Activité principale</i18n:string>
			<i18n:string key="info-denominationRaisonSociale">Dénomination ou raison sociale</i18n:string>
			<i18n:string key="info-raisonSocialeDenomination">Raison sociale ou dénomination</i18n:string>
			<i18n:string key="info-denominationSociale">Dénomination sociale</i18n:string>
			<i18n:string key="info-sigle">Sigle</i18n:string>
			<i18n:string key="info-nomDeDomaine">Nom de domaine du ou des sites Internet</i18n:string>
			<i18n:string key="info-dateImmat">Date d'immatriculation</i18n:string>
			<i18n:string key="info-dateImmatOrigine">Date d'immatriculation d'origine</i18n:string>
			<i18n:string key="info-dateRadiation">Immatriculation radiée le</i18n:string>
			<i18n:string key="info-dateRadiation2">Date de radiation</i18n:string>
			<i18n:string key="info-nomUsage">Nom d'usage</i18n:string>
			<i18n:string key="info-deces">Décès</i18n:string>
			<i18n:string key="info-nationalite">Nationalité</i18n:string>
			<i18n:string key="info-mention">Mention</i18n:string>
			<i18n:string key="info-adresseEtablissement">Adresse de l'établissement</i18n:string>
			<i18n:string key="info-SiretNaf">SIRET - NAF</i18n:string>
			<i18n:string key="info-Siret">SIRET</i18n:string>
			<i18n:string key="info-Naf">Nomenclature d'activités française (code NAF)</i18n:string>
			<i18n:string key="info-nomCommercial">Nom commercial</i18n:string>
			<i18n:string key="info-enseigne">Enseigne</i18n:string>
			<i18n:string key="info-nomDeDomaineEtablissement">Nom de domaine du site Internet</i18n:string>
			<i18n:string key="info-activiteExercees">Activité(s) exercée(s)</i18n:string>
			<i18n:string key="info-activiteExerceesDansEtablissement">Activité(s) exercée(s) dans l'établissement</i18n:string>
			<i18n:string key="info-brancheActivite">Branche d'activité</i18n:string>
			<i18n:string key="info-typeActivite">Type d'activité</i18n:string>
			<i18n:string key="info-dateDebutActivite">Date de commencement d'activité</i18n:string>
			<i18n:string key="info-origineFondsActivite">Origine du fonds ou de l'activité</i18n:string>
			<i18n:string key="info-dateLieuNaissance">Date et lieu de naissance</i18n:string>
			<i18n:string key="info-dateNaissance">Date de naissance</i18n:string>
			<i18n:string key="info-lieuNaissance">Lieu de naissance</i18n:string>
			<i18n:string key="info-conjointCollaborateur">Conjoint collaborateur</i18n:string>
			<i18n:string key="info-partenairePacseCollaborateur">Partenaire pacsé collaborateur</i18n:string>
			<i18n:string key="info-conjointCollaborateurPartenairePacseCollaborateur">Conjoint collaborateur ou partenaire pacsé collaborateur</i18n:string>
			<i18n:string key="info-modeExploitation">Mode d'exploitation</i18n:string>
			<i18n:string key="info-denoSocialePersonneResponsable">Dénomination sociale de la personne responsable de l'appui</i18n:string>
			<i18n:string key="info-communeActiviteRessortissantsUnionEuropeenneOuEeeAmbulante">Commune ou s'exerce l'activité principale </i18n:string>
			<i18n:string key="info-nomJournalAnnonceLegale">Nom du journal d'annonces légales</i18n:string>
			<i18n:string key="info-dateParution">Date de parution</i18n:string>
			<i18n:string key="info-informationConcernant">Informations concernant le (ou l' ou la )</i18n:string>
			<i18n:string key="info-designationCedant">Désignation du cédant</i18n:string>
			<i18n:string key="info-rcsVilleDuGreffe">RCS ville du greffe</i18n:string>
			<i18n:string key="info-motifRadiation">Motif de la radiation</i18n:string>
			<i18n:string key="info-dateExpirationIncapaciteInterdiction">Date d'expiration de l'incapacité ou de l'interdiction</i18n:string>
			<i18n:string key="info-tutelleCuratelle">Tutelle ou curatelle</i18n:string>
			<i18n:string key="info-declarationInsaisissabilite">Déclaration d'insaisissabilité</i18n:string>
			<i18n:string key="info-nomDomaineSiteInternet">Nom de domaine du site internet</i18n:string>
			<i18n:string key="info-immatriculationRepertoireMetiers">Immatriculation au répertoire des métiers</i18n:string>
			<i18n:string key="info-declarationAffectationPatrimoineDeposeeGreffe">Déclaration d'affectation du patrimoine déposée au greffe de </i18n:string>
			<i18n:string key="info-dateContrat">Date du contrat</i18n:string>
			<i18n:string key="info-mesuresIncapaciteInterfiction">Mesures d'incapacité ou d'interdiction</i18n:string>
			<i18n:string key="info-absenceRepLegal">Absence de représentant légal. Voir in fine, rubrique "observations et renseignements complémentaires".</i18n:string>
			<i18n:string key="info-AttentePieceCapacite">En attente de la production de la pièce justifiant de la capacité</i18n:string>
			<i18n:string key="info-titreVide"> </i18n:string>
			<!-- libelles des balise information -->
			<i18n:string key="libelle-dateContratDebut">Début</i18n:string>
			<i18n:string key="libelle-dateContratTerme">Terme</i18n:string>
			<i18n:string key="libelle-numImmat">N° d'immatriculation</i18n:string>
			<i18n:string key="libelle-capitauxPropres">Non dissolution malgré des capitaux propres inférieurs à la moitié du capital social</i18n:string>
			<i18n:string key="libelle-decisionDu">Décision du</i18n:string>
			<i18n:string key="libelle-mentionDu">Mention du</i18n:string>
			<i18n:string key="libelle-sansActiviteImmat">Sans activité depuis l'immatriculation</i18n:string>
			<i18n:string key="libelle-sansExercerActivite">Personne morale immatriculée sans exercer d'activité</i18n:string>
			<i18n:string key="libelle-civile">Civile</i18n:string>
			<i18n:string key="libelle-commerciale">Commerciale</i18n:string>
			<i18n:string key="libelle-dureeIllimitee">Durée illimitée</i18n:string>
			<i18n:string key="libelle-auGreffe">Au greffe du Tribunal de Commerce</i18n:string>
			<i18n:string key="libelle-auGreffeInstance">Au greffe du Tribunal d'Instance</i18n:string>
			<!-- XAS 20170819 AN2017_INTEGRATION_STRASBOURG_MULHOUSE TI/TMC-->
			<i18n:string key="libelle-rectifierLe">rectifié le</i18n:string>
			<i18n:string key="libelle-RCS" name="R.C.S.">R.C.S.</i18n:string>
			<i18n:string key="libelle-RSAC" name="R.S.A.C.">R.S.A.C.</i18n:string>
			<i18n:string key="libelle.RSEIRL" name="R.S.E.I.R.L.">R.S.E.I.R.L.</i18n:string>
			<i18n:string key="libelle-societeAssocieUnique">Société à associé unique</i18n:string>
			<i18n:string key="libelle-capaciteEngagerSocieteNon">Le représentant ne peut pas engager la société vis à vis de tiers</i18n:string>
			<i18n:string key="libelle-capaciteEngagerSocieteSeul">Ayant pouvoir d'engager seul la société</i18n:string>
			<i18n:string key="libelle-capaciteEngagerSocieteConjointement">Ayant pouvoir d'engager conjointement la société</i18n:string>
			<i18n:string key="libelle-depotALa">Dépôt à la</i18n:string>
			<i18n:string key="libelle-depotGreffeTribunalCommerce">Dépôt au Greffe du Tribunal de Commerce</i18n:string>
			<i18n:string key="libelle-depotGreffeTribunalInstance">Dépôt au Greffe du Tribunal d'Instance</i18n:string>
			<!-- XAS 20170819 AN2017_INTEGRATION_STRASBOURG_MULHOUSE TI/TMC-->
			<i18n:string key="libelle-depotGreffeTribunalMixteCommerce">Dépôt au Greffe du Tribunal Mixte de Commerce</i18n:string>
			<!-- XAS 20170819 AN2017_INTEGRATION_STRASBOURG_MULHOUSE TI/TMC-->
			<i18n:string key="libelle-depotGreffeTribunal">Dépôt au Greffe du Tribunal</i18n:string>
			<!-- XAS 20170819 AN2017_INTEGRATION_STRASBOURG_MULHOUSE TI/TMC-->
			<i18n:string key="libelle-depotAu">Dépôt au</i18n:string>
			<i18n:string key="libelle-sousNum">sous le n°</i18n:string>
			<i18n:string key="libelle-etablissementPrincipal">Etablissement principal</i18n:string>
			<i18n:string key="libelle-societeLiquidationAimable">société en liquidation amiable</i18n:string>
			<i18n:string key="libelle-societeLiquidationJudiciaire">société en liquidation judiciaire</i18n:string>
			<i18n:string key="libelle-enCoursAttribution">En cours d'attribution</i18n:string>
			<i18n:string key="libelle-avecEffetAu">avec effet au</i18n:string>
			<i18n:string key="libelle-adresseEntreprise">adresse de l'entreprise</i18n:string>
			<i18n:string key="libelle-siegeSocial">siège social</i18n:string>
			<i18n:string key="libelle-transfereeDansRessortRcs">transférée dans le ressort du R.C.S.</i18n:string>
			<i18n:string key="libelle-transfereDansRessortRcs">tranferé dans le ressort du R.C.S.</i18n:string>
			<i18n:string key="libelle-incapaciteIntediction">Décision d'incapacité ou d'interdiction</i18n:string>
			<i18n:string key="libelle-enDateDu">en date du</i18n:string>
			<i18n:string key="libelle-pourUneDureeDe">pour une durée de</i18n:string>
			<i18n:string key="libelle-domicileRepresentantLegalDomicilProvisoire">Au domicile du représentant légal (domiciliation provisoire)</i18n:string>
			<i18n:string key="libelle-activiteExcerceeDansCetEtablissementIdentiquePrincipalesActivitesEntreprise">L'activité exercée dans cet établissement est identique aux principales activités de l'entreprise</i18n:string>
			<i18n:string key="libelle-activiteSaisonniere">Activité Saisonnière</i18n:string>
			<i18n:string key="libelle-activiteAmbulante">Activité ambulante</i18n:string>
			<i18n:string key="libelle-activiteNonSedentaire">Activité non sédentaire</i18n:string>
			<i18n:string key="libelle-paruAuJournal">paru au journal</i18n:string>
			<i18n:string key="libelle-rectifieLe">rectifié le</i18n:string>
			<i18n:string key="libelle-nomDUsage">nom d'usage</i18n:string>
			<i18n:string key="libelle-NomDUsage">Nom d'usage</i18n:string>
			<i18n:string key="libelle-pseudonyme">pseudonyme</i18n:string>
			<i18n:string key="libelle-ne(e)Le">né(e) le</i18n:string>
			<i18n:string key="libelle-ne(e)En">né(e) en</i18n:string>
			<i18n:string key="libelle-demeurant">demeurant</i18n:string>
			<i18n:string key="libelle-nationalite">nationalité</i18n:string>
			<i18n:string key="libelle-renouvlableTaciteReconduction">Contrat renouvelable par tacite reconduction</i18n:string>
			<i18n:string key="libelle-indefinimentRenouvlable">Contrat indéfiniment renouvelable</i18n:string>
			<i18n:string key="libelle-FormeEntreprise">Forme de l'entreprise</i18n:string>
			<i18n:string key="libelle-legislationApplicable">Législation applicable</i18n:string>
			<i18n:string key="libelle-publicationJournalOfficiel">Publication(s) au journal officiel</i18n:string>
			<i18n:string key="libelle-identifiacationSocietesParticipeOperation">Identification des sociétés ayant participées à l'opération</i18n:string>
			<i18n:string key="libelle-cessationPartielleActiviteExercee">Cessation partielle d'activité exercée</i18n:string>
			<i18n:string key="libelle-conditionsExercice">Conditions d'exercice</i18n:string>
			<i18n:string key="libelle-conditionExercice">Condition d'exercice</i18n:string>
			<i18n:string key="libelle-personneAyantPouvoirEngagerResponsabilitePersonneMorale">Personne ayant le pouvoir d'engager la responsabilité de la personne morale</i18n:string>
			<i18n:string key="libelle-personnesAyantPouvoirEngagerResponsabilitePersonneMorale">Personnes ayant le pouvoir d'engager la responsabilité de la personne morale</i18n:string>
			<i18n:string key="libelle-personneAyantPouvoirEngagerResponsabilitePersonnePhysique">Personne ayant le pouvoir d'engager la responsabilité de la personne physique</i18n:string>
			<i18n:string key="libelle-personnesAyantPouvoirEngagerResponsabilitePersonneMorale">Personnes ayant le pouvoir d'engager la responsabilité de la personne morale</i18n:string>
			<i18n:string key="libelle-infomationsConcernantContrat">Infomations concernant le contrat</i18n:string>
			<i18n:string key="libelle-refEtabPrincipauxSecondairesSituesImmatDansUEOuEEE">Etablissements situés et immatriculés dans un autre Etat membre de l’Union européenne ou de l’EEE</i18n:string>
			<i18n:string key="libelle-entrepreneurIndividuelResponsabiliteLimitee">Entrepreneur individuel à responsabilité limitée (EIRL)</i18n:string>
			<i18n:string key="libelle-renonciationDeclarationAffectationPatrimoine">Renonciation à la déclaration d'affectation du patrimoine</i18n:string>
			<i18n:string key="libelle-audomicileRepresentantLegalDomiciliationProvisoire">Au domicile du représentant légal (domiciliation provisoire)</i18n:string>
			<i18n:string key="libelle-entrepriseEconomieSocialeSolidaire">Entreprise de l'économie sociale et solidaire</i18n:string>
			<i18n:string key="libelle-societeAMission">Société à mission</i18n:string>
			<i18n:string key="libelle-neantobs">NEANT - Voir observation</i18n:string>
			<i18n:string key="libelle-declarationAutorisationTitreDiplome">Déclaration, autorisation, titre ou diplôme</i18n:string>
			<i18n:string key="libelle-autorite">Autorité</i18n:string>
			<i18n:string key="libelle-dateDelivrance">Date de délivrance</i18n:string>
			<i18n:string key="libelle-dateEcheance">Date d'échéance</i18n:string>
			<i18n:string key="libelle-occultationMsg">Confidentialité des informations relatives à l’adresse personnelle en application de l’article R. 123-54-1 du code de commerce</i18n:string>
			<!-- libelles commune -->
			<i18n:string key="common-a">à</i18n:string>
			<i18n:string key="common-au">au</i18n:string>
			<i18n:string key="common-du">du</i18n:string>
			<i18n:string key="common-Du">Du</i18n:string>
			<i18n:string key="common-de">de</i18n:string>
			<i18n:string key="common-d">d'</i18n:string>
			<i18n:string key="common-le">le</i18n:string>
			<i18n:string key="common-En">En</i18n:string>
			<i18n:string key="common-Le">Le</i18n:string>
			<i18n:string key="common-numero">N°</i18n:string>
			<i18n:string key="common-min-numero">n°</i18n:string>
			<i18n:string key="common-jusquau">Jusqu'au</i18n:string>
			<i18n:string key="common-tiret">-</i18n:string>
			<i18n:string key="common-deuxpoint">:</i18n:string>
			<i18n:string key="common-mois">mois</i18n:string>
			<i18n:string key="common-1er">1er</i18n:string>
			<i18n:string key="common-eme">ème</i18n:string>
			<i18n:string key="common-Date">Date</i18n:string>
			<!-- libelles des mois -->
			<i18n:string key="common-janvier" code="1">janvier</i18n:string>
			<i18n:string key="common-fevrier" code="2">février</i18n:string>
			<i18n:string key="common-mars" code="3">mars</i18n:string>
			<i18n:string key="common-avril" code="4">avril</i18n:string>
			<i18n:string key="common-mai" code="5">mai</i18n:string>
			<i18n:string key="common-juin" code="6">juin</i18n:string>
			<i18n:string key="common-juillet" code="7">juillet</i18n:string>
			<i18n:string key="common-aout" code="8">août</i18n:string>
			<i18n:string key="common-septembre" code="9">septembre</i18n:string>
			<i18n:string key="common-octobre" code="10">octobre</i18n:string>
			<i18n:string key="common-novembre" code="11">novembre</i18n:string>
			<i18n:string key="common-decembre" code="12">décembre</i18n:string>
		</i18n:hash-list>
	</xsl:variable>
	<xsl:function name="i18n:getText">
		<xsl:param name="key"/>
		<xsl:param name="lang"/>
		<xsl:choose>
			<xsl:when test="$i18n/i18n:hash-list[@lang = $lang]/i18n:string[@key = $key]">
				<xsl:value-of select="$i18n/i18n:hash-list[@lang = $lang]/i18n:string[@key = $key]"/>
			</xsl:when>
			<xsl:when test="$i18n/i18n:hash-list[@lang = $vLang_default]/i18n:string[@key = $key]">
				<xsl:value-of select="$i18n/i18n:hash-list[@lang = $vLang_default]/i18n:string[@key = $key]"/>
			</xsl:when>
			<xsl:otherwise>
				!!-- <xsl:value-of select="$key"/> --!!
			</xsl:otherwise>
		</xsl:choose>
	</xsl:function>
	<xsl:function name="i18n:getTextByName">
		<xsl:param name="name"/>
		<xsl:param name="lang"/>
		<xsl:choose>
			<xsl:when test="$i18n/i18n:hash-list[@lang = $lang]/i18n:string[@name = $name]">
				<xsl:value-of select="$i18n/i18n:hash-list[@lang = $lang]/i18n:string[@name = $name]"/>
			</xsl:when>
			<xsl:when test="$i18n/i18n:hash-list[@lang = $vLang_default]/i18n:string[@name = $name]">
				<xsl:value-of select="$i18n/i18n:hash-list[@lang = $vLang_default]/i18n:string[@name = $name]"/>
			</xsl:when>
			<xsl:otherwise>
				!!-- <xsl:value-of select="$name"/> --!!
			</xsl:otherwise>
		</xsl:choose>
	</xsl:function>
	<xsl:function name="i18n:getTextTwoLine">
		<xsl:param name="key"/>
		<xsl:param name="lang"/>
		<xsl:choose>
			<xsl:when test="$i18n/i18n:hash-list[@lang = $lang]/i18n:string[@key = $key]/@twoLine">
				<xsl:value-of select="$i18n/i18n:hash-list[@lang = $lang]/i18n:string[@key = $key]/@twoLine"/>
			</xsl:when>
			<xsl:when test="$i18n/i18n:hash-list[@lang = $vLang_default]/i18n:string[@key = $key]/@twoLine">
				<xsl:value-of select="$i18n/i18n:hash-list[@lang = $vLang_default]/i18n:string[@key = $key]/@twoLine"/>
			</xsl:when>
			<xsl:otherwise>
				<xsl:value-of select="false()"/>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:function>
	<xsl:function name="i18n:getTextByCode">
		<xsl:param name="code"/>
		<xsl:param name="lang"/>
		<xsl:choose>
			<xsl:when test="$i18n/i18n:hash-list[@lang = $lang]/i18n:string[@code = $code]">
				<xsl:value-of select="$i18n/i18n:hash-list[@lang = $lang]/i18n:string[@code = $code]"/>
			</xsl:when>
			<xsl:when test="$i18n/i18n:hash-list[@lang = $vLang_default]/i18n:string[@code = $code]">
				<xsl:value-of select="$i18n/i18n:hash-list[@lang = $vLang_default]/i18n:string[@code = $code]"/>
			</xsl:when>
			<xsl:otherwise>
				!!-- <xsl:value-of select="$code"/> --!!
			</xsl:otherwise>
		</xsl:choose>
	</xsl:function>
	<xsl:function name="i18n:getTextPL">
		<xsl:param name="key"/>
		<xsl:value-of select="i18n:getText($key, $pLang)"/>
	</xsl:function>
	<xsl:function name="i18n:getTextPLPrefSuff">
		<xsl:param name="prefix"/>
		<xsl:param name="key"/>
		<xsl:param name="suffix"/>
		<xsl:value-of disable-output-escaping="yes" select="$prefix"/>
		<xsl:value-of select="i18n:getText($key, $pLang)"/>
		<xsl:value-of disable-output-escaping="yes" select="$suffix"/>
	</xsl:function>
	<xsl:function name="i18n:getTextTwoLinePL">
		<xsl:param name="key"/>
		<xsl:value-of select="i18n:getTextTwoLine($key, $pLang)"/>
	</xsl:function>
	<xsl:function name="i18n:getTextByCodePL">
		<xsl:param name="code"/>
		<xsl:value-of select="i18n:getTextByCode($code, $pLang)"/>
	</xsl:function>
	<xsl:function name="i18n:getTextByNamePL">
		<xsl:param name="name"/>
		<xsl:value-of select="i18n:getTextByName($name, $pLang)"/>
	</xsl:function>
	<!-- Fin I18N -->
	<!-- Infogreffe Functions -->
	<xsl:decimal-format name="european" decimal-separator="," grouping-separator=" "/>
	<xsl:variable name="defaultDateFormatPattern">[D01]/[M01]/[Y0001]</xsl:variable>
	<xsl:variable name="headDateFormatPattern">[D1]/[M01]/[Y0001]</xsl:variable>
	<xsl:variable name="defaultDateHeureFormatPattern">[D01]/[M01]/[Y0001] - [H01]:[m01]:[s01]</xsl:variable>
	<xsl:variable name="defaultSirenFormatPattern">000 000 000</xsl:variable>
	<xsl:variable name="defaultSirenFormatPatternNC"># ##0</xsl:variable>
	<xsl:variable name="defaultChronoIdentifiantFormatPatternTahiti">####0</xsl:variable>
	<xsl:variable name="defaultMontantPattern"># ##0,00</xsl:variable>
	<xsl:function name="infg:format-date">
		<xsl:param name="date"/>
		<xsl:value-of select="format-date($date, $defaultDateFormatPattern)"/>
	</xsl:function>
	<xsl:function name="infg:format-dateHeure">
		<xsl:param name="date"/>
		<xsl:value-of select="format-dateTime($date, $defaultDateHeureFormatPattern)"/>
	</xsl:function>
	<xsl:function name="infg:format-siren">
		<xsl:param name="siren"/>
		<xsl:value-of select="format-number(number($siren), $defaultSirenFormatPattern, 'european')"/>
	</xsl:function>
	<xsl:function name="infg:format-siren-nc">
		<xsl:param name="siren"/>
		<xsl:value-of select="format-number(number($siren), $defaultSirenFormatPatternNC, 'european')"/>
	</xsl:function>
	<xsl:function name="infg:format-identifiant-tahiti">
		<xsl:param name="identifiant"/>
		<xsl:value-of select="substring($identifiant/infogreffe:NumeroGestion/infogreffe:Millesime, 3, 2)"/>
		<xsl:text> </xsl:text>
		<xsl:value-of select="format-number(number($identifiant/infogreffe:NumeroGestion/infogreffe:Chrono), $defaultChronoIdentifiantFormatPatternTahiti, 'european')"/>
		<xsl:text> </xsl:text>
		<xsl:value-of select="$identifiant/infogreffe:NumeroGestion/infogreffe:Statut"/>
	</xsl:function>
	<xsl:function name="infg:format-money">
		<xsl:param name="amount"/>
		<xsl:value-of select="format-number(number($amount), $defaultMontantPattern, 'european')"/>
	</xsl:function>
	<xsl:function name="infg:format-date-day-month-long">
		<xsl:param name="date"/>
		<xsl:variable name="dateSansTirets"
					  select="substring-after($date cast as xs:string,'--')"/>
		<xsl:value-of select="substring-after($dateSansTirets,'-')"/>
		<xsl:text> </xsl:text>
		<xsl:value-of select="infg:month-label-formatting(substring-before($dateSansTirets,'-'))"/>
	</xsl:function>
	<xsl:function name="infg:format-date-long">
		<xsl:param name="date"/>
		<xsl:variable name="day" select="substring($date, 1, 2)"/>
		<xsl:choose>
			<xsl:when test="starts-with($day , '0')">
				<xsl:value-of select="substring($date, 2, 1)"/>
			</xsl:when>
			<xsl:otherwise>
				<xsl:value-of select="$day"/>
			</xsl:otherwise>
		</xsl:choose>
		<xsl:text> </xsl:text>
		<xsl:value-of select="infg:month-label-formatting(substring($date, 4, 2))"/>
		<xsl:text> </xsl:text>
		<xsl:value-of select="substring($date, 7, 4)"/>
	</xsl:function>
	<xsl:function name="infg:month-label-formatting">
		<xsl:param name="month"/>
		<xsl:variable name="monthI18nCode" select="number($month)"/>
		<xsl:value-of select="i18n:getTextByCodePL($monthI18nCode)"/>
	</xsl:function>
	<xsl:function name="infg:in-list">
		<xsl:param name="value"/>
		<xsl:param name="list"/>
		<xsl:sequence select="boolean(index-of(distinct-values($list) , $value))"/>
	</xsl:function>
	<xsl:function name="infg:format-status-nomGreffe">
		<xsl:param name="statutRegistre"/>
		<xsl:param name="libelleGreffe"/>
		<xsl:value-of select="concat($statutRegistre , ' ')"/>
		<xsl:value-of select="infg:accord-greffe($libelleGreffe)"/>
	</xsl:function>
	<xsl:function name="infg:accord-greffe">
		<xsl:param name="libelleGreffe"/>
		<xsl:variable name="premiereLettre" select="substring($libelleGreffe, 1, 1)"/>
		<xsl:choose>
			<!-- Voyelle -->
			<xsl:when test="contains('AEIOUY', $premiereLettre)">
				<xsl:value-of select="i18n:getTextPL('common-d')"/>
				<xsl:value-of select="$libelleGreffe"/>
			</xsl:when>
			<xsl:otherwise>
				<xsl:variable name="prefixe" select="tokenize($libelleGreffe, '\s')[1]"/>
				<xsl:choose>
					<xsl:when test="lower-case($prefixe) = i18n:getTextPL('common-le')">
						<xsl:value-of select="i18n:getTextPLPrefSuff('','common-du', ' ')"/>
						<xsl:value-of select="substring($libelleGreffe, 4, string-length($libelleGreffe) - 3)"/>
					</xsl:when>
					<xsl:otherwise>
						<xsl:value-of select="i18n:getTextPLPrefSuff('','common-de', ' ')"/>
						<xsl:value-of select="$libelleGreffe"/>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:function>
	<xsl:function name="infg:upperFirstLetter">
		<xsl:param name="input"/>
		<xsl:variable name="accent"
					  select="'ŠŽšžŸÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖÙÚÛÜÝàáâãäåçèéêëìíîïðñòóôõöùúûüýÿ'"/>
		<xsl:variable name="sansAccent"
					  select="'SZszYAAAAAACEEEEIIIIDNOOOOOUUUUYaaaaaaceeeeiiiidnooooouuuuyy'"/>
		<xsl:sequence select="concat(upper-case(translate(substring($input,1,1), $accent, $sansAccent)),substring($input, 2))"/>
	</xsl:function>
	<xsl:function name="infg:toFreindlyUrlDeno">
		<xsl:param name="input"/>
		<xsl:variable name="accent"
					  select="'ŠŽšžŸÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖÙÚÛÜÝàáâãäåçèéêëìíîïðñòóôõöùúûüýÿ'"/>
		<xsl:variable name="sansAccent"
					  select="'SZszYAAAAAACEEEEIIIIDNOOOOOUUUUYaaaaaaceeeeiiiidnooooouuuuyy'"/>
		<xsl:sequence select="replace(translate($input, $accent, $sansAccent), '[^A-Za-z0-9]+', '-')"/>
	</xsl:function>
	<xsl:function name="infg:trimLeandingTowPoint">
		<xsl:param name="input"/>
		<xsl:sequence select="replace(replace($input,'^\s*(-|:)\s*',''),'^\s+','')"/>
	</xsl:function>
	<xsl:function name="infg:isTJ"> <!-- XAS 20191219 ajout du libelle Tribunal de justice -->
		<xsl:param name="numGreffe"/>
		<xsl:sequence select="infg:in-list($numGreffe, $greffes.TJ)"/>
	</xsl:function>
	<xsl:function name="infg:isTI">
		<xsl:param name="numGreffe"/>
		<xsl:sequence select="infg:in-list($numGreffe, $greffes.TI)"/>
	</xsl:function>
	<xsl:function name="infg:isTMC">
		<xsl:param name="numGreffe"/>
		<xsl:sequence select="infg:in-list($numGreffe, $greffes.TMC) or infg:isTahiti($numGreffe)"/>
	</xsl:function>
	<xsl:function name="infg:isTAE">
		<xsl:param name="numGreffe"/>
		<xsl:sequence select="infg:in-list($numGreffe, $greffes.TAE)"/>
	</xsl:function>
	<xsl:function name="infg:isNC">
		<xsl:param name="numGreffe"/>
		<xsl:choose> <!-- 20190328 XAS (pb test MNE)on test si le parametre est null auquel cas on retourne false -->
			<xsl:when test="$numGreffe">
				<xsl:sequence select="infg:in-list($numGreffe, $greffes.NC)"/>
			</xsl:when>
			<xsl:otherwise>
				<xsl:sequence select="boolean(false())"/>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:function>
	<xsl:function name="infg:isTahiti">
		<xsl:param name="numGreffe"/>
		<xsl:choose> <!-- 20190328 XAS (pb test MNE)on test si le parametre est null auquel cas on retourne false -->
			<xsl:when test="$numGreffe">
				<xsl:sequence select="infg:in-list($numGreffe, $greffes.Tahiti)"/>
			</xsl:when>
			<xsl:otherwise>
				<xsl:sequence select="boolean(false())"/>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:function>
	<xsl:function name="infg:isPP">
		<xsl:param name="statutDossier"/>
		<xsl:sequence select="boolean(($statutDossier = $statutDossier.A) or ($statutDossier = $statutDossier.P) or ($statutDossier = $statutDossier.I))"/>
	</xsl:function>
	<xsl:function name="infg:isPM">
		<xsl:param name="statutDossier"/>
		<xsl:sequence select="boolean(($statutDossier = $statutDossier.BCD) or ($statutDossier = $statutDossier.M))"/>
	</xsl:function>
	<xsl:function name="infg:hasObservationVisible">
		<xsl:param name="mentionType"/>
		<xsl:param name="cheminObservation"/>
		<xsl:sequence select="boolean(count(infg:selectObservationVisible($mentionType, $cheminObservation)) &gt; 0)"/>
	</xsl:function>
	<xsl:function name="infg:selectObservationVisible">
		<xsl:param name="mentionType"/>
		<xsl:param name="cheminObservation"/>
		<xsl:choose>
			<xsl:when test="$mentionType">
				<xsl:sequence select="$cheminObservation[infogreffe:TypeAgrege = $mentionType and infogreffe:RevelableExtraits = true() and empty(infogreffe:DateSuppression)]"/>
			</xsl:when>
			<xsl:otherwise>
				<xsl:sequence select="$cheminObservation[infogreffe:RevelableExtraits = true() and empty(infogreffe:DateSuppression)]"/>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:function>
	<xsl:function name="infg:nomPrenoms">
		<xsl:param name="identification"/>
		<xsl:variable name="nom">
			<xsl:choose>
				<xsl:when test="$identification/infogreffe:Nom/infogreffe:NomPatronymique">
					<xsl:value-of select="$identification/infogreffe:Nom/infogreffe:NomPatronymique"/>
				</xsl:when>
				<xsl:otherwise>
					<xsl:value-of select="$identification/infogreffe:Nom/infogreffe:NomUsage"/>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:value-of select="concat($nom, ' ' ,$identification/infogreffe:Prenoms)"/>
	</xsl:function>
	<!-- Fin Infogreffe Functions -->
	<xsl:template match="infogreffe:Dossier">
		<!-- Statut du dossier par statut (BCD, A, M, P ou I) -->
		<xsl:variable name="statutDossier">
			<xsl:choose>
				<xsl:when test="infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut = $statutDossier.B        or infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut = $statutDossier.C        or infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut = $statutDossier.D">
					<xsl:value-of select="$statutDossier.BCD"/>
				</xsl:when>
				<xsl:when test="infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut = $statutDossier.AC         and //infogreffe:PersonneRSAC/infogreffe:PersonneMorale">
					<xsl:value-of select="$statutDossier.M"/>
				</xsl:when>
				<xsl:when test="infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut = $statutDossier.AC         and //infogreffe:PersonneRSAC/infogreffe:PersonnePhysique">
					<xsl:value-of select="$statutDossier.P"/>
				</xsl:when>
				<xsl:otherwise>
					<xsl:value-of select="infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut"/>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<!-- Statut du dossier par registre (RCS, RSAC ou RSEIRL) -->
		<xsl:variable name="statutRegistre">
			<xsl:choose>
				<xsl:when test="infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut = $statutDossier.AC      or infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut = $statutDossier.M     or infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut = $statutDossier.P">
					<xsl:value-of select="$statutRegistre.RSAC"/>
				</xsl:when>
				<xsl:when test="infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut = $statutDossier.I">
					<xsl:value-of select="$statutRegistre.RSEIRL"/>
				</xsl:when>
				<xsl:otherwise>
					<xsl:value-of select="$statutRegistre.RCS"/>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<!-- Liste des observation -->
		<xsl:variable name="cheminObservation"
					  select="//infogreffe:Extrait/infogreffe:Observation"/>
		<xsl:element name="extrait">
			<!-- Variable du Greffe -->
			<xsl:variable name="libelleGreffe"
						  select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Libelle"/>
			<xsl:variable name="adresseGreffe"
						  select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Adresse"/>
			<!-- Variable personneType -->
			<xsl:variable name="personneType">
				<xsl:choose>
					<xsl:when test="$statutDossier = $statutDossier.BCD">infogreffe:PersonneRCS</xsl:when>
					<xsl:when test="$statutDossier = $statutDossier.A">infogreffe:PersonneRCS</xsl:when>
					<xsl:when test="$statutDossier = $statutDossier.M">infogreffe:PersonneRSAC</xsl:when>
					<xsl:when test="$statutDossier = $statutDossier.P">infogreffe:PersonneRSAC</xsl:when>
					<xsl:when test="$statutDossier = $statutDossier.I">infogreffe:PersonneRSEIRL</xsl:when>
				</xsl:choose>
			</xsl:variable>
			<!-- EN-TETE -->
			<xsl:call-template name="entete">
				<xsl:with-param name="Greffe"
								select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe"/>
				<xsl:with-param name="showKbisTitre" select="$statutRegistre = $statutRegistre.RCS"/>
				<xsl:with-param name="titreKey">
					<xsl:choose>
						<xsl:when test="$statutRegistre = $statutRegistre.RCS">
							<xsl:choose>
								<xsl:when test="infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.P">
									<xsl:text>titre-RCS</xsl:text>
								</xsl:when>
								<xsl:when test="infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.S">
									<xsl:text>titre-RCS-SEC</xsl:text>
								</xsl:when>
								<xsl:otherwise>
									<xsl:text>titre-RCS</xsl:text>
								</xsl:otherwise>
							</xsl:choose>
						</xsl:when>
						<xsl:when test="$statutRegistre = $statutRegistre.RSAC">titre-RSAC</xsl:when>
						<xsl:when test="$statutRegistre = $statutRegistre.RSEIRL">titre-RSEIRL</xsl:when>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="dateFraicheur" select="//infogreffe:DateFraicheur"/>
				<xsl:with-param name="URLImage" select="//infogreffe:URLImage"/>
				<xsl:with-param name="codeVerification" select="//infogreffe:CodeVerification"/>
				<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
			</xsl:call-template>
			<!-- FIN EN-TETE -->
			<!-- IDENTIFICATION -->
			<xsl:call-template name="identification">
				<xsl:with-param name="statutDossier" select="$statutDossier"/>
				<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="libelleGreffe">
					<xsl:choose>
						<xsl:when test="$statutRegistre = $statutRegistre.RCS and infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.S">
							<xsl:value-of select="infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Libelle"/>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Libelle"/>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:with-param>
			</xsl:call-template>
			<!-- GESTION, DIRECTION, ADMINISTRATION, CONTRÔLE pour ($statutDossier = 'BCD') or ($statutDossier = 'M') -->
			<xsl:call-template name="gestionDirectionAdmin">
				<xsl:with-param name="statutDossier" select="$statutDossier"/>
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="typeInscription"
								select="infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription"/>
			</xsl:call-template>
			<!-- IDENTIFICATION DU CONJOINT OU DE LA PERSONNE LIEE PAR UN PACTE CIVIL DE SOLIDARITE QUI COLLABORE A L'ACTIVITE DE LA SOCIETE -->
			<xsl:call-template name="conjointCollaborateur">
				<!-- xsl:with-param name="bloc" select="$information.bloc.indentBloc"/-->
				<xsl:with-param name="statutDossier" select="$statutDossier"/>
				<xsl:with-param name="conjointCollaborateur"
								select="//*[name() = 'infogreffe:PersonneMorale' or name() = 'infogreffe:PersonnePhysique']/infogreffe:ConjointCollaborateur[not(ancestor::infogreffe:Representant)] | //infogreffe:Representant[infogreffe:Actif = true()]/*[name() = 'infogreffe:PersonneMorale' or name() = 'infogreffe:PersonnePhysique']/infogreffe:ConjointCollaborateur"/>
				<xsl:with-param name="identificationAdresse"
								select="//*[name() = 'infogreffe:PersonneMorale' or name() = 'infogreffe:PersonnePhysique']/infogreffe:Identification//infogreffe:AdresseDomicile"/>
			</xsl:call-template>
			<!-- SOCIETE RESULTANT D'UNE FUSION OU D'UNE SCISSION pour ($statutDossier = 'BCD') -->
			<xsl:call-template name="fusionScission">
				<xsl:with-param name="statutDossier" select="$statutDossier"/>
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="mentionType" select="$mentionTypeAggrege.FS_RESULT"/>
				<xsl:with-param name="titreKey">section-societeResultantFusionScission</xsl:with-param>
			</xsl:call-template>
			<!-- FUSION OU D'UNE SCISSION AYANT ENTRAINE UNE AUGMENTATION DE CAPITAL pour ($statutDossier = 'BCD') -->
			<xsl:call-template name="fusionScission">
				<xsl:with-param name="statutDossier" select="$statutDossier"/>
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="mentionType" select="$mentionTypeAggrege.FS_AUGM"/>
				<xsl:with-param name="titreKey">section-fusionScissionAyantEntraineAugmentationCapital</xsl:with-param>
			</xsl:call-template>
			<!-- section et informations associées pour $statutRegistre = 'R.S.A.C.' uniquement -->
			<xsl:if test="($statutRegistre = $statutRegistre.RSAC)">
				<!-- RENSEIGNEMENTS RELATIFS A L'ACTIVITE  -->
				<section titre="{i18n:getTextPL('section-RenseignementsRelatifActivite')}">
					<!-- Adresse professionnelle -->
					<!-- pour $statutDossier = 'P' -->
					<!-- xsl:if test="($statutDossier = $statutDossier.P)" -->
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">adressePro</xsl:with-param>
						<xsl:with-param name="titreKey">info-adresseProfessionnelle</xsl:with-param>
						<xsl:with-param name="libelle">
							<xsl:call-template name="adresse">
								<xsl:with-param name="cheminAdresse"
												select="//infogreffe:PersonneRSAC/infogreffe:AdresseProfessionnelle"/>
							</xsl:call-template>
						</xsl:with-param>
					</xsl:call-template>
					<!--/xsl:if -->
					<!-- Cessation partielle d'activité exercée -->
					<xsl:call-template name="observationsSansTitre">
						<xsl:with-param name="ligne">cessationPartielleActiviteExercee</xsl:with-param>
						<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.CESS_PARTIELLE"/>
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					</xsl:call-template>
					<!-- Date de cessation d'activité -->
					<xsl:call-template name="cessationActivite">
						<xsl:with-param name="dateFin"
										select="//*[name() = $personneType]/infogreffe:Activite/infogreffe:DateCessationActivite"/>
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					</xsl:call-template>
					<!-- Siret - NAF -->
					<xsl:if test="//infogreffe:DonneesInsee/infogreffe:NAF/infogreffe:Code or //*[name() = $personneType]/infogreffe:Nic">
						<xsl:choose>
							<xsl:when test="//infogreffe:DonneesInsee/infogreffe:NAF/infogreffe:Code = $naf.0000Z">
								<xsl:call-template name="siretNAF">
									<xsl:with-param name="codeNIC" select="//*[name() = $personneType]/infogreffe:Nic"/>
								</xsl:call-template>
							</xsl:when>
							<xsl:otherwise>
								<xsl:call-template name="siretNAF">
									<xsl:with-param name="codeNAF"
													select="//infogreffe:DonneesInsee/infogreffe:NAF/infogreffe:Code"/>
									<xsl:with-param name="codeNIC" select="//*[name() = $personneType]/infogreffe:Nic"/>
								</xsl:call-template>
							</xsl:otherwise>
						</xsl:choose>
					</xsl:if>
					<xsl:call-template name="activite">
						<xsl:with-param name="statutDossier" select="$statutDossier"/>
						<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						<xsl:with-param name="nomCommercial"
										select="//*[name() = $personneType]/infogreffe:NomCommercial"/>
						<xsl:with-param name="enseigne"
										select="//*[name() = $personneType]/infogreffe:Enseigne"/>
						<xsl:with-param name="activite"
										select="//*[name() = $personneType]/infogreffe:ActivitesPrincipalesExercees"/>
						<xsl:with-param name="dateDebut"
										select="//*[name() = $personneType]/infogreffe:Activite/infogreffe:DateDebutActivite"/>
						<xsl:with-param name="nomDeDomaineEtablissement"
										select="//*[name() = $personneType]/infogreffe:SiteInternet"/>
						<xsl:with-param name="position" select="1"/>
					</xsl:call-template>
				</section>
			</xsl:if>
			<!-- sections et informations associées pour $statutRegistre = 'R.C.S.' uniquement -->
			<xsl:call-template name="etablissementRCS">
				<xsl:with-param name="statutDossier" select="$statutDossier"/>
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
				<xsl:with-param name="personneType" select="$personneType"/>
			</xsl:call-template>
			<!-- MESURE(S) DE PROTECTION DE L'ENTREPRENEUR INDIVIDUEL -->
			<xsl:call-template name="mesureProtectionEntrepreneurIndividuel">
				<xsl:with-param name="statutDossier" select="$statutDossier"/>
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="personneType" select="$personneType"/>
			</xsl:call-template>
			<!-- MENTION(S) D'OFFICE RELATIVE(S) A UNE PROCEDURE COLLECTIVE -->
			<xsl:call-template name="mentionOfficeRelativeProcedureCollective">
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
			</xsl:call-template>
			<!-- DISSOLUTION OU DECISION PRONONCANT LA NULLITE -->
			<xsl:call-template name="dissolutionDecisionPrononcantNullite">
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="mentionTypeAggrege.DISSOLUTION"
								select="$mentionTypeAggrege.DISSOLUTION"/>
			</xsl:call-template>
			<!-- MENTION(S) RADIATION -->
			<xsl:call-template name="mentionRadiation">
				<xsl:with-param name="personneType" select="$personneType"/>
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="statutDossier" select="$statutDossier"/>
			</xsl:call-template>
			<!-- AUTRE(S) MENTION(S) ET OBSERVATION(S) -->
			<xsl:call-template name="autresMentionObservation">
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="statutDossier" select="$statutDossier"/>
				<xsl:with-param name="typeInscription"
								select="infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription"/>
			</xsl:call-template>
			<!-- SIGNATURE -->
			<xsl:call-template name="signature">
				<xsl:with-param name="Greffe"
								select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe"/>
			</xsl:call-template>
			<!-- PIED DE PAGE -->
			<xsl:call-template name="piedDePage">
				<xsl:with-param name="dateDuJourAvecHeure" select="$dateDuJourAvecHeure"/>
				<xsl:with-param name="greffe"
								select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe"/>
				<xsl:with-param name="referenceGreffe" select="//infogreffe:ReferenceGreffe"/>
				<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
				<!-- 20170306 XAS Mantis 47712 Afficher le bon registre sur l'extrait -->
			</xsl:call-template>
			<!-- FIN PIED DE PAGE -->
		</xsl:element>
	</xsl:template>
	<!-- Some sections -->
	<xsl:template name="immatHorsRessort">
		<xsl:variable name="cheminInscriptionHorsRessortFrance"
					  select="//infogreffe:Extrait/infogreffe:Etablissement/infogreffe:InscriptionHorsRessortFrance"/>
		<xsl:call-template name="section">
			<xsl:with-param name="titreKey">
				<xsl:choose>
					<xsl:when test="count($cheminInscriptionHorsRessortFrance) = 1">
						<xsl:text>section-immatriculationHorsRessort</xsl:text>
					</xsl:when>
					<xsl:otherwise>
						<xsl:text>section-immatriculationsHorsRessort</xsl:text>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:with-param>
			<xsl:with-param name="informations">
				<xsl:for-each select="$cheminInscriptionHorsRessortFrance">
					<!-- Tri de la liste par Etablissements:InscriptionHorsRessortFrance:GreffeInscription:Code -->
					<xsl:sort select="infogreffe:GreffeInscription/infogreffe:Code"
							  order="ascending"
							  data-type="number"/>
					<!-- Sélection des inscriptions secondairesHRFrance ouvertes : Etablissements:InscriptionHorsRessort:supprime non renseigné -->
					<xsl:if test="not(infogreffe:Supprime) or infogreffe:Supprime = false()">
						<xsl:variable name="inscription">
							<xsl:value-of select="concat(i18n:getTextPL('libelle-RCS'), ' ', infogreffe:GreffeInscription/infogreffe:Libelle)"/>
						</xsl:variable>
						<xsl:variable name="sirenHR">
							<xsl:choose>
								<xsl:when test="infg:isNC(infogreffe:GreffeInscription/infogreffe:Code)">
									<xsl:value-of select="infg:format-siren-nc(infogreffe:SirenHR)"/>
								</xsl:when>
								<xsl:otherwise>
									<xsl:value-of select="infg:format-siren(infogreffe:SirenHR)"/>
								</xsl:otherwise>
							</xsl:choose>
						</xsl:variable>
						<xsl:choose>
							<xsl:when test="infogreffe:EtablissementPrincipal and infogreffe:EtablissementPrincipal = true()">
								<xsl:choose>
									<xsl:when test="not($sirenHR='NaN') and (infg:isNC(/infogreffe:Dossier/infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Code) or infg:isNC(infogreffe:GreffeInscription/infogreffe:Code))">
										<xsl:call-template name="information">
											<xsl:with-param name="ligne">rcsVilleDuGreffe</xsl:with-param>
											<xsl:with-param name="titre" select="concat($inscription,', numéro')"/>
											<xsl:with-param name="libelleKey">libelle-etablissementPrincipal</xsl:with-param>
											<xsl:with-param name="libelle" select="$sirenHR"/>
										</xsl:call-template>
									</xsl:when>
									<xsl:otherwise>
										<xsl:call-template name="information">
											<xsl:with-param name="ligne">rcsVilleDuGreffe</xsl:with-param>
											<xsl:with-param name="titre" select="$inscription"/>
											<xsl:with-param name="libelleKey">libelle-etablissementPrincipal</xsl:with-param>
										</xsl:call-template>
									</xsl:otherwise>
								</xsl:choose>
							</xsl:when>
							<xsl:otherwise>
								<xsl:choose>
									<xsl:when test="not($sirenHR='NaN') and (infg:isNC(/infogreffe:Dossier/infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Code) or infg:isNC(infogreffe:GreffeInscription/infogreffe:Code))">
										<xsl:call-template name="information">
											<xsl:with-param name="ligne">rcsVilleDuGreffe</xsl:with-param>
											<xsl:with-param name="titre" select="concat($inscription,', numéro')"/>
											<xsl:with-param name="libelle" select="$sirenHR"/>
										</xsl:call-template>
									</xsl:when>
									<xsl:otherwise>
										<xsl:call-template name="information">
											<xsl:with-param name="ligne">rcsVilleDuGreffe</xsl:with-param>
											<xsl:with-param name="libelle" select="$inscription"/>
										</xsl:call-template>
									</xsl:otherwise>
								</xsl:choose>
							</xsl:otherwise>
						</xsl:choose>
						<!--xsl:choose>
									<xsl:when test="infogreffe:EtablissementPrincipal and self::node()[infogreffe:EtablissementPrincipal = true()]">
										<xsl:call-template name="information">
											<xsl:with-param name="ligne">registreHorsRessortFrance</xsl:with-param>
											<xsl:with-param name="titre" select="$inscription"/>
											<xsl:with-param name="libelleKey">libelle-etablissementPrincipal</xsl:with-param>
										</xsl:call-template>
									</xsl:when>
									<xsl:otherwise>
										<xsl:call-template name="information">
											<xsl:with-param name="ligne">registreHorsRessortFrance</xsl:with-param>
											<xsl:with-param name="libelle" select="$inscription"/>
										</xsl:call-template>
									</xsl:otherwise>
								</xsl:choose-->
					</xsl:if>
				</xsl:for-each>
				<xsl:call-template name="sautLigne"/>
				<xsl:if test="count(//infogreffe:Extrait/infogreffe:Etablissement/infogreffe:InscriptionHorsRessortUE[not(infogreffe:Supprime) or infogreffe:Supprime = false()]) &gt; 0">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">refEtabPrincipauxSecondairesSituesImmatDansUEOuEEE</xsl:with-param>
						<xsl:with-param name="libelleKey">libelle-refEtabPrincipauxSecondairesSituesImmatDansUEOuEEE</xsl:with-param>
					</xsl:call-template>
					<xsl:for-each select="//infogreffe:Extrait/infogreffe:Etablissement/infogreffe:InscriptionHorsRessortUE">
						<!-- Tri de la liste par Etablissements:InscriptionHorsRessortUE:Adresse:Pays:Code -->
						<xsl:sort select="infogreffe:Adresse/infogreffe:Pays/infogreffe:Code"
								  order="ascending"/>
						<!-- Sélection des inscriptions secondaires étranger ouvertes Etablissements:InscriptionHorsRessortUE:supprime non renseigné -->
						<xsl:if test="not(infogreffe:Supprime) or infogreffe:Supprime = false()">
							<!-- Sauter une ligne entre chaque établissement -->
							<xsl:if test="position() != 1">
								<xsl:call-template name="sautLigne"/>
							</xsl:if>
							<!-- Adresse -->
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">ligne</xsl:with-param>
								<xsl:with-param name="titreKey">info-adresse</xsl:with-param>
								<xsl:with-param name="libelle">
									<xsl:call-template name="adresse">
										<xsl:with-param name="cheminAdresse" select="infogreffe:Adresse"/>
									</xsl:call-template>
								</xsl:with-param>
							</xsl:call-template>
							<!-- Activité -->
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">activitePrincipale</xsl:with-param>
								<xsl:with-param name="titreKey">info-activitePrincipale</xsl:with-param>
								<xsl:with-param name="libelle" select="infg:upperFirstLetter(infogreffe:Activite)"/>
							</xsl:call-template>
							<!-- Numéro d'immatriculation -->
							<xsl:if test="infogreffe:Immatriculation/infogreffe:NumeroRegistre or infogreffe:immatriculation/infogreffe:RegistreImmatriculation or infogreffe:Immatriculation/infogreffe:LieuRegistre">
								<xsl:call-template name="information">
									<xsl:with-param name="ligne">immatriculationHorsRessortUE</xsl:with-param>
									<xsl:with-param name="titreKey">info-immatriculationHorsRessortUE</xsl:with-param>
									<xsl:with-param name="libelle">
										<xsl:value-of select="infogreffe:Immatriculation/infogreffe:NumeroRegistre"/>
										<xsl:text> </xsl:text>
										<xsl:value-of select="infogreffe:Immatriculation/infogreffe:RegistreImmatriculation"/>
										<xsl:text> </xsl:text>
										<xsl:value-of select="infogreffe:Immatriculation/infogreffe:LieuRegistre"/>
									</xsl:with-param>
								</xsl:call-template>
							</xsl:if>
						</xsl:if>
						<xsl:call-template name="sautLigne"/>
					</xsl:for-each>
				</xsl:if>
			</xsl:with-param>
		</xsl:call-template>
	</xsl:template>
	<xsl:template name="fusionScission">
		<xsl:param name="statutDossier"/>
		<xsl:param name="cheminObservation"/>
		<xsl:param name="mentionType"/>
		<xsl:param name="titreKey"/>
		<xsl:variable name="observationsFUSSCIS"
					  select="$cheminObservation[infogreffe:RevelableExtraits = true() and empty(infogreffe:DateSuppression) and (infogreffe:TypeAgrege = $mentionType)]"/>
		<xsl:if test="($statutDossier = $statutDossier.BCD) and (count($observationsFUSSCIS) &gt; 0)">
			<xsl:call-template name="section">
				<xsl:with-param name="titreKey" select="$titreKey"/>
				<xsl:with-param name="informations">
					<!-- Les différentes sections de mentions avec une ou des mentions et le ou les intervenants associés -->
					<!-- Sélection des observations non supprimées Extrait:Observation:DateSuppression non renseignée et révélables Observation:RevelableExtrait = 1 pour un typeAgrege donné -->
					<xsl:for-each select="$observationsFUSSCIS">
						<!-- tri par ordre Extrait:Observation:OrdreAffichage -->
						<xsl:sort select="infogreffe:OrdreAffichage"
								  data-type="number"
								  order="ascending"/>
						<xsl:variable name="interveantsList"
									  select="infogreffe:Intervenant[infogreffe:DiffusableExtrait = true()]"/>
						<xsl:variable name="numero" select="infogreffe:Numero"/>
						<xsl:variable name="date" select="infogreffe:Date"/>
						<xsl:for-each select="infogreffe:Texte">
							<xsl:choose>
								<xsl:when test="position() = 1">
									<xsl:call-template name="information">
										<xsl:with-param name="ligne">identifiacationSocietesParticipeOperation</xsl:with-param>
										<xsl:with-param name="justify" select="true()"/>
										<xsl:with-param name="titre">
											<xsl:value-of select="concat(i18n:getTextPL('common-tiret'), ' ', i18n:getTextPL('info-mention'), ' ')"/>
											<xsl:if test="$numero">
												<xsl:value-of select="concat(i18n:getTextPL('common-min-numero'), ' ', $numero)"/>
											</xsl:if>
											<xsl:if test="$date">
												<xsl:value-of select="concat(' ', i18n:getTextPL('common-du'), ' ', infg:format-date($date))"/>
											</xsl:if>
										</xsl:with-param>
										<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint(current()/text())"/>
									</xsl:call-template>
								</xsl:when>
								<xsl:otherwise>
									<xsl:call-template name="complement">
										<xsl:with-param name="justify" select="true()"/>
										<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint(current()/text())"/>
									</xsl:call-template>
								</xsl:otherwise>
							</xsl:choose>
						</xsl:for-each>
						<!-- Le ou les intervenants -->
						<xsl:for-each select="$interveantsList">
							<!-- Tri par Extrait:Observation:Intervenant:Type:Code -->
							<xsl:sort select="infogreffe:Type/infogreffe:Code" order="ascending"/>
							<xsl:call-template name="information">
								<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
								<xsl:with-param name="libelle" select="infogreffe:Type/infogreffe:Libelle"/>
							</xsl:call-template>
							<xsl:call-template name="personneMorale">
								<xsl:with-param name="bloc" select="$information.bloc.indentDeuxBloc"/>
								<xsl:with-param name="cheminPersonneMorale" select="infogreffe:PersonneMorale"/>
								<xsl:with-param name="titreDeno"
												select="i18n:getTextPL('info-raisonSocialeDenomination')"/>
								<xsl:with-param name="type" select="infogreffe:Type/infogreffe:Code"/>
								<xsl:with-param name="mode" select="$personneMorale.mode.FUSIONSCISSION"/>
							</xsl:call-template>
							<!-- Complement intervenant de l'observation - infogreffe:Observation/infogreffe:Intervenant/infogreffe:Complement -->
							<xsl:if test="infogreffe:Complement">
								<xsl:call-template name="complement">
									<xsl:with-param name="libelle" select="infogreffe:Complement"/>
								</xsl:call-template>
							</xsl:if>
							<xsl:if test="position() != last()">
								<xsl:call-template name="sautLigne"/>
							</xsl:if>
						</xsl:for-each>
						<!--xsl:choose>
							<xsl:when test="count($interveantsList) eq 0">
							</xsl:when>
							<xsl:otherwise>
							</xsl:otherwise>
						</xsl:choose-->
					</xsl:for-each>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="gestionDirectionAdmin">
		<xsl:param name="statutDossier"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="typeInscription"/>
		<xsl:if test="     ((($statutDossier = $statutDossier.BCD) or ($statutDossier = $statutDossier.M))     and count(//infogreffe:Representant[infogreffe:Actif and infogreffe:Actif = true()]) gt 0)     or //infogreffe:PersonneMorale/infogreffe:Indicateurs/infogreffe:CarenceGerance = true()">
			<xsl:call-template name="section">
				<xsl:with-param name="titreKey">
					<xsl:choose>
						<xsl:when test="$statutDossier = $statutDossier.BCD">
							<xsl:text>section-GestionDirectionAdminCtrl</xsl:text>
						</xsl:when>
						<xsl:when test="$statutDossier = $statutDossier.M">
							<xsl:text>section-Administration</xsl:text>
						</xsl:when>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="informations">
					<xsl:if test="//infogreffe:PersonneMorale/infogreffe:Indicateurs/infogreffe:CarenceGerance = true()">
						<!-- mantis 47303  XAS 20170130 si CarrenceGerance on affiche  Absence de représentant légal - Voir in fine, rubrique "Observations et renseignements complémentaires"-->
						<xsl:call-template name="information">
							<xsl:with-param name="libelle">
								<xsl:value-of select="i18n:getTextPL('info-absenceRepLegal')"/>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:if>
					<!-- Liste des représentants -->
					<!-- Sélectionner si Representant:Actif -->
					<xsl:for-each select="//infogreffe:Representant[infogreffe:Actif and infogreffe:Actif = true()]">

						<!-- Tri par Extrait:Representant:OrdreAffichage -->
						<xsl:sort select="infogreffe:OrdreAffichage"
								  data-type="number"
								  order="ascending"/>
						<!-- Liste des qualités (pas de limite Mantis0027748) -->
						<!-- Sélectionner si Representant:Fonction:Actif -->
						<!-- On affiche la qualité si elle est différente de ZZZh -->
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">qualite</xsl:with-param>
							<xsl:with-param name="weight" select="$information.weight.bold"/>
							<xsl:with-param name="libelle">
								<xsl:for-each select="current()/infogreffe:Fonction[infogreffe:Actif and infogreffe:Actif = true() and not(infogreffe:Qualite/infogreffe:Code = $formeJuridique.ZZZh)]">
									<!-- Trier les fonctions  par Representant:Fonction:OrdreAffichage -->
									<xsl:sort select="infogreffe:OrdreAffichage"
											  data-type="number"
											  order="ascending"/>
									<!-- Qualité/Fonction (pas de limite Mantis0027748) -->
									<xsl:if test="position() != 1">
										<xsl:text> - </xsl:text>
										<xsl:value-of select="infogreffe:Qualite/infogreffe:Libelle"/>
									</xsl:if>
									<xsl:if test="position() = 1">
										<xsl:value-of select="infg:upperFirstLetter(infogreffe:Qualite/infogreffe:Libelle)"/>
									</xsl:if>
									<xsl:if test="infogreffe:Qualite/infogreffe:Complement">
										<xsl:text> </xsl:text>
										<xsl:value-of select="infogreffe:Qualite/infogreffe:Complement"/>
									</xsl:if>
								</xsl:for-each>
							</xsl:with-param>
						</xsl:call-template>
						<!-- fonctions -->
						<xsl:variable name="fonctionCodes"
									  select="infogreffe:Fonction[infogreffe:Actif and infogreffe:Actif = true()]/infogreffe:Qualite/infogreffe:Code"/>
						<xsl:if test="infogreffe:PersonneMorale and infogreffe:Fonction/infogreffe:Actif = true()">
							<!-- //XAS  20170201 mantis 47335 -->
							<xsl:variable name="FJrepresentantPM"
										  select="infogreffe:PersonneMorale/infogreffe:Identification/infogreffe:FormeJuridique/infogreffe:Code"/>
							<xsl:variable name="FJdossier"
										  select="/infogreffe:Dossier/infogreffe:Extrait[1]/infogreffe:Personne[1]/infogreffe:PersonneRCS[1]/infogreffe:PersonneMorale[1]/infogreffe:Identification[1]/infogreffe:FormeJuridique[1]/infogreffe:Code[1]"/>
							<xsl:call-template name="personneMorale">
								<xsl:with-param name="cheminPersonneMorale" select="infogreffe:PersonneMorale"/>
								<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
								<xsl:with-param name="fonctionCodes" select="$fonctionCodes"/>
								<xsl:with-param name="titreDeno">
									<xsl:choose>
										<xsl:when test="infg:in-list($fonctionCode.MEMh, $fonctionCodes)">
											<xsl:value-of select="i18n:getTextPL('info-raisonSocialeDenomination')"/>
										</xsl:when>
										<xsl:otherwise>
											<xsl:value-of select="i18n:getTextPL('info-denomination')"/>
										</xsl:otherwise>
									</xsl:choose>
								</xsl:with-param>
							</xsl:call-template>
							<!--  Liste des sanctions d'un représentant -->
							<xsl:variable name="numeroChrono" select="infogreffe:NumeroChrono"/>
							<!-- Recherche des sanctions avec : Extrait:SanctionPersonnelle:Inscrit renseigné et Extrait:SanctionPersonnelle:NumeroChronoRepresentant = Extrait:Representant:NumeroChrono -->
							<xsl:call-template name="sanction">
								<xsl:with-param name="sanctionList"
												select="//infogreffe:Extrait/infogreffe:SanctionPersonnelle[infogreffe:NumeroChronoRepresentantPP = $numeroChrono]"/>
								<xsl:with-param name="showBloc" select="true()"/>
							</xsl:call-template>
							<!-- Mantis 37028 XAS 20161014 affichage du bloc représentants permanents ou Personnes ayant le pouvoir de diriger, gérer ou engager à titre habituel-->
							<!-- Mantis 37028 XAS 20171003 ajout de code pour certain FJ (dossier) -->
							<!-- Mantis 65720 XAS 20220830 ajout de code pour certain FJ (dossier) -->
							<xsl:variable name="testAffBloc"
										  select="         $FJdossier and (($fonctionCodes = $fonctionQualCode.ADMh and infg:in-list($FJdossier, ($formeJuridique.SAh, $formeJuridique.SACAh, $formeJuridique.SAOSh, $formeJuridique.SAPOh, $formeJuridique.SAPh, $formeJuridique.SAEMh, $formeJuridique.SAHLMh, $formeJuridique.SAHLMCh, $formeJuridique.SASPh, $formeJuridique.GIEh, $formeJuridique.GIEVh, $formeJuridique.COIFAh, $formeJuridique.COIFAVh, $formeJuridique.COIFACh, $formeJuridique.COICAFAVh, $formeJuridique.COIHLMFAh, $formeJuridique.COIHLMFAVh,$formeJuridique.SPLh, $formeJuridique.SPLAh, $formeJuridique.SPLAFAh)))         or ($fonctionCodes = $fonctionQualCode.MEMCONSURh and infg:in-list($FJdossier, ($formeJuridique.SAh, $formeJuridique.SADIRh, $formeJuridique.SAOSh, $formeJuridique.SAPOh, $formeJuridique.SAPh, $formeJuridique.SAEMh, $formeJuridique.SAHLMh, $formeJuridique.SAHLMDh, $formeJuridique.SASPh, $formeJuridique.COIFAh, $formeJuridique.COIFAVh, $formeJuridique.COIFADVh, $formeJuridique.COICAFAVh, $formeJuridique.COIHLMFAh, $formeJuridique.COIHLMFAVh,$formeJuridique.SPLh, $formeJuridique.SPLAh, $formeJuridique.SPLAFAh)))         or ($fonctionCodes = $fonctionQualCode.GERh and infg:in-list($FJdossier, ($formeJuridique.GEIEh)))         or ($fonctionCodes = $fonctionQualCode.SOCGESh and infg:in-list($FJdossier, ($formeJuridique.SPPICAVh, $formeJuridique.SPPICAVCAh, $formeJuridique.SPPICAVASh))))"/>
							<xsl:variable name="imatriculation"
										  select="infogreffe:PersonneMorale/infogreffe:immatriculationFrance/infogreffe:Siren"/>
							<!-- TMA/XAS 20230306  mantis 65720 PM est immatriculée -->
							<!-- représentants permanents -->
							<xsl:if test="$testAffBloc">
								<xsl:call-template name="representantsPermanents">
									<xsl:with-param name="cheminRepresentantsPermanents"
													select="infogreffe:PersonneMorale/infogreffe:RepresentantPermanent"/>
								</xsl:call-template>
							</xsl:if>
							<!-- Personnes ayant le pouvoir de diriger, gérer ou engager à titre habituel -->
							<xsl:if test="not($testAffBloc)">
								<xsl:call-template name="personnesPouvoir">
									<xsl:with-param name="cheminPersonnesPouvoir"
													select="infogreffe:PersonneMorale/infogreffe:RepresentantPermanent"/>
								</xsl:call-template>
							</xsl:if>
							<!-- Complément -->
							<xsl:if test="infogreffe:Complement">
								<xsl:call-template name="complement">
									<xsl:with-param name="libelle" select="infogreffe:Complement"/>
									<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
								</xsl:call-template>
							</xsl:if>
						</xsl:if>
						<xsl:variable name="noline" select="false()"/>
						<xsl:if test="infogreffe:PersonnePhysique">
							<xsl:variable name="noline" select="true()"/>
							<!-- XAS pour n'afficher la ligne separtrice que si necessaire-->
							<!-- //XAS  20170201 mantis 47335 -->
							<xsl:if test="infogreffe:Fonction/infogreffe:Actif = true()">
								<!-- //XAS  20170201 mantis 47335 -->
								<xsl:variable name="noline" select="false()"/>
								<xsl:call-template name="personnePhysique">
									<xsl:with-param name="cheminPersonnePhysique" select="infogreffe:PersonnePhysique"/>
									<xsl:with-param name="elementTitre" select="i18n:getTextPL('info-nomPrenoms')"/>
									<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
									<xsl:with-param name="fonctionCodes" select="$fonctionCodes"/>
									<xsl:with-param name="mode"/>
								</xsl:call-template>
								<!-- Pouvoir : infogreffe:CapaciteEngagerSociete
								Si = "INCONNU ne rien afficher
								Si = "NON" afficher "Le représentant ne peut pas engager la société vis à vis de tiers"
								Si = "SEUL" afficher "Ayant pouvoir d'engager seul la société"
								Si = "CONJOINTEMENT" afficher "Ayant pouvoir d'engager conjointement la société" -->
								<xsl:call-template name="information">
									<xsl:with-param name="ligne">pouvoir</xsl:with-param>
									<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
									<xsl:with-param name="libelle">
										<xsl:choose>
											<xsl:when test="infogreffe:PersonnePhysique/infogreffe:CapaciteEngagerSociete = $capaciteEngagerSociete.NON">
												<xsl:value-of select="i18n:getTextPL('libelle-capaciteEngagerSocieteNon')"/>
											</xsl:when>
											<xsl:when test="infogreffe:PersonnePhysique/infogreffe:CapaciteEngagerSociete = $capaciteEngagerSociete.SEUL">
												<xsl:value-of select="i18n:getTextPL('libelle-capaciteEngagerSocieteSeul')"/>
											</xsl:when>
											<xsl:when test="infogreffe:PersonnePhysique/infogreffe:CapaciteEngagerSociete = $capaciteEngagerSociete.CONJOINTEMENT">
												<xsl:value-of select="i18n:getTextPL('libelle-capaciteEngagerSocieteConjointement')"/>
											</xsl:when>
										</xsl:choose>
									</xsl:with-param>
								</xsl:call-template>
								<!--  Liste des sanctions d'un représentant -->
								<xsl:variable name="numeroChrono" select="infogreffe:NumeroChrono"/>
								<!-- Recherche des sanctions avec : Extrait:SanctionPersonnelle:Inscrit renseigné et Extrait:SanctionPersonnelle:NumeroChronoRepresentant = Extrait:Representant:NumeroChrono -->
								<xsl:call-template name="sanction">
									<xsl:with-param name="sanctionList"
													select="//infogreffe:Extrait/infogreffe:SanctionPersonnelle[infogreffe:NumeroChronoRepresentantPP = $numeroChrono]"/>
									<xsl:with-param name="showBloc" select="true()"/>
								</xsl:call-template>
								<!-- Complément -->
								<xsl:if test="infogreffe:Complement">
									<xsl:call-template name="complement">
										<xsl:with-param name="libelle" select="infogreffe:Complement"/>
										<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
									</xsl:call-template>
								</xsl:if>
							</xsl:if>
						</xsl:if>
						<xsl:if test="position() != last() and $noline = false()">
							<xsl:call-template name="barreHorizontale"/>
						</xsl:if>
					</xsl:for-each>
					<!-- représentants -->
					<xsl:if test="$statutDossier = $statutDossier.BCD and $typeInscription = $typeInscription.P">
						<xsl:if test="         infg:hasObservationVisible($mentionTypeAggrege.SANCTION, $cheminObservation)         or infg:hasObservationVisible($mentionTypeAggrege.TUTELLE_CURATELLE, $cheminObservation)">
							<xsl:call-template name="barreHorizontale"/>
						</xsl:if>
						<xsl:call-template name="sautLigne"/>
						<xsl:call-template name="observationsSansTitre">
							<xsl:with-param name="ligne">mesuresIncapaciteInterfiction</xsl:with-param>
							<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.SANCTION"/>
							<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						</xsl:call-template>
						<xsl:call-template name="sautLigne"/>
						<xsl:call-template name="mentions">
							<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
							<xsl:with-param name="typeAgrege" select="$mentionTypeAggrege.TUTELLE_CURATELLE"/>
						</xsl:call-template>
					</xsl:if>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="autreEtablissementDansRessort">
		<xsl:param name="cheminEtablissementDansRessortNonSiegeEtNonFerme"/>
		<xsl:param name="statutRegistre"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="personneType"/>
		<xsl:param name="statutDossier"/>
		<xsl:call-template name="section">
			<xsl:with-param name="titreKey">
				<xsl:choose>
					<xsl:when test="count($cheminEtablissementDansRessortNonSiegeEtNonFerme) = 2">
						<xsl:text>section-autreEtablissementRessort</xsl:text>
					</xsl:when>
					<xsl:otherwise>
						<xsl:text>section-autresEtablissementsRessort</xsl:text>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:with-param>
			<xsl:with-param name="informations">
				<xsl:call-template name="etablissementsDansRessort">
					<xsl:with-param name="etablissements"
									select="$cheminEtablissementDansRessortNonSiegeEtNonFerme"/>
					<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
					<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					<xsl:with-param name="personneType" select="$personneType"/>
					<xsl:with-param name="start" select="2"/>
					<xsl:with-param name="finish" select="last()"/>
					<xsl:with-param name="statutDossier" select="$statutDossier"/>
					<xsl:with-param name="infogreffeSiegeEtranger"
									select="//infogreffe:AdresseDomicile/infogreffe:Pays/infogreffe:Code != 'FR'"/>
				</xsl:call-template>
			</xsl:with-param>
		</xsl:call-template>
	</xsl:template>
	<xsl:template name="renseignementsRelatifsEtablissement">
		<xsl:param name="statutDossier"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="cheminEtablissementDansRessortNonSiegeEtNonFerme"/>
		<xsl:param name="statutRegistre"/>
		<xsl:param name="personneType"/>
		<xsl:call-template name="section">
			<xsl:with-param name="titreKey">
				<xsl:call-template name="etablissementTitre">
					<xsl:with-param name="statutDossier" select="$statutDossier"/>
					<xsl:with-param name="infogreffeSiegeEtranger"
									select="//infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeEtranger"/>
					<xsl:with-param name="infogreffeType" select="infogreffe:Type"/>
				</xsl:call-template>
			</xsl:with-param>
			<xsl:with-param name="informations">
				<xsl:call-template name="etablissementsDansRessort">
					<xsl:with-param name="etablissements"
									select="$cheminEtablissementDansRessortNonSiegeEtNonFerme"/>
					<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
					<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					<xsl:with-param name="personneType" select="$personneType"/>
					<xsl:with-param name="start" select="1"/>
					<xsl:with-param name="finish" select="1"/>
					<xsl:with-param name="infogreffeSiegeEtranger"
									select="//infogreffe:AdresseDomicile/infogreffe:Pays/infogreffe:Code != 'FR'"/>
					<xsl:with-param name="statutDossier" select="$statutDossier"/>
				</xsl:call-template>
			</xsl:with-param>
		</xsl:call-template>
	</xsl:template>
	<xsl:template name="etablissementPrincipalCessationDate">
		<xsl:param name="statutDossier"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="etablissementTitreKey"/>
		<xsl:param name="dateFin"/>
		<xsl:param name="etablissement"/>
		<xsl:variable name="sectionTitreKey">
			<xsl:choose>
				<xsl:when test="$etablissementTitreKey">
					<xsl:value-of select="$etablissementTitreKey"/>
				</xsl:when>
				<xsl:otherwise>
					<xsl:call-template name="etablissementTitre">
						<xsl:with-param name="statutDossier" select="$statutDossier"/>
						<xsl:with-param name="infogreffeSiegeEtranger"
										select="//infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeEtranger"/>
						<xsl:with-param name="infogreffeType" select="$etablissement/infogreffe:Type"/>
					</xsl:call-template>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:call-template name="section">
			<xsl:with-param name="titreKey" select="$sectionTitreKey"/>
			<xsl:with-param name="informations">
				<xsl:if test="$etablissement">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">adressePremierEtablissementFerme</xsl:with-param>
						<xsl:with-param name="titreKey">info-adresseEtablissement</xsl:with-param>
						<xsl:with-param name="libelle">
							<xsl:call-template name="adresse">
								<xsl:with-param name="cheminAdresse" select="$etablissement/infogreffe:Adresse"/>
							</xsl:call-template>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
				<xsl:call-template name="cessationActivite">
					<xsl:with-param name="dateFin" select="$dateFin"/>
					<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				</xsl:call-template>
			</xsl:with-param>
		</xsl:call-template>
	</xsl:template>
	<xsl:template name="dissolutionDecisionPrononcantNullite">
		<xsl:param name="cheminObservation"/>
		<xsl:param name="mentionTypeAggrege.DISSOLUTION"/>
		<xsl:if test="$cheminObservation/infogreffe:TypeAgrege = $mentionTypeAggrege.DISSOLUTION">
			<xsl:call-template name="section">
				<xsl:with-param name="titreKey">section-DissolutionDecisionPrononcantNullite</xsl:with-param>
				<xsl:with-param name="informations">
					<!-- Sélection des observations de type DISSOLUTION Extrait:Observation:TypeAgrege est DISSOLUTION -->
					<xsl:call-template name="mentions">
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						<xsl:with-param name="typeAgrege" select="$mentionTypeAggrege.DISSOLUTION"/>
					</xsl:call-template>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="mentionRadiation">
		<xsl:param name="personneType"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="statutDossier"/>
		<xsl:variable name="motifRadiation"
					  select="//infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:MotifRadiation/infogreffe:Libelle"/>
		<xsl:variable name="codeMotifRadiation"
					  select="//infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:MotifRadiation/infogreffe:Code"/>
		<xsl:variable name="dateRadiation"
					  select="//infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation"/>
		<xsl:variable name="dateFin"
					  select="//*[name() = $personneType]/infogreffe:Activite/infogreffe:DateCessationActivite"/>
		<xsl:variable name="existObservationRadiation"
					  select="infg:hasObservationVisible($mentionTypeAggrege.RADIATION, $cheminObservation)"/>
		<xsl:variable name="existMotifRadiation"
					  select="$motifRadiation and $codeMotifRadiation ne $motifRadiation.R_CF_OBS and not(normalize-space($motifRadiation) = '')"/>
		<xsl:if test="$existMotifRadiation or $existObservationRadiation or $dateRadiation">
			<xsl:call-template name="section">
				<xsl:with-param name="titreKey">section-radiation</xsl:with-param>
				<xsl:with-param name="informations">
					<xsl:if test="$existMotifRadiation">
						<xsl:call-template name="information">
							<!-- R_CF_OBS -->
							<xsl:with-param name="ligne">motifRadiation</xsl:with-param>
							<xsl:with-param name="titreKey">info-motifRadiation</xsl:with-param>
							<xsl:with-param name="libelle" select="$motifRadiation"/>
						</xsl:call-template>
					</xsl:if>
					<xsl:if test="$dateRadiation">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">date</xsl:with-param>
							<xsl:with-param name="titreKey">info-dateRadiation2</xsl:with-param>
							<xsl:with-param name="libelle"
											select="format-date($dateRadiation, '[D01]/[M01]/[Y0001]')"/>
						</xsl:call-template>
					</xsl:if>
					<xsl:if test="$existObservationRadiation">
						<xsl:call-template name="mentions">
							<xsl:with-param name="bloc"/>
							<xsl:with-param name="typeAgrege" select="$mentionTypeAggrege.RADIATION"/>
							<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						</xsl:call-template>
					</xsl:if>
					<xsl:if test="$dateFin">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">dateCessationActivite</xsl:with-param>
							<xsl:with-param name="titreKey">info-dateCessationActivite</xsl:with-param>
							<xsl:with-param name="libelle" select="format-date($dateFin, '[D01]/[M01]/[Y0001]')"/>
						</xsl:call-template>
					</xsl:if>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="mentionOfficeRelativeProcedureCollective">
		<xsl:param name="cheminObservation"/>
		<xsl:if test="$cheminObservation/infogreffe:TypeAgrege = $mentionTypeAggrege.JUDICIAIRE">
			<xsl:call-template name="section">
				<xsl:with-param name="titreKey">section-mentionsOfficeRelativesProcedureCollective</xsl:with-param>
				<xsl:with-param name="informations">
					<!-- Sélection des observations de type JUDICIAIRE Extrait:Observation:TypeAgrege est JUDICIAIRE -->
					<xsl:call-template name="mentions">
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						<xsl:with-param name="typeAgrege" select="$mentionTypeAggrege.JUDICIAIRE"/>
					</xsl:call-template>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="autresMentionObservation">
		<xsl:param name="cheminObservation"/>
		<xsl:param name="statutDossier"/>
		<xsl:param name="typeInscription"/>
		<xsl:variable name="count_etablissement"
					  select="count(//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = false() and infogreffe:Type != $etablissementDansRessortType.SIE])"/>
		<xsl:variable name="count_branches_actives"
					  select="count(//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = false() and (infogreffe:Type != $etablissementDansRessortType.SIE)]/infogreffe:ActiviteFonds[infogreffe:Ferme = false()])"/>
		<xsl:variable name="diverObservation"
					  select="     infg:selectObservationVisible(null, $cheminObservation)[     (infg:isPM($statutDossier) and     (infg:in-list(infogreffe:TypeAgrege, ($mentionTypeAggrege.RESTRUCTURATION, $mentionTypeAggrege.DIVERS, $mentionTypeAggrege.INTERNET)))     or (infg:in-list(infogreffe:TypeAgrege, ($mentionTypeAggrege.TUTELLE_CURATELLE, $mentionTypeAggrege.SANCTION)) and $typeInscription = $typeInscription.S and $statutDossier = $statutDossier.BCD)     or (infogreffe:TypeAgrege = $mentionTypeAggrege.AUTORIS_DIPLOME and ($count_etablissement ne 1 or ($count_etablissement eq 1 and $count_branches_actives gt 1)))     )     or (infg:isPP($statutDossier) and     (infg:in-list(infogreffe:TypeAgrege, ($mentionTypeAggrege.DIVERS, $mentionTypeAggrege.LEGISLATION)))     or (infg:in-list(infogreffe:TypeAgrege, ($mentionTypeAggrege.INTERNET, $mentionTypeAggrege.DOMICILIATION)) and $count_etablissement ne 1)     or (infogreffe:TypeAgrege = $mentionTypeAggrege.AUTORIS_DIPLOME and ($count_etablissement ne 1 or ($count_etablissement eq 1 and $count_branches_actives gt 1)))     )     ]"> </xsl:variable>
		<xsl:if test="count($diverObservation) &gt; 0">
			<xsl:call-template name="section">
				<xsl:with-param name="titreKey">section-autresMentionsObservations</xsl:with-param>
				<xsl:with-param name="informations">
					<xsl:call-template name="mentions">
						<xsl:with-param name="cheminObservation" select="$diverObservation"/>
					</xsl:call-template>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="conjointCollaborateur">
		<xsl:param name="conjointCollaborateur"/>
		<xsl:param name="identificationAdresse"/>
		<xsl:param name="statutDossier"/>
		<xsl:if test="count($conjointCollaborateur) &gt; 0">
			<xsl:call-template name="section">
				<xsl:with-param name="titreKey">
					<xsl:choose>
						<xsl:when test="infg:isPM($statutDossier)">
							<xsl:text>section-IdentConjointPersonneLieePactCivilSolidariteCollaboreActiviteSocieteOuConjointConcubinPM</xsl:text>
						</xsl:when>
						<xsl:otherwise>
							<xsl:text>section-IdentConjointPersonneLieePactCivilSolidariteCollaboreActiviteSocieteOuConjointConcubinPP</xsl:text>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="informations">
					<xsl:for-each select="$conjointCollaborateur">
						<xsl:if test="not(infogreffe:DateFinCollaboration)">
							<xsl:variable name="cheminIdentification" select="infogreffe:Identification"/>
							<!-- Nom et prénoms -->
							<xsl:call-template name="nomPrenoms">
								<xsl:with-param name="identification" select="$cheminIdentification"/>
								<xsl:with-param name="attributeLigne">nomPrenoms</xsl:with-param>
								<xsl:with-param name="elementTitre" select="i18n:getTextPL('info-nomPrenoms')"/>
							</xsl:call-template>
							<!-- Nom d'usage -->
							<xsl:call-template name="nomUsage">
								<xsl:with-param name="identification" select="$cheminIdentification"/>
								<xsl:with-param name="attributeLigne">ligne</xsl:with-param>
							</xsl:call-template>
							<!-- Pseudonyme -->
							<xsl:if test="$cheminIdentification/infogreffe:Pseudonyme">
								<xsl:call-template name="information">
									<xsl:with-param name="ligne">pseudonyme</xsl:with-param>
									<xsl:with-param name="titreKey">info-pseudonyme</xsl:with-param>
									<xsl:with-param name="libelle" select="$cheminIdentification/infogreffe:Pseudonyme"/>
								</xsl:call-template>
							</xsl:if>
							<!-- Date et lieu de naissance -->
							<xsl:call-template name="infoNaissance">
								<xsl:with-param name="chemin" select="$cheminIdentification"/>
								<xsl:with-param name="attributeLigne">ligne</xsl:with-param>
							</xsl:call-template>
							<!-- Nationalité -->
							<xsl:call-template name="nationalite">
								<xsl:with-param name="chemin" select="$cheminIdentification"/>
								<xsl:with-param name="attributeLigne">ligne</xsl:with-param>
							</xsl:call-template>
							<!-- Adresse -->
							<xsl:variable name="libelleAdresseConjoint">
								<xsl:call-template name="adresse">
									<xsl:with-param name="cheminAdresse"
													select="$cheminIdentification/infogreffe:AdresseDomicile"/>
								</xsl:call-template>
							</xsl:variable>
							<xsl:variable name="libelleAdresse">
								<xsl:call-template name="adresse">
									<xsl:with-param name="cheminAdresse" select="$identificationAdresse"/>
								</xsl:call-template>
							</xsl:variable>
							<xsl:if test="$libelleAdresse != $libelleAdresseConjoint">
								<xsl:call-template name="information">
									<xsl:with-param name="ligne">ligne</xsl:with-param>
									<xsl:with-param name="titreKey">info-domicile</xsl:with-param>
									<xsl:with-param name="libelle">
										<xsl:call-template name="adresse">
											<xsl:with-param name="cheminAdresse"
															select="$cheminIdentification/infogreffe:AdresseDomicile"/>
										</xsl:call-template>
									</xsl:with-param>
								</xsl:call-template>
							</xsl:if>
						</xsl:if>
					</xsl:for-each>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="mesureProtectionEntrepreneurIndividuel">
		<xsl:param name="statutDossier"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="personneType"/>
		<xsl:if test="infg:isPP($statutDossier)">
			<xsl:call-template name="section">
				<xsl:with-param name="titreKey">section-mesureProtectionEntrepreneurIndividuel</xsl:with-param>
				<xsl:with-param name="informations">
					<!-- Déclaration d'insaisissabilité -->
					<xsl:call-template name="observationsSansTitre">
						<xsl:with-param name="ligne">declarationInsaisissabilite</xsl:with-param>
						<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.INSAISISSABILITE"/>
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					</xsl:call-template>
					<xsl:call-template name="observationsSansTitre">
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.EIRL"/>
					</xsl:call-template>
					<!-- Sélection des PersonneRCS:PersonnePhysique:DeclarationAffectationPatrimoine or PersonneRSEIRL:DeclarationAffectationPatrimoine
				Si PersonneRCS:PersonnePhysique:DeclarationAffectationPatrimoine:DateRenoncement non renseignée -->
					<xsl:if test="not(infg:hasObservationVisible($mentionTypeAggrege.EIRL, $cheminObservation))">
						<xsl:variable name="dapList"
									  select="//*[name() = $personneType]/descendant-or-self::infogreffe:DeclarationAffectationPatrimoine[empty(infogreffe:DateRenoncement)]"/>
						<!-- Entrepreneur individuel à responsabilité limitée (EIRL) -->
						<xsl:if test="count($dapList)">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">entrepreneurIndividuelResponsabiliteLimitee</xsl:with-param>
								<xsl:with-param name="libelleKey">libelle-entrepreneurIndividuelResponsabiliteLimitee</xsl:with-param>
							</xsl:call-template>
						</xsl:if>
						<xsl:for-each select="$dapList">
							<!-- tri par ordre PersonneRCS:PersonnePhysique:DeclarationAffectationPatrimoine:OrdreAffichage -->
							<xsl:sort select="infogreffe:OrdreAffichage"
									  order="ascending"
									  data-type="number"/>
							<!-- Déclaration d'affectation du patrimoine -->
							<!-- 20191212 supprimer de la section -->
							<!-- Dénomination utilisée pour l'activité -->
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">denominationUtilisePourActivite</xsl:with-param>
								<xsl:with-param name="titreKey">info-denominationUtilisePourActivite</xsl:with-param>
								<xsl:with-param name="libelle" select="infogreffe:Denomination"/>
							</xsl:call-template>
							<!-- Objet de l'activité -->
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">objetActiviteProfessionnelle</xsl:with-param>
								<xsl:with-param name="justify" select="true()"/>
								<xsl:with-param name="titreKey">info-objetActiviteProfessionnelle</xsl:with-param>
								<xsl:with-param name="libelle" select="infogreffe:Objet"/>
							</xsl:call-template>
							<!-- Adresse d'exercice -->
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">adresseDAP</xsl:with-param>
								<xsl:with-param name="titreKey">info-adresseExercice</xsl:with-param>
								<xsl:with-param name="libelle">
									<xsl:call-template name="adresse">
										<xsl:with-param name="cheminAdresse" select="infogreffe:AdresseActivite"/>
									</xsl:call-template>
								</xsl:with-param>
							</xsl:call-template>
							<!-- Date de clôture de l'exercice comptable -->
							<xsl:if test="infogreffe:DateCloture">
								<xsl:call-template name="information">
									<xsl:with-param name="ligne">dateClotureDAP</xsl:with-param>
									<xsl:with-param name="titreKey">info-dateClotureExerciceComptable</xsl:with-param>
									<xsl:with-param name="libelle"
													select="infg:format-date-day-month-long(infogreffe:DateCloture)"/>
								</xsl:call-template>
							</xsl:if>
							<!-- Immatriculation au repertoire des metiers -->
							<xsl:if test="infogreffe:RegistreDepot/infogreffe:Libelle and infogreffe:TypeRegistreDepot = 'RM'">
								<xsl:call-template name="information">
									<xsl:with-param name="ligne">immatriculationRepertoireMetiers</xsl:with-param>
									<xsl:with-param name="titreKey">info-immatriculationRepertoireMetiers</xsl:with-param>
									<xsl:with-param name="libelle" select="infogreffe:RegistreDepot/infogreffe:Libelle"/>
								</xsl:call-template>
							</xsl:if>
							<xsl:if test="position() != last()">
								<!--xsl:call-template name="ligneSeparation"/-->
								<xsl:call-template name="sautLigne"/>
							</xsl:if>
						</xsl:for-each>
					</xsl:if>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<!-- TEMPLATE IDENTIFICATION -->
	<xsl:template name="identification">
		<xsl:param name="statutDossier"/>
		<xsl:param name="statutRegistre"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="libelleGreffe"/>
		<!-- Variable personneType -->
		<xsl:variable name="personneType">
			<xsl:choose>
				<xsl:when test="$statutRegistre = $statutRegistre.RCS">infogreffe:PersonneRCS</xsl:when>
				<xsl:when test="$statutRegistre = $statutRegistre.RSAC">infogreffe:PersonneRSAC</xsl:when>
				<xsl:when test="$statutRegistre = $statutRegistre.RSEIRL">infogreffe:PersonneRSEIRL</xsl:when>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="personne"
					  select="infogreffe:Extrait/infogreffe:Personne/*[name() = $personneType]"/>
		<xsl:variable name="numeroGreffe"
					  select="//infogreffe:Dossier/infogreffe:Identifiant/infogreffe:Greffe"/>
		<xsl:choose>
			<xsl:when test="$personne/infogreffe:PersonneMorale">
				<xsl:call-template name="identificationPersonneMorale">
					<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
					<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					<xsl:with-param name="libelleGreffe" select="$libelleGreffe"/>
					<xsl:with-param name="personne" select="$personne"/>
					<xsl:with-param name="statutDossier" select="$statutDossier"/>
					<xsl:with-param name="personneType" select="$personneType"/>
				</xsl:call-template>
			</xsl:when>
			<xsl:when test="$personne/infogreffe:PersonnePhysique">
				<xsl:call-template name="identificationPersonnePhysique">
					<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
					<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					<xsl:with-param name="libelleGreffe" select="$libelleGreffe"/>
					<xsl:with-param name="personne" select="$personne"/>
					<xsl:with-param name="statutDossier" select="$statutDossier"/>
				</xsl:call-template>
			</xsl:when>
		</xsl:choose>
	</xsl:template>
	<xsl:template name="identificationPersonnePhysique">
		<xsl:param name="statutRegistre"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="libelleGreffe"/>
		<xsl:param name="personne"/>
		<xsl:param name="statutDossier"/>
		<xsl:call-template name="section">
			<xsl:with-param name="titreKey">section-identificationPersonnePhysique</xsl:with-param>
			<xsl:with-param name="informations">
				<!-- Si SiegeFrance:Greffe:Libellé est renseigné le prendre sinon prendre Greffe:Libelle -->
				<!-- numero immat -->
				<xsl:call-template name="informationPersonnePhysique">
					<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
					<xsl:with-param name="personne" select="$personne"/>
				</xsl:call-template>
				<!-- Variable immatriculationType -->
				<xsl:variable name="immatriculationType">
					<xsl:choose>
						<xsl:when test="$statutRegistre = $statutRegistre.RCS">infogreffe:ImmatriculationRCS</xsl:when>
						<xsl:when test="$statutRegistre = $statutRegistre.RSAC">infogreffe:ImmatriculationRSAC</xsl:when>
						<xsl:when test="$statutRegistre = $statutRegistre.RSEIRL">infogreffe:ImmatriculationRSEIRL</xsl:when>
					</xsl:choose>
				</xsl:variable>
				<!-- Date d'immatriculation -->
				<xsl:if test="not($personne/*[name() = $immatriculationType]/infogreffe:TypeInscription) or $personne/*[name() = $immatriculationType]/infogreffe:TypeInscription = $typeInscription.P">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">dateImmatriculation</xsl:with-param>
						<xsl:with-param name="titreKey">info-dateImmat</xsl:with-param>
						<xsl:with-param name="libelle"
										select="infg:format-date($personne/*[name() = $immatriculationType]/infogreffe:DateImmatriculation)"/>
					</xsl:call-template>
					<!-- Transfert du -->
					<xsl:variable name="cheminTransfertArrivee"
								  select="//infogreffe:Transfert/infogreffe:TransfertArrivee/infogreffe:AncienGreffe/infogreffe:Libelle"/>
					<xsl:if test="($statutDossier = $statutDossier.A) and ($personne/infogreffe:Transfert/infogreffe:TransfertArrivee/infogreffe:AncienGreffe/infogreffe:Libelle)">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">transfertDu</xsl:with-param>
							<xsl:with-param name="titreKey">info-transfertDu</xsl:with-param>
							<xsl:with-param name="libelle">
								<xsl:value-of select="infg:format-status-nomGreffe(i18n:getTextByNamePL($statutRegistre) ,  $personne/infogreffe:Transfert/infogreffe:TransfertArrivee/infogreffe:AncienGreffe/infogreffe:Libelle)"/>
								<xsl:if test="($statutDossier = $statutDossier.A) and ($personne/infogreffe:Transfert/infogreffe:TransfertArrivee/infogreffe:DateTransfert)">
									<xsl:text> </xsl:text>
									<xsl:value-of select="i18n:getTextPL('info-enDateDu')"/>
									<xsl:text> </xsl:text>
									<xsl:value-of select="infg:format-date($personne/infogreffe:Transfert/infogreffe:TransfertArrivee/infogreffe:DateTransfert)"/>
								</xsl:if>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:if>
				</xsl:if>
				<!-- Date de début d'activité commerciale devient Date d'immatriculation d'origine  -->
				<!-- Mantis 0036771 xsl:if test="($statutDossier = $statutDossier.A) and $personne/infogreffe:ImmatriculationRCS/infogreffe:DatePremiereImmatriculation">
					<xsl:if test="(not($personne/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription) or $personne/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.P)
								and $personne/infogreffe:Transfert/infogreffe:TransfertArrivee
								and $personne/infogreffe:ImmatriculationRCS/infogreffe:DateImmatriculation != $personne/infogreffe:ImmatriculationRCS/infogreffe:DatePremiereImmatriculation">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">dateDebutActivite</xsl:with-param>
							<xsl:with-param name="titreKey">info-dateImmatOrigine</xsl:with-param>
							<xsl:with-param name="libelle" select="infg:format-date($personne/infogreffe:ImmatriculationRCS/infogreffe:DatePremiereImmatriculation)"/>
						</xsl:call-template>
					</xsl:if>
				</xsl:if -->
				<!-- Si Radiation:DateRadiation renseigné -->
				<xsl:if test="not($personne/*[name() = $immatriculationType]/infogreffe:TypeInscription) or $personne/*[name() = $immatriculationType]/infogreffe:TypeInscription = $typeInscription.P">
					<xsl:if test="infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">dateRadiatation</xsl:with-param>
							<xsl:with-param name="titreKey">info-dateRadiation</xsl:with-param>
							<xsl:with-param name="libelle">
								<xsl:value-of select="infg:format-date(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)"/>
								<xsl:if test="($statutDossier = $statutDossier.A)           and (//infogreffe:PersonneRCS/infogreffe:Transfert/infogreffe:TransfertDepart/infogreffe:NouveauGreffe/infogreffe:Libelle)          and infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:MotifRadiation/infogreffe:Code = $motifRadiation.R_TRANS">
									<xsl:text> </xsl:text>
									<xsl:value-of select="i18n:getTextPL('info-transfertAu')"/>
									<xsl:text> </xsl:text>
									<xsl:value-of select="infg:format-status-nomGreffe(i18n:getTextByNamePL($statutRegistre) ,  //infogreffe:PersonneRCS/infogreffe:Transfert/infogreffe:TransfertDepart/infogreffe:NouveauGreffe/infogreffe:Libelle)"/>
								</xsl:if>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:if>
				</xsl:if>
				<xsl:call-template name="sautLigne"/>
				<!-- Informations complémentaires pour $statutDossier = 'A' or $statutDossier = 'P' or $statutDossier = 'I' -->
				<xsl:if test="infg:isPP($statutDossier)">

					<!--<xsl:variable name="cheminIdentification" select="infogreffe:Extrait/infogreffe:Personne/*[name() = $personneType]/descendant-or-self::infogreffe:Identification" />-->
					<xsl:variable name="cheminIdentification"
								  select="$personne/*[name() = 'infogreffe:PersonneMorale' or name() = 'infogreffe:PersonnePhysique']/infogreffe:Identification"/>
					<!-- Nom de naissance et prénoms -->
					<xsl:call-template name="nomPrenoms">
						<xsl:with-param name="identification" select="$cheminIdentification"/>
						<xsl:with-param name="attributeLigne">nomPrenoms</xsl:with-param>
						<xsl:with-param name="elementTitre" select="i18n:getTextPL('info-nomPrenoms')"/>
						<xsl:with-param name="weight" select="$information.weight.bold"/>
					</xsl:call-template>
					<!-- Nom d'usage -->
					<xsl:call-template name="nomUsage">
						<xsl:with-param name="identification" select="$cheminIdentification"/>
						<xsl:with-param name="attributeLigne">nomUsage</xsl:with-param>
					</xsl:call-template>
					<!-- Pseudonyme -->
					<xsl:if test="$cheminIdentification/infogreffe:Pseudonyme">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">pseudonyme</xsl:with-param>
							<xsl:with-param name="titreKey">info-pseudonyme</xsl:with-param>
							<xsl:with-param name="libelle" select="$cheminIdentification/infogreffe:Pseudonyme"/>
						</xsl:call-template>
					</xsl:if>
					<!-- Date et lieu de naissance -->
					<xsl:call-template name="infoNaissance">
						<xsl:with-param name="chemin" select="$cheminIdentification"/>
						<xsl:with-param name="attributeLigne">dateLieuNaissance</xsl:with-param>
					</xsl:call-template>
					<!-- Deces -->
					<xsl:call-template name="observationsSansTitre">
						<xsl:with-param name="ligne">deces</xsl:with-param>
						<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.DECES"/>
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					</xsl:call-template>
					<!-- Nationalité -->
					<xsl:call-template name="nationalite">
						<xsl:with-param name="chemin" select="$cheminIdentification"/>
						<xsl:with-param name="attributeLigne">nationalite</xsl:with-param>
					</xsl:call-template>
					<!-- Adresse du domicile personnel -->
					<xsl:if test="$cheminIdentification/infogreffe:AdresseDomicile">
						<xsl:variable name="cheminAdresse"
									  select="$cheminIdentification/infogreffe:AdresseDomicile"/>
						<xsl:choose>
							<xsl:when test="$personne/infogreffe:PersonnePhysique/infogreffe:Indicateurs/infogreffe:Forain = true()          and not($cheminAdresse/infogreffe:Ligne1)          and not($cheminAdresse/infogreffe:Ligne2)          and not($cheminAdresse/infogreffe:Ligne3)         and $cheminAdresse/infogreffe:BureauDistributeur">
								<xsl:call-template name="information">
									<xsl:with-param name="ligne">communeRattachement</xsl:with-param>
									<xsl:with-param name="titreKey">info-communeRattachement</xsl:with-param>
									<xsl:with-param name="libelle" select="$cheminAdresse/infogreffe:BureauDistributeur"/>
								</xsl:call-template>
							</xsl:when>
							<xsl:otherwise>
								<xsl:call-template name="information">
									<xsl:with-param name="ligne">domicilePersonel</xsl:with-param>
									<xsl:with-param name="titreKey">info-domicilePersonel</xsl:with-param>
									<xsl:with-param name="libelle">
										<xsl:call-template name="adresse">
											<xsl:with-param name="cheminAdresse"
															select="$cheminIdentification/infogreffe:AdresseDomicile"/>
										</xsl:call-template>
									</xsl:with-param>
								</xsl:call-template>
							</xsl:otherwise>
						</xsl:choose>
					</xsl:if>
					<xsl:if test="(not($personne/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription) or $personne/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.P)             and not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)">
						<xsl:if test="//infogreffe:Personne/infogreffe:DonneesInsee/infogreffe:NAF/infogreffe:Code">
							<xsl:call-template name="siretNAF">
								<xsl:with-param name="codeNAF"
												select="//infogreffe:Personne/infogreffe:DonneesInsee/infogreffe:NAF/infogreffe:Code"/>
							</xsl:call-template>
						</xsl:if>
					</xsl:if>
					<xsl:call-template name="sautLigne"/>
					<!-- Tutelle ou curatelle (mise sous protection, mainlevée ou rapport) -->
					<xsl:call-template name="observationsSansTitre">
						<xsl:with-param name="ligne">tutelleCuratelleMiseProtectionMainleveeRapport</xsl:with-param>
						<xsl:with-param name="mentionTypeAggrege"
										select="$mentionTypeAggrege.TUTELLE_CURATELLE"/>
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					</xsl:call-template>
					<xsl:call-template name="sautLigne"/>
					<xsl:if test="$statutDossier != $statutDossier.BCD">
						<xsl:call-template name="observationsSansTitre">
							<xsl:with-param name="ligne">mesuresIncapaciteInterfiction</xsl:with-param>
							<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.SANCTION"/>
							<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						</xsl:call-template>
					</xsl:if>
					<xsl:if test="($statutDossier = $statutDossier.A or $statutDossier = $statutDossier.P )">
						<!-- Incapacité(s) ou interdiction(s)  pour $statutDossier = 'A' ou $statutDossier = 'P' -->
						<xsl:if test="not(infg:hasObservationVisible($mentionTypeAggrege.SANCTION, $cheminObservation)) and //infogreffe:Extrait/infogreffe:SanctionPersonnelle">
							<xsl:call-template name="sanction">
								<xsl:with-param name="sanctionList"
												select="infogreffe:Extrait/infogreffe:SanctionPersonnelle[infogreffe:Inscrit = true()]"/>
								<xsl:with-param name="showBloc" select="false()"/>
							</xsl:call-template>
						</xsl:if>
						<!-- Contrat d'appui (CAPE) pour $statutDossier = 'A' or $statutDossier = 'P' -->
						<xsl:call-template name="mentionCAPE">
							<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						</xsl:call-template>
					</xsl:if>
				</xsl:if>
				<xsl:if test="($statutDossier = $statutDossier.A) and $personne/*[name() = $immatriculationType]/infogreffe:TypeInscription = $typeInscription.S">
					<xsl:variable name="cheminSiegeOuEtablissementPrincipalPPHorsRessort"
								  select="//infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort"/>
					<!-- Adresse -->
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">adresseEtablissementFrance</xsl:with-param>
						<xsl:with-param name="titreKey">info-adresseEtabPrincipal</xsl:with-param>
						<xsl:with-param name="libelle">
							<xsl:call-template name="adresse">
								<xsl:with-param name="cheminAdresse"
												select="$cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Adresse"/>
							</xsl:call-template>
						</xsl:with-param>
					</xsl:call-template>
					<!-- Entreprise de domiciliation -->
					<xsl:if test="$cheminSiegeOuEtablissementPrincipalPPHorsRessort and ($cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire        and ($cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:Denomination        or $cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:Siren        or $cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Libelle        or $cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:Complement))">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">entrepriseDomiciliation</xsl:with-param>
							<xsl:with-param name="titreKey">info-domiciliationCommun</xsl:with-param>
							<xsl:with-param name="libelle">
								<xsl:if test="$cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:Denomination">
									<xsl:value-of select="$cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:Denomination"/>
								</xsl:if>
								<xsl:if test="$cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:Siren">
									<xsl:value-of select="i18n:getTextPLPrefSuff(' ', 'common-tiret', ' ')"/>
									<xsl:value-of select="i18n:getTextPLPrefSuff('', 'libelle-numImmat', ' : ')"/>
									<xsl:if test="infg:isNC($cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Code)">
										<xsl:value-of select="infg:format-siren-nc($cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:Siren)"/>
									</xsl:if>
									<xsl:if test="not(infg:isNC($cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Code))">
										<xsl:value-of select="infg:format-siren($cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:Siren)"/>
									</xsl:if>
								</xsl:if>
								<xsl:if test="$cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Libelle">
									<xsl:text> </xsl:text>
									<xsl:value-of select="infg:format-status-nomGreffe(i18n:getTextByNamePL($statutRegistre), $cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Libelle)"/>
								</xsl:if>
								<xsl:if test="$cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:Complement">
									<xsl:text> </xsl:text>
									<xsl:value-of select="$cheminSiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire/infogreffe:Complement"/>
								</xsl:if>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:if>
				</xsl:if>
			</xsl:with-param>
		</xsl:call-template>
	</xsl:template>
	<xsl:template name="identificationPersonneMorale">
		<xsl:param name="statutRegistre"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="libelleGreffe"/>
		<xsl:param name="personne"/>
		<xsl:param name="statutDossier"/>
		<xsl:param name="personneType"/>
		<xsl:call-template name="section">
			<xsl:with-param name="titreKey">section-identification</xsl:with-param>
			<xsl:with-param name="informations">
				<!-- Si SiegeFrance:Greffe:Libellé est renseigné le prendre sinon prendre Greffe:Libelle -->
				<!-- num immat -->
				<xsl:call-template name="informationPersonneMorale">
					<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
					<xsl:with-param name="personne" select="$personne"/>
				</xsl:call-template>
				<!-- Variable immatriculationType -->
				<xsl:variable name="immatriculationType">
					<xsl:choose>
						<xsl:when test="$statutRegistre = $statutRegistre.RCS">infogreffe:ImmatriculationRCS</xsl:when>
						<xsl:when test="$statutRegistre = $statutRegistre.RSAC">infogreffe:ImmatriculationRSAC</xsl:when>
						<xsl:when test="$statutRegistre = $statutRegistre.RSEIRL">infogreffe:ImmatriculationRSEIRL</xsl:when>
					</xsl:choose>
				</xsl:variable>
				<xsl:if test="not($personne/*[name() = $immatriculationType]/infogreffe:TypeInscription) or $personne/*[name() = $immatriculationType]/infogreffe:TypeInscription = $typeInscription.P">
					<!-- Date d'immatriculation -->
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">dateImmatriculation</xsl:with-param>
						<xsl:with-param name="titreKey">info-dateImmat</xsl:with-param>
						<xsl:with-param name="libelle"
										select="infg:format-date($personne/*[name() = $immatriculationType]/infogreffe:DateImmatriculation)"/>
					</xsl:call-template>
					<!-- Transfert du -->
					<xsl:variable name="cheminTransfertArrivee"
								  select="//infogreffe:Transfert/infogreffe:TransfertArrivee/infogreffe:AncienGreffe/infogreffe:Libelle"/>
					<xsl:if test="$cheminTransfertArrivee">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">transfertDu</xsl:with-param>
							<xsl:with-param name="titreKey">info-transfertDu</xsl:with-param>
							<xsl:with-param name="libelle">
								<xsl:value-of select="infg:format-status-nomGreffe(i18n:getTextByNamePL($statutRegistre) ,  $cheminTransfertArrivee)"/>
								<xsl:if test="$personne/infogreffe:Transfert/infogreffe:TransfertArrivee/infogreffe:DateTransfert">
									<xsl:text> </xsl:text>
									<xsl:value-of select="i18n:getTextPL('info-enDateDu')"/>
									<xsl:text> </xsl:text>
									<xsl:value-of select="infg:format-date($personne/infogreffe:Transfert/infogreffe:TransfertArrivee/infogreffe:DateTransfert)"/>
								</xsl:if>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:if>
					<!-- Date d'immatriculation d'origine -->
					<xsl:if test="($statutDossier = $statutDossier.BCD)">
						<xsl:if test="$personne/infogreffe:ImmatriculationRCS/infogreffe:DatePremiereImmatriculation         and $personne/infogreffe:ImmatriculationRCS/infogreffe:DatePremiereImmatriculation lt $personne/infogreffe:ImmatriculationRCS/infogreffe:DateImmatriculation         and (not($personne/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription) or $personne/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.P)         and not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">constitution</xsl:with-param>
								<xsl:with-param name="titreKey">info-constitution</xsl:with-param>
								<xsl:with-param name="libelle">
									<xsl:value-of select="infg:format-date(//infogreffe:ImmatriculationRCS/infogreffe:DatePremiereImmatriculation)"/>
								</xsl:with-param>
							</xsl:call-template>
						</xsl:if>
					</xsl:if>
					<!-- Immatriculation radiée le -->
					<!-- Si Radiation:DateRadiation renseigné -->
					<xsl:if test="infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">dateRadiatation</xsl:with-param>
							<xsl:with-param name="titreKey">info-dateRadiation</xsl:with-param>
							<xsl:with-param name="libelle">
								<xsl:value-of select="infg:format-date(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)"/>
								<xsl:if test="//infogreffe:TransfertDepart/infogreffe:NouveauGreffe/infogreffe:Libelle             and infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:MotifRadiation/infogreffe:Code = $motifRadiation.R_TRANS">
									<xsl:text> </xsl:text>
									<xsl:value-of select="i18n:getTextPL('info-transfertAu')"/>
									<xsl:text> </xsl:text>
									<xsl:value-of select="infg:format-status-nomGreffe(i18n:getTextByNamePL($statutRegistre) ,  //infogreffe:TransfertDepart/infogreffe:NouveauGreffe/infogreffe:Libelle)"/>
								</xsl:if>
								<!--xsl:if	test="infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateEffetRadiation">
											<xsl:value-of select="i18n:getTextPLPrefSuff(' ', 'libelle-avecEffetAu', ' ')"/>
											<xsl:value-of select="infg:format-date(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateEffetRadiation)"/>
										</xsl:if -->
							</xsl:with-param>
						</xsl:call-template>
					</xsl:if>
				</xsl:if>
				<!-- saut de ligne -->
				<xsl:call-template name="sautLigne"/>
				<!-- Dénomination et sigle sont visibles pour $statutDossier = 'BCD' or $statutDossier = 'M' -->
				<!-- Dénomination social -->
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">denominationRaisonSociale</xsl:with-param>
					<xsl:with-param name="titreKey">info-denominationRaisonSociale</xsl:with-param>
					<xsl:with-param name="weight" select="$information.weight.bold"/>
					<xsl:with-param name="libelle">
						<xsl:value-of select="$personne/infogreffe:PersonneMorale/infogreffe:Identification/infogreffe:Denomination"/>
						<!-- Si Indicateurs:dateDissolution renseignée : afficher "(société en liquidation amiable)", sinon Si ProcédureCollective:DernierEtatProcedure in ('AA', 'A2) : afficher "(société en liquidation judiciaire)"-->
						<!--xsl:choose>
									<xsl:when test="infogreffe:DateDissolution">
										<xsl:value-of select="i18n:getTextPLPrefSuff(' (','libelle-societeLiquidationAimable',')')"/>
									</xsl:when>
									<xsl:when test="(infogreffe:DernierEtatProcedure = $etatProcedure.AA) or (infogreffe:DernierEtatProcedure = $etatProcedure.A2)">
										<xsl:value-of select="i18n:getTextPLPrefSuff(' (','libelle-societeLiquidationJudiciaire',')')"/>
									</xsl:when>
								</xsl:choose-->
					</xsl:with-param>
				</xsl:call-template>
				<!-- Sigle -->
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">sigleSociete</xsl:with-param>
					<xsl:with-param name="titreKey">info-sigle</xsl:with-param>
					<xsl:with-param name="libelle"
									select="$personne/infogreffe:PersonneMorale/infogreffe:Identification/infogreffe:Sigle"/>
				</xsl:call-template>
				<!-- nom de domaine -->
				<xsl:call-template name="informationNomDeDomaine">
					<xsl:with-param name="ligne">nomDeDomaine</xsl:with-param>
					<xsl:with-param name="titreKey">info-nomDeDomaine</xsl:with-param>
					<xsl:with-param name="libelle"
									select="$personne/infogreffe:PersonneMorale/infogreffe:Identification/infogreffe:SiteInternet"/>
				</xsl:call-template>
				<!-- Forme juridique + éventuellement "Associé unique" -->
				<!-- Si code != ZZZh -->
				<xsl:variable name="formeJuridique"
							  select="$personne/infogreffe:PersonneMorale/infogreffe:Identification/infogreffe:FormeJuridique"/>
				<xsl:if test="not($formeJuridique/infogreffe:Code = $formeJuridique.ZZZh)">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">formeJuridique</xsl:with-param>
						<xsl:with-param name="titreKey">
							<xsl:choose>
								<xsl:when test="$formeJuridique/infogreffe:Code = $formeJuridique.EPICh">
									<xsl:text>info-formEntreprise</xsl:text>
								</xsl:when>
								<xsl:otherwise>
									<xsl:text>info-formJuridique</xsl:text>
								</xsl:otherwise>
							</xsl:choose>
						</xsl:with-param>
						<xsl:with-param name="libelle">
							<xsl:value-of select="$formeJuridique/infogreffe:Libelle"/>
							<xsl:if test="$personne/infogreffe:PersonneMorale/infogreffe:Identification/infogreffe:AssocieUnique[text() = true()]">
								<xsl:text> (</xsl:text>
								<xsl:value-of select="i18n:getTextPL('libelle-societeAssocieUnique')"/>
								<xsl:text>)</xsl:text>
							</xsl:if>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
				<!-- Forme de l'entreprise Sélectioninfogreffe:Indicateurs des observations non supprimées
							et révélables  de type agrgégé FORME_EPIC tri par ordre -->
				<xsl:if test="infg:hasObservationVisible($mentionTypeAggrege.FORME_EPIC , $cheminObservation)">
					<xsl:call-template name="observationsSansTitre">
						<xsl:with-param name="ligne">formeEntreprise</xsl:with-param>
						<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.FORME_EPIC"/>
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					</xsl:call-template>
				</xsl:if>
				<xsl:if test="$personne/infogreffe:PersonneMorale/infogreffe:Indicateurs/infogreffe:EconomieSocialeSolidaire = true()">
					<xsl:call-template name="complement">
						<xsl:with-param name="libelleKey">libelle-entrepriseEconomieSocialeSolidaire</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
				<xsl:if test="$personne/infogreffe:PersonneMorale/infogreffe:Indicateurs/infogreffe:SocieteAMission = true()">
					<xsl:call-template name="complement">
						<xsl:with-param name="libelleKey">libelle-societeAMission</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
				<!-- Si PM:capital:Type = V alors Capital variable (capital minimum) sinon Capital  -->
				<xsl:if test="(//infogreffe:Capital/infogreffe:Type = 'V' and //infogreffe:Capital/infogreffe:MontantActuel) or (//infogreffe:Capital/infogreffe:Type = 'F'and //infogreffe:Capital/infogreffe:Montant)">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">capitalType</xsl:with-param>
						<xsl:with-param name="titreKey">info-capital</xsl:with-param>
						<xsl:with-param name="libelle">
							<xsl:choose>
								<xsl:when test="//infogreffe:Capital/infogreffe:Type = 'V' and //infogreffe:Capital/infogreffe:MontantActuel">
									<xsl:value-of select="infg:format-money(//infogreffe:Capital/infogreffe:MontantActuel)"/>
									<xsl:text> </xsl:text>
									<xsl:value-of select="//infogreffe:Capital/infogreffe:Devise/infogreffe:Libelle"/>
								</xsl:when>
								<xsl:when test="//infogreffe:Capital/infogreffe:Type = 'F' and //infogreffe:Capital/infogreffe:Montant">
									<xsl:value-of select="infg:format-money(//infogreffe:Capital/infogreffe:Montant)"/>
									<xsl:text> </xsl:text>
									<xsl:value-of select="//infogreffe:Capital/infogreffe:Devise/infogreffe:Libelle"/>
								</xsl:when>
							</xsl:choose>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
				<!-- -->
				<xsl:if test="//infogreffe:Capital/infogreffe:Type = 'V' and //infogreffe:Capital/infogreffe:Montant">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">capitalType</xsl:with-param>
						<xsl:with-param name="titreKey">info-capitalVar</xsl:with-param>
						<xsl:with-param name="libelle">
							<xsl:value-of select="infg:format-money(//infogreffe:Capital/infogreffe:Montant)"/>
							<xsl:text> </xsl:text>
							<xsl:value-of select="//infogreffe:Capital/infogreffe:Devise/infogreffe:Libelle"/>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
				<!-- Sélection des observations non supprimées et révélables  de type agrgégé CAPITAL tri par ordre  -->
				<xsl:call-template name="observationsSansTitre">
					<xsl:with-param name="ligne">mentionsCapital</xsl:with-param>
					<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.CAPITAL"/>
					<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				</xsl:call-template>
				<!-- TODO Ajout de condition pas de observation type CAPITAL -->
				<!-- Mention de K propres < 1/2 K social => test si la DateCapitauxPropres existe -->
				<xsl:if test="not(infg:hasObservationVisible($mentionTypeAggrege.CAPITAL, $cheminObservation)) and //infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:PersonneMorale/infogreffe:Indicateurs/infogreffe:DateCapitauxPropresInferieursMoitieCapitalSocial">
					<!-- and count(//infogreffe:Extrait/infogreffe:Observation[infogreffe:TypeAgrege = $mentionTypeAggrege.CAPITAL and infogreffe:RevelableExtraits = true() and empty(infogreffe:DateSuppression)]) = 0"-->
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">capitauxPropres</xsl:with-param>
						<xsl:with-param name="titreKey">libelle-capitauxPropres</xsl:with-param>
						<xsl:with-param name="libelle">
							<xsl:value-of select="i18n:getTextPL('libelle-mentionDu')"/>
							<xsl:text> </xsl:text>
							<xsl:value-of select="infg:format-date(//infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:PersonneMorale/infogreffe:Indicateurs/infogreffe:DateCapitauxPropresInferieursMoitieCapitalSocial)"/>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
				<!-- saut de ligne -->
				<xsl:call-template name="sautLigne"/>
				<!-- Législation applicable -  Sélection des observations non supprimées et révélables  de type agrgégé LEGISLATION
							tri par ordre -->
				<xsl:call-template name="observationsSansTitre">
					<xsl:with-param name="ligne">legislationApplicable</xsl:with-param>
					<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.LEGISLATION"/>
					<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				</xsl:call-template>
				<!-- Pays, lieu et numéro d'immatriculation - Si siege à l'etranger -->
				<xsl:if test="//infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeEtranger/infogreffe:Immatriculation/infogreffe:LieuRegistre         or //infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeEtranger/infogreffe:Immatriculation/infogreffe:NumeroRegistre">
					<xsl:variable name="cheminSiegeEtranger"
								  select="//infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeEtranger/infogreffe:Immatriculation"/>
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">siegeEtranger</xsl:with-param>
						<xsl:with-param name="titreKey">info-regPubEtranger</xsl:with-param>
						<xsl:with-param name="libelle">
							<xsl:if test="$cheminSiegeEtranger/infogreffe:NumeroRegistre">
								<xsl:value-of select="$cheminSiegeEtranger/infogreffe:NumeroRegistre"/>
								<xsl:text> </xsl:text>
							</xsl:if>
							<xsl:if test="$cheminSiegeEtranger/infogreffe:LieuRegistre">
								<xsl:value-of select="$cheminSiegeEtranger/infogreffe:LieuRegistre"/>
								<xsl:text> </xsl:text>
							</xsl:if>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
				<!-- saut de ligne -->
				<xsl:call-template name="sautLigne"/>
				<xsl:variable name="codeGreffe"
							  select="//infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Code"/>
				<!-- Numéro d’identification Européen – EUID - Inscription Principal-->
				<xsl:if test="$personne/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.P         and not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)         and not(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeEtranger)          and not(infg:isNC($codeGreffe))">
					<xsl:variable name="euidSiegeFr"
								  select="//infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:PersonneMorale/infogreffe:EUIDSiegeFR"/>
					<xsl:variable name="sirenInsee" select="//infogreffe:DonneesInsee/infogreffe:Siren"/>
					<xsl:choose>
						<xsl:when test="$euidSiegeFr">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">euidSiegeFr</xsl:with-param>
								<xsl:with-param name="titreKey">info-euidSiegeFr</xsl:with-param>
								<xsl:with-param name="libelle">
									<xsl:value-of select="$euidSiegeFr"/>
								</xsl:with-param>
							</xsl:call-template>
						</xsl:when>
						<xsl:otherwise>
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">euidSiegeFr</xsl:with-param>
								<xsl:with-param name="titreKey">info-euidSiegeFr</xsl:with-param>
								<xsl:with-param name="libelle">
									<xsl:if test="$sirenInsee and $codeGreffe">
										<xsl:value-of select="concat('FR', $codeGreffe, '.', format-number(number($sirenInsee), '000000000', 'european'))"/>
									</xsl:if>
								</xsl:with-param>
							</xsl:call-template>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:if>
				<!-- Adresse Siege -->
				<xsl:if test="($statutDossier = $statutDossier.M)">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">adresseSiege</xsl:with-param>
						<xsl:with-param name="titreKey">info-adresseSiege</xsl:with-param>
						<xsl:with-param name="libelle">
							<xsl:call-template name="adresse">
								<xsl:with-param name="cheminAdresse"
												select="//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = false()             and (infogreffe:Type = $etablissementDansRessortType.SIE)]/infogreffe:Adresse"/>
							</xsl:call-template>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
				<xsl:if test="($statutDossier = $statutDossier.BCD)">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">adresseSiege</xsl:with-param>
						<xsl:with-param name="titreKey">info-adresseSiege</xsl:with-param>
						<xsl:with-param name="libelle">
							<!-- Exclure les établissements fermés EtablissementDansRessort:Ferme = 1
									Si existe EtablissementDansRessort:Type = SIE ou SEP alors prendre EtablissementDansRessort:Adresse,
									Sinon prendre SiegeouEtablissement PrincipalPPHorsRessort:Adresse -->
							<xsl:variable name="cheminEtablissementSiegeNonFerme"
										  select="//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = false()            and (infogreffe:Type = $etablissementDansRessortType.SIE            or infogreffe:Type = $etablissementDansRessortType.SEP)]"/>
							<xsl:choose>
								<xsl:when test="$cheminEtablissementSiegeNonFerme">
									<xsl:call-template name="adresse">
										<xsl:with-param name="cheminAdresse"
														select="$cheminEtablissementSiegeNonFerme[position() = 1]/infogreffe:Adresse"/>
									</xsl:call-template>
								</xsl:when>
								<xsl:otherwise>
									<xsl:call-template name="adresse">
										<xsl:with-param name="cheminAdresse"
														select="//infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Adresse"/>
									</xsl:call-template>
								</xsl:otherwise>
							</xsl:choose>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
				<!-- Numéro d’identification Européen – EUID - Inscription Secondaire-->
				<xsl:if test="$personne/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.S         and not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)         and not(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeEtranger)">
					<xsl:variable name="euidSiegeFr"
								  select="//infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:PersonneMorale/infogreffe:EUIDSiegeFR"/>
					<xsl:variable name="sirenInsee" select="//infogreffe:DonneesInsee/infogreffe:Siren"/>
					<xsl:variable name="codeGreffe"
								  select="//infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Code"/>
					<xsl:variable name="sirenSiege"
								  select="//infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:SirenSiege"/>
					<xsl:variable name="codeGreffeIdentifiant"
								  select="//infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Code"/>
					<xsl:choose>
						<xsl:when test="$euidSiegeFr">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">euidSiegeFr</xsl:with-param>
								<xsl:with-param name="titreKey">info-euidSiegeFr</xsl:with-param>
								<xsl:with-param name="libelle">
									<xsl:value-of select="$euidSiegeFr"/>
								</xsl:with-param>
							</xsl:call-template>
						</xsl:when>
						<xsl:otherwise>
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">euidSiegeFr</xsl:with-param>
								<xsl:with-param name="titreKey">info-euidSiegeFr</xsl:with-param>
								<xsl:with-param name="libelle">
									<xsl:if test="$sirenSiege and $codeGreffe and infg:isNC($codeGreffeIdentifiant)">
										<xsl:value-of select="concat('FR', $codeGreffe, '.', format-number(number($sirenSiege), '000000000', 'european'))"/>
									</xsl:if>
									<xsl:if test="$sirenInsee and $codeGreffe and not(infg:isNC($codeGreffeIdentifiant)) ">
										<xsl:value-of select="concat('FR', $codeGreffe, '.', format-number(number($sirenInsee), '000000000', 'european'))"/>
									</xsl:if>
								</xsl:with-param>
							</xsl:call-template>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:if>
				<!-- TODO revoir l'organisation du code ?? remonter template information ?? -->
				<xsl:if test="($statutDossier = $statutDossier.BCD)">
					<!-- Siège fixé dans une entreprise de domiciliation -->
					<xsl:variable name="domiciliataireType1"
								  select="//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = false()                     and (infogreffe:Type = $etablissementDansRessortType.SIE                     or infogreffe:Type = $etablissementDansRessortType.SEP)]/infogreffe:Domiciliataire"/>
					<xsl:variable name="domiciliataireType2"
								  select="//infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:Domiciliataire"/>
					<xsl:choose>
						<xsl:when test="$domiciliataireType1">
							<xsl:call-template name="siegeDomiciliation">
								<xsl:with-param name="cheminEtablissementSiegeDomiciliataire"
												select="$domiciliataireType1"/>
								<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
								<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
							</xsl:call-template>
						</xsl:when>
						<xsl:when test="$domiciliataireType2">
							<xsl:call-template name="siegeDomiciliation">
								<xsl:with-param name="cheminEtablissementSiegeDomiciliataire"
												select="$domiciliataireType2"/>
								<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
								<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
							</xsl:call-template>
						</xsl:when>
						<xsl:otherwise>
							<xsl:call-template name="siegeDomiciliation">
								<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
								<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
							</xsl:call-template>
						</xsl:otherwise>
					</xsl:choose>
					<!-- saut de ligne -->
					<xsl:call-template name="sautLigne"/>
					<!-- Si activitePrincipaleEntreprise est renseignee et que sansActiviteDepuisImmatriculation est true alors "Sans activité depuis l'immatriculation" est placée sous les activités
								 Sinon si activitePrincipaleEntreprise n'est PAS renseignée et que sansActiviteDepuisImmatriculation est true alors "Sans activité depuis l'immatriculation" est en face du titre "Principales activités de l'entreprise" -->
					<xsl:if test="//infogreffe:ActivitePrincipaleEntreprise/text()">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">activitePrincipaleEntreprise</xsl:with-param>
							<xsl:with-param name="justify" select="true()"/>
							<xsl:with-param name="titreKey">info-principalActiviteEntrep</xsl:with-param>
							<xsl:with-param name="libelle"
											select="infg:upperFirstLetter(//infogreffe:ActivitePrincipaleEntreprise)"/>
						</xsl:call-template>
					</xsl:if>
					<xsl:if test="(not($personne/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.P) or $personne/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.P)          and not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)">
						<xsl:if test="//infogreffe:Personne/infogreffe:DonneesInsee/infogreffe:NAF/infogreffe:Code">
							<xsl:call-template name="siretNAF">
								<xsl:with-param name="codeNAF"
												select="//infogreffe:Personne/infogreffe:DonneesInsee/infogreffe:NAF/infogreffe:Code"/>
							</xsl:call-template>
						</xsl:if>
					</xsl:if>
					<!-- naturePrincipActivite -->
					<xsl:if test="infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Statut = 'C'          and (//*[name() = $personneType]/infogreffe:PersonneMorale/infogreffe:ActiviteCivile = true()          or //*[name() = $personneType]/infogreffe:PersonneMorale/infogreffe:ActiviteCommerciale = true())">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">natureGIE</xsl:with-param>
							<xsl:with-param name="titreKey">info-naturePrincipActivite</xsl:with-param>
							<xsl:with-param name="libelle">
								<xsl:if test="//*[name() = $personneType]/infogreffe:PersonneMorale/infogreffe:ActiviteCivile = true()">
									<xsl:value-of select="i18n:getTextPL('libelle-civile')"/>
								</xsl:if>
								<xsl:if test="//*[name() = $personneType]/infogreffe:PersonneMorale/infogreffe:ActiviteCommerciale = true()">
									<xsl:value-of select="i18n:getTextPL('libelle-commerciale')"/>
								</xsl:if>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:if>
					<xsl:if test="//infogreffe:Personne/*[name() = $personneType]/infogreffe:Activite[infogreffe:SansActiviteDepuisImmatriculation = true()]">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">sans exercer activite</xsl:with-param>
							<xsl:with-param name="libelleKey">libelle-sansExercerActivite</xsl:with-param>
						</xsl:call-template>
					</xsl:if>
					<!-- Fin société -->
					<xsl:variable name="finSociete"
								  select="//infogreffe:Constitution/infogreffe:FinSociete"/>
					<xsl:if test="$finSociete/infogreffe:DateFinSociete          or $finSociete[infogreffe:DureeIllimitee          and infogreffe:DureeIllimitee = true()]          or $finSociete/infogreffe:Complement">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">finSociete</xsl:with-param>
							<xsl:with-param name="titreKey">info-dureePersonneMorale</xsl:with-param>
							<xsl:with-param name="libelle">
								<xsl:if test="$finSociete/infogreffe:DateFinSociete">
									<xsl:value-of select="i18n:getTextPL('common-jusquau')"/>
									<xsl:text> </xsl:text>
									<xsl:value-of select="infg:format-date($finSociete/infogreffe:DateFinSociete)"/>
								</xsl:if>
								<xsl:if test="$finSociete/infogreffe:DureeIllimitee and $finSociete/infogreffe:DureeIllimitee = true()">
									<xsl:value-of select="i18n:getTextPL('libelle-dureeIllimitee')"/>
								</xsl:if>
								<xsl:if test="not($finSociete/infogreffe:DateFinSociete)             and (not($finSociete/infogreffe:DureeIllimitee)             or not($finSociete/infogreffe:DureeIllimitee = true()))">
									<xsl:value-of select="$finSociete/infogreffe:Complement"/>
								</xsl:if>
							</xsl:with-param>
						</xsl:call-template>
						<xsl:if test="not(not($finSociete/infogreffe:DateFinSociete)         and (not($finSociete/infogreffe:DureeIllimitee)         or not($finSociete/infogreffe:DureeIllimitee = true())))">
							<xsl:call-template name="complement">
								<xsl:with-param name="libelle" select="$finSociete/infogreffe:Complement"/>
							</xsl:call-template>
						</xsl:if>
					</xsl:if>
					<!-- Date cloture excercice -->
					<xsl:if test="//infogreffe:PersonneMorale/infogreffe:DateCloture/infogreffe:DateCloture">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">dateClotureExercice</xsl:with-param>
							<xsl:with-param name="titreKey">info-dateClotureExcerciceSocial</xsl:with-param>
							<xsl:with-param name="libelle">
								<xsl:value-of select="infg:format-date-day-month-long(//infogreffe:PersonneMorale/infogreffe:DateCloture/infogreffe:DateCloture)"/>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:if>
					<!-- Date cloture exceptionel premier excercice -->
					<xsl:variable name="dateCloturePremierExcerciceSocial"
								  select="//infogreffe:PersonneMorale/infogreffe:DateCloture/infogreffe:DateClotureExceptionnelle"/>
					<xsl:variable name="dateImmat"
								  select="//infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:ImmatriculationRCS/infogreffe:DateImmatriculation"/>
					<xsl:if test="$dateCloturePremierExcerciceSocial/text() and (xs:date($dateImmat) + xs:yearMonthDuration('P3Y')) &gt; $dateDuJour">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">dateClotureExercice</xsl:with-param>
							<xsl:with-param name="titreKey">info-dateCloturePremierExcerciceSocial</xsl:with-param>
							<xsl:with-param name="libelle">
								<xsl:value-of select="infg:format-date($dateCloturePremierExcerciceSocial)"/>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:if>
					<!-- saut de ligne -->
					<xsl:call-template name="sautLigne"/>
					<!-- Publication(s) au journal officiel -->
					<xsl:call-template name="observationsSansTitre">
						<xsl:with-param name="ligne">publicationJournalOfficiel</xsl:with-param>
						<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.ACTE_EPIC"/>
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					</xsl:call-template>
					<!-- Contrat d'appui -->
					<xsl:call-template name="mentionCAPE">
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					</xsl:call-template>
				</xsl:if>
			</xsl:with-param>
		</xsl:call-template>
	</xsl:template>
	<xsl:template name="informationPersonneMorale">
		<xsl:param name="statutRegistre"/>
		<xsl:param name="personne"/>
		<xsl:variable name="immatriculationType">
			<xsl:choose>
				<xsl:when test="$statutRegistre = $statutRegistre.RCS">infogreffe:ImmatriculationRCS</xsl:when>
				<xsl:when test="$statutRegistre = $statutRegistre.RSAC">infogreffe:ImmatriculationRSAC</xsl:when>
				<xsl:when test="$statutRegistre = $statutRegistre.RSEIRL">infogreffe:ImmatriculationRSEIRL</xsl:when>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="numeroGreffe"
					  select="//infogreffe:Dossier/infogreffe:Identifiant/infogreffe:Greffe"/>
		<xsl:variable name="immatriculationSecondaire"
					  select="$personne/*[name() = $immatriculationType]/infogreffe:TypeInscription = $typeInscription.S"/>
		<!--
Si  Inscription Principale,  TypeInscription = 'P'  : afficher DonneesInsee:Siren + Identifiant:Greffe:Libelle
-->
		<xsl:if test="$immatriculationSecondaire = false()">
			<xsl:variable name="libelleGreffe"
						  select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Libelle"/>
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">numeroImmatriculation</xsl:with-param>
				<!-- <xsl:with-param name="titreKey">info-numImmat</xsl:with-param> -->
				<!-- 20170306 XAS Mantis 47712 Afficher le bon registre sur l'extrait -->
				<xsl:with-param name="titre">
					<xsl:choose>
						<xsl:when test="$statutRegistre=$statutRegistre.RSAC">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RSAC')"/>
						</xsl:when>
						<xsl:when test="$statutRegistre=$statutRegistre.RSEIRL">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RSEIRL')"/>
						</xsl:when>
						<xsl:when test="$statutRegistre=$statutRegistre.RCS">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RCS')"/>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="i18n:getTextPL('info-numImmat')"/>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:choose>
						<xsl:when test="//infogreffe:DonneesInsee/infogreffe:Siren">
							<xsl:if test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
								<xsl:value-of select="infg:format-siren-nc(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
							</xsl:if>
							<xsl:if test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe))">
								<xsl:value-of select="infg:format-siren(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
							</xsl:if>
							<xsl:if test="$libelleGreffe">
								<xsl:text> </xsl:text>
								<xsl:value-of select="concat(i18n:getTextByNamePL($statutRegistre) , ' ', $libelleGreffe)"/>
							</xsl:if>
						</xsl:when>
						<xsl:when test="infg:isTahiti($numeroGreffe)">
							<xsl:value-of select="infg:format-identifiant-tahiti(infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant)"/>
							<xsl:text> </xsl:text>
							<xsl:value-of select="concat(i18n:getTextByNamePL($statutRegistre) , ' ', $libelleGreffe)"/>
						</xsl:when>
						<xsl:when test="not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)        and infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Millesime &gt; 2010        and not(infg:isTahiti($numeroGreffe))">
							<xsl:value-of select="i18n:getTextPL('libelle-enCoursAttribution')"/>
						</xsl:when>
					</xsl:choose>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
		<!--
Si  Inscription Secondaire, TypeInscription = 'S' :
-->
		<xsl:if test="$immatriculationSecondaire = true()">
			<!--
- si le greffe du dossier est 9812 : afficher  DonneesInsee:Siren + Identifiant:Greffe:Libelle
Sinon (greffe du dossier différent de 9812) :
- afficher  DonneesInsee:Siren + SiegeOuEtablissementPrincipalPPHorsRessort:SiegeFrance:Greffe:Libelle
		-->
			<xsl:variable name="Siren">
				<xsl:choose>
					<xsl:when test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
						<xsl:value-of select="infg:format-siren-nc(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
					</xsl:when>
					<xsl:when test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)) and not(infg:isNC(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Code))">
						<xsl:value-of select="infg:format-siren(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
					</xsl:when>
					<xsl:otherwise>
						<xsl:value-of select="infg:format-siren(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:variable>
			<xsl:variable name="libellegr"> <!-- ? -->
				<xsl:choose>
					<xsl:when test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
						<xsl:value-of select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Libelle"/>
					</xsl:when>
					<xsl:when test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)) and not(infg:isNC(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Code))">
						<xsl:value-of select="infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Libelle"/>
					</xsl:when>
					<xsl:otherwise>
						<xsl:value-of select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Libelle"/>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:variable>
			<xsl:variable name="SirenSiege">
				<xsl:choose>
					<xsl:when test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
						<xsl:value-of select="infg:format-siren-nc(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:SirenSiege)"/>
					</xsl:when>
					<xsl:when test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)) and infg:isNC(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Code)">
						<xsl:value-of select="infg:format-siren-nc(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:SirenSiege)"/>
					</xsl:when>
				</xsl:choose>
			</xsl:variable>
			<xsl:variable name="libelleSiege">
				<xsl:choose>
					<xsl:when test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
						<xsl:value-of select="infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Libelle"/>
					</xsl:when>
					<xsl:when test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe))  and infg:isNC(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Code)">
						<xsl:value-of select="infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Libelle"/>
					</xsl:when>
				</xsl:choose>
			</xsl:variable>
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">numeroImmatriculation</xsl:with-param>
				<!-- <xsl:with-param name="titreKey">info-numImmat</xsl:with-param> -->
				<!-- 20170306 XAS Mantis 47712 Afficher le bon registre sur l'extrait -->
				<xsl:with-param name="titre">
					<xsl:choose>
						<xsl:when test="$statutRegistre=$statutRegistre.RSAC">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RSAC')"/>
						</xsl:when>
						<xsl:when test="$statutRegistre=$statutRegistre.RSEIRL">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RSEIRL')"/>
						</xsl:when>
						<xsl:when test="$statutRegistre=$statutRegistre.RCS">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RCS')"/>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="i18n:getTextPL('info-numImmat')"/>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:choose>
						<xsl:when test="$Siren">
							<xsl:value-of select="$Siren"/>
							<xsl:if test="$libellegr">
								<xsl:text> </xsl:text>
								<xsl:value-of select="concat(i18n:getTextByNamePL($statutRegistre) , ' ', $libellegr)"/>
							</xsl:if>
						</xsl:when>
						<xsl:when test="not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)        and infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Millesime &gt; 2010">
							<xsl:value-of select="i18n:getTextPL('libelle-enCoursAttribution')"/>
						</xsl:when>
					</xsl:choose>
				</xsl:with-param>
			</xsl:call-template>
			<!--
Si  Inscription Secondaire, TypeInscription = 'S' :

- si le greffe du dossier est 9812 alors  afficher SiegeOuEtablissementPrincipalPPHorsRessort:SiegeFrance:SirenSiege + SiegeOuEtablissementPrincipalPPHorsRessort:SiegeFrance:Greffe:Libelle

- si le greffe du dossier est différent de 9812 et que  SiegeFrance:Greffe = '9812'
alors  afficher SiegeOuEtablissementPrincipalPPHorsRessort:SiegeFrance:SirenSiege + SiegeOuEtablissementPrincipalPPHorsRessort:SiegeFrance:Greffe:Libelle
-->
			<xsl:if test="not($SirenSiege= 'NaN') and (infg:isNC(infogreffe:Identifiant/infogreffe:Greffe) or infg:isNC(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Code))">
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">numeroImmatriculationPrincale</xsl:with-param>
					<!-- <xsl:with-param name="titreKey">info-numImmat</xsl:with-param> -->
					<!-- 20170306 XAS Mantis 47712 Afficher le bon registre sur l'extrait -->
					<xsl:with-param name="titre">
						<xsl:choose>
							<xsl:when test="$statutRegistre=$statutRegistre.RSAC">
								<xsl:value-of select="i18n:getTextPL('info-numImmat-RSAC')"/>
							</xsl:when>
							<xsl:when test="$statutRegistre=$statutRegistre.RSEIRL">
								<xsl:value-of select="i18n:getTextPL('info-numImmat-RSEIRL')"/>
							</xsl:when>
							<xsl:when test="$statutRegistre=$statutRegistre.RCS">
								<xsl:value-of select="i18n:getTextPL('info-numImmatPrincipale-RCS')"/>
							</xsl:when>
							<xsl:otherwise>
								<xsl:value-of select="i18n:getTextPL('info-numImmat')"/>
							</xsl:otherwise>
						</xsl:choose>
					</xsl:with-param>
					<xsl:with-param name="libelle">
						<xsl:choose>
							<xsl:when test="$SirenSiege">
								<xsl:value-of select="$SirenSiege"/>
								<xsl:if test="$libelleSiege">
									<xsl:text> </xsl:text>
									<xsl:value-of select="concat(i18n:getTextByNamePL($statutRegistre) , ' ', $libelleSiege)"/>
								</xsl:if>
							</xsl:when>
							<xsl:when test="not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)         and infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Millesime &gt; 2010">
								<xsl:value-of select="i18n:getTextPL('libelle-enCoursAttribution')"/>
							</xsl:when>
						</xsl:choose>
					</xsl:with-param>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
	</xsl:template>
	<xsl:template name="informationPersonnePhysique">
		<xsl:param name="statutRegistre"/>
		<xsl:param name="personne"/>
		<xsl:variable name="immatriculationType">
			<xsl:choose>
				<xsl:when test="$statutRegistre = $statutRegistre.RCS">infogreffe:ImmatriculationRCS</xsl:when>
				<xsl:when test="$statutRegistre = $statutRegistre.RSAC">infogreffe:ImmatriculationRSAC</xsl:when>
				<xsl:when test="$statutRegistre = $statutRegistre.RSEIRL">infogreffe:ImmatriculationRSEIRL</xsl:when>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="immatriculationSecondaire"
					  select="$personne/*[name() = $immatriculationType]/infogreffe:TypeInscription = $typeInscription.S"/>
		<xsl:variable name="numeroGreffe"
					  select="//infogreffe:Dossier/infogreffe:Identifiant/infogreffe:Greffe"/>
		<!--
Si  Inscription Principale,  TypeInscription = 'P'  : afficher DonneesInsee:Siren + Identifiant:Greffe:Libelle
-->
		<xsl:if test="$immatriculationSecondaire = false()">
			<xsl:variable name="libelleGreffe"
						  select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Libelle"/>
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">numeroImmatriculation</xsl:with-param>
				<!-- <xsl:with-param name="titreKey">info-numImmat</xsl:with-param> -->
				<!-- 20170306 XAS Mantis 47712 Afficher le bon registre sur l'extrait -->
				<xsl:with-param name="titre">
					<xsl:choose>
						<xsl:when test="$statutRegistre=$statutRegistre.RSAC">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RSAC')"/>
						</xsl:when>
						<xsl:when test="$statutRegistre=$statutRegistre.RSEIRL">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RSEIRL')"/>
						</xsl:when>
						<xsl:when test="$statutRegistre=$statutRegistre.RCS">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RCS')"/>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="i18n:getTextPL('info-numImmat')"/>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:choose>
						<xsl:when test="//infogreffe:DonneesInsee/infogreffe:Siren">
							<xsl:if test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
								<xsl:value-of select="infg:format-siren-nc(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
							</xsl:if>
							<xsl:if test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe))">
								<xsl:value-of select="infg:format-siren(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
							</xsl:if>
							<xsl:if test="$libelleGreffe">
								<xsl:text> </xsl:text>
								<xsl:value-of select="concat(i18n:getTextByNamePL($statutRegistre) , ' ', $libelleGreffe)"/>
							</xsl:if>
						</xsl:when>
						<xsl:when test="infg:isTahiti($numeroGreffe)">
							<xsl:value-of select="infg:format-identifiant-tahiti(infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant)"/>
							<xsl:text> </xsl:text>
							<xsl:value-of select="concat(i18n:getTextByNamePL($statutRegistre) , ' ', $libelleGreffe)"/>
						</xsl:when>
						<xsl:when test="not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)           and infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Millesime &gt; 2010           and not(infg:isTahiti($numeroGreffe))">
							<xsl:value-of select="i18n:getTextPL('libelle-enCoursAttribution')"/>
						</xsl:when>
					</xsl:choose>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
		<!--
Si  Inscription Secondaire, TypeInscription = 'S' :
-->
		<xsl:if test="$immatriculationSecondaire = true()">
			<!--
- si le greffe du dossier est 9812 : afficher  DonneesInsee:Siren + Identifiant:Greffe:Libelle
Sinon (greffe du dossier différent de 9812) :
- afficher  DonneesInsee:Siren + SiegeOuEtablissementPrincipalPPHorsRessort:SiegeFrance:Greffe:Libelle
		-->
			<xsl:variable name="Siren">
				<xsl:choose>
					<xsl:when test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
						<xsl:value-of select="infg:format-siren-nc(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
					</xsl:when>
					<xsl:when test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)) and not(infg:isNC(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Code))">
						<xsl:value-of select="infg:format-siren(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
					</xsl:when>
					<xsl:otherwise>
						<xsl:value-of select="infg:format-siren(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:variable>
			<xsl:variable name="libellegr"> <!-- ? -->
				<xsl:choose>
					<xsl:when test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
						<xsl:value-of select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Libelle"/>
					</xsl:when>
					<xsl:when test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)) and not(infg:isNC(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Code))">
						<xsl:value-of select="infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Libelle"/>
					</xsl:when>
					<xsl:otherwise>
						<xsl:value-of select="infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Libelle"/>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:variable>
			<xsl:variable name="SirenSiege">
				<xsl:choose>
					<xsl:when test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
						<xsl:value-of select="infg:format-siren-nc(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:SirenSiege)"/>
					</xsl:when>
					<xsl:when test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)) and infg:isNC(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Code)">
						<xsl:value-of select="infg:format-siren-nc(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:SirenSiege)"/>
					</xsl:when>
				</xsl:choose>
			</xsl:variable>
			<xsl:variable name="libelleSiege">
				<xsl:choose>
					<xsl:when test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
						<xsl:value-of select="infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Libelle"/>
					</xsl:when>
					<xsl:when test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe))  and infg:isNC(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Code)">
						<xsl:value-of select="infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe/infogreffe:Libelle"/>
					</xsl:when>
				</xsl:choose>
			</xsl:variable>
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">numeroImmatriculation</xsl:with-param>
				<!-- <xsl:with-param name="titreKey">info-numImmat</xsl:with-param> -->
				<!-- 20170306 XAS Mantis 47712 Afficher le bon registre sur l'extrait -->
				<xsl:with-param name="titre">
					<xsl:choose>
						<xsl:when test="$statutRegistre=$statutRegistre.RSAC">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RSAC')"/>
						</xsl:when>
						<xsl:when test="$statutRegistre=$statutRegistre.RSEIRL">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RSEIRL')"/>
						</xsl:when>
						<xsl:when test="$statutRegistre=$statutRegistre.RCS">
							<xsl:value-of select="i18n:getTextPL('info-numImmat-RCS')"/>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="i18n:getTextPL('info-numImmat')"/>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:choose>
						<xsl:when test="$Siren">
							<xsl:value-of select="$Siren"/>
							<xsl:if test="$libellegr">
								<xsl:text> </xsl:text>
								<xsl:value-of select="concat(i18n:getTextByNamePL($statutRegistre) , ' ', $libellegr)"/>
							</xsl:if>
						</xsl:when>
						<xsl:when test="not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)        and infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Millesime &gt; 2010">
							<xsl:value-of select="i18n:getTextPL('libelle-enCoursAttribution')"/>
						</xsl:when>
					</xsl:choose>
				</xsl:with-param>
			</xsl:call-template>
			<!--
Si  Inscription Secondaire, TypeInscription = 'S' :

- si le greffe du dossier est 9812 alors  afficher SiegeOuEtablissementPrincipalPPHorsRessort:SiegeFrance:SirenSiege + SiegeOuEtablissementPrincipalPPHorsRessort:SiegeFrance:Greffe:Libelle

- si le greffe du dossier est différent de 9812 et que  SiegeFrance:Greffe = '9812'
alors  afficher SiegeOuEtablissementPrincipalPPHorsRessort:SiegeFrance:SirenSiege + SiegeOuEtablissementPrincipalPPHorsRessort:SiegeFrance:Greffe:Libelle
-->
			<xsl:if test="not($SirenSiege= 'NaN') and (infg:isNC(infogreffe:Identifiant/infogreffe:Greffe) or infg:isNC(infogreffe:Extrait/infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeFrance/infogreffe:Greffe))">
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">numeroImmatriculationPrincale</xsl:with-param>
					<!-- <xsl:with-param name="titreKey">info-numImmat</xsl:with-param> -->
					<!-- 20170306 XAS Mantis 47712 Afficher le bon registre sur l'extrait -->
					<xsl:with-param name="titre">
						<xsl:choose>
							<xsl:when test="$statutRegistre=$statutRegistre.RSAC">
								<xsl:value-of select="i18n:getTextPL('info-numImmat-RSAC')"/>
							</xsl:when>
							<xsl:when test="$statutRegistre=$statutRegistre.RSEIRL">
								<xsl:value-of select="i18n:getTextPL('info-numImmat-RSEIRL')"/>
							</xsl:when>
							<xsl:when test="$statutRegistre=$statutRegistre.RCS">
								<xsl:value-of select="i18n:getTextPL('info-numImmatPrincipale-RCS')"/>
							</xsl:when>
							<xsl:otherwise>
								<xsl:value-of select="i18n:getTextPL('info-numImmat')"/>
							</xsl:otherwise>
						</xsl:choose>
					</xsl:with-param>
					<xsl:with-param name="libelle">
						<xsl:choose>
							<xsl:when test="$SirenSiege">
								<xsl:value-of select="$SirenSiege"/>
								<xsl:if test="$libelleSiege">
									<xsl:text> </xsl:text>
									<xsl:value-of select="concat(i18n:getTextByNamePL($statutRegistre) , ' ', $libelleSiege)"/>
								</xsl:if>
							</xsl:when>
							<xsl:when test="not(infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)        and infogreffe:Identifiant/infogreffe:NumeroGestion/infogreffe:Millesime &gt; 2010">
								<xsl:value-of select="i18n:getTextPL('libelle-enCoursAttribution')"/>
							</xsl:when>
						</xsl:choose>
					</xsl:with-param>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
	</xsl:template>
	<!-- FIN TEMPLATE IDENTIFICATION -->
	<!-- Templates Utilitaires -->
	<xsl:variable name="personneMorale.mode.CAPE">cape</xsl:variable>
	<xsl:variable name="personneMorale.mode.FUSIONSCISSION">FUSIONSCISSION</xsl:variable>
	<xsl:variable name="personneMorale.mode.OrigineFonds">OrigineFonds</xsl:variable>
	<xsl:variable name="personneMorale.mode.ModeExploitation">ModeExploitation</xsl:variable>
	<xsl:template name="personneMorale">
		<xsl:param name="cheminPersonneMorale"/>
		<xsl:param name="titreDeno"/>
		<xsl:param name="bloc"/>
		<xsl:param name="type"/>
		<xsl:param name="fonctionCodes"/>
		<xsl:param name="mode"/>
		<!-- Denomination -->
		<xsl:call-template name="information">
			<xsl:with-param name="ligne">ligne</xsl:with-param>
			<xsl:with-param name="titre" select="$titreDeno"/>
			<xsl:with-param name="bloc" select="$bloc"/>
			<xsl:with-param name="libelle">
				<xsl:value-of select="$cheminPersonneMorale/infogreffe:Identification/infogreffe:Denomination"/>
			</xsl:with-param>
		</xsl:call-template>
		<!-- sigle -->
		<xsl:if test="$mode != $personneMorale.mode.CAPE and infg:in-list($fonctionCode.MEMh , $fonctionCodes)">
			<xsl:call-template name="information">
				<xsl:with-param name="bloc" select="$bloc"/>
				<xsl:with-param name="ligne">sigleSociete</xsl:with-param>
				<xsl:with-param name="titreKey">info-sigle</xsl:with-param>
				<xsl:with-param name="libelle"
								select="$cheminPersonneMorale/infogreffe:Identification/infogreffe:Sigle"/>
			</xsl:call-template>
		</xsl:if>
		<!-- nom de domaine -->
		<xsl:call-template name="informationNomDeDomaine">
			<xsl:with-param name="bloc" select="$bloc"/>
			<xsl:with-param name="ligne">nomDeDomaine</xsl:with-param>
			<xsl:with-param name="titreKey">info-nomDeDomaine</xsl:with-param>
			<xsl:with-param name="libelle"
							select="$cheminPersonneMorale/infogreffe:Identification/infogreffe:SiteInternet"/>
		</xsl:call-template>
		<xsl:if test="$mode != $personneMorale.mode.CAPE">
			<!-- Mail Bea 20170602 en liaison avec mantis 48320/mantis 27859 coorection d'apres ce qui est ecrit ds 27859
			and $mode != $personneMorale.mode.OrigineFonds and $mode != $personneMorale.mode.ModeExploitation">
			-->
			<xsl:variable name="formeJuridique"
						  select="$cheminPersonneMorale/infogreffe:Identification/infogreffe:FormeJuridique"/>
			<!-- si code != ZZZh && infogreffe:PersonneMorale/infogreffe:Identification/infogreffe:FormeJuridique/infogreffe:Libelle -->
			<xsl:if test="not($formeJuridique/infogreffe:Code = $formeJuridique.ZZZh) and $formeJuridique/infogreffe:Libelle">
				<!-- si PERS_APPUI INDIVISAIRE LOUEUR MANDANT MANDATAIRE PROPRIO_INDIVIS : ne pas afficher la forme juridique // XAS 20170904 Mantis 48320 ajout de $intervenantType.PRECEDENT_EXPLOIT, $intervenantType.PRECEDENT_PROEXP et $intervenantType.PRECEDENT_PROPRIO, dans la liste ou on n'affiche pas le FJ-->
				<xsl:if test="not($type) or ($type and not(infg:in-list($type, ($intervenantType.PERS_APPUI, $intervenantType.INDIVISAIRE, $intervenantType.LOUEUR, $intervenantType.MANDANT, $intervenantType.MANDATAIRE, $intervenantType.PROPRIO_INDIVIS, $intervenantType.PRECEDENT_EXPLOIT,$intervenantType.PRECEDENT_PROPRIO,$intervenantType.PRECEDENT_PROEXP))))">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">ligne</xsl:with-param>
						<xsl:with-param name="bloc" select="$bloc"/>
						<xsl:with-param name="titreKey">
							<xsl:choose>
								<xsl:when test="$formeJuridique/infogreffe:Code = $formeJuridique.EPICh">
									<xsl:text>info-formEntreprise</xsl:text>
								</xsl:when>
								<xsl:otherwise>
									<xsl:text>info-formJuridique</xsl:text>
								</xsl:otherwise>
							</xsl:choose>
						</xsl:with-param>
						<xsl:with-param name="libelle">
							<xsl:value-of select="$formeJuridique/infogreffe:Libelle"/>
							<xsl:if test="$mode != $personneMorale.mode.CAPE">
								<xsl:if test="$cheminPersonneMorale/infogreffe:Identification/infogreffe:AssocieUnique[text() = true()]">
									<xsl:text> (</xsl:text>
									<xsl:value-of select="i18n:getTextPL('libelle-societeAssocieUnique')"/>
									<xsl:text>)</xsl:text>
								</xsl:if>
							</xsl:if>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
			</xsl:if>
		</xsl:if>
		<!-- si infogreffe:PersonneMorale/infogreffe:Adresse -->
		<xsl:if test="$cheminPersonneMorale/infogreffe:Adresse">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">ligne</xsl:with-param>
				<xsl:with-param name="bloc" select="$bloc"/>
				<xsl:with-param name="titreKey">
					<xsl:choose>
						<xsl:when test="infg:in-list($mode, ($personneMorale.mode.CAPE, $personneMorale.mode.FUSIONSCISSION)) or infg:in-list($fonctionCode.MEMh , $fonctionCodes)        or (infg:in-list($mode, ($personneMorale.mode.OrigineFonds, $personneMorale.mode.ModeExploitation)) and infg:in-list($type, ($intervenantType.MANDANT, $intervenantType.MANDATAIRE)))">
							<xsl:text>info-adresseSiegeSocial</xsl:text>
						</xsl:when>
						<xsl:otherwise>
							<xsl:text>info-adresse</xsl:text>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:call-template name="adresse">
						<xsl:with-param name="cheminAdresse" select="$cheminPersonneMorale/infogreffe:Adresse"/>
					</xsl:call-template>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
		<!-- si Extrait:Observation:Intervenant:PersonneMorale:ImmatriculationFrance -->
		<xsl:if test="$cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:RegistreImmatriculation or $cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Libelle or $cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren">
			<!-- si INDIVISAIRE LOUEUR PROPRIO_INDIVIS : ne pas afficher la forme juridique : ne pas afficher le n° d'immatriculation -->
			<xsl:if test="not($type) or ($type and not(infg:in-list($type , ($intervenantType.INDIVISAIRE,$intervenantType.LOUEUR, $intervenantType.PROPRIO_INDIVIS))))">
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">ligne</xsl:with-param>
					<xsl:with-param name="bloc" select="$bloc"/>
					<xsl:with-param name="titreKey">
						<xsl:choose>
							<!-- Numéro unique d'identification : Afficher si ImmatriculationFrance renseigné mais Greffe:Code non renseigné + Afficher Siren-->
							<xsl:when test="(infg:in-list($mode, ($personneMorale.mode.OrigineFonds, $personneMorale.mode.ModeExploitation)) and infg:in-list($type , ($intervenantType.PRECEDENT_EXPLOIT , $intervenantType.PRECEDENT_PROEXP)))">
								<xsl:text>info-numUniqueIdentification</xsl:text>
							</xsl:when>
							<xsl:when test="not($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren)">
								<xsl:text>info-immatRCS</xsl:text>
							</xsl:when>
							<!--since AN2019_KBIS_EVOL_REPRESENANTS-->
							<xsl:when test="$cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren">
								<xsl:if test="$cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Code or  ($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:RegistreImmatriculation eq 'RCS')">
									<xsl:text>info-numImmat</xsl:text>
								</xsl:if>
								<xsl:if test="not($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Code or $cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:RegistreImmatriculation eq 'RCS')">
									<xsl:text>info-numImmatEtranger</xsl:text>
								</xsl:if>
							</xsl:when>
							<xsl:otherwise>
								<xsl:text>info-numImmat</xsl:text>
							</xsl:otherwise>
						</xsl:choose>
					</xsl:with-param>
					<xsl:with-param name="libelle">
						<xsl:if test="$cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren">
							<!--xsl:value-of select="infg:format-siren($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/-->
							<!--since AN2018_WEB_NC_RIDET-->
							<xsl:if test="$cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Code">
								<xsl:if test="infg:isNC($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Code)">
									<xsl:value-of select="infg:format-siren-nc($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/>
								</xsl:if>
								<xsl:if test="not(infg:isNC($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Code))">
									<xsl:value-of select="infg:format-siren($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/>
								</xsl:if>
							</xsl:if>
							<!--since AN2018_WEB_NC_RIDET-->
							<!--Si le code greffe est absent, si le dossier pour lequel on restitue le kbis est dans le greffe de Nouvelle CalÃ©donie et que le n identification d'un dirigeant a une longueur
                            infÃ©rieure ou Ã©gale Ã  7 chiffres, c'est un RIDET  pas de padding par des 0 sur 9 chiffres.Mantis(0055257)/-->
							<xsl:if test="not($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Code)">
								<xsl:if test="infg:isNC(/infogreffe:Dossier/infogreffe:Identifiant/infogreffe:Greffe)">
									<xsl:if test="string-length(string(number($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren)))&lt;8">
										<xsl:value-of select="infg:format-siren-nc($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/>
									</xsl:if>
									<xsl:if test="string-length(string(number($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren)))&gt;7">
										<xsl:value-of select="infg:format-siren($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/>
									</xsl:if>
								</xsl:if>
								<xsl:if test="not(infg:isNC(/infogreffe:Dossier/infogreffe:Identifiant/infogreffe:Greffe))">
									<xsl:value-of select="infg:format-siren($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/>
								</xsl:if>
							</xsl:if>
						</xsl:if>
						<!-- Numéro unique d'identification : Afficher si ImmatriculationFrance renseigné mais Greffe:Code non renseigné + Afficher Siren-->
						<!-- si PRECEDENT_EXPLOIT PRECEDENT_PROEXP : afficher le n° d'immatriculation sans le type de registre(RegistreImmatriculation) et le lieu de registre (LieuRegistre) -->
						<xsl:if test="not($type) or ($type and not(infg:in-list($type , ($intervenantType.PRECEDENT_EXPLOIT , $intervenantType.PRECEDENT_PROEXP))))">
							<xsl:text> </xsl:text>
							<xsl:value-of select="$cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:RegistreImmatriculation"/>
							<xsl:text> </xsl:text>
							<xsl:if test="$cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Code">
								<xsl:value-of select="$cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Libelle"/>
							</xsl:if>
							<xsl:if test="not($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Code) and ($cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:LieuRegistre)">
								<xsl:value-of select="$cheminPersonneMorale/infogreffe:ImmatriculationFrance/infogreffe:LieuRegistre"/>
							</xsl:if>
						</xsl:if>
					</xsl:with-param>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
		<!-- si Extrait:Observation:Intervenant:PersonneMorale:ImmatriculationEtranger -->
		<xsl:if test="not(infg:in-list($mode, ($personneMorale.mode.OrigineFonds, $personneMorale.mode.ModeExploitation, $personneMorale.mode.CAPE))) and $cheminPersonneMorale/infogreffe:ImmatriculationEtranger and $cheminPersonneMorale/infogreffe:ImmatriculationEtranger/*">
			<!-- si INDIVISAIRE LOUEUR PROPRIO_INDIVIS : ne pas afficher la forme juridique : ne pas afficher le n° d'immatriculation -->
			<xsl:if test="not($type) or ($type and not(infg:in-list($type, ($intervenantType.INDIVISAIRE, $intervenantType.LOUEUR, $intervenantType.PROPRIO_INDIVIS))))">
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">ligne</xsl:with-param>
					<xsl:with-param name="bloc" select="$bloc"/>
					<xsl:with-param name="titreKey">info-numImmatEtranger</xsl:with-param>
					<xsl:with-param name="libelle">
						<xsl:value-of select="$cheminPersonneMorale/infogreffe:ImmatriculationEtranger/infogreffe:NumeroRegistre"/>
						<!-- si PRECEDENT_EXPLOIT PRECEDENT_PROEXP : afficher le n° d'immatriculation sans le type de registre(RegistreImmatriculation) et le lieu de registre (LieuRegistre) -->
						<xsl:if test="not($type) or ($type and not(infg:in-list($type, ($intervenantType.PRECEDENT_EXPLOIT, $intervenantType.PRECEDENT_PROEXP))))">
							<xsl:text> </xsl:text>
							<xsl:value-of select="$cheminPersonneMorale/infogreffe:ImmatriculationEtranger/infogreffe:RegistreImmatriculation"/>
							<xsl:text> </xsl:text>
							<xsl:value-of select="$cheminPersonneMorale/infogreffe:ImmatriculationEtranger/infogreffe:LieuRegistre"/>
						</xsl:if>
					</xsl:with-param>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
	</xsl:template>
	<xsl:variable name="personnePhysique.mode.RepresentantPermanentPP">RepresentantPermanentPP</xsl:variable>
	<xsl:variable name="personnePhysique.mode.OrigineFonds">OrigineFonds</xsl:variable>
	<xsl:variable name="personnePhysique.mode.ModeExploitation">ModeExploitation</xsl:variable>
	<xsl:variable name="personnePhysique.mode.RepresentantPP">RepresentantPP</xsl:variable>
	<xsl:template name="personnePhysique">
		<xsl:param name="cheminPersonnePhysique"/>
		<xsl:param name="elementTitre"/>
		<xsl:param name="bloc"/>
		<xsl:param name="defautLibelleNom"/>
		<xsl:param name="type"/>
		<xsl:param name="fonctionCodes"/>
		<xsl:param name="mode"/>
		<xsl:variable name="identification"
					  select="$cheminPersonnePhysique/infogreffe:Identification"/>
		<!-- Nom et prénoms -->
		<xsl:call-template name="nomPrenoms">
			<xsl:with-param name="identification" select="$identification"/>
			<xsl:with-param name="attributeLigne"/>
			<xsl:with-param name="elementTitre" select="$elementTitre"/>
			<xsl:with-param name="bloc" select="$bloc"/>
			<!-- xsl:with-param name="defautLibelleNom" select="$defautLibelleNom" /-->
		</xsl:call-template>
		<!-- Nom d'usage -->
		<xsl:call-template name="nomUsage">
			<xsl:with-param name="identification"
							select="$cheminPersonnePhysique/infogreffe:Identification"/>
			<xsl:with-param name="attributeLigne">ligne</xsl:with-param>
			<xsl:with-param name="bloc" select="$bloc"/>
		</xsl:call-template>
		<!-- Pseudonyme -->
		<xsl:if test="$cheminPersonnePhysique/infogreffe:Identification/infogreffe:Pseudonyme">
			<!-- si MANDANT et MANDATAIRE (PP) : ne pas afficher le pseudonyme -->
			<xsl:if test="not($type) or ($type and not(infg:in-list($type, ($intervenantType.MANDANT, $intervenantType.MANDATAIRE))))">
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">pseudonyme</xsl:with-param>
					<xsl:with-param name="bloc" select="$bloc"/>
					<xsl:with-param name="titreKey">info-pseudonyme</xsl:with-param>
					<xsl:with-param name="libelle"
									select="$cheminPersonnePhysique/infogreffe:Identification/infogreffe:Pseudonyme"/>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
		<!-- INDIVISAIRE LOUEUR MANDANT MANDATAIRE PROPRIO_INDIVIS HERIT PRECEDENT_EXPLOIT PRECEDENT_PROEXP (PP) : ne pas afficher la date et le lieu de naissance + ne pas afficher la nationalité -->
		<!-- <xsl:if	test="not($mode = $personnePhysique.mode.OrigineFonds) and not($mode = $personnePhysique.mode.ModeExploitation)  and
			XAS 20170531 mantis 48320 (a partir des infos de mantis 27859) on enleve les criteres sur l'origineFonds et ModeExploitation
			XAS 20170904 Mantis 48320 ajout de $intervenantType.PRECEDENT_PROPRIO dans la liste pour laquel on n'affiche pas les infos de naissance
		-->
		<xsl:if test=" (not($type) or ($type and not(infg:in-list($type, ($intervenantType.INDIVISAIRE, $intervenantType.LOUEUR, $intervenantType.MANDANT, $intervenantType.MANDATAIRE, $intervenantType.PROPRIO_INDIVIS, $intervenantType.HERIT, $intervenantType.PRECEDENT_EXPLOIT,$intervenantType.PRECEDENT_PROPRIO, $intervenantType.PRECEDENT_PROEXP)))))">
			<xsl:if test="not(count($fonctionCodes) eq 1 and infg:in-list($fonctionCode.LIQh , $fonctionCodes))">
				<!-- Date et lieu de naissance -->
				<xsl:call-template name="infoNaissance">
					<xsl:with-param name="chemin"
									select="$cheminPersonnePhysique/infogreffe:Identification"/>
					<xsl:with-param name="attributeLigne">ligne</xsl:with-param>
					<xsl:with-param name="bloc" select="$bloc"/>
				</xsl:call-template>
				<!-- Nationalité -->
				<xsl:call-template name="nationalite">
					<xsl:with-param name="chemin"
									select="$cheminPersonnePhysique/infogreffe:Identification"/>
					<xsl:with-param name="attributeLigne">ligne</xsl:with-param>
					<xsl:with-param name="bloc" select="$bloc"/>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
		<!-- Adresse -->
		<!-- si PRECEDENT_EXPLOIT PRECEDENT_PROEXP (PP) ne pas afficher l'adresse du domicile -->
		<xsl:if test="not($type) or ($type and not(infg:in-list($type, ($intervenantType.PRECEDENT_EXPLOIT, $intervenantType.PRECEDENT_PROEXP))))">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">ligne</xsl:with-param>
				<xsl:with-param name="bloc" select="$bloc"/>
				<xsl:with-param name="titreKey">
					<xsl:choose>
						<xsl:when test="infg:in-list($fonctionCode.CACSUPh , $fonctionCodes) or infg:in-list($fonctionCode.CACTITh , $fonctionCodes)">
							<xsl:text>info-domicilePersonelOuAdressePro</xsl:text>
						</xsl:when>
						<xsl:when test="(not($mode = $personnePhysique.mode.ModeExploitation) and (not(infg:in-list($fonctionCode.CACSUPh , $fonctionCodes) or infg:in-list($fonctionCode.CACTITh , $fonctionCodes) or infg:in-list($fonctionCode.LIQh , $fonctionCodes))))        or ($mode = $personnePhysique.mode.ModeExploitation and infg:in-list($type , ($intervenantType.HERIT, $intervenantType.PERS_APPUI)))">
							<xsl:text>info-domicilePersonel</xsl:text>
						</xsl:when>
						<xsl:when test="(not($mode = $personnePhysique.mode.ModeExploitation) and infg:in-list($fonctionCode.LIQh , $fonctionCodes))         or ($mode = $personnePhysique.mode.ModeExploitation and not(infg:in-list($type , ($intervenantType.HERIT, $intervenantType.PERS_APPUI))))">
							<xsl:text>info-domicile</xsl:text>
						</xsl:when>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:call-template name="adresse">
						<xsl:with-param name="cheminAdresse"
										select="$cheminPersonnePhysique/infogreffe:Identification/infogreffe:AdresseDomicile"/>
						<xsl:with-param name="nameOccultation"
										select="$cheminPersonnePhysique/infogreffe:OccultationAdresseDomicile"/>
					</xsl:call-template>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
		<!--since AN2019_KBIS_EVOL_REPRESENTANTS-->
		<xsl:if test="$cheminPersonnePhysique/infogreffe:ImmatriculationFrance">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">ligne</xsl:with-param>
				<xsl:with-param name="bloc" select="$bloc"/>
				<xsl:with-param name="titreKey">
					<xsl:choose>
						<xsl:when test="$cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Libelle or ($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:RegistreImmatriculation eq 'RCS') ">
							<xsl:text>info-numImmat</xsl:text>
						</xsl:when>
						<xsl:when test="not($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Libelle or ($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:RegistreImmatriculation eq 'RCS'))">
							<xsl:text>info-numImmatEtranger</xsl:text>
						</xsl:when>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:if test="$cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Siren">
						<xsl:if test="$cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Libelle">
							<xsl:if test="infg:isNC($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Code)">
								<xsl:value-of select="infg:format-siren-nc($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/>
							</xsl:if>
							<xsl:if test="not(infg:isNC($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Code))">
								<xsl:value-of select="infg:format-siren($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/>
							</xsl:if>
							<xsl:text> </xsl:text>
							<xsl:value-of select="$cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:RegistreImmatriculation"/>
							<xsl:text> </xsl:text>
							<xsl:value-of select="$cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Libelle"/>
							<xsl:text> </xsl:text>
						</xsl:if>
						<xsl:if test="not($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Greffe/infogreffe:Libelle)">
							<xsl:if test="infg:isNC(/infogreffe:Dossier/infogreffe:Identifiant/infogreffe:Greffe)">
								<xsl:if test="string-length(string(number($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Siren)))&lt;8">
									<xsl:value-of select="infg:format-siren-nc($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/>
								</xsl:if>
								<xsl:if test="string-length(string(number($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Siren)))&gt;7">
									<xsl:value-of select="infg:format-siren($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/>
								</xsl:if>
							</xsl:if>
							<xsl:if test="not(infg:isNC(/infogreffe:Dossier/infogreffe:Identifiant/infogreffe:Greffe))">
								<xsl:value-of select="infg:format-siren($cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:Siren)"/>
							</xsl:if>
							<xsl:text> </xsl:text>
							<xsl:value-of select="$cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:RegistreImmatriculation"/>
							<xsl:text> </xsl:text>
							<xsl:value-of select="$cheminPersonnePhysique/infogreffe:ImmatriculationFrance/infogreffe:LieuRegistre"/>
						</xsl:if>
					</xsl:if>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
		<!-- si Extrait:Observation:Intervenant:PersonnePhysique:ImmatriculationEtranger -->
		<xsl:if test="$cheminPersonnePhysique/infogreffe:ImmatriculationEtranger and $cheminPersonnePhysique/infogreffe:ImmatriculationEtranger/*">
			<!-- si INDIVISAIRE LOUEUR PROPRIO_INDIVIS : ne pas afficher la forme juridique : ne pas afficher le nÂ° d'immatriculation -->
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">ligne</xsl:with-param>
				<xsl:with-param name="bloc" select="$bloc"/>
				<xsl:with-param name="titreKey">info-numImmatEtranger</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:value-of select="$cheminPersonnePhysique/infogreffe:ImmatriculationEtranger/infogreffe:NumeroRegistre"/>
					<xsl:text> </xsl:text>
					<xsl:value-of select="$cheminPersonnePhysique/infogreffe:ImmatriculationEtranger/infogreffe:RegistreImmatriculation"/>
					<xsl:text> </xsl:text>
					<xsl:value-of select="$cheminPersonnePhysique/infogreffe:ImmatriculationEtranger/infogreffe:LieuRegistre"/>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
		<xsl:if test="$cheminPersonnePhysique/infogreffe:Immatriculation and $cheminPersonnePhysique/infogreffe:Immatriculation/*">
			<!-- si INDIVISAIRE LOUEUR PROPRIO_INDIVIS HERIT PERS_ENGAGE_ETS PERS_APRES_DECES (PP) : ne pas afficher le n° d'immatriculation -->
			<xsl:if test="not($type) or ($type and not(infg:in-list($type, ($intervenantType.INDIVISAIRE, $intervenantType.LOUEUR, $intervenantType.PROPRIO_INDIVIS, $intervenantType.HERIT, $intervenantType.PERS_ENGAGE_ETS, $intervenantType.PERS_APRES_DECES))))">
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">ligne</xsl:with-param>
					<xsl:with-param name="bloc" select="$bloc"/>
					<xsl:with-param name="titreKey">
						<!-- Numéro unique d'identification : Afficher si ImmatriculationFrance renseigné mais Greffe:Code non renseigné + Afficher Siren-->
						<xsl:choose>
							<xsl:when test="(($mode = $personnePhysique.mode.OrigineFonds or  $mode = $personnePhysique.mode.ModeExploitation) and infg:in-list($type , ($intervenantType.PRECEDENT_EXPLOIT , $intervenantType.PRECEDENT_PROEXP)))">
								<xsl:text>info-numUniqueIdentification</xsl:text>
							</xsl:when>
							<xsl:otherwise>
								<xsl:text>info-numImmat</xsl:text>
							</xsl:otherwise>
						</xsl:choose>
					</xsl:with-param>
					<xsl:with-param name="libelle">
						<xsl:if test="$cheminPersonnePhysique/infogreffe:Immatriculation/infogreffe:Siren">
							<xsl:value-of select="format-number(number($cheminPersonnePhysique/infogreffe:Immatriculation/infogreffe:Siren), '000 000 000', 'european')"/>
						</xsl:if>
						<!-- si PRECEDENT_EXPLOIT PRECEDENT_PROEXP (PP et PM) : afficher le n° d'immatriculation sans le type de registre(RegistreImmatriculation) et le lieu de registre (LieuRegistre) -->
						<xsl:if test="not($type) or ($type and not(infg:in-list($type, ($intervenantType.PRECEDENT_EXPLOIT, $intervenantType.PRECEDENT_PROEXP))))">
							<xsl:text> </xsl:text>
							<xsl:value-of select="$cheminPersonnePhysique/infogreffe:Immatriculation/infogreffe:RegistreImmatriculation"/>
							<xsl:text> </xsl:text>
							<xsl:value-of select="$cheminPersonnePhysique/infogreffe:Immatriculation/infogreffe:LieuRegistre"/>
						</xsl:if>
					</xsl:with-param>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
	</xsl:template>
	<xsl:template name="nomPrenoms">
		<xsl:param name="identification"/>
		<xsl:param name="attributeLigne"/>
		<xsl:param name="elementTitre"/>
		<xsl:param name="bloc"/>
		<xsl:param name="weight"/>
		<xsl:variable name="nom">
			<xsl:choose>
				<xsl:when test="$identification/infogreffe:Nom/infogreffe:NomPatronymique">
					<xsl:value-of select="$identification/infogreffe:Nom/infogreffe:NomPatronymique"/>
				</xsl:when>
				<xsl:otherwise>
					<xsl:value-of select="$identification/infogreffe:Nom/infogreffe:NomUsage"/>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:call-template name="information">
			<xsl:with-param name="ligne" select="$attributeLigne"/>
			<xsl:with-param name="bloc" select="$bloc"/>
			<xsl:with-param name="titre" select="$elementTitre"/>
			<xsl:with-param name="libelle"
							select="concat($nom, ' ' ,$identification/infogreffe:Prenoms)"/>
			<xsl:with-param name="weight" select="$weight"/>
		</xsl:call-template>
		<!--xsl:choose>
			<xsl:when test="$chemin/infogreffe:Nom/infogreffe:NomPatronymique">
			</xsl:when>
			<xsl:when test="$defautLibelleNom">
				<xsl:call-template name="information">
					<xsl:with-param name="ligne" select="$attributeLigne" />
					<xsl:with-param name="bloc" select="$bloc"/>
					<xsl:with-param name="libelle" select="$defautLibelleNom" />
				</xsl:call-template>
			</xsl:when>
		</xsl:choose-->
	</xsl:template>
	<xsl:template name="nomUsage">
		<xsl:param name="identification"/>
		<xsl:param name="attributeLigne"/>
		<xsl:param name="bloc"/>
		<xsl:if test="$identification/infogreffe:Nom/infogreffe:NomUsage and $identification/infogreffe:Nom/infogreffe:NomPatronymique">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne" select="$attributeLigne"/>
				<xsl:with-param name="bloc" select="$bloc"/>
				<xsl:with-param name="titreKey">info-nomUsage</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:value-of select="$identification/infogreffe:Nom/infogreffe:NomUsage"/>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="infoNaissance">
		<xsl:param name="chemin"/>
		<xsl:param name="attributeLigne"/>
		<xsl:param name="bloc"/>
		<xsl:variable name="dateNaissance">
			<xsl:choose>
				<xsl:when test="$chemin/infogreffe:DateNaissance/infogreffe:Date">
					<xsl:value-of select="concat(i18n:getTextPL('common-Le'), ' ', infg:format-date($chemin/infogreffe:DateNaissance/infogreffe:Date))"/>
				</xsl:when>
				<xsl:when test="$chemin/infogreffe:DateNaissance/infogreffe:Mois or $chemin/infogreffe:DateNaissance/infogreffe:Annee">
					<xsl:value-of select="concat(i18n:getTextPL('common-En'), ' ', $chemin/infogreffe:DateNaissance/infogreffe:Mois, ' ' , $chemin/infogreffe:DateNaissance/infogreffe:Annee)"/>
				</xsl:when>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="departementNaissance">
			<xsl:choose>
				<xsl:when test="$chemin/infogreffe:LieuNaissance/infogreffe:DepartementNaissance">
					<xsl:text> (</xsl:text>
					<xsl:value-of select="$chemin/infogreffe:LieuNaissance/infogreffe:DepartementNaissance"/>
					<xsl:text>) </xsl:text>
				</xsl:when>
				<xsl:when test="$chemin/infogreffe:LieuNaissance/infogreffe:CommuneNaissance">
					<xsl:variable name="communeNaissance"
								  select="$chemin/infogreffe:LieuNaissance/infogreffe:CommuneNaissance"/>
					<xsl:text> (</xsl:text>
					<xsl:choose>
						<xsl:when test="number(substring($communeNaissance, 1, 2)) &gt; 95">
							<xsl:value-of select="substring($communeNaissance, 1, 3)"/>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="substring($communeNaissance, 1, 2)"/>
						</xsl:otherwise>
					</xsl:choose>
					<xsl:text>) </xsl:text>
				</xsl:when>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="paysNaissance">
			<xsl:choose>
				<xsl:when test="$chemin/infogreffe:LieuNaissance/infogreffe:PaysNaissance and not($chemin/infogreffe:LieuNaissance/infogreffe:PaysNaissance/infogreffe:Code = 'FR')">
					<xsl:text> (</xsl:text>
					<xsl:value-of select="$chemin/infogreffe:LieuNaissance/infogreffe:PaysNaissance/infogreffe:Libelle"/>
					<xsl:text>) </xsl:text>
				</xsl:when>
				<xsl:otherwise>
					<xsl:text/>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="lieuNaissance">
			<xsl:if test="$chemin/infogreffe:LieuNaissance/infogreffe:LieuNaissance       or $chemin/infogreffe:LieuNaissance/infogreffe:ArrondissementNaissance or $departementNaissance != '' or $paysNaissance != ''">
				<xsl:if test="$dateNaissance != ''">
					<xsl:value-of select="i18n:getTextPLPrefSuff(' ', 'common-a', ' ')"/>
				</xsl:if>
				<xsl:if test="$chemin/infogreffe:LieuNaissance/infogreffe:LieuNaissance">
					<xsl:value-of select="$chemin/infogreffe:LieuNaissance/infogreffe:LieuNaissance"/>
					<xsl:text> </xsl:text>
				</xsl:if>
				<xsl:if test="$chemin/infogreffe:LieuNaissance/infogreffe:ArrondissementNaissance">
					<xsl:call-template name="getArrondissement">
						<xsl:with-param name="numArrondissement"
										select="number($chemin/infogreffe:LieuNaissance/infogreffe:ArrondissementNaissance)"/>
					</xsl:call-template>
					<xsl:text> </xsl:text>
				</xsl:if>
				<xsl:if test="$departementNaissance">
					<xsl:value-of select="$departementNaissance"/>
					<xsl:text> </xsl:text>
				</xsl:if>
				<xsl:if test="$paysNaissance">
					<xsl:value-of select="$paysNaissance"/>
				</xsl:if>
			</xsl:if>
		</xsl:variable>
		<xsl:variable name="titre">
			<xsl:choose>
				<xsl:when test="$dateNaissance != '' and $lieuNaissance != ''">
					<xsl:value-of select="i18n:getTextPL('info-dateLieuNaissance')"/>
				</xsl:when>
				<xsl:when test="$dateNaissance != ''">
					<xsl:value-of select="i18n:getTextPL('info-dateNaissance')"/>
				</xsl:when>
				<xsl:otherwise>
					<xsl:value-of select="i18n:getTextPL('info-lieuNaissance')"/>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:if test="$dateNaissance != '' or $lieuNaissance != ''">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne" select="$attributeLigne"/>
				<xsl:with-param name="bloc" select="$bloc"/>
				<xsl:with-param name="titre" select="$titre"/>
				<xsl:with-param name="libelle" select="concat($dateNaissance, $lieuNaissance)"/>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="nationalite">
		<xsl:param name="chemin"/>
		<xsl:param name="attributeLigne"/>
		<xsl:param name="bloc"/>
		<xsl:if test="$chemin/infogreffe:Nationalite">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne" select="$attributeLigne"/>
				<xsl:with-param name="bloc" select="$bloc"/>
				<xsl:with-param name="titreKey">info-nationalite</xsl:with-param>
				<xsl:with-param name="libelle"
								select="$chemin/infogreffe:Nationalite/infogreffe:Libelle"/>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="adresse">
		<xsl:param name="cheminAdresse"/>
		<xsl:param name="nameOccultation"/>
		<xsl:if test="$cheminAdresse">
			<xsl:choose>
				<xsl:when test="not($nameOccultation) or $nameOccultation = false()">
					<xsl:if test="$cheminAdresse/infogreffe:Ligne1">
						<xsl:value-of select="$cheminAdresse/infogreffe:Ligne1"/>
						<xsl:text> </xsl:text>
					</xsl:if>
					<xsl:if test="$cheminAdresse/infogreffe:Ligne2">
						<xsl:value-of select="$cheminAdresse/infogreffe:Ligne2"/>
						<xsl:text> </xsl:text>
					</xsl:if>
					<xsl:if test="$cheminAdresse/infogreffe:Ligne3">
						<xsl:value-of select="$cheminAdresse/infogreffe:Ligne3"/>
						<xsl:text> </xsl:text>
					</xsl:if>
					<xsl:if test="$cheminAdresse/infogreffe:CodePostal">
						<xsl:value-of select="$cheminAdresse/infogreffe:CodePostal"/>
						<xsl:text> </xsl:text>
					</xsl:if>
					<xsl:if test="$cheminAdresse/infogreffe:BureauDistributeur">
						<xsl:value-of select="$cheminAdresse/infogreffe:BureauDistributeur"/>
						<xsl:text> </xsl:text>
					</xsl:if>
					<xsl:if test="$cheminAdresse/infogreffe:Pays and not($cheminAdresse/infogreffe:Pays/infogreffe:Code = 'FR')">
						<xsl:text> (</xsl:text>
						<xsl:value-of select="$cheminAdresse/infogreffe:Pays/infogreffe:Libelle"/>
						<xsl:text>)</xsl:text>
					</xsl:if>
				</xsl:when>
				<xsl:otherwise>
					<xsl:call-template name="occultationMsg"/>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:if>
	</xsl:template>
	<xsl:template name="etablissementRCS">
		<xsl:param name="statutDossier"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="statutRegistre"/>
		<xsl:param name="personneType"/>
		<xsl:if test="($statutRegistre = $statutRegistre.RCS)">

			<!-- RENSEIGNEMENTS RELATIFS A L'ETABLISSEMENT PRINCIPAL ET A L'ACTIVITE ou RENSEIGNEMENTS RELATIFS A L'ACTIVITE ET AUTRES ETABLISSEMENT(S) DANS LE RESSORT -->
			<xsl:variable name="cheminEtablissementDansRessortNonSiegeEtNonFerme"
						  select="//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = false() and infogreffe:Type != $etablissementDansRessortType.SIE]"/>
			<xsl:variable name="length"
						  select="count($cheminEtablissementDansRessortNonSiegeEtNonFerme)"/>
			<xsl:variable name="dateFin"
						  select="//*[name() = $personneType]/infogreffe:Activite/infogreffe:DateCessationActivite"/>
			<xsl:variable name="observationsCessation"
						  select="//infogreffe:Extrait/infogreffe:Observation[infogreffe:TypeAgrege = $mentionTypeAggrege.CESS_TOTALE and infogreffe:RevelableExtraits = true() and empty(infogreffe:DateSuppression)]"/>
			<xsl:if test="$length = 0 and ($dateFin or count($observationsCessation) &gt; 0)">
				<xsl:variable name="cheminEtablissementDansRessortNonSiegeEtFerme"
							  select="//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = true() and infogreffe:Type != $etablissementDansRessortType.SIE]"/>
				<xsl:variable name="cheminEtablissementDansRessortNonSiegeEtFermeCount"
							  select="count($cheminEtablissementDansRessortNonSiegeEtFerme)"/>
				<xsl:choose>
					<xsl:when test="$cheminEtablissementDansRessortNonSiegeEtFermeCount = 0 or $statutDossier ne $statutDossier.A">
						<xsl:call-template name="etablissementPrincipalCessationDate">
							<xsl:with-param name="statutDossier" select="$statutDossier"/>
							<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
							<xsl:with-param name="dateFin" select="$dateFin"/>
							<xsl:with-param name="etablissementTitreKey">section-RenseignementsRelatifEtablissementPrincipalActivite</xsl:with-param>
						</xsl:call-template>
					</xsl:when>
					<xsl:otherwise>
						<xsl:for-each select="$cheminEtablissementDansRessortNonSiegeEtFerme">
							<!-- Tri de la liste par Etablissement:EtablissementDansRessort:OrdreAffichage -->
							<xsl:sort select="infogreffe:OrdreAffichage"
									  order="ascending"
									  data-type="number"/>
							<!-- RENSEIGNEMENTS RELATIFS A L'ETABLISSEMENT PRINCIPAL ET A L'ACTIVITE ou RENSEIGNEMENTS RELATIFS A L'ACTIVITE -->
							<!-- Le premier établissement est soit le type SEP, soit le PRI, soit le premier SEC -->
							<xsl:if test="position() = 1">
								<xsl:call-template name="etablissementPrincipalCessationDate">
									<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
									<xsl:with-param name="statutDossier" select="$statutDossier"/>
									<xsl:with-param name="dateFin" select="$dateFin"/>
									<xsl:with-param name="etablissement" select="current()"/>
								</xsl:call-template>
							</xsl:if>
						</xsl:for-each>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:if>
			<xsl:for-each select="$cheminEtablissementDansRessortNonSiegeEtNonFerme">

				<!-- Tri de la liste par Etablissement:EtablissementDansRessort:OrdreAffichage -->
				<xsl:sort select="infogreffe:OrdreAffichage"
						  order="ascending"
						  data-type="number"/>
				<!-- Le premier établissement est soit le type SEP, soit le PRI, soit le premier SEC -->
				<xsl:if test="position() = 1">
					<!-- RENSEIGNEMENTS RELATIFS A L'ETABLISSEMENT PRINCIPAL ET A L'ACTIVITE ou RENSEIGNEMENTS RELATIFS A L'ACTIVITE -->
					<xsl:call-template name="renseignementsRelatifsEtablissement">
						<xsl:with-param name="statutDossier" select="$statutDossier"/>
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						<xsl:with-param name="cheminEtablissementDansRessortNonSiegeEtNonFerme"
										select="$cheminEtablissementDansRessortNonSiegeEtNonFerme"/>
						<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
						<xsl:with-param name="personneType" select="$personneType"/>
					</xsl:call-template>
				</xsl:if>
				<!-- Ne pas sélectionner l'établissement affiché dans le pavé RENSEIGNEMENTS RELATIFS A L'ACTIVITE
							Sélection des établissements secondaires ouverts Extrait:Etablissement:EtablissementDansRessort:Ferme non renseigné  -->
				<!-- Les autres établissements de type 2 -->
				<xsl:if test="position() = 2">
					<!-- AUTRES ETABLISSEMENT(S) DANS LE RESSORT -->
					<xsl:call-template name="autreEtablissementDansRessort">
						<xsl:with-param name="cheminEtablissementDansRessortNonSiegeEtNonFerme"
										select="$cheminEtablissementDansRessortNonSiegeEtNonFerme"/>
						<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
						<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						<xsl:with-param name="personneType" select="$personneType"/>
						<xsl:with-param name="statutDossier" select="$statutDossier"/>
					</xsl:call-template>
				</xsl:if>
			</xsl:for-each>
			<!-- IMMATRICULATION(S) HORS RESSORT -->
			<xsl:call-template name="immatHorsRessort"/>
		</xsl:if>
	</xsl:template>
	<xsl:template name="journal">
		<xsl:param name="cheminParutionJournal"/>
		<xsl:if test="$cheminParutionJournal/infogreffe:Journal">
			<xsl:value-of select="$cheminParutionJournal/infogreffe:Journal"/>
		</xsl:if>
		<xsl:if test="$cheminParutionJournal/infogreffe:Date">
			<xsl:value-of select="i18n:getTextPLPrefSuff(' ', 'common-du', ' ')"/>
			<xsl:value-of select="infg:format-date($cheminParutionJournal/infogreffe:Date)"/>
		</xsl:if>
		<xsl:if test="$cheminParutionJournal/infogreffe:DateFin">
			<xsl:value-of select="i18n:getTextPLPrefSuff(' ', 'common-au', ' ')"/>
			<xsl:value-of select="infg:format-date($cheminParutionJournal/infogreffe:DateFin)"/>
		</xsl:if>
		<xsl:if test="$cheminParutionJournal/infogreffe:DateRectificatif">
			<xsl:value-of select="i18n:getTextPLPrefSuff(' ', 'libelle-rectifierLe', ' ')"/>
			<xsl:value-of select="infg:format-date($cheminParutionJournal/infogreffe:DateRectificatif)"/>
		</xsl:if>
	</xsl:template>
	<xsl:template name="sanction">
		<xsl:param name="sanctionList"/>
		<xsl:param name="showBloc"/>
		<!-- Recherche des sanctions avec : Extrait:SanctionPersonnelle:Inscrit renseigné -->
		<!-- si dateDebut + Duree > sysdate et (not(date annulation) ou sysdate <= date annulation)  -->
		<xsl:for-each select="$sanctionList[(not(infogreffe:Duree) or (xs:date(infogreffe:DateDebut) + xs:yearMonthDuration(concat(concat('P' , infogreffe:Duree), 'M'))) &gt; current-date() ) and (not(infogreffe:DateAnnulation) or current-date() &lt; infogreffe:DateAnnulation)]">
			<!--xsl:if test="position() = 1">
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">sanctionTitre</xsl:with-param>
					<xsl:with-param name="bloc">
						<xsl:if test="$showBloc">
							<xsl:value-of select="$information.bloc.indentBloc"/>
						</xsl:if>
					</xsl:with-param>
					<xsl:with-param name="libelleKey">libelle-incapaciteIntediction</xsl:with-param>
				</xsl:call-template>
			</xsl:if-->

			<!-- Les lignes de sanction -->
			<xsl:if test="not(infogreffe:Duree) or infogreffe:Duree = 999 or  xs:date(infogreffe:DateDebut) + xs:yearMonthDuration(concat(concat('P' , infogreffe:Duree), 'M')) &gt; $dateDuJour">
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">sanction</xsl:with-param>
					<xsl:with-param name="bloc">
						<xsl:if test="$showBloc">
							<xsl:value-of select="$information.bloc.indentBloc"/>
						</xsl:if>
					</xsl:with-param>
					<xsl:with-param name="titreKey">libelle-incapaciteIntediction</xsl:with-param>
					<xsl:with-param name="libelle">
						<xsl:value-of select="concat(infogreffe:TypeSanction/infogreffe:Libelle,' ', i18n:getTextPL('libelle-enDateDu'), ' ', infg:format-date(infogreffe:DateDebut))"/>
					</xsl:with-param>
				</xsl:call-template>
				<!-- i18n:getTextPL('libelle-pourUneDureeDe'), ' ', infogreffe:Duree, ' ', i18n:getTextPL('common-mois'))-->
				<!-- Mantis 0029393 -->
				<xsl:if test="infogreffe:Duree and infogreffe:Duree != 999">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">dateExpirationIncapaciteInterdiction</xsl:with-param>
						<xsl:with-param name="bloc">
							<xsl:if test="$showBloc">
								<xsl:value-of select="$information.bloc.indentBloc"/>
							</xsl:if>
						</xsl:with-param>
						<xsl:with-param name="titreKey">info-dateExpirationIncapaciteInterdiction</xsl:with-param>
						<xsl:with-param name="libelle"
										select="infg:format-date(xs:date(infogreffe:DateDebut) + xs:yearMonthDuration(concat(concat('P' , infogreffe:Duree), 'M')))"/>
					</xsl:call-template>
				</xsl:if>
			</xsl:if>
			<xsl:call-template name="sautLigne"/>
		</xsl:for-each>
	</xsl:template>
	<xsl:template name="mentionCAPE">
		<xsl:param name="cheminObservation" required="yes"/>
		<!-- Contrat d'appui (CAPE) -->
		<xsl:if test="infg:hasObservationVisible($mentionTypeAggrege.CAPE, $cheminObservation)">
			<xsl:variable name="firstObservation"
						  select="$cheminObservation[infogreffe:TypeAgrege = $mentionTypeAggrege.CAPE      and not(infogreffe:DateSuppression)      and infogreffe:RevelableExtraits = true()     and infogreffe:OrdreAffichage[not(. &gt; $cheminObservation[infogreffe:TypeAgrege = $mentionTypeAggrege.CAPE     and not(infogreffe:DateSuppression)      and infogreffe:RevelableExtraits = true()]/infogreffe:OrdreAffichage)]]"/>
			<xsl:choose>
				<xsl:when test="count($firstObservation/infogreffe:Texte) &gt; 0 and (count($firstObservation/infogreffe:Intervenant) = 0 or $firstObservation/infogreffe:Intervenant/infogreffe:DiffusableExtrait=false())"> <!-- Mantis 43302 XAS 20161014 si pas d'intervenant -->
					<xsl:call-template name="observationAvecMention">
						<xsl:with-param name="ligne">contratCape</xsl:with-param>
						<xsl:with-param name="observation" select="$firstObservation"/>
					</xsl:call-template>
				</xsl:when>
				<xsl:otherwise>
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">contratCape</xsl:with-param>
						<xsl:with-param name="libelleKey">info-contratAppuiCAPE</xsl:with-param>
					</xsl:call-template>
					<!-- Sélection des observations de type CAPE Extrait:Observation:TypeAgrege est CAPE et qui n'ont pas de date de suppression-->
					<xsl:call-template name="mentionsCAPE">
						<xsl:with-param name="mentionsCAPE" select="$firstObservation"/>
					</xsl:call-template>
					<!--					<xsl:call-template name="information">
						<xsl:with-param name="ligne">contratCape</xsl:with-param>
						<xsl:with-param name="justify" select="true()"/>
						<xsl:with-param name="titreKey">info-contratAppuiCAPE</xsl:with-param>
						<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint($firstObservation/infogreffe:Texte)"/>
					</xsl:call-template>-->
				</xsl:otherwise>
			</xsl:choose>
		</xsl:if>
	</xsl:template>
	<xsl:template name="mentionsCAPE">
		<xsl:param name="mentionsCAPE"/>
		<xsl:for-each select="$mentionsCAPE">
			<!-- tri par ordre Extrait:Observation:OrdreAffichage -->
			<xsl:sort select="infogreffe:OrdreAffichage"
					  data-type="number"
					  order="ascending"/>
			<!--xsl:call-template name="information">
				<xsl:with-param name="ligne">mention</xsl:with-param>
				<xsl:with-param name="titreKey">info-contratAppuiCAPE</xsl:with-param>
				<xsl:with-param name="libelle"><xsl:text>&#xA0;</xsl:text></xsl:with-param>
			</xsl:call-template-->
			<!-- Le ou les intervenants -->
			<xsl:variable name="intervenants"
						  select="infogreffe:Intervenant[infogreffe:DiffusableExtrait = true()]"/>
			<xsl:choose>
				<xsl:when test="count($intervenants) &gt; 0">
					<xsl:for-each select="$intervenants">
						<!-- Tri par Extrait:Observation:Intervenant:Type:Code -->
						<xsl:sort select="infogreffe:Type/infogreffe:Code" order="ascending"/>
						<xsl:call-template name="personneMorale">
							<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
							<xsl:with-param name="cheminPersonneMorale" select="infogreffe:PersonneMorale"/>
							<xsl:with-param name="titreDeno"
											select="i18n:getTextPL('info-denoSocialePersonneResponsable')"/>
							<xsl:with-param name="type" select="infogreffe:Type/infogreffe:Code"/>
							<xsl:with-param name="mode" select="$personneMorale.mode.CAPE"/>
						</xsl:call-template>
						<!-- Complement intervenant de l'observation - infogreffe:Observation/infogreffe:Intervenant/infogreffe:Complement -->
						<xsl:if test="infogreffe:Complement">
							<xsl:call-template name="complement">
								<xsl:with-param name="libelle" select="infogreffe:Complement"/>
							</xsl:call-template>
						</xsl:if>
						<xsl:if test="position() != last()">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">ligne</xsl:with-param>
								<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
								<xsl:with-param name="libelle">
									<xsl:text> ------------------------------------------ </xsl:text>
								</xsl:with-param>
							</xsl:call-template>
						</xsl:if>
					</xsl:for-each>
					<xsl:if test="position() != last()">
						<xsl:call-template name="ligneSeparation"/>
					</xsl:if>
				</xsl:when>
				<xsl:otherwise>
					<xsl:if test="position() &gt; 1">
						<xsl:call-template name="complement">
							<xsl:with-param name="libelle" select="infogreffe:Texte"/>
						</xsl:call-template>
						<xsl:if test="position() != last()">
							<xsl:call-template name="ligneSeparation"/>
						</xsl:if>
					</xsl:if>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:for-each>
	</xsl:template>
	<xsl:template name="mentions">
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="typeAgrege"/>
		<xsl:param name="bloc"/>
		<!-- Les différentes sections de mentions avec une ou des mentions et le ou les intervenants associés -->
		<!-- Sélection des observations non supprimées Extrait:Observation:DateSuppression non renseignée et révélables Observation:RevelableExtrait = 1 pour un typeAgrege donné -->
		<xsl:for-each select="infg:selectObservationVisible($typeAgrege, $cheminObservation)">
			<!-- tri par ordre Extrait:Observation:OrdreAffichage -->
			<xsl:sort select="infogreffe:OrdreAffichage"
					  data-type="number"
					  order="ascending"/>
			<xsl:variable name="numero" select="infogreffe:Numero"/>
			<xsl:variable name="date" select="infogreffe:Date"/>
			<xsl:for-each select="infogreffe:Texte">
				<xsl:choose>
					<xsl:when test="position() = 1">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">mention</xsl:with-param>
							<xsl:with-param name="justify" select="true()"/>
							<xsl:with-param name="bloc" select="$bloc"/>
							<xsl:with-param name="titre">
								<xsl:value-of select="concat(i18n:getTextPL('common-tiret'), ' ', i18n:getTextPL('info-mention'), ' ')"/>
								<xsl:if test="$numero">
									<xsl:value-of select="concat(i18n:getTextPL('common-min-numero'), ' ', $numero)"/>
								</xsl:if>
								<xsl:if test="$date">
									<xsl:value-of select="concat(' ', i18n:getTextPL('common-du'), ' ', infg:format-date($date))"/>
								</xsl:if>
							</xsl:with-param>
							<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint(current()/text())"/>
						</xsl:call-template>
					</xsl:when>
					<xsl:otherwise>
						<xsl:call-template name="complement">
							<xsl:with-param name="justify" select="true()"/>
							<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint(current()/text())"/>
						</xsl:call-template>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:for-each>
			<!-- Le ou les intervenants -->
			<xsl:for-each select="infogreffe:Intervenant[infogreffe:DiffusableExtrait = true()]">

				<!-- Tri par Extrait:Observation:Intervenant:Type:Code -->
				<xsl:sort select="infogreffe:Type/infogreffe:Code" order="ascending"/>
				<xsl:call-template name="personneMorale">
					<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
					<xsl:with-param name="cheminPersonneMorale" select="infogreffe:PersonneMorale"/>
					<xsl:with-param name="titreDeno" select="infogreffe:Type/infogreffe:Libelle"/>
					<xsl:with-param name="type" select="infogreffe:Type/infogreffe:Code"/>
				</xsl:call-template>
				<!-- Complement intervenant de l'observation - infogreffe:Observation/infogreffe:Intervenant/infogreffe:Complement -->
				<xsl:if test="infogreffe:Complement">
					<xsl:call-template name="complement">
						<xsl:with-param name="libelle" select="infogreffe:Complement"/>
					</xsl:call-template>
				</xsl:if>
				<xsl:if test="position() != last()">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">ligne</xsl:with-param>
						<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
						<xsl:with-param name="libelle">
							<xsl:text> ------------------------------------------ </xsl:text>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>
			</xsl:for-each>
		</xsl:for-each>
	</xsl:template>
	<xsl:template name="etablissementsDansRessort">
		<xsl:param name="etablissements"/>
		<xsl:param name="statutRegistre"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="personneType"/>
		<xsl:param name="start"/>
		<xsl:param name="finish"/>
		<xsl:param name="infogreffeSiegeEtranger"/>
		<xsl:param name="statutDossier"/>
		<xsl:for-each select="$etablissements">

			<!-- Tri de la liste par Etablissement:EtablissementDansRessort:OrdreAffichage -->
			<xsl:sort select="infogreffe:OrdreAffichage"
					  order="ascending"
					  data-type="number"/>
			<xsl:if test="$start &lt;= position() and position() &lt;= $finish">
				<xsl:call-template name="etablissementDansRessort">
					<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
					<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
					<xsl:with-param name="personneType" select="$personneType"/>
					<xsl:with-param name="infogreffeSiegeEtranger" select="$infogreffeSiegeEtranger"/>
					<xsl:with-param name="statutDossier" select="$statutDossier"/>
					<xsl:with-param name="first" select="$start = 1 and position() = 1"/>
				</xsl:call-template>
				<xsl:if test="position() &gt; 1 and position() != last()">
					<xsl:call-template name="barreHorizontale"/>
				</xsl:if>
			</xsl:if>
		</xsl:for-each>
	</xsl:template>
	<xsl:template name="etablissementDansRessort">
		<xsl:param name="statutRegistre"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<!-- type établissement utile pour afficher différents titre pour l'adresse, si etab princ ou autre -->
		<xsl:param name="personneType"/>
		<xsl:param name="infogreffeSiegeEtranger"/>
		<xsl:param name="statutDossier"/>
		<xsl:param name="first"/>
		<xsl:variable name="nonSedentaire"
					  select="//infogreffe:ActiviteFonds[infogreffe:Ferme = false()][1]/infogreffe:NonSedentaire =  true()"/>
		<xsl:if test="$first and //infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:ImmatriculationRCS/infogreffe:TypeInscription = $typeInscription.S">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">dateImmatriculation</xsl:with-param>
				<xsl:with-param name="titreKey">info-dateImmat</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:value-of select="infg:format-date(//infogreffe:Extrait/infogreffe:Personne/infogreffe:PersonneRCS/infogreffe:ImmatriculationRCS/infogreffe:DateImmatriculation)"/>
				</xsl:with-param>
			</xsl:call-template>
			<xsl:if test="//infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation">
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">dateRadiatation</xsl:with-param>
					<xsl:with-param name="titreKey">info-dateRadiation</xsl:with-param>
					<xsl:with-param name="libelle">
						<xsl:value-of select="infg:format-date(//infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)"/>
					</xsl:with-param>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
		<!-- Adresse -->
		<xsl:if test="not($infogreffeSiegeEtranger and $nonSedentaire)">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">adresseActivite</xsl:with-param>
				<xsl:with-param name="titreKey">info-adresseEtablissement</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:call-template name="adresse">
						<xsl:with-param name="cheminAdresse" select="infogreffe:Adresse"/>
					</xsl:call-template>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
		<xsl:call-template name="sautLigne"/>
		<!-- siegeDomiciliation -->
		<xsl:variable name="count_etablissement"
					  select="count(//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = false() and (infogreffe:Type != $etablissementDansRessortType.SIE)])"/>
		<xsl:if test="infg:isPP($statutDossier) ">
			<xsl:call-template name="siegeDomiciliation">
				<xsl:with-param name="cheminEtablissementSiegeDomiciliataire"
								select="infogreffe:Domiciliataire"/>
				<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="pp" select="true()"/>
				<xsl:with-param name="showObservation" select="$count_etablissement = 1"/>
			</xsl:call-template>
		</xsl:if>
		<xsl:if test="infg:isPM($statutDossier)">
			<!-- Siège fixé dans une entreprise de domiciliation -->
			<xsl:variable name="etabPrim"
						  select="infogreffe:Ferme = false() and  infogreffe:Type = $etablissementDansRessortType.PRI"/>
			<xsl:variable name="siegeEtrange"
						  select="//infogreffe:Etablissement/infogreffe:SiegeOuEtablissementPrincipalPPHorsRessort/infogreffe:SiegeEtranger"/>
			<xsl:if test="$etabPrim and $siegeEtrange">
				<xsl:call-template name="siegeDomiciliation">
					<xsl:with-param name="cheminEtablissementSiegeDomiciliataire"
									select="infogreffe:Domiciliataire"/>
					<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
					<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
		<xsl:call-template name="sautLigne"/>
		<!-- Commune ou s'exerce l'activité principale pour les ressortissants de l'Union européenne ou d'une Etat
			partie à l'accord sur l'EEE non domiciliés en France qui exerce une activité ambulante -->
		<xsl:if test="$infogreffeSiegeEtranger and $nonSedentaire">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">communeActiviteRessortissantsUnionEuropeenneOuEeeAmbulante</xsl:with-param>
				<xsl:with-param name="titreKey">info-communeActiviteRessortissantsUnionEuropeenneOuEeeAmbulante</xsl:with-param>
				<xsl:with-param name="libelle"
								select="infogreffe:Adresse/infogreffe:BureauDistributeur"/>
			</xsl:call-template>
		</xsl:if>
		<xsl:call-template name="sautLigne"/>
		<!-- Cessation partielle d'activité exercée -->
		<xsl:call-template name="observationsSansTitre">
			<xsl:with-param name="ligne">cessationPartielleActiviteExercee</xsl:with-param>
			<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.CESS_PARTIELLE"/>
			<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
		</xsl:call-template>
		<!-- Date de cessation d'activité -->
		<xsl:call-template name="cessationActivite">
			<xsl:with-param name="dateFin"
							select="//*[name() = $personneType]/infogreffe:Activite/infogreffe:DateCessationActivite"/>
			<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
		</xsl:call-template>
		<!-- Personnes(s) ayant le pouvoir d'engager la responsabilité de la personne morale -->
		<xsl:for-each select="infogreffe:ActiviteFonds[infogreffe:Ferme = false()]/infogreffe:ModeExploitation/infogreffe:Intervenant[infogreffe:DiffusableExtrait = true() and contains(' $intervenantType.PERS_ENGAGE_ETS $intervenantType.FONDE_POUVOIRS ', infogreffe:Type/infogreffe:Code)]">

			<!-- Tri de la liste par Etablissement:EtablissementDansRessort:ActiviteFonds:OrdreAffichage -->
			<xsl:sort select="infogreffe:OrdreAffichage"
					  order="ascending"
					  data-type="number"/>
			<!-- xsl:if test="position() = 1">
				<xsl:choose>
					<xsl:when test="infg:isPP($statutDossier)">
						<xsl:call-template name="information">
							<xsl:with-param name="libelleKey">
								<xsl:choose>
									<xsl:when test="last()">
										<xsl:text>libelle-personneAyantPouvoirEngagerResponsabilitePersonnePhysique</xsl:text>
									</xsl:when>
									<xsl:otherwise>
										<xsl:text>libelle-personnesAyantPouvoirEngagerResponsabilitePersonnePhysique</xsl:text>
									</xsl:otherwise>
								</xsl:choose>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:when>
					<xsl:otherwise>
						<xsl:call-template name="information">
							<xsl:with-param name="libelleKey">
								<xsl:choose>
									<xsl:when test="last()">
										<xsl:text>libelle-personneAyantPouvoirEngagerResponsabilitePersonneMorale</xsl:text>
									</xsl:when>
									<xsl:otherwise>
										<xsl:text>libelle-personnesAyantPouvoirEngagerResponsabilitePersonneMorale</xsl:text>
									</xsl:otherwise>
								</xsl:choose>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:if -->
			<xsl:call-template name="information">
				<xsl:with-param name="libelle">
					<xsl:value-of select="infogreffe:Type/infogreffe:Libelle"/>
				</xsl:with-param>
			</xsl:call-template>
			<xsl:if test="infogreffe:PersonnePhysique"> <!-- XAS 20180214 mantis 0051061 filtrage des personnes physique -->
				<xsl:call-template name="personnePhysique">
					<xsl:with-param name="cheminPersonnePhysique" select="infogreffe:PersonnePhysique"/>
					<xsl:with-param name="type" select="infogreffe:Type/infogreffe:Code"/>
					<xsl:with-param name="elementTitre" select="i18n:getTextPL('info-nomPrenoms')"/>
				</xsl:call-template>
			</xsl:if>
			<xsl:if test="infogreffe:PersonneMorale"> <!-- XAS 20180214 mantis 0051061 gestion du cas des personnes morale -->
				<xsl:call-template name="personneMorale">
					<xsl:with-param name="cheminPersonneMorale" select="infogreffe:PersonneMorale"/>
					<xsl:with-param name="type" select="infogreffe:Type/infogreffe:Code"/>
					<xsl:with-param name="titreDeno" select="i18n:getTextPL('info-denomination')"/>
				</xsl:call-template>
			</xsl:if>
			<xsl:call-template name="sautLigne"/>
		</xsl:for-each>
		<xsl:call-template name="sautLigne"/>
		<!-- -->
		<xsl:variable name="showCodeNaf"
					  select="(infogreffe:Type = $etablissementDansRessortType.PRI or infogreffe:Type = $etablissementDansRessortType.SEP)    and not(//infogreffe:Extrait/infogreffe:Personne/infogreffe:Radiation/infogreffe:DateRadiation)"/>
		<xsl:variable name="codeNaf"
					  select="infogreffe:DonneesInsee/infogreffe:NAF/infogreffe:Code"/>
		<!-- Pour un Etablissement: Liste des Activités -->
		<!-- Selection des activités non fermées Extrait:Etablissement:EtablissementDansRessort:ActiviteFonds:Ferme -->
		<xsl:for-each select="infogreffe:ActiviteFonds[infogreffe:Ferme = false()]">

			<!-- Tri de la liste par Etablissement:EtablissementDansRessort:ActiviteFonds:OrdreAffichage -->
			<xsl:sort select="infogreffe:OrdreAffichage"
					  order="ascending"
					  data-type="number"/>
			<xsl:call-template name="activite">
				<xsl:with-param name="statutDossier" select="$statutDossier"/>
				<xsl:with-param name="statutRegistre" select="$statutRegistre"/>
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				<xsl:with-param name="nomCommercial" select="infogreffe:NomCommercial"/>
				<xsl:with-param name="enseigne" select="infogreffe:Enseigne"/>
				<xsl:with-param name="activite" select="infogreffe:Activite"/>
				<xsl:with-param name="dateDebut" select="infogreffe:DateDebut"/>
				<xsl:with-param name="showCodeNaf" select="$showCodeNaf"/>
				<xsl:with-param name="codeNaf" select="$codeNaf"/>
				<xsl:with-param name="nomDeDomaineEtablissement" select="infogreffe:SiteInternet"/>
				<xsl:with-param name="position" select="position()"/>
			</xsl:call-template>
			<xsl:if test="position() != last()">
				<xsl:call-template name="ligneSeparation"/>
			</xsl:if>
		</xsl:for-each>
	</xsl:template>
	<xsl:template name="siretNAF">
		<xsl:param name="codeNAF"/>
		<xsl:param name="codeNIC"/>
		<xsl:variable name="titre">
		</xsl:variable>
		<xsl:if test="(not(//infogreffe:Diffusabilite) or (//infogreffe:Diffusabilite = 'true'))">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">siretNAF</xsl:with-param>
				<xsl:with-param name="titre">
					<xsl:choose>
						<xsl:when test="$codeNAF and $codeNIC">
							<xsl:value-of select="i18n:getTextPL('info-SiretNaf')"/>
						</xsl:when>
						<xsl:when test="$codeNIC">
							<xsl:value-of select="i18n:getTextPL('info-Siret')"/>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="i18n:getTextPL('info-Naf')"/>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:if test="//infogreffe:DonneesInsee/infogreffe:Siren and $codeNIC">
						<xsl:if test="infg:isNC(infogreffe:Identifiant/infogreffe:Greffe)">
							<xsl:value-of select="infg:format-siren-nc(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
						</xsl:if>
						<xsl:if test="not(infg:isNC(infogreffe:Identifiant/infogreffe:Greffe))">
							<xsl:value-of select="infg:format-siren(//infogreffe:DonneesInsee/infogreffe:Siren)"/>
						</xsl:if>
						<xsl:text> </xsl:text>
						<xsl:value-of select="$codeNIC"/>
						<xsl:if test="$codeNAF">
							<xsl:value-of select="i18n:getTextPLPrefSuff(' ', 'common-tiret', ' ')"/>
						</xsl:if>
					</xsl:if>
					<xsl:if test="$codeNAF">
						<xsl:value-of select="$codeNAF"/>
					</xsl:if>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
	</xsl:template>
	<xsl:template name="activite">
		<xsl:param name="statutDossier"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="statutRegistre"/>
		<xsl:param name="nomCommercial"/>
		<xsl:param name="enseigne"/>
		<xsl:param name="nomDeDomaineEtablissement"/>
		<xsl:param name="activite"/>
		<xsl:param name="dateDebut"/>
		<xsl:param name="showCodeNaf"/>
		<xsl:param name="codeNaf"/>
		<xsl:param name="position"/>
		<xsl:if test="$position = 1">
			<!-- Nom commercial -->
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">nomCommercial</xsl:with-param>
				<xsl:with-param name="titreKey">info-nomCommercial</xsl:with-param>
				<xsl:with-param name="libelle" select="$nomCommercial"/>
			</xsl:call-template>
			<!-- Enseigne -->
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">enseigne</xsl:with-param>
				<xsl:with-param name="titreKey">info-enseigne</xsl:with-param>
				<xsl:with-param name="libelle" select="$enseigne"/>
			</xsl:call-template>
			<xsl:variable name="count_etablissement"
						  select="count(//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = false() and (infogreffe:Type != $etablissementDansRessortType.SIE)])"/>
			<xsl:if test="infg:isPP($statutDossier) and $count_etablissement = 1">
				<xsl:call-template name="observationsSansTitre">
					<xsl:with-param name="ligne">nomDomaineSiteInternet</xsl:with-param>
					<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.INTERNET"/>
					<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
		<xsl:call-template name="sautLigne"/>
		<!-- nom de domaine d'un Etablissement -->
		<xsl:call-template name="informationNomDeDomaine">
			<xsl:with-param name="ligne">nomDeDomaineEtablissement</xsl:with-param>
			<xsl:with-param name="titreKey">info-nomDeDomaineEtablissement</xsl:with-param>
			<xsl:with-param name="libelle" select="$nomDeDomaineEtablissement"/>
		</xsl:call-template>
		<!-- Activités exercées dans l'établissement OU Activités exercées -->
		<xsl:call-template name="information">
			<xsl:with-param name="ligne">activiteEtablissement</xsl:with-param>
			<xsl:with-param name="justify" select="true()"/>
			<xsl:with-param name="titre">
				<xsl:choose>
					<xsl:when test="$position = 1">
						<xsl:value-of select="i18n:getTextPL('info-activiteExercees')"/>
					</xsl:when>
					<xsl:otherwise>
						<xsl:value-of select="i18n:getTextPL('info-brancheActivite')"/>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:with-param>
			<xsl:with-param name="libelle">
				<xsl:value-of select="infg:upperFirstLetter($activite)"/>
			</xsl:with-param>
		</xsl:call-template>
		<xsl:variable name="activiteSaisonniere"
					  select="infogreffe:ActiviteSaisonniere = 'S'"/>
		<xsl:if test="infogreffe:ActiviteAmbulante = true() or $activiteSaisonniere">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">typeActivite</xsl:with-param>
				<xsl:with-param name="titreKey">info-typeActivite</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:choose>
						<xsl:when test="(not(infogreffe:ActiviteAmbulante) or not(infogreffe:ActiviteAmbulante = true())) and $activiteSaisonniere">
							<xsl:value-of select="i18n:getTextPL('libelle-activiteSaisonniere')"/>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="i18n:getTextPL('libelle-activiteAmbulante')"/>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
		<!-- Complement activité saisonnière -->
		<xsl:if test="infogreffe:ActiviteAmbulante = true() and $activiteSaisonniere">
			<xsl:call-template name="complement">
				<xsl:with-param name="libelleKey">libelle-activiteSaisonniere</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
		<!-- Activité non sedentaire -->
		<xsl:if test="infogreffe:NonSedentaire = true()">
			<xsl:call-template name="complement">
				<xsl:with-param name="libelleKey">libelle-activiteNonSedentaire</xsl:with-param>
			</xsl:call-template>
		</xsl:if>
		<!-- NAF -->
		<xsl:if test="$codeNaf and $showCodeNaf and $position = 1">
			<xsl:call-template name="siretNAF">
				<xsl:with-param name="codeNAF" select="$codeNaf"/>
			</xsl:call-template>
		</xsl:if>
		<!-- Date de début d'activité -->
		<xsl:call-template name="information">
			<xsl:with-param name="ligne">dateDebutActivite</xsl:with-param>
			<xsl:with-param name="titreKey">info-dateDebutActivite</xsl:with-param>
			<xsl:with-param name="libelle" select="infg:format-date($dateDebut)"/>
		</xsl:call-template>
		<xsl:call-template name="sautLigne"/>
		<!-- Conditions d'exercice -->
		<!-- 20170228 XAS mantis  	0047520: Affichae sur le KBIS des conditions d'exercice  -->
		<xsl:variable name="chemin_conditionsExercice" select="infogreffe:ConditionsExercice"/>
		<xsl:variable name="count_conditionsExercice"
					  select="count($chemin_conditionsExercice)"/>
		<xsl:if test="$count_conditionsExercice &gt; 0 ">
			<xsl:for-each select="$chemin_conditionsExercice">
				<xsl:sort select="infogreffe:NumeroChrono"
						  order="ascending"
						  data-type="number"/>
				<xsl:choose>
					<xsl:when test="count(infogreffe:Commentaire) =1">
						<xsl:variable name="chemin_conditionsExerciceCommentaire"
									  select="infogreffe:Commentaire"/>
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">conditionExercice</xsl:with-param>
							<xsl:with-param name="titreKey">libelle-conditionExercice</xsl:with-param>
							<xsl:with-param name="libelle">
								<xsl:value-of select="$chemin_conditionsExerciceCommentaire"/>
							</xsl:with-param>
						</xsl:call-template>
					</xsl:when>
					<xsl:when test="count(infogreffe:AttentePiece) &gt;0">
						<xsl:if test="infogreffe:AttentePiece =true() ">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">conditionExercice</xsl:with-param>
								<xsl:with-param name="titreKey"/>
								<xsl:with-param name="libelle">
									<xsl:value-of select="i18n:getTextPL('info-AttentePieceCapacite')"/>
								</xsl:with-param>
							</xsl:call-template>
						</xsl:if>
					</xsl:when>
					<xsl:otherwise>
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">titreAutorite</xsl:with-param>
							<xsl:with-param name="titreKey"/>
							<xsl:with-param name="libelle">
								<xsl:value-of select="i18n:getTextPL('libelle-declarationAutorisationTitreDiplome')"/>
							</xsl:with-param>
						</xsl:call-template>
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">autorite</xsl:with-param>
							<xsl:with-param name="titreKey">libelle-autorite</xsl:with-param>
							<xsl:with-param name="libelle" select="infogreffe:Autorite"/>
						</xsl:call-template>
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">dateDelivrance</xsl:with-param>
							<xsl:with-param name="titreKey">libelle-dateDelivrance</xsl:with-param>
							<xsl:with-param name="libelle" select="infg:format-date(infogreffe:DateDelivrance)"/>
						</xsl:call-template>
						<xsl:if test="count(infogreffe:DateEcheance) &gt;0">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">dateEcheance</xsl:with-param>
								<xsl:with-param name="titreKey">libelle-dateEcheance</xsl:with-param>
								<xsl:with-param name="libelle" select="infg:format-date(infogreffe:DateEcheance)"/>
							</xsl:call-template>
						</xsl:if>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:for-each>
			<xsl:call-template name="sautLigne"/>
		</xsl:if>
		<!-- fin 0047520 -->
		<!-- Conditions d'exercice : ancien traitement-->
		<xsl:variable name="count_etablissement"
					  select="count(//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = false()and (infogreffe:Type != $etablissementDansRessortType.SIE)])"/>
		<xsl:variable name="count_branches_actives"
					  select="count(//infogreffe:Etablissement/infogreffe:EtablissementDansRessort[infogreffe:Ferme = false()and (infogreffe:Type != $etablissementDansRessortType.SIE)]/infogreffe:ActiviteFonds[infogreffe:Ferme = false()])"/>
		<xsl:if test="$count_etablissement eq 1 and $count_branches_actives le 1">
			<xsl:call-template name="observationsSansTitre">
				<xsl:with-param name="ligne">conditionsExercice</xsl:with-param>
				<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.AUTORIS_DIPLOME"/>
				<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
			</xsl:call-template>
		</xsl:if>
		<xsl:call-template name="sautLigne"/>
		<!-- TODO Ajout de Décision d'incapacité ou d'interdiction -->
		<!--xsl:call-template name="information">

		</xsl:call-template-->
		<!-- TODO Ajout de Date d'expiration de l'incapacité ou de l'interdiction -->
		<xsl:call-template name="sautLigne"/>
		<xsl:if test="($statutRegistre = $statutRegistre.RCS)">

			<!-- Origine du fonds ou de l'activité -->
			<xsl:choose>
				<xsl:when test="(infogreffe:OrigineFondsActivite/infogreffe:Type/infogreffe:Code = $origineFondType.DIV         or not(infogreffe:OrigineFondsActivite/infogreffe:Type/infogreffe:Libelle))         and infogreffe:OrigineFondsActivite/infogreffe:ComplementInformation">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">complementOrigineFonds</xsl:with-param>
						<xsl:with-param name="titreKey">info-origineFondsActivite</xsl:with-param>
						<xsl:with-param name="libelle"
										select="infogreffe:OrigineFondsActivite/infogreffe:ComplementInformation"/>
					</xsl:call-template>
				</xsl:when>
				<xsl:when test="infogreffe:OrigineFondsActivite/infogreffe:Type/infogreffe:Code != $origineFondType.DIV         and infogreffe:OrigineFondsActivite/infogreffe:Type/infogreffe:Libelle">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">origineFondsActivite</xsl:with-param>
						<xsl:with-param name="titreKey">info-origineFondsActivite</xsl:with-param>
						<xsl:with-param name="libelle"
										select="infogreffe:OrigineFondsActivite/infogreffe:Type/infogreffe:Libelle"/>
					</xsl:call-template>
				</xsl:when>
				<xsl:when test="infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal       or infogreffe:OrigineFondsActivite/infogreffe:LocationGeranceMandat">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">origineFondsActivite</xsl:with-param>
						<xsl:with-param name="libelleKey">info-origineFondsActivite</xsl:with-param>
					</xsl:call-template>
				</xsl:when>
			</xsl:choose>
			<!-- Complément origine fonds -->
			<xsl:if test="infogreffe:OrigineFondsActivite/infogreffe:ComplementInformation     and infogreffe:OrigineFondsActivite/infogreffe:Type/infogreffe:Code != $origineFondType.DIV     and infogreffe:OrigineFondsActivite/infogreffe:Type/infogreffe:Libelle">
				<xsl:call-template name="complement">
					<xsl:with-param name="libelle"
									select="infogreffe:OrigineFondsActivite/infogreffe:ComplementInformation"/>
				</xsl:call-template>
			</xsl:if>
			<xsl:variable name="origineCode"
						  select="infogreffe:OrigineFondsActivite/infogreffe:Type/infogreffe:Code"/>
			<!-- Liste des intervenants de l'origine du fonds -->
			<!-- Sélectionner les intervenants diffusable : DiffusableExtrait = true -->
			<xsl:for-each select="infogreffe:OrigineFondsActivite/infogreffe:Intervenant[infogreffe:DiffusableExtrait = true()]">

				<!-- Tri par Extrait:Etablissement:EtablissementDansRessort:ActiviteFonds:Intervenants:OrdreAffichage -->
				<xsl:sort select="infogreffe:OrdreAffichage"
						  order="ascending"
						  data-type="number"/>
				<!-- Infomrations concernant le (ou l' ou la ) -->
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">informationConcernant</xsl:with-param>
					<xsl:with-param name="libelle" select="infogreffe:Type/infogreffe:Libelle"/>
				</xsl:call-template>
				<xsl:choose>
					<xsl:when test="$origineCode = $origineFondType.CES and infogreffe:Type/infogreffe:Code = $intervenantType.CEDANT">
						<xsl:choose>
							<xsl:when test="infogreffe:PersonneMorale">
								<xsl:call-template name="information">
									<xsl:with-param name="ligne">designationCedant</xsl:with-param>
									<xsl:with-param name="titreKey">info-designationCedant</xsl:with-param>
									<xsl:with-param name="libelle"
													select="infogreffe:PersonneMorale/infogreffe:Identification/infogreffe:Denomination"/>
								</xsl:call-template>
							</xsl:when>
							<xsl:when test="infogreffe:PersonnePhysique">
								<xsl:call-template name="nomPrenoms">
									<xsl:with-param name="identification"
													select="infogreffe:PersonnePhysique/infogreffe:Identification"/>
									<xsl:with-param name="attributeLigne">designationCedant</xsl:with-param>
									<xsl:with-param name="elementTitre"
													select="i18n:getTextByCodePL('info-designationCedant')"/>
								</xsl:call-template>
							</xsl:when>
						</xsl:choose>
					</xsl:when>
					<xsl:otherwise>
						<xsl:if test="infogreffe:PersonneMorale">
							<xsl:call-template name="personneMorale">
								<xsl:with-param name="cheminPersonneMorale" select="infogreffe:PersonneMorale"/>
								<!-- Qualite d'origine -->
								<xsl:with-param name="titreDeno">
									<xsl:choose>
										<xsl:when test="infg:in-list(infogreffe:Type/infogreffe:Code , ($intervenantType.INDIVISAIRE, $intervenantType.MANDANT, $intervenantType.MANDATAIRE))">
											<xsl:value-of select="i18n:getTextPL('info-raisonSocialeDenomination')"/>
										</xsl:when>
										<xsl:otherwise>
											<xsl:value-of select="i18n:getTextPL('info-denomination')"/>
										</xsl:otherwise>
									</xsl:choose>
								</xsl:with-param>
								<xsl:with-param name="type" select="infogreffe:Type/infogreffe:Code"/>
								<xsl:with-param name="mode" select="$personneMorale.mode.OrigineFonds"/>
							</xsl:call-template>
						</xsl:if>
						<xsl:if test="infogreffe:PersonnePhysique">
							<xsl:call-template name="personnePhysique">
								<xsl:with-param name="cheminPersonnePhysique" select="infogreffe:PersonnePhysique"/>
								<!-- Qualite d'origine -->
								<xsl:with-param name="elementTitre" select="i18n:getTextPL('info-nomPrenoms')"/>
								<xsl:with-param name="type" select="infogreffe:Type/infogreffe:Code"/>
								<xsl:with-param name="mode" select="$personnePhysique.mode.OrigineFonds"/>
							</xsl:call-template>
						</xsl:if>
					</xsl:otherwise>
				</xsl:choose>
				<!-- Complement intervenant de l'origine du fonds - infogreffe:OrigineFondsActivite/infogreffe:Intervenant/infogreffe:Complement -->
				<xsl:if test="infogreffe:Complement">
					<xsl:call-template name="complement">
						<xsl:with-param name="libelle" select="infogreffe:Complement"/>
					</xsl:call-template>
				</xsl:if>
				<xsl:call-template name="sautLigne"/>
			</xsl:for-each>
			<xsl:if test="$origineCode and infg:in-list($origineCode, ($origineFondType.ACH, $origineFondType.ACB, $origineFondType.ASB, $origineFondType.LIC, $origineFondType.PAR,$origineFondType.AFS,$origineFondType.TAD,$origineFondType.LAC))">

				<!-- Nom du journal d'annonces légales -->
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">nomJournalAnnonceLegale</xsl:with-param>
					<xsl:with-param name="titreKey">info-nomJournalAnnonceLegale</xsl:with-param>
					<xsl:with-param name="libelle"
									select="infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal/infogreffe:Journal"/>
				</xsl:call-template>
				<!-- Date de parution -->
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">dateParution</xsl:with-param>
					<xsl:with-param name="titreKey">info-dateParution</xsl:with-param>
					<xsl:with-param name="libelle">
						<xsl:if test="infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal/infogreffe:Date">
							<xsl:if test="infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal/infogreffe:DateFin">
								<xsl:value-of select="i18n:getTextPLPrefSuff(' ', 'common-du', ' ')"/>
							</xsl:if>
							<xsl:value-of select="infg:format-date(infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal/infogreffe:Date)"/>
						</xsl:if>
						<xsl:if test="infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal/infogreffe:DateFin">
							<xsl:value-of select="i18n:getTextPLPrefSuff(' ', 'common-au', ' ')"/>
							<xsl:value-of select="infg:format-date(infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal/infogreffe:DateFin)"/>
						</xsl:if>
						<xsl:if test="infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal/infogreffe:DateRectificatif">
							<xsl:value-of select="i18n:getTextPLPrefSuff(' ', 'libelle-rectifieLe', ' ')"/>
							<xsl:value-of select="infg:format-date(infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal/infogreffe:DateRectificatif)"/>
						</xsl:if>
					</xsl:with-param>
				</xsl:call-template>
				<!-- Complément journal -->
				<xsl:if test="infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal/infogreffe:Complement">
					<xsl:call-template name="complement">
						<xsl:with-param name="libelle"
										select="infogreffe:OrigineFondsActivite/infogreffe:ParutionJournal/infogreffe:Complement"/>
					</xsl:call-template>
				</xsl:if>
			</xsl:if>
			<xsl:call-template name="locationGerance">
				<xsl:with-param name="cheminLocationGerance"
								select="infogreffe:OrigineFondsActivite/infogreffe:LocationGeranceMandat"/>
			</xsl:call-template>
			<xsl:call-template name="sautLigne"/>
			<!-- Mode Exploitation -->
			<xsl:choose>
				<xsl:when test="(infogreffe:ModeExploitation/infogreffe:Type/infogreffe:Code = $modeExploitation.DIV      or not(infogreffe:ModeExploitation/infogreffe:Type/infogreffe:Libelle))      and infogreffe:ModeExploitation/infogreffe:ComplementInformation">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">modeExploitation</xsl:with-param>
						<xsl:with-param name="titreKey">info-modeExploitation</xsl:with-param>
						<!-- Complément mode exploitation -->
						<xsl:with-param name="libelle"
										select="infogreffe:ModeExploitation/infogreffe:ComplementInformation"/>
					</xsl:call-template>
				</xsl:when>
				<xsl:when test="infogreffe:ModeExploitation/infogreffe:Type/infogreffe:Code != $modeExploitation.DIV      and infogreffe:ModeExploitation/infogreffe:Type/infogreffe:Libelle">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">modeExploitation</xsl:with-param>
						<xsl:with-param name="titreKey">info-modeExploitation</xsl:with-param>
						<xsl:with-param name="libelle"
										select="infogreffe:ModeExploitation/infogreffe:Type/infogreffe:Libelle"/>
					</xsl:call-template>
				</xsl:when>
				<xsl:when test="(count(infogreffe:ModeExploitation/infogreffe:Intervenant[infogreffe:DiffusableExtrait = true()]) &gt; 0)       or infogreffe:ModeExploitation/infogreffe:LocationGeranceMandat">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne">modeExploitation</xsl:with-param>
						<xsl:with-param name="libelleKey">info-modeExploitation</xsl:with-param>
					</xsl:call-template>
				</xsl:when>
			</xsl:choose>
			<!-- Complément ModeExploitation -->
			<xsl:if test="infogreffe:ModeExploitation/infogreffe:ComplementInformation     and infogreffe:ModeExploitation/infogreffe:Type/infogreffe:Code != $modeExploitation.DIV     and infogreffe:ModeExploitation/infogreffe:Type/infogreffe:Libelle">
				<xsl:call-template name="complement">
					<xsl:with-param name="libelle"
									select="infogreffe:ModeExploitation/infogreffe:ComplementInformation"/>
				</xsl:call-template>
			</xsl:if>
			<!-- Liste des intervenants du mode d'exploitation -->
			<!-- Sélectionner les intervenants non radiés  Extrait:Etablissement:EtablissementDansRessort:ModeExploitation:Intervenants:DateRadiation non renseignée -->
			<xsl:for-each select="infogreffe:ModeExploitation/infogreffe:Intervenant[infogreffe:DiffusableExtrait = true() and not(contains(' $intervenantType.PERS_ENGAGE_ETS $intervenantType.FONDE_POUVOIRS ', infogreffe:Type/infogreffe:Code))]">

				<!-- Tri par Extrait:Etablissement:EtablissementDansRessort:ModeExploitation:Intervenants:OrdreAffichage -->
				<xsl:sort select="infogreffe:OrdreAffichage"
						  order="ascending"
						  data-type="number"/>
				<!-- Infomrations concernant le (ou l' ou la ) -->
				<xsl:call-template name="information">
					<xsl:with-param name="ligne">informationConcernant</xsl:with-param>
					<xsl:with-param name="libelle" select="infogreffe:Type/infogreffe:Libelle"/>
				</xsl:call-template>
				<xsl:if test="infogreffe:PersonneMorale">
					<xsl:call-template name="personneMorale">
						<xsl:with-param name="cheminPersonneMorale" select="infogreffe:PersonneMorale"/>
						<!-- Qualite d'exploitant -->
						<xsl:with-param name="titreDeno">
							<xsl:choose>
								<xsl:when test="infg:in-list(infogreffe:Type/infogreffe:Code , ($intervenantType.INDIVISAIRE, $intervenantType.MANDANT, $intervenantType.MANDATAIRE))">
									<xsl:value-of select="i18n:getTextPL('info-denominationSociale')"/>
								</xsl:when>
								<xsl:otherwise>
									<xsl:value-of select="i18n:getTextPL('info-denomination')"/>
								</xsl:otherwise>
							</xsl:choose>
						</xsl:with-param>
						<xsl:with-param name="type" select="infogreffe:Type/infogreffe:Code"/>
						<xsl:with-param name="mode" select="$personneMorale.mode.ModeExploitation"/>
					</xsl:call-template>
				</xsl:if>
				<xsl:if test="infogreffe:PersonnePhysique">
					<xsl:call-template name="personnePhysique">
						<xsl:with-param name="cheminPersonnePhysique" select="infogreffe:PersonnePhysique"/>
						<!-- Qualite d'exploitant -->
						<xsl:with-param name="elementTitre" select="i18n:getTextPL('info-nomPrenoms')"/>
						<xsl:with-param name="type" select="infogreffe:Type/infogreffe:Code"/>
						<xsl:with-param name="mode" select="$personnePhysique.mode.ModeExploitation"/>
					</xsl:call-template>
				</xsl:if>
				<!-- Complement intervenant du mode d'exploitation - infogreffe:ModeExploitation/infogreffe:Intervenant/infogreffe:Complement -->
				<xsl:if test="infogreffe:Complement">
					<xsl:call-template name="complement">
						<xsl:with-param name="libelle" select="infogreffe:Complement"/>
					</xsl:call-template>
				</xsl:if>
				<xsl:call-template name="sautLigne"/>
			</xsl:for-each>
			<xsl:call-template name="locationGerance">
				<xsl:with-param name="cheminLocationGerance"
								select="infogreffe:ModeExploitation/infogreffe:LocationGeranceMandat"/>
			</xsl:call-template>
			<xsl:call-template name="sautLigne"/>
		</xsl:if>
	</xsl:template>
	<xsl:template name="cessationActivite">
		<xsl:param name="dateFin"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:if test="$dateFin and not(infg:hasObservationVisible($mentionTypeAggrege.CESS_TOTALE, $cheminObservation))">
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">cessationActivite</xsl:with-param>
				<xsl:with-param name="titreKey">info-sansActiviteCompter</xsl:with-param>
				<xsl:with-param name="libelle">
					<xsl:value-of select="infg:format-date($dateFin)"/>
				</xsl:with-param>
			</xsl:call-template>
			<!--			<xsl:if test="infg:hasObservationVisible($mentionTypeAggrege.CESS_TOTALE, $cheminObservation)">
				<xsl:for-each select="$observationsCessation">
					<!-\- tri par ordre Extrait:Observation:OrdreAffichage -\->
					<xsl:sort select="infogreffe:OrdreAffichage" data-type="number" order="ascending"/>
					<xsl:for-each select="infogreffe:Texte">
						<xsl:call-template name="complement">
							<xsl:with-param name="justify" select="true()"/>
							<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint(current()/text())"/>
						</xsl:call-template>
					</xsl:for-each>
				</xsl:for-each>
			</xsl:if>
-->
		</xsl:if>
		<xsl:call-template name="observationsSansTitre">
			<xsl:with-param name="ligne">cessationActivite</xsl:with-param>
			<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.CESS_TOTALE"/>
			<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
		</xsl:call-template>
	</xsl:template>
	<xsl:template name="observationsSansTitre">
		<xsl:param name="mentionTypeAggrege" required="yes"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="ligne"/>
		<xsl:param name="bloc"/>
		<xsl:if test="infg:hasObservationVisible($mentionTypeAggrege, $cheminObservation)">
			<!-- Les différentes sections de mentions avec une ou des mentions et le ou les intervenants associés -->
			<xsl:for-each select="infg:selectObservationVisible($mentionTypeAggrege, $cheminObservation)">
				<!-- tri par ordre Extrait:Observation:OrdreAffichage -->
				<xsl:sort select="infogreffe:OrdreAffichage"
						  data-type="number"
						  order="ascending"/>
				<xsl:call-template name="observationAvecMention">
					<xsl:with-param name="observation" select="current()"/>
					<xsl:with-param name="ligne" select="$ligne"/>
					<xsl:with-param name="bloc" select="$bloc"/>
				</xsl:call-template>
			</xsl:for-each>
		</xsl:if>
	</xsl:template>
	<xsl:template name="observationAvecMention">
		<xsl:param name="observation" required="yes"/>
		<xsl:param name="ligne"/>
		<xsl:param name="bloc"/>
		<xsl:variable name="numero" select="$observation/infogreffe:Numero"/>
		<xsl:variable name="date" select="$observation/infogreffe:Date"/>
		<xsl:for-each select="$observation/infogreffe:Texte">
			<xsl:choose>
				<xsl:when test="position() = 1">
					<xsl:call-template name="information">
						<xsl:with-param name="ligne" select="$ligne"/>
						<xsl:with-param name="justify" select="true()"/>
						<xsl:with-param name="bloc" select="$bloc"/>
						<xsl:with-param name="titre">
							<xsl:value-of select="concat(i18n:getTextPL('common-tiret'), ' ', i18n:getTextPL('info-mention'), ' ')"/>
							<xsl:if test="$numero">
								<xsl:value-of select="concat(i18n:getTextPL('common-min-numero'), ' ', $numero)"/>
							</xsl:if>
							<xsl:if test="$date">
								<xsl:value-of select="concat(' ', i18n:getTextPL('common-du'), ' ', infg:format-date($date))"/>
							</xsl:if>
						</xsl:with-param>
						<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint(current()/text())"/>
					</xsl:call-template>
				</xsl:when>
				<xsl:otherwise>
					<xsl:call-template name="complement">
						<xsl:with-param name="justify" select="true()"/>
						<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint(current()/text())"/>
					</xsl:call-template>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:for-each>
	</xsl:template>
	<xsl:template name="observationTexteDeuxColonne">
		<xsl:param name="mentionTypeAggrege" required="yes"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="titreKey"/>
		<xsl:param name="ligne"/>
		<xsl:if test="infg:hasObservationVisible($mentionTypeAggrege, $cheminObservation)">
			<xsl:variable name="observations"
						  select="infg:selectObservationVisible($mentionTypeAggrege, $cheminObservation)"/>
			<xsl:for-each select="$observations">

				<!-- tri par ordre Extrait:Observation:OrdreAffichage -->
				<xsl:sort select="infogreffe:OrdreAffichage"
						  data-type="number"
						  order="ascending"/>
				<xsl:choose>
					<xsl:when test="position() = 1">
						<xsl:for-each select="infogreffe:Texte">
							<xsl:choose>
								<xsl:when test="position() = 1 and $titreKey">
									<xsl:call-template name="information">
										<xsl:with-param name="ligne" select="$ligne"/>
										<xsl:with-param name="justify" select="true()"/>
										<xsl:with-param name="titreKey" select="$titreKey"/>
										<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint(current()/text())"/>
									</xsl:call-template>
								</xsl:when>
								<xsl:otherwise>
									<xsl:call-template name="complement">
										<xsl:with-param name="justify" select="true()"/>
										<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint(current()/text())"/>
									</xsl:call-template>
								</xsl:otherwise>
							</xsl:choose>
						</xsl:for-each>
					</xsl:when>
					<xsl:otherwise>
						<xsl:for-each select="infogreffe:Texte">
							<xsl:call-template name="complement">
								<xsl:with-param name="justify" select="true()"/>
								<xsl:with-param name="libelle" select="infg:trimLeandingTowPoint(current()/text())"/>
							</xsl:call-template>
						</xsl:for-each>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:for-each>
		</xsl:if>
	</xsl:template>
	<xsl:template name="siegeDomiciliation">
		<xsl:param name="cheminEtablissementSiegeDomiciliataire"/>
		<xsl:param name="statutRegistre"/>
		<xsl:param name="cheminObservation" required="yes"/>
		<xsl:param name="pp"/>
		<xsl:param name="showObservation" select="true()"/>
		<xsl:choose>
			<xsl:when test="$cheminEtablissementSiegeDomiciliataire and $cheminEtablissementSiegeDomiciliataire[infogreffe:ProvisoireAuDomicileDuRepresentantLegal = true()]">
				<xsl:choose>
					<xsl:when test="$pp">
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">siegeDomiciliation</xsl:with-param>
							<xsl:with-param name="titreKey">info-domiciliationCommun</xsl:with-param>
							<xsl:with-param name="libelleKey">libelle-audomicileRepresentantLegalDomiciliationProvisoire</xsl:with-param>
						</xsl:call-template>
					</xsl:when>
					<xsl:otherwise>
						<xsl:call-template name="information">
							<xsl:with-param name="ligne">siegeDomiciliation</xsl:with-param>
							<xsl:with-param name="libelleKey">info-siegeFixRep</xsl:with-param>
						</xsl:call-template>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:when>
			<xsl:otherwise>
				<xsl:choose>
					<xsl:when test="infg:hasObservationVisible($mentionTypeAggrege.DOMICILIATION, $cheminObservation) and $showObservation">
						<xsl:call-template name="observationsSansTitre">
							<xsl:with-param name="ligne">siegeDomiciliation</xsl:with-param>
							<xsl:with-param name="mentionTypeAggrege" select="$mentionTypeAggrege.DOMICILIATION"/>
							<xsl:with-param name="cheminObservation" select="$cheminObservation"/>
						</xsl:call-template>
					</xsl:when>
					<xsl:otherwise>
						<xsl:if test="($cheminEtablissementSiegeDomiciliataire and ($cheminEtablissementSiegeDomiciliataire/infogreffe:Denomination         or $cheminEtablissementSiegeDomiciliataire/infogreffe:Siren         or $cheminEtablissementSiegeDomiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Libelle))">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">siegeDomiciliation</xsl:with-param>
								<xsl:with-param name="libelleKey">info-domiciliationCommun</xsl:with-param>
							</xsl:call-template>
						</xsl:if>
						<xsl:if test="$cheminEtablissementSiegeDomiciliataire and $cheminEtablissementSiegeDomiciliataire/infogreffe:Denomination">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">denoDom</xsl:with-param>
								<xsl:with-param name="titreKey">info-nomDenoDomiciliataire</xsl:with-param>
								<xsl:with-param name="libelle">
									<xsl:value-of select="$cheminEtablissementSiegeDomiciliataire/infogreffe:Denomination"/>
								</xsl:with-param>
							</xsl:call-template>
						</xsl:if>
						<xsl:if test="$cheminEtablissementSiegeDomiciliataire and ($cheminEtablissementSiegeDomiciliataire/infogreffe:Siren or $cheminEtablissementSiegeDomiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Libelle)">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">numeroImmatriculation</xsl:with-param>
								<xsl:with-param name="titreKey">info-numImmat</xsl:with-param>
								<xsl:with-param name="libelle">
									<xsl:choose>
										<xsl:when test="$cheminEtablissementSiegeDomiciliataire/infogreffe:Siren">
											<xsl:if test="infg:isNC($cheminEtablissementSiegeDomiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Code)">
												<xsl:value-of select="infg:format-siren-nc($cheminEtablissementSiegeDomiciliataire/infogreffe:Siren)"/>
											</xsl:if>
											<xsl:if test="not(infg:isNC($cheminEtablissementSiegeDomiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Code))">
												<xsl:value-of select="infg:format-siren($cheminEtablissementSiegeDomiciliataire/infogreffe:Siren)"/>
											</xsl:if>
											<xsl:if test="$cheminEtablissementSiegeDomiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Libelle">
												<xsl:text> </xsl:text>
												<xsl:value-of select="concat(i18n:getTextByNamePL($statutRegistre), ' ', $cheminEtablissementSiegeDomiciliataire/infogreffe:GreffeImmatriculation/infogreffe:Libelle)"/>
											</xsl:if>
										</xsl:when>
									</xsl:choose>
								</xsl:with-param>
							</xsl:call-template>
						</xsl:if>
						<xsl:if test="$cheminEtablissementSiegeDomiciliataire and $cheminEtablissementSiegeDomiciliataire/infogreffe:DateContrat">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">dateContratDom</xsl:with-param>
								<xsl:with-param name="titreKey">info-nomDateContratDomiciliaire</xsl:with-param>
								<xsl:with-param name="libelle"
												select="infg:format-date($cheminEtablissementSiegeDomiciliataire/infogreffe:DateContrat)"/>
							</xsl:call-template>
						</xsl:if>
						<xsl:if test="$cheminEtablissementSiegeDomiciliataire and $cheminEtablissementSiegeDomiciliataire/infogreffe:DateCessation">
							<xsl:call-template name="information">
								<xsl:with-param name="ligne">dateCessationDom</xsl:with-param>
								<xsl:with-param name="titreKey">info-nomDateCessationDomiciliaire</xsl:with-param>
								<xsl:with-param name="libelle"
												select="infg:format-date($cheminEtablissementSiegeDomiciliataire/infogreffe:DateCessation)"/>
							</xsl:call-template>
						</xsl:if>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>
	<xsl:template name="representantsPermanents">
		<xsl:param name="cheminRepresentantsPermanents"/>
		<xsl:for-each select="$cheminRepresentantsPermanents">
			<!-- Tri par Extrait:Representant:RepresentantPermanant:OrdreAffichage -->
			<xsl:sort select="infogreffe:OrdreAffichage"
					  data-type="number"
					  order="ascending"/>
			<xsl:call-template name="information">
				<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
				<xsl:with-param name="ligne">representantPermanant</xsl:with-param>
				<xsl:with-param name="libelleKey">info-representantPermanent</xsl:with-param>
			</xsl:call-template>
			<xsl:if test="infogreffe:RepresentantPermanentPP">
				<xsl:call-template name="personnePhysique">
					<xsl:with-param name="cheminPersonnePhysique"
									select="infogreffe:RepresentantPermanentPP"/>
					<xsl:with-param name="elementTitre" select="i18n:getTextPL('info-nomPrenoms')"/>
					<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
					<xsl:with-param name="mode" select="$personnePhysique.mode.RepresentantPermanentPP"/>
				</xsl:call-template>
			</xsl:if>
			<xsl:if test="infogreffe:RepresentantPermanentPM">
				<xsl:call-template name="personneMorale">
					<xsl:with-param name="cheminPersonneMorale" select="infogreffe:RepresentantPermanentPM"/>
					<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
					<xsl:with-param name="titreDeno" select="i18n:getTextPL('info-denomination')"/>
				</xsl:call-template>
				<!--xsl:call-template name="representantsPermanents">
					<xsl:with-param name="cheminRepresentantsPermanents" select="infogreffe:RepresentantPermanentPM/infogreffe:RepresentantPermanent"/>
				</xsl:call-template-->
			</xsl:if>
			<xsl:if test="not(last())">
				<xsl:call-template name="sautLigne"/>
			</xsl:if>
		</xsl:for-each>
	</xsl:template>
	<xsl:template name="personnesPouvoir">
		<xsl:param name="cheminPersonnesPouvoir"/>
		<xsl:for-each select="$cheminPersonnesPouvoir">
			<!-- Tri par Extrait:Representant:RepresentantPermanant:OrdreAffichage -->
			<xsl:sort select="infogreffe:OrdreAffichage"
					  data-type="number"
					  order="ascending"/>
			<xsl:call-template name="information">
				<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
				<xsl:with-param name="ligne">personnePouvoir</xsl:with-param>
				<xsl:with-param name="libelleKey">info-personnePouvoir</xsl:with-param>
			</xsl:call-template>
			<xsl:if test="infogreffe:RepresentantPermanentPP">
				<xsl:call-template name="personnePhysique">
					<xsl:with-param name="cheminPersonnePhysique"
									select="infogreffe:RepresentantPermanentPP"/>
					<xsl:with-param name="elementTitre" select="i18n:getTextPL('info-nomPrenoms')"/>
					<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
					<xsl:with-param name="mode" select="$personnePhysique.mode.RepresentantPermanentPP"/>
				</xsl:call-template>
			</xsl:if>
			<xsl:if test="infogreffe:RepresentantPermanentPM">
				<xsl:call-template name="personneMorale">
					<xsl:with-param name="cheminPersonneMorale" select="infogreffe:RepresentantPermanentPM"/>
					<xsl:with-param name="bloc" select="$information.bloc.indentBloc"/>
					<xsl:with-param name="titreDeno" select="i18n:getTextPL('info-denomination')"/>
				</xsl:call-template>
				<!--xsl:call-template name="representantsPermanents">
					<xsl:with-param name="cheminRepresentantsPermanents" select="infogreffe:RepresentantPermanentPM/infogreffe:RepresentantPermanent"/>
				</xsl:call-template-->
			</xsl:if>
			<xsl:if test="not(last())">
				<xsl:call-template name="sautLigne"/>
			</xsl:if>
		</xsl:for-each>
	</xsl:template>
	<xsl:template name="etablissementTitre">
		<xsl:param name="statutDossier"/>
		<xsl:param name="infogreffeSiegeEtranger"/>
		<xsl:param name="infogreffeType"/>
		<xsl:choose>
			<xsl:when test="($statutDossier = 'BCD')">
				<xsl:choose>
					<xsl:when test="($infogreffeSiegeEtranger) and ($infogreffeType = 'PRI')">
						<xsl:choose>
							<xsl:when test="infg:isTahiti(/infogreffe:Dossier/infogreffe:Extrait/infogreffe:Personne/infogreffe:Identifiant/infogreffe:Greffe/infogreffe:Code)">
								<xsl:text>section-RenseignementsRelatifPremierEtablissementImmatriculePolynesieFrancaiseActivite</xsl:text>
							</xsl:when>
							<xsl:otherwise>
								<xsl:text>section-RenseignementsRelatifPremierEtablissementImmatriculeFranceActivite</xsl:text>
							</xsl:otherwise>
						</xsl:choose>
					</xsl:when>
					<xsl:when test="($infogreffeType = 'PRI' or $infogreffeType = 'SEP')">
						<xsl:text>section-RenseignementsRelatifEtablissementPrincipalActivite</xsl:text>
					</xsl:when>
					<xsl:otherwise>
						<xsl:text>section-RenseignementsRelatifEtablissementSecondaireActivite</xsl:text>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:when>
			<xsl:otherwise>
				<xsl:choose>
					<xsl:when test="$infogreffeType = 'PRI'">
						<xsl:text>section-RenseignementsRelatifEtablissementPrincipalActivite</xsl:text>
					</xsl:when>
					<xsl:otherwise>
						<xsl:text>section-RenseignementsRelatifEtablissementSecondaireActivite</xsl:text>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>
	<xsl:template name="locationGerance">
		<xsl:param name="cheminLocationGerance"/>
		<xsl:if test="$cheminLocationGerance">
			<xsl:variable name="dateContratDebutTerme">
				<xsl:if test="$cheminLocationGerance/infogreffe:DateDebut">
					<xsl:value-of select="i18n:getTextPLPrefSuff('', 'libelle-dateContratDebut', ' ')"/>
					<xsl:value-of select="infg:format-date($cheminLocationGerance/infogreffe:DateDebut)"/>
				</xsl:if>
				<xsl:if test="$cheminLocationGerance/infogreffe:DateFin">
					<xsl:value-of select="i18n:getTextPLPrefSuff('     ', 'libelle-dateContratTerme', ' ')"/>
					<xsl:value-of select="infg:format-date($cheminLocationGerance/infogreffe:DateFin)"/>
				</xsl:if>
			</xsl:variable>
			<xsl:variable name="contratrenouvlable">
				<xsl:choose>
					<xsl:when test="$cheminLocationGerance/infogreffe:RenouvellementTaciteReconduction = 'TACITE'">
						<xsl:value-of select="i18n:getTextPL('libelle-renouvlableTaciteReconduction')"/>
					</xsl:when>
					<xsl:when test="$cheminLocationGerance/infogreffe:RenouvellementTaciteReconduction = 'ILLIMITE'">
						<xsl:value-of select="i18n:getTextPL('libelle-indefinimentRenouvlable')"/>
					</xsl:when>
				</xsl:choose>
			</xsl:variable>
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">dateContratDebutTerme</xsl:with-param>
				<xsl:with-param name="titreKey">info-dateContrat</xsl:with-param>
				<xsl:with-param name="libelle" select="$dateContratDebutTerme"/>
				<xsl:with-param name="libelleNoTrim" select="true()"/>
			</xsl:call-template>
			<xsl:call-template name="information">
				<xsl:with-param name="ligne">contratrenouvlable</xsl:with-param>
				<xsl:with-param name="libelle" select="$contratrenouvlable">
				</xsl:with-param>
			</xsl:call-template>
			<xsl:if test="$cheminLocationGerance/infogreffe:Commentaire">
				<xsl:call-template name="complement">
					<xsl:with-param name="libelle">
						<xsl:text> (</xsl:text>
						<xsl:value-of select="$cheminLocationGerance/infogreffe:Commentaire"/>
						<xsl:text>) </xsl:text>
					</xsl:with-param>
				</xsl:call-template>
			</xsl:if>
		</xsl:if>
	</xsl:template>
	<xsl:template name="getArrondissement">
		<xsl:param name="numArrondissement"/>
		<xsl:choose>
			<xsl:when test="$numArrondissement=1">
				<xsl:value-of select="i18n:getTextPL('common-1er')"/>
			</xsl:when>
			<xsl:otherwise>
				<xsl:value-of select="$numArrondissement"/>
				<xsl:value-of select="i18n:getTextPL('common-eme')"/>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>
	<xsl:template name="occultationMsg">
		<libelle>
			<xsl:value-of select="i18n:getTextPL('libelle-occultationMsg')"/>
		</libelle>
	</xsl:template>
	<!-- FIN Templates Utilitaires -->
	<!--  Template balise information -->
	<xsl:template name="entete">
		<xsl:param name="Greffe"/>
		<xsl:param name="showKbisTitre"/>
		<xsl:param name="titreKey"/>
		<xsl:param name="dateFraicheur"/>
		<xsl:param name="URLImage"/>
		<xsl:param name="codeVerification"/>
		<xsl:param name="statutRegistre"/>
		<entete>
			<greffe>
				<xsl:if test="infg:isNC($Greffe/infogreffe:Code)">
					<libelle2>
						<xsl:value-of select="i18n:getTextPL('greffe-libelle2-NC')"/>
					</libelle2>
				</xsl:if>
				<libelle>
					<!-- XAS 20170819 AN2017_INTEGRATION_STRASBOURG_MULHOUSE TI/TMC-->
					<xsl:choose>
						<xsl:when test="infg:isTJ($Greffe/infogreffe:Code)">
							<!-- XAS 20191219 ajout du libelle Tribunal de justice -->
							<xsl:value-of select="i18n:getTextPL('greffe-libelle-TJ')"/>
						</xsl:when>
						<xsl:when test="infg:isTI($Greffe/infogreffe:Code)">
							<xsl:value-of select="i18n:getTextPL('greffe-libelle-TI')"/>
						</xsl:when>
						<xsl:when test="infg:isTMC($Greffe/infogreffe:Code)">
							<xsl:value-of select="i18n:getTextPL('greffe-libelle-TMC')"/>
						</xsl:when>
						<xsl:when test="infg:isTAE($Greffe/infogreffe:Code)">
							<xsl:value-of select="i18n:getTextPL('greffe-libelle-TAE')"/>
						</xsl:when>
						<xsl:when test="infg:isNC($Greffe/infogreffe:Code)">
							<xsl:value-of select="i18n:getTextPL('greffe-libelle-NC')"/>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="i18n:getTextPL('greffe-libelle')"/>
						</xsl:otherwise>
					</xsl:choose>
					<xsl:text> </xsl:text>
					<xsl:choose>
						<xsl:when test="infg:isNC($Greffe/infogreffe:Code)">
							<xsl:value-of select="i18n:getTextPL('greffe-nom-NC')"/>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="infg:accord-greffe($Greffe/infogreffe:Libelle)"/>
						</xsl:otherwise>
					</xsl:choose>
				</libelle>
				<xsl:call-template name="enteteAdresse">
					<xsl:with-param name="adresse" select="$Greffe/infogreffe:Adresse"/>
				</xsl:call-template>
			</greffe>
			<!-- Afficher KBIS uniquement pour le RCS -->
			<xsl:if test="$showKbisTitre">
				<kBis>
					<xsl:value-of select="i18n:getTextPL('titre-kbis')"/>
				</kBis>
			</xsl:if>
			<titre>
				<xsl:value-of select="i18n:getTextPL($titreKey)"/>
			</titre>
			<dateFraicheur>
				<xsl:if test="$dateFraicheur">
					<xsl:text> </xsl:text>
					<xsl:value-of select="i18n:getTextPL('dateFraicheurMsg')"/>
					<xsl:text> </xsl:text>
					<xsl:value-of select="infg:format-date-long(infg:format-date($dateFraicheur))"/>
				</xsl:if>
			</dateFraicheur>
			<gestion>
				<xsl:value-of select="i18n:getTextPL('numeroGestion')"/>
				<xsl:text> </xsl:text>
				<xsl:value-of select="infogreffe:Identifiant/infogreffe:NumeroGestion/*/text()"/>
			</gestion>
			<urlSignature>
				<xsl:value-of select="$URLImage/infogreffe:URLSignature"/>
			</urlSignature>
			<urlSceau>
				<xsl:value-of select="$URLImage/infogreffe:URLSceau"/>
			</urlSceau>
			<urlFiligrane>
				<xsl:value-of select="$URLImage/infogreffe:URLFiligrane"/>
			</urlFiligrane>
			<xsl:if test="($statutRegistre = $statutRegistre.RCS or $codeVerification/infogreffe:CodeVerificationValeur) and $URLImage/infogreffe:URLQRCode">
				<urlQrCode>
					<xsl:value-of select="$URLImage/infogreffe:URLQRCode"/>
					<xsl:text>&amp;width=80&amp;height=80&amp;url=</xsl:text>
				</urlQrCode>
			</xsl:if>
			<xsl:if test="$codeVerification">
				<urlSiteControle>
					<xsl:value-of select="$codeVerification/infogreffe:URLSiteControle"/>
				</urlSiteControle>
				<urlSiteControleVerification>
					<xsl:value-of select="$codeVerification/infogreffe:URLSiteControle"/>
					<xsl:text>/verif?codeVerif=</xsl:text>
					<xsl:value-of select="$codeVerification/infogreffe:CodeVerificationValeur"/>
				</urlSiteControleVerification>
				<codeVerification>
					<xsl:value-of select="i18n:getTextPL('codeVerif')"/>
					<xsl:text> : </xsl:text>
					<xsl:value-of select="$codeVerification/infogreffe:CodeVerificationValeur"/>
				</codeVerification>
			</xsl:if>
			<!-- Variable personneType -->
			<xsl:variable name="personneType">
				<xsl:choose>
					<xsl:when test="$statutRegistre = $statutRegistre.RCS">infogreffe:PersonneRCS</xsl:when>
					<xsl:when test="$statutRegistre = $statutRegistre.RSAC">infogreffe:PersonneRSAC</xsl:when>
					<xsl:when test="$statutRegistre = $statutRegistre.RSEIRL">infogreffe:PersonneRSEIRL</xsl:when>
				</xsl:choose>
			</xsl:variable>
			<xsl:variable name="personne"
						  select="infogreffe:Extrait/infogreffe:Personne/*[name() = $personneType]"/>
			<xsl:variable name="denomination">
				<xsl:choose>
					<xsl:when test="$personne/infogreffe:PersonneMorale">
						<xsl:value-of select="$personne/infogreffe:PersonneMorale/infogreffe:Identification/infogreffe:Denomination"/>
					</xsl:when>
					<xsl:when test="$personne/infogreffe:PersonnePhysique">
						<xsl:value-of select="infg:nomPrenoms($personne/infogreffe:PersonnePhysique/infogreffe:Identification)"/>
					</xsl:when>
				</xsl:choose>
			</xsl:variable>
			<urlEntreprise>
				<xsl:text>https://www.infogreffe.fr/societes/entreprise-societe/</xsl:text>
				<xsl:value-of select="/infogreffe:Dossier/infogreffe:Extrait/infogreffe:Personne/infogreffe:DonneesInsee/infogreffe:Siren"/>
				<xsl:text>-</xsl:text>
				<xsl:value-of select="infg:toFreindlyUrlDeno($denomination)"/>
				<xsl:text>-</xsl:text>
				<xsl:value-of select="infogreffe:Identifiant/infogreffe:Greffe/text()"/>
				<xsl:value-of select="infogreffe:Identifiant/infogreffe:NumeroGestion/*/text()"/>
				<xsl:text>.html</xsl:text>
			</urlEntreprise>
		</entete>
	</xsl:template>
	<xsl:template name="signature">
		<xsl:param name="Greffe"/>
		<signature>
			<xsl:choose>
				<xsl:when test="infg:isNC($Greffe/infogreffe:Code)">				Le registre du commerce de Nouméa			</xsl:when>
				<xsl:otherwise>				Le Greffier			</xsl:otherwise>
			</xsl:choose>
		</signature>
	</xsl:template>
	<xsl:template name="piedDePage">
		<xsl:param name="dateDuJourAvecHeure"/>
		<xsl:param name="greffe"/>
		<xsl:param name="referenceGreffe"/>
		<xsl:param name="statutRegistre"/>
		<piedDePage>
			<rcsVille>
				<!-- 20170306 XAS Mantis 47712 Afficher le bon registre sur l'extrait -->
				<xsl:value-of select="$statutRegistre"/>
				<xsl:text> </xsl:text>
				<xsl:value-of select="$greffe/infogreffe:Libelle"/>
			</rcsVille>
			<dateHeure>
				<xsl:value-of select="infg:format-dateHeure($dateDuJourAvecHeure)"/>
			</dateHeure>
			<referenceGreffe>
				<xsl:value-of select="$referenceGreffe"/>
			</referenceGreffe>
		</piedDePage>
	</xsl:template>
	<xsl:template name="enteteAdresse">
		<xsl:param name="adresse"/>
		<adresse>
			<ligne1>
				<xsl:value-of select="$adresse/infogreffe:Ligne1"/>
			</ligne1>
			<xsl:if test="not(empty($adresse/infogreffe:Ligne2))">
				<ligne2>
					<xsl:value-of select="$adresse/infogreffe:Ligne2"/>
				</ligne2>
			</xsl:if>
			<xsl:if test="not(empty($adresse/infogreffe:Ligne3))">
				<ligne3>
					<xsl:value-of select="$adresse/infogreffe:Ligne3"/>
				</ligne3>
			</xsl:if>
			<codePostal>
				<xsl:value-of select="$adresse/infogreffe:CodePostal"/>
			</codePostal>
			<bureauDistributeur>
				<xsl:value-of select="$adresse/infogreffe:BureauDistributeur"/>
			</bureauDistributeur>
		</adresse>
	</xsl:template>
	<xsl:variable name="information.ligne.complement">complement</xsl:variable>
	<xsl:variable name="information.ligne.barreHorizontale">barreHorizontale</xsl:variable>
	<xsl:variable name="information.ligne.ligneSeparation">ligneSeparation</xsl:variable>
	<xsl:variable name="information.ligne.saut">ligneSaut</xsl:variable>
	<xsl:variable name="information.bloc.indentBloc">indentBloc</xsl:variable>
	<xsl:variable name="information.bloc.indentDeuxBloc">indentBloc2</xsl:variable>
	<xsl:variable name="information.weight.bold">bold</xsl:variable>
	<xsl:template name="section">
		<xsl:param name="titreKey"/>
		<!--xsl:param name="titre"/><xsl:param name="twoLine"/-->
		<xsl:param name="informations"/>
		<section>
			<xsl:if test="$titreKey">
				<xsl:attribute name="titre" select="i18n:getTextPL($titreKey)"/>
				<xsl:attribute name="titreTwoLine" select="i18n:getTextTwoLinePL($titreKey)"/>
			</xsl:if>
			<!--xsl:if test="$titre"><xsl:attribute name="titre" select="$titre"/><xsl:attribute name="twoLine" select="$twoLine"/></xsl:if-->
			<xsl:sequence select="$informations"/>
			<!--xsl:call-template name="sautLigne"/-->
		</section>
	</xsl:template>
	<xsl:template name="information">
		<xsl:param name="ligne"/>
		<xsl:param name="weight"/>
		<xsl:param name="justify"/>
		<xsl:param name="bloc"/>
		<xsl:param name="titre"/>
		<xsl:param name="titreKey"/>
		<xsl:param name="libelle"/>
		<xsl:param name="libelleKey"/>
		<xsl:param name="libelleNoTrim"/>
		<xsl:if test="not((empty($libelle) or $libelle = '') and (empty($libelleKey) or $libelleKey = ''))">
			<information ligne="{$ligne}">
				<xsl:if test="$weight">
					<xsl:attribute name="weight" select="$weight"/>
				</xsl:if>
				<xsl:if test="$bloc">
					<xsl:attribute name="bloc" select="$bloc"/>
				</xsl:if>
				<xsl:if test="$titreKey">
					<titre>
						<xsl:value-of select="i18n:getTextPL($titreKey)"/>
					</titre>
				</xsl:if>
				<xsl:if test="$titre">
					<titre>
						<xsl:value-of select="$titre"/>
					</titre>
				</xsl:if>
				<xsl:if test="$libelle">
					<libelle noTrim="{$libelleNoTrim}">
						<xsl:if test="$justify">
							<xsl:attribute name="justify" select="$justify"/>
						</xsl:if>
						<xsl:if test="$libelle != 'NaN'">
							<xsl:value-of select="$libelle"/>
						</xsl:if>
					</libelle>
				</xsl:if>
				<xsl:if test="$libelleKey">
					<libelle noTrim="{$libelleNoTrim}">
						<xsl:if test="$justify">
							<xsl:attribute name="justify" select="$justify"/>
						</xsl:if>
						<xsl:value-of select="i18n:getTextPL($libelleKey)"/>
					</libelle>
				</xsl:if>
			</information>
		</xsl:if>
	</xsl:template>
	<xsl:template name="informationNomDeDomaine">
		<xsl:param name="ligne"/>
		<xsl:param name="weight"/>
		<xsl:param name="justify"/>
		<xsl:param name="bloc"/>
		<xsl:param name="titre"/>
		<xsl:param name="titreKey"/>
		<xsl:param name="libelle"/>
		<xsl:param name="libelleKey"/>
		<xsl:param name="libelleNoTrim"/>
		<xsl:if test="not((empty($libelle) or $libelle = '') and (empty($libelleKey) or $libelleKey = ''))">
			<xsl:for-each select="$libelle">
				<information ligne="{$ligne}">
					<xsl:if test="$weight">
						<xsl:attribute name="weight" select="$weight"/>
					</xsl:if>
					<xsl:if test="$bloc">
						<xsl:attribute name="bloc" select="$bloc"/>
					</xsl:if>
					<xsl:if test="$titreKey">
						<xsl:if test="position()=1">
							<titre>
								<xsl:value-of select="i18n:getTextPL($titreKey)"/>
							</titre>
						</xsl:if>
						<xsl:if test="not(position()=1)">
							<titre/>
						</xsl:if>
					</xsl:if>
					<xsl:if test="$titre">
						<xsl:if test="position()=1">
							<titre>
								<xsl:value-of select="$titre"/>
							</titre>
						</xsl:if>
						<xsl:if test="not(position()=1)">
							<titre/>
						</xsl:if>
					</xsl:if>
					<xsl:if test="$libelle">
						<libelle noTrim="{$libelleNoTrim}">
							<xsl:if test="$justify">
								<xsl:attribute name="justify" select="$justify"/>
							</xsl:if>
							<xsl:value-of select="current()"/>
						</libelle>
					</xsl:if>
					<xsl:if test="$libelleKey">
						<libelle noTrim="{$libelleNoTrim}">
							<xsl:if test="$justify">
								<xsl:attribute name="justify" select="$justify"/>
							</xsl:if>
							<xsl:value-of select="i18n:getTextPL($libelleKey)"/>
						</libelle>
					</xsl:if>
				</information>
			</xsl:for-each>
		</xsl:if>
	</xsl:template>
	<xsl:template name="complement">
		<xsl:param name="justify"/>
		<xsl:param name="libelle"/>
		<xsl:param name="libelleKey"/>
		<xsl:param name="bloc"/>
		<information ligne="{$information.ligne.complement}">
			<xsl:if test="$bloc">
				<xsl:attribute name="bloc" select="$bloc"/>
			</xsl:if>
			<xsl:if test="$libelle">
				<libelle>
					<xsl:if test="$justify">
						<xsl:attribute name="justify" select="$justify"/>
					</xsl:if>
					<xsl:value-of select="$libelle"/>
				</libelle>
			</xsl:if>
			<xsl:if test="$libelleKey">
				<libelle>
					<xsl:if test="$justify">
						<xsl:attribute name="justify" select="$justify"/>
					</xsl:if>
					<xsl:value-of select="i18n:getTextPL($libelleKey)"/>
				</libelle>
			</xsl:if>
		</information>
	</xsl:template>
	<xsl:template name="barreHorizontale">
		<information ligne="{$information.ligne.barreHorizontale}">
			<libelle>
				<xsl:text> </xsl:text>
			</libelle>
		</information>
	</xsl:template>
	<xsl:template name="sautLigne">
		<information ligne="{$information.ligne.saut}">
			<libelle>
				<xsl:text> </xsl:text>
			</libelle>
		</information>
	</xsl:template>
	<xsl:template name="ligneSeparation">
		<information ligne="{$information.ligne.ligneSeparation}">
			<libelle>
				<xsl:text> ______________________________________ </xsl:text>
			</libelle>
		</information>
	</xsl:template>
	<!--  Fin Template balise information -->
</xsl:stylesheet>
