using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintApp.Data;
using PrintApp.Models;
using PrintApp.Services;

namespace PrintApp.Controllers;

// Controller chung cho các chức năng thuộc dự án Sakura.
// SN Label Print là chức năng đầu tiên — các chức năng Sakura khác sẽ được thêm vào đây.
public class SakuraController : Controller
{
    private readonly SakuraService _snLabel;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly ViidooService _viidoo;

    public SakuraController(SakuraService snLabel, IConfiguration config, AppDbContext db, ViidooService viidoo)
    {
        _snLabel = snLabel;
        _config = config;
        _db = db;
        _viidoo = viidoo;
    }

    // Gói 1 exception thành JSON lỗi có errorCode/errorParams (nếu có) để front-end tự
    // dịch theo EN/ZH đang chọn (xem sakura-i18n.js -> translateApiError). Exception
    // không có mã (vd lỗi .NET/network chung chung) vẫn được dịch qua mã dùng chung
    // "common.unexpectedError", kèm message gốc làm tham số.
    private static object BuildError(Exception ex)
    {
        if (ex is ISakuraCodedException coded)
            return new { ok = false, error = ex.Message, errorCode = coded.Code, errorParams = coded.Params };
        return new { ok = false, error = ex.Message, errorCode = "common.unexpectedError", errorParams = new { message = ex.Message } };
    }

    // ── View ──────────────────────────────────────────────────────────────────

    // Trang chủ Sakura — tổng hợp các chức năng dưới dạng ô chọn (app tile).
    // Thêm chức năng mới: thêm 1 SakuraAppTile vào danh sách bên dưới.
    [HttpGet("/sakura")]
    public IActionResult Index()
    {
        // Title/Subtitle o day la fallback tieng Anh hien khi JS chua chay kip;
        // ban dich day du (EN/ZH) nam trong wwwroot/js/sakura-i18n.js, khoa theo Key.
        var tiles = new List<SakuraAppTile>
        {
            new SakuraAppTile
            {
                Key = "snlabelgroup",
                Icon = "🏷️",
                Title = "SN Label",
                Subtitle = "Serial number label printing",
                Enabled = true,
                Items = new List<SakuraAppTile>
                {
                    new SakuraAppTile
                    {
                        Key = "snlabel",
                        Icon = "🖨️",
                        Title = "Print",
                        Subtitle = "Print serial number labels",
                        Href = Url.Content("~/sakura/snlabel"),
                        Enabled = true
                    },
                    new SakuraAppTile
                    {
                        Key = "history",
                        Icon = "🕘",
                        Title = "History",
                        Subtitle = "SN Label print history",
                        Href = Url.Content("~/sakura/snlabel/history"),
                        Enabled = true
                    }
                }
            },
            new SakuraAppTile
            {
                Key = "laserstation",
                Icon = "🔦",
                Title = "Laser Station",
                Subtitle = "Back Panel laser marking check",
                Enabled = true,
                Items = new List<SakuraAppTile>
                {
                    new SakuraAppTile
                    {
                        Key = "laser",
                        Icon = "🔦",
                        Title = "Laser",
                        Subtitle = "Scan Work Order & Serial Number",
                        Href = Url.Content("~/backpanel/laser"),
                        Enabled = true
                    },
                    new SakuraAppTile
                    {
                        Key = "logLaser",
                        Icon = "📋",
                        Title = "Log Laser",
                        Subtitle = "Laser scan history",
                        Href = Url.Content("~/backpanel/laser/history"),
                        Enabled = true
                    }
                }
            },
            new SakuraAppTile
            {
                Key = "comingsoon",
                Icon = "➕",
                Title = "Coming soon",
                Subtitle = "Next Sakura feature",
                Href = null,
                Enabled = false
            }
        };
        return View("~/Views/Sakura/Index.cshtml", tiles);
    }

    [HttpGet("/sakura/snlabel")]
    public IActionResult SnLabelIndex()
    {
        return View("~/Views/Sakura/SnLabel.cshtml");
    }

