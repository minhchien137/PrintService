using System.Data;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PrintApp.Data;
using PrintApp.Models;

namespace PrintApp.Services;

// Service chung cho các chức năng thuộc dự án Sakura.
// SN Label Print là chức năng đầu tiên — các chức năng Sakura khác sẽ được thêm vào đây.
public class SakuraService
{
    private readonly AppDbContext _context;

    // Model hiện tại luôn là RM15A — để hằng số cho dễ đổi sau này.
    public const string Model = "RM15A";

    // Giới hạn số lượng in mỗi lần. Nhập thủ công giữ mức thấp để tránh gõ nhầm;
    // in theo Work Order tin tưởng số liệu từ Odoo nên cho phép cao hơn.
    public const int ManualMaxQuantity = 500;
    public const int WorkOrderMaxQuantity = 5000;

    private const string Base34Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // không có O, I
    private const int Base34 = 34;
    public const int MaxRunningNumberInt = Base34 * Base34 * Base34 - 1; // "ZZZ" = 39303

    public static readonly (string Variant, string Color)[] Variants =
    {
        ("00", "Blue"),
        ("01", "Pink"),
        ("02", "Green"),
    };

    private static readonly Dictionary<string, string> VariantColorMap =
        Variants.ToDictionary(v => v.Variant, v => v.Color);

    public SakuraService(AppDbContext context)
    {
        _context = context;
    }

    // ── Base34 helpers ────────────────────────────────────────────────────────

    public static string Base34Encode(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Giá trị phải >= 0.");

        if (value == 0) return "0";

        var sb = new StringBuilder();
        while (value > 0)
        {
            sb.Insert(0, Base34Alphabet[value % Base34]);
            value /= Base34;
        }
        return sb.ToString();
    }

    public static int Base34Decode(string s)
    {
        if (string.IsNullOrEmpty(s))
            throw new ArgumentException("Chuỗi base34 rỗng.", nameof(s));

        int result = 0;
        foreach (char raw in s)
        {
            char c = char.ToUpperInvariant(raw);
            int idx = Base34Alphabet.IndexOf(c);
            if (idx < 0)
                throw new ArgumentException($"Ký tự không hợp lệ trong chuỗi base34: '{raw}'.", nameof(s));
            result = result * Base34 + idx;
        }
        return result;
    }

    public static string ResolveColor(string variant) =>
        VariantColorMap.TryGetValue(variant, out var color) ? color
            : throw new SakuraValidationException("common.invalidVariant", $"Variant không hợp lệ: {variant}", new { variant });

    // Chấp nhận cả tên màu (không phân biệt hoa/thường) lẫn mã variant ("00"/"01"/"02")
    // vì chưa biết API Work Order thật sẽ trả về dạng nào.
    public static string? TryResolveVariantFromColor(string colorOrVariant)
    {
        if (string.IsNullOrWhiteSpace(colorOrVariant)) return null;
        string s = colorOrVariant.Trim();

        var byVariant = Variants.FirstOrDefault(v => string.Equals(v.Variant, s, StringComparison.OrdinalIgnoreCase));
        if (byVariant.Variant != null) return byVariant.Variant;

        var byColor = Variants.FirstOrDefault(v => string.Equals(v.Color, s, StringComparison.OrdinalIgnoreCase));
        return byColor.Variant;
    }

    // ── Serial number formatting ─────────────────────────────────────────────

    public static string BuildSerial(string variant, DateTime productionDate, string line, string runningNumber)
    {
        char yearChar = (char)('0' + (productionDate.Year % 10));
        string day = productionDate.DayOfYear.ToString("D3");
        return $"{Model}{variant}{yearChar}{day}{line}{runningNumber}";
    }

    private static string FormatRunning(int runningInt) => Base34Encode(runningInt).PadLeft(3, '0');

    // ── SN Label generation (concurrency-safe) ───────────────────────────────

