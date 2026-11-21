-- La part NF525 de la base V2 jetable du banc NumerotationV2Check : ce que lit et écrit le déclencheur
-- TR_NUMERO_METIER_JET de RIG_DEV (copié tel quel dans TR_NUMERO_METIER_JET.rigdev.sql), réduit aux
-- colonnes qu'il touche. Tables lues sur RIG_DEV le 2026-11-21 (lecture seule).
--
-- SP_INSERT_JET est une DOUBLURE : la vraie (RIG_DEV) range l'événement dans JET_P ou JET_NP selon
-- CODE_JET, le signe et ouvre l'exercice fiscal ; celle-ci ne fait que le ranger, ce qui suffit à voir
-- qu'il est émis (NUM-006 CA03). Le code 10 est « N » (non purgeable) dans RIG_DEV.CODE_JET.

CREATE TABLE dbo.PARAMETRAGE (
    PARAM_CODE   varchar(100)  NOT NULL CONSTRAINT PK_PARAMETRAGE PRIMARY KEY,
    PARAM_VALEUR varchar(1000) NULL
);
CREATE TABLE dbo.UTILISATEUR (
    UTLST_ID_UTLST int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UTILISATEUR PRIMARY KEY,
    UTLST_LOGIN    varchar(35)       NOT NULL
);
CREATE TABLE dbo.CODE_JET (
    R_CODEJET_CODE      nvarchar(3) NOT NULL CONSTRAINT PK_CODE_JET PRIMARY KEY,
    R_CODEJET_PURGEABLE varchar(1)  NOT NULL
);
CREATE TABLE dbo.JET_NP (
    JET_ID_JET_NP       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_JET_NP PRIMARY KEY,
    JET_DATE_NP         datetime      NOT NULL CONSTRAINT DF_JET_NP_DATE DEFAULT (GETDATE()),
    JET_CODE_NP         nvarchar(3)   NOT NULL,
    JET_LOGIN_NP        nvarchar(256) NULL,
    JET_INFORMATIONS_NP nvarchar(200) NULL,
    JET_CODE_CAISSE_NP  nvarchar(15)  NULL,
    JET_SIGNATURE_NP    nvarchar(max) NULL
);
INSERT dbo.CODE_JET (R_CODEJET_CODE, R_CODEJET_PURGEABLE) VALUES (N'10', 'N');
GO
CREATE PROCEDURE dbo.SP_INSERT_JET
    @JetCode nvarchar(3), @JetCodeOp nvarchar(256), @JetInfo nvarchar(200), @JetCodeCaiss nvarchar(15)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.CODE_JET WHERE R_CODEJET_CODE = @JetCode)
        THROW 50000, 'SP_INSERT_JET : code evenement inconnu dans CODE_JET.', 1;
    INSERT dbo.JET_NP (JET_CODE_NP, JET_LOGIN_NP, JET_INFORMATIONS_NP, JET_CODE_CAISSE_NP)
    VALUES (@JetCode, @JetCodeOp, @JetInfo, @JetCodeCaiss);
END;
GO
