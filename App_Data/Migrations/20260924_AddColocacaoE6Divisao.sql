SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.LutadorColocacao', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LutadorColocacao
    (
        IdLutador INT NOT NULL,
        PartidasRealizadas INT NOT NULL CONSTRAINT DF_LutadorColocacao_Partidas DEFAULT (0),
        Vitorias INT NOT NULL CONSTRAINT DF_LutadorColocacao_Vitorias DEFAULT (0),
        Derrotas INT NOT NULL CONSTRAINT DF_LutadorColocacao_Derrotas DEFAULT (0),
        DivisaoTeste TINYINT NOT NULL CONSTRAINT DF_LutadorColocacao_DivisaoTeste DEFAULT (3),
        CriadoEmUtc DATETIME2(0) NOT NULL CONSTRAINT DF_LutadorColocacao_CriadoEm DEFAULT (SYSUTCDATETIME()),
        AtualizadoEmUtc DATETIME2(0) NOT NULL CONSTRAINT DF_LutadorColocacao_AtualizadoEm DEFAULT (SYSUTCDATETIME()),
        FinalizadaEmUtc DATETIME2(0) NULL,
        CONSTRAINT PK_LutadorColocacao PRIMARY KEY (IdLutador),
        CONSTRAINT FK_LutadorColocacao_Lutador FOREIGN KEY (IdLutador) REFERENCES dbo.Lutador(Id) ON DELETE CASCADE,
        CONSTRAINT CK_LutadorColocacao_Partidas CHECK (PartidasRealizadas BETWEEN 0 AND 7),
        CONSTRAINT CK_LutadorColocacao_Resultados CHECK (Vitorias >= 0 AND Derrotas >= 0 AND Vitorias + Derrotas = PartidasRealizadas),
        CONSTRAINT CK_LutadorColocacao_DivisaoTeste CHECK (DivisaoTeste BETWEEN 0 AND 5)
    );
END;

IF OBJECT_ID('dbo.CK_LutadorDivisao_Divisao', 'C') IS NOT NULL
    ALTER TABLE dbo.LutadorDivisao DROP CONSTRAINT CK_LutadorDivisao_Divisao;

ALTER TABLE dbo.LutadorDivisao WITH CHECK
    ADD CONSTRAINT CK_LutadorDivisao_Divisao CHECK (DivisaoAtual BETWEEN 0 AND 6);

IF OBJECT_ID('dbo.CK_PartidaDivisao_Tipo', 'C') IS NOT NULL
    ALTER TABLE dbo.PartidaDivisao DROP CONSTRAINT CK_PartidaDivisao_Tipo;

ALTER TABLE dbo.PartidaDivisao WITH CHECK
    ADD CONSTRAINT CK_PartidaDivisao_Tipo
    CHECK (Tipo IN ('REGULAR', 'COLOCACAO', 'PROMOCAO', 'REBAIXAMENTO'));

IF OBJECT_ID('dbo.CK_PartidaDivisao_Resultado', 'C') IS NOT NULL
    ALTER TABLE dbo.PartidaDivisao DROP CONSTRAINT CK_PartidaDivisao_Resultado;

ALTER TABLE dbo.PartidaDivisao WITH CHECK
    ADD CONSTRAINT CK_PartidaDivisao_Resultado
    CHECK (Resultado IN ('REGULAR', 'EM_COLOCACAO', 'COLOCADO', 'PROMOVIDO', 'REBAIXADO', 'MANTEVE'));

COMMIT TRANSACTION;