    [HttpGet("/sakura/snlabel/history")]
    public IActionResult SnLabelHistory()
    {
        return View("~/Views/Sakura/History.cshtml");
    }

    // ── API: printer list (for other Sakura pages that need it) ────────────────

    [HttpGet("/api/sakura/printers")]
    public async Task<IActionResult> GetPrinters()
    {
        var list = await _db.PrinterInfos
            .Where(p => p.target == "Sakura")
            .Select(p => new { p.ID_Printer, p.Name_Printer, p.IP_Printer, p.Port_Printer })
            .ToListAsync();
        return Ok(list);
    }

    // ── API: status ───────────────────────────────────────────────────────────

    [HttpGet("/api/sakura/snlabel/status")]
    public async Task<IActionResult> Status([FromQuery] DateTime date, [FromQuery] string variant, [FromQuery] string line)
    {
        try
        {
            var result = await _snLabel.GetStatusAsync(date, variant, line);
            return Ok(new { ok = true, data = result });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(BuildError(ex));
        }
    }

    // ── API: Work Order lookup (mode "In qua Work Order") ──────────────────────

    [HttpGet("/api/sakura/snlabel/workorder-lookup")]
    public async Task<IActionResult> WorkOrderLookup([FromQuery] string workOrder)
    {
        if (string.IsNullOrWhiteSpace(workOrder))
            return BadRequest(new { ok = false, error = "Thiếu Work Order.", errorCode = "workOrder.missing" });

        string wo = workOrder.Trim();
        try
        {
            var result = await _viidoo.SearchAsync(wo);
            if (result == null)
                return BadRequest(new { ok = false, error = $"Không tìm thấy Work Order '{wo}' trên Odoo.", errorCode = "workOrder.notFoundOdoo", errorParams = new { wo } });

            if (string.IsNullOrWhiteSpace(result.Color))
                return BadRequest(new { ok = false, error = $"Không xác định được màu cho Work Order '{wo}'.", errorCode = "workOrder.colorUnknown", errorParams = new { wo } });

            if (result.Quantity is not decimal qty || qty < 1)
                return BadRequest(new { ok = false, error = $"Work Order '{wo}' không có số lượng hợp lệ.", errorCode = "workOrder.invalidQuantity", errorParams = new { wo } });

            string? variant = SakuraService.TryResolveVariantFromColor(result.Color);
            if (variant == null)
                return BadRequest(new { ok = false, error = $"Không nhận diện được màu '{result.Color}' trả về từ Odoo.", errorCode = "workOrder.unresolvedColor", errorParams = new { color = result.Color } });

            int totalQuantity = (int)qty;
            int printedQuantity = await _snLabel.GetWorkOrderPrintedCountAsync(wo);
            int remainingQuantity = Math.Max(0, totalQuantity - printedQuantity);

            if (remainingQuantity <= 0)
                return BadRequest(new { ok = false, error = $"Work Order '{wo}' đã in đủ số lượng ({printedQuantity}/{totalQuantity}).", errorCode = "workOrder.exhausted", errorParams = new { wo, printed = printedQuantity, total = totalQuantity } });

            // Nếu WO này đã in dở, ngày sản xuất bị khóa theo lần in đầu tiên — chỉ được
            // chọn ngày tự do khi đây là lần in đầu tiên (chưa có dòng nào trong DB).
            DateTime? lockedDate = printedQuantity > 0
                ? await _snLabel.GetWorkOrderProductionDateAsync(wo)
                : null;

            var response = new WorkOrderLookupResponse
            {
                WorkOrder = wo,
                Variant = variant,
                Color = SakuraService.ResolveColor(variant),
                TotalQuantity = totalQuantity,
                PrintedQuantity = printedQuantity,
                RemainingQuantity = remainingQuantity,
                LockedProductionDate = lockedDate
            };
            return Ok(new { ok = true, data = response });
        }
        catch (Exception ex)
        {
            return BadRequest(BuildError(ex));
        }
    }

