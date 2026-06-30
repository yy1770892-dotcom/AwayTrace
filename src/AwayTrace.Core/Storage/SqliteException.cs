namespace AwayTrace.Core.Storage;

public sealed class SqliteException : Exception
{
    public SqliteException(string message)
        : base(message)
    {
    }
}
