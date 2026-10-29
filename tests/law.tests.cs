#!/usr/bin/env dotnet
#:package xunit.v3@3.0.0
#:property OutputType=Exe
#:property Nullable=enable
#:property LangVersion=preview
#:include Cases.cs
#:include Harness.cs

using Xunit;

public class Decisions
{
    public static TheoryData<string> Names => Harness.CaseNames;

    [Theory]
    [MemberData(nameof(Names))]
    public void ReachesTheExpectedDecision(string name)
    {
        var (expected, actual) = Harness.Lookup(name);
        Assert.Equal(expected.Decision, actual.Decision);
        Assert.Equal(expected.Rule, actual.Rule);
    }
}

public class CarriedContext
{
    public static TheoryData<string> Names => Harness.CaseNames;

    [Theory]
    [MemberData(nameof(Names))]
    public void MatchesWhatTheCaseDeclares(string name)
    {
        var (expected, actual) = Harness.Lookup(name);
        Assert.Equal(expected.Context ?? "", actual.Context);
    }
}

public class RefusedDeletionLedger
{
    [Fact]
    public void RecordsEveryRemovalRefusedForLeavingTheLaunchFolder()
    {
        _ = Harness.Results;
        var ledger = File.ReadAllText(Harness.LedgerPath);
        Assert.StartsWith("# Refused deletions", ledger);
        Assert.Contains("rm /etc/__probe.ts", ledger);
        Assert.Contains("etc/__probe.ts`", ledger);
        Assert.Contains("(unknown: no CLAUDE_PROJECT_DIR)", ledger);
        Assert.DoesNotContain("rm $TMPDIR/junk.txt", ledger);
    }
}

public class Invariants
{
    [Fact]
    public void EveryEnforcingDecision_NamesTheRuleItEnforces()
    {
        var untagged = Harness.Results
            .Where(r => r.Actual.Decision is "deny" or "ask" && r.Actual.Rule.Length == 0)
            .Select(r => r.Case.Name)
            .ToList();
        Assert.Empty(untagged);
    }

    [Fact]
    public void EveryAllow_SaysNothing()
    {
        var chatty = Harness.Results
            .Where(r => r.Actual.Decision == "allow")
            .Where(r => r.Actual.Reason.Length > 0 || r.Actual.Context.Length > 0)
            .Select(r => r.Case.Name)
            .ToList();
        Assert.NotEmpty(Harness.Results.Where(r => r.Actual.Decision == "allow"));
        Assert.Empty(chatty);
    }

    [Fact]
    public void EveryEnforcingDenial_ExplainsItselfInMoreThanASentence()
    {
        var terse = Harness.Results
            .Where(r => r.Actual.Decision == "deny")
            .Where(r => r.Actual.Rule != "RULE-guard-fail-closed")
            .Where(r => r.Actual.Reason.Length < 200)
            .Select(r => r.Case.Name)
            .ToList();
        Assert.Empty(terse);
    }

    [Fact]
    public void FailClosedDenial_StaysShortEnoughToReadAtAGlance()
    {
        var failClosed = Harness.Results
            .Where(r => r.Actual.Rule == "RULE-guard-fail-closed")
            .ToList();
        Assert.NotEmpty(failClosed);
        Assert.All(failClosed, r => Assert.True(r.Actual.Reason.Length < 200));
    }

    [Fact]
    public void EveryRuleSlug_IsKebabCase()
    {
        var bad = Harness.Results
            .Select(r => r.Actual.Rule)
            .Where(s => s.Length > 0 && !Harness.KebabSlug().IsMatch(s))
            .ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void PassWithNoContext_WritesNothingToStdout()
    {
        var quiet = Harness.Results
            .Where(r => r.Actual.Decision == "pass" && r.Actual.Context.Length == 0)
            .Where(r => r.Actual.Raw.Length != 0)
            .Select(r => r.Case.Name)
            .ToList();
        Assert.Empty(quiet);
    }
}
