using MUTASSIM_SERVER.Core.Assist;
using MUTASSIM_SERVER.Core.Commands;
using Xunit;

namespace MUTASSIM_SERVER.Tests;

public class SolutionEngineTests
{
    private static readonly string?[] Modes =
    [
        null, "", "Unknown", "Adb", "AdbUnauthorized", "AdbOffline", "Recovery", "Sideload", "Fastboot", "FastbootD",
        "SamsungDownload", "Mtp", "QualcommEdl", "QualcommDiag", "MediaTekPreloader", "MediaTekBrom",
        "AppleNormal", "AppleRecovery", "AppleDfu", "UsbSerial", "SomethingNew"
    ];

    private static readonly (string? Brand, string? Chipset)[] Phones =
    [
        (null, null), ("samsung", "Exynos 9611"), ("Xiaomi", "Qualcomm SM8250"), ("TECNO", "MediaTek MT6789"),
        ("Redmi", "sdm660"), ("Google", "GoogleTensor")
    ];

    public static IEnumerable<object?[]> AllCombinations() =>
        from p in SolutionEngine.Problems
        from m in Modes
        from ph in Phones
        select new object?[] { p.Code, m, ph.Brand, ph.Chipset };

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Every_combination_returns_a_safe_complete_answer(string problem, string? mode, string? brand, string? chipset)
    {
        var device = new AssistDevice(mode, brand, brand, "X", chipset);
        var r = SolutionEngine.Diagnose(Guid.NewGuid(), problem, device);

        Assert.False(string.IsNullOrWhiteSpace(r.SummaryEn));
        Assert.False(string.IsNullOrWhiteSpace(r.SummaryAr));
        Assert.True(r.Steps.Count + r.Guidance.Count > 0, "answer must contain steps or guidance");
        Assert.All(r.Steps, s => Assert.True(AssistActions.IsAllowed(s.ActionId), s.ActionId));
        Assert.Equal(r.Steps.Count, r.Steps.Select(s => s.ActionId).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, r.Steps.Count), r.Steps.Select(s => s.Order));
        Assert.All(r.Steps, s => Assert.Equal(AssistActions.All[s.ActionId].Dangerous, s.Dangerous));
    }

    [Fact]
    public void Never_recommends_bypass_or_destructive_partition_actions()
    {
        Assert.DoesNotContain(AssistActions.All.Keys, k =>
            k.Contains("bypass") || k.Contains("imei") || k.Contains("erase") || k.Contains("flash") || k.Contains("unlock"));
    }

    [Fact]
    public void Adb_steps_only_when_phone_is_in_adb_capable_mode()
    {
        foreach (var p in SolutionEngine.Problems)
        foreach (var m in new[] { "Fastboot", "SamsungDownload", "QualcommEdl", "MediaTekBrom" })
        {
            var r = SolutionEngine.Diagnose(Guid.NewGuid(), p.Code, new AssistDevice(m, "samsung", null, null, "Qualcomm"));
            Assert.DoesNotContain(r.Steps, s => s.ActionId.StartsWith("adb.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Fastboot_steps_only_in_fastboot()
    {
        foreach (var p in SolutionEngine.Problems)
        foreach (var m in Modes.Where(m => m is not ("Fastboot" or "FastbootD")))
        {
            var r = SolutionEngine.Diagnose(Guid.NewGuid(), p.Code, new AssistDevice(m));
            Assert.DoesNotContain(r.Steps, s => s.ActionId.StartsWith("fb.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Bootloop_samsung_adb_reaches_download_mode()
    {
        var r = SolutionEngine.Diagnose(Guid.NewGuid(), "bootloop", new AssistDevice("Adb", "samsung"));
        Assert.Equal(["adb.info", "adb.reboot.recovery", "adb.reboot.download"], r.Steps.Select(s => s.ActionId));
    }

    [Fact]
    public void Edl_is_refused_for_non_qualcomm()
    {
        var r = SolutionEngine.Diagnose(Guid.NewGuid(), "edl", new AssistDevice("Adb", "TECNO", null, null, "MediaTek MT6789"));
        Assert.DoesNotContain(r.Steps, s => s.ActionId == "adb.reboot.edl");
        var q = SolutionEngine.Diagnose(Guid.NewGuid(), "edl", new AssistDevice("Adb", "Xiaomi", null, null, "Qualcomm SM8250"));
        Assert.Contains(q.Steps, s => s.ActionId == "adb.reboot.edl" && s.Dangerous);
    }

    [Fact]
    public void Frp_only_offers_official_recovery()
    {
        var r = SolutionEngine.Diagnose(Guid.NewGuid(), "account_locked", new AssistDevice("Adb"));
        Assert.Equal(["frp.info", "frp.google"], r.Steps.Select(s => s.ActionId));
    }

    [Fact]
    public void Unauthorized_phone_gets_connection_fix_first()
    {
        var r = SolutionEngine.Diagnose(Guid.NewGuid(), "bootloop", new AssistDevice("AdbUnauthorized"));
        Assert.Equal("unauthorized", r.ModeGroup);
        Assert.Equal(["device.info"], r.Steps.Select(s => s.ActionId));
        Assert.Contains(r.Guidance, g => g.En.Contains("Allow"));
    }
}

public class CommandQueueTests
{
    [Fact]
    public void Command_is_dispatched_once_even_under_contention()
    {
        var q = new CommandQueue();
        for (var i = 0; i < 200; i++) q.Add("D1", "PING");
        var got = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        Parallel.For(0, 1000, _ => { if (q.GetNext("D1") is { } c) got.Add(c.Id); });
        Assert.Equal(200, got.Count);
        Assert.Equal(200, got.Distinct().Count());
    }

    [Fact]
    public void Result_lifecycle_is_enforced()
    {
        var q = new CommandQueue();
        var c = q.Add("D1", "PING");
        Assert.Equal(CompleteOutcome.NotRunning, q.Complete(new CommandResult(c.Id, true, "x", DateTime.UtcNow)));
        q.GetNext("D1");
        Assert.Equal(CompleteOutcome.WrongDevice, q.Complete(new CommandResult(c.Id, true, "x", DateTime.UtcNow, "D2")));
        Assert.Equal(CompleteOutcome.Completed, q.Complete(new CommandResult(c.Id, true, "ok", DateTime.UtcNow, "D1")));
        Assert.Equal(CompleteOutcome.AlreadyCompleted, q.Complete(new CommandResult(c.Id, true, "again", DateTime.UtcNow)));
        Assert.Equal("ok", q.Get(c.Id)!.Result!.Message);
    }
}

public class AssistSessionStoreTests
{
    [Fact]
    public void Feedback_only_for_actions_in_the_session()
    {
        var store = new AssistSessionStore();
        var r = SolutionEngine.Diagnose(Guid.NewGuid(), "identify", new AssistDevice("Adb"));
        store.Add(new AssistRequest("T1", "identify", new AssistDevice("Adb")), r);
        Assert.Equal(AssistSessionStore.FeedbackOutcome.Recorded, store.AddFeedback(new AssistFeedback(r.SessionId, "adb.info", true, "ok")));
        Assert.Equal(AssistSessionStore.FeedbackOutcome.ActionNotInSession, store.AddFeedback(new AssistFeedback(r.SessionId, "adb.reboot.edl", true, null)));
        Assert.Equal(AssistSessionStore.FeedbackOutcome.SessionNotFound, store.AddFeedback(new AssistFeedback(Guid.NewGuid(), "adb.info", true, null)));
        Assert.Single(store.Get(r.SessionId)!.Feedback);
    }
}
