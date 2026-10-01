using System.Text;

namespace U1.Business.Services;

internal static class ProductCsvFormat
{
    public const int MaxBytes = 1048576;
    public const int MaxRows = 1000;

    public static readonly string[] Headers =
    [
        "code", "name", "description", "brand", "manufacturerCode", "specialCode1",
        "specialCode2", "imageUrl", "stock", "criticalStock", "price", "category",
        "rowVersion", "stockReason"
    ];

    public static string ExportCell(string? value)
    {
        value ??= "";
        var firstVisible = value.TrimStart();
        if (firstVisible.Length > 0 && "=+-@".Contains(firstVisible[0]))
            value = "'" + value;

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    public static string ImportCell(string value)
    {
        if (value.Length <= 1 || value[0] != '\'')
            return value;
        var content = value[1..].TrimStart();
        return content.Length > 0 && "=+-@".Contains(content[0])
            ? value[1..]
            : value;
    }

    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var afterQuote = false;

        void FinishField()
        {
            row.Add(field.ToString());
            field.Clear();
            afterQuote = false;
        }

        void FinishRow()
        {
            FinishField();
            if (row.Count != 1 || row[0].Length != 0)
                rows.Add(row.ToArray());
            row.Clear();
        }

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character != '"')
                {
                    field.Append(character);
                    continue;
                }

                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                    afterQuote = true;
                }
                continue;
            }

            if (character == ',')
            {
                FinishField();
                continue;
            }
            if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                FinishRow();
                continue;
            }
            if (afterQuote)
                throw new FormatException("Kapanmış çift tırnak sonrasında ayraç bekleniyor.");
            if (character == '"')
            {
                if (field.Length != 0)
                    throw new FormatException("Çift tırnak alanın yalnız başında kullanılabilir.");
                quoted = true;
                continue;
            }
            field.Append(character);
        }

        if (quoted)
            throw new FormatException("Kapanmamış çift tırnak bulundu.");
        FinishRow();

        if (rows.Count > 0 && rows[0].Length > 0)
            rows[0][0] = rows[0][0].TrimStart('\uFEFF');
        return rows;
    }
}
