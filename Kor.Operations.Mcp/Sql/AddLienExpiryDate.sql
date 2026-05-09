USE KorMcp;

IF COL_LENGTH('Mcp.CollectionsCase', 'LienExpiryDate') IS NULL
BEGIN
    ALTER TABLE Mcp.CollectionsCase ADD LienExpiryDate DATE NULL;
END;
