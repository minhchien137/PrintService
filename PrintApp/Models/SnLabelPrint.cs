using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintApp.Models;

[Table("SM_SNLabelPrint")]
public class SnLabelPrint
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(20)]
    public string SerialNumber { get; set; } = "";

    [Required]
    [StringLength(10)]
    public string Model { get; set; } = "";

    [Required]
    [StringLength(2)]
    public string Variant { get; set; } = "";

    [Required]
    [StringLength(20)]
    public string Color { get; set; } = "";

    [Required]
    [StringLength(1)]
    public string ProductionLine { get; set; } = "";

    [Column(TypeName = "date")]
    public DateTime ProductionDate { get; set; }

    [Required]
    [StringLength(3)]
    public string RunningNumber { get; set; } = "";

    public int RunningNumberInt { get; set; }

    public DateTime PrintedAt { get; set; }

    [StringLength(100)]
    public string? PrintedBy { get; set; }

    public Guid BatchId { get; set; }

    [StringLength(50)]
    public string? WorkOrder { get; set; }
}

// ── Request / response DTOs ──────────────────────────────────────────────────

public class SnLabelPrintRequest
{
    public DateTime Date { get; set; }
    public string Variant { get; set; } = "";
    public string Line { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string? PrintedBy { get; set; }
    public string? WorkOrder { get; set; }
}

public class WorkOrderLookupResponse
{
    public string WorkOrder { get; set; } = "";
    public string Variant { get; set; } = "";
    public string Color { get; set; } = "";
    public int TotalQuantity { get; set; }
    public int PrintedQuantity { get; set; }
    public int RemainingQuantity { get; set; }

    // Ngày sản xuất của lần in ĐẦU TIÊN cho Work Order này (null nếu đây là lần in đầu tiên).
    // Toàn bộ nhãn của cùng 1 WO phải dùng chung 1 ngày này — server sẽ tự khóa theo
    // giá trị này khi in tiếp, bất kể ngày hiện tại trên form là gì.
    public DateTime? LockedProductionDate { get; set; }
}

public class ManualUnlockRequest
{
    public string Password { get; set; } = "";
}

public class SnLabelSerialDto
{
    public string SerialNumber { get; set; } = "";
    public string RunningNumber { get; set; } = "";
    public int RunningNumberInt { get; set; }
}

public class SnLabelPrintResponse
{
    public bool Ok { get; set; } = true;
    public Guid BatchId { get; set; }
    public List<SnLabelSerialDto> Serials { get; set; } = new();
    public string Zpl { get; set; } = "";
    public string PrintMode { get; set; } = "";
    public bool? DirectPrintSent { get; set; }
    public string? DirectPrintError { get; set; }
}

public class SnLabelColorSummaryDto
{
    public string Variant { get; set; } = "";
    public string Color { get; set; } = "";
    public int Count { get; set; }
    public string? LastSerial { get; set; }
}

public class SnLabelStatusDto
{
    public DateTime Date { get; set; }
    public string Line { get; set; } = "";
    public string Variant { get; set; } = "";
    public string Color { get; set; } = "";
    public string? LastSerial { get; set; }
    public string NextSerial { get; set; } = "";
    public int Count { get; set; }
    public int RemainingCapacity { get; set; }
    public List<SnLabelColorSummaryDto> ColorSummary { get; set; } = new();
}

public class SnLabelHistoryItemDto
{
    public int Id { get; set; }
    public string SerialNumber { get; set; } = "";
    public string Variant { get; set; } = "";
    public string Color { get; set; } = "";
    public string ProductionLine { get; set; } = "";
    public DateTime PrintedAt { get; set; }
    public string? PrintedBy { get; set; }
    public Guid BatchId { get; set; }
    public string? WorkOrder { get; set; }
}

public class SnLabelHistoryPageDto
{
    public List<SnLabelHistoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
