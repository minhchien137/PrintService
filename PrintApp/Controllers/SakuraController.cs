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

    public SakuraController(SakuraService snLabel, IConfiguration config, AppDbContext db)
    {
        _snLabel = snLabel;
        _config = config;
        _db = db;
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
                Key = "snlabel",
                Icon = "🏷️",
                Title = "SN Label Print",
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
    public async Task<IActionResult> SnLabelIndex()
    {
        // Máy in có target = "Sakura" trong SVN_Printer_Info_New để bind vào dropdown (giống trang Toast).
        var printers = await _db.PrinterInfos
            .Where(p => p.target == "Sakura")
            .ToListAsync();
        return View("~/Views/Sakura/SnLabel.cshtml", printers);
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
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    // ── API: print ────────────────────────────────────────────────────────────

    [HttpPost("/api/sakura/snlabel/print")]
    public async Task<IActionResult> Print([FromBody] SnLabelPrintRequest req)
    {
        if (req == null)
            return BadRequest(new { ok = false, error = "Thiếu dữ liệu." });

        List<SnLabelPrint> rows;
        try
        {
            rows = await _snLabel.GenerateNextSerialsAsync(req.Date, req.Variant, req.Line, req.Quantity, req.PrintedBy);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ok = false, error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { ok = false, error = ex.Message });
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

    // ── API: history ──────────────────────────────────────────────────────────

    [HttpGet("/api/sakura/snlabel/history")]
    public async Task<IActionResult> History([FromQuery] DateTime date)
    {
        var list = await _snLabel.GetHistoryAsync(date);
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
