using System.Net.Sockets;
using PartnerCommission.Messaging.Consuming;
using Shouldly;

namespace Messaging.UnitTests;

public class RetryPolicyTests
{
    private static RetryPolicy Policy(double jitter = 1.0) =>
        new(maxAttempts: 5, baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30), jitter: () => jitter);

    [Fact]
    public void Permanent_failure_is_dead_lettered_immediately() =>
        Policy().Decide(new PermanentMessageException("bad"), attempt: 1).DeadLetter.ShouldBeTrue();

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Ordinary_failure_is_retried_before_the_limit(int attempt)
    {
        var decision = Policy().Decide(new InvalidOperationException(), attempt);

        decision.DeadLetter.ShouldBeFalse();
        decision.Delay.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Ordinary_failure_is_dead_lettered_at_the_limit(int attempt) =>
        Policy().Decide(new InvalidOperationException(), attempt).DeadLetter.ShouldBeTrue();

    [Theory]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(1000)]
    public void Transient_failure_is_retried_without_limit(int attempt) =>
        Policy().Decide(new TransientMessageException("partners down"), attempt).DeadLetter.ShouldBeFalse();

    public static TheoryData<Exception> InfrastructureFailures => new()
    {
        new FakeDbException(isTransient: true),                                              // database says: temporary
        new InvalidOperationException("EF wrapper", new FakeDbException(isTransient: true)), // EF Core wraps the real error
        new InvalidOperationException("outer", new IOException("io", new SocketException())),// network, deep in the chain
        new SocketException(),
        new TimeoutException()
    };

    [Theory]
    [MemberData(nameof(InfrastructureFailures))]
    public void Database_and_network_failures_are_retried_without_limit(Exception failure)
    {
        RetryPolicy.IsTransient(failure).ShouldBeTrue();
        Policy().Decide(failure, attempt: 1000).DeadLetter.ShouldBeFalse();
    }

    public static TheoryData<Exception> OrdinaryFailures => new()
    {
        new NullReferenceException(),                                                          // a bug
        new InvalidOperationException("plain"),
        new FakeDbException(isTransient: false),                                               // e.g. constraint violation
        new InvalidOperationException("wrapped", new FakeDbException(isTransient: false))
    };

    [Theory]
    [MemberData(nameof(OrdinaryFailures))]
    public void Bugs_and_permanent_database_errors_still_end_in_dead_letter(Exception failure)
    {
        RetryPolicy.IsTransient(failure).ShouldBeFalse();
        Policy().Decide(failure, attempt: 1).DeadLetter.ShouldBeFalse();
        Policy().Decide(failure, attempt: 5).DeadLetter.ShouldBeTrue();
    }

    [Fact]
    public void Permanent_exception_wins_even_if_it_wraps_a_transient_one() =>
        Policy().Decide(new PermanentMessageException("bad", new TimeoutException()), attempt: 1).DeadLetter.ShouldBeTrue();

    private sealed class FakeDbException(bool isTransient) : System.Data.Common.DbException("db error")
    {
        public override bool IsTransient => isTransient;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    public void Delay_doubles_with_each_attempt(int attempt, double expectedSeconds) =>
        Policy().DelayFor(attempt).TotalSeconds.ShouldBe(expectedSeconds);

    [Theory]
    [InlineData(6)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void Delay_is_capped(int attempt) =>
        Policy().DelayFor(attempt).TotalSeconds.ShouldBe(30);

    [Fact]
    public void Jitter_keeps_delay_between_half_and_full()
    {
        Policy(jitter: 0.0).DelayFor(3).TotalSeconds.ShouldBe(2);  // half of 4 s
        Policy(jitter: 1.0).DelayFor(3).TotalSeconds.ShouldBe(4);  // full 4 s
    }
}
