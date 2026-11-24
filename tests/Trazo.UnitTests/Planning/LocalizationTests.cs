using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Localization;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class LocalizerTests
{
    private static string En(string text) => Localizer.Translate(text, AppLanguage.En)!;

    [Fact]
    public void SpanishIsReturnedUnchanged()
    {
        const string text = "El objetivo no es válido.";
        Assert.Equal(text, Localizer.Translate(text, AppLanguage.Es));
        Assert.Null(Localizer.Translate((string?)null, AppLanguage.En));
        Assert.Equal("", Localizer.Translate("", AppLanguage.En));
    }

    [Fact]
    public void AWholeTextAndATemplateWithValuesAreTranslated()
    {
        Assert.Equal("The goal is not valid.", En("El objetivo no es válido."));
        Assert.Equal("The plan length must be between 4 and 52 weeks.", En("La duración del plan debe estar entre 4 y 52 semanas."));
        Assert.Equal("The area 'knee' is not valid.", En("La zona 'knee' no es válida."));
    }

    [Fact]
    public void AnEngineMessageIsTranslatedSentenceBySentence()
    {
        Assert.Equal(
            "The last exposure was not completed. Keep the load and check the reason before progressing.",
            En("La última exposición no se completó. Mantén la carga y revisa el motivo antes de progresar."));
        Assert.Equal(
            "In the last two exposures at 62.5 kg the prescribed work was not completed (sets or minimum repetitions). " +
            "The load was probably too high: reduce to about 57.5 kg in the next session and progress again from there. " +
            "Check it with your professional if there is pain or fatigue.",
            En("En las dos últimas exposiciones con 62.5 kg no se completó el trabajo prescrito (series o mínimo de repeticiones). " +
               "Es probable que la carga fuera demasiado alta: reduce a unos 57.5 kg en la próxima sesión y vuelve a progresar desde ahí. " +
               "Revísalo con tu profesional si hay dolor o fatiga."));
    }

    [Fact]
    public void ANestedTextAndAPrefixedNoteAreTranslated()
    {
        Assert.Equal(
            "According to your last log: The recorded recovery was insufficient. Keep the load and check the context before progressing.",
            En("Según tu último registro: La recuperación registrada fue insuficiente. Mantén la carga y revisa el contexto antes de progresar."));
        Assert.StartsWith("bench-press: Pain was logged in this exercise.",
            En("bench-press: Se ha registrado dolor en este ejercicio. No ajustes el volumen como si fuera fatiga; evita progresarlo y solicita revisión profesional."));
        // A word given as a value is translated too.
        Assert.Contains("Load raised compared with the last one (60 kg)",
            En("Carga aumentada respecto a la última (60 kg): estimación a partir de 6.5 repeticiones (fórmula de Epley, aproximada; cambio limitado al 10 %). Revísala con tu profesional."));
    }

    [Fact]
    public void PhaseTitlesAndSessionTitlesAreTranslated_AndUnknownTextStaysAsItIs()
    {
        Assert.Equal("Accumulation 2", En("Acumulación 2"));
        Assert.Equal("Upper body A", En("Tren superior A"));
        Assert.Equal("Legs B", En("Pierna B"));
        Assert.Equal("Mi rutina de los lunes", En("Mi rutina de los lunes")); // what the user wrote is theirs
    }

    [Fact]
    public void TextsWrittenInEnglishComeOutInSpanishForSpanishRequests_AndSpanishOnesAreUnchanged()
    {
        Assert.Equal("Empieza primero la configuración de la verificación en dos pasos.", Localizer.Translate("Start two-factor setup first.", AppLanguage.Es));
        Assert.Equal("El enlace no es válido.", Localizer.Translate("The link is invalid.", AppLanguage.Es));
        Assert.Equal("El objetivo no es válido.", Localizer.Translate("El objetivo no es válido.", AppLanguage.Es));
        Assert.Equal("Something nobody translated.", Localizer.Translate("Something nobody translated.", AppLanguage.Es));
        // The same text in English stays English.
        Assert.Equal("Start two-factor setup first.", Localizer.Translate("Start two-factor setup first.", AppLanguage.En));
    }

    [Fact]
    public void ATemplateValueThatIsItselfAPhraseIsTranslated()
    {
        Assert.Equal("This request was made from the web on 16/11/2026 at 08:00 (UTC).",
            En("Esta solicitud se hizo desde la web el 16/11/2026 a las 08:00 (hora UTC)."));
    }

    [Theory]
    [InlineData(null, AppLanguage.Es)]
    [InlineData("", AppLanguage.Es)]
    [InlineData("es-ES,es;q=0.9", AppLanguage.Es)]
    [InlineData("en-GB,en;q=0.9,es;q=0.8", AppLanguage.En)]
    [InlineData("es;q=0.5, en;q=0.9", AppLanguage.En)]
    [InlineData("fr-FR,fr;q=0.9", AppLanguage.Es)]
    [InlineData("fr, en;q=0.4", AppLanguage.En)]
    [InlineData("en;q=0", AppLanguage.Es)]
    [InlineData("*", AppLanguage.Es)]
    [InlineData("en;q=abc", AppLanguage.En)]
    public void TheLanguageComesFromAcceptLanguage_SpanishByDefault(string? header, AppLanguage expected) =>
        Assert.Equal(expected, LanguageNegotiation.FromAcceptLanguage(header));

    [Fact]
    public void TheCatalogHasNoSpanishInItsEnglishAndNoDuplicates()
    {
        var entries = typeof(Localizer).Assembly.GetType("Trazo.Application.Localization.EnglishCatalog")!
            .GetField("Entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as (string Es, string En)[];
        Assert.NotNull(entries);
        Assert.True(entries!.Length > 200);
        Assert.Equal(entries.Length, entries.Select(e => e.Es).Distinct().Count());
        foreach (var (es, en) in entries)
        {
            Assert.False(SpanishLeak.IsSpanish(en), $"English entry looks Spanish: {en}");
            Assert.Equal(
                Regex.Matches(es, @"\{\d+\}").Select(m => m.Value).Order(), Regex.Matches(en, @"\{\d+\}").Select(m => m.Value).Order());
        }
    }
}

/// <summary>Finds Spanish left in a text (accents and common words), to check that English answers have none.</summary>
internal static class SpanishLeak
{
    private static readonly Regex Marks = new("[áéíóúñ¿¡]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Words = new(@"\b(el|la|los|las|una|tu|tus|para|con|sin|por|que|del|hasta|entre|sesión|ejercicio|carga|serie|plan activo)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsSpanish(string text) => Marks.IsMatch(text) || Words.IsMatch(text);

    /// <summary>Every string of the JSON that looks Spanish, with its path.</summary>
    public static List<string> Find(JsonElement element, string path = "$")
    {
        var found = new List<string>();
        switch (element.ValueKind)
        {
            case JsonValueKind.String when IsSpanish(element.GetString()!):
                found.Add($"{path} = {element.GetString()}");
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) found.AddRange(Find(property.Value, $"{path}.{property.Name}"));
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray()) found.AddRange(Find(item, $"{path}[{index++}]"));
                break;
        }
        return found;
    }
}

