-- SM_SNLabelPrint — SN Label Print (Sakura project)
-- Run against svn_pentaho (same DB as SVN_Astro_Label_Data / SVN_Printer_Info_New / SVN_Toast_Serial_Info).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SM_SNLabelPrint')
BEGIN
    CREATE TABLE dbo.SM_SNLabelPrint
    (
        Id                  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SerialNumber        NVARCHAR(20)      NOT NULL,
        Model               NVARCHAR(10)      NOT NULL,
        Variant             NVARCHAR(2)       NOT NULL,
        Color               NVARCHAR(20)      NOT NULL,
        ProductionLine      NVARCHAR(1)       NOT NULL,
        ProductionDate      DATE              NOT NULL,
        RunningNumber       NVARCHAR(3)       NOT NULL,
        RunningNumberInt    INT               NOT NULL,
        PrintedAt           DATETIME          NOT NULL,
        PrintedBy           NVARCHAR(100)     NULL,
        BatchId             UNIQUEIDENTIFIER  NOT NULL
    );

    CREATE UNIQUE INDEX UX_SM_SNLabelPrint_SerialNumber
        ON dbo.SM_SNLabelPrint (SerialNumber);

    -- Fast MAX(RunningNumberInt) lookup for the (date, line, variant) counter scope.
    CREATE INDEX IX_SM_SNLabelPrint_Counter
        ON dbo.SM_SNLabelPrint (ProductionDate, ProductionLine, Variant, RunningNumberInt);
END
GO

-- SM_Sakura_ZplTemplate — ZPL templates for Sakura features, editable without a code deploy.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SM_Sakura_ZplTemplate')
BEGIN
    CREATE TABLE dbo.SM_Sakura_ZplTemplate
    (
        Id           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TemplateKey  NVARCHAR(50)      NOT NULL,   -- e.g. 'SnLabel'
        Name         NVARCHAR(100)     NULL,
        ZplContent   NVARCHAR(MAX)     NOT NULL,
        IsActive     BIT               NOT NULL DEFAULT (1),
        UpdatedAt    DATETIME          NOT NULL,
        UpdatedBy    NVARCHAR(100)     NULL
    );

    CREATE UNIQUE INDEX UX_SM_Sakura_ZplTemplate_TemplateKey
        ON dbo.SM_Sakura_ZplTemplate (TemplateKey);
END
GO

-- Seed the SN Label placeholder template (edit ZplContent directly once the real
-- Zebra Designer ZPL is ready — no code change needed after that).
IF NOT EXISTS (SELECT 1 FROM dbo.SM_Sakura_ZplTemplate WHERE TemplateKey = 'SnLabel')
BEGIN
    INSERT INTO dbo.SM_Sakura_ZplTemplate (TemplateKey, Name, ZplContent, IsActive, UpdatedAt, UpdatedBy)
    VALUES (
        'SnLabel',
        'SN Label (RM15A)',
        N'^XA
^CI28
^PW406
^LL203
^FO20,20^A0N,28,28^FD{serialNumber}^FS
^FO20,60^BY2^BCN,90,Y,N,N^FD{serialNumber}^FS
^XZ
',
        1,
        GETDATE(),
        'seed'
    );
END
GO