    // ── API: unlock Manual print mode with a shared password ────────────────────

    [HttpPost("/api/sakura/snlabel/verify-manual-password")]
    public IActionResult VerifyManualPassword([FromBody] ManualUnlockRequest req)
    {
        string expected = _config["Sakura:SnLabel:ManualModePassword"] ?? "";
        if (req == null || string.IsNullOrEmpty(expected) || req.Password != expected)
            return Unauthorized(new { ok = false, error = "Sai mật khẩu.", errorCode = "password.incorrect" });

        return Ok(new { ok = true });
    }

    // ── API: print ────────────────────────────────────────────────────────────

    [HttpPost("/api/sakura/snlabel/print")]
    public async Task<IActionResult> Print([FromBody] SnLabelPrintRequest req)
    {
        if (req == null)
            return BadRequest(new { ok = false, error = "Thiếu dữ liệu.", errorCode = "common.missingData" });

        // Không tin số lượng WO mà client gửi lên — tự tra lại tổng số lượng thật từ
        // Odoo ngay lúc in, để không ai (kể cả gọi thẳng API, bỏ qua UI) có thể né
        // giới hạn "còn lại bao nhiêu" của Work Order.
        int? workOrderTotalQuantity = null;
        if (!string.IsNullOrWhiteSpace(req.WorkOrder))
        {
            try
            {
                var woResult = await _viidoo.SearchAsync(req.WorkOrder.Trim());
                if (woResult == null || woResult.Quantity is not decimal woQty || woQty < 1)
                {
                    string wo = req.WorkOrder.Trim();
                    return BadRequest(new { ok = false, error = $"Không xác định được tổng số lượng của Work Order '{wo}'.", errorCode = "workOrder.totalUnavailable", errorParams = new { wo } });
                }
                workOrderTotalQuantity = (int)woQty;
            }
            catch (Exception ex)
            {
                return BadRequest(BuildError(ex));
            }
        }

        List<SnLabelPrint> rows;
        try
        {
            rows = await _snLabel.GenerateNextSerialsAsync(req.Date, req.Variant, req.Line, req.Quantity, req.PrintedBy, req.WorkOrder, workOrderTotalQuantity);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(BuildError(ex));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(BuildError(ex));
        }

        string template = await _snLabel.GetZplTemplateAsync("SnLabel");
        string zpl = SakuraService.BuildConcatenatedZpl(template, rows.Select(r => r.SerialNumber));
        string printMode = _config["Sakura:SnLabel:PrintMode"] ?? "FileDownload";

        var response = new SnLabelPrintResponse
        {
            Ok = true,
            BatchId = rows[0].BatchId,
            Serials = rows.Select(r => new SnLabelSerialDto
            {
                SerialNumber = r.SerialNumber,
                RunningNumber = r.RunningNumber,
                RunningNumberInt = r.RunningNumberInt
            }).ToList(),
            Zpl = zpl,
            PrintMode = printMode
        };

        if (string.Equals(printMode, "DirectTcp", StringComparison.OrdinalIgnoreCase))
        {
            string printerIp = _config["Sakura:SnLabel:PrinterIp"] ?? "";
            int printerPort = int.TryParse(_config["Sakura:SnLabel:PrinterPort"], out var p) ? p : 9100;

            if (string.IsNullOrWhiteSpace(printerIp))
            {
                response.DirectPrintSent = false;
                response.DirectPrintError = "Chưa cấu hình IP máy in (Sakura:SnLabel:PrinterIp trong appsettings.json).";
            }
            else
            {
                try
                {
                    await _snLabel.SendZplAsync(printerIp, printerPort, zpl);
                    response.DirectPrintSent = true;
                }
                catch (Exception ex)
                {
                    response.DirectPrintSent = false;
                    response.DirectPrintError = ex.Message;
                }
            }
        }

        return Ok(response);
    }

