using System.Globalization;
using System.IO.Compression;
using System.Text;
using Trazo.Domain.Fiscal;

namespace Trazo.Infrastructure.Fiscal;

/// <summary>
/// Builds the ZIP the accountant needs: books of income and expenses, a summary and the orientative quarterly figures.
/// CSV files use ';' and decimal commas with a UTF-8 BOM so that Spanish Excel opens them directly.
/// </summary>
public static class FiscalPackageBuilder
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static byte[] Build(string label, FiscalProfile profile, IReadOnlyList<FiscalEntry> entries, IReadOnlyList<FiscalClosure> closures,
        QuarterSummary quarter, DateTimeOffset generatedAt)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "LEEME.txt", Readme(label, profile, closures, quarter, generatedAt));
            Add(zip, $"libro_ingresos_{label}.csv", Csv(IncomeHeader, entries.Where(e => e.Kind == FiscalKinds.Income).OrderBy(e => e.Date).Select(IncomeRow)));
            Add(zip, $"libro_gastos_{label}.csv", Csv(ExpenseHeader, entries.Where(e => e.Kind == FiscalKinds.Expense).OrderBy(e => e.Date).Select(ExpenseRow)));
            Add(zip, $"resumen_{label}.csv", Csv(["Concepto", "Importe"], SummaryRows(FiscalCalculator.Totals(entries))));
            Add(zip, $"resumen_trimestre_{quarter.Label}.csv", Csv(["Modelo", "Concepto", "Importe"], QuarterRows(quarter)));
            if (quarter.Intra349.Count > 0)
                Add(zip, $"modelo349_{quarter.Label}.csv", Csv(["Clave", "Contraparte", "NIF-IVA", "Pais", "Base"],
                    quarter.Intra349.Select(r => new[] { r.Key, r.Counterparty ?? "", r.TaxId ?? "", r.Country ?? "", Money(r.Base) })));
        }
        return stream.ToArray();
    }

    // ------------------------------------------------------------------ books

    private static readonly string[] IncomeHeader =
        ["Fecha", "Periodo", "Concepto", "Cliente", "NIF", "Pais", "Factura", "Categoria", "Base", "Tratamiento IVA", "Tipo IVA %", "Cuota IVA", "Retencion %", "Retencion", "Total cobrado", "Origen", "Notas"];

    private static readonly string[] ExpenseHeader =
        ["Fecha", "Periodo", "Concepto", "Proveedor", "NIF", "Pais", "Factura", "Categoria", "Base", "Tratamiento IVA", "Tipo IVA %", "Cuota IVA", "% deducible", "Total pagado", "Notas"];

    private static string[] IncomeRow(FiscalEntry e) =>
    [
        Date(e.Date), e.Period, e.Concept, e.Counterparty ?? "", e.CounterpartyTaxId ?? "", e.CounterpartyCountry ?? "", e.InvoiceNumber ?? "",
        e.Category, Money(e.Base), VatLabel(e.VatTreatment), Money(e.VatRatePct), Money(e.VatAmount), Money(e.WithholdingPct), Money(e.WithholdingAmount),
        Money(e.Total), e.Source, e.Notes ?? ""
    ];

    private static string[] ExpenseRow(FiscalEntry e) =>
    [
        Date(e.Date), e.Period, e.Concept, e.Counterparty ?? "", e.CounterpartyTaxId ?? "", e.CounterpartyCountry ?? "", e.InvoiceNumber ?? "",
        e.Category, Money(e.Base), VatLabel(e.VatTreatment), Money(e.VatRatePct), Money(e.VatAmount), Money(e.DeductiblePct), Money(e.Total), e.Notes ?? ""
    ];

    private static string VatLabel(string treatment) => treatment switch
    {
        VatTreatments.Domestic => "IVA nacional",
        VatTreatments.EuReverseCharge => "Inversion sujeto pasivo UE",
        _ => "Sin IVA / exento"
    };

    private static IEnumerable<string[]> SummaryRows(FiscalTotals t) =>
    [
        ["Ingresos (base)", Money(t.IncomeBase)],
        ["  de los cuales con IVA nacional", Money(t.IncomeDomesticBase)],
        ["  de los cuales a cliente UE (inversion sujeto pasivo)", Money(t.IncomeEuB2bBase)],
        ["  de los cuales sin IVA", Money(t.IncomeNoVatBase)],
        ["IVA repercutido (ingresos nacionales)", Money(t.VatOnIncome)],
        ["IVA autorrepercutido (gastos con inversion sujeto pasivo)", Money(t.SelfAssessedVat)],
        ["Gastos (base)", Money(t.ExpenseBase)],
        ["Gasto deducible para IRPF", Money(t.DeductibleCost)],
        ["IVA soportado deducible", Money(t.VatInput)],
        ["Saldo de IVA (repercutido - soportado)", Money(t.VatBalance)],
        ["Retenciones de IRPF soportadas", Money(t.WithholdingsSuffered)],
        ["Resultado (ingresos - gasto deducible)", Money(t.NetResult)]
    ];

    private static IEnumerable<string[]> QuarterRows(QuarterSummary q) =>
    [
        ["Vencimiento", "Presentacion trimestral", Date(q.DueDate)],
        ["303", "Base imponible ingresos con IVA nacional", Money(q.Iva.DomesticIncomeBase)],
        ["303", "IVA repercutido", Money(q.Iva.VatOnIncome)],
        ["303", "Base adquisiciones con inversion sujeto pasivo", Money(q.Iva.SelfAssessedBase)],
        ["303", "IVA autorrepercutido", Money(q.Iva.SelfAssessedVat)],
        ["303", "Total IVA devengado", Money(q.Iva.VatOutput)],
        ["303", "Base de gastos deducibles", Money(q.Iva.DeductibleBase)],
        ["303", "IVA deducible", Money(q.Iva.VatInput)],
        ["303", "Resultado (a ingresar si es positivo)", Money(q.Iva.Balance)],
        ["303", "Operaciones no sujetas por reglas de localizacion (clientes UE)", Money(q.Iva.NotSubjectEuB2bBase)],
        ["130", "Aplica", q.Irpf.Applies ? "Si" : "No"],
        ["130", "Ingresos acumulados del ano", Money(q.Irpf.YtdIncome)],
        ["130", "Gastos deducibles acumulados", Money(q.Irpf.YtdDeductibleCost)],
        ["130", "Rendimiento neto acumulado", Money(q.Irpf.NetYtd)],
        ["130", $"Pago fraccionado ({Money(q.Irpf.Pct)} %)", Money(q.Irpf.Gross)],
        ["130", "Retenciones soportadas acumuladas", Money(q.Irpf.WithholdingsYtd)],
        ["130", "Pagos fraccionados de trimestres anteriores", Money(q.Irpf.PreviousPayments)],
        ["130", "Resultado a ingresar", Money(q.Irpf.ToPay)]
    ];

    private static string Readme(string label, FiscalProfile p, IReadOnlyList<FiscalClosure> closures, QuarterSummary q, DateTimeOffset at)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TRAZO - paquete para el gestor ({label})");
        sb.AppendLine($"Titular: {p.LegalName}  NIF: {p.TaxId}");
        if (!string.IsNullOrWhiteSpace(p.Address)) sb.AppendLine($"Domicilio: {p.Address}");
        sb.AppendLine($"Generado: {at:yyyy-MM-dd HH:mm} UTC");
        foreach (var c in closures.OrderBy(c => c.Period))
            sb.AppendLine($"Mes {c.Period} cerrado el {c.ClosedAt:yyyy-MM-dd} por {c.ClosedBy} ({c.EntryCount} apuntes)");
        sb.AppendLine();
        sb.AppendLine("Contenido:");
        sb.AppendLine("  libro_ingresos_*.csv  Libro de ingresos con base, IVA, retencion y total.");
        sb.AppendLine("  libro_gastos_*.csv    Libro de gastos con base, IVA y porcentaje deducible.");
        sb.AppendLine("  resumen_*.csv         Totales del periodo.");
        sb.AppendLine($"  resumen_trimestre_{q.Label}.csv  Cifras orientativas para los modelos 303 y 130.");
        sb.AppendLine("  modelo349_*.csv      Operaciones intracomunitarias (si las hay).");
        sb.AppendLine();
        sb.AppendLine("Formato: CSV con punto y coma, coma decimal y codificacion UTF-8; se abre directamente en Excel.");
        sb.AppendLine("Los ingresos de Google Play se registran contra Google Ireland Ltd. como servicio B2B a cliente de la UE");
        sb.AppendLine("(sin IVA, inversion del sujeto pasivo, modelo 349). Las cifras de los modelos son orientativas:");
        sb.AppendLine("el gestor debe revisarlas antes de presentar ninguna declaracion.");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ format

    private static string Money(decimal value) => value.ToString("0.00", Es);
    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(true));
        writer.Write(content);
    }

    private static string Csv(string[] header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(';', header.Select(Cell))).Append("\r\n");
        foreach (var row in rows) sb.Append(string.Join(';', row.Select(Cell))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Quotes a value and neutralises spreadsheet formulas typed by hand into a concept or name.</summary>
    public static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '@' or '\t' or '\r' || (value.Length > 1 && value[0] == '-' && !char.IsDigit(value[1]) && value[1] != ','))
            value = "'" + value;
        return value.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
