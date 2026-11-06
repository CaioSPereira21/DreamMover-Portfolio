IF COL_LENGTH(N'dbo.Cenario', N'CenasJson') IS NULL
BEGIN
    ALTER TABLE dbo.Cenario ADD CenasJson NVARCHAR(MAX) NULL;
END;
