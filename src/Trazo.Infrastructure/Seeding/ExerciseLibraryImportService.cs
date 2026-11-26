using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trazo.Infrastructure.Persistence;
using Trazo.Domain.Training;
using Trazo.Application.Catalog;

namespace Trazo.Infrastructure.Seeding;

/// <summary>
/// Receives the private DietoExpress exercise-library archive. Imports only approved global
/// records, validates every checksum before opening a database transaction, and preserves
/// records/images that have been locally curated in TRAZO.
/// </summary>
public sealed class ExerciseLibraryImportService(TrazoDbContext db, ILogger<ExerciseLibraryImportService> logger)
{
    private const int MaxArchiveBytes = 512 * 1024 * 1024;
    private const int MaxImageBytes = 2 * 1024 * 1024;
    private static readonly Regex SafeId = new("^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ImageManifest(string ExerciseId, int Slot, string Path, string ContentType, string Source, int Size, string Sha256);
    private sealed record Manifest(int SchemaVersion, string Source, DateTimeOffset ExportedAtUtc,
        int ExerciseCount, int ImageCount, string Attribution, string ExercisesSha256, List<ImageManifest> Images);

    public async Task<object> ImportAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(archivePath);
        if (!fileInfo.Exists || fileInfo.Length is <= 0 or > MaxArchiveBytes)
            throw new InvalidDataException("The exercise-library archive is empty or exceeds the 512 MB limit.");

        byte[] exerciseBytes;
        Manifest manifest;
        var images = new List<(ImageManifest Metadata, byte[] Bytes)>();
        using (var file = File.OpenRead(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false))
        {
            var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Archive manifest is missing.");
            var exerciseEntry = archive.GetEntry("exercises.json") ?? throw new InvalidDataException("Exercise data is missing.");
            if (manifestEntry.Length > 1024 * 1024 || exerciseEntry.Length > 64L * 1024L * 1024L)
                throw new InvalidDataException("Archive metadata or exercise data exceeds the allowed size.");
            manifest = await ReadEntryJsonAsync<Manifest>(manifestEntry, 1024 * 1024, cancellationToken)
                ?? throw new InvalidDataException("Archive manifest is invalid.");
            if (manifest.Images is null || manifest.SchemaVersion != 1 ||
                !string.Equals(manifest.Source, "DietoExpress", StringComparison.Ordinal) ||
                manifest.ExerciseCount < 1 || manifest.ExerciseCount > 10000 || manifest.ImageCount < 0 ||
                manifest.ImageCount > manifest.ExerciseCount * 4 || manifest.Images.Count != manifest.ImageCount ||
                archive.Entries.Count != manifest.ImageCount + 2 || string.IsNullOrWhiteSpace(manifest.Attribution))
                throw new InvalidDataException("Archive manifest validation failed.");

            exerciseBytes = await ReadEntryBytesAsync(exerciseEntry, 64 * 1024 * 1024, cancellationToken);
            EnsureHash(exerciseBytes, manifest.ExercisesSha256, "exercises.json");
            var imagePaths = new HashSet<string>(StringComparer.Ordinal);

            foreach (var image in manifest.Images)
            {
                if (!SafeId.IsMatch(image.ExerciseId) || image.Slot is < 0 or > 3 ||
                    image.Size is <= 0 or > MaxImageBytes || image.Source is not ("repdb-free" or "custom") ||
                    image.Path != $"images/{image.ExerciseId}/{image.Slot}.{ExtensionFor(image.ContentType)}" || !imagePaths.Add(image.Path))
                    throw new InvalidDataException("An image manifest entry contains an invalid path or size.");
                var entry = archive.GetEntry(image.Path) ?? throw new InvalidDataException($"Image '{image.Path}' is missing.");
                var bytes = await ReadEntryBytesAsync(entry, MaxImageBytes, cancellationToken);
                if (bytes.Length != image.Size) throw new InvalidDataException($"Image '{image.Path}' has an unexpected size.");
                EnsureImageType(bytes, image.ContentType, image.Path);
                EnsureHash(bytes, image.Sha256, image.Path);
                images.Add((image, bytes));
            }
        }

        using var exercisesDocument = JsonDocument.Parse(exerciseBytes);
        if (exercisesDocument.RootElement.ValueKind != JsonValueKind.Array ||
            exercisesDocument.RootElement.GetArrayLength() != manifest.ExerciseCount)
            throw new InvalidDataException("Exercise count does not match the manifest.");

        var exercises = exercisesDocument.RootElement.EnumerateArray().ToArray();
        var exerciseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exercise in exercises)
        {
            var id = RequiredString(exercise, "id");
            var source = OptionalString(exercise, "source");
            if (!SafeId.IsMatch(id) || !exerciseIds.Add(id) ||
                source is not ("repdb-free" or "dietoexpress-curated") ||
                !string.Equals(OptionalString(exercise, "reviewStatus"), "approved", StringComparison.OrdinalIgnoreCase) ||
                !OptionalBoolean(exercise, "reviewed"))
                throw new InvalidDataException($"Exercise '{id}' is duplicated, has an unsupported source, or is not an approved global catalog record.");
        }
        if (manifest.Images.Any(image => !exerciseIds.Contains(image.ExerciseId)))
            throw new InvalidDataException("An image references an exercise outside the approved export.");

