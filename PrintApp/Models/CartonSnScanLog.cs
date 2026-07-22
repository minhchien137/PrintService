using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintApp.Models;

// 1 dòng cho MỖI CARTON đã được IN THÀNH CÔNG (không phải 1 dòng/serial nữa) — dùng để (1) chặn
// 1 serial bị in trùng vào 2 carton khác nhau (kiểm tra DB lúc quét), (2) tính đã in/còn lại của
// 1 Work Order để quyết định carton hiện tại là đủ hộp (PcsPerCarton) hay lẻ hộp (phần dư).
// Serial/ScanDate/CountSerial là 3 cột bắt buộc theo yêu cầu nghiệp vụ:
//   - Serial: TOÀN BỘ serial trên carton này, nối chuỗi bằng dấu phẩy (VD "RM15A...00,RM15A...01,...").
//   - CountSerial: số lượng serial trong chuỗi Serial ở trên — 10 nếu đủ hộp, hoặc phần dư
//     (VD 5) nếu là carton lẻ hộp cuối cùng của Work Order.
//   - ScanDate: thời điểm in carton này.
[Table("SM_Sakura_CartonLabel_Data")]
public class CartonSnScanLog
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(400)]
    public string Serial { get; set; } = "";

    [Required]
    public DateTime ScanDate { get; set; }

    public int CountSerial { get; set; }

    [Required]
    [StringLength(50)]
    public string WorkOrder { get; set; } = "";

    [Required]
    [StringLength(30)]
    public string CartonNumber { get; set; } = "";

    [StringLength(20)]
    public string? Color { get; set; }

    [StringLength(10)]
    public string? Condition { get; set; }
}
