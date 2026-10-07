using System.Data.Common;
using System.Net.Sockets;

namespace PartnerCommission.Messaging.Consuming;

public sealed record RetryDecision(bool DeadLetter, TimeSpan Delay);

/// <summary>
/// Decides what to do with a failed message.
/// </summary>
public sealed class RetryPolicy(int maxAttempts, TimeSpan baseDelay, TimeSpan maxDelay, Func<double>? jitter = null)
{
    private readonly Func<double> _jitter = jitter ?? Random.Shared.NextDouble;
    
    public RetryDecision Decide(Exception exception, int attempt)
    {
        if (exception is PermanentMessageException)
        {
            return new RetryDecision(true, TimeSpan.Zero);
        }

        // A temporary failure of infrastructure says nothing about the message itself, so it is retried until it passes.
        if (!IsTransient(exception) && attempt >= maxAttempts)
        {
            return new RetryDecision(true, TimeSpan.Zero);
        }

        return new RetryDecision(false, DelayFor(attempt));
    }

    /// <summary>
    /// True for failures that go away on their own: an explicit <see cref="TransientMessageException"/>, or a database /
    /// network problem anywhere in the exception chain.
    /// A bug or a constraint violation is not transient and still ends in the dead-letter topic after the attempt limit.
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TransientMessageException or TimeoutException or SocketException or DbException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }

    public TimeSpan DelayFor(int attempt)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 20);
        var uncapped = baseDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var capped = Math.Min(uncapped, maxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped * (0.5 + 0.5 * _jitter()));
    }
}