        var records = exercises.Select(record => (Record: record, Parsed: ReadExercise(record))).ToList();
        var invalid = records.Where(r => !r.Parsed.Validation.IsValid).ToList();
        if (invalid.Count > 0)
            throw new InvalidDataException($"{invalid.Count} exercise(s) have invalid metadata; nothing was imported. " + string.Join(" ",
                invalid.Take(20).Select(r => $"'{r.Parsed.Validation.Metadata.Id}' — {r.Parsed.Validation.Describe()}")));

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(731904222)", connection, transaction))
                await advisory.ExecuteNonQueryAsync(cancellationToken);

            var inserted = 0;
            var updated = 0;
            var preserved = 0;

            foreach (var (record, (source, validation)) in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var changed = await UpsertExerciseAsync(connection, transaction, record, source, validation.Metadata, manifest, cancellationToken);
                if (changed == 0) { preserved++; continue; }
                if (changed == 1) inserted++; else updated++;
            }

            var imageImported = 0;
            var imagePreserved = 0;
            foreach (var (metadata, bytes) in images)
            {
                // Image import is independent from exercise metadata updates. A locally curated
                // exercise may still be missing an image; UpsertImageAsync inserts it in that case
                // and only replaces an existing image whose origin is DietoExpress.
                var changed = await UpsertImageAsync(connection, transaction, metadata, bytes, cancellationToken);
                if (changed) imageImported++; else imagePreserved++;
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Imported DietoExpress exercise library: {Inserted} new, {Updated} refreshed, {Preserved} locally curated, {Images} images.",
                inserted, updated, preserved, imageImported);
            return new
            {
                exerciseCount = manifest.ExerciseCount,
                imageCount = manifest.ImageCount,
                imported = inserted,
                updated,
                preserved,
                imagesImported = imageImported,
                imagesPreserved = imagePreserved,
                attribution = manifest.Attribution,
                exportedAtUtc = manifest.ExportedAtUtc
            };
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    /// <summary>An archive record (its source already checked) as catalog metadata, checked against the same rules as every other way into the catalog.</summary>
    private static (string Source, ExerciseValidation Validation) ReadExercise(JsonElement e)
    {
        var id = RequiredString(e, "id");
        var source = OptionalString(e, "source") ?? "unknown";
        var nameEn = OptionalString(e, "nameEn") ?? id;
        var sourceEquipment = StringArray(e, "requiredEquipment") ?? (OptionalString(e, "equipment") is { Length: > 0 } item ? [item] : []);
        var metadata = new ExerciseMetadata(
            id, OptionalString(e, "nameEs") ?? nameEn, nameEn,
            OptionalString(e, "descriptionEs") ?? OptionalString(e, "descriptionEn") ?? "",
            StringArray(e, "instructionsEs") ?? [], StringArray(e, "tipsEs") ?? [],
            OptionalString(e, "forceType") ?? "", OptionalString(e, "mechanic") ?? "", OptionalString(e, "difficulty") ?? "",
            OptionalString(e, "equipment"), OptionalString(e, "bodyPart") ?? "", OptionalDouble(e, "met") ?? 0d,
            OptionalBooleanNullable(e, "isUnilateral") ?? false, OptionalBooleanNullable(e, "isBodyweight") ?? false,
            OptionalString(e, "movementPattern") ?? "",
            StringArray(e, "reviewedPrimaryMuscles") ?? StringArray(e, "primaryMuscles") ?? [],
            StringArray(e, "reviewedSecondaryMuscles") ?? StringArray(e, "secondaryMuscles") ?? [],
            OptionalString(e, "minimumExperience") ?? "",
            StringArray(e, "suitableGoals") ?? StringArray(e, "goals") ?? [],
            ExerciseEquipmentRequirements.Normalize(id, sourceEquipment), OptionalInt(e, "selectionPriority") ?? 50);
        return (source, ExerciseMetadataValidator.Validate(metadata));
    }

