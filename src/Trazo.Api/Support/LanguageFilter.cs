using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Trazo.Application.Localization;

namespace Trazo.Api.Support;

/// <summary>
/// Answers in the language of the request. The language comes from <c>Accept-Language</c> (Spanish by default; English when it is the
/// first supported language the client asks for). It is set before the endpoint runs, so the services build their texts in it, and the
/// error responses (<c>ProblemDetails</c>, which are written in Spanish where they are created) are translated on the way out.
/// </summary>
public static class LanguageFilter
{
    public static TBuilder AddLanguage<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var language = LanguageNegotiation.FromAcceptLanguage(context.HttpContext.Request.Headers.AcceptLanguage.ToString());
            context.HttpContext.RequestServices.GetRequiredService<RequestLanguage>().Current = language;
            var result = await next(context);
            if (language == AppLanguage.En && result is ProblemHttpResult problem) Translate(problem.ProblemDetails);
            return result;
        });

    private static void Translate(ProblemDetails details)
    {
        details.Detail = Localizer.Translate(details.Detail, AppLanguage.En);
        if (details is HttpValidationProblemDetails validation)
            foreach (var key in validation.Errors.Keys.ToList())
                validation.Errors[key] = Localizer.Translate(validation.Errors[key], AppLanguage.En);
        if (details.Extensions.TryGetValue("errors", out var errors) && errors is IDictionary<string, string[]> map)
            foreach (var key in map.Keys.ToList())
                map[key] = Localizer.Translate(map[key], AppLanguage.En);
    }
}