/// <summary>The plan API answers in the language of the request: Spanish by default, English with <c>Accept-Language: en</c>.</summary>
public sealed class LocalizedApiTests() : PlansApiBase(enforced: true)
{
    private static readonly DateOnly Started = new(2026, 11, 9);

    private HttpClient English(HttpClient client)
    {
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.Add("Accept-Language", "en-GB,en;q=0.9,es;q=0.5");
        return client;
    }

    private static object GeneratedProfile(object? days = null) => new
    {
        goal = "Hypertrophy", experience = "Intermediate", totalWeeks = 12, startDate = Started,
        trainingDays = days ?? new[] { "Monday", "Tuesday", "Thursday", "Friday" }, maxSessionMinutes = 75,
        availableEquipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" },
        excludedExerciseIds = Array.Empty<string>(), limitations = Array.Empty<string>()
    };

    private static void AssertNoSpanish(JsonElement json) => Assert.Empty(SpanishLeak.Find(json));

    // ------------------------------------------------------------------ errors

    [Fact]
    public async Task ErrorsAreSpanishByDefault_AndEnglishWhenAsked()
    {
        var user = await AddUserAsync("i18n-errors@example.com", subscribed: true);
        var spanish = ClientFor(user);
        var english = English(ClientFor(user));

        var missing = $"/api/v1/plans/current/sessions/{Guid.NewGuid()}/log";
        Assert.Equal("No tienes un plan activo.", (await JsonAsync(await spanish.PostAsJsonAsync(missing, new { outcome = "Completed", sets = Array.Empty<object>() }))).GetProperty("detail").GetString());
        Assert.Equal("You do not have an active plan.", (await JsonAsync(await english.PostAsJsonAsync(missing, new { outcome = "Completed", sets = Array.Empty<object>() }))).GetProperty("detail").GetString());

        var invalid = new { goal = "Nope", experience = "Intermediate", trainingDays = new[] { "Monday", "Friday" } };
        var es = await JsonAsync(await spanish.PostAsJsonAsync("/api/v1/plans", invalid));
        var en = await JsonAsync(await english.PostAsJsonAsync("/api/v1/plans", invalid));
        Assert.Equal("El objetivo no es válido.", es.GetProperty("errors").GetProperty("goal")[0].GetString());
        Assert.Equal("The goal is not valid.", en.GetProperty("errors").GetProperty("goal")[0].GetString());
        AssertNoSpanish(en);
    }