    private static async Task<int> UpsertExerciseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        JsonElement record, string source, ExerciseMetadata e, Manifest manifest, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO exercise_catalog
                (exercise_id, "Source", "NameEn", "NameEs", "DescriptionEs", "InstructionsEs", "TipsEs",
                 "ForceType", "Mechanic", "Difficulty", "Equipment", "BodyPart", "Met", "IsUnilateral",
                 "IsBodyweight", "MovementPattern", "PrimaryMuscles", "SecondaryMuscles", "MinimumExperience",
                 "SuitableGoals", "RequiredEquipment", "SelectionPriority", "SeedVersion", "CreatedAt", "UpdatedAt")
            VALUES
                (@id, @source, @name_en, @name_es, @description, @instructions, @tips,
                 @force, @mechanic, @difficulty, @equipment, @body_part, @met, @unilateral,
                 @bodyweight, @pattern, @primary, @secondary, @experience, @goals, @required_equipment,
                 @priority, 1, NOW(), NOW())
            ON CONFLICT (exercise_id) DO UPDATE SET
                "Source" = EXCLUDED."Source", "NameEn" = EXCLUDED."NameEn", "NameEs" = EXCLUDED."NameEs",
                "DescriptionEs" = EXCLUDED."DescriptionEs", "InstructionsEs" = EXCLUDED."InstructionsEs",
                "TipsEs" = EXCLUDED."TipsEs", "ForceType" = EXCLUDED."ForceType", "Mechanic" = EXCLUDED."Mechanic",
                "Difficulty" = EXCLUDED."Difficulty", "Equipment" = EXCLUDED."Equipment", "BodyPart" = EXCLUDED."BodyPart",
                "Met" = EXCLUDED."Met", "IsUnilateral" = EXCLUDED."IsUnilateral", "IsBodyweight" = EXCLUDED."IsBodyweight",
                "MovementPattern" = EXCLUDED."MovementPattern", "PrimaryMuscles" = EXCLUDED."PrimaryMuscles",
                "SecondaryMuscles" = EXCLUDED."SecondaryMuscles", "MinimumExperience" = EXCLUDED."MinimumExperience",
                "SuitableGoals" = EXCLUDED."SuitableGoals", "RequiredEquipment" = EXCLUDED."RequiredEquipment",
                "SelectionPriority" = EXCLUDED."SelectionPriority", "UpdatedAt" = NOW()
            WHERE EXISTS (SELECT 1 FROM exercise_catalog_import_metadata m
                          WHERE m.exercise_id = EXCLUDED.exercise_id AND m.is_locally_curated = FALSE)
            RETURNING (xmax = 0) AS inserted
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", e.Id);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("name_en", e.NameEn);
        command.Parameters.AddWithValue("name_es", e.NameEs);
        command.Parameters.AddWithValue("description", e.DescriptionEs);
        command.Parameters.AddWithValue("instructions", e.InstructionsEs);
        command.Parameters.AddWithValue("tips", e.TipsEs);
        command.Parameters.AddWithValue("force", e.ForceType);
        command.Parameters.AddWithValue("mechanic", e.Mechanic);
        command.Parameters.AddWithValue("difficulty", e.Difficulty);
        command.Parameters.AddWithValue("equipment", (object?)e.Equipment ?? DBNull.Value);
        command.Parameters.AddWithValue("body_part", e.BodyPart);
        command.Parameters.AddWithValue("met", e.Met);
        command.Parameters.AddWithValue("unilateral", e.IsUnilateral);
        command.Parameters.AddWithValue("bodyweight", e.IsBodyweight);
        command.Parameters.AddWithValue("pattern", e.MovementPattern);
        command.Parameters.AddWithValue("primary", e.PrimaryMuscles);
        command.Parameters.AddWithValue("secondary", e.SecondaryMuscles);
        command.Parameters.AddWithValue("experience", e.MinimumExperience);
        command.Parameters.AddWithValue("goals", e.SuitableGoals);
        command.Parameters.AddWithValue("required_equipment", e.RequiredEquipment);
        command.Parameters.AddWithValue("priority", e.SelectionPriority);
        var scalar = await command.ExecuteScalarAsync(ct);
        if (scalar is null) return 0;

