USE KorTransmittals;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmployeeScoreSnapshots')
BEGIN
    CREATE TABLE dbo.EmployeeScoreSnapshots (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        SnapshotDate    DATE NOT NULL,
        EmployeeId      NVARCHAR(50) NOT NULL,
        EmployeeName    NVARCHAR(200) NOT NULL,
        PrimaryRole     NVARCHAR(50) NOT NULL DEFAULT '',
        BillableRateScore   FLOAT NOT NULL DEFAULT 0,
        EfficiencyScore     FLOAT NOT NULL DEFAULT 0,
        ProjectHealthScore  FLOAT NOT NULL DEFAULT 0,
        ProductivityScore   FLOAT NOT NULL DEFAULT 0,
        ProductivityGrade   NVARCHAR(5) NOT NULL DEFAULT '',
        FeePerHr            FLOAT NOT NULL DEFAULT 0,
        ProjectCount        INT NOT NULL DEFAULT 0,
        PrimaryConstructionType NVARCHAR(100) NOT NULL DEFAULT '',
        CreatedUtc      DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_EmployeeScoreSnapshot UNIQUE (SnapshotDate, EmployeeId)
    );

    CREATE NONCLUSTERED INDEX IX_EmployeeScoreSnapshots_Employee
        ON dbo.EmployeeScoreSnapshots (EmployeeId, SnapshotDate);
END
GO
