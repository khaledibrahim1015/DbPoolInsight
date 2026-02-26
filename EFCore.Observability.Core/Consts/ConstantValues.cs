

namespace EFCore.Observability.Core.Consts;

/// <summary>
/// Constants for Entity Framework Core diagnostics event names.
/// These can be used with DiagnosticListener to subscribe to EF Core events.
/// </summary>
public static class EfCoreDiagnosticConstants
{
    /// <summary>
    /// The DiagnosticListener name for Entity Framework Core
    /// </summary>
    public const string ListenerName = "Microsoft.EntityFrameworkCore";

    /// <summary>
    /// Event fired when a DbContext instance is initialized (created)
    /// </summary>
    public const string ContextInitialized = "Microsoft.EntityFrameworkCore.Infrastructure.ContextInitialized";

    /// <summary>
    /// Event fired when a DbContext instance is disposed
    /// </summary>
    public const string ContextDisposed = "Microsoft.EntityFrameworkCore.Infrastructure.ContextDisposed";

    /// <summary>
    /// Event fired when a DbContext is starting to be disposed
    /// </summary>
    public const string ContextDisposing = "Microsoft.EntityFrameworkCore.Infrastructure.ContextDisposing";

    /// <summary>
    /// Event fired when a query is executed
    /// </summary>
    public const string QueryExecuting = "Microsoft.EntityFrameworkCore.Query.QueryExecuting";

    /// <summary>
    /// Event fired when a query execution completes
    /// </summary>
    public const string QueryExecuted = "Microsoft.EntityFrameworkCore.Query.QueryExecuted";

    /// <summary>
    /// Event fired when a command is executed
    /// </summary>
    public const string CommandExecuting = "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting";

    /// <summary>
    /// Event fired when a command execution completes
    /// </summary>
    public const string CommandExecuted = "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted";

    /// <summary>
    /// Event fired when an error occurs during command execution
    /// </summary>
    public const string CommandError = "Microsoft.EntityFrameworkCore.Database.Command.CommandError";

    /// <summary>
    /// Event fired when a connection is opening
    /// </summary>
    public const string ConnectionOpening = "Microsoft.EntityFrameworkCore.Database.Connection.ConnectionOpening";

    /// <summary>
    /// Event fired when a connection is opened
    /// </summary>
    public const string ConnectionOpened = "Microsoft.EntityFrameworkCore.Database.Connection.ConnectionOpened";

    /// <summary>
    /// Event fired when a connection is closing
    /// </summary>
    public const string ConnectionClosing = "Microsoft.EntityFrameworkCore.Database.Connection.ConnectionClosing";

    /// <summary>
    /// Event fired when a connection is closed
    /// </summary>
    public const string ConnectionClosed = "Microsoft.EntityFrameworkCore.Database.Connection.ConnectionClosed";

    /// <summary>
    /// Event fired when a transaction is started
    /// </summary>
    public const string TransactionStarted = "Microsoft.EntityFrameworkCore.Database.Transaction.TransactionStarted";

    /// <summary>
    /// Event fired when a transaction is committed
    /// </summary>
    public const string TransactionCommitted = "Microsoft.EntityFrameworkCore.Database.Transaction.TransactionCommitted";

    /// <summary>
    /// Event fired when a transaction is rolled back
    /// </summary>
    public const string TransactionRolledBack = "Microsoft.EntityFrameworkCore.Database.Transaction.TransactionRolledBack";

    /// <summary>
    /// Event fired when a transaction is disposed
    /// </summary>
    public const string TransactionDisposed = "Microsoft.EntityFrameworkCore.Database.Transaction.TransactionDisposed";

    /// <summary>
    /// Event fired when a save changes operation begins
    /// </summary>
    public const string SaveChangesStarting = "Microsoft.EntityFrameworkCore.SaveChangesStarting";

    /// <summary>
    /// Event fired when a save changes operation completes
    /// </summary>
    public const string SaveChangesCompleted = "Microsoft.EntityFrameworkCore.SaveChangesCompleted";

    /// <summary>
    /// Event fired when an error occurs during save changes
    /// </summary>
    public const string SaveChangesFailed = "Microsoft.EntityFrameworkCore.SaveChangesFailed";

   
}