    [Fact]
    public async Task APlanThatCannotBeUsedOrFoundIsExplainedInEnglish()
    {
        var client = English(ClientFor(await AddUserAsync("i18n-conflict@example.com", subscribed: true)));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans", GeneratedProfile())).StatusCode);
        var again = await JsonAsync(await client.PostAsJsonAsync("/api/v1/plans", GeneratedProfile()));
        Assert.Equal("You already have an active plan. Archive it before creating another one.", again.GetProperty("detail").GetString());

        var sessions = (await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"))).GetProperty("weeks")[0].GetProperty("sessions");
        var body = new { outcome = "Completed", sets = Array.Empty<object>(), performedAt = Now.AddHours(2) };
        var future = await JsonAsync(await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{sessions[0].GetProperty("id").GetString()}/log", body));
        Assert.Equal("The workout date cannot be in the future.", future.GetProperty("detail").GetString());
        AssertNoSpanish(future);
    }

    // ------------------------------------------------------------------ what the plan shows

    [Fact]
    public async Task AGeneratedPlanIsShownInEnglish_WithTitlesPhasesNamesAndDisclaimer()
    {
        var client = English(ClientFor(await AddUserAsync("i18n-plan@example.com", subscribed: true)));
        var created = await JsonAsync(await client.PostAsJsonAsync("/api/v1/plans", GeneratedProfile()));
        AssertNoSpanish(created);
        Assert.StartsWith("Plan generated automatically from your profile.", created.GetProperty("disclaimer").GetString());
        var phases = created.GetProperty("macrocycle").GetProperty("phases").EnumerateArray().ToList();
        Assert.Equal("Accumulation 1", phases[0].GetProperty("name").GetString());
        Assert.Contains(phases, p => p.GetProperty("name").GetString()!.StartsWith("Deload"));

        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        AssertNoSpanish(week);
        Assert.Equal("Upper body A", week.GetProperty("sessions")[0].GetProperty("title").GetString());
        Assert.DoesNotContain("Ejercicio", week.ToString()); // the names come from the catalog in English

        var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
        AssertNoSpanish(block);
    }

    [Fact]
    public async Task TheSameGeneratedPlanIsStillSpanishWithoutTheHeader()
    {
        var client = ClientFor(await AddUserAsync("i18n-default@example.com", subscribed: true));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans", GeneratedProfile())).StatusCode);
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.Equal("Tren superior A", week.GetProperty("sessions")[0].GetProperty("title").GetString());
        Assert.Contains("Ejercicio", week.ToString());
    }

    [Fact]
    public async Task AManualPlanKeepsTheWordsOfTheUser_AndTranslatesOurOwn()
    {
        var client = English(ClientFor(await AddUserAsync("i18n-manual@example.com")));
        var created = await client.PostAsJsonAsync("/api/v1/plans/manual", new
        {
            name = "Mi rutina de fuerza", totalWeeks = 4, startDate = Started, notes = "Las notas son mías",
            days = new object[] { new { day = "Monday", title = "Pierna", exercises = new object[] { new { exerciseId = "squat", sets = 3 } } } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plan = await JsonAsync(created);
        Assert.Equal("Mi rutina de fuerza", plan.GetProperty("macrocycle").GetProperty("name").GetString()); // typed by the user
        Assert.Equal("Las notas son mías", plan.GetProperty("macrocycle").GetProperty("notes").GetString());
        Assert.StartsWith("Plan created by you.", plan.GetProperty("disclaimer").GetString());
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.Equal("Pierna", week.GetProperty("sessions")[0].GetProperty("title").GetString()); // not "Legs"
        Assert.Equal("squat", week.GetProperty("sessions")[0].GetProperty("exercises")[0].GetProperty("name").GetString()); // the catalog's English name
    }

    [Fact]
    public async Task TheOptionsShowTheDaysInEnglish()
    {
        var client = English(ClientFor(await AddUserAsync("i18n-options@example.com")));
        var options = await JsonAsync(await client.GetAsync("/api/v1/plans/options"));
        var monday = options.GetProperty("dayOptions")[0];
        Assert.Equal("Monday", monday.GetProperty("id").GetString());
        Assert.Equal("Monday", monday.GetProperty("label").GetString());
        var spanish = await JsonAsync(await ClientFor(await AddUserAsync("i18n-options2@example.com")).GetAsync("/api/v1/plans/options"));
        Assert.Equal("Lunes", spanish.GetProperty("dayOptions")[0].GetProperty("label").GetString());
    }

    // ------------------------------------------------------------------ what the engine says

    [Fact]
    public async Task TheAdaptationReportAndTheNotesAreInEnglish()
    {
        var client = English(ClientFor(await AddUserAsync("i18n-adapt@example.com", subscribed: true)));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans", GeneratedProfile())).StatusCode);
        var sessions = (await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"))).GetProperty("weeks")[0].GetProperty("sessions").EnumerateArray().ToList();
        JsonElement last = default;
        for (var i = 0; i < 3; i++)
        {
            var sets = sessions[i].GetProperty("exercises").EnumerateArray().Select(e => new
            {
                exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = 1, weightKg = 50m, repetitions = 6, rir = 0, completed = true
            }).ToArray();
            last = await JsonAsync(await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{sessions[i].GetProperty("id").GetString()}/log",
                new { outcome = "Partial", nonCompletionReason = "Fatigue", recovery = "Insufficient", sessionRpe = 10, sets, performedAt = Now.AddHours(-9 + i) }));
        }
        var report = last.GetProperty("adaptation");
        Assert.Contains(report.GetProperty("applied").EnumerateArray(), a => a.GetProperty("explanation").GetString()!.StartsWith("Conservative deload"));
        AssertNoSpanish(last);
        Assert.NotEqual(0, report.GetProperty("exercises").GetArrayLength());
    }

    [Fact]
    public async Task ALateLogExplainsItselfInEnglish_AndAFreeUserSeesTheLockInEnglish()
    {
        var client = English(ClientFor(await AddUserAsync("i18n-free@example.com")));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", new
        {
            name = "Plan", totalWeeks = 4, startDate = Started,
            days = new object[] { new { day = "Monday", title = "A", exercises = new object[] { new { exerciseId = "squat", sets = 3 } } } }
        })).StatusCode);
        var session = (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"))).GetProperty("sessions")[0];
        var logged = await JsonAsync(await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "NotCompleted", nonCompletionReason = "Time", sets = Array.Empty<object>(), performedAt = Now.AddDays(-1) }));
        Assert.Equal("Automatic adaptation is a Premium feature.", logged.GetProperty("adaptation").GetProperty("sessionExplanation").GetString());
        AssertNoSpanish(logged);
    }

    // ------------------------------------------------------------------ recovery and coming back

    [Fact]
    public async Task TheRecoveryAdviceAndTheReentryMessageAreInEnglish()
    {
        var client = English(ClientFor(await AddUserAsync("i18n-next@example.com", subscribed: true)));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", new
        {
            name = "Two presses", totalWeeks = 4, startDate = Started, schedulingMode = "Sequence", days = new object[]
            {
                new { title = "Press A", exercises = new object[] { new { exerciseId = "bench-press", sets = 4, targetRir = 2 } } },
                new { title = "Press B", exercises = new object[] { new { exerciseId = "bench-press", sets = 4, targetRir = 2 } } }
            }
        })).StatusCode);
        var first = (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"))).GetProperty("sessions")[0];
        var sets = Enumerable.Range(1, 4).Select(n => new { exerciseId = "bench-press", setNumber = n, weightKg = 60m, repetitions = 8, rir = 0, completed = true }).ToArray();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{first.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets, performedAt = Now.AddHours(-5) })).StatusCode);

        var wait = await JsonAsync(await client.GetAsync("/api/v1/plans/current/next"));
        var reason = wait.GetProperty("availability").GetProperty("reasons")[0].GetString()!;
        Assert.StartsWith("Chest: worked 5 h ago", reason);
        AssertNoSpanish(wait);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var log = await db.WorkoutLogs.SingleAsync();
            log.PerformedAt = Now.AddDays(-30);
            log.PerformedOn = new DateOnly(2026, 10, 17);
            await db.SaveChangesAsync();
        }
        var back = await JsonAsync(await client.GetAsync("/api/v1/plans/current/next"));
        Assert.StartsWith("You have gone 30 days without training. We have lightened this session", back.GetProperty("reentry").GetProperty("message").GetString());
        AssertNoSpanish(back);
    }
}

