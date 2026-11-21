CREATE FUNCTION [dbo].[VerifTypeMetier] (@TypeMetier varchar(5),@Annee integer,@NumAVerif varchar(100),@CodeGreffe varchar(4)) RETURNS bit
BEGIN
	DECLARE @return as bit
	DECLARE @i as integer
	DECLARE @iNumVerif as integer

	declare @STRUCTURE_NUMERO varchar(6)
	declare @SEPARATEUR varchar(5)
	declare @QUANTIEME integer
	declare @LETTRE_CATEGORIE varchar(3)
	declare @CHRONO integer
	declare @TAILLE_CHRONO integer
	declare @CODAGE_ANNEE integer

--> Traitement(s):

	select  	@STRUCTURE_NUMERO =  NMMTR_STRUCTURE_NUMERO,
		@SEPARATEUR =  NMMTR_SEPARATEUR,
		@QUANTIEME =  NMMTR_QUANTIEME,
		@LETTRE_CATEGORIE =  NMMTR_LETTRE_CATEGORIE,
		@TAILLE_CHRONO =  NMMTR_TAILLE_CHRONO,
		@CODAGE_ANNEE = NMMTR_CODAGE_ANNEE
	from 	NUMERO_METIER
	where 	NMMTR_TYPE_METIER = @TypeMetier 
		And NMMTR_ANNEE = @Annee

	--on initialise la variable
	set @return = 1
	set @iNumVerif = 1
	set @i = 1
	    while @i<=len(@STRUCTURE_NUMERO)
	        begin  
	            if substring(@STRUCTURE_NUMERO,@i,1)='A'  and @return = 1
		   	Begin
		 	 --On regarde si les n caractères suivants de notre numéro sont bien l'année
	  		  If (substring(@NumAVerif,@iNumVerif, @CODAGE_ANNEE) = right(@Annee,@CODAGE_ANNEE))
			  	Begin 
				  	Set @return=1
					Set @iNumVerif = @iNumVerif + @CODAGE_ANNEE
		  	              End
			  Else
				Begin
					Set @return = 0
				End
			  End
	            else if substring(@STRUCTURE_NUMERO,@i,1)='S' and @return = 1
			Begin
			   --On regarde si les n caractères suivants de notre numéro sont bien le séparateur
 			   If(substring(@NumAVerif,@iNumVerif, len(@SEPARATEUR)) = @SEPARATEUR)
				Begin 
				  	Set @return=1
					Set @iNumVerif = @iNumVerif + len(@SEPARATEUR)
		  	              End
			  Else
				Begin
					Set @return = 0
				End
			  End 
	            else if substring(@STRUCTURE_NUMERO,@i,1)='C' and @return = 1
			Begin
			   -- On regarde si les n caractères suivants de notre numéro sont bien le chrono 
		   	   If(isnumeric(substring(@NumAVerif,@iNumVerif, @TAILLE_CHRONO))= 1)
				Begin 
				  	Set @return=1
					Set @iNumVerif = @iNumVerif + @TAILLE_CHRONO
		  	              End
			  Else
				Begin
					Set @return = 0
				End
			  End 
	            else if substring(@STRUCTURE_NUMERO,@i,1)='L'  and @return = 1
			Begin
			   -- On regarde si les n caractères suivants de notre numéro sont bien la (ou les) lettre
     			   if(substring(@NumAVerif,@iNumVerif,len(@LETTRE_CATEGORIE))= @LETTRE_CATEGORIE)
				Begin 
				  	Set @return=1
					Set @iNumVerif = @iNumVerif + len(@LETTRE_CATEGORIE)
		  	              End
			  Else
				Begin
					Set @return = 0
				End
			  End 
	            else if substring(@STRUCTURE_NUMERO,@i,1)='Q'  and @return = 1
			Begin
			   -- On regarde si les n caractères suivants de notre numéro sont bien le quantieme
     			   if(substring(@NumAVerif,@iNumVerif,len(@QUANTIEME))= @QUANTIEME)
				Begin 
				  	Set @return=1
					Set @iNumVerif = @iNumVerif + len(@QUANTIEME)
		  	              End
			  Else
				Begin
					Set @return = 0
				End
			  End
	            else if substring(@STRUCTURE_NUMERO,@i,1)='G'  and @return = 1
			Begin
			   -- On regarde si les 4 caractères suivants de notre numéro sont bien le code greffe
     			   If (substring(@NumAVerif,@iNumVerif,4)= @CodeGreffe)
				Begin 
				  	Set @return=1
					Set @iNumVerif = @iNumVerif + 4
		  	              End
			  Else
				Begin
					Set @return = 0
				End
			  End
	            else if substring(@STRUCTURE_NUMERO,@i,1)='K'  and @return = 1
			Begin
		   	 -- On regarde si le caractère suivant de notre numéro est bien une étoile
	     		   If(substring(@NumAVerif,@iNumVerif,1)= '*')
				Begin 
				  	Set @return=1
					Set @iNumVerif = @iNumVerif + 1
		  	              End
			  Else
				Begin
					Set @return = 0
				End
			 End 
	            set @i=@i + 1
	        end

	RETURN @return
END