    public async Task<List<SnLabelPrint>> GenerateNextSerialsAsync(
        DateTime date, string variant, string line, int quantity, string? printedBy,
        string? workOrder = null, int? workOrderTotalQuantity = null)
    {
        int maxQuantity = string.IsNullOrWhiteSpace(workOrder) ? ManualMaxQuantity : WorkOrderMaxQuantity;
        if (quantity < 1 || quantity > maxQuantity)
            throw new SakuraValidationException("print.invalidQuantity", $"Số lượng phải từ 1 đến {maxQuantity}.", new { max = maxQuantity });
        if (line != "0" && line != "1")
            throw new SakuraValidationException("print.invalidLine", "Line không hợp lệ (chỉ 0 hoặc 1).");

        string color = ResolveColor(variant); // throws if invalid
        DateTime prodDate = date.Date;

        const int maxRetries = 5;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                // Khóa ngày sản xuất theo lần in ĐẦU TIÊN của Work Order này — bất kể ngày
                // hiện tại trên form là gì, để toàn bộ nhãn cùng 1 WO luôn dùng chung 1 ngày
                // (vd in 2000/3000 ngày 8/7, hôm sau in tiếp 1000 còn lại vẫn phải ra ngày 8/7).
                if (!string.IsNullOrWhiteSpace(workOrder))
                {
                    var existingDate = await _context.SnLabelPrints
                        .Where(x => x.WorkOrder == workOrder)
                        .OrderBy(x => x.PrintedAt)
                        .Select(x => (DateTime?)x.ProductionDate)
                        .FirstOrDefaultAsync();
                    if (existingDate is DateTime lockedDate)
                        prodDate = lockedDate;
                }

                // Re-check "còn lại bao nhiêu" ngay trong transaction — phòng trường hợp
                // người khác đã in thêm cho cùng Work Order này sau khi lookup nhưng
                // trước khi bấm PRINT (vd 2 máy cùng thao tác 1 WO).
                if (!string.IsNullOrWhiteSpace(workOrder) && workOrderTotalQuantity is int totalQty)
                {
                    int alreadyPrinted = await _context.SnLabelPrints
                        .Where(x => x.WorkOrder == workOrder)
                        .CountAsync();
                    int woRemaining = totalQty - alreadyPrinted;
                    if (quantity > woRemaining)
                    {
                        int clampedRemaining = Math.Max(0, woRemaining);
                        throw new SakuraConflictException(
                            "print.workOrderQuantityExceeded",
                            $"Work Order '{workOrder}' chỉ còn {clampedRemaining} trên tổng {totalQty} có thể in (đã in {alreadyPrinted}).",
                            new { wo = workOrder, remaining = clampedRemaining, total = totalQty, printed = alreadyPrinted });
                    }
                }

                int lastRunning = await _context.SnLabelPrints
                    .Where(x => x.ProductionDate == prodDate && x.ProductionLine == line && x.Variant == variant)
                    .Select(x => (int?)x.RunningNumberInt)
                    .MaxAsync() ?? -1;

                int startRunning = lastRunning + 1;
                if (startRunning + quantity - 1 > MaxRunningNumberInt)
                {
                    int remaining = Math.Max(0, MaxRunningNumberInt - startRunning + 1);
                    throw new SakuraConflictException(
                        "print.serialCapacityExceeded",
                        $"Không thể sinh serial: số thứ tự sẽ vượt quá ZZZ ({MaxRunningNumberInt}). " +
                        $"Chỉ còn {remaining} serial khả dụng cho {color} / Line {line} / {prodDate:yyyy-MM-dd}.",
                        new { max = MaxRunningNumberInt, remaining, color, line, date = prodDate.ToString("yyyy-MM-dd") });
                }

                var batchId = Guid.NewGuid();
                var printedAt = VietnamNow();
                var rows = new List<SnLabelPrint>(quantity);

                for (int i = 0; i < quantity; i++)
                {
                    int runningInt = startRunning + i;
                    string runningStr = FormatRunning(runningInt);
                    string serial = BuildSerial(variant, prodDate, line, runningStr);

                    rows.Add(new SnLabelPrint
                    {
                        SerialNumber = serial,
                        Model = Model,
                        Variant = variant,
                        Color = color,
                        ProductionLine = line,
                        ProductionDate = prodDate,
                        RunningNumber = runningStr,
                        RunningNumberInt = runningInt,
                        PrintedAt = printedAt,
                        PrintedBy = printedBy,
                        BatchId = batchId,
                        WorkOrder = workOrder
                    });
                }

                _context.SnLabelPrints.AddRange(rows);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                return rows;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await tx.RollbackAsync();
                // Va chạm với 1 request khác đang sinh cùng lúc — thử lại với MAX mới.
                continue;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        throw new SakuraConflictException("print.concurrencyFailed", "Không thể sinh serial do tranh chấp đồng thời quá nhiều lần, vui lòng thử lại.");
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);