public sealed class LocalizedReviewApiTests() : PlansApiBase(enforced: false)
{
    [Fact]
    public async Task TheReviewErrorsAreTranslated()
    {
        var client = ClientFor(await AddUserAsync("i18n-review@example.com"));
        var created = await client.PostAsJsonAsync("/api/v1/plans/manual", new
        {
            name = "Plan", totalWeeks = 4, days = new object[] { new { day = "Monday", title = "A", exercises = new object[] { new { exerciseId = "squat", sets = 3 } } } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        client.DefaultRequestHeaders.Add("Accept-Language", "en");
        var none = await JsonAsync(await client.GetAsync("/api/v1/plans/current/review-request"));
        Assert.Equal("You have not requested any review.", none.GetProperty("detail").GetString());
    }
}

/// <summary>The user chooses the language: it is stored, it wins over the header on their own requests, and it is reported back.</summary>
public sealed class UserLanguageApiTests() : PlansApiBase(enforced: false)
{
    private static HttpClient WithHeader(HttpClient client, string language)
    {
        client.DefaultRequestHeaders.Add("Accept-Language", language);
        return client;
    }

    private static async Task<HttpResponseMessage> ChooseAsync(HttpClient client, string? language) =>
        await client.PutAsJsonAsync("/api/v1/account/language", new { language });

    [Fact]
    public async Task TheChoiceIsValidated_Stored_AndReportedBack()
    {
        var client = ClientFor(await AddUserAsync("lang-store@example.com"));
        Assert.Equal(HttpStatusCode.NoContent, (await ChooseAsync(client, "en")).StatusCode);
        Assert.Equal("en", (await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("language").GetString());
        Assert.Equal("en", (await JsonAsync(await client.GetAsync("/api/v1/auth/me"))).GetProperty("language").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await ChooseAsync(client, "ES")).StatusCode); // case does not matter
        Assert.Equal("es", (await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("language").GetString());

        var invalid = await ChooseAsync(client, "xx");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("es", (await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("language").GetString()); // unchanged

        Assert.Equal(HttpStatusCode.NoContent, (await ChooseAsync(client, "auto")).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("language").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await ChooseAsync(client, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ChooseAsync(ClientFor(null), "en")).StatusCode);
    }

    [Fact]
    public async Task EveryLanguageOfTheAppCanBeChosen_AndTheApiAnswersInEnglishUntilItIsTranslated()
    {
        var client = WithHeader(ClientFor(await AddUserAsync("lang-app@example.com")), "es");
        foreach (var language in new[] { "ca", "de", "pt", "fr", "it", "pl", "tr", "sv", "zh" })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await ChooseAsync(client, language)).StatusCode);
            Assert.Equal(language, (await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("language").GetString());
            // the choice wins over the header, and a language without translated texts is answered in English, never in Spanish
            Assert.Equal("You do not have an active plan.", (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"))).GetProperty("detail").GetString());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await ChooseAsync(client, "ru")).StatusCode);
    }

    [Fact]
    public void AnAppLanguageThatTheApiDoesNotSpeakYetIsEnglish()
    {
        Assert.Equal(AppLanguage.Es, LanguageNegotiation.FromChoice("es"));
        Assert.Equal(AppLanguage.En, LanguageNegotiation.FromChoice("en"));
        Assert.Equal(AppLanguage.En, LanguageNegotiation.FromChoice(" DE "));
        Assert.Null(LanguageNegotiation.FromChoice("ru"));
        Assert.Null(LanguageNegotiation.FromChoice(null));
        // the app asks "de, en;q=0.8": the first supported language is English
        Assert.Equal(AppLanguage.En, LanguageNegotiation.FromAcceptLanguage("de, en;q=0.8"));
    }

    [Fact]
    public async Task TheChosenLanguageWinsOverTheHeader_AndWithoutAChoiceTheHeaderDecides()
    {
        var client = WithHeader(ClientFor(await AddUserAsync("lang-wins@example.com")), "es");
        Assert.Equal("No tienes un plan activo.", (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"))).GetProperty("detail").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await ChooseAsync(client, "en")).StatusCode);
        Assert.Equal("You do not have an active plan.", (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"))).GetProperty("detail").GetString()); // header says es

        Assert.Equal(HttpStatusCode.NoContent, (await ChooseAsync(client, "auto")).StatusCode);
        Assert.Equal("No tienes un plan activo.", (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"))).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task TheChoiceOfOneUserNeverAffectsAnother()
    {
        var english = ClientFor(await AddUserAsync("lang-a@example.com"));
        var other = WithHeader(ClientFor(await AddUserAsync("lang-b@example.com")), "es");
        await ChooseAsync(english, "en");
        Assert.Equal("No tienes un plan activo.", (await JsonAsync(await other.GetAsync("/api/v1/plans/current/week"))).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task TheAccountFactsAreInTheChosenLanguage()
    {
        var admin = await AddUserAsync("lang-admin@example.com", role: "superadmin");
        var client = ClientFor(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await ChooseAsync(client, "en")).StatusCode);
        var info = await JsonAsync(await client.GetAsync("/api/v1/account"));
        Assert.Equal("You are the last superadministrator: appoint another one before deleting the account.", info.GetProperty("cannotDeleteReason").GetString());
    }
}
