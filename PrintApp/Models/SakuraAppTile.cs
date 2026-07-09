namespace PrintApp.Models;

// Một ô chức năng trên trang Sakura Home (Views/Sakura/Index.cshtml).
// Thêm chức năng Sakura mới sau này: chỉ cần thêm 1 SakuraAppTile trong SakuraController.Index().
public class SakuraAppTile
{
    // Dùng làm data-i18n key: "tile.{Key}.title" / "tile.{Key}.desc" (xem wwwroot/js/sakura-i18n.js)
    public string Key { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string? Href { get; set; }
    public bool Enabled { get; set; } = true;
}
