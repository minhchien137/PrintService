namespace PrintApp.Models;

// Payload gửi lên từ view Carton SN (PrintApp/Views/Sakura/CartonSN.cshtml) để build ZPL
// cho tem Carton Label (template key "CartonLabel" trong SM_Sakura_ZplTemplate).
public class CartonLabelPrintRequest
{
    public string CartonNumber { get; set; } = "";
    public string Color { get; set; } = "";
    public string Condition { get; set; } = "";
    public List<string> SerialNumbers { get; set; } = new();
}