    // Tổng số label đã in cho 1 Work Order (không phân biệt ngày/line — cộng dồn
    // qua nhiều lượt in khác nhau). Dùng để tính "còn lại bao nhiêu" khi lookup.
    public async Task<int> GetWorkOrderPrintedCountAsync(string workOrder)
    {
        return await _context.SnLabelPrints
            .AsNoTracking()
            .CountAsync(x => x.WorkOrder == workOrder);
    }

    // Ngày sản xuất của lần in đầu tiên cho Work Order này (null nếu chưa in lần nào).
    // Dùng để hiển thị/khóa ô ngày trên form khi lookup lại 1 WO đã in dở.
    public async Task<DateTime?> GetWorkOrderProductionDateAsync(string workOrder)
    {
        return await _context.SnLabelPrints
            .AsNoTracking()
            .Where(x => x.WorkOrder == workOrder)
            .OrderBy(x => x.PrintedAt)
            .Select(x => (DateTime?)x.ProductionDate)
            .FirstOrDefaultAsync();
    }

    // ── Status / summary ──────────────────────────────────────────────────────

    public async Task<SnLabelStatusDto> GetStatusAsync(DateTime date, string variant, string line)
    {
        string color = ResolveColor(variant);
        DateTime prodDate = date.Date;

        var dayLineRows = await _context.SnLabelPrints
            .AsNoTracking()
            .Where(x => x.ProductionDate == prodDate && x.ProductionLine == line)
            .Select(x => new { x.Variant, x.SerialNumber, x.RunningNumberInt, x.Color })
            .ToListAsync();

        var forVariant = dayLineRows.Where(x => x.Variant == variant).ToList();
        var last = forVariant.OrderByDescending(x => x.RunningNumberInt).FirstOrDefault();
        int nextRunning = (last?.RunningNumberInt ?? -1) + 1;

        string nextSerial = nextRunning > MaxRunningNumberInt
            ? ""
            : BuildSerial(variant, prodDate, line, FormatRunning(nextRunning));

        var summary = Variants.Select(v =>
        {
            var rows = dayLineRows.Where(x => x.Variant == v.Variant).ToList();
            var lastForColor = rows.OrderByDescending(x => x.RunningNumberInt).FirstOrDefault();
            return new SnLabelColorSummaryDto
            {
                Variant = v.Variant,
                Color = v.Color,
                Count = rows.Count,
                LastSerial = lastForColor?.SerialNumber
            };
        }).ToList();

        return new SnLabelStatusDto
        {
            Date = prodDate,
            Line = line,
            Variant = variant,
            Color = color,
            LastSerial = last?.SerialNumber,
            NextSerial = nextSerial,
            Count = forVariant.Count,
            RemainingCapacity = Math.Max(0, MaxRunningNumberInt - nextRunning + 1),
            ColorSummary = summary
        };
    }

    // ── History ───────────────────────────────────────────────────────────────

    public async Task<SnLabelHistoryPageDto> GetHistoryAsync(DateTime? date, string? workOrder, string? serialNumber, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _context.SnLabelPrints.AsNoTracking().AsQueryable();

        if (date.HasValue)
        {
            DateTime prodDate = date.Value.Date;
            query = query.Where(x => x.ProductionDate == prodDate);
        }

        // Chọn từ dropdown (chỉ liệt kê các WO đã có sẵn trong DB) nên khớp chính xác,
        // không cần "chứa" như trước.
        if (!string.IsNullOrWhiteSpace(workOrder))
        {
            string wo = workOrder.Trim();
            query = query.Where(x => x.WorkOrder == wo);
        }

        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            string sn = serialNumber.Trim();
            query = query.Where(x => x.SerialNumber.Contains(sn));
        }

