using System.Reflection;
using Erp.Core.Contracts;
using NetArchTest.Rules;

namespace Erp.Architecture.Tests;

/// <summary>Regulile de structura din Bune practici (NET-01, NET-04, NET-06) si criteriul 02.01-A14.</summary>
public sealed class ModuleBoundaryTests
{
    // Assembly-urile ERP din folderul testelor: modulele si gazdele (Erp.Host compune modulele, deci le vede pe toate).
    private static readonly Assembly[] ErpAssemblies = Directory.EnumerateFiles(AppContext.BaseDirectory, "Erp.*.dll")
        .Select(path => AssemblyName.GetAssemblyName(path))
        .Where(name => !name.Name!.EndsWith(".Tests", StringComparison.Ordinal))
        .Select(Assembly.Load)
        .ToArray();

    private static IEnumerable<Assembly> ModuleAssemblies(string layer) =>
        ErpAssemblies.Where(a => a.GetName().Name is { } name && name.StartsWith("Erp.", StringComparison.Ordinal)
            && name.EndsWith("." + layer, StringComparison.Ordinal) && name.Split('.').Length == 3);

    private static string ModuleOf(Assembly assembly) => assembly.GetName().Name!.Split('.')[1];

    // Prefixul permisiunilor unui modul (NET-04): codul modulului, cu exceptiile din specificatiile validate (02.02: „Fin.”).
    private static string PermissionPrefix(string module) => module switch
    {
        "Finance" => "Fin",
        "Inventory" => "Inv",
        _ => module,
    };

    [Fact]
    public void Module_assemblies_are_loaded()
    {
        Assert.Contains(ErpAssemblies, a => a.GetName().Name == "Erp.Core.Application");
        Assert.Contains(ErpAssemblies, a => a.GetName().Name == "Erp.Core.Infrastructure");
        Assert.Contains(ErpAssemblies, a => a.GetName().Name == "Erp.Finance.Application");
        Assert.Contains(ErpAssemblies, a => a.GetName().Name == "Erp.Finance.Infrastructure");
        Assert.Contains(ErpAssemblies, a => a.GetName().Name == "Erp.Inventory.Application");
    }

    [Fact]
    [Trait("Criteriu", "02.01-A14")]
    public void No_assembly_outside_a_module_and_the_host_uses_another_modules_domain_or_infrastructure()
    {
        var failures = new List<string>();
        foreach (var assembly in ErpAssemblies.Where(a => a.GetName().Name != "Erp.Host"))
        {
            var owner = assembly.GetName().Name!.Split('.') is { Length: 3 } parts ? parts[1] : null;
            foreach (var module in ErpAssemblies.Select(a => a.GetName().Name!.Split('.')).Where(p => p.Length == 3).Select(p => p[1]).Distinct())
            {
                if (module == owner)
                {
                    continue;
                }

                var result = Types.InAssembly(assembly)
                    .ShouldNot().HaveDependencyOnAny($"Erp.{module}.Domain", $"Erp.{module}.Infrastructure", $"Erp.{module}.Application")
                    .GetResult();
                if (!result.IsSuccessful)
                {
                    failures.AddRange(result.FailingTypes.Select(t => $"{t.FullName} -> Erp.{module}"));
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Application_contains_no_data_access()
    {
        var failures = ModuleAssemblies("Application")
            .Select(a => Types.InAssembly(a).ShouldNot().HaveDependencyOnAny("Dapper", "Microsoft.Data.SqlClient", "System.Data.SqlClient").GetResult())
            .Where(r => !r.IsSuccessful)
            .SelectMany(r => r.FailingTypes.Select(t => t.FullName))
            .ToList();

        Assert.Empty(failures);
    }

    [Fact]
    public void Contracts_depend_on_no_other_layer()
    {
        var failures = ModuleAssemblies("Contracts")
            .Select(a => Types.InAssembly(a)
                .ShouldNot().HaveDependencyOnAny(ErpAssemblies.Select(x => x.GetName().Name!).Where(n => !n.EndsWith(".Contracts", StringComparison.Ordinal)).ToArray())
                .GetResult())
            .Where(r => !r.IsSuccessful)
            .SelectMany(r => r.FailingTypes.Select(t => t.FullName))
            .ToList();

        Assert.Empty(failures);
    }

    [Fact]
    public void Every_command_declares_its_module_and_permission_and_queries_declare_access()
    {
        var failures = new List<string>();
        foreach (var type in ModuleAssemblies("Application").SelectMany(a => a.GetTypes()).Where(t => !t.IsAbstract))
        {
            var isCommand = Implements(type, typeof(ICommand<>));
            var isQuery = Implements(type, typeof(IQuery<>));
            if (!isCommand && !isQuery)
            {
                continue;
            }

            var module = type.GetCustomAttribute<ModuleAttribute>();
            var permission = type.GetCustomAttribute<RequiresPermissionAttribute>();
            var anyUser = type.GetCustomAttribute<AuthenticatedUserAttribute>() is not null;
            if (module is null)
            {
                failures.Add($"{type.FullName}: lipsește [Module]");
            }
            else if (module.ModuleCode != ModuleOf(type.Assembly))
            {
                failures.Add($"{type.FullName}: [Module(\"{module.ModuleCode}\")] diferă de modulul assembly-ului");
            }

            if (isCommand && (permission is null || anyUser))
            {
                failures.Add($"{type.FullName}: comanda cere [RequiresPermission] (NET-04)");
            }

            if (isQuery && permission is null && !anyUser)
            {
                failures.Add($"{type.FullName}: interogarea cere [RequiresPermission] sau [AuthenticatedUser]");
            }

            if (permission is not null && !permission.Permission.StartsWith(PermissionPrefix(ModuleOf(type.Assembly)) + ".", StringComparison.Ordinal))
            {
                failures.Add($"{type.FullName}: permisiunea {permission.Permission} nu aparține modulului");
            }
        }

        Assert.Empty(failures);
    }

    private static bool Implements(Type type, Type openGeneric) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGeneric);
}
