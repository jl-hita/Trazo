using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Trazo.Api.Support;

/// <summary>
/// The error contract of the API. Every problem answer (RFC 9457 <c>application/problem+json</c>) carries <c>type</c>, <c>title</c>,
/// <c>status</c>, <c>detail</c> (a text that is safe to show), <c>traceId</c> and a stable <c>code</c>. <c>title</c> already was the stable
/// code for the errors that have one (the Android app reads it), so it keeps that role and <c>code</c> repeats it for new clients.
/// Nothing from the exception (message, stack trace, SQL, addresses) ever reaches the answer.
/// </summary>
public static class ErrorResults
{
    public const string ServerError = "server_error";

    /// <summary>The identifier that is given to the client and written to the logs, so the two can be matched.</summary>
    public static string TraceIdOf(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;

    /// <summary>A problem answer with a stable code. For expected errors (nothing to report).</summary>
    public static IResult Problem(int status, string code, string detail) =>
        Results.Problem(detail: detail, statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// For a <c>catch</c> block that turns an exception into an answer. An exception marked as an expected condition
    /// (<see cref="ErrorMarks.AsExpected{T}"/>: the mail server or Firebase did not answer) gets the answer the caller describes and no
    /// ticket. Any other exception is unexpected: it is reported as an error ticket (once) and answered 500 <c>server_error</c>, whatever
    /// the caller expected. The client gets the trace id; the exception text is never sent.
    /// <para>
    /// <paramref name="keepAnswerWhenReported"/>: for a failure that is a real fault but whose answer is known (the database does not
    /// answer: 503). It is reported as a ticket and still gets the answer the caller describes instead of 500.
    /// </para>
    /// </summary>
    public static IResult Failure(Exception exception, int status, string code, string detail, bool keepAnswerWhenReported = false) =>
        new FailureResult(exception, status, code, detail, keepAnswerWhenReported);

    /// <summary>Completes any problem body with the trace id and, when it has none, a code derived from the status. Called for all of them.</summary>
    public static void Describe(ProblemDetails problem, HttpContext context)
    {
        problem.Extensions.TryAdd("traceId", TraceIdOf(context));
        if (problem.Extensions.ContainsKey("code")) return;
        var status = problem.Status ?? context.Response.StatusCode;
        // A title without spaces is already a stable code (plan errors, server_error); sentences ("One or more validation errors...") are not.
        var code = problem.Title is { Length: > 0 } title && !title.Contains(' ') ? title : DefaultCode(status);
        problem.Extensions["code"] = code;
    }

    private static string DefaultCode(int status) => status switch
    {
        400 or 422 => "validation_error",
        401 => "unauthorized",
        402 => "subscription_required",
        403 => "forbidden",
        404 => "not_found",
        409 => "conflict",
        413 => "payload_too_large",
        429 => "rate_limited",
        503 => "service_unavailable",
        >= 500 => ServerError,
        _ => "error"
    };

    private sealed class FailureResult(Exception exception, int requestedStatus, string requestedCode, string requestedDetail, bool keepAnswer) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var traceId = TraceIdOf(httpContext);
            var expected = ErrorMarks.IsExpected(exception);
            var (status, code, detail) = (requestedStatus, requestedCode, requestedDetail);
            if (!expected)
            {
                if (!keepAnswer) (status, code, detail) = (StatusCodes.Status500InternalServerError, ServerError, "Error inesperado.");
                await httpContext.RequestServices.GetRequiredService<IErrorReporter>().ReportAsync(exception, traceId, CancellationToken.None);
            }
            // The exception type only: the text of a mail or network error can quote an address.
            httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(ErrorTicketLoggerProvider.OwnCategory).LogWarning(
                "{Code} ({ExceptionType}) while serving {Endpoint}, trace {TraceId}; {Ticket}.", code, exception.GetType().FullName,
                httpContext.GetEndpoint()?.DisplayName ?? "(no endpoint)", traceId, expected ? "expected condition, no ticket" : "reported as an error ticket");
            await Problem(status, code, detail).ExecuteAsync(httpContext);
        }
    }
}
