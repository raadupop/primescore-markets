using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Structural;

/// <summary>
/// The seven dependency rules of brief §5, one test per rule. They define the architecture:
/// a red test here is an architectural violation, not a flaky test.
/// </summary>
public sealed class ArchitectureRules
{
    private const string HostAssembly = "PrimeScore.Engine.Host";

    private static readonly string[] LedgerTables = ["ledger", "obs_log", "ledger_verifications"];
    private const string CompositionNamespace = "PrimeScore.Engine.Host.Composition";

    [Fact]
    public void Rule1_module_implementations_reference_only_contracts_of_other_modules()
    {
        var violations = new List<string>();
        foreach (var module in EngineSolution.ImplementedModules)
        {
            var project = EngineSolution.Projects[EngineSolution.Implementation(module)];
            violations.AddRange(project.ReferencedProjects
                .Where(reference => EngineSolution.IsModuleImplementation(reference))
                .Select(reference => $"{project.Name} project-references {reference}"));

            var otherImplementationTypes = EngineSolution.ImplementedModules
                .Where(other => other != module)
                .SelectMany(other => EngineSolution.Load(EngineSolution.Implementation(other)).GetTypes())
                .Select(type => type.FullName!)

                // Compiler-synthesized types (e.g. <>z__ReadOnlySingleElementList`1 for a `[x]` collection
                // expression) are emitted into every assembly under the same name; they are not module code.
                .Where(name => !name.StartsWith('<'))
                .ToArray();
            violations.AddRange(Failing(
                Types.InAssembly(EngineSolution.Load(project.Name)).Should().NotHaveDependencyOnAny(otherImplementationTypes),
                $"{project.Name} depends on another module's implementation type"));
        }

        Assert.True(violations.Count == 0, "Violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Rule2_module_contracts_reference_only_the_shared_kernel()
    {
        var violations = new List<string>();
        foreach (var module in EngineSolution.AllModules)
        {
            var project = EngineSolution.Projects[EngineSolution.Contracts(module)];
            violations.AddRange(project.ReferencedProjects
                .Where(reference => reference != "PrimeScore.SharedKernel")
                .Select(reference => $"{project.Name} project-references {reference}"));

            var forbidden = new[] { "PrimeScore.Ledger", "PrimeScore.Api", "PrimeScore.Engine" }
                .Concat(EngineSolution.AllModules.Where(other => other != module).Select(other => EngineSolution.ModulesPrefix + other + "."))
                .Concat(EngineSolution.AllModules.Where(other => other != module).Select(EngineSolution.Implementation))
                .Append(EngineSolution.Implementation(module) + ".")
                .ToArray();
            var ownNamespace = EngineSolution.Contracts(module);
            var types = Types.InAssembly(EngineSolution.Load(project.Name)).GetTypes()
                .Where(type => type.FullName is not null)
                .ToArray();
            violations.AddRange(Failing(
                Types.InAssembly(EngineSolution.Load(project.Name)).Should().NotHaveDependencyOnAny(forbidden.Where(prefix => !ownNamespace.StartsWith(prefix, StringComparison.Ordinal)).ToArray()),
                $"{project.Name} depends outside the shared kernel"));
            Assert.All(types, type => Assert.StartsWith(ownNamespace, type.Namespace ?? "", StringComparison.Ordinal));
        }

        Assert.True(violations.Count == 0, "Violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Rule3_ledger_and_shared_kernel_reference_no_module()
    {
        var violations = new List<string>();
        foreach (var name in new[] { "PrimeScore.Ledger", "PrimeScore.SharedKernel" })
        {
            violations.AddRange(EngineSolution.Projects[name].ReferencedProjects
                .Where(reference => reference.StartsWith(EngineSolution.ModulesPrefix, StringComparison.Ordinal) || reference.StartsWith("PrimeScore.Api", StringComparison.Ordinal))
                .Select(reference => $"{name} project-references {reference}"));
            violations.AddRange(Failing(
                Types.InAssembly(EngineSolution.Load(name)).Should().NotHaveDependencyOnAny("PrimeScore.Modules", "PrimeScore.Api", "PrimeScore.Engine"),
                $"{name} depends on a module, the API contract or the host"));
        }

        Assert.True(violations.Count == 0, "Violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Rule4_host_uses_module_implementations_only_through_registration_and_api_dtos_stay_out_of_modules()
    {
        var violations = new List<string>();
        var registrationTypes = new List<string>();
        foreach (var module in EngineSolution.ImplementedModules)
        {
            var assembly = EngineSolution.Load(EngineSolution.Implementation(module));
            var expected = $"{EngineSolution.Implementation(module)}.{module}Module";
            registrationTypes.Add(expected);

            var exported = assembly.GetExportedTypes().Select(type => type.FullName).ToArray();
            if (exported is not [var only] || only != expected)
            {
                violations.Add($"{assembly.GetName().Name} exports [{string.Join(", ", exported)}]; only {expected} may be public");
            }

            var friends = assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => attribute.AssemblyName);
            violations.AddRange(friends
                .Where(friend => friend != $"{EngineSolution.Implementation(module)}.Tests")
                .Select(friend => $"{assembly.GetName().Name} exposes internals to {friend}"));
        }

        var hostTypesUsingModules = Types.InAssembly(EngineSolution.Load(HostAssembly))
            .That().HaveDependencyOnAny(registrationTypes.ToArray())
            .And().DoNotResideInNamespace(CompositionNamespace)
            .GetTypes()
            .Select(type => $"{type.FullName} uses a module registration outside the composition root");
        violations.AddRange(hostTypesUsingModules);

        foreach (var module in EngineSolution.AllModules)
        {
            foreach (var name in new[] { EngineSolution.Implementation(module), EngineSolution.Contracts(module) }.Where(EngineSolution.Projects.ContainsKey))
            {
                violations.AddRange(EngineSolution.Projects[name].ReferencedProjects
                    .Where(reference => reference == "PrimeScore.Api.Contracts")
                    .Select(_ => $"{name} project-references the API contract"));
                violations.AddRange(Failing(
                    Types.InAssembly(EngineSolution.Load(name)).Should().NotHaveDependencyOn("PrimeScore.Api"),
                    $"{name} uses an API DTO"));
            }
        }

        Assert.True(violations.Count == 0, "Violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Rule5_command_handlers_return_acknowledgements_and_query_handlers_do_not_write()
    {
        var violations = new List<string>();
        foreach (var assembly in EngineSolution.ImplementedModules.Select(module => EngineSolution.Load(EngineSolution.Implementation(module))))
        {
            foreach (var handler in assembly.GetTypes())
            {
                var commandInterfaces = GenericInterfaces(handler, typeof(ICommandHandler<,>)).ToArray();
                var queryInterfaces = GenericInterfaces(handler, typeof(IQueryHandler<,>)).ToArray();
                if (commandInterfaces.Length > 0 && queryInterfaces.Length > 0)
                {
                    violations.Add($"{handler.FullName} is both a command and a query handler");
                }

                foreach (var command in commandInterfaces)
                {
                    var ack = command.GetGenericArguments()[1];
                    violations.AddRange(AcknowledgementViolations(ack, handler.FullName!, depth: 0));
                }

                if (queryInterfaces.Length > 0)
                {
                    violations.AddRange(ModuleIl.WriteCalls(assembly, handler)
                        .Select(call => $"query handler {handler.FullName} writes via {call}"));
                }
            }
        }

        Assert.True(violations.Count == 0, "Violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Rule6_no_module_reads_the_ledger_table_directly()
    {
        var violations = new List<string>();
        foreach (var module in EngineSolution.ImplementedModules)
        {
            var assembly = EngineSolution.Load(EngineSolution.Implementation(module));
            violations.AddRange(Failing(
                Types.InAssembly(assembly).Should().NotHaveDependencyOnAny(
                    "Microsoft.Data.Sqlite",
                    "PrimeScore.Ledger.ILedgerAuditQuery",
                    "PrimeScore.Ledger.ILedgerStatusQuery",
                    "PrimeScore.Ledger.IPipelineLogQuery"),
                $"{assembly.GetName().Name} reads ledger storage outside projections"));
            violations.AddRange(ModuleIl.LedgerAccess(assembly).Select(finding => $"{assembly.GetName().Name}: {finding}"));

            foreach (var contextType in assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(DbContext)) && !type.IsAbstract))
            {
                using var context = CreateContext(contextType);
                foreach (var entity in context.Model.GetEntityTypes())
                {
                    var mapped = new[] { entity.GetTableName(), entity.GetViewName(), entity.GetFunctionName(), entity.GetSqlQuery() }
                        .Where(name => name is not null)
                        .ToArray();
                    violations.AddRange(mapped
                        .Where(name => LedgerTables.Any(table => name!.Contains(table, StringComparison.OrdinalIgnoreCase)))
                        .Select(name => $"{contextType.FullName} maps {entity.DisplayName()} to '{name}'"));
                }
            }
        }

        Assert.True(violations.Count == 0, "Violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Rule7_milestone_B_modules_have_contracts_but_no_implementation()
    {
        foreach (var module in EngineSolution.MilestoneBModules)
        {
            var directory = Path.Combine(EngineSolution.Root, "src", "Modules", module);
            var projects = Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
                .Select(path => Path.GetFileNameWithoutExtension(path)!)
                .ToArray();

            Assert.Equal([EngineSolution.Contracts(module)], projects);
            Assert.DoesNotContain(EngineSolution.Implementation(module), EngineSolution.Projects.Keys);
            Assert.NotEmpty(EngineSolution.Load(EngineSolution.Contracts(module)).GetExportedTypes());
        }
    }

    private static IEnumerable<Type> GenericInterfaces(Type type, Type definition) =>
        type.GetInterfaces().Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == definition);

    private static IEnumerable<string> AcknowledgementViolations(Type ack, string handler, int depth)
    {
        if (!typeof(ICommandAck).IsAssignableFrom(ack))
        {
            yield return $"{handler} returns {ack.FullName}, which is not an ICommandAck";
            yield break;
        }

        var members = ack.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => (property.Name, property.PropertyType))
            .Concat(ack.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(field => (field.Name, field.FieldType)));
        foreach (var (name, memberType) in members)
        {
            var type = Nullable.GetUnderlyingType(memberType) ?? memberType;
            var element = ElementType(type);
            if (IsIdentifierOrStatus(element))
            {
                continue;
            }

            if (typeof(ICommandAck).IsAssignableFrom(element) && depth < 3)
            {
                foreach (var nested in AcknowledgementViolations(element, handler, depth + 1))
                {
                    yield return nested;
                }

                continue;
            }

            yield return $"{handler} acknowledgement {ack.Name}.{name} is {type.Name}, which is domain state";
        }
    }

    private static Type ElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType()!;
        }

        return type != typeof(string) && type.IsGenericType && type.GetGenericArguments() is [var element]
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
            ? element
            : type;
    }

    /// <summary>
    /// Identifiers, counts, flags, status enums, timestamps and message text. Floating-point and
    /// decimal members are excluded: a computed score in an acknowledgement is domain state.
    /// </summary>
    private static bool IsIdentifierOrStatus(Type type) =>
        type.IsEnum
        || type == typeof(bool) || type == typeof(int) || type == typeof(long)
        || type == typeof(string) || type == typeof(Guid)
        || type == typeof(DateTimeOffset)
        || type == typeof(SharedKernel.CorrelationId) || type == typeof(SharedKernel.ConfigVersion);

    private static DbContext CreateContext(Type contextType)
    {
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        builder.UseSqlite("Data Source=:memory:");
        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }

    private static IEnumerable<string> Failing(ConditionList condition, string description)
    {
        var result = condition.GetResult();
        return result.IsSuccessful
            ? []
            : (result.FailingTypeNames ?? []).Select(type => $"{description}: {type}");
    }
}
