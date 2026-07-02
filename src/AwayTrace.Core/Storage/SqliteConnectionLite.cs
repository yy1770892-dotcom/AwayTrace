using System.Runtime.InteropServices;
using System.Text;

namespace AwayTrace.Core.Storage;

public sealed class SqliteConnectionLite : IDisposable
{
    private readonly IntPtr _db;
    private bool _disposed;

    public SqliteConnectionLite(string path)
    {
        var flags = WinsqliteNative.SqliteOpenReadWrite
            | WinsqliteNative.SqliteOpenCreate
            | WinsqliteNative.SqliteOpenFullMutex;

        var result = WinsqliteNative.sqlite3_open_v2(ToUtf8(path), out _db, flags, IntPtr.Zero);
        if (result != WinsqliteNative.SqliteOk)
        {
            throw new SqliteException("SQLite 데이터베이스를 열 수 없습니다.");
        }
    }

    public void ExecuteBatch(string sql)
    {
        EnsureNotDisposed();

        var result = WinsqliteNative.sqlite3_exec(_db, ToUtf8(sql), IntPtr.Zero, IntPtr.Zero, out var errorPointer);
        if (result != WinsqliteNative.SqliteOk)
        {
            var message = errorPointer == IntPtr.Zero ? ReadError() : PtrToUtf8String(errorPointer);
            if (errorPointer != IntPtr.Zero)
            {
                WinsqliteNative.sqlite3_free(errorPointer);
            }

            throw new SqliteException(message);
        }
    }

    public int Execute(string sql, params SqliteParameter[] parameters)
    {
        EnsureNotDisposed();
        var statement = Prepare(sql);
        try
        {
            BindParameters(statement, parameters);
            var result = WinsqliteNative.sqlite3_step(statement);
            if (result != WinsqliteNative.SqliteDone)
            {
                throw new SqliteException(ReadError());
            }

            return 1;
        }
        finally
        {
            Finalize(statement);
        }
    }

    public long ExecuteInsert(string sql, params SqliteParameter[] parameters)
    {
        Execute(sql, parameters);
        return WinsqliteNative.sqlite3_last_insert_rowid(_db);
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string sql, params SqliteParameter[] parameters)
    {
        EnsureNotDisposed();
        var statement = Prepare(sql);
        try
        {
            BindParameters(statement, parameters);
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            while (true)
            {
                var result = WinsqliteNative.sqlite3_step(statement);
                if (result == WinsqliteNative.SqliteDone)
                {
                    return rows;
                }

                if (result != WinsqliteNative.SqliteRow)
                {
                    throw new SqliteException(ReadError());
                }

                rows.Add(ReadRow(statement));
            }
        }
        finally
        {
            Finalize(statement);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        WinsqliteNative.sqlite3_close(_db);
        _disposed = true;
    }

    private IntPtr Prepare(string sql)
    {
        var result = WinsqliteNative.sqlite3_prepare_v2(_db, ToUtf8(sql), -1, out var statement, IntPtr.Zero);
        if (result != WinsqliteNative.SqliteOk)
        {
            throw new SqliteException(ReadError());
        }

        return statement;
    }

    private void BindParameters(IntPtr statement, SqliteParameter[] parameters)
    {
        for (var index = 0; index < parameters.Length; index++)
        {
            var result = BindParameter(statement, index + 1, parameters[index].Value);
            if (result != WinsqliteNative.SqliteOk)
            {
                throw new SqliteException(ReadError());
            }
        }
    }

    private static int BindParameter(IntPtr statement, int index, object? value)
    {
        return value switch
        {
            null => WinsqliteNative.sqlite3_bind_null(statement, index),
            bool typed => WinsqliteNative.sqlite3_bind_int64(statement, index, typed ? 1 : 0),
            int typed => WinsqliteNative.sqlite3_bind_int64(statement, index, typed),
            long typed => WinsqliteNative.sqlite3_bind_int64(statement, index, typed),
            double typed => WinsqliteNative.sqlite3_bind_double(statement, index, typed),
            // UTC로 통일해 저장한다. 로컬 오프셋(+09:00 등)을 섞어 저장하면
            // SQLite의 문자열 비교/정렬(ORDER BY, >=)이 시간 순서와 어긋날 수 있다.
            // 표시할 때는 각 화면에서 ToLocalTime()으로 변환한다.
            DateTimeOffset typed => BindText(statement, index, typed.ToUniversalTime().ToString("O")),
            DateTime typed => BindText(statement, index, typed.ToUniversalTime().ToString("O")),
            Guid typed => BindText(statement, index, typed.ToString()),
            Enum typed => BindText(statement, index, typed.ToString()),
            _ => BindText(statement, index, Convert.ToString(value) ?? string.Empty)
        };
    }

    private static int BindText(IntPtr statement, int index, string value)
    {
        var bytes = ToUtf8(value);
        return WinsqliteNative.sqlite3_bind_text(
            statement,
            index,
            bytes,
            bytes.Length - 1,
            WinsqliteNative.SqliteTransient);
    }

    private static IReadOnlyDictionary<string, object?> ReadRow(IntPtr statement)
    {
        var count = WinsqliteNative.sqlite3_column_count(statement);
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var name = PtrToUtf8String(WinsqliteNative.sqlite3_column_name(statement, i));
            row[name] = ReadColumn(statement, i);
        }

        return row;
    }

    private static object? ReadColumn(IntPtr statement, int columnIndex)
    {
        return WinsqliteNative.sqlite3_column_type(statement, columnIndex) switch
        {
            WinsqliteNative.SqliteInteger => WinsqliteNative.sqlite3_column_int64(statement, columnIndex),
            WinsqliteNative.SqliteFloat => WinsqliteNative.sqlite3_column_double(statement, columnIndex),
            WinsqliteNative.SqliteText => ReadColumnText(statement, columnIndex),
            WinsqliteNative.SqliteNull => null,
            _ => ReadColumnText(statement, columnIndex)
        };
    }

    private static string? ReadColumnText(IntPtr statement, int columnIndex)
    {
        var pointer = WinsqliteNative.sqlite3_column_text(statement, columnIndex);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        var byteCount = WinsqliteNative.sqlite3_column_bytes(statement, columnIndex);
        var bytes = new byte[byteCount];
        Marshal.Copy(pointer, bytes, 0, byteCount);
        return Encoding.UTF8.GetString(bytes);
    }

    private void Finalize(IntPtr statement)
    {
        var result = WinsqliteNative.sqlite3_finalize(statement);
        if (result != WinsqliteNative.SqliteOk)
        {
            throw new SqliteException(ReadError());
        }
    }

    private string ReadError() => PtrToUtf8String(WinsqliteNative.sqlite3_errmsg(_db));

    private static byte[] ToUtf8(string value) => Encoding.UTF8.GetBytes(value + "\0");

    private static string PtrToUtf8String(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return string.Empty;
        }

        var length = 0;
        while (Marshal.ReadByte(pointer, length) != 0)
        {
            length++;
        }

        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
