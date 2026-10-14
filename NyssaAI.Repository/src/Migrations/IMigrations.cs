
namespace NyssaAI.Repository.Migrations;

internal interface IMigrations
{
    public long Resolve(string release);
}
