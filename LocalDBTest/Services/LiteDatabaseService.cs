using LiteDB;
using System.IO;

namespace LocalDBTest.Services;

public interface IDatabaseService
{
    LiteDatabase GetDatabase();
}

public class LiteDatabaseService : IDatabaseService
{
    private readonly string _dbPath;
    private LiteDatabase _database;

    public LiteDatabaseService()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _dbPath = Path.Combine(path, "app.db");
    }

    public LiteDatabase GetDatabase()
    {
        if (_database == null)
        {
            _database = new LiteDatabase(_dbPath);
        }
        return _database;
    }
}