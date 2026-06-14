using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace OpHalo.ArchitectureTests;

/// <summary>
/// Enforces the locked dependency boundaries from the OpHalo Foundation build plan (§8).
/// These run before product behavior is moved, so the rules can guard every later phase.
/// </summary>
public class ArchitectureRulesTests
{
    static readonly Assembly SharedKernel = typeof(OpHalo.SharedKernel.AssemblyMarker).Assembly;
    static readonly Assembly FoundationCore = typeof(OpHalo.Foundation.Core.AssemblyMarker).Assembly;
    static readonly Assembly FoundationApp = typeof(OpHalo.Foundation.Application.AssemblyMarker).Assembly;
    static readonly Assembly FoundationInfra = typeof(OpHalo.Foundation.Infrastructure.AssemblyMarker).Assembly;
    static readonly Assembly KeepCore = typeof(OpHalo.Keep.Core.AssemblyMarker).Assembly;
    static readonly Assembly KeepApp = typeof(OpHalo.Keep.Application.AssemblyMarker).Assembly;
    static readonly Assembly KeepInfra = typeof(OpHalo.Keep.Infrastructure.AssemblyMarker).Assembly;
    static readonly Assembly Api = typeof(OpHalo.Api.AssemblyMarker).Assembly;
    static readonly Assembly Worker = typeof(OpHalo.Worker.AssemblyMarker).Assembly;

    static Assembly[] AllProductionAssemblies =>
    [
        SharedKernel,
        FoundationCore, FoundationApp, FoundationInfra,
        KeepCore, KeepApp, KeepInfra,
        Api, Worker
    ];

    static void AssertPasses(TestResult result, string rule)
    {
        var failing = result.FailingTypeNames is null ? "none" : string.Join(", ", result.FailingTypeNames);
        Assert.True(result.IsSuccessful, $"Architecture rule violated: {rule}. Offending types: {failing}");
    }

    [Fact]
    public void Foundation_must_not_reference_Keep()
    {
        var result = Types.InAssemblies([FoundationCore, FoundationApp, FoundationInfra])
            .ShouldNot().HaveDependencyOn("OpHalo.Keep")
            .GetResult();

        AssertPasses(result, "Foundation must not reference Keep");
    }

    [Fact]
    public void Core_must_not_depend_on_Application_or_Infrastructure()
    {
        var result = Types.InAssemblies([FoundationCore, KeepCore])
            .ShouldNot().HaveDependencyOnAny(
                "OpHalo.Foundation.Application", "OpHalo.Foundation.Infrastructure",
                "OpHalo.Keep.Application", "OpHalo.Keep.Infrastructure")
            .GetResult();

        AssertPasses(result, "Core/domain projects must not depend on Application or Infrastructure");
    }

    [Fact]
    public void Application_must_not_depend_on_Infrastructure()
    {
        var result = Types.InAssemblies([FoundationApp, KeepApp])
            .ShouldNot().HaveDependencyOnAny(
                "OpHalo.Foundation.Infrastructure", "OpHalo.Keep.Infrastructure")
            .GetResult();

        AssertPasses(result, "Application must not depend on Infrastructure");
    }

    [Fact]
    public void SharedKernel_must_not_contain_business_or_framework_concepts()
    {
        var result = Types.InAssembly(SharedKernel)
            .ShouldNot().HaveDependencyOnAny(
                "OpHalo.Foundation", "OpHalo.Keep",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        AssertPasses(result, "SharedKernel must stay free of business/framework concepts");
    }

    [Theory]
    [InlineData("CurrentUser")]   // identity is a Foundation concern (build plan §3.3)
    [InlineData("EmailSender")]   // email sending is a Foundation concern
    [InlineData("DbContext")]     // persistence is Infrastructure
    [InlineData("Account")]       // account logic is a Foundation concern
    [InlineData("Notification")]  // notifications are a Foundation concern
    [InlineData("Entitlement")]   // entitlements are a Foundation concern
    [InlineData("Keep")]          // no product concepts in SharedKernel
    public void SharedKernel_must_not_contain_business_typed_names(string forbiddenFragment)
    {
        var result = Types.InAssembly(SharedKernel)
            .ShouldNot().HaveNameMatching($".*{forbiddenFragment}.*")
            .GetResult();

        AssertPasses(result, $"SharedKernel must not contain a type named like '{forbiddenFragment}'");
    }

    [Theory]
    [InlineData("OpHalo.Signal")]
    [InlineData("OpHalo.Continuity")]
    [InlineData("OpHalo.Platform")]
    public void No_active_project_may_reference_legacy_families(string legacyNamespace)
    {
        var result = Types.InAssemblies(AllProductionAssemblies)
            .ShouldNot().HaveDependencyOn(legacyNamespace)
            .GetResult();

        AssertPasses(result, $"No active project may reference {legacyNamespace}.*");
    }
}
