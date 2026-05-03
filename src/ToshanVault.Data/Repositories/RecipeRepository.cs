using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

public sealed class RecipeRepository
{
    private readonly IDbConnectionFactory _factory;

    public RecipeRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<long> InsertAsync(Recipe r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.AddedAt == default) r.AddedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(r.Category)) r.Category = RecipeCategorizer.Classify(r.Title);
        await using var conn = _factory.Open();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO recipe(title, author, cuisine, rating, youtube_url,
                                 thumbnail_path, notes_md, is_favourite, is_tried,
                                 category, added_at)
              VALUES (@Title, @Author, @Cuisine, @Rating, @YoutubeUrl,
                      @ThumbnailPath, @NotesMd, @IsFavourite, @IsTried,
                      @Category, @AddedAt);
              SELECT last_insert_rowid();",
            r, cancellationToken: ct)).ConfigureAwait(false);
        r.Id = id;
        return id;
    }

    public async Task UpdateAsync(Recipe r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (string.IsNullOrWhiteSpace(r.Category)) r.Category = RecipeCategorizer.Classify(r.Title);
        await using var conn = _factory.Open();
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE recipe SET title=@Title, author=@Author, cuisine=@Cuisine,
                                rating=@Rating, youtube_url=@YoutubeUrl,
                                thumbnail_path=@ThumbnailPath, notes_md=@NotesMd,
                                is_favourite=@IsFavourite, is_tried=@IsTried,
                                category=@Category
              WHERE id=@Id;",
            r, cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0) throw new InvalidOperationException($"Recipe {r.Id} not found.");
    }

    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM recipe WHERE id=@id;", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<Recipe?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<Recipe>(new CommandDefinition(
            @"SELECT id, title, author, cuisine, rating, youtube_url,
                     thumbnail_path, notes_md, is_favourite, is_tried,
                     category, added_at
              FROM recipe WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Recipe>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        // Sort: tried first (user prefers proven recipes at top), then category
        // (Chicken / Egg / Other), then favourite within category, then title.
        var rows = await conn.QueryAsync<Recipe>(new CommandDefinition(
            @"SELECT id, title, author, cuisine, rating, youtube_url,
                     thumbnail_path, notes_md, is_favourite, is_tried,
                     category, added_at
              FROM recipe
              ORDER BY is_tried DESC, category, is_favourite DESC, title;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task SetTagsAsync(long recipeId, IEnumerable<string> tags, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        await using var conn = _factory.Open();
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM recipe_tag WHERE recipe_id=@recipeId;",
                new { recipeId }, tx, cancellationToken: ct)).ConfigureAwait(false);

            var distinctTags = tags.Where(t => !string.IsNullOrWhiteSpace(t))
                                   .Select(t => t.Trim())
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .ToList();
            foreach (var tag in distinctTags)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO recipe_tag(recipe_id, tag) VALUES (@recipeId, @tag);",
                    new { recipeId, tag }, tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* surface original */ }
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(long recipeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT tag FROM recipe_tag WHERE recipe_id=@recipeId ORDER BY tag;",
            new { recipeId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }
}
