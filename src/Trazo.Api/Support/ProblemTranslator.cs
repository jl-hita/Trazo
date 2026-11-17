using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Trazo.Application.Localization;

namespace Trazo.Api.Support;

/// <summary>
/// Error answers are written in the language of the code (mostly Spanish, some English) where they are created and put in the language
/// of the request on the way out: <c>ProblemDetails</c> (every <c>Results.Problem</c> and <c>Results.ValidationProblem</c> goes through
/// <c>AddProblemDetails</c>) with <see cref="Translate"/>, and the <c>{ error, message }</c> bodies with <see cref="ApiErrors"/>.
/// </summary>
public static class ProblemTranslator
{
    public static void Translate(ProblemDetails details, AppLanguage language)
    {
        details.Detail = Localizer.Translate(details.Detail, language);
        if (details is HttpValidationProblemDetails validation)
            foreach (var key in validation.Errors.Keys.ToList())
                validation.Errors[key] = Localizer.Translate(validation.Errors[key], language);
        if (details.Extensions.TryGetValue("errors", out var errors) && errors is IDictionary<string, string[]> map)
            foreach (var key in map.Keys.ToList())
                map[key] = Localizer.Translate(map[key], language);
    }
}

/// <summary>
/// JSON error bodies (<c>{ code, error }</c>, <c>{ error, message }</c>) whose text is translated into the language of the request when
/// the answer is written. Same shape and status as <c>Results.BadRequest/Conflict/Json</c>, which they replace for error answers.
/// </summary>
public static class ApiErrors
{
    public static IResult Json(object body, int statusCode) => new LocalizedJsonResult(body, statusCode);
    public static IResult BadRequest(object body) => Json(body, StatusCodes.Status400BadRequest);
    public static IResult Conflict(object body) => Json(body, StatusCodes.Status409Conflict);

    private sealed class LocalizedJsonResult(object body, int statusCode) : IResult
    {
        private static readonly string[] TextFields = ["error", "message", "detail"];

        public Task ExecuteAsync(HttpContext httpContext)
        {
            var language = httpContext.RequestServices.GetRequiredService<RequestLanguage>().Current;
            var options = httpContext.RequestServices.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
            var node = JsonSerializer.SerializeToNode(body, body.GetType(), options) as JsonObject;
            if (node is not null)
                foreach (var field in TextFields)
                    if (node[field] is JsonValue value && value.TryGetValue<string>(out var text))
                        node[field] = Localizer.Translate(text, language);
            httpContext.Response.StatusCode = statusCode;
            return httpContext.Response.WriteAsJsonAsync(node ?? (object)body, options);
        }
    }
}
