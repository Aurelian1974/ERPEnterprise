namespace Shared.Kernel.Abstractions;

public interface IDbConnectionFactory
{
    System.Data.IDbConnection Create();
}
