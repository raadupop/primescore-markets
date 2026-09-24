using System.Reflection;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PrimeScore.Structural;

/// <summary>
/// Scans compiled method bodies, including async state machines and lambdas (nested types)
/// and every method they reach inside the same assembly.
/// </summary>
internal static partial class ModuleIl
{
    private static readonly string[] EntityWrites =
    [
        "SaveChanges", "SaveChangesAsync", "Add", "AddAsync", "AddRange", "AddRangeAsync", "Update", "UpdateRange",
        "Remove", "RemoveRange", "Attach", "AttachRange",
    ];

    private static readonly (string DeclaringType, string[] Methods)[] WriteMethods =
    [
        ("Microsoft.EntityFrameworkCore.DbContext", EntityWrites),
        ("Microsoft.EntityFrameworkCore.DbSet`1", EntityWrites),
        ("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions", ["ExecuteDelete", "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteUpdateAsync"]),
        ("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", ["ExecuteDelete", "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteUpdateAsync"]),
        ("Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions", ["ExecuteSql", "ExecuteSqlAsync", "ExecuteSqlRaw", "ExecuteSqlRawAsync", "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync"]),
        ("System.Data.Common.DbCommand", ["ExecuteNonQuery", "ExecuteNonQueryAsync"]),
        ("PrimeScore.Ledger.ILedger", ["AppendAsync"]),
        ("PrimeScore.SharedKernel.Messaging.IIntegrationEventPublisher", ["PublishAsync"]),
        ("PrimeScore.SharedKernel.Cqrs.ICommandHandler`2", ["HandleAsync"]),
    ];

    /// <summary>Entry points that bypass a module's own EF model and could read any table.</summary>
    private static readonly (string DeclaringType, string[] Methods)[] RawSqlMethods =
    [
        ("Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions", ["SqlQuery", "SqlQueryRaw", "GetDbConnection", "ExecuteSql", "ExecuteSqlAsync", "ExecuteSqlRaw", "ExecuteSqlRawAsync", "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync"]),
        ("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions", ["FromSql", "FromSqlRaw", "FromSqlInterpolated"]),
        ("PrimeScore.Ledger.EngineDatabase", ["CreateConnection"]),
    ];

    /// <summary>Calls that write state, reachable from <paramref name="handler"/> within its assembly.</summary>
    public static IEnumerable<string> WriteCalls(Assembly assembly, Type handler)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        var definition = module.GetType(handler.FullName!.Replace('+', '/'));
        return definition is null
            ? []
            : ReachableCalls(module, [.. WithNestedAndBases(definition, module)])
                .Where(IsWrite)
                .Select(Describe)
                .Distinct()
                .ToArray();
    }

    /// <summary>Raw SQL entry points, raw ADO.NET use, and SQL text naming a ledger table, anywhere in the assembly.</summary>
    public static IEnumerable<string> LedgerAccess(Assembly assembly)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        var findings = new List<string>();
        foreach (var method in module.Types.SelectMany(WithNested).SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj && instruction.Operand is MethodReference target
                    && (Matches(target, RawSqlMethods) || target.DeclaringType.Namespace == "System.Data.Common"))
                {
                    findings.Add($"{method.FullName} calls {Describe(target)}");
                }

                if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string text && LedgerSql().IsMatch(text))
                {
                    findings.Add($"{method.FullName} contains SQL naming a ledger table: \"{text}\"");
                }
            }
        }

        return findings;
    }

    private static IEnumerable<MethodReference> ReachableCalls(ModuleDefinition module, IEnumerable<TypeDefinition> roots)
    {
        var pending = new Stack<MethodDefinition>(roots.SelectMany(type => type.Methods).Where(method => method.HasBody));
        var visited = new HashSet<MethodDefinition>();
        while (pending.Count > 0)
        {
            var method = pending.Pop();
            if (!visited.Add(method))
            {
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj) || instruction.Operand is not MethodReference target)
                {
                    continue;
                }

                yield return target;
                if (target.Module == module && target.Resolve() is { HasBody: true } local)
                {
                    pending.Push(local);
                    foreach (var nested in WithNested(local.DeclaringType).SelectMany(type => type.Methods).Where(candidate => candidate.HasBody))
                    {
                        pending.Push(nested);
                    }
                }
            }
        }
    }

    private static bool IsWrite(MethodReference target) =>
        Matches(target, WriteMethods) || IsDerivedContextWrite(target);

    private static bool Matches(MethodReference target, (string DeclaringType, string[] Methods)[] table)
    {
        var declaring = target.DeclaringType.GetElementType().FullName;
        return table.Any(entry =>
            string.Equals(entry.DeclaringType, declaring, StringComparison.Ordinal)
            && entry.Methods.Contains(target.Name, StringComparer.Ordinal));
    }

    private static bool IsDerivedContextWrite(MethodReference target)
    {
        if (!EntityWrites.Contains(target.Name, StringComparer.Ordinal))
        {
            return false;
        }

        for (var type = SafeResolve(target.DeclaringType); type is not null; type = SafeResolve(type.BaseType))
        {
            if (type.FullName == "Microsoft.EntityFrameworkCore.DbContext")
            {
                return true;
            }
        }

        return false;
    }

    private static TypeDefinition? SafeResolve(TypeReference? type)
    {
        try
        {
            return type?.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    private static string Describe(MethodReference target) => $"{target.DeclaringType.GetElementType().FullName}::{target.Name}";

    private static IEnumerable<TypeDefinition> WithNested(TypeDefinition type) =>
        new[] { type }.Concat(type.NestedTypes.SelectMany(WithNested));

    private static IEnumerable<TypeDefinition> WithNestedAndBases(TypeDefinition type, ModuleDefinition module)
    {
        for (var current = type; current is not null; current = current.BaseType?.Module == module ? SafeResolve(current.BaseType) : null)
        {
            foreach (var nested in WithNested(current))
            {
                yield return nested;
            }
        }
    }

    [GeneratedRegex(@"\b(from|join|into|update|table)\s+[""`\[]?(ledger|obs_log|ledger_verifications)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LedgerSql();
}
