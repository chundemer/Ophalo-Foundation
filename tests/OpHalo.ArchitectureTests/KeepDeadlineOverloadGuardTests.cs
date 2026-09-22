using System.Reflection;
using System.Reflection.Emit;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Xunit;

namespace OpHalo.ArchitectureTests;

/// <summary>
/// GAP-100 / ADR-505: production writers must stamp response deadlines through the explicit-deadline
/// or delegate overloads (business-clock aware). The four duration-based overloads on
/// <see cref="KeepRequest"/> stay only as test-seed/compatibility overloads; no production
/// assembly outside Core may call them.
///
/// The check reads method IL (call/callvirt/newobj targets), so it also covers async state
/// machines, lambdas, and local functions, which compile into nested types. Known limit: it cannot
/// see calls made through reflection, delegates built from reflection, or expression trees.
/// </summary>
public class KeepDeadlineOverloadGuardTests
{
    static readonly string[] GuardedNames =
    [
        nameof(KeepRequest.AddCustomerMessage),
        nameof(KeepRequest.SubmitFeedback),
        nameof(KeepRequest.LogInboundExternalContact),
        nameof(KeepRequest.CreateFromCustomerIntake),
    ];

    // Core defines the overloads (they delegate to the deadline overloads), so it is excluded.
    static readonly Assembly[] GuardedAssemblies =
    [
        typeof(OpHalo.Keep.Application.AssemblyMarker).Assembly,
        typeof(OpHalo.Keep.Infrastructure.AssemblyMarker).Assembly,
        typeof(OpHalo.Api.AssemblyMarker).Assembly,
        typeof(OpHalo.Worker.AssemblyMarker).Assembly,
    ];

    [Fact]
    public void Exactly_the_four_known_duration_overloads_exist()
    {
        // Fails loudly when an overload is removed or a new duration overload is added, so the
        // guard is updated deliberately rather than silently going stale.
        var legacy = FindLegacyOverloads();

        Assert.Equal(4, legacy.Count);
        Assert.Equal(
            GuardedNames.OrderBy(n => n),
            legacy.Select(m => m.Name).OrderBy(n => n));
    }

    [Fact]
    public void Scanner_detects_calls_to_every_legacy_overload_positive_control()
    {
        var legacy = FindLegacyOverloads();

        var hits = FindCallsIn([typeof(LegacyOverloadCallerFixture)], legacy);

        Assert.Equal(
            legacy.Select(m => m.Name).OrderBy(n => n),
            hits.Select(h => h.Target.Name).OrderBy(n => n));
    }

    [Fact]
    public void Production_code_outside_Core_never_calls_duration_based_deadline_overloads()
    {
        var legacy = FindLegacyOverloads();

        var hits = FindCallsIn(GuardedAssemblies.SelectMany(a => a.GetTypes()), legacy);

        Assert.True(
            hits.Count == 0,
            "Production writers must use the explicit/delegate deadline overloads (ADR-505). Calls found: "
            + string.Join("; ", hits.Select(h => $"{h.Caller.DeclaringType?.FullName}.{h.Caller.Name} -> {h.Target.Name}")));
    }

    static List<MethodInfo> FindLegacyOverloads() =>
        typeof(KeepRequest)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => GuardedNames.Contains(m.Name))
            .Where(m => m.GetParameters().Any(p => p.Name!.EndsWith("ResponseTargetMinutes", StringComparison.Ordinal)))
            .ToList();

    static List<(MethodBase Caller, MethodInfo Target)> FindCallsIn(
        IEnumerable<Type> types, IReadOnlyCollection<MethodInfo> targets)
    {
        var hits = new List<(MethodBase, MethodInfo)>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var type in types)
        {
            IEnumerable<MethodBase> methods = type.GetMethods(all).Cast<MethodBase>()
                .Concat(type.GetConstructors(all));

            foreach (var method in methods)
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il is null) continue;

                foreach (var token in CallTokens(il))
                {
                    MethodBase? callee;
                    try
                    {
                        callee = method.Module.ResolveMethod(
                            token,
                            type.IsGenericType ? type.GetGenericArguments() : null,
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    if (callee is MethodInfo mi && targets.Any(t => t.MetadataToken == mi.MetadataToken && t.Module == mi.Module))
                        hits.Add((method, mi));
                }
            }
        }

        return hits;
    }

    static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    // Walks real instruction boundaries (never raw bytes) and yields call/callvirt/newobj tokens.
    static IEnumerable<int> CallTokens(byte[] il)
    {
        var i = 0;
        while (i < il.Length)
        {
            short value = il[i++];
            if (value == 0xFE) value = (short)(0xFE00 | il[i++]);

            var op = OpCodesByValue[value];
            var isCall = op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj;

            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    i += 1;
                    break;
                case OperandType.InlineVar:
                    i += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    i += 8;
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, i);
                    i += 4 + 4 * count;
                    break;
                default: // InlineMethod, InlineField, InlineString, InlineTok, InlineType, InlineSig, InlineI, ShortInlineR, InlineBrTarget
                    if (isCall)
                        yield return BitConverter.ToInt32(il, i);
                    i += 4;
                    break;
            }
        }
    }

    // Positive control: compiles calls to each legacy overload. Never executed, only scanned.
    static class LegacyOverloadCallerFixture
    {
        public static void Calls(KeepRequest request)
        {
            _ = request.AddCustomerMessage(MessageIntent.GeneralMessage, "m", 60, 240, 60, DateTime.UtcNow);
            _ = request.SubmitFeedback(false, null, 60, DateTime.UtcNow);
            _ = request.LogInboundExternalContact(
                CommunicationChannel.Phone, true, "s", Guid.NewGuid(), "n", 240, DateTime.UtcNow);
            _ = KeepRequest.CreateFromCustomerIntake(
                Guid.NewGuid(), Guid.NewGuid(), "n", "p", null, "d", "r", "t", DateTime.UtcNow, 60);
        }
    }
}
