<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<!-- VERSION 202409-2025 - XSD Version (0.32 - 0.10) -->
	<xsl:output method="html" encoding="UTF-8" indent="yes" doctype-public="-//W3C//DTD XHTML 1.0 Transitional//EN" omit-xml-declaration="yes"/>
	<xsl:template match="*|/">
		<xsl:apply-templates/>
	</xsl:template>
	<xsl:template match="text()|@*">
		<xsl:value-of select="."/>
	</xsl:template>
	<xsl:template match="extrait">
		<html>
			<head>
				<link rel="stylesheet" type="text/css" href="./styles/infogreffe_base.css"></link>
				<link rel="stylesheet" type="text/css" href="./styles/kbis.css"></link>
				<title><xsl:value-of select="entete/greffe/libelle"></xsl:value-of></title>
				<script type="text/javascript" src="./js/tools.js"/>
				<script type="text/javascript" src="./js/prototype.js"/>
				<script type="text/javascript" src="./js/kbis.js"/>

				<xsl:variable name="urlFiligrane" select="entete/urlFiligrane"/>
				<style>
					div#conteneur::after {
					content: "";
					background-image: url('<xsl:value-of select="$urlFiligrane"/>');
					background-repeat: no-repeat;
					background-size: 50%;
					top: 25%;
					left: 25%;
					bottom: 0;
					right: 0;
					position: absolute;
					z-index: -1;
					}
				</style>

			</head>
			<body class="simple preventSelection" onload="adapterDimensions('conteneur');">
				<center>
					<div id="conteneur" class="kbis">
						<xsl:call-template name="theExtrait"></xsl:call-template>
					</div>
				</center>
			</body>
		</html>
	</xsl:template>
	<xsl:template name="theExtrait">
		<xsl:variable name="urlSignature" select="entete/urlSignature"/>
		<xsl:variable name="urlSceau" select="entete/urlSceau"/>
		<xsl:variable name="urlQrCode" select="entete/urlQrCode"/>
		<xsl:variable name="urlEntreprise" select="entete/urlEntreprise"/>
		
		<xsl:variable name="urlSiteControle" select="entete/urlSiteControle"/>
		<xsl:variable name="codeVerification" select="entete/codeVerification"/>
		<xsl:variable name="urlSiteControleVerification" select="entete/urlSiteControleVerification"/>
		
		<table class="classeTexteCourant" width="100%" cellspacing="0" cellpadding="0" border="0">
			<tr>
				<td align="left" colspan="2" class="taille10 bold">
				</td>
				<td align="right" valign="top" class="taille11">
					<div class="no-print">
						<div id="imprimer">
						</div>
					</div>
					<div class="no-print">
						<!-- Gestion des boutons : contruction dans l'ActionStruts et traitement via appel Ajax -->
						<div id="panier">
						</div>
					</div>
				</td>
			</tr>
			<tr>
				<td align="left" colspan="2" class="taille10 bold">
					<xsl:value-of select="entete/greffe/libelle2"/>
					<br/>
					<xsl:value-of select="entete/greffe/libelle"/>
				</td>
					<xsl:if test="$urlQrCode">
						<xsl:choose>
							<xsl:when test="$codeVerification">
								<td align="right" class="taille10 bold codeVerif" rowspan="4">
									<div class="codeVerifWrap">
										<xsl:value-of select="$codeVerification"/>
										<br/>
										<a target="_blank">
											<xsl:attribute name="href"><xsl:value-of select="$urlSiteControle"/></xsl:attribute>
											<xsl:value-of select="$urlSiteControle"/>
										</a>
									</div>
								</td>
								<td align="right" class="taille10 bold qrcode" rowspan="4">
									<a target="_blank">
										<xsl:attribute name="href"><xsl:value-of select="$urlSiteControleVerification"/></xsl:attribute>
										<img>
											<xsl:attribute name="src"><xsl:value-of select="$urlQrCode"/><xsl:value-of select="$urlSiteControleVerification"/></xsl:attribute>
										</img>
									</a>
								</td>
							</xsl:when>
							<xsl:otherwise>
								<xsl:if test="$urlEntreprise" >
									<td align="right" class="taille10 bold qrcode" rowspan="4">
										<a target="_blank">
											<xsl:attribute name="href"><xsl:value-of select="$urlEntreprise"/></xsl:attribute>
											<img>
												<xsl:attribute name="src"><xsl:value-of select="$urlQrCode"/><xsl:value-of select="$urlEntreprise"/></xsl:attribute>
											</img>
										</a>
									</td>
								</xsl:if>
							</xsl:otherwise>
						</xsl:choose>
					</xsl:if>
					
					
					
					
					<xsl:if test="$urlQrCode">

					</xsl:if>
			</tr>
			<tr valign="top">
				<td class="taille8">
					<xsl:if test="entete/greffe/adresse/ligne1 and entete/greffe/adresse/ligne1/text()">
						<xsl:value-of select="entete/greffe/adresse/ligne1"/>
						<br/>
					</xsl:if>
					<xsl:if test="entete/greffe/adresse/ligne2 and entete/greffe/adresse/ligne2/text()">
						<xsl:value-of select="entete/greffe/adresse/ligne2"/>
						<br/>
					</xsl:if>
					<xsl:if test="entete/greffe/adresse/ligne3 and entete/greffe/adresse/ligne3/text()">
						<xsl:value-of select="entete/greffe/adresse/ligne3"/>
						<br/>
					</xsl:if>
					<xsl:value-of select="entete/greffe/adresse/codePostal"/><xsl:text>
					</xsl:text><xsl:value-of select="entete/greffe/adresse/bureauDistributeur"/>
					<br/><br/>
					<xsl:value-of select="entete/gestion"/>
					
				</td>
				<td align="center">
				</td>
				<td class="message" align="right" valign="bottom">
				</td>
			</tr>
			<tr>
				<td align="center" colspan="4">
				</td>
			</tr>
			<tr>
				<td align="center" colspan="4">
					<h3 class="extraitTitre"><i><xsl:value-of select="entete/kBis"/></i></h3>
				</td>
			</tr>
		</table>
		<!-- Pied de page avec mention lÃ©gale. besoin de modifier la feuile de style css pour IE. -->
		<!-- Mantis 0032600		<table width="100%">
			<tr>
				<td> -->
					<h4 class="titreWrap">
						<center class="titre">
							<xsl:value-of select="entete/titre"/>
						</center>
					</h4>
					<h5 class="greffeWrap">
						<center class="greffe">
							<xsl:value-of select="entete/dateFraicheur"/>
						</center>
					</h5>
					<xsl:for-each select="section">
						<!-- Non affichage des sections vides -->
						<xsl:if test="child::* and count(child::*) > count(child::*[@ligne ='ligneSaut'])">
							<table class="taille12" width="100%">
								<tr>
									<td width="30%"/>
									<td width="70%"/>
								</tr>
								<tr>
									<td colspan="2" class="sectionTitre">
										<xsl:choose>
											<xsl:when test="@titreTwoLine = 'true'">
												<b class="textUnderline"><xsl:value-of select="@titre"/></b>
											</xsl:when>
											<xsl:otherwise>
												<b><xsl:value-of select="@titre"/></b>
												<hr size="2" class="sousTitreTrait taille2"/>
											</xsl:otherwise>
										</xsl:choose>
									</td>
								</tr>
								<xsl:for-each select="information">
									<xsl:variable name="indentTitre" select="@bloc"/>
									<!-- Une ligne 'titre' : 'libelle' d'information d'une section et Ã©ventuellement 'commentaire' + verrouillage non affichage des lignes sans libellÃ© -->
									<xsl:if test="@ligne != 'complement' and @ligne != 'ligneSaut' and @ligne != 'barreHorizontale' and (libelle/text() or @ligne = 'titreSeul') ">
										<!-- les lignes ayant un titre + un libellÃ© -->
										<xsl:if test="titre">
											<tr class="titreHeight">
												<td align="left" valign="top" class="label {$indentTitre}">
													<!--<b><i><xsl:value-of select="titre/text()" /></i></b>-->
													<i><xsl:value-of select="titre/text()"/></i>
												</td>
												<xsl:variable name="libelleBold">
													<xsl:choose>
														<xsl:when test="@weight">bold</xsl:when>
														<xsl:otherwise>fontNormal</xsl:otherwise>
													</xsl:choose>
												</xsl:variable>
												<xsl:variable name="styleJustify">
													<xsl:choose>
														<xsl:when test="libelle//@justify">textJustify</xsl:when>
														<xsl:otherwise></xsl:otherwise>
													</xsl:choose>
												</xsl:variable>
												
												<td valign="top" class="{$libelleBold} {$styleJustify}">
													<xsl:value-of select="libelle/text()"/>
												</td>
											</tr>
										</xsl:if>
										<!-- les lignes sans titre / libellÃ© uniquement -->
										<xsl:if test="not(titre)">
											<xsl:variable name="libelleBold">
												<xsl:choose>
													<xsl:when test="@weight">bold</xsl:when>
													<xsl:otherwise>fontNormal</xsl:otherwise>
												</xsl:choose>
											</xsl:variable>
											<xsl:variable name="styleJustify">
												<xsl:choose>
													<xsl:when test="libelle//@justify">textJustify</xsl:when>
													<xsl:otherwise></xsl:otherwise>
												</xsl:choose>
											</xsl:variable>
											<tr class="titreHeight">
												<td align="left" valign="top" class="label fontItalic {$indentTitre} {$libelleBold} {$styleJustify} " colspan="2">
													<xsl:value-of select="libelle/text()"/>
												</td>
											</tr>
										</xsl:if>
									</xsl:if>
									<xsl:if test="@ligne = 'complement' and libelle/text()">
										<xsl:variable name="styleJustify">
											<xsl:choose>
												<xsl:when test="libelle//@justify">textJustify</xsl:when>
												<xsl:otherwise>fontNormal</xsl:otherwise>
											</xsl:choose>
										</xsl:variable>
										<tr class="titreHeight">
											<td>
											</td>
											<td valign="top" class="{$styleJustify}">
												<xsl:value-of select="libelle/text()"/>
											</td>
										</tr>
									</xsl:if>
									<xsl:if test="@ligne = 'barreHorizontale'">
										<tr><td colspan="2"><hr size="1" class="barreHorizontaleHeight"/></td></tr>
									</xsl:if>
									<xsl:if test="@ligne = 'ligneSaut'">
										<xsl:if test="not(following-sibling::information[1][@ligne = 'ligneSaut'] or not(following-sibling::*))">
											<tr class="titreHeight"><td colspan="2"></td></tr>
										</xsl:if>
									</xsl:if>
									
								</xsl:for-each>
							</table>
						</xsl:if>
					</xsl:for-each>
		<!-- Mantis 0032600/td>
			</tr>
		</table-->
		<table width="100%" class="sectionBasDePage">
			<tr>
				<td align="right" valign="middle">
						<xsl:value-of select="signature"/> <xsl:text> </xsl:text>
				</td>
			</tr>
			<tr>
				<td align="right" width="85%">
					<img height="60px">
						<xsl:attribute name="src"><xsl:value-of select="$urlSceau"/></xsl:attribute>
					</img>
					<img class="signature">
						<xsl:attribute name="src"><xsl:value-of select="$urlSignature"/></xsl:attribute>
					</img>
				</td>
			</tr>
			<tr>
				<td align="right" colspan="2">FIN DE L'EXTRAIT</td>
			</tr>
			<tr>
				<td align="left" valign="middle" colspan="2">
					<xsl:value-of select="piedDePage/rcsVille"/><xsl:text> - </xsl:text><xsl:value-of select="piedDePage/dateHeure"/>&#160;&#160;&#160;&#160;<xsl:value-of select="piedDePage/referenceGreffe"/>
				</td>
			</tr>
		</table>
	</xsl:template>
</xsl:stylesheet>
