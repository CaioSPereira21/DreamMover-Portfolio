IF COL_LENGTH(N'dbo.Partida', N'IdCenario') IS NULL
BEGIN
    ALTER TABLE dbo.Partida ADD IdCenario INT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_Partida_IdCenario'
      AND object_id = OBJECT_ID(N'dbo.Partida')
)
BEGIN
    CREATE INDEX IX_Partida_IdCenario ON dbo.Partida (IdCenario);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_Partida_Cenario'
      AND parent_object_id = OBJECT_ID(N'dbo.Partida')
)
BEGIN
    ALTER TABLE dbo.Partida WITH CHECK
        ADD CONSTRAINT FK_Partida_Cenario
        FOREIGN KEY (IdCenario) REFERENCES dbo.Cenario (Id);

    ALTER TABLE dbo.Partida CHECK CONSTRAINT FK_Partida_Cenario;
END;
