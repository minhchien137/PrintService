using System.Data;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
    private readonly IHttpClientFactory _httpClientFactory;

    // Model hiện tại luôn là RM15A — để hằng số cho dễ đổi sau này.
    public const string Model = "RM15A";

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

    public SakuraService(AppDbContext context, IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
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
        VariantColorMap.TryGetValue(variant, out var color) ? color : throw new ArgumentException($"Variant không hợp lệ: {variant}");

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
        DateTime date, string variant, string line, int quantity, string? printedBy, string? workOrder = null)
    {
        if (quantity < 1 || quantity > 500)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Số lượng phải từ 1 đến 500.");
        if (line != "0" && line != "1")
            throw new ArgumentException("Line không hợp lệ (chỉ 0 hoặc 1).", nameof(line));

        string color = ResolveColor(variant); // throws if invalid
        DateTime prodDate = date.Date;

        const int maxRetries = 5;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                int lastRunning = await _context.SnLabelPrints
                    .Where(x => x.ProductionDate == prodDate && x.ProductionLine == line && x.Variant == variant)
                    .Select(x => (int?)x.RunningNumberInt)
                    .MaxAsync() ?? -1;

                int startRunning = lastRunning + 1;
                if (startRunning + quantity - 1 > MaxRunningNumberInt)
                {
                    int remaining = Math.Max(0, MaxRunningNumberInt - startRunning + 1);
                    throw new InvalidOperationException(
                        $"Không thể sinh serial: số thứ tự sẽ vượt quá ZZZ ({MaxRunningNumberInt}). " +
                        $"Chỉ còn {remaining} serial khả dụng cho {color} / Line {line} / {prodDate:yyyy-MM-dd}.");
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

        throw new InvalidOperationException("Không thể sinh serial do tranh chấp đồng thời quá nhiều lần, vui lòng thử lại.");
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);

    // ── Work Order lookup (in mode "In qua Work Order") ─────────────────────
    //
    // GỌI TẠM THỜI — chưa có API thật. Giả định hiện tại:
    //   GET {apiUrl}?workOrder={workOrder}
    //   → 200 OK { "color": "Blue", "quantity": 20 }   (color: tên màu hoặc mã variant "00"/"01"/"02", không phân biệt hoa/thường)
    // Khi có API thật, chỉ cần sửa lại phần đọc "color"/"quantity" bên dưới cho khớp field thật.
    public async Task<WorkOrderLookupResponse> LookupWorkOrderAsync(string workOrder, string apiUrl)
    {
        if (string.IsNullOrWhiteSpace(workOrder))
            throw new ArgumentException("Work Order không được để trống.", nameof(workOrder));
        if (string.IsNullOrWhiteSpace(apiUrl))
            throw new InvalidOperationException("Chưa cấu hình địa chỉ API Work Order (Sakura:SnLabel:WorkOrderApiUrl).");

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(8);

        string url = apiUrl + (apiUrl.Contains('?') ? "&" : "?") + "workOrder=" + Uri.EscapeDataString(workOrder);

        using var res = await client.GetAsync(url);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"API Work Order trả về lỗi ({(int)res.StatusCode}).");

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        string? colorRaw = TryGetStringCaseInsensitive(root, "color");
        int quantity = TryGetIntCaseInsensitive(root, "quantity") ?? 0;

        if (string.IsNullOrWhiteSpace(colorRaw))
            throw new InvalidOperationException("API Work Order không trả về màu (color).");
        if (quantity < 1)
            throw new InvalidOperationException("API Work Order không trả về số lượng (quantity) hợp lệ.");

        string? variant = TryResolveVariantFromColor(colorRaw);
        if (variant == null)
            throw new InvalidOperationException($"Không nhận diện được màu '{colorRaw}' trả về từ API Work Order.");

        return new WorkOrderLookupResponse
        {
            WorkOrder = workOrder,
            Variant = variant,
            Color = ResolveColor(variant),
            Quantity = quantity
        };
    }

    private static string? TryGetStringCaseInsensitive(JsonElement obj, string name)
    {
        foreach (var prop in obj.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
        return null;
    }

    private static int? TryGetIntCaseInsensitive(JsonElement obj, string name)
    {
        foreach (var prop in obj.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int v) ? v
                    : int.TryParse(prop.Value.ToString(), out int v2) ? v2 : null;
        return null;
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

    public async Task<SnLabelHistoryPageDto> GetHistoryAsync(DateTime date, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        DateTime prodDate = date.Date;

        var query = _context.SnLabelPrints
            .AsNoTracking()
            .Where(x => x.ProductionDate == prodDate);

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
                WorkOrder = x.WorkOrder
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

    public async Task<List<SnLabelPrint>> GetByBatchAsync(Guid batchId)
    {
        return await _context.SnLabelPrints
            .AsNoTracking()
            .Where(x => x.BatchId == batchId)
            .OrderBy(x => x.RunningNumberInt)
            .ToListAsync();
    }

    // ── Reprint by Serial (Manual mode) — re-emit ZPL for an already-printed serial ──
    public async Task<SnLabelPrint?> FindBySerialAsync(string serialNumber)
    {
        return await _context.SnLabelPrints
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SerialNumber == serialNumber.Trim());
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