        // The approved source content is the Spanish translation. Never overwrite a
        // translation that has been explicitly published/curated by an administrator.
        const string spanishTranslationSql = """
            INSERT INTO exercise_catalog_translations
                (exercise_id, "LanguageCode", "Name", "Description", "Instructions", "Tips", "Status", "UpdatedAt")
            VALUES (@id, 'es', @name_es, @description, @instructions, @tips, 'published', NOW())
            ON CONFLICT (exercise_id, "LanguageCode") DO UPDATE SET
                "Name" = EXCLUDED."Name", "Description" = EXCLUDED."Description",
                "Instructions" = EXCLUDED."Instructions", "Tips" = EXCLUDED."Tips", "UpdatedAt" = NOW()
            WHERE exercise_catalog_translations."Status" <> 'published'
            """;
        await using (var translationCommand = new NpgsqlCommand(spanishTranslationSql, connection, transaction))
        {
            translationCommand.Parameters.AddWithValue("id", e.Id);
            translationCommand.Parameters.AddWithValue("name_es", e.NameEs);
            translationCommand.Parameters.AddWithValue("description", e.DescriptionEs);
            translationCommand.Parameters.AddWithValue("instructions", e.InstructionsEs);
            translationCommand.Parameters.AddWithValue("tips", e.TipsEs);
            await translationCommand.ExecuteNonQueryAsync(ct);
        }

        const string englishNameSql = """
            INSERT INTO exercise_catalog_translations
                (exercise_id, "LanguageCode", "Name", "Status", "UpdatedAt")
            VALUES (@id, 'en', @name_en, 'draft', NOW())
            ON CONFLICT (exercise_id, "LanguageCode") DO UPDATE SET
                "Name" = EXCLUDED."Name", "UpdatedAt" = NOW()
            WHERE exercise_catalog_translations."Status" = 'draft'
            """;
        await using (var translationCommand = new NpgsqlCommand(englishNameSql, connection, transaction))
        {
            translationCommand.Parameters.AddWithValue("id", e.Id);
            translationCommand.Parameters.AddWithValue("name_en", e.NameEn);
            await translationCommand.ExecuteNonQueryAsync(ct);
        }

