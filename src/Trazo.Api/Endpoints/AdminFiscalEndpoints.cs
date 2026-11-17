using System.Security.Claims;
using Trazo.Infrastructure.Fiscal;

namespace Trazo.Api.Endpoints;

public sealed record CloseMonthRequest(bool AcknowledgeWarnings);
public sealed record ReopenMonthRequest(string? Reason);

/// <summary>
/// Tax books of the owner for the administration console: ledgers, monthly closing and the package for the accountant.
/// Superadmin only: administrators and other roles are refused.
/// </summary>
public static class AdminFiscalEndpoints
{
    public static IEndpointRouteBuilder MapAdminFiscalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/v1/admin/fiscal").WithTags("Admin fiscal")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy).RequireRateLimiting(SetupEndpoints.SetupPolicy);

        g.MapGet("/profile", (IFiscalService s, CancellationToken ct) => Run(async () => Results.Ok(await s.GetProfileAsync(ct))));
        g.MapPut("/profile", (FiscalProfile body, IFiscalService s, CancellationToken ct) => Run(async () => Results.Ok(await s.SaveProfileAsync(body, ct))));
        g.MapGet("/options", () => Results.Ok(new
        {
            incomeCategories = FiscalService.IncomeCategories, expenseCategories = FiscalService.ExpenseCategories,
            vatTreatments = Trazo.Domain.Fiscal.VatTreatments.All
        }));

        g.MapGet("/years/{year:int}", (int year, IFiscalService s, CancellationToken ct) => Run(async () => Results.Ok(await s.GetYearAsync(year, ct))));
        g.MapGet("/months/{period}", (string period, IFiscalService s, CancellationToken ct) => Run(async () => Results.Ok(await s.GetMonthAsync(period, ct))));
        g.MapPost("/months/{period}/book-google-estimate", (string period, IFiscalService s, ClaimsPrincipal user, CancellationToken ct) =>
            Run(async () => Results.Ok(await s.BookGoogleEstimateAsync(period, OperationsSettingsEndpoints.Actor(user), ct))));
        g.MapPost("/months/{period}/close", (string period, CloseMonthRequest? body, IFiscalService s, ClaimsPrincipal user, CancellationToken ct) =>
            Run(async () => Results.Ok(await s.CloseMonthAsync(period, body?.AcknowledgeWarnings ?? false, OperationsSettingsEndpoints.Actor(user), ct))));
        g.MapPost("/months/{period}/reopen", (string period, ReopenMonthRequest? body, IFiscalService s, ClaimsPrincipal user, CancellationToken ct) =>
            Run(async () => { await s.ReopenMonthAsync(period, body?.Reason, OperationsSettingsEndpoints.Actor(user), ct); return Results.NoContent(); }));
        g.MapGet("/months/{period}/package", (string period, IFiscalService s, CancellationToken ct) =>
            Run(async () => { var p = await s.GetMonthPackageAsync(period, ct); return Results.File(p.Content, "application/zip", p.FileName); }));

        g.MapGet("/quarters/{year:int}/{quarter:int}", (int year, int quarter, IFiscalService s, CancellationToken ct) =>
            Run(async () => Results.Ok(await s.GetQuarterAsync(year, quarter, ct))));
        g.MapGet("/quarters/{year:int}/{quarter:int}/package", (int year, int quarter, IFiscalService s, CancellationToken ct) =>
            Run(async () => { var p = await s.GetQuarterPackageAsync(year, quarter, ct); return Results.File(p.Content, "application/zip", p.FileName); }));

        g.MapPost("/entries", (FiscalEntryInput body, IFiscalService s, ClaimsPrincipal user, CancellationToken ct) =>
            Run(async () => Results.Ok(await s.CreateEntryAsync(body, OperationsSettingsEndpoints.Actor(user), ct))));
        g.MapPut("/entries/{id:guid}", (Guid id, FiscalEntryInput body, IFiscalService s, CancellationToken ct) =>
            Run(async () => Results.Ok(await s.UpdateEntryAsync(id, body, ct))));
        g.MapDelete("/entries/{id:guid}", (Guid id, IFiscalService s, CancellationToken ct) =>
            Run(async () => { await s.DeleteEntryAsync(id, ct); return Results.NoContent(); }));

        g.MapGet("/audit", (IFiscalService s, CancellationToken ct) => Run(async () => Results.Ok(new { items = await s.GetAuditAsync(ct) })));
        return endpoints;
    }

    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (FiscalException e)
        {
            if (e.Errors is not null) return Results.ValidationProblem(e.Errors, title: e.Message, statusCode: e.Status);
            return Results.Json(new { error = e.Code, message = e.Message, detail = e.Detail }, statusCode: e.Status);
        }
    }
}
