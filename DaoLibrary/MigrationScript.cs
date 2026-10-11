namespace DaoLibrary;

// A migration script ready to run: name (history key), SQL batch and SHA-256 hash of its content.
public record MigrationScript(string Name, string Sql, string Hash);