        var raw = record.GetRawText();
        const string metadataSql = """
            INSERT INTO exercise_catalog_import_metadata
                (exercise_id, source_system, source_record_json, attribution, review_notes, reviewed_by, reviewed_at,
                 is_locally_curated, imported_at)
            VALUES (@id, 'DietoExpress', @raw::jsonb, @attribution, @notes, @reviewed_by, @reviewed_at, FALSE, NOW())
            ON CONFLICT (exercise_id) DO UPDATE SET source_record_json = EXCLUDED.source_record_json,
                attribution = EXCLUDED.attribution, review_notes = EXCLUDED.review_notes,
                reviewed_by = EXCLUDED.reviewed_by, reviewed_at = EXCLUDED.reviewed_at, imported_at = NOW()
            WHERE exercise_catalog_import_metadata.is_locally_curated = FALSE
            """;
        await using var metadataCommand = new NpgsqlCommand(metadataSql, connection, transaction);
        metadataCommand.Parameters.AddWithValue("id", e.Id);
        metadataCommand.Parameters.AddWithValue("raw", raw);
        metadataCommand.Parameters.AddWithValue("attribution", manifest.Attribution);
        metadataCommand.Parameters.AddWithValue("notes", (object?)OptionalString(record, "reviewNotes") ?? DBNull.Value);
        metadataCommand.Parameters.AddWithValue("reviewed_by", (object?)OptionalString(record, "reviewedBy") ?? DBNull.Value);
        metadataCommand.Parameters.AddWithValue("reviewed_at", (object?)OptionalDateTime(record, "reviewedAt") ?? DBNull.Value);
        await metadataCommand.ExecuteNonQueryAsync(ct);
        return Convert.ToBoolean(scalar) ? 1 : 2;
    }

    private static async Task<bool> UpsertImageAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ImageManifest image, byte[] bytes, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO exercise_catalog_images (exercise_id, slot, content_type, data, sha256, source, origin_system, imported_at)
            VALUES (@id, @slot, @type, @data, @hash, @source, 'DietoExpress', NOW())
            ON CONFLICT (exercise_id, slot) DO UPDATE SET content_type = EXCLUDED.content_type,
                data = EXCLUDED.data, sha256 = EXCLUDED.sha256, source = EXCLUDED.source, imported_at = NOW()
            WHERE exercise_catalog_images.origin_system = 'DietoExpress'
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", image.ExerciseId);
        command.Parameters.AddWithValue("slot", (short)image.Slot);
        command.Parameters.AddWithValue("type", image.ContentType);
        command.Parameters.AddWithValue("data", bytes);
        command.Parameters.AddWithValue("hash", image.Sha256.ToLowerInvariant());
        command.Parameters.AddWithValue("source", image.Source);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    private static async Task<T?> ReadEntryJsonAsync<T>(ZipArchiveEntry entry, int maxBytes, CancellationToken ct)
    {
        var bytes = await ReadEntryBytesAsync(entry, maxBytes, ct);
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
    }

    private static async Task<byte[]> ReadEntryBytesAsync(ZipArchiveEntry entry, int maxBytes, CancellationToken ct)
    {
        if (entry.Length > maxBytes) throw new InvalidDataException($"Archive entry '{entry.FullName}' exceeds its size limit.");
        await using var input = entry.Open();
        using var output = new MemoryStream((int)Math.Min(entry.Length, maxBytes));
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > maxBytes)
                throw new InvalidDataException($"Archive entry '{entry.FullName}' expands beyond its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }

    private static void EnsureHash(byte[] bytes, string expected, string path)
    {
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Checksum mismatch for '{path}'.");
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/webp" => "webp",
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/svg+xml" => "svg",
        _ => throw new InvalidDataException($"Unsupported image content type '{contentType}'.")
    };

    private static void EnsureImageType(byte[] bytes, string contentType, string path)
    {
        var valid = contentType switch
        {
            "image/webp" => bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P',
            "image/png" => bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47,
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            "image/svg+xml" => IsSafeSvg(bytes),
            _ => false
        };
        if (!valid) throw new InvalidDataException($"Image signature validation failed for '{path}'.");
    }

    private static bool IsSafeSvg(byte[] bytes)
    {
        var svg = System.Text.Encoding.UTF8.GetString(bytes);
        return svg.StartsWith("<svg ", StringComparison.Ordinal) &&
            svg.Contains("xmlns=\"http://www.w3.org/2000/svg\"", StringComparison.Ordinal) &&
            !Regex.IsMatch(svg, @"<\s*(script|foreignObject|iframe|object|embed)\b|\bon[a-z]+\s*=|(?:href|src)\s*=|url\s*\(|javascript:|<!DOCTYPE|<!ENTITY", RegexOptions.IgnoreCase);
    }

    private static string RequiredString(JsonElement e, string name) =>
        OptionalString(e, name) is { Length: > 0 } value ? value : throw new InvalidDataException($"Missing '{name}'.");

    private static string? OptionalString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string[]? StringArray(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
            : null;

    private static bool OptionalBoolean(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool? OptionalBooleanNullable(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static int? OptionalInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static double? OptionalDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;

    private static DateTimeOffset? OptionalDateTime(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), out var date) ? date : null;
}