    // ── API: reprint by serial (Manual mode) ────────────────────────────────────

    [HttpGet("/api/sakura/snlabel/reprint")]
    public async Task<IActionResult> Reprint([FromQuery] string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            return BadRequest(new { ok = false, error = "Thiếu Serial Number.", errorCode = "reprint.missingSerial" });

        // Đánh dấu reprint ngay trên dòng gốc (tăng ReprintCount, ghi lại thời điểm) —
        // không tạo dòng mới nên không đụng tới bộ đếm RunningNumber.
        var row = await _snLabel.MarkReprintedAsync(serialNumber, reprintedBy: null);
        if (row == null)
        {
            string sn = serialNumber.Trim();
            return NotFound(new { ok = false, error = $"Không tìm thấy serial '{sn}'.", errorCode = "reprint.notFound", errorParams = new { serial = sn } });
        }

        string template = await _snLabel.GetZplTemplateAsync("SnLabel");
        string zpl = SakuraService.BuildConcatenatedZpl(template, new[] { row.SerialNumber });

        return Ok(new
        {
            ok = true,
            data = new
            {
                row.SerialNumber,
                row.Variant,
                row.Color,
                row.ProductionLine,
                row.ProductionDate,
                row.WorkOrder,
                row.PrintedAt,
                row.ReprintCount,
                zpl
            }
        });
    }

    // ── API: history ──────────────────────────────────────────────────────────

    [HttpGet("/api/sakura/snlabel/history")]
    public async Task<IActionResult> History([FromQuery] DateTime? date, [FromQuery] string? workOrder, [FromQuery] string? serialNumber, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _snLabel.GetHistoryAsync(date, workOrder, serialNumber, page, pageSize);
        return Ok(new { ok = true, data = result });
    }

    // Danh sách Work Order để đổ vào dropdown filter — nếu có ngày thì chỉ lấy WO của
    // đúng ngày đó (vd ngày 9 có 3 WO thì dropdown chỉ hiện đúng 3 WO đó).
    [HttpGet("/api/sakura/snlabel/workorders")]
    public async Task<IActionResult> GetWorkOrders([FromQuery] DateTime? date)
    {
        var list = await _snLabel.GetWorkOrdersAsync(date);
        return Ok(new { ok = true, data = list });
    }

    // ── API: re-download ZPL file for a batch ────────────────────────────────

    [HttpGet("/api/sakura/snlabel/download/{batchId:guid}")]
    public async Task<IActionResult> Download(Guid batchId)
    {
        var rows = await _snLabel.GetByBatchAsync(batchId);
        if (rows.Count == 0)
            return NotFound(new { ok = false, error = "Không tìm thấy batch." });

        string template = await _snLabel.GetZplTemplateAsync("SnLabel");
        string zpl = SakuraService.BuildConcatenatedZpl(template, rows.Select(r => r.SerialNumber));
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(zpl);
        return File(bytes, "text/plain", $"sn-labels-{batchId}.zpl");
    }

    // ── API: ZPL template CRUD (edit template content without touching code) ──

    [HttpGet("/api/sakura/zpl-template/{key}")]
    public async Task<IActionResult> GetZplTemplate(string key)
    {
        string content = await _snLabel.GetZplTemplateAsync(key);
        return Ok(new { ok = true, data = new { templateKey = key, zplContent = content } });
    }

    [HttpPut("/api/sakura/zpl-template/{key}")]
    public async Task<IActionResult> UpdateZplTemplate(string key, [FromBody] SakuraZplTemplateUpdateRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.ZplContent))
            return BadRequest(new { ok = false, error = "Thiếu nội dung ZPL." });

        var row = await _snLabel.UpsertZplTemplateAsync(key, req.ZplContent, req.UpdatedBy);
        return Ok(new { ok = true, data = new { row.TemplateKey, row.UpdatedAt, row.UpdatedBy } });
    }
}
