namespace PrintApp.Services;

// Fallback templates — dùng để seed DB (SM_Sakura_ZplTemplate) và làm giá trị dự phòng
// nếu chưa có template nào trong DB. Nguồn thật để CHỈNH SỬA là bảng DB, không phải file này.
public static class ZplTemplates
{
    // ⚠ PLACEHOLDER — sẽ được seed vào SM_Sakura_ZplTemplate (key = "SnLabel").
    // {serialNumber} sẽ được thay vào cả field text người đọc lẫn field Code128 barcode.
    public const string DefaultSnLabel = @"^XA
^CI28
^PW406
^LL203
^FO20,20^A0N,28,28^FD{serialNumber}^FS
^FO20,60^BY2^BCN,90,Y,N,N^FD{serialNumber}^FS
^XZ
";
}
