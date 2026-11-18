<?xml version="1.0" encoding="UTF-8"?>
<!-- VERSION 202409-2025 - XSD Version (0.32 - 0.10) -->
<xsl:stylesheet 
	xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0" 
	xmlns:fo="http://www.w3.org/1999/XSL/Format">
	<!--   -->
	<xsl:output method="xml" version="1.0" encoding="UTF-8" indent="yes"/>
	<xsl:attribute-set name="entete.greffe">
		<xsl:attribute name="font-family">Times</xsl:attribute>
		<xsl:attribute name="font-size">8pt</xsl:attribute>
		<xsl:attribute name="line-height">1.1</xsl:attribute>
	</xsl:attribute-set>
	<xsl:attribute-set name="font11">
		<xsl:attribute name="font-family">Times</xsl:attribute>
		<xsl:attribute name="font-size">11pt</xsl:attribute>
		<xsl:attribute name="line-height">1.1</xsl:attribute>
	</xsl:attribute-set>
	<xsl:attribute-set name="font9">
		<xsl:attribute name="font-family">Times</xsl:attribute>
		<xsl:attribute name="font-size">9pt</xsl:attribute>
		<xsl:attribute name="line-height">1.1</xsl:attribute>
	</xsl:attribute-set>
	<xsl:attribute-set name="font10">
		<xsl:attribute name="font-family">Times</xsl:attribute>
		<xsl:attribute name="font-size">10pt</xsl:attribute>
		<xsl:attribute name="line-height">1.1</xsl:attribute>
	</xsl:attribute-set>
	<xsl:template match="extrait">
		<xsl:variable name="urlSignature" select="entete/urlSignature"/>
		<xsl:variable name="urlSceau" select="entete/urlSceau"/>
		<xsl:variable name="urlFiligrane" select="entete/urlFiligrane"/>
		<xsl:variable name="urlQrCode" select="entete/urlQrCode"/>
		<xsl:variable name="urlEntreprise" select="entete/urlEntreprise"/>
		<xsl:variable name="urlSiteControle" select="entete/urlSiteControle"/>
		<xsl:variable name="codeVerification" select="entete/codeVerification"/>
		<xsl:variable name="urlSiteControleVerification" select="entete/urlSiteControleVerification"/>
		<xsl:variable name="numeroGreffe" select="entete/greffe/numero"/>
		<!-- ******** DEBUT **************** -->
		<xsl:variable name="rectoVerso">
			<xsl:choose>
				<xsl:when test="@recto-verso = 1">end-on-even</xsl:when>
				<xsl:otherwise>no-force</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<!-- ******** FIN **************** -->
		<fo:root 
			xmlns:fo="http://www.w3.org/1999/XSL/Format">
			<fo:layout-master-set>
				<!-- KHI 20180810 mantis 52885 chevauchement de ligne dans le PDF -->
				<fo:simple-page-master master-name="A41" page-height="29.7cm" page-width="21cm" margin-bottom="0.5cm" margin-left="1cm" margin-right="2cm">
					<fo:region-body region-name="xsl-region-body" margin-bottom="0.75cm" margin-top="2.8cm"/>
					<fo:region-before extent="2cm"/>
					<fo:region-after extent="1cm"/>
				</fo:simple-page-master>
				<fo:simple-page-master master-name="A4" page-height="29.7cm" page-width="21cm" margin-bottom="0.5cm" margin-left="1cm" margin-right="2cm">
					<fo:region-body region-name="xsl-region-body" margin-bottom="0.75cm" margin-top="3.5cm"/>
					<fo:region-before extent="2cm"/>
					<fo:region-after extent="1cm"/>
				</fo:simple-page-master>
				<!-- ******** DEBUT **************** -->
				<fo:simple-page-master master-name="blank-page" page-height="29.7cm" page-width="21cm" margin-top="1cm" margin-bottom="1cm" margin-left="2cm" margin-right="2cm">
					<fo:region-body/>
					<fo:region-before region-name="header-blank" extent="29.7cm"/>
				</fo:simple-page-master>
				<fo:page-sequence-master master-name="document">
					<fo:repeatable-page-master-alternatives>
						<fo:conditional-page-master-reference page-position = "first" blank-or-not-blank="not-blank" master-reference="A41"/>
						<fo:conditional-page-master-reference page-position = "rest" blank-or-not-blank="not-blank" master-reference="A4"/>
						<fo:conditional-page-master-reference blank-or-not-blank="blank" master-reference="blank-page"/>
					</fo:repeatable-page-master-alternatives>
				</fo:page-sequence-master>
				<!-- ******** FIN **************** -->
			</fo:layout-master-set>
			<fo:page-sequence master-reference="document">
				<!-- ******** DEBUT **************** -->
				<xsl:attribute name="force-page-count">
					<xsl:value-of select="$rectoVerso"/>
				</xsl:attribute>
				<!-- ******** FIN **************** -->
				<fo:static-content flow-name="xsl-region-before">
					<fo:block-container absolute-position="absolute" top="8.1cm" left="3.25cm" width="10.5cm" height="10.5cm">
						<fo:block>
							<fo:external-graphic height="10.5cm" scaling="uniform" content-width="scale-down-to-fit">
								<xsl:attribute name="src">
									<xsl:value-of select="$urlFiligrane" />
								</xsl:attribute>
							</fo:external-graphic>
						</fo:block>
					</fo:block-container>
					<fo:block line-height="1.1" font-family="Times" padding-before="12pt">
						<fo:table table-layout="fixed" width="100%">
							<fo:table-body>
								<fo:table-row>
									<fo:table-cell>
										<fo:block xsl:use-attribute-sets="entete.greffe" font-weight="bold" font-size="10pt">
											<xsl:value-of select="entete/greffe/libelle2"/>
										</fo:block>
										<fo:block xsl:use-attribute-sets="entete.greffe" font-weight="bold" font-size="10pt">
											<xsl:value-of select="entete/greffe/libelle"/>
										</fo:block>
										<fo:block xsl:use-attribute-sets="entete.greffe">
											<xsl:value-of select="entete/greffe/adresse/ligne1"/>
										</fo:block>
										<fo:block xsl:use-attribute-sets="entete.greffe">
											<xsl:value-of select="entete/greffe/adresse/ligne2"/>
										</fo:block>
										<fo:block xsl:use-attribute-sets="entete.greffe">
											<xsl:value-of select="entete/greffe/adresse/ligne3"/>
										</fo:block>
										<fo:block xsl:use-attribute-sets="entete.greffe">
											<xsl:value-of select="entete/greffe/adresse/codePostal"/>
											<xsl:text>&#xA0;</xsl:text>
											<xsl:value-of select="entete/greffe/adresse/bureauDistributeur"/>
										</fo:block>
										<fo:block xsl:use-attribute-sets="entete.greffe" font-size="10pt" padding-before="8pt">
											<xsl:value-of select="entete/gestion"/>
										</fo:block>
									</fo:table-cell>
								</fo:table-row>
							</fo:table-body>
						</fo:table>
					</fo:block>
				</fo:static-content>
				<fo:static-content flow-name="xsl-region-after">
					<fo:block line-height="1.1" font-family="Times" font-size="8pt" padding-before="12pt">
						<fo:table table-layout="fixed" width="100%">
							<fo:table-column column-width="350px"/>
							<fo:table-column column-width="proportional-column-width(1)"/>
							<fo:table-body>
								<fo:table-row>
									<fo:table-cell>
										<fo:block line-height="1.1" white-space-collapse="false">
											<xsl:value-of select="piedDePage/rcsVille"/>
											<xsl:text> - </xsl:text>
											<xsl:value-of select="piedDePage/dateHeure"/>
											<xsl:text>&#xA0;</xsl:text>
											<xsl:value-of select="piedDePage/referenceGreffe"/>
										</fo:block>
									</fo:table-cell>
									<fo:table-cell>
										<fo:block line-height="1.1" text-align="right">
											<xsl:text> page </xsl:text>
											<fo:page-number/>
											<xsl:text>/</xsl:text>
											<fo:page-number-citation ref-id="nbPageTotal"/>
										</fo:block>
									</fo:table-cell>
								</fo:table-row>
							</fo:table-body>
						</fo:table>
					</fo:block>
				</fo:static-content>
				<fo:flow flow-name="xsl-region-body">
					<!-- Haut de page -->
					<fo:block xsl:use-attribute-sets="font11" font-weight="bold" text-align="center" font-style="italic">
						<xsl:value-of select="entete/kBis"/>
					</fo:block>
					<fo:block xsl:use-attribute-sets="font10" font-weight="bold" text-align="center" space-before.conditionality="retain" space-before="15pt">
						<xsl:value-of select="entete/titre"/>
					</fo:block>
					<fo:block xsl:use-attribute-sets="font10" text-align="center" space-after.conditionality="retain" space-after="11pt">
						<xsl:value-of select="entete/dateFraicheur"/>
					</fo:block>
					<fo:block-container position="fixed" top="0.5cm" right="2cm">
						<fo:table>
							<fo:table-body>
								<fo:table-row>
									<fo:table-cell>
										<xsl:choose>
											<xsl:when test="$urlQrCode">
												<xsl:choose>
													<xsl:when test="$codeVerification">
														<fo:block-container position="absolute" top="10px" right="80px" font-size="8pt" inline-progression-dimension="19em">
															<fo:block line-height="1.1" font-family="Times" font-size="8pt" white-space-collapse="true">
																<fo:block text-align="left">
																	<xsl:value-of select="$codeVerification"/>
																	<fo:block/>
																	<fo:basic-link text-decoration="underline">
																		<xsl:attribute name="external-destination">
																			<xsl:text>url('</xsl:text>
																			<xsl:value-of select="$urlSiteControle"/>
																			<xsl:text>')</xsl:text>
																		</xsl:attribute>
																		<xsl:value-of select="$urlSiteControle"/>
																	</fo:basic-link>
																</fo:block>
															</fo:block>
														</fo:block-container>
														<!--fo:block-container position="absolute" top="1px" right="50px"-->
														<fo:block text-align="right">
															<fo:basic-link>
																<xsl:attribute name="external-destination">
																	<xsl:text>url('</xsl:text>
																	<xsl:value-of select="$urlSiteControleVerification"/>
																	<xsl:text>')</xsl:text>
																</xsl:attribute>
																<fo:external-graphic>
																	<xsl:attribute name="src">
																		<xsl:value-of select="$urlQrCode"/>
																		<xsl:value-of select="$urlSiteControleVerification"/>
																	</xsl:attribute>
																</fo:external-graphic>
															</fo:basic-link>
														</fo:block>
														<!--/fo:block-container-->
													</xsl:when>
													<xsl:otherwise>
														<xsl:if test="$urlEntreprise">
															<fo:block-container position="fixed" top="1px" left="17cm">
																<fo:block>
																	<fo:basic-link>
																		<xsl:attribute name="external-destination">
																			<xsl:text>url('</xsl:text>
																			<xsl:value-of select="$urlEntreprise"/>
																			<xsl:text>')</xsl:text>
																		</xsl:attribute>
																		<fo:external-graphic>
																			<xsl:attribute name="src">
																				<xsl:value-of select="$urlQrCode"/>
																				<xsl:value-of select="$urlEntreprise"/>
																			</xsl:attribute>
																		</fo:external-graphic>
																	</fo:basic-link>
																</fo:block>
															</fo:block-container>
														</xsl:if>
													</xsl:otherwise>
												</xsl:choose>
											</xsl:when>
											<xsl:otherwise>
												<fo:block/>
											</xsl:otherwise>
										</xsl:choose>
									</fo:table-cell>
								</fo:table-row>
							</fo:table-body>
						</fo:table>
					</fo:block-container>
					<!-- Corps de page  -->
					<fo:table table-layout="fixed" width="100%" table-omit-footer-at-break="true">
						<fo:table-column width="100%" column-width="proportional-column-width(1)"/>
						<fo:table-footer>
							<fo:table-row>
								<fo:table-cell>
									<fo:block>
										<!-- Bas de page -->
										<fo:table table-layout="fixed" width="100%">
											<fo:table-column width="100%" column-width="proportional-column-width(1)"/>
											<fo:table-body>
												<fo:table-row font-family="Times" keep-together="always">
													<fo:table-cell>
														<fo:block line-height="1.1" font-size="8.5pt" space-before.conditionality="retain" space-before="0pt">
															<fo:table text-align="right" table-layout="fixed" width="100%">
																<fo:table-body>
																	<fo:table-row>
																		<fo:table-cell>
																			<fo:block line-height="12pt" font-size="10pt" space-before.optimum="10pt" space-before.conditionality="retain" space-after.optimum="8pt" space-after.conditionality="retain">
																				<xsl:value-of select="signature"/>
																			</fo:block>
																		</fo:table-cell>
																	</fo:table-row>
																	<fo:table-row>
																		<fo:table-cell>
																			<fo:block line-height="1.1">
																				<fo:external-graphic height="60px" scaling="uniform" content-width="scale-down-to-fit">
																					<xsl:attribute name="src">
																						<xsl:value-of select="$urlSceau"/>
																					</xsl:attribute>
																				</fo:external-graphic>
																				<fo:external-graphic height="60px" scaling="uniform" content-width="scale-down-to-fit">
																					<xsl:attribute name="src">
																						<xsl:value-of select="$urlSignature"/>
																					</xsl:attribute>
																				</fo:external-graphic>
																			</fo:block>
																		</fo:table-cell>
																	</fo:table-row>
																	<fo:table-row>
																		<fo:table-cell>
																			<fo:block line-height="1.1" space-before.optimum="10pt" space-before.conditionality="retain">FIN DE L'EXTRAIT</fo:block>
																		</fo:table-cell>
																	</fo:table-row>
																</fo:table-body>
															</fo:table>
														</fo:block>
													</fo:table-cell>
												</fo:table-row>
											</fo:table-body>
										</fo:table>
										<fo:block line-height="1.1" id="nbPageTotal"/></fo:block>
								</fo:table-cell>
							</fo:table-row>
						</fo:table-footer>
						<fo:table-body>
							<fo:table-row keep-with-previous.within-page="always">
								<fo:table-cell>
									<fo:block line-height="1.1" keep-with-previous.within-page="always">
										<fo:table text-align="left" table-layout="fixed" width="100%">
											<fo:table-column column-width="205px" />
											<fo:table-column column-width="proportional-column-width(1)"/>
											<fo:table-body>
												<xsl:for-each select="section">
													<!-- Non affichage des sections vides -->
													<xsl:if test="child::* and count(child::*) > count(child::*[@ligne ='ligneSaut'])">
														<!-- Pour éviter les sauts de pages, affichage des sections/informations comme un ensemble  -->
														<fo:table-row xsl:use-attribute-sets="font9" space-after.conditionality="retain" space-after="52pt" keep-with-next="always">
															<fo:table-cell line-height="1.1" number-columns-spanned="2">
																<!-- Titre section -->
																<fo:block text-align="justify" line-height="1.1" font-weight="bold" space-before.conditionality="retain" space-before.optimum="10pt" keep-together.within-column="always">
																	<!-- Une ligne de séparation -->
																	<xsl:choose>
																		<xsl:when test="@titreTwoLine = 'true'">
																			<fo:block line-height="1.1" space-after.conditionality="retain" space-after.optimum="5pt">
																				<fo:inline border-after-width="0.9" border-after-style="solid">
																					<xsl:value-of select="@titre" />
																				</fo:inline>
																			</fo:block>
																		</xsl:when>
																		<xsl:otherwise>
																			<fo:inline>
																				<xsl:value-of select="@titre" />
																			</fo:inline>
																			<fo:block line-height="1.1" space-after.conditionality="retain" space-after.optimum="5pt" space-before.conditionality="retain" space-before.optimum="-7pt">
																				<!-- La valeur de l'attribut 'titre' de la balise 'section' -->
																				<fo:leader leader-pattern="rule" leader-length.optimum="100%" rule-thickness="0.9px"/>
																			</fo:block>
																		</xsl:otherwise>
																	</xsl:choose>
																</fo:block>
															</fo:table-cell>
														</fo:table-row>
														<xsl:for-each select="information">
															<!-- ligne titreSeul si on veut afficher le titre même si le libelle est vide ou null-->
															<xsl:if test="(libelle/text() and @ligne != 'ligneSaut') or @ligne = 'titreSeul'">
																<fo:table-row xsl:use-attribute-sets="font10" space-before.conditionality="retain" space-before.optimum="0pt" space-after.conditionality="retain" space-after.optimum="5pt" text-align="left" height="10pt">
																	<xsl:variable name="indentTitre">
																		<xsl:choose>
																			<xsl:when test="@bloc = 'indentBloc'">1cm</xsl:when>
																			<xsl:when test="@bloc = 'indentBloc2'">2cm</xsl:when>
																			<xsl:otherwise>0cm</xsl:otherwise>
																		</xsl:choose>
																	</xsl:variable>
																	<!-- Une ligne 'titre' : 'libelle' d'information d'une section et éventuellement 'commentaire' + verrouillage non affichage des lignes sans libellé -->
																	<xsl:if test="@ligne != 'complement' and @ligne != 'barreHorizontale'">
																		<!-- les lignes ayant un titre + un libellé -->
																		<xsl:if test="titre">
																			<fo:table-cell padding-bottom="4pt" margin-right="1px">
																				<fo:block line-height="0.9" font-style="italic">
																					<xsl:attribute name="start-indent">
																						<xsl:value-of select="$indentTitre"/>
																					</xsl:attribute>
																					<xsl:value-of select="titre/text()"/>
																				</fo:block>
																			</fo:table-cell>
																			<fo:table-cell padding-bottom="4pt">
																				<xsl:if test="libelle/@noTrim">
																					<fo:block line-height="0.9" white-space-collapse="false">
																						<xsl:if test="@weight">
																							<xsl:attribute name="font-weight">
																								<xsl:value-of select="@weight"/>
																							</xsl:attribute>
																						</xsl:if>
																						<xsl:if test="libelle/@justify">
																							<xsl:attribute name="text-align">justify</xsl:attribute>
																						</xsl:if>
																						<xsl:value-of select="libelle/text()"/>
																					</fo:block>
																				</xsl:if>
																				<xsl:if test="not(libelle/@noTrim)">
																					<fo:block line-height="0.9">
																						<xsl:if test="libelle/@justify">
																							<xsl:attribute name="text-align">justify</xsl:attribute>
																						</xsl:if>
																						<xsl:if test="@weight">
																							<xsl:attribute name="font-weight">
																								<xsl:value-of select="@weight"/>
																							</xsl:attribute>
																						</xsl:if>
																						<xsl:value-of select="libelle/text()"/>
																					</fo:block>
																				</xsl:if>
																			</fo:table-cell>
																		</xsl:if>
																		<!-- les lignes sans titre / libellé uniquement -->
																		<xsl:if test="not(titre)">
																			<fo:table-cell number-columns-spanned="2" padding-bottom="4pt">
																				<fo:block line-height="0.9" font-style="italic">
																					<xsl:if test="@weight">
																						<xsl:attribute name="font-weight">
																							<xsl:value-of select="@weight"/>
																						</xsl:attribute>
																					</xsl:if>
																					<xsl:if test="libelle/@justify">
																						<xsl:attribute name="text-align">justify</xsl:attribute>
																					</xsl:if>
																					<xsl:attribute name="start-indent">
																						<xsl:value-of select="$indentTitre"/>
																					</xsl:attribute>
																					<xsl:value-of select="libelle/text()"/>
																				</fo:block>
																			</fo:table-cell>
																		</xsl:if>
																	</xsl:if>
																	<!-- Cas spécifique pour les intervenants des mentions qui doivent être sous le libellé (texte) de la mention + verrouillage non affichage des lignes sans libellé -->
																	<xsl:if test="@ligne = 'complement'">
																		<fo:table-cell padding-bottom="4pt">
																			<fo:block line-height="0.9"></fo:block>
																		</fo:table-cell>
																		<fo:table-cell padding-bottom="4pt">
																			<fo:block line-height="0.9">
																				<xsl:if test="libelle/@justify">
																					<xsl:attribute name="text-align">justify</xsl:attribute>
																				</xsl:if>
																				<xsl:value-of select="libelle/text()"/>
																			</fo:block>
																		</fo:table-cell>
																	</xsl:if>
																	<xsl:if test="@ligne = 'barreHorizontale'">
																		<fo:table-cell padding-bottom="2.9pt" number-columns-spanned="2">
																			<fo:block line-height="1.1">
																				<fo:leader leader-length.optimum="100%" leader-pattern="rule" rule-thickness="0.1pt"/>
																			</fo:block>
																		</fo:table-cell>
																	</xsl:if>
																</fo:table-row>
															</xsl:if>
															<xsl:if test="@ligne = 'ligneSaut'">
																<xsl:if test="not(following-sibling::information[1][@ligne = 'ligneSaut'] or not(following-sibling::*))">
																	<fo:table-row xsl:use-attribute-sets="font10" space-before.conditionality="retain" space-before.optimum="0pt" space-after.conditionality="retain" space-after.optimum="0pt" text-align="left" height="7pt">
																		<fo:table-cell padding-bottom="0pt" number-columns-spanned="2">
																			<fo:block line-height="0.9" font-style="italic"></fo:block>
																		</fo:table-cell>
																	</fo:table-row>
																</xsl:if>
															</xsl:if>
														</xsl:for-each>
													</xsl:if>
												</xsl:for-each>
											</fo:table-body>
										</fo:table>
									</fo:block>
								</fo:table-cell>
							</fo:table-row>
						</fo:table-body>
					</fo:table>
				</fo:flow>
			</fo:page-sequence>
		</fo:root>
	</xsl:template>
</xsl:stylesheet>