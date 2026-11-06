-- Banco novo da demonstracao. Os scripts seguintes acrescentam as funcionalidades.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.Lutador', 'U') IS NULL
    CREATE TABLE dbo.Lutador (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Lutador PRIMARY KEY,
        Exp FLOAT NULL, Nivel INT NULL, Nome NVARCHAR(255) NULL,
        Universo NVARCHAR(255) NULL, Autor NVARCHAR(255) NULL,
        Portrait NVARCHAR(255) NULL, Stand NVARCHAR(255) NULL
    );
IF OBJECT_ID('dbo.Tag', 'U') IS NULL
    CREATE TABLE dbo.Tag (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tag PRIMARY KEY,
        Nome NVARCHAR(100) NOT NULL
    );
IF OBJECT_ID('dbo.LutadorTag', 'U') IS NULL
    CREATE TABLE dbo.LutadorTag (
        IdLutador INT NOT NULL, IdTag INT NOT NULL,
        CONSTRAINT PK_LutadorTag PRIMARY KEY (IdLutador, IdTag),
        CONSTRAINT FK_LutadorTag_Lutador FOREIGN KEY (IdLutador) REFERENCES dbo.Lutador(Id),
        CONSTRAINT FK_LutadorTag_Tag FOREIGN KEY (IdTag) REFERENCES dbo.Tag(Id)
    );
IF OBJECT_ID('dbo.Partida', 'U') IS NULL
    CREATE TABLE dbo.Partida (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Partida PRIMARY KEY,
        idPerdedor INT NULL, IdVencedor INT NULL, DataPartida DATETIME2 NULL,
        IdLutadorCasa INT NULL, IdLutadorVisitante INT NULL,
        PontosVencedor INT NULL, PontosPerdedor INT NULL,
        CONSTRAINT FK_Partida_LutadorPerdedor FOREIGN KEY (idPerdedor) REFERENCES dbo.Lutador(Id),
        CONSTRAINT FK_Partida_LutadorVencedor FOREIGN KEY (IdVencedor) REFERENCES dbo.Lutador(Id),
        CONSTRAINT FK_Partida_LutadorCasa FOREIGN KEY (IdLutadorCasa) REFERENCES dbo.Lutador(Id),
        CONSTRAINT FK_Partida_LutadorVisitante FOREIGN KEY (IdLutadorVisitante) REFERENCES dbo.Lutador(Id)
    );
-- Adicionadas em um batch anterior as migracoes que consultam essas colunas.
IF COL_LENGTH('dbo.Lutador', 'RatingElo') IS NULL
    ALTER TABLE dbo.Lutador ADD RatingElo INT NOT NULL CONSTRAINT DF_Lutador_RatingElo DEFAULT (1000) WITH VALUES;
IF COL_LENGTH('dbo.Lutador', 'PastaPersonagem') IS NULL
    ALTER TABLE dbo.Lutador ADD PastaPersonagem NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.Partida', 'IdCenario') IS NULL
    ALTER TABLE dbo.Partida ADD IdCenario INT NULL;
COMMIT TRANSACTION;
