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

    // ⚠ PLACEHOLDER — sẽ được seed vào SM_Sakura_ZplTemplate (key = "CartonLabel").
    // Xuất từ ZebraDesigner; 10 block bitmap SN gốc đã được thay bằng {snSlots} (xem
    // ZplTemplates.BuildCartonSnSlotZpl + CartonSnSlots bên dưới).
    public const string DefaultCartonLabel = @"^XA
^MMT
^PW1200
^LL1800
^LS0
^FO44,51^GB1107,1693,4^FS
^FO111,892^GB0,847,4^FS
^FO200,892^GB0,847,4^FS
^FO268,892^GB0,847,4^FS
^FO334,895^GB0,847,4^FS
^FO402,892^GB0,847,4^FS
^FO465,892^GB0,847,4^FS
^FO529,56^GFA,57,6732,4,:Z64:eJztwwEJAAAIA7AnMYnBjG+OwwabJKOqWnZVVQufqmrhB7kuzEI=:9C00
^FO49,890^GFA,77,448,64,:Z64:eJxjYCAOyP/HBB9wqLXHohYbAOmvJ1ItLv0UaCdJ/wMcfsWm9gCRYQoCDSSoxQYAnKXy0Q==:B0CA
^FO50,1315^GB61,0,4^FS
^FO201,1310^GFA,69,308,44,:Z64:eJxjYMAJ5P8jA3RZ5v+4AVC6Ho80ulpilRJQ+wDdiSiyf3B7FAQa8EujAACtzKNd:0169
^BY6,11^FT484,833^B7B,11,2,3,5,N
^FH\^FD{pdf417Data}^FS
^FPH,1^FT94,1711^A0B,38,38^FH\^CI28^FDCARTON NUMBER^FS^CI27
^FPH,1^FT250,1711^A0B,38,38^FH\^CI28^FDCARTON CONTAINS^FS^CI27
^FT94,1271^A0B,38,38^FH\^CI28^FD{cartonNumber}^FS^CI27
^BY3,3,63^FT188,1562^BCB,,N,N
^FD{cartonNumber}^FS
^FPH,1^FT317,1711^A0B,38,38^FH\^CI28^FDSKU/PV ID:^FS^CI27
^FPH,1^FT317,1283^A0B,38,38^FH\^CI28^FDDESCRIPTION:^FS^CI27
^FPH,1^FT449,1283^A0B,38,38^FH\^CI28^FDCONDITION:^FS^CI27
^FPH,1^FT449,1711^A0B,38,38^FH\^CI28^FDQUANTITY:^FS^CI27
^FPH,1^FT384,1668^A0B,38,38^FH\^CI28^FD{skuPvId}^FS^CI27
^FPH,1^FT384,1200^A0B,38,38^FH\^CI28^FD{description}^FS^CI27
^FPH,1^FT514,1134^A0B,38,38^FH\^CI28^FD{condition}^FS^CI27
^FPH,1^FT514,1544^A0B,38,38^FH\^CI28^FD{quantity}^FS^CI27
{snSlots}
^XZ
";

    // Màu → (SKU/PV ID, mô tả) cho Carton Label — 1 nguồn dùng chung, đổi màu sửa ở đây.
    public static readonly IReadOnlyDictionary<string, (string SkuPvId, string Description)> CartonColorMeta =
        new Dictionary<string, (string SkuPvId, string Description)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Blue"] = ("RM15A-1000NW", "FOLIO BLUE"),
            ["Pink"] = ("RM15A-1001NW", "FOLIO PINK"),
            ["Green"] = ("RM15A-1002NW", "FOLIO GREEN"),
        };

    // Toạ độ gốc (X,Y) của 10 ô SN trên tem Carton Label, theo thứ tự slot 1-10 —
    // sửa ở đây sau khi test in thực tế, không cần đụng logic build ZPL.
    public static readonly IReadOnlyList<(int X, int Y)> CartonSnSlots = new List<(int X, int Y)>
    {
        (586, 1314), (728, 1314), (875, 1314), (1003, 1314),
        (586, 731),  (728, 731),  (875, 731),  (1003, 731),
        (586, 170),  (728, 170),
    };

    // Kích thước text/barcode cho 10 ô SN — bản đầu (26/2/64) in ra nhỏ hơn nhiều so với
    // bitmap gốc trong thiết kế ZebraDesigner (~312 dots/ô), nên tăng lên đây. Đây là ước
    // lượng ban đầu — chỉnh lại các số này sau khi có bản in thật, không cần đụng logic khác.
    public const int SnTextFontSize = 34;          // ^A0B,<size>,<size>
    public const int SnBarcodeModuleWidth = 3;      // ^BY<width>,...
    public const int SnBarcodeHeight = 90;          // ^BY...,,<height>
    public const int SnTextOffset = 34;             // offset X/Y của field text so với gốc slot
    public const int SnBarcodeOffset = 50;          // offset X/Y của barcode so với gốc slot (phải > SnTextOffset để không đè lên text)

    // 2 dòng ZPL cho 1 SN slot: field text + Code128 barcode (cùng format ^BCB,,N,N
    // 4 tham số như barcode CARTON NUMBER gốc — không dùng ^FH\ vì serial không cần hex-escape).
    public static string BuildCartonSnSlotZpl(int x, int y, string serialNumber) =>
        $"^FT{x + SnTextOffset},{y + 305}^A0B,{SnTextFontSize},{SnTextFontSize}^FH\\^CI28^FD{serialNumber}^FS^CI27\n" +
        $"^BY{SnBarcodeModuleWidth},3,{SnBarcodeHeight}^FT{x + SnBarcodeOffset},{y + 305}^BCB,,N,N^FD{serialNumber}^FS";
}
