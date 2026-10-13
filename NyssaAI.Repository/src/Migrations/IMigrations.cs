using NyssaAI.Results;

namespace NyssaAI.Repository.Migrations;

internal interface IMigrations
{
    public Result<long> Resolve(string release);
}
