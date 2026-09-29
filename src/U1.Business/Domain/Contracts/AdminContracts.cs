using System.ComponentModel.DataAnnotations;

namespace U1.Business.Domain;

public sealed class GridColumnUpdateInput
{
    public int Id { get; set; }
    public string Field { get; set; } = "";

    [Required, StringLength(60)]
    public string Label { get; set; } = "";

    public string RenderType { get; set; } = "text";

    [Range(0, 100)]
    public int Position { get; set; }

    [Range(60, 600)]
    public int Width { get; set; }

    public string Align { get; set; } = "left";
    public bool Desktop { get; set; } = true;
    public bool Tablet { get; set; } = true;
    public bool Mobile { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public class BannerInput
{
    [Required, StringLength(100)]
    public string Title { get; set; } = "";

    [Required, StringLength(300)]
    public string Subtitle { get; set; } = "";

    [Required, StringLength(40)]
    public string ButtonText { get; set; } = "Ürünleri incele";

    [StringLength(100)]
    public string SearchTerm { get; set; } = "";

    public bool IsActive { get; set; } = true;

    [Range(0, 100)]
    public int Position { get; set; }
}

public sealed class BannerUpdateInput : BannerInput
{
    public byte[] RowVersion { get; set; } = [];
}
