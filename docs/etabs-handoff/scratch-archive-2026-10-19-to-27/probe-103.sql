USE KorStandards;
GO
BEGIN TRANSACTION;
GO
:r "C:\VIsual Studio Projects\KOR.Drafter\db\103_AnEngineersInkDeclaresWhatItIs.sql"
GO
SELECT @@TRANCOUNT AS [open transactions - must be 1 before the rollback];
ROLLBACK TRANSACTION;
GO
SELECT COUNT(*) AS [rows left after rollback - must be 0] FROM analysis.FormatConvention WHERE CreatedBy = N'migration 103';
GO
