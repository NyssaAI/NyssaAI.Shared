using Npgsql;
using Nyssa.Results;
using Standards.Sample.Email;

namespace Standards.Sample.Persistence.Registration;

/// <summary>Registers a normalized email once in PostgreSQL and owns the operational outcome.</summary>
public sealed class EmailRegistration
{
    private readonly string _connectionString;
    private readonly RegistrationLog _log;
    private readonly TimeProvider _time;

    /// <summary>Validates connection syntax and borrows the host log and optional monotonic clock.</summary>
    /// <remarks>Construction performs no I/O. Each call owns and disposes its unpooled connection; setup errors throw.</remarks>
    public EmailRegistration(string connectionString, RegistrationLog log, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false, Timeout = 2, CommandTimeout = 2, IncludeErrorDetail = false
        }.ConnectionString;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Returns success, validation/conflict rejection, or the canonical dependency/cancellation failure.</summary>
    /// <remarks>Cancellation is returned as Canceled. A lost acknowledgement after sending the insert is Indeterminate;
    /// reconcile persisted state before retrying. Errors retain diagnostic cause details internally: expose only UserMessage
    /// outside the trusted host and never serialize the complete Result into logs.</remarks>
    public async Task<Result> RegisterAsync(string? email, Guid correlationId, CancellationToken cancellationToken = default)
    {
        var started = _time.GetTimestamp();
        var outcome = "failure";
        var code = "database.unavailable";
        var attempts = 0;
        var isWriteStarted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var address = EmailAddress.Create(email);
            if (address.IsFailure)
            {
                outcome = "rejected";
                code = "email.invalid";
                return Result.Failure(address.Errors);
            }

            var settings = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                ApplicationName = correlationId.ToString("D")
            };
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            attempts = 1;
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "INSERT INTO registrations (email) VALUES ($1) ON CONFLICT DO NOTHING", connection);
            command.Parameters.AddWithValue(address.Value.Value);
            isWriteStarted = true;
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                outcome = "rejected";
                code = "email.exists";
                return Result.Failure(Error.FromMessage("Email already registered.", ErrorCategory.Conflict));
            }
            outcome = "success";
            code = "none";
            return Result.Success();
        }
        catch (OperationCanceledException error)
        {
            var cause = Error.FromCancellation(error, cancellationToken);
            outcome = cause.Category == ErrorCategory.Canceled ? "cancelled" : "failure";
            code = outcome == "cancelled" ? "caller.cancelled" : "database.timeout";
            return Result.Failure(cause);
        }
        catch (PostgresException error)
        {
            // A server response establishes rejection, unlike a lost transport acknowledgement.
            outcome = "failure";
            code = "database.rejected";
            return Result.Failure(Error.FromException(error, "Database rejected registration.", ErrorCategory.Unexpected));
        }
        catch (NpgsqlException error)
        {
            outcome = "failure";
            code = isWriteStarted ? "database.outcome_unknown" : "database.unavailable";
            return Result.Failure(Error.FromException(error, "Database registration could not be confirmed.",
                isWriteStarted ? ErrorCategory.Indeterminate : ErrorCategory.Unavailable));
        }
        finally
        {
            var elapsedMs = _time.GetElapsedTime(started).TotalMilliseconds;
            // Provisional local example threshold, not a measured service-level objective.
            if (attempts > 0 && elapsedMs > 250)
                _log.Slow(correlationId, elapsedMs);
            _log.Completed(correlationId, outcome, elapsedMs, code, attempts);
        }
    }
}
