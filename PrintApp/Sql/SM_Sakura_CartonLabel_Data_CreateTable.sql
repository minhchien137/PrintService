-- SM_Sakura_CartonLabel_Data — 1 dòng cho mỗi CARTON đã IN THÀNH CÔNG (không phải 1 dòng/serial).
-- Serial chứa TOÀN BỘ serial trên carton đó, nối bằng dấu phẩy (VD "RM15A...00,RM15A...01,...");
-- CountSerial là số lượng serial trong chuỗi đó (10 nếu đủ hộp, hoặc phần dư nếu lẻ hộp).
-- Dùng để chặn 1 serial bị in trùng vào 2 carton khác nhau + tính số lượng đã in/còn lại
-- của 1 Work Order (đủ hộp vs lẻ hộp). Run against svn_pentaho.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('dbo.SM_Sakura_CartonLabel_Data'))
BEGIN
    CREATE TABLE dbo.SM_Sakura_CartonLabel_Data (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        Serial          NVARCHAR(400) NOT NULL, -- toàn bộ serial của carton này, nối bằng dấu phẩy
        ScanDate        DATETIME NOT NULL,
        CountSerial     INT NOT NULL, -- số lượng serial trong cột Serial ở trên (10, hoặc phần dư nếu lẻ hộp)
        WorkOrder       NVARCHAR(50) NOT NULL,
        CartonNumber    NVARCHAR(30) NOT NULL,
        Color           NVARCHAR(20) NULL,
        Condition       NVARCHAR(10) NULL
    );

    CREATE INDEX IX_SM_Sakura_CartonLabel_Data_WorkOrder ON dbo.SM_Sakura_CartonLabel_Data (WorkOrder);
END
GO

-- Bảng đã được tạo TỪ TRƯỚC KHI đổi sang lưu 1 dòng/carton (Serial dạng CSV) sẽ vẫn còn
-- Serial NVARCHAR(20) + unique index cũ — insert 1 chuỗi CSV dài (~170 ký tự) vào đó sẽ lỗi
-- "String or binary data would be truncated". Chạy lại file này để tự nới cột + bỏ index cũ.
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.SM_Sakura_CartonLabel_Data') AND name = 'Serial' AND max_length / 2 < 400
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SM_Sakura_CartonLabel_Data_Serial' AND object_id = OBJECT_ID('dbo.SM_Sakura_CartonLabel_Data'))
        DROP INDEX IX_SM_Sakura_CartonLabel_Data_Serial ON dbo.SM_Sakura_CartonLabel_Data;

    ALTER TABLE dbo.SM_Sakura_CartonLabel_Data ALTER COLUMN Serial NVARCHAR(400) NOT NULL;
END
GO