        int totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.PrintedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SnLabelHistoryItemDto
            {
                Id = x.Id,
                SerialNumber = x.SerialNumber,
                Variant = x.Variant,
                Color = x.Color,
                ProductionLine = x.ProductionLine,
                PrintedAt = x.PrintedAt,
                PrintedBy = x.PrintedBy,
                BatchId = x.BatchId,
                WorkOrder = x.WorkOrder,
                ReprintCount = x.ReprintCount,
                LastReprintedAt = x.LastReprintedAt
            })
            .ToListAsync();

        return new SnLabelHistoryPageDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    // Danh sách Work Order khác nhau đã in — nếu có ngày thì chỉ lấy WO của ngày đó
    // (dùng để đổ vào dropdown filter Work Order trên trang History).
    public async Task<List<string>> GetWorkOrdersAsync(DateTime? date)
    {
        var query = _context.SnLabelPrints
            .AsNoTracking()
            .Where(x => x.WorkOrder != null && x.WorkOrder != "");

        if (date.HasValue)
        {
            DateTime prodDate = date.Value.Date;
            query = query.Where(x => x.ProductionDate == prodDate);
        }

        return await query
            .Select(x => x.WorkOrder!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
    }

    public async Task<List<SnLabelPrint>> GetByBatchAsync(Guid batchId)
    {
        return await _context.SnLabelPrints
            .AsNoTracking()
            .Where(x => x.BatchId == batchId)
            .OrderBy(x => x.RunningNumberInt)
            .ToListAsync();
    }

    // ── Reprint by Serial (Manual mode) — re-emit ZPL for an already-printed serial ──
    // Đánh dấu reprint ngay trên dòng gốc (không tạo dòng mới, không đụng RunningNumber) —
    // để trang History biết serial nào đã bị in lại, in lại mấy lần, lần gần nhất khi nào.
    public async Task<SnLabelPrint?> MarkReprintedAsync(string serialNumber, string? reprintedBy)
    {
        var row = await _context.SnLabelPrints.FirstOrDefaultAsync(x => x.SerialNumber == serialNumber.Trim());
        if (row == null) return null;

        row.ReprintCount += 1;
        row.LastReprintedAt = VietnamNow();
        row.LastReprintedBy = reprintedBy;
        await _context.SaveChangesAsync();
        return row;
    }

    // ── ZPL templates (stored in DB — SM_Sakura_ZplTemplate) ─────────────────

    // Trả về nội dung template đang active cho 1 key (vd "SnLabel").
    // Nếu chưa có trong DB (chưa seed), trả về fallback từ ZplTemplates để không vỡ luồng in.
    public async Task<string> GetZplTemplateAsync(string templateKey)
    {
        var row = await _context.SakuraZplTemplates
            .AsNoTracking()
            .Where(x => x.TemplateKey == templateKey && x.IsActive)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync();

        if (row != null) return row.ZplContent;

        return templateKey == "SnLabel" ? ZplTemplates.DefaultSnLabel : "";
    }

    // Thêm mới hoặc cập nhật template theo key — dùng để sửa ZPL trực tiếp trong DB
    // (qua API) thay vì phải sửa code.
    public async Task<SakuraZplTemplate> UpsertZplTemplateAsync(string templateKey, string zplContent, string? updatedBy, string? name = null)
    {
        var row = await _context.SakuraZplTemplates
            .FirstOrDefaultAsync(x => x.TemplateKey == templateKey);

        var now = VietnamNow();
        if (row == null)
        {
            row = new SakuraZplTemplate
            {
                TemplateKey = templateKey,
                Name = name ?? templateKey,
                IsActive = true
            };
            _context.SakuraZplTemplates.Add(row);
        }

        row.ZplContent = zplContent;
        row.UpdatedAt = now;
        row.UpdatedBy = updatedBy;
        if (name != null) row.Name = name;

        await _context.SaveChangesAsync();
        return row;
    }

    public static string BuildConcatenatedZpl(string templateContent, IEnumerable<string> serials) =>
        string.Concat(serials.Select(s => templateContent.Replace("{serialNumber}", s)));

    // ── Direct TCP send (raw port 9100) ──────────────────────────────────────

    public async Task SendZplAsync(string host, int port, string zpl)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port);
        using var stream = client.GetStream();
        byte[] bytes = Encoding.UTF8.GetBytes(zpl);
        await stream.WriteAsync(bytes, 0, bytes.Length);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateTime VietnamNow()
    {
        var tzId = System.Runtime.InteropServices.RuntimeInformation
            .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? "SE Asia Standard Time" : "Asia/Bangkok";
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(tzId));
    }
}
